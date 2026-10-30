"""Options de LANCEMENT du daemon : `~/.config/ultima/console.json` (hors contrat).

    {"daemon": {"mirror": false, "verbose": false, "extraArgs": []},
     "theme": "ultima", "logColors": {"info": "cream", …}}   ← v1.3, MIGRÉ une fois dans settings.json

Depuis la v1.4 (docs/44 §3), l'apparence du TUI (thème, couleurs du journal) vit dans
`settings.json → appearance.tui`. Ce fichier ne garde que le lancement du daemon ; ses anciennes clés
`theme`/`logColors` sont seulement LUES pour la migration (`appearance.migrate_console`), conservées
telles quelles à l'écriture, et jamais réutilisées ensuite.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from . import paths, theme
from .settings import atomic_write_text

LEVELS: tuple[str, ...] = ("debug", "info", "warn", "error")
COLOR_KEYS: tuple[str, ...] = ("timestamp",) + LEVELS
# Anciens défauts (v1.3) : conservés pour la migration, plus utilisés pour dessiner.
DEFAULT_LOG_COLORS: dict[str, str] = {
    "timestamp": "muted", "debug": "muted", "info": "text", "warn": "warn", "error": "danger",
}


@dataclass
class ConsoleConfig:
    mirror: bool = False
    verbose: bool = False
    extra_args: list[str] = field(default_factory=list)
    theme: str = "sts"                       # hérité (v1.3), lu pour la migration seulement
    log_colors: dict[str, str] = field(default_factory=dict)
    legacy_theme: bool = False
    legacy_log_colors: bool = False
    legacy_keys: list[str] = field(default_factory=list)
    extra: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: Any) -> "ConsoleConfig":
        c = cls()
        if not isinstance(data, dict):
            return c
        d = data.get("daemon")
        if isinstance(d, dict):
            if isinstance(d.get("mirror"), bool):
                c.mirror = d["mirror"]
            if isinstance(d.get("verbose"), bool):
                c.verbose = d["verbose"]
            if isinstance(d.get("extraArgs"), list) and all(isinstance(a, str) for a in d["extraArgs"]):
                c.extra_args = list(d["extraArgs"])
        if isinstance(data.get("theme"), str) and data["theme"]:
            c.theme = data["theme"]
            c.legacy_theme = True
        c.log_colors = {k: DEFAULT_LOG_COLORS[k] for k in COLOR_KEYS}
        lc = data.get("logColors")
        if isinstance(lc, dict):
            c.legacy_log_colors = True
            for k in COLOR_KEYS:
                if k not in lc:
                    continue                      # ne pas inventer une clé absente (migration fidèle)
                c.legacy_keys.append(k)
                v = lc.get(k)
                if isinstance(v, str) and (theme.is_hex(v) or v in theme.TONES or v in LEGACY_TOKENS):
                    c.log_colors[k] = v
        # Tout ce que ce module ne comprend pas (dont theme/logColors) est conservé tel quel.
        c.extra = {k: v for k, v in data.items() if k != "daemon"}
        return c

    def has_legacy_appearance(self) -> bool:
        return self.legacy_theme or self.legacy_log_colors

    def to_json(self) -> dict[str, Any]:
        out = dict(self.extra)
        out["daemon"] = {"mirror": self.mirror, "verbose": self.verbose, "extraArgs": self.extra_args}
        return out

    def daemon_args(self) -> list[str]:
        args = [] if self.mirror else ["--no-mirror"]
        if self.verbose:
            args.append("--verbose")
        return args + self.extra_args


#: Tokens de la palette v1.3 (skill design §1) encore lus dans console.json pour la migration.
LEGACY_TOKENS: tuple[str, ...] = ("cream", "gold", "red", "green", "blue", "orange", "purple", "aqua",
                                  "lightGray", "gray")


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
