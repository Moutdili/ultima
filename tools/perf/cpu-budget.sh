#!/usr/bin/env bash
# cpu-budget.sh — coût CPU et réveils des processus ULTIMA, de gnome-shell (qui exécute
# l'extension) et, pour contexte, de Xwayland et du jeu. LECTURE SEULE de /proc et /sys :
# ne lance rien, n'ouvre aucune fenêtre, n'attache rien (pas de ptrace, pas de perf).
#
# Méthode : deux relevés de /proc/<pid>/task/<tid>/{schedstat,status,comm} séparés de --duration s
# (relevés faits en Python d'un seul coup, ~ms ; Δt = temps mur entre les deux relevés).
#   CPU %      = Δ temps d'exécution (schedstat, ns) / Δt   (100 % = un cœur)
#   réveils/s  = Δ voluntary_ctxt_switches / Δt  (chaque mise en sommeil volontaire est suivie
#                d'un réveil : approximation standard ; un timer GLib à 12,5 Hz ≈ 12,5/s)
#   préempt./s = Δ nonvoluntary_ctxt_switches / Δt
#   Fils créés pendant la mesure : comptés depuis 0 ; fils disparus : signalés (non comptés).
# --gpu : moyenne de /sys/class/drm/card*/device/gpu_busy_percent échantillonné à 10 Hz
#         (charge GPU globale, tous clients confondus : jeu + compositeur).
# L'en-tête note l'alimentation (secteur/batterie) et le profil d'énergie : deux mesures ne se
# comparent QUE dans les mêmes conditions.
#
# Usage : cpu-budget.sh [--duration S] [--label TXT] [--tsv FICHIER] [--gpu] [--top N]
#                       [--proc NOM=REGEX_cmdline | NOM=comm:NOM_EXACT]...  (remplace la liste)
# Exemple A/B (fenêtre de test ; le chef bascule l'état entre deux mesures) :
#   cpu-budget.sh --duration 60 --gpu --label rail-visible  --tsv ~/sts2-proofs/perf/cpu.tsv
#   cpu-budget.sh --duration 60 --gpu --label ultima-masque --tsv ~/sts2-proofs/perf/cpu.tsv
set -euo pipefail

DUR=30; LABEL=repos; TSV=''; GPU=0; TOP=3; PROCS=()
while [[ $# -gt 0 ]]; do
  case $1 in
    --duration) DUR=$2; shift 2 ;;
    --label) LABEL=$2; shift 2 ;;
    --tsv) TSV=$2; shift 2 ;;
    --gpu) GPU=1; shift ;;
    --top) TOP=$2; shift 2 ;;
    --proc) PROCS+=("$2"); shift 2 ;;
    -h|--help) sed -n '2,24p' "$0"; exit 0 ;;
    *) echo "option inconnue : $1" >&2; exit 2 ;;
  esac
done
[[ $DUR =~ ^[0-9]+([.][0-9]+)?$ ]] || { echo "--duration : nombre de secondes" >&2; exit 2; }
if [[ ${#PROCS[@]} -eq 0 ]]; then
  PROCS=(
    'gnome-shell=comm:gnome-shell'
    'Xwayland=comm:Xwayland'
    'ultima-daemon=Ultima\.Daemon|ultima-daemon'
    'ultima-mirror=ultima-mirror'
    'ultima-x11=ultima-x11\.dll'
    'jeu=comm:SlayTheSpire2'
  )
fi

echo "# cpu-budget — libellé « $LABEL » — $(date '+%F %T') — durée demandée ${DUR} s"
ac=$(cat /sys/class/power_supply/A*/online 2>/dev/null | head -1 || true)
case $ac in 1) ac=secteur ;; 0) ac='BATTERIE (mesures non comparables à une mesure sur secteur)' ;; *) ac='?' ;; esac
echo "# noyau $(uname -r) · $(gnome-shell --version 2>/dev/null || echo 'gnome-shell ?') · alimentation : $ac · platform_profile : $(cat /sys/firmware/acpi/platform_profile 2>/dev/null || echo '?') · tuned : $(tuned-adm active 2>/dev/null | sed 's/^Current active profile: //' || echo '?')"

exec python3 - "$DUR" "$LABEL" "$TOP" "$GPU" "$TSV" "$$" "${PROCS[@]}" <<'EOF'
import datetime, glob, os, re, sys, threading, time

dur, label, top, gpu_on, tsv, self_pid = sys.argv[1:7]
dur, top, gpu_on, self_pid = float(dur), int(top), gpu_on == "1", int(self_pid)
uid = os.getuid()
specs = []
for s in sys.argv[7:]:
    name, _, pat = s.partition("=")
    specs.append((name, ("comm", pat[5:]) if pat.startswith("comm:") else ("re", re.compile(pat))))

def read(p):
    with open(p, "rb") as f:
        return f.read().decode(errors="replace")

def find_pids():
    found = []
    me = {os.getpid(), self_pid}
    for d in os.listdir("/proc"):
        if not d.isdigit() or int(d) in me:
            continue
        try:
            if os.stat(f"/proc/{d}").st_uid != uid:
                continue
            comm = read(f"/proc/{d}/comm").strip()
            cmd = read(f"/proc/{d}/cmdline").replace("\0", " ").strip()
        except OSError:
            continue
        if "cpu-budget" in cmd:
            continue
        for name, (kind, pat) in specs:
            if (kind == "comm" and comm == pat) or (kind == "re" and cmd and pat.search(cmd)):
                found.append((name, d))
                break
    return found

def snapshot(pids):
    snap = {}
    for name, pid in pids:
        for t in glob.glob(f"/proc/{pid}/task/*"):
            try:
                rt = int(read(f"{t}/schedstat").split()[0])
                st = read(f"{t}/status")
                vol = int(re.search(r"^voluntary_ctxt_switches:\s+(\d+)", st, re.M).group(1))
                nv = int(re.search(r"^nonvoluntary_ctxt_switches:\s+(\d+)", st, re.M).group(1))
                comm = read(f"{t}/comm").strip().replace(" ", "_")
            except (OSError, AttributeError, ValueError, IndexError):
                continue
            snap[(name, pid, t.rsplit("/", 1)[1])] = (rt, vol, nv, comm)
    return snap

gpu_file = next((f for f in sorted(glob.glob("/sys/class/drm/card*/device/gpu_busy_percent")) if os.access(f, os.R_OK)), None)
gpu_vals, stop = [], threading.Event()
def gpu_loop():
    while not stop.is_set():
        try:
            gpu_vals.append(int(read(gpu_file).strip()))
        except (OSError, ValueError):
            pass
        stop.wait(0.1)

pids = find_pids()
th = None
if gpu_on and gpu_file:
    th = threading.Thread(target=gpu_loop, daemon=True); th.start()
t0 = time.monotonic_ns(); A = snapshot(pids)
time.sleep(dur)
B = snapshot(pids); t1 = time.monotonic_ns()
stop.set()
if th:
    th.join()
dt = (t1 - t0) / 1e9

rows = {}
for k, (rt, vol, nv, comm) in B.items():
    r0, v0, n0, _ = A.get(k, (0, 0, 0, comm))
    e = rows.setdefault(k[:2], {"rt": 0, "vol": 0, "nv": 0, "thr": 0, "new": 0, "gone": 0, "per": []})
    e["rt"] += rt - r0; e["vol"] += vol - v0; e["nv"] += nv - n0; e["thr"] += 1; e["new"] += k not in A
    e["per"].append((rt - r0, vol - v0, comm, k[2]))
for k in A:
    if k not in B:
        rows.setdefault(k[:2], {"rt": 0, "vol": 0, "nv": 0, "thr": 0, "new": 0, "gone": 0, "per": []})["gone"] += 1

def rss(pid):
    try:
        m = re.search(r"^VmRSS:\s+(\d+)", read(f"/proc/{pid}/status"), re.M)
        return int(m.group(1)) / 1024 if m else float("nan")
    except OSError:
        return float("nan")

print(f"# Δt mesuré : {dt:.3f} s\n")
print("| processus | pid | fils | CPU % (1 cœur) | réveils/s | préempt./s | RSS Mo | fils les plus actifs (CPU % / réveils/s) |")
print("|---|---:|---:|---:|---:|---:|---:|---|")
out, seen = [], set()
for (name, pid), e in sorted(rows.items()):
    seen.add(name)
    cpu, wk, pre = e["rt"] / 1e9 / dt * 100, e["vol"] / dt, e["nv"] / dt
    tops = ", ".join(f"{c}#{t} {r/1e9/dt*100:.2f}/{v/dt:.1f}" for r, v, c, t in sorted(e["per"], reverse=True)[:top])
    extra = (f" ; +{e['new']} fil(s) créé(s)" if e["new"] else "") + (f" ; {e['gone']} disparu(s)" if e["gone"] else "")
    m = rss(pid)
    print(f"| {name} | {pid} | {e['thr']} | {cpu:.2f} | {wk:.1f} | {pre:.1f} | {m:.0f} | {tops}{extra} |")
    out.append((name, pid, e["thr"], cpu, wk, pre, m))
gpu = ""
if gpu_vals:
    gpu = f"{sum(gpu_vals)/len(gpu_vals):.1f}"
    print(f"\nGPU occupé : moyenne {gpu} % · max {max(gpu_vals)} % ({len(gpu_vals)} échantillons, {gpu_file})")
absent = [n for n, _ in specs if n not in seen]
if absent:
    print(f"\nsans processus : {', '.join(absent)}")
if tsv:
    new = not os.path.exists(tsv)
    os.makedirs(os.path.dirname(os.path.abspath(tsv)), exist_ok=True)
    now = datetime.datetime.now().isoformat(timespec="seconds")
    with open(tsv, "a") as f:
        if new:
            f.write("horodatage\tlibelle\tduree_s\tprocessus\tpid\tfils\tcpu_pct\treveils_s\tpreempt_s\trss_mo\tgpu_pct\n")
        for name, pid, thr, cpu, wk, pre, m in out:
            f.write(f"{now}\t{label}\t{dt:.2f}\t{name}\t{pid}\t{thr}\t{cpu:.3f}\t{wk:.2f}\t{pre:.2f}\t{m:.0f}\t{gpu}\n")
    print(f"\najouté à {tsv}")
EOF
