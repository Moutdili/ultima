"""Modèle des réglages de l'overlay — miroir Python de `OverlaySettings` / `ModuleSetting` /
`ModuleIds` (Ultima.Engine/Contracts/EngineContracts.cs, contrat v1, propriété du chef).

Format du fichier (camelCase, comme System.Text.Json côté daemon) :
    {"modules": [{"id": "deck", "enabled": true, "order": 0}, ...]}

Lecture tolérante : champs inconnus ignorés, fichier absent/illisible → valeurs par défaut,
modules connus manquants ajoutés à la fin, modules inconnus (version future du daemon) conservés.
Écriture atomique : fichier temporaire dans le même dossier + fsync + os.replace (rename).
"""

from __future__ import annotations

import json
import os
import tempfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from . import paths

# ModuleIds (contrat v1).
DECK = "deck"
ENCHANTED = "enchanted"
CURSES = "curses"
RELICS = "relics"
POTIONS = "potions"
REWARD = "reward"
SHOP = "shop"
PATHS = "paths"
DECK_SCORE = "deck_score"

# OverlaySettings.Default : ordre voulu par Mio (2026-10-29).
DEFAULT_ORDER: tuple[str, ...] = (
    DECK, ENCHANTED, CURSES, RELICS, POTIONS, REWARD, SHOP, PATHS, DECK_SCORE,
)

MODULE_LABELS: dict[str, str] = {
    DECK: "Deck (avec score)",
    ENCHANTED: "Cartes enchantées",
    CURSES: "Malédictions",
    RELICS: "Reliques (avec score)",
    POTIONS: "Potions",
    REWARD: "Récompense (écran de récompense)",
    SHOP: "Boutique (écran du shop)",
    PATHS: "Chemins (écran de la carte)",
    DECK_SCORE: "Note du deck",
}


def module_label(module_id: str) -> str:
    return MODULE_LABELS.get(module_id, f"{module_id} (module inconnu de cette version)")


@dataclass
class ModuleSetting:
    id: str
    enabled: bool = True
    order: int = 0

    def to_json(self) -> dict[str, Any]:
        # Ordre des clés = ordre des paramètres du record C# (Id, Enabled, Order).
        return {"id": self.id, "enabled": self.enabled, "order": self.order}


@dataclass
class OverlaySettings:
    modules: list[ModuleSetting] = field(default_factory=list)
    # Clés de premier niveau inconnues (version future) : conservées telles quelles à l'écriture.
    extra: dict[str, Any] = field(default_factory=dict)

    # ---- construction -------------------------------------------------------------------
    @classmethod
    def default(cls) -> "OverlaySettings":
        return cls([ModuleSetting(mid, True, i) for i, mid in enumerate(DEFAULT_ORDER)])

    @classmethod
    def from_json(cls, data: Any) -> "OverlaySettings":
        """Construit depuis un objet JSON déjà décodé. Ne lève jamais : ce qui est invalide est ignoré."""
        if not isinstance(data, dict):
            return cls.default()
        raw = data.get("modules")
        extra = {k: v for k, v in data.items() if k != "modules"}
        if not isinstance(raw, list):
            s = cls.default()
            s.extra = extra
            return s

        parsed: list[tuple[int, int, ModuleSetting]] = []
        seen: set[str] = set()
        for index, item in enumerate(raw):
            if not isinstance(item, dict):
                continue
            mid = item.get("id")
            if not isinstance(mid, str) or not mid or mid in seen:
                continue
            seen.add(mid)
            enabled = item.get("enabled", True)
            if not isinstance(enabled, bool):
                enabled = True
            order = item.get("order", index)
            if isinstance(order, bool) or not isinstance(order, int):
                order = index
            parsed.append((order, index, ModuleSetting(mid, enabled, order)))

        parsed.sort(key=lambda t: (t[0], t[1]))
        modules = [m for _, _, m in parsed]
        for mid in DEFAULT_ORDER:  # modules connus absents du fichier → ajoutés, activés, à la fin
            if mid not in seen:
                modules.append(ModuleSetting(mid, True, 0))
        s = cls(modules, extra)
        s.renumber()
        return s

    @classmethod
    def from_text(cls, text: str) -> "OverlaySettings":
        try:
            return cls.from_json(json.loads(text))
        except (ValueError, RecursionError):
            return cls.default()

    # ---- sérialisation ------------------------------------------------------------------
    def to_json(self) -> dict[str, Any]:
        out: dict[str, Any] = dict(self.extra)
        out["modules"] = [m.to_json() for m in self.modules]
        return out

    def to_text(self) -> str:
        return json.dumps(self.to_json(), ensure_ascii=False, indent=2) + "\n"

    # ---- opérations de la page Overlay --------------------------------------------------
    def renumber(self) -> None:
        for i, m in enumerate(self.modules):
            m.order = i

    def index_of(self, module_id: str) -> int:
        for i, m in enumerate(self.modules):
            if m.id == module_id:
                return i
        raise KeyError(module_id)

    def move(self, module_id: str, delta: int) -> bool:
        """Déplace un module de `delta` places (−1 = monter). Retourne False si impossible."""
        i = self.index_of(module_id)
        j = i + delta
        if delta == 0 or j < 0 or j >= len(self.modules):
            return False
        m = self.modules.pop(i)
        self.modules.insert(j, m)
        self.renumber()
        return True

    def move_up(self, module_id: str) -> bool:
        return self.move(module_id, -1)

    def move_down(self, module_id: str) -> bool:
        return self.move(module_id, +1)

    def set_enabled(self, module_id: str, enabled: bool) -> None:
        self.modules[self.index_of(module_id)].enabled = bool(enabled)

    def reset(self) -> None:
        """Valeurs par défaut (ordre Mio, tout activé). Les clés inconnues sont conservées."""
        self.modules = OverlaySettings.default().modules

    def ids(self) -> list[str]:
        return [m.id for m in self.modules]

    def same_modules(self, other: "OverlaySettings") -> bool:
        return [m.to_json() for m in self.modules] == [m.to_json() for m in other.modules]


@dataclass
class LoadResult:
    settings: OverlaySettings
    exists: bool
    error: str | None = None  # message lisible si le fichier existe mais est illisible


def load(path: Path | None = None) -> LoadResult:
    path = path or paths.settings_path()
    try:
        text = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return LoadResult(OverlaySettings.default(), exists=False)
    except OSError as e:
        return LoadResult(OverlaySettings.default(), exists=True, error=f"lecture impossible : {e}")
    try:
        data = json.loads(text)
    except (ValueError, RecursionError) as e:
        return LoadResult(OverlaySettings.default(), exists=True,
                          error=f"JSON invalide ({e}) — valeurs par défaut affichées")
    return LoadResult(OverlaySettings.from_json(data), exists=True)


def save(settings: OverlaySettings, path: Path | None = None) -> Path:
    """Écriture atomique : tmp (même dossier) → fsync → os.replace → fsync du dossier."""
    path = path or paths.settings_path()
    path.parent.mkdir(parents=True, exist_ok=True)
    data = settings.to_text().encode("utf-8")
    fd, tmp = tempfile.mkstemp(prefix=".settings.", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(data)
            f.flush()
            os.fsync(f.fileno())
        os.chmod(tmp, 0o644)
        os.replace(tmp, path)
    except BaseException:
        try:
            os.unlink(tmp)
        except FileNotFoundError:
            pass
        raise
    try:
        dfd = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(dfd)
        finally:
            os.close(dfd)
    except OSError:
        pass
    return path
