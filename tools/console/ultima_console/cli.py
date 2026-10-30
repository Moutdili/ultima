"""Point d'entrée de `ultima` : TUI sans argument (terminal interactif), sous-commandes sinon."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

from . import (CONTRACT_MAJOR, CONTRACT_MINOR, VERSION, appearance, banner, consolecfg, custom, doctor,
               fonts, logs, paths, procs, settings, textview)
from . import theme as themes
from .palette import KO_MARK, OK_MARK, SEP, WARN_MARK, Ansi, color_enabled
from .state import ALIVE, get, load as load_state, probe_socket

PROG = "ultima"

USAGE = f"""\
ULTIMA — tracker externe pour Slay the Spire 2

  {PROG}                      TUI plein écran (état, deck, HUD, journal, contrôles, apparence)
  {PROG} start [-v] [--mirror] [-- options du daemon]
                              lance le daemon en arrière-plan (journal : ~/.local/state/ultima/daemon.log)
  {PROG} kill [--all]         arrête le daemon de ce dossier IPC (--all : tous les ultima-daemon)
  {PROG} restart              kill puis start
  {PROG} status               état publié, décision en cours (code 3 si daemon injoignable)
  {PROG} logs [-f] [-n N] [--level debug|info|warn|error] [--grep TEXTE]
  {PROG} ps                   processus du daemon
  {PROG} hud on|off|status    active / désactive / interroge le HUD (extension GNOME ultima@mio)
  {PROG} theme list|show|check|set <surface> <id>
                              thèmes intégrés, perso et terminal (surface : tui, hud, compagnon)
  {PROG} font list|refresh    polices : quatre dossiers + fontconfig (docs/44 §5)
  {PROG} module list|validate <fichier>|new <id>
                              modules déclaratifs (aucun code exécuté)
  {PROG} plugin list|new <id>|test <id>|enable <id>|disable <id>
                              plugins (processus séparé ; activés après un test réussi)
  {PROG} dump [options]       calcule l'état une fois sur stdout (rien n'est publié)
  {PROG} doctor               vérifie l'installation (lecture seule)
  {PROG} install [--from DÉPÔT] [options de tools/install.sh]
  {PROG} uninstall [--purge] [-y]
  {PROG} version

Options communes : --json (sortie machine), --color auto|always|never (NO_COLOR respecté).
Fichiers : réglages ~/.config/ultima/settings.json (dont appearance) · lancement console.json ·
thèmes ~/.config/ultima/themes/ · modules ~/.config/ultima/modules/ · plugins ~/.local/share/ultima/plugins/
"""


def _character(state: Any) -> str | None:
    run = get(state, "run", default=None)
    if not isinstance(run, dict):
        return None
    for key in ("character", "characterId"):
        v = run.get(key)
        if isinstance(v, str) and v:
            return v
    return None


def load_appearance() -> tuple[appearance.Appearance, str | None]:
    """Charge `appearance` (migration unique de console.json faite au passage)."""
    try:
        if appearance.migrate_console():
            pass
    except (settings.SettingsError, OSError) as e:
        return appearance.Appearance(), str(e)
    return appearance.load()


def tui_resolution(state: Any = None) -> themes.Resolution:
    app, _ = load_appearance()
    return app.resolve("tui", character=_character(state))


class Out:
    def __init__(self, color_mode: str, res: themes.Resolution | None = None):
        self.ansi = Ansi(color_enabled(sys.stdout, color_mode))
        self.res = res or themes.resolve(themes.DEFAULT)

    def line(self, segs: textview.Line) -> str:
        parts = []
        for text, tone, bold in segs:
            style = self.res.style(tone, bold)
            parts.append(self.ansi.tone(text, style) if style else text)
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
    res = tui_resolution()
    out = Out(color, res)
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
            "theme": lambda: cmd_theme(out, args, as_json),
            "font": lambda: cmd_font(out, args, as_json),
            "module": lambda: cmd_module(out, args, as_json),
            "plugin": lambda: cmd_plugin(out, args, as_json),
            "dump": lambda: cmd_dump(args + passthrough),
            "doctor": lambda: cmd_doctor(out, args, as_json),
            "install": lambda: cmd_install(args + passthrough),
            "uninstall": lambda: cmd_uninstall(out, args),
            "version": lambda: cmd_version(out, as_json), "--version": lambda: cmd_version(out, as_json),
            "banner": lambda: cmd_banner(out),
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
    """Aide en couleur : titre en texte, commandes en accent, descriptions en secondaire."""
    if not ansi.enabled:
        return USAGE
    res = themes.resolve(themes.DEFAULT)
    out = []
    for i, line in enumerate(USAGE.splitlines()):
        stripped = line.strip()
        if i == 0:
            out.append(ansi.tone(line, res.style("text", True)))
        elif line.startswith(f"  {PROG}"):
            cmd, sep, desc = stripped.partition("  ")
            gap = line[: len(line) - len(line.lstrip())] + cmd
            rest = line[len(gap):]
            out.append(ansi.tone(gap, res.style("accent", True)) + ansi.dim(rest) if sep
                       else ansi.tone(line, res.style("accent", True)))
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
    out.res = tui_resolution(res.state)
    alive = h.daemon == ALIVE
    if as_json:
        app, _ = load_appearance()
        print(json.dumps({
            "console": VERSION,
            "daemon": {"reachable": alive, "socket": str(paths.socket_path()), "pids": h.pids,
                       "systemd": procs.uses_systemd()},
            "state": {"kind": res.kind, "path": str(res.path), "ageS": None if res.age_s is None else round(res.age_s, 1),
                      "error": res.error},
            "appearance": appearance.summary(app) if app else None,
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
    res = tui_resolution(load_state().state)
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
            print(logs.color_line(ll, res, out.ansi), flush=True)

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
    app, app_err = load_appearance()
    hud = app.hud
    st["settings"] = s.hud.to_json()
    st["appearance"] = {"theme": hud.theme, "font": hud.font.to_json(), "tones": hud.tones,
                        "position": hud.position.to_json(), "railEdge": hud.rail_edge}
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
    print(out.ansi.dim(f"apparence thème {hud.theme}{SEP}police {hud.font.family or 'du thème'}"
                       f"{SEP}{hud.position.width} px{SEP}{hud.position.side}/{hud.position.anchor}"))
    print(out.ansi.dim(f"chemin   {st['path']}"))
    if err:
        print(out.ansi.fg(err, "orange"))
    if app_err:
        print(out.ansi.fg(app_err, "orange"))
    return 0


# ---------- theme ----------

def cmd_theme(out: Out, args: list[str], as_json: bool) -> int:
    sub = args[0] if args else "list"
    rest = args[1:]
    if sub == "list":
        rows = themes.list_ids()
        if as_json:
            app, _ = load_appearance()
            print(json.dumps({"themes": rows, "appearance": appearance.summary(app)}, ensure_ascii=False,
                             indent=2))
            return 0
        app, _ = load_appearance()
        print(out.ansi.tone("Thèmes ULTIMA", out.res.style("text", True)))
        for r in rows:
            if r["source"] == "error":
                print(out.ansi.fg(f"  ! {r['description']}", "orange"))
                continue
            mark = "*" if r["id"] in (app.tui.theme, app.hud.theme, app.companion.theme) else " "
            tid = r["id"]
            print(f" {mark} {out.ansi.fg(f'{tid:<22}', 'gold')} {out.ansi.dim(r['label'])}"
                  + (out.ansi.dim(f"  ({r['source']})") if r["source"] != "builtin" else ""))
        print(out.ansi.dim(f"  * = utilisé par une surface · fichiers {paths.themes_dir()} et "
                           f"{paths.user_themes_dir()}"))
        return 0
    if sub == "show":
        if not rest:
            print(f"usage : {PROG} theme show <id>", file=sys.stderr)
            return 2
        tid = rest[0]
        t = themes.load_all().get(tid)
        if t is None:
            print(f"{PROG} theme show : thème « {tid} » introuvable ({PROG} theme list)", file=sys.stderr)
            return 1
        res = themes.resolve(tid)
        if as_json:
            print(json.dumps({"theme": t.to_json(), "source": t.source, "resolved": res.tones,
                              "logColors": res.log_colors, "ansi16": res.ansi16}, ensure_ascii=False, indent=2))
            return 0
        print(out.ansi.tone(f"{t.id}{SEP}{t.label}", out.res.style("text", True)))
        if t.description:
            print(out.ansi.dim(t.description))
        print(out.ansi.dim(f"source {t.source}" + (f" · extends {t.extends}" if t.extends else "")))
        for tone in themes.TONES:
            color = res.tones.get(tone, "")
            if color:
                print(f"  {tone:<10} {out.ansi.fg('■', color) if themes.is_hex(color) else out.ansi.fg('■', color)}"
                      f" {out.ansi.dim(color)}")
        return 0
    if sub == "check":
        if not rest:
            print(f"usage : {PROG} theme check <id>", file=sys.stderr)
            return 2
        result = themes.check(rest[0])
        if as_json:
            print(json.dumps(result, ensure_ascii=False, indent=2))
            return 0 if result["ok"] else 1
        mark = out.ansi.fg(OK_MARK, "green") if result["ok"] else out.ansi.fg(KO_MARK, "red")
        contrast = result.get("contrast")
        print(f"{mark} {result['id']}" + (out.ansi.dim(f"{SEP}contraste {contrast}:1") if contrast else ""))
        for p in result["problems"]:
            print(out.ansi.fg(f"  ! {p}", "orange"))
        if not result["problems"]:
            print(out.ansi.dim("  schéma, héritage, tons et contraste : OK"))
        return 0 if result["ok"] else 1
    if sub == "set":
        if len(rest) < 2:
            print(f"usage : {PROG} theme set <tui|hud|compagnon> <id>", file=sys.stderr)
            return 2
        surface, tid = rest[0], rest[1]
        try:
            appearance.set_theme(surface, tid)
        except appearance.AppearanceError as e:
            print(f"{PROG} theme set : {e}", file=sys.stderr)
            return 1
        if as_json:
            print(json.dumps({"surface": surface, "theme": tid}, ensure_ascii=False))
        else:
            print(f"{out.ansi.fg(OK_MARK, 'green')} {surface} : thème {tid}")
        return 0
    print(f"usage : {PROG} theme list|show <id>|check <id>|set <surface> <id>", file=sys.stderr)
    return 2


# ---------- font ----------

def cmd_font(out: Out, args: list[str], as_json: bool) -> int:
    sub = args[0] if args else "list"
    if sub == "list":
        listing = fonts.list_fonts()
        problems = fonts.verify_base()
        if as_json:
            print(json.dumps({"fonts": listing, "baseProblems": problems,
                              "fontconfig": str(paths.fontconfig_path()),
                              "dir": str(paths.fonts_dir())}, ensure_ascii=False, indent=2))
            return 0
        print(out.ansi.tone(f"Polices{SEP}{paths.fonts_dir()}", out.res.style("text", True)))
        for name in fonts.FOLDERS:
            entries = listing.get(name, [])
            print(f"  {out.ansi.fg(name, 'gold'):<10} {len(entries)} police(s)")
            for e in entries[:8]:
                print(out.ansi.dim(f"      {e['family']}  ({Path(e['file']).name})"))
            if len(entries) > 8:
                print(out.ansi.dim(f"      … {len(entries) - 8} autre(s)"))
        if problems:
            for p in problems:
                print(out.ansi.fg(f"  ! {p}", "orange"))
        else:
            print(out.ansi.dim("  polices livrées : licence + sha256 OK (≤ 2 Mo)"))
        print(out.ansi.dim(f"  fontconfig : {paths.fontconfig_path()}"
                           f"{'' if paths.fontconfig_path().is_file() else ' (absent : tools/install.sh)'}"))
        return 0
    if sub == "refresh":
        try:
            info = fonts.refresh()
            conf = fonts.write_fontconfig()
        except (fonts.FontError, OSError) as e:
            print(f"{PROG} font refresh : {e}", file=sys.stderr)
            return 1
        recorded = 0
        if paths.manifest_path().is_file():
            from . import installer
            recorded = installer.record_font_entries()
        if as_json:
            print(json.dumps({"links": info["links"], "index": info["index"], "fontconfig": str(conf),
                              "warnings": info["warnings"], "manifestEntries": recorded}, ensure_ascii=False,
                             indent=2))
            return 0
        print(f"{out.ansi.fg(OK_MARK, 'green')} {len(info['links'])} lien(s) système{SEP}"
              f"{info['index']}{SEP}{conf}")
        for w in info["warnings"]:
            print(out.ansi.fg(f"  ! {w}", "orange"))
        return 0
    print(f"usage : {PROG} font list|refresh", file=sys.stderr)
    return 2


# ---------- module ----------

def cmd_module(out: Out, args: list[str], as_json: bool) -> int:
    sub = args[0] if args else "list"
    rest = args[1:]
    if sub == "list":
        rows = custom.load_modules()
        if as_json:
            print(json.dumps([f.to_json() for f in rows], ensure_ascii=False, indent=2))
            return 0
        if not rows:
            print(out.ansi.dim(f"aucun module ({paths.modules_dir()})"))
            return 0
        for f in rows:
            mark = out.ansi.fg(OK_MARK, "green") if f.valid else out.ansi.fg(KO_MARK, "red")
            print(f"{mark} {out.ansi.fg(f.id, 'gold'):<28} {out.ansi.dim(f.label)}"
                  + (out.ansi.dim(f"  ({', '.join(f.screens)})") if f.screens else ""))
            for p in f.problems:
                print(out.ansi.fg(f"    ! {p}", "orange"))
        return 0
    if sub == "validate":
        if not rest:
            print(f"usage : {PROG} module validate <fichier>", file=sys.stderr)
            return 2
        result = custom.test_module(Path(rest[0]))
        if as_json:
            print(json.dumps(result.to_json(), ensure_ascii=False, indent=2))
            return 0 if result.ok else 1
        mark = out.ansi.fg(OK_MARK, "green") if result.ok else out.ansi.fg(KO_MARK, "red")
        print(f"{mark} {rest[0]} ({result.mode}) {out.ansi.dim(result.detail)}")
        return 0 if result.ok else 1
    if sub == "new":
        if not rest:
            print(f"usage : {PROG} module new <x.id> [--label LIBELLÉ]", file=sys.stderr)
            return 2
        label = _pop_opt(rest, "--label")
        try:
            path = custom.new_module(rest[0], label)
        except (custom.CustomError, OSError) as e:
            print(f"{PROG} module new : {e}", file=sys.stderr)
            return 1
        if as_json:
            print(json.dumps({"path": str(path)}, ensure_ascii=False))
        else:
            print(f"{out.ansi.fg(OK_MARK, 'green')} {path}")
            print(out.ansi.dim(f"  valider : {PROG} module validate {path}"))
        return 0
    print(f"usage : {PROG} module list|validate <fichier>|new <id>", file=sys.stderr)
    return 2


# ---------- plugin ----------

def cmd_plugin(out: Out, args: list[str], as_json: bool) -> int:
    sub = args[0] if args else "list"
    rest = args[1:]
    if sub == "list":
        rows = custom.load_plugins()
        enabled = set(appearance.plugins_enabled())
        if as_json:
            print(json.dumps([dict(f.to_json(), enabled=f.id in enabled) for f in rows], ensure_ascii=False,
                             indent=2))
            return 0
        if not rows:
            print(out.ansi.dim(f"aucun plugin ({paths.plugins_dir()})"))
            return 0
        for f in rows:
            mark = out.ansi.fg(OK_MARK, "green") if f.valid else out.ansi.fg(KO_MARK, "red")
            state = "actif" if f.id in enabled else "inactif"
            print(f"{mark} {out.ansi.fg(f.id, 'gold'):<28} {out.ansi.dim(f.label)}  {state}")
            for p in f.problems:
                print(out.ansi.fg(f"    ! {p}", "orange"))
        return 0
    if sub == "new":
        if not rest:
            print(f"usage : {PROG} plugin new <x.id> [--label LIBELLÉ]", file=sys.stderr)
            return 2
        label = _pop_opt(rest, "--label")
        try:
            folder = custom.new_plugin(rest[0], label)
        except (custom.CustomError, OSError) as e:
            print(f"{PROG} plugin new : {e}", file=sys.stderr)
            return 1
        if as_json:
            print(json.dumps({"path": str(folder)}, ensure_ascii=False))
        else:
            print(f"{out.ansi.fg(OK_MARK, 'green')} {folder}")
            print(out.ansi.dim(f"  tester : {PROG} plugin test {rest[0]}"))
        return 0
    if sub == "test":
        if not rest:
            print(f"usage : {PROG} plugin test <id|dossier>", file=sys.stderr)
            return 2
        folder = Path(rest[0]) if Path(rest[0]).is_dir() else paths.plugins_dir() / rest[0]
        result = custom.test_plugin(folder)
        if as_json:
            print(json.dumps(result.to_json(), ensure_ascii=False, indent=2))
            return 0 if result.ok else 1
        mark = out.ansi.fg(OK_MARK, "green") if result.ok else out.ansi.fg(KO_MARK, "red")
        print(f"{mark} {folder.name} ({result.mode}) {out.ansi.dim(result.detail)}")
        return 0 if result.ok else 1
    if sub in ("enable", "disable"):
        if not rest:
            print(f"usage : {PROG} plugin {sub} <id>", file=sys.stderr)
            return 2
        pid = rest[0]
        try:
            if sub == "enable":
                ok, result, msg = custom.enable_plugin(pid)
            else:
                msg = custom.disable_plugin(pid)
                ok, result = True, None
        except (appearance.AppearanceError, settings.SettingsError, custom.CustomError, OSError) as e:
            print(f"{PROG} plugin {sub} : {e}", file=sys.stderr)
            return 1
        if as_json:
            print(json.dumps({"ok": ok, "message": msg,
                              "test": result.to_json() if result else None}, ensure_ascii=False, indent=2))
            return 0 if ok else 1
        mark = out.ansi.fg(OK_MARK, "green") if ok else out.ansi.fg(KO_MARK, "red")
        print(f"{mark} {msg}")
        return 0 if ok else 1
    print(f"usage : {PROG} plugin list|new <id>|test <id>|enable <id>|disable <id>", file=sys.stderr)
    return 2


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
    cmd_banner(out)
    print()
    contract = info["contract"]
    print(f"ultima {VERSION}" + out.ansi.dim(f"{SEP}contrat v{contract}"))
    if info["commit"]:
        print(out.ansi.dim(f"commit {info['commit']}{SEP}installé {info['installedAt']}"))
    return 0


def cmd_banner(out: Out) -> int:
    """Bandeau ULTIMA (repli une ligne si terminal < 52×30 ou locale non UTF-8) ; fin de tools/install.sh."""
    size = shutil.get_terminal_size((80, 24))
    print(banner.render(out.ansi, size.columns, size.lines, res=out.res))
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
