"""Réglages de l'overlay — miroir Python d'`OverlaySettings` / `ModuleSetting` / `HudSettings`
(Ultima.Engine/Contracts/EngineContracts.cs, contrat v1.3, propriété du chef).

Fichier `~/.config/ultima/settings.json`, forme System.Text.Json camelCase du record C# :

    {"modules": [{"id": "deck", "enabled": true, "order": 0}, ...],
     "hud": {"quality": "verre", "heightFraction": 0.55, "restOpacity": 0.62, "side": "right",
             "deckScoreInCombat": false, "gradesOnly": false}}

Lecture tolérante (consommateur du contrat) : clés inconnues conservées telles quelles (premier niveau,
`hud`, modules d'une version future), valeurs invalides → défaut du contrat, modules connus manquants
ajoutés à la fin (activés), ordre renuméroté 0..n-1. Différence assumée avec le daemon : un module d'id
inconnu (version future) est conservé dans le fichier au lieu d'être supprimé (le daemon l'ignore).
Écriture atomique : fichier temporaire dans le même dossier, fsync, rename(2) (le daemon surveille le
dossier par inotify et regroupe tmp + rename).
"""

from __future__ import annotations

import json
import math
import os
import tempfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from . import paths

# ModuleIds (contrat v1 + v1.2) ; ordre = OverlaySettings.Default (ordre voulu par Mio).
DEFAULT_ORDER: tuple[str, ...] = (
    "deck", "enchanted", "curses", "relics", "potions",
    "reward", "shop", "event", "paths", "route", "party", "deck_score",
)

# Mêmes libellés que les titres du HUD (overlay/gnome/ultima@mio/render.js).
MODULE_LABELS: dict[str, str] = {
    "deck": "Deck",
    "enchanted": "Enchantées",
    "curses": "Malédictions",
    "relics": "Reliques",
    "potions": "Potions",
    "reward": "Récompense",
    "shop": "Shop",
    "event": "Événement",
    "paths": "Chemins",
    "route": "Route",
    "party": "Équipe",
    "deck_score": "Note du deck",
}

# Modules contextuels : affichés seulement sur leur écran (skill sts2-engine-contract).
CONTEXT_SCREEN: dict[str, str] = {
    "reward": "Reward", "shop": "Shop", "event": "Event", "paths": "Map", "route": "Map",
}

QUALITIES: tuple[str, ...] = ("eco", "verre", "loupe")
SIDES: tuple[str, ...] = ("right", "left")
QUALITY_LABELS = {"eco": "eco", "verre": "verre", "loupe": "loupe"}
SIDE_LABELS = {"right": "droite", "left": "gauche"}

# Bornes du contrat v1.3 (HudSettings.HeightMin/HeightMax/OpacityMin/OpacityMax, docs/22) : communes au
# daemon, au HUD, au compagnon et au TUI ; une valeur hors bornes est RAMENÉE à la borne.
HEIGHT_MIN, HEIGHT_MAX, HEIGHT_STEP = 0.30, 0.85, 0.05
OPACITY_MIN, OPACITY_MAX, OPACITY_STEP = 0.35, 1.00, 0.02


def module_label(module_id: str) -> str:
    return MODULE_LABELS.get(module_id, f"{module_id} (inconnu)")


def _num(v: float) -> float:
    """Même écriture que le compagnon (overlay/companion/ultima_companion/settings.py) : 2 décimales."""
    return round(float(v), 2)


@dataclass
class HudSettings:
    quality: str = "verre"
    height_fraction: float = 0.55
    rest_opacity: float = 0.62
    side: str = "right"
    deck_score_in_combat: bool = False
    grades_only: bool = False
    extra: dict[str, Any] = field(default_factory=dict)

    KEYS = ("quality", "heightFraction", "restOpacity", "side", "deckScoreInCombat", "gradesOnly")

    @classmethod
    def from_json(cls, data: Any) -> "HudSettings":
        """Champ par champ (comme SettingsStore.NormalizeHud) : valeur invalide (type, NaN) → SA valeur par
        défaut ; nombre hors bornes du contrat → ramené à la borne ; clés et valeurs texte sans casse."""
        d = cls()
        if not isinstance(data, dict):
            return d
        lower = {k.lower(): k for k in cls.KEYS}
        canon: dict[str, Any] = {}
        d.extra = {}
        for k, v in data.items():
            if isinstance(k, str) and k.lower() in lower:
                canon[lower[k.lower()]] = v
            else:
                d.extra[k] = v
        q = canon.get("quality")
        if isinstance(q, str) and q.lower() in QUALITIES:
            d.quality = q.lower()
        h = canon.get("heightFraction")
        if _is_num(h):
            d.height_fraction = _clamp(h, HEIGHT_MIN, HEIGHT_MAX)
        o = canon.get("restOpacity")
        if _is_num(o):
            d.rest_opacity = _clamp(o, OPACITY_MIN, OPACITY_MAX)
        s = canon.get("side")
        if isinstance(s, str) and s.lower() in SIDES:
            d.side = s.lower()
        if isinstance(canon.get("deckScoreInCombat"), bool):
            d.deck_score_in_combat = canon["deckScoreInCombat"]
        if isinstance(canon.get("gradesOnly"), bool):
            d.grades_only = canon["gradesOnly"]
        return d

    def to_json(self) -> dict[str, Any]:
        # Ordre des clés = ordre des paramètres du record C#.
        out: dict[str, Any] = {
            "quality": self.quality,
            "heightFraction": _num(self.height_fraction),
            "restOpacity": _num(self.rest_opacity),
            "side": self.side,
            "deckScoreInCombat": self.deck_score_in_combat,
            "gradesOnly": self.grades_only,
        }
        out.update(self.extra)
        return out

    # ---- édition (TUI) ----
    def cycle_quality(self, step: int) -> None:
        i = QUALITIES.index(self.quality) if self.quality in QUALITIES else 1
        self.quality = QUALITIES[(i + step) % len(QUALITIES)]

    def cycle_side(self, step: int = 1) -> None:
        self.side = "left" if self.side == "right" else "right"

    def step_height(self, step: int) -> None:
        self.height_fraction = _clamp(self.height_fraction + step * HEIGHT_STEP, HEIGHT_MIN, HEIGHT_MAX)

    def step_opacity(self, step: int) -> None:
        self.rest_opacity = _clamp(self.rest_opacity + step * OPACITY_STEP, OPACITY_MIN, OPACITY_MAX)


def _is_num(v: Any) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def _clamp(v: float, lo: float, hi: float) -> float:
    return round(min(hi, max(lo, v)), 2)


@dataclass
class ModuleSetting:
    id: str
    enabled: bool = True
    order: int = 0
    extra: dict[str, Any] = field(default_factory=dict)

    @property
    def screens(self) -> list[str] | None:
        """Écrans où le bloc apparaît (v1.4, docs/44 §3) ; absent = écrans par défaut du module."""
        value = self.extra.get("screens")
        return [s for s in value if isinstance(s, str)] if isinstance(value, list) else None

    def to_json(self) -> dict[str, Any]:
        out: dict[str, Any] = {"id": self.id, "enabled": self.enabled, "order": self.order}
        out.update(self.extra)
        return out


@dataclass
class OverlaySettings:
    modules: list[ModuleSetting] = field(default_factory=list)
    hud: HudSettings = field(default_factory=HudSettings)
    extra: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def default(cls) -> "OverlaySettings":
        return cls([ModuleSetting(m, True, i) for i, m in enumerate(DEFAULT_ORDER)], HudSettings())

    @classmethod
    def from_json(cls, data: Any) -> "OverlaySettings":
        if not isinstance(data, dict):
            return cls.default()
        extra = {k: v for k, v in data.items() if k.lower() not in ("modules", "hud")}
        hud_raw = next((v for k, v in data.items() if k.lower() == "hud"), None)
        raw = next((v for k, v in data.items() if k.lower() == "modules"), None)
        hud = HudSettings.from_json(hud_raw)
        if not isinstance(raw, list):
            s = cls.default()
            s.hud, s.extra = hud, extra
            return s
        parsed: list[tuple[int, int, ModuleSetting]] = []
        seen: set[str] = set()
        for index, item in enumerate(raw):
            if not isinstance(item, dict):
                continue
            low = {k.lower(): v for k, v in item.items() if isinstance(k, str)}
            mid = low.get("id")
            if not isinstance(mid, str) or not mid or mid in seen:
                continue
            seen.add(mid)
            enabled = low.get("enabled", True)
            if not isinstance(enabled, bool):
                enabled = True
            order = low.get("order", index)
            if isinstance(order, bool) or not isinstance(order, int):
                order = index
            mextra = {k: v for k, v in item.items() if not (isinstance(k, str) and k.lower() in ("id", "enabled", "order"))}
            parsed.append((order, index, ModuleSetting(mid, enabled, order, mextra)))
        parsed.sort(key=lambda t: (t[0], t[1]))
        modules = [m for _, _, m in parsed]
        for mid in DEFAULT_ORDER:
            if mid not in seen:
                modules.append(ModuleSetting(mid, True, 0))
        s = cls(modules, hud, extra)
        s.renumber()
        return s

    def to_json(self) -> dict[str, Any]:
        out: dict[str, Any] = {"modules": [m.to_json() for m in self.modules], "hud": self.hud.to_json()}
        out.update(self.extra)
        return out

    def to_text(self) -> str:
        return json.dumps(self.to_json(), ensure_ascii=False, indent=2) + "\n"

    # ---- édition ----
    def renumber(self) -> None:
        for i, m in enumerate(self.modules):
            m.order = i

    def index_of(self, module_id: str) -> int:
        for i, m in enumerate(self.modules):
            if m.id == module_id:
                return i
        raise KeyError(module_id)

    def toggle(self, module_id: str) -> None:
        m = self.modules[self.index_of(module_id)]
        m.enabled = not m.enabled

    def move(self, module_id: str, delta: int) -> int:
        i = self.index_of(module_id)
        j = max(0, min(len(self.modules) - 1, i + delta))
        if i != j:
            self.modules.insert(j, self.modules.pop(i))
            self.renumber()
        return j

    def enabled_ids(self) -> list[str]:
        return [m.id for m in self.modules if m.enabled]


class SettingsError(Exception):
    pass


def read_raw(path: Path | None = None) -> tuple[dict[str, Any], str | None]:
    """(contenu JSON, erreur). Absent → ({}, None) ; illisible → ({}, message) — jamais d'écrasement."""
    path = path or paths.settings_path()
    try:
        text = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return {}, None
    except OSError as e:
        return {}, f"settings.json illisible : {e}"
    try:
        data = json.loads(text)
    except (ValueError, RecursionError) as e:
        return {}, f"settings.json invalide : {e}"
    if not isinstance(data, dict):
        return {}, "settings.json : objet JSON attendu"
    return data, None


def _deep_set(target: dict[str, Any], patch: dict[str, Any]) -> None:
    """Applique un patch ; `None` SUPPRIME la clé. Les clés inconnues du fichier sont conservées."""
    for key, value in patch.items():
        if value is None:
            target.pop(key, None)
        elif isinstance(value, dict):
            sub = target.get(key)
            if not isinstance(sub, dict):
                sub = {}
                target[key] = sub
            _deep_set(sub, value)
        else:
            target[key] = value


def write_merge(patch: dict[str, Any], path: Path | None = None) -> Path:
    """RELIT le fichier, applique `patch`, écrit atomiquement. Refuse d'écraser un JSON invalide."""
    path = path or paths.settings_path()
    data, err = read_raw(path)
    if err:
        raise SettingsError(f"{err} — fichier non réécrit (le réparer ou le retirer)")
    _deep_set(data, patch)
    atomic_write_text(path, json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    return path


def patch_module(module_id: str, fields: dict[str, Any], path: Path | None = None) -> Path:
    """Modifie UNIQUEMENT les champs donnés de l'entrée `modules[]` de cet id."""
    path = path or paths.settings_path()
    data, err = read_raw(path)
    if err:
        raise SettingsError(f"{err} — fichier non réécrit")
    modules = data.get("modules")
    if not isinstance(modules, list):
        modules = []
    entry = next((m for m in modules if isinstance(m, dict) and m.get("id") == module_id), None)
    if entry is None:
        entry = {"id": module_id}
        modules.append(entry)
    for key, value in fields.items():
        if value is None:
            entry.pop(key, None)
        else:
            entry[key] = value
    data["modules"] = modules
    atomic_write_text(path, json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    return path


def plugins_enabled(path: Path | None = None) -> list[str]:
    """`pluginsEnabled` (réglage du daemon, docs/44 §7) : ids `x.…` seulement."""
    data, _ = read_raw(path)
    value = data.get("pluginsEnabled")
    return [v for v in value if isinstance(v, str) and v.startswith("x.")] if isinstance(value, list) else []


def load(path: Path | None = None) -> tuple[OverlaySettings, str | None]:
    """(réglages, erreur). Absent → défaut sans erreur ; illisible → défaut + message (le fichier
    n'est réécrit qu'après une modification explicite)."""
    path = path or paths.settings_path()
    try:
        text = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return OverlaySettings.default(), None
    except OSError as e:
        return OverlaySettings.default(), f"settings.json illisible : {e}"
    try:
        data = json.loads(text)
    except (ValueError, RecursionError) as e:
        return OverlaySettings.default(), f"settings.json invalide : {e}"
    if not isinstance(data, dict):
        return OverlaySettings.default(), "settings.json : objet JSON attendu"
    return OverlaySettings.from_json(data), None


def atomic_write_text(path: Path, text: str, mode: int = 0o644) -> None:
    """tmp dans le même dossier + fsync + rename : un lecteur voit l'ancien OU le nouveau fichier."""
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=str(path.parent))
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.write(text)
            f.flush()
            os.fsync(f.fileno())
        os.chmod(tmp, mode)
        os.replace(tmp, path)
    except BaseException:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise
    try:
        dfd = os.open(str(path.parent), os.O_RDONLY)
        try:
            os.fsync(dfd)
        finally:
            os.close(dfd)
    except OSError:
        pass


def save(settings: OverlaySettings, path: Path | None = None) -> Path:
    """RELIT le fichier avant d'écrire, ne remplace que `modules` et `hud` (les clés inconnues —
    `appearance`, `pluginsEnabled`, un champ d'une version future — sont conservées telles quelles)."""
    path = path or paths.settings_path()
    data, err = read_raw(path)
    if err:
        raise SettingsError(f"{err} — fichier non réécrit (le réparer ou le retirer)")
    data["modules"] = [m.to_json() for m in settings.modules]
    data["hud"] = settings.hud.to_json()
    atomic_write_text(path, json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    return path
