"""Chemins d'ULTIMA, résolus à chaque appel (jamais figés à l'import) : les tests redirigent HOME,
XDG_* et ULTIMA_* vers des dossiers temporaires.

Disposition installée (tools/install.sh) :
    $ULTIMA_HOME (défaut $XDG_DATA_HOME/ultima = ~/.local/share/ultima)
      bin/ultima               lanceur (lien : ~/.local/bin/ultima)
      bin/ultima-daemon        lien -> ../lib/daemon/ultima-daemon
      lib/daemon/              dotnet publish Ultima.Daemon
      lib/mirror/              dotnet publish Ultima.Mirror.Probe (ultima-mirror, trouvé par le daemon en ../mirror)
      lib/console/ultima_console/
      data/                    ULTIMA_DATA (meta/, catalog/, calibration/… ; trouvé par le daemon en remontant)
      kb/kb.json               base FR (tools/kb/build-kb), chemin fixe lu par le daemon
      venv/                    Textual (TUI)
      manifest.json            ce qui a été installé (lu par `ultima uninstall`)
    ~/.local/state/ultima/daemon.log   journal du daemon
    ~/.config/ultima/settings.json     réglages (OverlaySettings + Hud v1.3)
    ~/.config/ultima/console.json      préférences du TUI (thème, couleurs du journal)
"""

from __future__ import annotations

import getpass
import os
import tempfile
from pathlib import Path


def _xdg(var: str, default: Path) -> Path:
    value = os.environ.get(var)
    if value and os.path.isabs(value):
        return Path(value)
    return default


def home() -> Path:
    return Path(os.environ.get("HOME") or Path.home())


def config_home() -> Path:
    return _xdg("XDG_CONFIG_HOME", home() / ".config")


def data_home() -> Path:
    return _xdg("XDG_DATA_HOME", home() / ".local" / "share")


def state_home() -> Path:
    return _xdg("XDG_STATE_HOME", home() / ".local" / "state")


def ultima_home() -> Path:
    """Racine de l'installation (ULTIMA_HOME, sinon $XDG_DATA_HOME/ultima)."""
    value = os.environ.get("ULTIMA_HOME")
    if value and os.path.isabs(value):
        return Path(value)
    return data_home() / "ultima"


def bin_dir() -> Path:
    """Dossier du lien `ultima` (ULTIMA_BIN_DIR, sinon ~/.local/bin)."""
    value = os.environ.get("ULTIMA_BIN_DIR")
    if value and os.path.isabs(value):
        return Path(value)
    return home() / ".local" / "bin"


def manifest_path() -> Path:
    return ultima_home() / "manifest.json"


def repo_root() -> Path | None:
    """Racine du dépôt (contient `schemas/` et `themes/`), trouvée en remontant depuis le paquet.

    Installé, le paquet vit dans `$ULTIMA_HOME/lib/console/ultima_console` : la remontée ne trouve rien,
    et les schémas/thèmes sont alors lus sous `$ULTIMA_HOME` (`share_root`)."""
    for parent in Path(__file__).resolve().parents:
        if (parent / "schemas").is_dir() and (parent / "themes").is_dir():
            return parent
    return None


def share_root() -> Path:
    """Racine des fichiers partagés : schémas, thèmes, polices livrées (dépôt ou installation)."""
    installed = ultima_home()
    if (installed / "schemas").is_dir() or (installed / "themes").is_dir():
        return installed
    root = repo_root()
    return root if root is not None else installed


def schemas_dir() -> Path:
    value = os.environ.get("ULTIMA_SCHEMAS")
    if value and os.path.isabs(value):
        return Path(value)
    return share_root() / "schemas"


def themes_dir() -> Path:
    """Thèmes intégrés (livrés avec le dépôt, copiés dans l'installation)."""
    value = os.environ.get("ULTIMA_THEMES")
    if value and os.path.isabs(value):
        return Path(value)
    return share_root() / "themes"


def user_themes_dir() -> Path:
    return config_dir() / "themes"


def modules_dir() -> Path:
    return config_dir() / "modules"


def plugins_dir() -> Path:
    return ultima_home() / "plugins"


def fonts_dir() -> Path:
    return ultima_home() / "fonts"


def repo_fonts_dir() -> Path:
    """Polices libres livrées DANS le dépôt (`fonts/base`, avec licences et sha256)."""
    root = repo_root()
    return (root if root is not None else share_root()) / "fonts"


def fontconfig_path() -> Path:
    return config_home() / "fontconfig" / "conf.d" / "60-ultima.conf"


def venv_dir() -> Path:
    return ultima_home() / "venv"


def kb_path() -> Path:
    """Chemin FIXE lu par le daemon (Ultima.Knowledge : $XDG_DATA_HOME/ultima/kb/kb.json)."""
    return data_home() / "ultima" / "kb" / "kb.json"


def config_dir() -> Path:
    return config_home() / "ultima"


def settings_path() -> Path:
    return config_dir() / "settings.json"


def console_config_path() -> Path:
    return config_dir() / "console.json"


def log_dir() -> Path:
    return state_home() / "ultima"


def log_path() -> Path:
    value = os.environ.get("ULTIMA_LOG")
    if value and os.path.isabs(value):
        return Path(value)
    return log_dir() / "daemon.log"


def daemon_capture_path() -> Path:
    """stdout/stderr du daemon lancé par `ultima start` (le journal lui-même est daemon.log)."""
    return log_path().parent / "daemon.stderr"


def runtime_dir() -> Path:
    """Même règle que le daemon (StatePublisher.DefaultDir) : $XDG_RUNTIME_DIR/ultima, sinon
    <tmp>/ultima-<utilisateur>."""
    xdg = os.environ.get("XDG_RUNTIME_DIR")
    if xdg and os.path.isabs(xdg) and os.path.isdir(xdg):
        return Path(xdg) / "ultima"
    return Path(tempfile.gettempdir()) / f"ultima-{getpass.getuser()}"


def state_path() -> Path:
    return runtime_dir() / "state.json"


def socket_path() -> Path:
    return runtime_dir() / "state.sock"


def lock_path() -> Path:
    return runtime_dir() / "daemon.lock"


def pid_path() -> Path:
    """PID du daemon lancé par `ultima start` (complément du verrou du daemon)."""
    return runtime_dir() / "console-daemon.pid"


def systemd_unit_path() -> Path:
    return config_home() / "systemd" / "user" / "ultima-daemon.service"


def extension_dir(uuid: str = "ultima@mio") -> Path:
    return data_home() / "gnome-shell" / "extensions" / uuid


def legacy_log_path() -> Path:
    """Ancien journal à la racine de ~ (script ultima d'avant l'installation propre)."""
    return home() / "ultima-daemon.log"


def daemon_candidates() -> list[Path]:
    """Ordre de recherche de l'exécutable du daemon. Rien ne pointe vers un dossier d'agent :
    variable explicite, puis installation."""
    out: list[Path] = []
    env = os.environ.get("ULTIMA_DAEMON")
    if env:
        out.append(Path(env))
    out.append(ultima_home() / "lib" / "daemon" / "ultima-daemon")
    out.append(ultima_home() / "bin" / "ultima-daemon")
    return out


def find_daemon() -> Path | None:
    for c in daemon_candidates():
        if c.is_file() and os.access(c, os.X_OK):
            return c
    return None


def mirror_path() -> Path:
    env = os.environ.get("ULTIMA_MIRROR")
    if env:
        return Path(env)
    return ultima_home() / "lib" / "mirror" / "ultima-mirror"


def data_dir() -> Path:
    env = os.environ.get("ULTIMA_DATA")
    if env:
        return Path(env)
    return ultima_home() / "data"


def dotnet_root() -> Path | None:
    env = os.environ.get("DOTNET_ROOT")
    if env:
        return Path(env)
    cand = home() / ".dotnet"
    return cand if (cand / "dotnet").exists() else None


def daemon_env() -> dict[str, str]:
    """Environnement du daemon : DOTNET_ROOT (runtime .NET de l'utilisateur, binaires
    « framework-dependent »)."""
    env = dict(os.environ)
    root = dotnet_root()
    if root is not None and "DOTNET_ROOT" not in env:
        env["DOTNET_ROOT"] = str(root)
    return env


def private_dir(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True)
    try:
        os.chmod(path, 0o700)
    except OSError:
        pass
    return path
