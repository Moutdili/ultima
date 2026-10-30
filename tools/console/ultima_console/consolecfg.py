"""Préférences de la console : `~/.config/ultima/console.json` (propre au TUI/CLI, hors contrat).

    {"theme": "ultima",
     "logColors": {"timestamp": "gray", "debug": "gray", "info": "cream", "warn": "orange", "error": "red"},
     "daemon": {"mirror": false, "verbose": false, "extraArgs": []}}

Couleurs : un token de la palette du jeu (cream, gold, red, green, blue, orange, purple, aqua, lightGray,
gray) ou « #RRGGBB ». `daemon.mirror` = false reproduit le script `ultima` d'origine (`run --no-mirror`).
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from . import palette, paths
from .settings import atomic_write_text

LEVELS: tuple[str, ...] = ("debug", "info", "warn", "error")
COLOR_KEYS: tuple[str, ...] = ("timestamp",) + LEVELS
DEFAULT_LOG_COLORS: dict[str, str] = {
    "timestamp": "gray", "debug": "gray", "info": "cream", "warn": "orange", "error": "red",
}
# Ordre de rotation proposé dans le TUI (palette du jeu uniquement).
COLOR_CHOICES: tuple[str, ...] = ("cream", "lightGray", "gray", "gold", "aqua", "blue", "green",
                                  "orange", "purple", "red")
# « ultima » = palette du jeu sur la teinte du verre ; « ansi-* » = couleurs du terminal ; autres = Textual.
THEMES: tuple[str, ...] = ("ultima", "ansi-dark", "ansi-light", "textual-dark", "nord", "gruvbox", "tokyo-night")


@dataclass
class ConsoleConfig:
    theme: str = "ultima"
    log_colors: dict[str, str] = field(default_factory=lambda: dict(DEFAULT_LOG_COLORS))
    mirror: bool = False
    verbose: bool = False
    extra_args: list[str] = field(default_factory=list)
    extra: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: Any) -> "ConsoleConfig":
        c = cls()
        if not isinstance(data, dict):
            return c
        if isinstance(data.get("theme"), str) and data["theme"]:
            c.theme = data["theme"]
        lc = data.get("logColors")
        if isinstance(lc, dict):
            for k in COLOR_KEYS:
                if palette.is_color(lc.get(k)):
                    c.log_colors[k] = lc[k]
        d = data.get("daemon")
        if isinstance(d, dict):
            if isinstance(d.get("mirror"), bool):
                c.mirror = d["mirror"]
            if isinstance(d.get("verbose"), bool):
                c.verbose = d["verbose"]
            if isinstance(d.get("extraArgs"), list) and all(isinstance(a, str) for a in d["extraArgs"]):
                c.extra_args = list(d["extraArgs"])
        c.extra = {k: v for k, v in data.items() if k not in ("theme", "logColors", "daemon")}
        return c

    def to_json(self) -> dict[str, Any]:
        out: dict[str, Any] = {
            "theme": self.theme,
            "logColors": {k: self.log_colors.get(k, DEFAULT_LOG_COLORS[k]) for k in COLOR_KEYS},
            "daemon": {"mirror": self.mirror, "verbose": self.verbose, "extraArgs": self.extra_args},
        }
        out.update(self.extra)
        return out

    def color(self, key: str) -> str:
        """#RRGGBB d'une clé (timestamp, debug, info, warn, error)."""
        return palette.resolve(self.log_colors.get(key), DEFAULT_LOG_COLORS.get(key, "cream"))

    def cycle_color(self, key: str, step: int) -> str:
        cur = self.log_colors.get(key, DEFAULT_LOG_COLORS[key])
        choices = list(COLOR_CHOICES)
        i = choices.index(cur) if cur in choices else -1
        self.log_colors[key] = choices[(i + step) % len(choices)]
        return self.log_colors[key]

    def daemon_args(self) -> list[str]:
        args = [] if self.mirror else ["--no-mirror"]
        if self.verbose:
            args.append("--verbose")
        return args + self.extra_args


def load(path: Path | None = None) -> tuple[ConsoleConfig, str | None]:
    path = path or paths.console_config_path()
    try:
        return ConsoleConfig.from_json(json.loads(path.read_text(encoding="utf-8"))), None
    except FileNotFoundError:
        return ConsoleConfig(), None
    except (OSError, ValueError, RecursionError) as e:
        return ConsoleConfig(), f"console.json illisible : {e}"


def save(cfg: ConsoleConfig, path: Path | None = None) -> Path:
    path = path or paths.console_config_path()
    atomic_write_text(path, json.dumps(cfg.to_json(), ensure_ascii=False, indent=2) + "\n")
    return path
