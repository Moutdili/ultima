#!/usr/bin/env python3
"""analyze-frametimes.py — compare des journaux MangoHud (temps par image) par condition A/B.

Lit les CSV écrits par MangoHud 0.8.x (`log_interval=0` : une ligne par image présentée,
`frametime` en ms, `elapsed` en ns depuis le début du journal), avec ou sans `log_versioning`.
Ignore les `*_summary.csv`. N'écrit rien : tout sort sur stdout.

Attribution d'une condition à chaque CSV (première règle qui s'applique) :
  1. --assign FICHIER=CONDITION (répétable ; FICHIER = nom de base) ;
  2. --marks MARKS.tsv : lignes « iso_local<TAB>condition » (écrites par tools/perf/session.sh mark) ;
     le CSV prend la dernière marque dont l'heure est <= heure de début du CSV + 1 s
     (heure lue dans le nom MangoHud « <prog>_AAAA-MM-JJ_HH-MM-SS.csv », heure locale) ;
  3. le nom du dossier parent, s'il diffère du dossier passé en argument ;
  4. sinon « inconnu ».

Indicateurs (par segment et par condition, images de toutes les répétitions mises bout à bout) :
  FPS moyen = 1000 * n / somme(frametime)
  1 % low   = 1000 / moyenne des 1 % de frametimes les plus longs (« moyenne du pire 1 % »)
  0,1 % low = idem sur 0,1 %
  p50, p99  = percentiles du temps par image (interpolation linéaire), en ms
  à-coups   = images > 1,5 x médiane (à 60 Hz : >= 25 ms, une synchro verticale manquée), par minute
  pire s    = FPS de la pire seconde (tranches de 1 s sur `elapsed`)
  GPU %     = moyenne de la colonne gpu_load (charge GPU GLOBALE : jeu + compositeur ; à travail du
              jeu égal, un écart entre conditions = coût GPU du compositeur/overlay)

Verdict par condition vs --baseline (défaut « sans-ultima ») et --budget (défaut 3 % de perte de 1 % low) :
  OK        perte <= budget
  DEPASSE   perte > budget et perte > bruit de mesure
  INDECIS   perte > budget mais <= bruit (bruit = écart relatif max-min du 1 % low entre
            répétitions de la référence ou de la condition) -> refaire des répétitions
  Bruit inconnu (une seule répétition) : DEPASSE est marqué « bruit inconnu ».

Segments invalides (exclus par défaut, --keep-invalid pour les garder) :
  - un événement FocusOut / « Limiting background FPS » du jeu tombe dans le segment
    (--events EVENTS.tsv : « iso_local<TAB>texte », écrit par session.sh godot) : le jeu se
    bride lui-même à 30 FPS quand il perd le focus ;
  - plafond 30 FPS détecté : >= 2 secondes consécutives à 27-31 FPS avec médiane 32-35 ms.

Codes retour : 0 tout OK · 1 au moins un DEPASSE · 3 INDECIS ou pas de référence · 2 erreur.

Usage : analyze-frametimes.py [options] CHEMIN...   (fichiers .csv ou dossiers, récursif)
  --marks F  --events F  --assign FICHIER=COND  --baseline COND  --budget PCT
  --trim S (secondes retirées au début et à la fin de chaque segment, défaut 1.0)
  --order A,B,C (ordre d'affichage)  --json  --keep-invalid
"""
import argparse
import datetime as dt
import json
import math
import os
import re
import sys

FNAME_TS = re.compile(r"_(\d{4}-\d{2}-\d{2})_(\d{2}-\d{2}-\d{2})\.csv$")
FOCUS_RE = re.compile(r"FocusOut received|Limiting background FPS")


# ----------------------------------------------------------------------------- lecture

class Segment:
    def __init__(self, path):
        self.path = path
        self.name = os.path.basename(path)
        self.frametimes = []   # ms
        self.elapsed = []      # s depuis le début du journal
        self.gpu = []          # gpu_load % (charge GPU globale échantillonnée par MangoHud)
        self.bad_rows = 0
        self.header_kind = "?"
        self.start = None      # datetime naïf, heure locale (nom de fichier)
        self.condition = "inconnu"
        self.condition_source = "-"
        self.invalid = []      # raisons


def parse_csv(path):
    seg = Segment(path)
    m = FNAME_TS.search(seg.name)
    if m:
        seg.start = dt.datetime.strptime(m.group(1) + " " + m.group(2), "%Y-%m-%d %H-%M-%S")
    with open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()
    hdr = None
    for i, line in enumerate(lines):
        cols = [c.strip() for c in line.split(",")]
        if "frametime" in cols and "fps" in cols:
            hdr = i
            break
    if hdr is None:
        raise ValueError("pas de ligne d'en-tête « fps,frametime,… » (journal MangoHud ?)")
    seg.header_kind = "versionné" if any("FRAME METRICS" in l for l in lines[:hdr]) else "simple"
    cols = [c.strip() for c in lines[hdr].split(",")]
    i_ft = cols.index("frametime")
    i_el = cols.index("elapsed") if "elapsed" in cols else None
    i_gpu = cols.index("gpu_load") if "gpu_load" in cols else None
    acc = 0.0
    for line in lines[hdr + 1:]:
        if not line.strip():
            continue
        parts = line.split(",")
        try:
            if len(parts) != len(cols):
                raise ValueError
            ft = float(parts[i_ft])
            if not (ft > 0 and math.isfinite(ft)):
                raise ValueError
            el = float(parts[i_el]) / 1e9 if i_el is not None else None
            gp = float(parts[i_gpu]) if i_gpu is not None else None
        except ValueError:
            seg.bad_rows += 1
            continue
        acc += ft / 1000.0
        seg.frametimes.append(ft)
        seg.elapsed.append(el if el is not None else acc)
        seg.gpu.append(gp)
    return seg


def trim(seg, s):
    if s <= 0 or not seg.elapsed:
        return
    t0, t1 = seg.elapsed[0] + s, seg.elapsed[-1] - s
    keep = [(f, e, g) for f, e, g in zip(seg.frametimes, seg.elapsed, seg.gpu) if t0 <= e <= t1]
    seg.frametimes = [k[0] for k in keep]
    seg.elapsed = [k[1] for k in keep]
    seg.gpu = [k[2] for k in keep]


def read_tsv_times(path):
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            ts, _, text = line.partition("\t")
            try:
                t = dt.datetime.fromisoformat(ts.strip())
            except ValueError:
                continue
            out.append((t.replace(tzinfo=None), text.strip()))
    out.sort(key=lambda x: x[0])
    return out


def collect(paths):
    files = []
    for p in paths:
        if os.path.isdir(p):
            for root, _, names in os.walk(p):
                for n in sorted(names):
                    if n.endswith(".csv") and not n.endswith("_summary.csv"):
                        files.append((os.path.join(root, n), os.path.abspath(p)))
        elif p.endswith(".csv"):
            files.append((p, None))
        else:
            raise SystemExit(f"chemin ignoré (ni .csv ni dossier) : {p}")
    return files


# ----------------------------------------------------------------------------- statistiques

def percentile(sorted_vals, q):
    if not sorted_vals:
        return float("nan")
    k = (len(sorted_vals) - 1) * q
    lo, hi = math.floor(k), math.ceil(k)
    if lo == hi:
        return sorted_vals[lo]
    return sorted_vals[lo] + (sorted_vals[hi] - sorted_vals[lo]) * (k - lo)


def low_fps(sorted_vals, frac):
    n = max(1, int(round(len(sorted_vals) * frac)))
    worst = sorted_vals[-n:]
    return 1000.0 / (sum(worst) / len(worst))


def per_second(frametimes, elapsed):
    bins = {}
    for f, e in zip(frametimes, elapsed):
        bins.setdefault(int(e), []).append(f)
    out = []
    for k in sorted(bins):
        fts = bins[k]
        out.append((k, 1000.0 * len(fts) / sum(fts), sorted(fts)[len(fts) // 2]))
    return out


def metrics(frametimes, secs, gpu=()):
    if not frametimes:
        return None
    s = sorted(frametimes)
    med = percentile(s, 0.5)
    total_s = sum(frametimes) / 1000.0
    stutters = sum(1 for f in frametimes if f > 1.5 * med)
    # tranches entières seulement (une tranche partielle en bord de segment fausserait le min)
    full = [fps for _, fps, _ in secs[1:-1]] or [fps for _, fps, _ in secs]
    return {
        "frames": len(frametimes),
        "duration_s": round(total_s, 2),
        "avg_fps": 1000.0 * len(frametimes) / sum(frametimes),
        "low1_fps": low_fps(s, 0.01),
        "low01_fps": low_fps(s, 0.001),
        "p50_ms": med,
        "p99_ms": percentile(s, 0.99),
        "max_ms": s[-1],
        "stutters_per_min": stutters / (total_s / 60.0) if total_s > 0 else float("nan"),
        "worst_sec_fps": min(full) if full else float("nan"),
        "gpu_load_pct": (sum(g for g in gpu if g is not None) / max(1, sum(1 for g in gpu if g is not None)))
                        if any(g is not None for g in gpu) else None,
    }


def cap30(secs):
    run = 0
    for _, fps, med in secs:
        if 27.0 <= fps <= 31.0 and 32.0 <= med <= 35.0:
            run += 1
            if run >= 2:
                return True
        else:
            run = 0
    return False


# ----------------------------------------------------------------------------- principal

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--marks")
    ap.add_argument("--events")
    ap.add_argument("--assign", action="append", default=[])
    ap.add_argument("--baseline", default="sans-ultima")
    ap.add_argument("--budget", type=float, default=3.0)
    ap.add_argument("--trim", type=float, default=1.0)
    ap.add_argument("--order")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--keep-invalid", action="store_true")
    a = ap.parse_args(argv)

    assign = {}
    for item in a.assign:
        k, sep, v = item.partition("=")
        if not sep:
            ap.error(f"--assign attend FICHIER=CONDITION : {item}")
        assign[os.path.basename(k)] = v
    marks = read_tsv_times(a.marks) if a.marks else []
    events = [(t, x) for t, x in read_tsv_times(a.events) if FOCUS_RE.search(x)] if a.events else []

    segs = []
    for path, root in collect(a.paths):
        try:
            seg = parse_csv(path)
        except (OSError, ValueError) as e:
            print(f"# ERREUR {path} : {e}", file=sys.stderr)
            continue
        if seg.name in assign:
            seg.condition, seg.condition_source = assign[seg.name], "--assign"
        elif marks and seg.start:
            before = [lab for t, lab in marks if t <= seg.start + dt.timedelta(seconds=1)]
            if before:
                seg.condition, seg.condition_source = before[-1], "marque"
        if seg.condition == "inconnu" and root:
            parent = os.path.dirname(os.path.abspath(path))
            if parent != root:
                seg.condition, seg.condition_source = os.path.basename(parent), "dossier"
        raw_end = seg.elapsed[-1] if seg.elapsed else 0.0
        trim(seg, a.trim)
        if len(seg.frametimes) < 60:
            seg.invalid.append(f"trop court ({len(seg.frametimes)} images après --trim)")
        secs = per_second(seg.frametimes, seg.elapsed)
        if seg.start and events:
            t1 = seg.start + dt.timedelta(seconds=raw_end + 1)
            hits = [x for t, x in events if seg.start <= t <= t1]
            if hits:
                seg.invalid.append(f"focus perdu ({len(hits)} év. jeu)")
        if cap30(secs):
            seg.invalid.append("plafond 30 FPS (focus perdu ?)")
        seg.metrics = metrics(seg.frametimes, secs, seg.gpu)
        segs.append(seg)

    if not segs:
        print("aucun journal lisible", file=sys.stderr)
        return 2

    # ---- regroupement par condition
    conds = {}
    for s in segs:
        if s.invalid and not a.keep_invalid:
            continue
        conds.setdefault(s.condition, []).append(s)
    order = a.order.split(",") if a.order else []
    names = [c for c in order if c in conds] + sorted(c for c in conds if c not in order)

    table = {}
    for c in names:
        fts, secs, gpu = [], [], []
        for s in conds[c]:
            fts += s.frametimes
            gpu += s.gpu
            secs += per_second(s.frametimes, s.elapsed)
        m = metrics(fts, secs, gpu)
        reps = [s.metrics["low1_fps"] for s in conds[c] if s.metrics]
        m["reps"] = len(reps)
        m["noise_pct"] = ((max(reps) - min(reps)) / (sum(reps) / len(reps)) * 100.0) if len(reps) >= 2 else None
        table[c] = m

    base = table.get(a.baseline)
    worst_code = 0
    for c in names:
        m = table[c]
        if c == a.baseline:
            m["verdict"] = "REFERENCE"
            continue
        if not base:
            m["verdict"] = "SANS REFERENCE"
            worst_code = max(worst_code, 3)
            continue
        loss = (base["low1_fps"] - m["low1_fps"]) / base["low1_fps"] * 100.0
        m["d_avg_pct"] = (m["avg_fps"] - base["avg_fps"]) / base["avg_fps"] * 100.0
        m["loss_low1_pct"] = loss
        m["d_p99_pct"] = (m["p99_ms"] - base["p99_ms"]) / base["p99_ms"] * 100.0
        noises = [x for x in (base["noise_pct"], m["noise_pct"]) if x is not None]
        noise = max(noises) if noises else None
        m["noise_used_pct"] = noise
        if loss <= a.budget:
            m["verdict"] = "OK"
        elif noise is None:
            m["verdict"] = "DEPASSE (bruit inconnu)"
            worst_code = max(worst_code, 1)
        elif loss > noise:
            m["verdict"] = "DEPASSE"
            worst_code = max(worst_code, 1)
        else:
            m["verdict"] = "INDECIS"
            worst_code = 1 if worst_code == 1 else 3

    if a.json:
        print(json.dumps({
            "baseline": a.baseline, "budget_pct": a.budget, "trim_s": a.trim,
            "segments": [{"file": s.name, "condition": s.condition, "source": s.condition_source,
                          "start": s.start.isoformat() if s.start else None, "header": s.header_kind,
                          "bad_rows": s.bad_rows, "invalid": s.invalid, "metrics": s.metrics} for s in segs],
            "conditions": table, "exit_code": worst_code,
        }, indent=2, ensure_ascii=False, default=lambda x: None))
        return worst_code

    f1 = lambda x: "—" if x is None or (isinstance(x, float) and math.isnan(x)) else f"{x:.1f}"
    f2 = lambda x: "—" if x is None or (isinstance(x, float) and math.isnan(x)) else f"{x:.2f}"
    sg = lambda x: "—" if x is None else f"{x:+.1f}"
    print(f"# Temps par image — référence « {a.baseline} », budget ≤ {a.budget:g} % de perte de 1 % low, trim {a.trim:g} s\n")
    print("## Segments\n")
    print("| fichier | condition (source) | en-tête | images | durée s | FPS moy | 1 % low | p99 ms | lignes rejetées | validité |")
    print("|---|---|---|---:|---:|---:|---:|---:|---:|---|")
    for s in segs:
        m = s.metrics or {}
        val = "OK" if not s.invalid else ("EXCLU : " if not a.keep_invalid else "gardé : ") + "; ".join(s.invalid)
        print(f"| {s.name} | {s.condition} ({s.condition_source}) | {s.header_kind} | {m.get('frames', 0)} | "
              f"{f1(m.get('duration_s'))} | {f1(m.get('avg_fps'))} | {f1(m.get('low1_fps'))} | {f2(m.get('p99_ms'))} | "
              f"{s.bad_rows} | {val} |")
    print("\n## Comparaison par condition\n")
    print("| condition | rép. | images | FPS moy | Δ moy % | 1 % low | perte 1 % low % | 0,1 % low | p50 ms | p99 ms | Δ p99 % | à-coups/min | pire s FPS | GPU % | bruit % | verdict |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|")
    for c in names:
        m = table[c]
        print(f"| {c} | {m['reps']} | {m['frames']} | {f1(m['avg_fps'])} | {sg(m.get('d_avg_pct'))} | "
              f"{f1(m['low1_fps'])} | {sg(m.get('loss_low1_pct'))} | {f1(m['low01_fps'])} | {f2(m['p50_ms'])} | "
              f"{f2(m['p99_ms'])} | {sg(m.get('d_p99_pct'))} | {f1(m['stutters_per_min'])} | {f1(m['worst_sec_fps'])} | {f1(m['gpu_load_pct'])} | "
              f"{f1(m.get('noise_used_pct', m['noise_pct']))} | {m['verdict']} |")
    print(f"\ncode retour : {worst_code}  (0 = tout OK · 1 = au moins un DEPASSE · 3 = INDECIS / sans référence)")
    return worst_code


if __name__ == "__main__":
    sys.exit(main())
