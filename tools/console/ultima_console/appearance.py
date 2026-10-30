"""`settings.json` → `appearance` (docs/44 §3) : thème, tons, police, position, blocs, plugins.

Une configuration PAR SURFACE (TUI, HUD, compagnon). Écriture conforme à la règle du contrat : tout
écrivain RELIT le fichier juste avant d'écrire, ne modifie que ses clés, CONSERVE les clés inconnues
(à tous les niveaux) et écrit atomiquement (tmp + fsync + rename). Aucune valeur invalide n'est écrite :
un champ hors schéma retombe sur son défaut, champ par champ.

Migration unique (docs/44 §3) : l'ancien `console.json` portait `theme` et `logColors` ; ils sont repris
UNE fois dans `appearance.tui` (thème → `sts`/`terminal`, token de palette → ton sémantique). Le fichier
`console.json` est conservé tel quel ; il ne sert plus qu'au lancement du daemon.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from . import consolecfg, settings, theme

SURFACES: tuple[str, ...] = ("tui", "hud", "companion")
DEFAULT_THEME = theme.DEFAULT
WIDTH_MIN, WIDTH_MAX = 280, 520
SIZE_MIN, SIZE_MAX = 12, 24
ANCHORS: tuple[str, ...] = ("top", "center", "bottom")
SIDES: tuple[str, ...] = ("left", "right")

# Ancien `console.json` → thèmes de v1.4 (migration unique, documentée).
LEGACY_THEMES: dict[str, str] = {
    "ultima": "sts", "ansi-dark": "terminal", "ansi-light": "terminal", "textual-dark": "sts",
    "nord": "ardoise", "gruvbox": "ardoise", "tokyo-night": "nuit",
}
# Ancien token de palette (skills design §1) → ton sémantique v1.4.
LEGACY_TONES: dict[str, str] = {
    "cream": "text", "lightGray": "muted", "gray": "muted", "gold": "accent", "red": "danger",
    "green": "ok", "blue": "info", "orange": "warn", "purple": "curse", "aqua": "enchant",
}


class AppearanceError(Exception):
    pass


def _is_int(v: Any) -> bool:
    return isinstance(v, int) and not isinstance(v, bool)


@dataclass
class Font:
    family: str | None = None
    size: int | None = None
    extra: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: Any) -> "Font":
        f = cls()
        if not isinstance(data, dict):
            return f
        fam = data.get("family")
        if isinstance(fam, str) and 1 <= len(fam) <= 80:
            f.family = fam
        size = data.get("size")
        if _is_int(size) and SIZE_MIN <= size <= SIZE_MAX:
            f.size = size
        f.extra = {k: v for k, v in data.items() if k not in ("family", "size")}
        return f

    def to_json(self) -> dict[str, Any]:
        out: dict[str, Any] = {}
        if self.family:
            out["family"] = self.family
        if self.size is not None:
            out["size"] = self.size
        out.update(self.extra)
        return out


@dataclass
class Position:
    side: str = "right"
    anchor: str = "center"
    offset_x: int = 0
    offset_y: int = 0
    width: int = 360
    extra: dict[str, Any] = field(default_factory=dict)

    KEYS = ("side", "anchor", "offsetX", "offsetY", "width")

    @classmethod
    def from_json(cls, data: Any) -> "Position":
        p = cls()
        if not isinstance(data, dict):
            return p
        side = data.get("side")
        if isinstance(side, str) and side.lower() in SIDES:
            p.side = side.lower()
        anchor = data.get("anchor")
        if isinstance(anchor, str) and anchor.lower() in ANCHORS:
            p.anchor = anchor.lower()
        for key, attr in (("offsetX", "offset_x"), ("offsetY", "offset_y")):
            v = data.get(key)
            if _is_int(v):
                setattr(p, attr, max(-4000, min(4000, v)))
        w = data.get("width")
        if _is_int(w):
            p.width = max(WIDTH_MIN, min(WIDTH_MAX, w))
        p.extra = {k: v for k, v in data.items() if k not in cls.KEYS}
        return p

    def to_json(self) -> dict[str, Any]:
        out: dict[str, Any] = {"side": self.side, "anchor": self.anchor, "offsetX": self.offset_x,
                               "offsetY": self.offset_y, "width": self.width}
        out.update(self.extra)
        return out

    def cycle(self, field_name: str, step: int = 1) -> None:
        if field_name == "side":
            self.side = SIDES[(SIDES.index(self.side) + step) % len(SIDES)]
        elif field_name == "anchor":
            self.anchor = ANCHORS[(ANCHORS.index(self.anchor) + step) % len(ANCHORS)]
        elif field_name == "width":
            self.width = max(WIDTH_MIN, min(WIDTH_MAX, self.width + step * 20))
        elif field_name == "offsetX":
            self.offset_x = max(-4000, min(4000, self.offset_x + step * 10))
        elif field_name == "offsetY":
            self.offset_y = max(-4000, min(4000, self.offset_y + step * 10))


@dataclass
class Surface:
    theme: str = DEFAULT_THEME
    tones: dict[str, str] = field(default_factory=dict)
    log_colors: dict[str, str] = field(default_factory=dict)
    font: Font = field(default_factory=Font)
    position: Position = field(default_factory=Position)
    rail_edge: bool = True
    extra: dict[str, Any] = field(default_factory=dict)

    KEYS = ("theme", "tones", "logColors", "font", "position", "railEdge")

    @classmethod
    def from_json(cls, data: Any) -> "Surface":
        s = cls()
        if not isinstance(data, dict):
            return s
        tid = data.get("theme")
        if isinstance(tid, str) and tid:
            s.theme = tid                      # validé à l'usage : thème inconnu → repli sts
        tones = data.get("tones")
        if isinstance(tones, dict):
            for k, v in tones.items():
                if k in theme.TONES and theme.is_hex(v):
                    s.tones[k] = v.upper()
        logs = data.get("logColors")
        if isinstance(logs, dict):
            for k, v in logs.items():
                if k in theme.LOG_KEYS and isinstance(v, str) and (theme.is_hex(v) or v in theme.TONES):
                    s.log_colors[k] = v
        s.font = Font.from_json(data.get("font"))
        s.position = Position.from_json(data.get("position"))
        if isinstance(data.get("railEdge"), bool):
            s.rail_edge = data["railEdge"]
        s.extra = {k: v for k, v in data.items() if k not in cls.KEYS}
        return s

    def to_json(self) -> dict[str, Any]:
        out: dict[str, Any] = {"theme": self.theme}
        if self.tones:
            out["tones"] = dict(self.tones)
        if self.log_colors:
            out["logColors"] = dict(self.log_colors)
        if self.font.family or self.font.size is not None or self.font.extra:
            out["font"] = self.font.to_json()
        if self.theme:                        # HUD/compagnon : position et liseré éditables
            if self.position.to_json() != Position().to_json() or self.position.extra:
                out["position"] = self.position.to_json()
            if self.rail_edge is not True:
                out["railEdge"] = self.rail_edge
        out.update(self.extra)
        return out

    def clean(self, surface: str) -> dict[str, Any]:
        """Forme écrite sur disque : les clés d'une AUTRE surface ne sont pas inventées."""
        out = self.to_json()
        if surface == "tui":
            out.pop("position", None)
            out.pop("railEdge", None)
            out.pop("font", None)             # sans objet pour le TUI (police du terminal)
        return out


@dataclass
class Appearance:
    tui: Surface = field(default_factory=Surface)
    hud: Surface = field(default_factory=Surface)
    companion: Surface = field(default_factory=Surface)
    extra: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: Any) -> "Appearance":
        a = cls()
        if not isinstance(data, dict):
            return a
        a.tui = Surface.from_json(data.get("tui"))
        a.hud = Surface.from_json(data.get("hud"))
        a.companion = Surface.from_json(data.get("companion"))
        a.extra = {k: v for k, v in data.items() if k not in SURFACES}
        return a

    def surface(self, name: str) -> Surface:
        if name not in SURFACES:
            raise AppearanceError(f"surface inconnue : {name} ({', '.join(SURFACES)})")
        return getattr(self, name)

    def to_json(self) -> dict[str, Any]:
        out: dict[str, Any] = {name: self.surface(name).clean(name) for name in SURFACES}
        out.update(self.extra)
        return out

    def resolve(self, name: str, character: str | None = None) -> theme.Resolution:
        s = self.surface(name)
        return theme.resolve(s.theme, surface=name, overrides=s.tones, character=character,
                             log_overrides=s.log_colors, font_family=s.font.family,
                             font_size=s.font.size)


# ---------- lecture / écriture (relire, ne toucher que ses clés, conserver l'inconnu) ----------

def load(path: Path | None = None) -> tuple[Appearance, str | None]:
    data, err = settings.read_raw(path)
    if err:
        return Appearance(), err
    return Appearance.from_json(data.get("appearance")), None


def save(appearance: Appearance, path: Path | None = None) -> Path:
    """N'écrit QUE la clé `appearance` (les autres clés du fichier sont relues puis conservées)."""
    return settings.write_merge({"appearance": appearance.to_json()}, path)


def _set_surface(surface: str, patch: dict[str, Any], path: Path | None = None) -> Path:
    if surface not in SURFACES:
        raise AppearanceError(f"surface inconnue : {surface}")
    return settings.write_merge({"appearance": {surface: patch}}, path)


def set_theme(surface: str, theme_id: str, path: Path | None = None) -> Path:
    known = theme.load_all()
    if theme_id not in known:
        raise AppearanceError(f"thème « {theme_id} » inconnu (ultima theme list)")
    return _set_surface(surface, {"theme": theme_id}, path)


def set_tone(surface: str, tone: str, value: str | None, path: Path | None = None) -> Path:
    if tone not in theme.TONES:
        raise AppearanceError(f"ton inconnu : {tone}")
    if value is not None and not theme.is_hex(value):
        raise AppearanceError(f"couleur attendue #RRGGBB ou #RRGGBBAA : {value}")
    return _set_surface(surface, {"tones": {tone: value}}, path)


def set_log_color(key: str, value: str | None, path: Path | None = None) -> Path:
    if key not in theme.LOG_KEYS:
        raise AppearanceError(f"clé de journal inconnue : {key}")
    if value is not None and not (theme.is_hex(value) or value in theme.TONES):
        raise AppearanceError(f"ton ou #RRGGBB attendu : {value}")
    return _set_surface("tui", {"logColors": {key: value}}, path)


def set_font(surface: str, family: str | None = None, size: int | None = None,
             path: Path | None = None) -> Path:
    if surface == "tui":
        raise AppearanceError("le TUI utilise la police du terminal (docs/44 §3)")
    patch: dict[str, Any] = {}
    if family is not None:
        patch["family"] = family
    if size is not None:
        if not _is_int(size) or not SIZE_MIN <= size <= SIZE_MAX:
            raise AppearanceError(f"taille hors bornes {SIZE_MIN}–{SIZE_MAX} : {size}")
        patch["size"] = size
    return _set_surface(surface, {"font": patch}, path)


def unset_font(surface: str, field_name: str, path: Path | None = None) -> Path:
    """Retire `family` ou `size` (la surface retombe sur la police du thème)."""
    if field_name not in ("family", "size"):
        raise AppearanceError(f"champ de police inconnu : {field_name}")
    return _set_surface(surface, {"font": {field_name: None}}, path)


def set_position(surface: str = "hud", **fields: Any) -> Path:
    clean = {k: v for k, v in fields.items() if k in Position.KEYS}
    if "width" in clean and _is_int(clean["width"]):
        clean["width"] = max(WIDTH_MIN, min(WIDTH_MAX, clean["width"]))
    return _set_surface(surface, {"position": clean})


def set_rail_edge(enabled: bool, surface: str = "hud") -> Path:
    return _set_surface(surface, {"railEdge": bool(enabled)})


# ---------- plugins et blocs (clés de premier niveau de settings.json) ----------

def plugins_enabled(path: Path | None = None) -> list[str]:
    """Ids `x.…` seulement, sans doublon (un id hors `x.` est refusé par le daemon, docs/44 §7)."""
    data, _ = settings.read_raw(path)
    value = data.get("pluginsEnabled")
    out: list[str] = []
    for v in value if isinstance(value, list) else []:
        if isinstance(v, str) and v.startswith("x.") and v not in out:
            out.append(v)
    return out


def set_plugins_enabled(ids: list[str], path: Path | None = None) -> Path:
    clean = []
    for i in ids:
        if isinstance(i, str) and i.startswith("x.") and i not in clean:
            clean.append(i)
    return settings.write_merge({"pluginsEnabled": clean}, path)


def enable_plugin(plugin_id: str, path: Path | None = None) -> Path:
    ids = plugins_enabled(path)
    if plugin_id not in ids:
        ids.append(plugin_id)
    return set_plugins_enabled(ids, path)


def disable_plugin(plugin_id: str, path: Path | None = None) -> Path:
    return set_plugins_enabled([i for i in plugins_enabled(path) if i != plugin_id], path)


def set_module_screens(module_id: str, screens: list[str] | None, path: Path | None = None) -> Path:
    """`screens` d'un module (docs/44 §3) : ne touche qu'à cette clé de CETTE entrée `modules[]`."""
    data, _ = settings.read_raw(path)
    modules = data.get("modules")
    entry = next((m for m in modules if isinstance(m, dict) and m.get("id") == module_id), None) \
        if isinstance(modules, list) else None
    if entry is None:
        raise AppearanceError(f"module « {module_id} » absent de settings.json")
    return settings.patch_module(module_id, {"screens": screens}, path)


# ---------- migration unique console.json → appearance.tui ----------

def migrate_console(path: Path | None = None, console_path: Path | None = None) -> bool:
    """Reprend `theme`/`logColors` de console.json UNE fois. Rend True si le fichier a changé.

    Ne touche JAMAIS console.json (il est conservé) et ne réécrit rien si `appearance.tui.theme`
    existe déjà (migration déjà faite, ou choix de l'utilisateur)."""
    legacy, _ = consolecfg.load(console_path)
    if not legacy.has_legacy_appearance():
        return False
    data, _ = settings.read_raw(path)
    appearance = data.get("appearance") if isinstance(data.get("appearance"), dict) else {}
    tui = appearance.get("tui") if isinstance(appearance.get("tui"), dict) else {}
    if isinstance(tui.get("theme"), str) and tui["theme"]:
        return False
    patch: dict[str, Any] = {"theme": LEGACY_THEMES.get(legacy.theme, DEFAULT_THEME)}
    logs: dict[str, str] = {}
    for key in legacy.legacy_keys:
        value = legacy.log_colors.get(key)
        if not isinstance(value, str):
            continue
        if theme.is_hex(value) or value in theme.TONES:
            logs[key] = value
        else:
            logs[key] = LEGACY_TONES.get(value, "text")
    if logs and not isinstance(tui.get("logColors"), dict):
        patch["logColors"] = logs
    settings.write_merge({"appearance": {"tui": patch}}, path)
    return True


def summary(appearance: Appearance) -> list[dict[str, Any]]:
    """Vue machine (`--json`) : thème effectif et réglages de chaque surface."""
    out = []
    for name in SURFACES:
        s = appearance.surface(name)
        res = appearance.resolve(name)
        out.append({
            "surface": name, "theme": s.theme, "effective": res.id, "ansi16": res.ansi16,
            "tones": s.tones, "logColors": s.log_colors,
            "font": {"family": s.font.family, "size": s.font.size},
            "position": s.position.to_json() if name == "hud" else None,
            "railEdge": s.rail_edge if name == "hud" else None,
            "problems": res.problems,
        })
    return out


def validate(data: Any) -> list[str]:
    from . import schema
    return schema.validate(data, schema.load("appearance"))
