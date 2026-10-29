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


def settings_path() -> Path:
    """~/.config/ultima/settings.json (écrit par le compagnon, lu par le daemon)."""
    return config_home() / "ultima" / "settings.json"


def state_path() -> Path:
    """$XDG_RUNTIME_DIR/ultima/state.json (écrit atomiquement par le daemon)."""
    return runtime_dir() / "ultima" / "state.json"


def socket_path() -> Path:
    """$XDG_RUNTIME_DIR/ultima/state.sock (une ligne JSON par état)."""
    return runtime_dir() / "ultima" / "state.sock"
