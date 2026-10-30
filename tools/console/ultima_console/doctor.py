"""`ultima doctor` : vérifie l'installation sans rien modifier (lecture seule, aucune écriture).

Statuts : ok, warn (fonctionne en mode réduit), fail (à corriger), info.
"""

from __future__ import annotations

import os
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path

from . import VERSION, appearance, consolecfg, fonts, paths, procs, settings
from .state import ALIVE, probe_socket

OK, WARN, FAIL, INFO = "ok", "warn", "fail", "info"


@dataclass
class Check:
    status: str
    label: str
    detail: str

    def to_json(self) -> dict:
        return {"status": self.status, "label": self.label, "detail": self.detail}


def read_manifest() -> dict | None:
    from .installer import read_manifest as _read
    return _read()


def _dotnet_runtime_ok() -> tuple[bool, str]:
    root = paths.dotnet_root()
    exe = (root / "dotnet") if root else shutil.which("dotnet")
    if not exe or not Path(exe).exists():
        return False, "dotnet introuvable (DOTNET_ROOT ou ~/.dotnet)"
    try:
        r = subprocess.run([str(exe), "--list-runtimes"], capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.TimeoutExpired) as e:
        return False, str(e)
    rts = [ln.split()[1] for ln in r.stdout.splitlines() if ln.startswith("Microsoft.NETCore.App ")]
    ok = any(v.startswith("9.") for v in rts)
    return ok, f"{exe} : Microsoft.NETCore.App {', '.join(rts) or 'aucun'}"


def run_checks(quick: bool = False) -> list[Check]:
    out: list[Check] = []
    home = paths.ultima_home()
    man = read_manifest()
    if man:
        out.append(Check(OK, "installation", f"{home} (version {man.get('version')}, commit {man.get('commit') or '?'})"))
    else:
        out.append(Check(WARN, "installation", f"aucun manifest dans {home} (lancer tools/install.sh)"))
    out.append(Check(INFO, "console", f"ultima {VERSION}"))

    link = paths.bin_dir() / "ultima"
    target = home / "bin" / "ultima"
    if link.is_symlink() and Path(os.path.realpath(link)) == Path(os.path.realpath(target)):
        on_path = shutil.which("ultima")
        st = OK if on_path and Path(os.path.realpath(on_path)) == Path(os.path.realpath(target)) else WARN
        out.append(Check(st, "commande", f"{link} → {target}" + ("" if st == OK else f" (PATH : {on_path or 'absent'})")))
    elif link.exists():
        out.append(Check(WARN, "commande", f"{link} n'est pas le lien de l'installation ({os.path.realpath(link)})"))
    else:
        out.append(Check(FAIL, "commande", f"{link} absent"))

    daemon = paths.find_daemon()
    out.append(Check(OK if daemon else FAIL, "daemon", str(daemon) if daemon else "ultima-daemon introuvable"))
    mirror = paths.mirror_path()
    out.append(Check(OK if mirror.is_file() else WARN, "miroir",
                     str(mirror) if mirror.is_file() else f"{mirror} absent (lecture mémoire indisponible)"))
    if not quick:
        ok, detail = _dotnet_runtime_ok()
        out.append(Check(OK if ok else FAIL, "runtime .NET 9", detail))
    data = paths.data_dir()
    out.append(Check(OK if (data / "meta").is_dir() else WARN, "données du calcul",
                     str(data) if (data / "meta").is_dir() else f"{data}/meta absent (scores « — »)"))
    kb = paths.kb_path()
    out.append(Check(OK if kb.is_file() else WARN, "base FR (kb.json)",
                     f"{kb} ({kb.stat().st_size // 1024} Kio)" if kb.is_file() else f"{kb} absent (noms = identifiants)"))

    vpy = paths.venv_dir() / "bin" / "python3"
    if vpy.exists():
        if quick:
            out.append(Check(OK, "TUI (venv)", str(paths.venv_dir())))
        else:
            r = subprocess.run([str(vpy), "-c", "import textual; print(textual.__version__)"],
                               capture_output=True, text=True)
            out.append(Check(OK if r.returncode == 0 else FAIL, "TUI (Textual)",
                             f"textual {r.stdout.strip()}" if r.returncode == 0 else "textual non importable"))
    else:
        out.append(Check(WARN, "TUI (venv)", f"{paths.venv_dir()} absent : sous-commandes seulement"))

    ext = paths.extension_dir(procs.EXT_UUID)
    out.append(Check(OK if ext.is_dir() else WARN, "extension HUD",
                     f"{ext} (activation : ultima hud on)" if ext.is_dir() else f"{ext} absente"))
    unit = paths.systemd_unit_path()
    out.append(Check(INFO, "service systemd", str(unit) if unit.is_file() else "non installé (facultatif)"))

    try:
        scope = Path("/proc/sys/kernel/yama/ptrace_scope").read_text().strip()
        out.append(Check(OK if scope == "0" else WARN, "ptrace_scope",
                         scope if scope == "0" else f"{scope} (doit valoir 0 pour la lecture mémoire)"))
    except OSError:
        pass

    sock = probe_socket()
    pids = procs.owned_pids()
    out.append(Check(OK if sock == ALIVE else INFO, "daemon en marche",
                     f"socket joignable{(' · pid ' + ' '.join(map(str, pids))) if pids else ''}" if sock == ALIVE
                     else f"non ({paths.socket_path()})"))

    s, err = settings.load()
    out.append(Check(FAIL if err else OK, "settings.json",
                     err or f"{paths.settings_path()} (HUD {s.hud.quality}, {round(s.hud.height_fraction * 100)} %)"))
    app, aerr = appearance.load()
    out.append(Check(FAIL if aerr else OK, "apparence",
                     aerr or f"thème TUI {app.tui.theme} · HUD {app.hud.theme} · "
                             f"compagnon {app.companion.theme}"))
    font_problems = fonts.verify_installed()
    if not paths.fonts_dir().is_dir():
        out.append(Check(INFO, "polices", f"{paths.fonts_dir()} absent (tools/install.sh)"))
    else:
        out.append(Check(WARN if font_problems else OK, "polices",
                         " ; ".join(font_problems[:3]) if font_problems else
                         f"{paths.fonts_dir()} · fontconfig {paths.fontconfig_path()}"))
    _, cerr = consolecfg.load()
    if cerr:
        out.append(Check(WARN, "console.json", cerr))
    elif consolecfg.load()[0].has_legacy_appearance():
        out.append(Check(INFO, "console.json", "ancien theme/logColors : repris une fois dans "
                                               "appearance.tui (migration v1.4)"))

    log = paths.log_path()
    out.append(Check(OK if log.parent.is_dir() else INFO, "journal", str(log)))
    legacy = paths.legacy_log_path()
    if legacy.exists():
        out.append(Check(INFO, "ancien journal", f"{legacy} (plus utilisé, peut être supprimé)"))
    return out


def worst(checks: list[Check]) -> str:
    st = {c.status for c in checks}
    return FAIL if FAIL in st else WARN if WARN in st else OK
