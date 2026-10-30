"""Modèle des réglages de l'overlay — miroir Python de `OverlaySettings` / `ModuleSetting` /
`ModuleIds` / `HudSettings` (Ultima.Engine/Contracts/EngineContracts.cs, contrat v1.3, propriété du chef).

Format du fichier (camelCase, comme System.Text.Json côté daemon — vérifié par une sérialisation .NET
réelle, voir tests/fixtures/settings.v13.default.json) :
    {"modules": [{"id": "deck", "enabled": true, "order": 0}, ...],
     "hud": {"quality": "verre", "heightFraction": 0.55, "restOpacity": 0.62, "side": "right",
             "deckScoreInCombat": false, "gradesOnly": false}}

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
EVENT = "event"   # v1.2
ROUTE = "route"   # v1.2
PARTY = "party"   # v1.2

# OverlaySettings.Default (v1.3) : ordre voulu par Mio, puis modules v1.2 insérés par le chef.
DEFAULT_ORDER: tuple[str, ...] = (
    DECK, ENCHANTED, CURSES, RELICS, POTIONS, REWARD, SHOP, EVENT, PATHS, ROUTE, PARTY, DECK_SCORE,
)

# Écrans nommés du contrat (ModuleSetting.Screens, camelCase de l'enum Screen).
SCREENS: tuple[str, ...] = ("menu", "map", "combat", "reward", "shop", "event", "restSite", "treasure")
_SCREENS_BY_LOWER = {s.lower(): s for s in SCREENS}

# Libellés = titres des blocs du HUD (render.js) en casse de phrase : une seule grammaire (§11).
MODULE_LABELS: dict[str, str] = {
    DECK: "Deck",
    ENCHANTED: "Enchantées",
    CURSES: "Malédictions",
    RELICS: "Reliques",
    POTIONS: "Potions",
    REWARD: "Récompense",
    SHOP: "Shop",
    EVENT: "Événement",
    PATHS: "Chemins",
    ROUTE: "Route",
    PARTY: "Équipe",
    DECK_SCORE: "Note du deck",
}

# Quand le module apparaît (une ligne, affichée en sous-titre dans les réglages).
MODULE_HINTS: dict[str, str] = {
    DECK: "toujours", ENCHANTED: "toujours", CURSES: "toujours", RELICS: "toujours", POTIONS: "toujours",
    REWARD: "écran de récompense", SHOP: "boutique", EVENT: "événement", PATHS: "carte", ROUTE: "carte",
    PARTY: "co-op", DECK_SCORE: "hors combat",
}


def module_label(module_id: str, custom_labels: dict[str, str] | None = None) -> str:
    if custom_labels and module_id in custom_labels:
        return custom_labels[module_id]
    if module_id in MODULE_LABELS:
        return MODULE_LABELS[module_id]
    if module_id.startswith("x."):
        return f"{module_id} (module perso)"
    return f"{module_id} (module inconnu de cette version)"


def module_hint(module_id: str) -> str:
    if module_id.startswith("x."):
        return "module perso ou plugin, dessiné comme les autres blocs"
    return MODULE_HINTS.get(module_id, "")


# ---- HudSettings (v1.3) ---------------------------------------------------------------------------

QUALITIES: tuple[str, ...] = ("eco", "verre", "loupe")   # HudSettings.Qualities
SIDES: tuple[str, ...] = ("right", "left")
QUALITY_LABELS = {"eco": "Éco", "verre": "Verre", "loupe": "Loupe"}
QUALITY_HINTS = {
    "eco": "aplat teinté, coût nul",
    "verre": "flou + teinte + reflet",
    "loupe": "verre + jeu grossi derrière le panneau",
}
SIDE_LABELS = {"left": "Gauche", "right": "Droite"}
# Bornes du contrat v1.4 (docs/44 §3) : la surface produit ET lit toujours dans ces bornes.
HEIGHT_RANGE = (0.30, 0.85)
OPACITY_RANGE = (0.35, 1.00)


def _ci(data: dict, key: str) -> Any:
    """Lecture insensible à la casse, comme le daemon (`PropertyNameCaseInsensitive = true`) :
    la clé exacte (camelCase) d'abord, sinon la première variante de casse."""
    if key in data:
        return data[key]
    low = key.lower()
    for k, v in data.items():
        if isinstance(k, str) and k.lower() == low:
            return v
    return None


def _unknown(data: dict, known: set[str]) -> dict[str, Any]:
    """Clés inconnues conservées ; les variantes de casse d'une clé connue sont lues puis réécrites en
    camelCase, jamais dupliquées (sinon le daemon verrait deux valeurs pour la même propriété)."""
    lows = {k.lower() for k in known}
    return {k: v for k, v in data.items() if not (isinstance(k, str) and k.lower() in lows)}


def _choice(v: Any, allowed: tuple[str, ...], default: str) -> str:
    """Comme `SettingsStore.NormalizeHud` : comparaison en minuscules, valeur écrite en minuscules."""
    if isinstance(v, str) and v.lower() in allowed:
        return v.lower()
    return default


def _fraction(v: Any, default: float, bounds: tuple[float, float]) -> float:
    """Nombre dans les bornes du contrat (arrondi 2 décimales) ; sinon valeur par défaut."""
    lo, hi = bounds
    if isinstance(v, bool) or not isinstance(v, (int, float)):
        return default
    v = float(v)
    if v != v:  # NaN
        return default
    return round(min(hi, max(lo, v)), 2)


HUD_KEYS = {"quality", "heightFraction", "restOpacity", "side", "deckScoreInCombat", "gradesOnly"}


@dataclass
class HudSettings:
    quality: str = "verre"
    height_fraction: float = 0.55
    rest_opacity: float = 0.62
    side: str = "right"
    deck_score_in_combat: bool = False
    grades_only: bool = False
    extra: dict[str, Any] = field(default_factory=dict)   # clés futures conservées

    @classmethod
    def default(cls) -> "HudSettings":
        return cls()

    @classmethod
    def from_json(cls, data: Any) -> "HudSettings":
        """Tolérant : objet absent/invalide → défaut ; champ invalide → sa valeur par défaut."""
        d = cls()
        if not isinstance(data, dict):
            return d
        dsc = _ci(data, "deckScoreInCombat")
        go = _ci(data, "gradesOnly")
        return cls(
            quality=_choice(_ci(data, "quality"), QUALITIES, d.quality),
            height_fraction=_fraction(_ci(data, "heightFraction"), d.height_fraction, HEIGHT_RANGE),
            rest_opacity=_fraction(_ci(data, "restOpacity"), d.rest_opacity, OPACITY_RANGE),
            side=_choice(_ci(data, "side"), SIDES, d.side),
            deck_score_in_combat=dsc if isinstance(dsc, bool) else d.deck_score_in_combat,
            grades_only=go if isinstance(go, bool) else d.grades_only,
            extra=_unknown(data, HUD_KEYS),
        )

    def to_json(self) -> dict[str, Any]:
        # Ordre des clés = ordre des paramètres du record C# HudSettings.
        out: dict[str, Any] = {
            "quality": self.quality,
            "heightFraction": round(float(self.height_fraction), 2),
            "restOpacity": round(float(self.rest_opacity), 2),
            "side": self.side,
            "deckScoreInCombat": self.deck_score_in_combat,
            "gradesOnly": self.grades_only,
        }
        out.update(self.extra)
        return out

    def summary(self) -> str:
        """Une ligne, mêmes mots que la page HUD : « Verre · hauteur 55 % · repos 62 % · droite »
        (+ « note du deck en combat », « lettres seules » seulement s'ils sont activés)."""
        parts = [QUALITY_LABELS.get(self.quality, self.quality),
                 f"hauteur {round(self.height_fraction * 100)} %",
                 f"repos {round(self.rest_opacity * 100)} %",
                 SIDE_LABELS.get(self.side, self.side).lower()]
        if self.deck_score_in_combat:
            parts.append("note du deck en combat")
        if self.grades_only:
            parts.append("lettres seules")
        return " · ".join(parts)

    def set_quality(self, q: str) -> None:
        if q not in QUALITIES:
            raise ValueError(q)
        self.quality = q

    def set_side(self, side: str) -> None:
        if side not in SIDES:
            raise ValueError(side)
        self.side = side

    def set_height(self, v: float) -> None:
        lo, hi = HEIGHT_RANGE
        self.height_fraction = round(min(hi, max(lo, float(v))), 2)

    def set_opacity(self, v: float) -> None:
        lo, hi = OPACITY_RANGE
        self.rest_opacity = round(min(hi, max(lo, float(v))), 2)


@dataclass
class ModuleSetting:
    id: str
    enabled: bool = True
    order: int = 0
    screens: list[str] | None = None       # v1.4 : écrans où le bloc apparaît (None = défaut)
    extra: dict[str, Any] = field(default_factory=dict)   # clés futures conservées

    def to_json(self) -> dict[str, Any]:
        # Ordre des clés = ordre des paramètres du record C# (Id, Enabled, Order, Screens?) ;
        # `screens` n'est écrit QUE s'il est présent (défaut du contrat = absent).
        out: dict[str, Any] = {"id": self.id, "enabled": self.enabled, "order": self.order}
        if self.screens is not None:
            out["screens"] = list(self.screens)
        out.update(self.extra)
        return out


def _screens(v: Any) -> list[str] | None:
    """`screens` v1.4 : écrans connus, casse tolérée, doublons retirés ; absent/invalide → None."""
    if not isinstance(v, list):
        return None
    out: list[str] = []
    for item in v:
        if not isinstance(item, str):
            continue
        canonical = _SCREENS_BY_LOWER.get(item.strip().lower())
        if canonical and canonical not in out:
            out.append(canonical)
    return out


def _module_setting(item: dict, index: int) -> ModuleSetting | None:
    mid = _ci(item, "id")
    if not isinstance(mid, str) or not mid:
        return None
    enabled = _ci(item, "enabled")
    if not isinstance(enabled, bool):
        enabled = True                      # v1.4 : `enabled` absent ⇒ true
    order = _ci(item, "order")
    if isinstance(order, bool) or not isinstance(order, int):
        order = index
    return ModuleSetting(mid, enabled, order, _screens(_ci(item, "screens")),
                         _unknown(item, {"id", "enabled", "order", "screens"}))


@dataclass
class OverlaySettings:
    modules: list[ModuleSetting] = field(default_factory=list)
    hud: HudSettings = field(default_factory=HudSettings)
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
        raw = _ci(data, "modules")
        hud = HudSettings.from_json(_ci(data, "hud"))
        extra = _unknown(data, {"modules", "hud"})
        if not isinstance(raw, list):
            s = cls.default()
            s.hud = hud
            s.extra = extra
            return s

        parsed: list[tuple[int, int, ModuleSetting]] = []
        seen: set[str] = set()
        for index, item in enumerate(raw):
            if not isinstance(item, dict):
                continue
            module = _module_setting(item, index)
            if module is None or module.id in seen:
                continue
            seen.add(module.id)
            parsed.append((module.order, index, module))

        parsed.sort(key=lambda t: (t[0], t[1]))
        modules = [m for _, _, m in parsed]
        for mid in DEFAULT_ORDER:  # modules connus absents du fichier → ajoutés, activés, à la fin
            if mid not in seen:
                modules.append(ModuleSetting(mid, True, 0))
        s = cls(modules, hud, extra)
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
        # Ordre des clés = System.Text.Json sur le record C# : Modules puis Hud ; clés futures ensuite.
        out: dict[str, Any] = {"modules": [m.to_json() for m in self.modules], "hud": self.hud.to_json()}
        for k, v in self.extra.items():
            out.setdefault(k, v)
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
        """Modules par défaut (ordre Mio, tout activé). Le HUD et les clés inconnues sont conservés."""
        self.modules = OverlaySettings.default().modules

    def reset_hud(self) -> None:
        self.hud = HudSettings.default()

    def ids(self) -> list[str]:
        return [m.id for m in self.modules]

    def raw_get(self, key: str) -> Any:
        """Clé inconnue conservée (ex. `appearance`, `pluginsEnabled`), lue sans tenir compte de la casse."""
        return _ci(self.extra, key)

    def same_modules(self, other: "OverlaySettings") -> bool:
        return [m.to_json() for m in self.modules] == [m.to_json() for m in other.modules]

    def same_applied(self, other: "OverlaySettings") -> bool:
        """Modules ET HUD identiques (comparaison des réglages publiés par le daemon)."""
        return self.same_modules(other) and self.hud.to_json() == other.hud.to_json()


@dataclass
class LoadResult:
    settings: OverlaySettings
    exists: bool
    error: str | None = None  # message lisible si le fichier existe mais est illisible


class SettingsWriteError(OSError):
    """`settings.json` illisible : on refuse d'écrire plutôt que de perdre les clés inconnues."""


def read_settings_file(path: Path | None = None) -> tuple[dict[str, Any] | None, str | None]:
    """Objet JSON du fichier, et message d'erreur. (None, None) = absent ; (None, msg) = illisible."""
    path = path or paths.settings_path()
    try:
        text = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return None, None
    except OSError as e:
        return None, f"lecture impossible : {e}"
    try:
        data = json.loads(text)
    except (ValueError, RecursionError) as e:
        return None, f"JSON invalide ({e})"
    if not isinstance(data, dict):
        return None, "la racine n'est pas un objet JSON"
    return data, None


def write_atomic(path: Path, text: str) -> Path:
    """Écriture atomique : tmp (même dossier) → fsync → os.replace → fsync du dossier."""
    path.parent.mkdir(parents=True, exist_ok=True)
    data = text.encode("utf-8")
    fd, tmp = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
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


def reload_if_changed(local: OverlaySettings, path: Path | None = None) -> OverlaySettings | None:
    """Le TUI `ultima` écrit le même fichier : si settings.json (valide) diffère de ce que le compagnon
    a en mémoire, renvoie la version du fichier (à adopter avant toute nouvelle écriture), sinon None.
    Fichier absent ou illisible → None : on garde la version en mémoire, on n'écrase rien."""
    loaded = load(path)
    if not loaded.exists or loaded.error is not None:
        return None
    if loaded.settings.to_json() == local.to_json():
        return None
    return loaded.settings


def save(settings: OverlaySettings, path: Path | None = None) -> Path:
    """Écriture des clés du compagnon (`modules`, `hud`) sans toucher aux autres.

    Le fichier est RELU juste avant l'écriture : les clés inconnues (`appearance`, `pluginsEnabled`,
    clés d'une version future) sont conservées telles quelles ; seules `modules` et `hud` changent.
    Un fichier existant mais illisible est REFUSÉ (on ne peut pas préserver ce qu'on ne lit pas).
    Écriture atomique (tmp, fsync, rename, fsync du dossier).
    """
    path = path or paths.settings_path()
    data = settings.to_json()
    fresh, error = read_settings_file(path)
    if error is not None:
        raise SettingsWriteError(f"settings.json illisible ({error}) : rien n'a été écrit")
    if fresh is not None:
        merged: dict[str, Any] = {"modules": data["modules"], "hud": data["hud"]}
        for key, value in fresh.items():
            if isinstance(key, str) and key.lower() in ("modules", "hud"):
                continue          # jamais deux clés pour la même propriété (lecture insensible à la casse)
            merged[key] = value
        data = merged
    write_atomic(path, json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    return path
