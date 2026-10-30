"""Point d'entrée de `ultima` : TUI sans argument (terminal interactif), sous-commandes sinon."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

from . import CONTRACT_MAJOR, CONTRACT_MINOR, VERSION, consolecfg, doctor, logs, paths, procs, settings, textview
from .palette import KO_MARK, OK_MARK, SEP, WARN_MARK, Ansi, color_enabled
from .state import ALIVE, load as load_state, probe_socket

PROG = "ultima"

USAGE = f"""\
ULTIMA — tracker externe pour Slay the Spire 2

  {PROG}                      TUI plein écran (tableau de bord, deck, HUD, journal, contrôles)
  {PROG} start [-v] [--mirror] [-- options du daemon]
                              lance le daemon en arrière-plan (journal : ~/.local/state/ultima/daemon.log)
  {PROG} kill [--all]         arrête le daemon de ce dossier IPC (--all : tous les ultima-daemon)
  {PROG} restart              kill puis start
  {PROG} status               état publié, décision en cours (code 3 si daemon injoignable)
  {PROG} logs [-f] [-n N] [--level debug|info|warn|error] [--grep TEXTE]
  {PROG} ps                   processus du daemon
  {PROG} hud on|off|status    active / désactive / interroge le HUD (extension GNOME ultima@mio)
  {PROG} dump [options]       calcule l'état une fois sur stdout (rien n'est publié)
  {PROG} doctor               vérifie l'installation (lecture seule)
  {PROG} install [--from DÉPÔT] [options de tools/install.sh]
  {PROG} uninstall [--purge] [-y]
  {PROG} version

Options communes : --json (sortie machine : status, ps, hud status, doctor, version, logs),
--color auto|always|never (NO_COLOR respecté).
Fichiers : réglages ~/.config/ultima/settings.json · préférences console.json · état $XDG_RUNTIME_DIR/ultima/
"""


class Out:
    def __init__(self, color_mode: str):
        self.ansi = Ansi(color_enabled(sys.stdout, color_mode))

    def line(self, segs: textview.Line) -> str:
        parts = []
        for text, tone, bold in segs:
            if tone == "dim":
                parts.append(self.ansi.dim(text))
            elif tone is None and not bold:
                parts.append(text)
            else:
                parts.append(self.ansi.fg(text, tone or "cream", bold=bold))
        return "".join(parts)

    def print(self, segs: textview.Line) -> None:
        print(self.line(segs))


def _pop_flag(args: list[str], *names: str) -> bool:
    found = False
    for n in names:
        while n in args:
            args.remove(n)
            found = True
    return found


def _pop_opt(args: list[str], name: str, default: str | None = None) -> str | None:
    for i, a in enumerate(args):
        if a == name and i + 1 < len(args):
            v = args[i + 1]
            del args[i:i + 2]
            return v
        if a.startswith(name + "="):
            del args[i]
            return a.split("=", 1)[1]
    return default


def main(argv: list[str] | None = None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    passthrough: list[str] = []
    if "--" in args:
        i = args.index("--")
        args, passthrough = args[:i], args[i + 1:]
    color = _pop_opt(args, "--color", "auto") or "auto"
    as_json = _pop_flag(args, "--json")
    out = Out(color)
    cmd = args.pop(0) if args else None

    if cmd is None:
        if sys.stdout.isatty() and sys.stdin.isatty() and not as_json:
            return cmd_tui()
        return cmd_status(out, as_json)
    try:
        handler = {
            "tui": lambda: cmd_tui(),
            "start": lambda: cmd_start(out, args + passthrough),
            "up": lambda: cmd_start(out, args + passthrough),
            "kill": lambda: cmd_kill(out, args), "stop": lambda: cmd_kill(out, args),
            "down": lambda: cmd_kill(out, args),
            "restart": lambda: cmd_restart(out, args + passthrough),
            "status": lambda: cmd_status(out, as_json), "st": lambda: cmd_status(out, as_json),
            "logs": lambda: cmd_logs(out, args, as_json), "log": lambda: cmd_logs(out, args, as_json),
            "ps": lambda: cmd_ps(out, as_json),
            "hud": lambda: cmd_hud(out, args, as_json),
            "dump": lambda: cmd_dump(args + passthrough),
            "doctor": lambda: cmd_doctor(out, args, as_json),
            "install": lambda: cmd_install(args + passthrough),
            "uninstall": lambda: cmd_uninstall(out, args),
            "version": lambda: cmd_version(out, as_json), "--version": lambda: cmd_version(out, as_json),
            "daemon-exec": lambda: cmd_daemon_exec(args + passthrough),
            "help": lambda: _usage(0, color=color), "-h": lambda: _usage(0, color=color),
            "--help": lambda: _usage(0, color=color),
        }.get(cmd)
        if handler is None:
            print(f"{PROG} : commande inconnue « {cmd} »\n", file=sys.stderr)
            return _usage(2, sys.stderr, color)
        return handler()
    except KeyboardInterrupt:
        return 130
    except BrokenPipeError:
        try:
            sys.stdout = open(os.devnull, "w")
        except OSError:
            pass
        return 0


def colored_usage(ansi: Ansi) -> str:
    """Aide en couleur : titre crème gras, commandes en or, descriptions et options en secondaire."""
    if not ansi.enabled:
        return USAGE
    out = []
    for i, line in enumerate(USAGE.splitlines()):
        stripped = line.strip()
        if i == 0:
            out.append(ansi.fg(line, "cream", bold=True))
        elif line.startswith(f"  {PROG}"):
            cmd, sep, desc = stripped.partition("  ")
            gap = line[: len(line) - len(line.lstrip())] + cmd
            rest = line[len(gap):]
            out.append(ansi.fg(gap, "gold", bold=True) + ansi.dim(rest) if sep else ansi.fg(line, "gold", bold=True))
        elif stripped:
            out.append(ansi.dim(line))
        else:
            out.append(line)
    return "\n".join(out) + "\n"


def _usage(code: int, stream=None, color: str = "auto") -> int:
    stream = stream or sys.stdout
    print(colored_usage(Ansi(color_enabled(stream, color))), end="", file=stream)
    return code


# ---------- TUI ----------

def cmd_tui() -> int:
    try:
        from .tui import run_tui
    except ImportError as e:
        print(f"{PROG} : TUI indisponible ({e}). Relancer tools/install.sh (venv + Textual) ;"
              f" en attendant : {PROG} status | {PROG} help", file=sys.stderr)
        return 1
    return run_tui()


# ---------- status ----------

def gather_health():
    res = load_state()
    sock = probe_socket()
    h = textview.Health(daemon=sock, pids=procs.owned_pids(), state_kind=res.kind, age_s=res.age_s,
                        error=res.error)
    return res, h


def cmd_status(out: Out, as_json: bool) -> int:
    res, h = gather_health()
    alive = h.daemon == ALIVE
    if as_json:
        print(json.dumps({
            "console": VERSION,
            "daemon": {"reachable": alive, "socket": str(paths.socket_path()), "pids": h.pids,
                       "systemd": procs.uses_systemd()},
            "state": {"kind": res.kind, "path": str(res.path), "ageS": None if res.age_s is None else round(res.age_s, 1),
                      "error": res.error},
            "engine": res.state,
        }, ensure_ascii=False, indent=2))
        return 0 if alive else 3
    width = min(shutil.get_terminal_size((100, 24)).columns, 120)
    out.print(textview.health_line(res.state, h))
    for line in textview.dashboard(res.state, h, width):
        out.print(line)
    return 0 if alive else 3


# ---------- start / kill ----------

def cmd_start(out: Out, args: list[str]) -> int:
    cfg, _ = consolecfg.load()
    if _pop_flag(args, "--mirror"):
        cfg.mirror = True
    if _pop_flag(args, "-v", "--verbose"):
        cfg.verbose = True
    r = procs.start(cfg, args or None)
    stream = sys.stdout if r.ok else sys.stderr
    mark = out.ansi.fg(OK_MARK, "green") if r.ok else out.ansi.fg(KO_MARK, "red")
    print(f"{mark} {r.message}", file=stream)
    if r.ok and "déjà" not in r.message:
        print(out.ansi.dim(f"  journal : {paths.log_path()}"))
    return 0 if r.ok else 1


def cmd_kill(out: Out, args: list[str]) -> int:
    r = procs.stop(all_users_daemons=_pop_flag(args, "--all"))
    print(f"{out.ansi.fg(OK_MARK, 'green') if r.pids else out.ansi.dim('·')} {r.message}")
    return 0


def cmd_restart(out: Out, args: list[str]) -> int:
    cmd_kill(out, [])
    return cmd_start(out, args)


# ---------- logs ----------

def cmd_logs(out: Out, args: list[str], as_json: bool) -> int:
    path = paths.log_path()
    follow = _pop_flag(args, "-f", "--follow")
    n = int(_pop_opt(args, "-n", "50") or 50)
    level = _pop_opt(args, "--level", "debug") or "debug"
    needle = _pop_opt(args, "--grep")
    cfg, _ = consolecfg.load()
    if not path.exists() and not follow:
        print(f"aucun journal ({path})", file=sys.stderr)
        legacy = paths.legacy_log_path()
        if legacy.exists():
            print(out.ansi.dim(f"  ancien journal : {legacy}"), file=sys.stderr)
        return 1
    prev = "info"

    def emit(raw: str) -> None:
        nonlocal prev
        ll = logs.parse_line(raw, prev)
        prev = ll.level
        if not logs.accept(ll, level, needle):
            return
        if as_json:
            print(json.dumps({"ts": ll.ts, "level": ll.level, "msg": ll.msg}, ensure_ascii=False), flush=True)
        else:
            print(logs.color_line(ll, cfg, out.ansi), flush=True)

    for raw in logs.tail(path, n):
        emit(raw)
    if follow:
        for raw in logs.follow(path):
            emit(raw)
    return 0


# ---------- ps ----------

def cmd_ps(out: Out, as_json: bool) -> int:
    pids = procs.all_daemon_pids()
    owned = set(procs.owned_pids())
    infos = [i for i in (procs.proc_info(p) for p in pids) if i]
    if as_json:
        print(json.dumps([dict(i.to_json(), owned=i.pid in owned) for i in infos], ensure_ascii=False, indent=2))
        return 0
    if not infos:
        print("aucun processus ULTIMA")
        return 0
    print(out.ansi.dim(f"{'PID':>7}  {'DURÉE':>9}  {'CPU s':>7}  {'RSS':>8}  COMMANDE"))
    for i in infos:
        mark = "" if i.pid in owned else out.ansi.dim("  (autre dossier IPC)")
        print(f"{i.pid:>7}  {procs.fmt_elapsed(i.etime_s):>9}  {i.cpu_s:>7.1f}  {i.rss_kb // 1024:>6} Mo  {i.args}{mark}")
    return 0


# ---------- hud ----------

def cmd_hud(out: Out, args: list[str], as_json: bool) -> int:
    sub = args[0] if args else "status"
    if sub in ("on", "off"):
        r = procs.hud_set(sub == "on")
        print(f"{out.ansi.fg(OK_MARK, 'green') if r.ok else out.ansi.fg(KO_MARK, 'red')} {r.message}",
              file=sys.stdout if r.ok else sys.stderr)
        if r.ok and sub == "on" and not paths.extension_dir(procs.EXT_UUID).is_dir():
            print(out.ansi.dim("  extension absente : tools/install.sh puis se reconnecter"))
        return 0 if r.ok else 1
    if sub != "status":
        print(f"usage : {PROG} hud on|off|status", file=sys.stderr)
        return 2
    st = procs.hud_status()
    s, err = settings.load()
    st["settings"] = s.hud.to_json()
    if as_json:
        print(json.dumps(st, ensure_ascii=False, indent=2))
        return 0 if st["installed"] else 1
    if not st["installed"]:
        print(f"{out.ansi.fg(KO_MARK, 'red')} extension {procs.EXT_UUID} absente")
        return 1
    en = st["enabled"]
    state = f"{'activé' if en else 'désactivé' if en is False else '?'}{SEP}{st['state'] or '?'}"
    print(f"HUD      {out.ansi.fg(state, 'green' if en else 'orange') if en is not None else state}")
    h = s.hud
    print(out.ansi.dim(f"réglages {h.quality}{SEP}hauteur {round(h.height_fraction * 100)} %{SEP}"
                       f"opacité {round(h.rest_opacity * 100)} %{SEP}{settings.SIDE_LABELS[h.side]}"))
    print(out.ansi.dim(f"chemin   {st['path']}"))
    if err:
        print(out.ansi.fg(err, "orange"))
    return 0


# ---------- dump ----------

def cmd_dump(args: list[str]) -> int:
    daemon = paths.find_daemon()
    if daemon is None:
        print(f"{PROG} : binaire du daemon introuvable (tools/install.sh, ou ULTIMA_DAEMON=/chemin)", file=sys.stderr)
        return 1
    return subprocess.run([str(daemon), "dump", *args], env=paths.daemon_env()).returncode


def cmd_daemon_exec(args: list[str]) -> int:
    cfg, _ = consolecfg.load()
    procs.exec_daemon(cfg, args)
    return 1  # jamais atteint


# ---------- doctor ----------

def cmd_doctor(out: Out, args: list[str], as_json: bool) -> int:
    checks = doctor.run_checks(quick=_pop_flag(args, "--quick"))
    worst = doctor.worst(checks)
    if as_json:
        print(json.dumps({"status": worst, "checks": [c.to_json() for c in checks]}, ensure_ascii=False, indent=2))
        return 1 if worst == doctor.FAIL else 0
    marks = {doctor.OK: (OK_MARK, "green"), doctor.WARN: (WARN_MARK, "orange"), doctor.FAIL: (KO_MARK, "red"),
             doctor.INFO: ("·", "gray")}
    w = max(len(c.label) for c in checks)
    for c in checks:
        m, tone = marks[c.status]
        print(f"{out.ansi.fg(m, tone, bold=True)} {c.label:<{w}}  {out.ansi.dim(c.detail)}")
    return 1 if worst == doctor.FAIL else 0


# ---------- version ----------

def cmd_version(out: Out, as_json: bool) -> int:
    man = doctor.read_manifest() or {}
    info = {"console": VERSION, "contract": f"{CONTRACT_MAJOR}.{CONTRACT_MINOR}", "home": str(paths.ultima_home()),
            "commit": man.get("commit"), "installedAt": man.get("installedAt"),
            "daemon": str(paths.find_daemon() or "") or None}
    if as_json:
        print(json.dumps(info, ensure_ascii=False, indent=2))
        return 0
    contract = info["contract"]
    print(f"ultima {VERSION}" + out.ansi.dim(f"{SEP}contrat v{contract}"))
    if info["commit"]:
        print(out.ansi.dim(f"commit {info['commit']}{SEP}installé {info['installedAt']}"))
    return 0


# ---------- install / uninstall ----------

def cmd_install(args: list[str]) -> int:
    src = _pop_opt(args, "--from")
    if src is None:
        src = (doctor.read_manifest() or {}).get("source")
    if not src:
        print(f"{PROG} install : préciser --from <dépôt ULTIMA>", file=sys.stderr)
        return 2
    script = Path(src) / "tools" / "install.sh"
    if not script.is_file():
        print(f"{PROG} install : {script} introuvable", file=sys.stderr)
        return 2
    os.execv("/usr/bin/env", ["env", "bash", str(script), *args])
    return 1


def cmd_uninstall(out: Out, args: list[str]) -> int:
    """Retire exactement ce que le manifeste liste ; --purge : réglages, journal et dossier IPC aussi."""
    from . import installer   # importé en entier AVANT que ses fichiers soient supprimés
    purge = _pop_flag(args, "--purge")
    yes = _pop_flag(args, "-y", "--yes")
    man = installer.read_manifest()
    if man is None:
        print(f"{PROG} uninstall : aucune installation trouvée dans {paths.ultima_home()} (manifest.json absent)",
              file=sys.stderr)
        return 1
    p = installer.uninstall_plan(man, purge)
    print(f"Sera retiré : {len(p['remove'])} élément(s) listés dans {paths.manifest_path()}")
    for b in p["restore"]:
        print(f"  remis en place : {b['original']}")
    for x in p["purge"]:
        print(out.ansi.fg(f"  PURGE : {x}", "orange"))
    if not purge:
        print(out.ansi.dim(f"Conservé : {paths.config_dir()} (réglages), {paths.log_dir()} (journal)"
                           f" — --purge pour tout retirer"))
    if not yes:
        if not sys.stdin.isatty():
            print(f"{PROG} uninstall : confirmer avec -y (entrée non interactive)", file=sys.stderr)
            return 2
        if input("Continuer ? [o/N] ").strip().lower() not in ("o", "oui", "y", "yes"):
            print("annulé")
            return 1
    try:
        installer.uninstall(purge=purge, log=lambda m: print(out.ansi.dim(f"  {m}")))
    except installer.InstallError as e:
        print(f"{out.ansi.fg(KO_MARK, 'red')} {e}", file=sys.stderr)
        return 1
    print(f"{out.ansi.fg(OK_MARK, 'green')} ULTIMA désinstallé")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
