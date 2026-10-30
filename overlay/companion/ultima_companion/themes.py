"""Résolveur de thèmes (docs/44 §3-§4, contrat v1.4) — pur, sans GTK.

Chaîne : `appearance.<surface>.theme` → `extends` (plusieurs niveaux, cycles refusés) →
`appearance.<surface>.tones` par-dessus. `auto-perso` = thème du personnage de la run en cours,
repli `sts` hors run. Un thème introuvable, illisible, d'une autre API ou sans les tons obligatoires
est refusé **en entier** : on retombe sur `sts` et on renvoie un message lisible (jamais d'exception,
jamais une fenêtre sans couleurs).

Les couleurs ne vivent que dans les fichiers de thème (`themes/*.json`, `~/.config/ultima/themes/`,
`~/.local/share/ultima/themes/`) : ce module ne contient aucun hexadécimal.
"""

from __future__ import annotations

import json
import os
import re
from dataclasses import dataclass, field, replace
from pathlib import Path

from . import paths, tones

DEFAULT_ID = "sts"
AUTO_ID = "auto-perso"
API = 1
MAX_DEPTH = 6
ID_RE = re.compile(r"^[a-z0-9][a-z0-9-]{0,39}$")
FAMILY_MAX = 80
DEFAULT_GLASS_OPACITY = 0.55

CHARACTER_THEME = {
    "IRONCLAD": "ironclad",
    "SILENT": "silent",
    "DEFECT": "defect",
    "REGENT": "regent",
    "NECROBINDER": "necrobinder",
}


class ThemeProblem(Exception):
    """Un thème ne peut pas être chargé ou hérité : message destiné à l'utilisateur."""


@dataclass
class Theme:
    """Un thème chargé, avant fusion de `extends` (les tons absents viennent du parent)."""

    id: str
    label: str
    api: int = API
    tones: dict[str, str] = field(default_factory=dict)
    glass_tint: str | None = None
    glass_opacity: float | None = None
    font_family: str | None = None
    font_size: int | None = None
    description: str = ""
    extends: str | None = None
    path: Path | None = None
    warnings: list[str] = field(default_factory=list)

    def copy(self, **changes) -> "Theme":
        return replace(self, **changes)


@dataclass
class Catalog:
    themes: dict[str, Theme] = field(default_factory=dict)
    problems: dict[str, str] = field(default_factory=dict)
    dirs: list[Path] = field(default_factory=list)

    def get(self, theme_id: str) -> Theme | None:
        return self.themes.get(theme_id)


@dataclass
class Resolution:
    """Thème effectif d'une surface : tons fusionnés + surcharges + messages à afficher."""

    theme: Theme | None
    requested: str
    effective_id: str
    tones: dict[str, str] = field(default_factory=dict)
    glass_tint: str | None = None
    glass_opacity: float = DEFAULT_GLASS_OPACITY
    overrides: dict[str, str] = field(default_factory=dict)
    fallback: bool = False
    messages: list[str] = field(default_factory=list)   # visibles dans l'interface
    warnings: list[str] = field(default_factory=list)   # champs ignorés (page Apparence, doctor)

    @property
    def label(self) -> str:
        return self.theme.label if self.theme is not None else self.effective_id

    def message(self) -> str | None:
        return " · ".join(self.messages) if self.messages else None


# ---- emplacements ------------------------------------------------------------------------------

def repo_themes_dir() -> Path:
    """`themes/` du dépôt (worktree compris) : remontée jusqu'à `schemas/theme.schema.json`."""
    here = Path(__file__).resolve()
    for parent in here.parents:
        if (parent / "schemas" / "theme.schema.json").is_file():
            return parent / "themes"
        if (parent / "themes" / "sts.json").is_file():
            return parent / "themes"
    return here.parents[3] / "themes" if len(here.parents) > 3 else Path("themes")


def default_dirs() -> list[Path]:
    """Ordre de recherche : explicite (tests/dev), utilisateur, installés, dépôt."""
    out: list[Path] = []
    explicit = os.environ.get("ULTIMA_THEMES_DIR")
    if explicit:
        out.append(Path(explicit))
    out += [paths.user_themes_dir(), paths.installed_themes_dir(), repo_themes_dir()]
    seen: set[Path] = set()
    unique: list[Path] = []
    for d in out:
        key = Path(os.path.abspath(d))
        if key not in seen:
            seen.add(key)
            unique.append(d)
    return unique


# ---- chargement ---------------------------------------------------------------------------------

def _family(value: object) -> str | None:
    if not isinstance(value, str):
        return None
    text = value.strip()
    if not text or len(text) > FAMILY_MAX:
        return None
    # Une famille ne doit pas pouvoir injecter de CSS (guillemets, accolades, point-virgule…).
    return text if re.fullmatch(r"[^\"\\{};\n\r]{1,%d}" % FAMILY_MAX, text) else None


def _opacity(value: object) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    v = float(value)
    if v != v or v < 0.0 or v > 1.0:
        return None
    return v


def _load_theme_file(path: Path) -> Theme:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except OSError as e:
        raise ThemeProblem(f"illisible ({e})") from e
    except (ValueError, RecursionError) as e:
        raise ThemeProblem(f"JSON invalide ({e})") from e
    if not isinstance(data, dict):
        raise ThemeProblem("la racine n'est pas un objet JSON")
    payload: dict = data
    tid = payload.get("id")
    if not isinstance(tid, str) or not ID_RE.match(tid):
        raise ThemeProblem(f"id invalide ({tid!r})")
    label = payload.get("label")
    if not isinstance(label, str) or not label.strip() or len(label) > 40:
        raise ThemeProblem("label absent ou invalide")
    api = payload.get("api")
    if api != API:
        raise ThemeProblem(f"API {api!r} inconnue (1 attendue)")
    raw_tones = payload.get("tones", {})
    if not isinstance(raw_tones, dict):
        raise ThemeProblem("tones n'est pas un objet")
    warnings: list[str] = []
    loaded: dict[str, str] = {}
    for name, value in raw_tones.items():
        if name not in tones.ALL:
            warnings.append(f"ton « {name} » ignoré (inconnu)")
            continue
        if not tones.is_color(value):
            warnings.append(f"ton « {name} » ignoré (couleur invalide)")
            continue
        loaded[name] = value
    extends = payload.get("extends")
    if extends is not None and (not isinstance(extends, str) or not ID_RE.match(extends)):
        warnings.append(f"extends « {extends!r} » ignoré (id invalide)")
        extends = None
    glass = payload.get("glass") if isinstance(payload.get("glass"), dict) else {}
    tint = glass.get("tint")
    if tint is not None and not tones.is_color(tint):
        warnings.append("glass.tint ignoré (couleur invalide)")
        tint = None
    opacity = _opacity(glass.get("tintOpacity"))
    font = payload.get("font") if isinstance(payload.get("font"), dict) else {}
    family = _family(font.get("family"))
    size = font.get("size")
    if isinstance(size, bool) or not isinstance(size, int) or not 12 <= size <= 24:
        size = None
    description = payload.get("description")
    return Theme(
        id=tid, label=label.strip(), api=API, tones=loaded, glass_tint=tint,
        glass_opacity=opacity, font_family=family, font_size=size,
        description=description if isinstance(description, str) else "",
        extends=extends, path=path, warnings=warnings,
    )


def load_catalog(dirs: list[Path] | None = None) -> Catalog:
    """Charge tous les `*.json` des dossiers, dans l'ordre (le premier gagne). Ne lève jamais."""
    catalog = Catalog(dirs=list(dirs) if dirs is not None else default_dirs())
    for folder in catalog.dirs:
        try:
            files = sorted(folder.glob("*.json"))
        except OSError:
            continue
        for path in files:
            try:
                theme = _load_theme_file(path)
            except ThemeProblem as e:
                catalog.problems.setdefault(path.stem, str(e))
                continue
            catalog.themes.setdefault(theme.id, theme)
    return catalog


def available(catalog: Catalog) -> list[Theme]:
    return [catalog.themes[tid] for tid in sorted(catalog.themes)]


# ---- héritage -----------------------------------------------------------------------------------

def _chain(theme_id: str, catalog: Catalog) -> Theme:
    """Fusionne `theme_id` et tous ses ancêtres `extends`. Lève ThemeProblem si impossible."""
    if theme_id not in catalog.themes:
        if theme_id in catalog.problems:
            raise ThemeProblem(f"Thème « {theme_id} » : {catalog.problems[theme_id]}")
        raise ThemeProblem(f"Thème « {theme_id} » introuvable")
    chain: list[str] = []
    seen: set[str] = set()
    current = theme_id
    while True:
        if current in seen:
            raise ThemeProblem(f"Thème « {theme_id} » : héritage circulaire "
                               f"({' → '.join(chain + [current])})")
        if len(chain) >= MAX_DEPTH:
            raise ThemeProblem(f"Thème « {theme_id} » : héritage trop profond ({' → '.join(chain)})")
        theme = catalog.themes.get(current)
        if theme is None:
            reason = catalog.problems.get(current, "introuvable")
            raise ThemeProblem(f"Thème « {theme_id} » : extends « {current} » — {reason}")
        seen.add(current)
        chain.append(current)
        if not theme.extends:
            break
        current = theme.extends

    root = catalog.themes[theme_id]
    merged = Theme(id=root.id, label=root.label, api=root.api, description=root.description,
                   extends=root.extends, path=root.path)
    for item in reversed(chain):
        layer = catalog.themes[item]
        merged.tones.update(layer.tones)
        if layer.glass_tint:
            merged.glass_tint = layer.glass_tint
        if layer.glass_opacity is not None:
            merged.glass_opacity = layer.glass_opacity
        if layer.font_family:
            merged.font_family = layer.font_family
        if layer.font_size is not None:
            merged.font_size = layer.font_size
        for warning in layer.warnings:
            if warning not in merged.warnings:
                merged.warnings.append(warning)
    missing = [name for name in tones.ALL if name not in merged.tones]
    if missing:
        raise ThemeProblem(f"Thème « {theme_id} » : tons manquants ({', '.join(missing)})")
    if not merged.glass_tint:
        merged.glass_tint = merged.tones[tones.BACKGROUND]
    if merged.glass_opacity is None:
        merged.glass_opacity = DEFAULT_GLASS_OPACITY
    return merged


def character_theme_id(character_id: str | None) -> str | None:
    """`CHARACTER.IRONCLAD` / `Ironclad` → `ironclad` ; personnage inconnu → None (repli `sts`)."""
    if not character_id or not isinstance(character_id, str):
        return None
    key = character_id.split(".")[-1].strip().upper().replace("_", "").replace("-", "")
    return CHARACTER_THEME.get(key)


def _apply_overrides(theme: Theme, overrides: object) -> tuple[dict[str, str], dict[str, str], list[str]]:
    """Surcharges `appearance.<surface>.tones` : ton inconnu ou couleur invalide → ignorée, signalée."""
    merged = dict(theme.tones)
    applied: dict[str, str] = {}
    warnings: list[str] = []
    if not isinstance(overrides, dict):
        return merged, applied, warnings
    for name, value in overrides.items():
        if not tones.is_known_tone(name):
            warnings.append(f"Tons : « {name} » ignoré (ton inconnu)")
            continue
        if not tones.is_color(value):
            warnings.append(f"Tons : « {name} » ignoré (couleur invalide)")
            continue
        merged[name] = value
        applied[name] = value
    return merged, applied, warnings


# ---- résolution ---------------------------------------------------------------------------------

def resolve(requested: object = None, overrides: object = None, run_character: str | None = None,
            dirs: list[Path] | None = None, catalog: Catalog | None = None) -> Resolution:
    """Thème effectif. Ne lève jamais : un thème cassé donne `sts` + un message visible."""
    cat = catalog if catalog is not None else load_catalog(dirs)
    asked = requested.strip() if isinstance(requested, str) and requested.strip() else DEFAULT_ID
    messages: list[str] = []
    warnings: list[str] = []

    target = asked
    if asked == AUTO_ID:
        target = character_theme_id(run_character) or DEFAULT_ID

    theme: Theme | None = None
    fallback = False
    try:
        theme = _chain(target, cat)
    except ThemeProblem as e:
        fallback = True
        messages.append(f"{e}. Repli sur « {DEFAULT_ID} ».")
        if target != DEFAULT_ID:
            try:
                theme = _chain(DEFAULT_ID, cat)
            except ThemeProblem as e2:
                messages.append(f"Thème « {DEFAULT_ID} » inutilisable : {e2}")
                theme = None
        else:
            theme = None

    if theme is None:
        return Resolution(theme=None, requested=asked, effective_id=DEFAULT_ID, tones={},
                          glass_tint=None, glass_opacity=DEFAULT_GLASS_OPACITY, fallback=True,
                          messages=messages, warnings=warnings)

    merged, applied, override_warnings = _apply_overrides(theme, overrides)
    warnings.extend(theme.warnings)
    warnings.extend(override_warnings)
    if asked == AUTO_ID:
        # Trace utile : auto-perso résolu depuis la run (ou repli hors run) — information, pas alerte.
        source = character_theme_id(run_character)
        warnings.append(f"auto-perso : thème « {theme.id} » ({run_character})." if source
                        else f"auto-perso hors run : repli sur « {DEFAULT_ID} ».")
    return Resolution(theme=theme, requested=asked, effective_id=theme.id, tones=merged,
                      glass_tint=theme.glass_tint, glass_opacity=float(theme.glass_opacity or 0),
                      overrides=applied, fallback=fallback, messages=messages, warnings=warnings)
