"""Thèmes ULTIMA (docs/44 §4) : chargement, héritage `extends`, tons, contraste.

Un thème associe une couleur à chaque **ton sémantique** (`text`, `muted`, `accent`, `danger`, `ok`,
`warn`, `curse`, `enchant`, `info`, `grade.S` … `grade.F`, plus `background` et `border` réservés aux
thèmes). Les surfaces (TUI, HUD, compagnon) et les modules ne parlent jamais en hex : ils demandent un
ton, le résolveur rend la couleur du thème, puis les surcharges `appearance.<surface>.tones`.

Trois familles de thèmes :
- **intégrés** (`themes/*.json` du dépôt, copiés dans `~/.local/share/ultima/themes/`) : validés par
  `schemas/theme.schema.json` ; un fichier invalide est ignoré en entier, avec sa raison ;
- **utilisateur** (`~/.config/ultima/themes/<id>.json`) : mêmes schémas, ils priment sur un id intégré ;
- **virtuels** : `terminal` (16 couleurs ANSI, aucune couleur 24 bits, fond du terminal respecté ; il ne
  peut pas être un fichier JSON puisque le schéma exige des hex) et `auto-perso` (résolu depuis
  `run.character`, repli `sts`).

Aucun hexadécimal dans ce module : les seules couleurs sont dans `themes/`.
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path

from . import paths, schema

TONES: tuple[str, ...] = (
    "text", "muted", "accent", "danger", "ok", "warn", "curse", "enchant", "info",
    "grade.S", "grade.A", "grade.B", "grade.C", "grade.D", "grade.F", "background", "border",
)
LOG_KEYS: tuple[str, ...] = ("timestamp", "debug", "info", "warn", "error")
DEFAULT = "sts"
AUTO = "auto-perso"
TERMINAL = "terminal"

#: Les 16 couleurs ANSI nommées (noms Rich : ils sortent en codes 30-37 / 90-97, jamais en 24 bits).
ANSI16: tuple[str, ...] = (
    "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white",
    "bright_black", "bright_red", "bright_green", "bright_yellow",
    "bright_blue", "bright_magenta", "bright_cyan", "bright_white",
)

#: Thème `terminal` : un ton = une des 16 couleurs du terminal, aucun hex, pas de fond imposé.
TERMINAL_TONES: dict[str, str] = {
    "text": "bright_white", "muted": "bright_black", "accent": "bright_yellow", "danger": "red",
    "ok": "green", "warn": "yellow", "curse": "magenta", "enchant": "cyan", "info": "blue",
    "grade.S": "bright_yellow", "grade.A": "bright_green", "grade.B": "bright_cyan",
    "grade.C": "bright_blue", "grade.D": "yellow", "grade.F": "red",
    "background": "", "border": "bright_black",
}
TERMINAL_LOG_COLORS: dict[str, str] = {
    "timestamp": "bright_black", "debug": "bright_black", "info": "bright_white",
    "warn": "yellow", "error": "red",
}

#: Un personnage → son thème (`NameColor` / `EnergyLabelOutlineColor`, skill design §2).
CHARACTER_THEMES: dict[str, str] = {
    "CHARACTER.IRONCLAD": "ironclad", "IRONCLAD": "ironclad", "ironclad": "ironclad",
    "CHARACTER.SILENT": "silent", "SILENT": "silent", "silent": "silent",
    "CHARACTER.DEFECT": "defect", "DEFECT": "defect", "defect": "defect",
    "CHARACTER.REGENT": "regent", "REGENT": "regent", "regent": "regent",
    "CHARACTER.NECROBINDER": "necrobinder", "NECROBINDER": "necrobinder", "necrobinder": "necrobinder",
}

_HEX = re.compile(r"^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")


class ThemeError(Exception):
    pass


def is_hex(value: object) -> bool:
    return isinstance(value, str) and bool(_HEX.match(value))


# ---------- contraste (WCAG 2.1) ----------

def rgb(value: str) -> tuple[int, int, int]:
    h = value.lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


def alpha(value: str) -> float:
    h = value.lstrip("#")
    return int(h[6:8], 16) / 255 if len(h) == 8 else 1.0


def over(color: str, background: str) -> str:
    """`#RRGGBBAA` composé sur un fond opaque → `#RRGGBB` (le contraste se mesure sur le rendu)."""
    if not is_hex(color) or len(color) == 7:
        return color
    a = alpha(color)
    cr, cg, cb = rgb(color)
    br, bg, bb = rgb(background)
    out = [round(c * a + b * (1 - a)) for c, b in ((cr, br), (cg, bg), (cb, bb))]
    return "#{:02X}{:02X}{:02X}".format(*out)


def _luminance(color: str) -> float:
    def channel(c: float) -> float:
        c = c / 255
        return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4

    r, g, b = rgb(color)
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)


def contrast(fg: str, bg: str) -> float:
    """Rapport de contraste WCAG (1.0 … 21.0). `fg` translucide est composé sur `bg`."""
    fg = over(fg, bg)
    a, b = _luminance(fg), _luminance(bg)
    hi, lo = max(a, b), min(a, b)
    return round((hi + 0.05) / (lo + 0.05), 2)


# ---------- modèle ----------

@dataclass
class Theme:
    id: str
    label: str
    api: int = 1
    extends: str | None = None
    tones: dict[str, str] = field(default_factory=dict)
    log_colors: dict[str, str] = field(default_factory=dict)
    glass: dict = field(default_factory=dict)
    font: dict = field(default_factory=dict)
    description: str = ""
    source: str = "builtin"        # builtin | user | virtual
    path: Path | None = None
    raw: dict = field(default_factory=dict)

    @property
    def virtual(self) -> bool:
        return self.source == "virtual"

    def to_json(self) -> dict:
        """Forme canonique (celle du schéma) — un thème utilisateur réécrit reste valide."""
        out: dict = {"id": self.id, "label": self.label, "api": self.api}
        if self.description:
            out["description"] = self.description
        if self.extends:
            out["extends"] = self.extends
        out["tones"] = dict(self.tones)
        if self.log_colors:
            out["logColors"] = dict(self.log_colors)
        if self.glass:
            out["glass"] = dict(self.glass)
        if self.font:
            out["font"] = dict(self.font)
        return out


def _theme_from_json(data: dict, *, source: str, path: Path | None = None) -> Theme:
    return Theme(
        id=str(data.get("id", "")), label=str(data.get("label", "")), api=int(data.get("api", 0)),
        extends=data.get("extends") if isinstance(data.get("extends"), str) else None,
        tones=dict(data.get("tones") or {}),
        log_colors=dict(data.get("logColors") or {}),
        glass=dict(data.get("glass") or {}),
        font=dict(data.get("font") or {}),
        description=str(data.get("description") or ""),
        source=source, path=path, raw=dict(data),
    )


def terminal_theme() -> Theme:
    return Theme(id=TERMINAL, label="Terminal (16 couleurs)", api=1, tones=dict(TERMINAL_TONES),
                 log_colors=dict(TERMINAL_LOG_COLORS), source="virtual", description=(
                     "Les 16 couleurs ANSI du terminal de l'utilisateur, aucune couleur imposée, "
                     "fond du terminal respecté (TUI seulement)."))


def _read(path: Path, source: str, problems: list[str]) -> Theme | None:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError, RecursionError) as e:
        problems.append(f"{path.name} : illisible ({e})")
        return None
    if not isinstance(data, dict):
        problems.append(f"{path.name} : objet JSON attendu")
        return None
    errs = schema.validate(data, schema.load("theme"))
    if errs:
        problems.append(f"{path.name} : " + " ; ".join(errs[:4]) + (" …" if len(errs) > 4 else ""))
        return None
    return _theme_from_json(data, source=source, path=path)


def diagnose() -> tuple[dict[str, Theme], list[str]]:
    """(thèmes valides, problèmes). Un fichier invalide est ignoré en entier, jamais deviné."""
    themes: dict[str, Theme] = {}
    problems: list[str] = []
    themes[TERMINAL] = terminal_theme()
    for directory, source in ((paths.themes_dir(), "builtin"), (paths.user_themes_dir(), "user")):
        if not directory.is_dir():
            continue
        for f in sorted(directory.glob("*.json")):
            t = _read(f, source, problems)
            if t is not None:
                themes[t.id] = t
    return themes, problems


def load_all() -> dict[str, Theme]:
    return diagnose()[0]


def get(theme_id: str) -> Theme | None:
    return load_all().get(theme_id)


def character_theme(character: object) -> str:
    """`run.character` → id de thème ; inconnu ou hors run → `sts` (repli du contrat)."""
    if not isinstance(character, str):
        return DEFAULT
    return CHARACTER_THEMES.get(character) or CHARACTER_THEMES.get(character.upper()) or DEFAULT


def chain(theme_id: str, themes: dict[str, Theme] | None = None) -> list[Theme]:
    """`thème → extends → …` (le plus ancien d'abord) ; cycle ou parent inconnu = arrêt propre."""
    themes = load_all() if themes is None else themes
    out: list[Theme] = []
    seen: set[str] = set()
    cur = themes.get(theme_id)
    while cur is not None and cur.id not in seen:
        seen.add(cur.id)
        out.append(cur)
        cur = themes.get(cur.extends) if cur.extends else None
    out.reverse()
    return out


@dataclass
class Resolution:
    """Résultat prêt à dessiner : couleurs par ton, couleurs du journal, police, fond."""
    requested: str                 # id demandé (peut être auto-perso)
    id: str                        # id effectif
    surface: str
    tones: dict[str, str]
    raw_tones: dict[str, str]
    log_colors: dict[str, str]
    font_family: str | None = None
    font_size: int | None = None
    background: str | None = None
    border: str = ""
    character: str | None = None
    ansi16: bool = False
    problems: list[str] = field(default_factory=list)

    def color(self, tone: str | None, default: str = "text") -> str:
        """Couleur d'un ton ; `perso:<CHARACTER.X>` = accent du thème du personnage."""
        if not tone:
            return self.tones.get(default, "")
        if tone.startswith("perso:"):
            return character_theme_color(tone.split(":", 1)[1], self.tones)
        return self.tones.get(tone) or self.tones.get(default, "")

    def style(self, tone: str | None = None, bold: bool = False) -> str:
        """Style Rich/Textual d'un ton (« dim », « bold <couleur> », « bright_red »)."""
        if tone == "dim":
            return "bold dim" if bold else "dim"
        color = self.color(tone)
        if not color:
            return "bold" if bold else ""
        return f"bold {color}" if bold else color

    def log_style(self, key: str, bold: bool = False) -> str:
        """Style d'une ligne de journal : ton du thème OU couleur #RRGGBB (surcharge)."""
        value = self.log_colors.get(key, "text")
        if is_hex(value):
            return f"bold {value}" if bold else value
        return self.style(value, bold)


def character_theme_color(character: object, fallback: dict[str, str]) -> str:
    themes = load_all()
    t = themes.get(character_theme(character))
    if t is None:
        return fallback.get("accent", "")
    return t.tones.get("accent") or fallback.get("accent", "")


def _merge_tones(theme_id: str, themes: dict[str, Theme]) -> tuple[dict[str, str], list[str], Theme | None]:
    problems: list[str] = []
    tones: dict[str, str] = {}
    provided: set[str] = set()
    parts = chain(theme_id, themes)
    if not parts:
        problems.append(f"thème « {theme_id} » introuvable : repli {DEFAULT}")
        parts = chain(DEFAULT, themes)
    for t in parts:
        for k, v in t.tones.items():
            if isinstance(v, str):
                provided.add(k)          # une valeur vide est un CHOIX (fond du terminal), pas un oubli
                tones[k] = v
    missing = [t for t in TONES if t not in provided]
    if missing:
        problems.append("tons absents : " + ", ".join(missing))
        base = themes.get(DEFAULT)
        for m in missing:
            if base is not None and m in base.tones:
                tones[m] = base.tones[m]
    return tones, problems, (parts[-1] if parts else None)


def resolve(theme_id: str | None, *, surface: str = "tui", overrides: dict | None = None,
            character: str | None = None, log_overrides: dict | None = None,
            font_family: str | None = None, font_size: int | None = None) -> Resolution:
    """Thème → `extends` → tons → surcharges `appearance.<surface>.tones`."""
    themes = load_all()
    requested = theme_id or DEFAULT
    problems: list[str] = []
    effective = requested
    if requested == AUTO:
        effective = character_theme(character)
    if effective not in themes:
        problems.append(f"thème « {effective} » inconnu : repli {DEFAULT}")
        effective = DEFAULT

    tones, more, theme = _merge_tones(effective, themes)
    problems += more
    ansi16 = theme is not None and theme.virtual and theme.id == TERMINAL
    if ansi16:
        # Thème terminal : QUE des noms ANSI, jamais un hex hérité d'un parent.
        tones = {k: (v if v in ANSI16 else TERMINAL_TONES.get(k, "bright_white"))
                 for k, v in tones.items()}
        tones["background"] = ""

    for k, v in (overrides or {}).items():
        if k not in TONES or not isinstance(v, str):
            continue
        if ansi16:                                  # un thème ANSI ne reçoit pas de 24 bits
            continue
        if is_hex(v):
            tones[k] = v.upper()

    # Couleurs du journal : thème, puis `appearance.tui.logColors` (ton ou hex).
    log_colors: dict[str, str] = {}
    for key in LOG_KEYS:
        value = (theme.log_colors.get(key) if theme else None) or "text"
        override = (log_overrides or {}).get(key)
        if isinstance(override, str) and override and not (ansi16 and is_hex(override)):
            value = override
        log_colors[key] = value

    family = font_family or (theme.font.get("family") if theme else None)
    size = font_size if isinstance(font_size, int) and not isinstance(font_size, bool) else None
    raw = dict(tones)
    bg = tones.get("background") or None
    if ansi16:
        bg = None                                   # fond du terminal respecté
    elif bg and not is_hex(bg):
        bg = None
    # Rich ne comprend pas #RRGGBBAA : on affiche la couleur composée sur le fond du thème.
    display = {k: (over(v, bg) if (bg and is_hex(v) and len(v) == 9) else v) for k, v in tones.items()}
    return Resolution(requested=requested, id=effective, surface=surface, tones=display, raw_tones=raw,
                      log_colors=log_colors, font_family=family, font_size=size,
                      background=bg, border=display.get("border", ""), character=character,
                      ansi16=ansi16, problems=problems)


def check(theme_id: str) -> dict:
    """`ultima theme check` : schéma, héritage, tons complets, contraste, couleurs du journal."""
    themes, problems = diagnose()
    out: dict = {"id": theme_id, "ok": True, "problems": [], "contrast": None}
    t = themes.get(theme_id)
    if t is None:
        out["ok"] = False
        out["problems"].append(f"thème « {theme_id} » introuvable")
        return out
    if t.virtual:
        bad = [k for k, v in t.tones.items() if k not in TONES or (v and v not in ANSI16)]
        if bad:
            out["problems"].append("tons ANSI invalides : " + ", ".join(bad))
        out["ansi16"] = True
    else:
        out["path"] = str(t.path)
        errs = schema.validate(t.raw, schema.load("theme"))
        out["problems"] += errs
    tones, more, _ = _merge_tones(theme_id, themes)
    out["problems"] += more
    bg = tones.get("background")
    if bg and not out.get("ansi16"):
        ratio = contrast(tones.get("text", bg), bg)
        out["contrast"] = ratio
        if ratio < 4.5:
            out["problems"].append(f"contraste texte/fond {ratio}:1 < 4,5:1 (thème accepté mais signalé)")
    out["problems"] += [p for p in problems if p.startswith(f"{theme_id}.json")]
    out["ok"] = not any("introuvable" in p for p in out["problems"])
    out["tonal"] = bool(bg) and (out.get("contrast") or 0) >= 4.5
    return out


def list_ids(include_virtual: bool = True) -> list[dict]:
    themes, problems = diagnose()
    out = []
    for tid, t in sorted(themes.items(), key=lambda kv: (kv[1].source != "builtin", kv[0])):
        if t.virtual and not include_virtual:
            continue
        out.append({"id": tid, "label": t.label, "source": t.source,
                    "description": t.description,
                    "extends": t.extends, "font": t.font.get("family")})
    for p in problems:
        out.append({"id": "?", "label": "invalide", "source": "error", "description": p})
    return out
