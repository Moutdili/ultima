"""Chemins XDG. Résolus à chaque appel (jamais figés à l'import) pour que les tests puissent
rediriger XDG_CONFIG_HOME / XDG_RUNTIME_DIR vers des dossiers temporaires."""

from __future__ import annotations

import os
from pathlib import Path


def config_home() -> Path:
    value = os.environ.get("XDG_CONFIG_HOME")
    if value and os.path.isabs(value):
        return Path(value)
    return Path.home() / ".config"


def runtime_dir() -> Path:
    value = os.environ.get("XDG_RUNTIME_DIR")
    if value and os.path.isabs(value):
        return Path(value)
    # Repli documenté : sans XDG_RUNTIME_DIR, /run/user/<uid> (convention systemd).
    return Path(f"/run/user/{os.getuid()}")


def data_home() -> Path:
    value = os.environ.get("XDG_DATA_HOME")
    if value and os.path.isabs(value):
        return Path(value)
    return Path.home() / ".local" / "share"


def settings_path() -> Path:
    """~/.config/ultima/settings.json (écrit par le compagnon, lu par le daemon)."""
    return config_home() / "ultima" / "settings.json"


def user_themes_dir() -> Path:
    """Thèmes utilisateur : ~/.config/ultima/themes/<id>.json (docs/44 §2)."""
    return config_home() / "ultima" / "themes"


def installed_themes_dir() -> Path:
    """Thèmes intégrés installés : ~/.local/share/ultima/themes/ (docs/44 §2)."""
    return data_home() / "ultima" / "themes"


def installed_fonts_dir() -> Path:
    """Polices : ~/.local/share/ultima/fonts/{perso,jeu,base,systeme}/ (docs/44 §5)."""
    return data_home() / "ultima" / "fonts"


def state_path() -> Path:
    """$XDG_RUNTIME_DIR/ultima/state.json (écrit atomiquement par le daemon)."""
    return runtime_dir() / "ultima" / "state.json"


def socket_path() -> Path:
    """$XDG_RUNTIME_DIR/ultima/state.sock (une ligne JSON par état)."""
    return runtime_dir() / "ultima" / "state.sock"
