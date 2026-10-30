"""Apparence du compagnon (docs/44 §3) — modèle pur, sans GTK.

- lit `settings.json` → `appearance.companion` (thème, police, taille, surcharges de tons) ;
- résout le thème effectif (`themes.py`) et la police effective (`fonts.py`) ;
- écrit **uniquement** `appearance.companion` : le fichier est relu avant l'écriture, `appearance.hud`,
  `appearance.tui`, `pluginsEnabled` et les clés inconnues sont conservées, écriture atomique ;
- expose les autres surfaces (HUD, TUI) en LECTURE avec la commande `ultima` équivalente.

Aucune couleur ici : les tons sont des noms, les couleurs viennent des fichiers de thème.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from . import fonts, paths, settings as settings_mod, themes, tones

MIN_FONT_SIZE = 12
MAX_FONT_SIZE = 24
DEFAULT_FONT_SIZE = 15
DEFAULT_TUI_THEME = "terminal"


class AppearanceWriteError(Exception):
    """`settings.json` illisible : on refuse d'écrire plutôt que de perdre les réglages."""


def clamp_size(value: object) -> int | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    if value != value:                      # NaN
        return None
    return int(min(MAX_FONT_SIZE, max(MIN_FONT_SIZE, round(float(value)))))


@dataclass
class CompanionAppearance:
    """Valeur de `appearance.companion` ; les clés inconnues sont conservées (`extra`)."""

    theme: str = themes.DEFAULT_ID
    font_family: str | None = None
    font_size: int | None = None
    tones: dict[str, str] = field(default_factory=dict)
    extra: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: Any) -> "CompanionAppearance":
        d = cls()
        if not isinstance(data, dict):
            return d
        payload: dict = data
        theme = payload.get("theme")
        if isinstance(theme, str) and theme.strip() and len(theme) <= 40:
            d.theme = theme.strip()
        raw_font = payload.get("font")
        font = raw_font if isinstance(raw_font, dict) else {}
        d.font_family = fonts.clean_family(font.get("family"))
        d.font_size = clamp_size(font.get("size"))
        raw_tones = payload.get("tones")
        if isinstance(raw_tones, dict):
            d.tones = {k: v for k, v in raw_tones.items() if isinstance(k, str) and isinstance(v, str)}
        known = {"theme", "font", "tones"}
        d.extra = {k: v for k, v in payload.items() if k not in known}
        return d

    def to_json(self) -> dict[str, Any]:
        font: dict[str, Any] = {}
        if self.font_family:
            font["family"] = self.font_family
        if self.font_size is not None:
            font["size"] = int(self.font_size)
        out: dict[str, Any] = {"theme": self.theme, "font": font, "tones": dict(self.tones)}
        out.update(self.extra)
        return out

    # ---- actions de l'éditeur -------------------------------------------------------------
    def set_theme(self, theme: object) -> None:
        if isinstance(theme, str) and theme.strip() and len(theme.strip()) <= 40:
            self.theme = theme.strip()

    def set_font_family(self, family: object) -> None:
        self.font_family = fonts.clean_family(family)

    def set_font_size(self, size: object) -> None:
        clamped = clamp_size(size)
        self.font_size = clamped

    def set_tone(self, name: str, color: object) -> bool:
        """Surcharge un ton ; couleur invalide ou ton inconnu → refusé (retourne False)."""
        if not tones.is_known_tone(name) or not tones.is_color(color):
            return False
        self.tones[name] = str(color)
        return True

    def clear_tone(self, name: str) -> None:
        self.tones.pop(name, None)


# ---- lecture -----------------------------------------------------------------------------------

def _existing_key(data: dict, key: str) -> str | None:
    """Clé exacte, sinon première variante de casse (le daemon lit sans tenir compte de la casse)."""
    if key in data:
        return key
    low = key.lower()
    for candidate in data:
        if isinstance(candidate, str) and candidate.lower() == low:
            return candidate
    return None


def _get(data: dict, key: str) -> Any:
    found = _existing_key(data, key)
    return data[found] if found is not None else None


def _appearance_dict(source: Any) -> dict:
    if isinstance(source, settings_mod.OverlaySettings):
        value = source.raw_get("appearance")
    elif isinstance(source, dict):
        value = _get(source, "appearance")
    elif isinstance(source, (str, Path)):
        data, _error = settings_mod.read_settings_file(Path(source))
        value = _get(data, "appearance") if data else None
    else:
        data, _error = settings_mod.read_settings_file()
        value = _get(data, "appearance") if data else None
    return value if isinstance(value, dict) else {}


def read_companion(source: Any = None) -> CompanionAppearance:
    """`appearance.companion` ; source = OverlaySettings, dict de settings, chemin, ou None (fichier)."""
    return CompanionAppearance.from_json(_get(_appearance_dict(source), "companion"))


def read_hud(source: Any = None) -> dict:
    value = _get(_appearance_dict(source), "hud")
    return value if isinstance(value, dict) else {}


def read_tui(source: Any = None) -> dict:
    value = _get(_appearance_dict(source), "tui")
    return value if isinstance(value, dict) else {}


# ---- résolution thème + police -------------------------------------------------------------------

@dataclass
class ResolvedAppearance:
    requested_theme: str
    theme_id: str
    theme_label: str
    tones: dict[str, str]
    glass_tint: str | None
    glass_opacity: float
    font_family: str | None
    font_css: str
    font_size: int
    font_source: str
    font_file: Path | None
    font_message: str | None
    fallback: bool
    messages: list[str] = field(default_factory=list)   # visibles (repli de thème)
    warnings: list[str] = field(default_factory=list)

    @property
    def message(self) -> str | None:
        return " · ".join(self.messages) if self.messages else None

    @property
    def font_summary(self) -> str:
        family = self.font_family or "police système"
        return f"{family} {self.font_size} px ({self.font_source})"


def resolve(app: CompanionAppearance | None = None, run_character: str | None = None,
            dirs: list[Path] | None = None, catalog: themes.Catalog | None = None,
            font_root: Path | None = None, use_fontconfig: bool = True,
            fc_families: set[str] | None = None) -> ResolvedAppearance:
    """Thème + police effectifs. Ne lève jamais : repli `sts` avec message visible."""
    app = app or CompanionAppearance()
    res = themes.resolve(app.theme, app.tones, run_character, dirs=dirs, catalog=catalog)
    theme_family = res.theme.font_family if res.theme is not None else None
    theme_size = res.theme.font_size if res.theme is not None else None
    font = fonts.resolve_family(app.font_family, theme_family, root_dir=font_root,
                               use_fontconfig=use_fontconfig, fc_families=fc_families)
    size = app.font_size
    if size is None:
        size = clamp_size(theme_size)
    if size is None:
        size = DEFAULT_FONT_SIZE
    warnings = list(res.warnings)
    if font.message:
        warnings.append(font.message)
    return ResolvedAppearance(
        requested_theme=res.requested,
        theme_id=res.effective_id,
        theme_label=res.theme.label if res.theme is not None else "—",
        tones=dict(res.tones),
        glass_tint=res.overrides.get(tones.BACKGROUND) or res.glass_tint,
        glass_opacity=res.glass_opacity,
        font_family=font.family,
        font_css=fonts.css_family(font.family),
        font_size=size,
        font_source=font.source,
        font_file=font.file,
        font_message=font.message,
        fallback=res.fallback,
        messages=list(res.messages),
        warnings=warnings,
    )


# ---- écriture : uniquement `appearance.companion` --------------------------------------------------

def save_companion(app: CompanionAppearance, path: Path | None = None) -> Path:
    """Relit `settings.json`, remplace `appearance.companion`, garde tout le reste, écrit atomiquement.

    Les clés existantes sont réutilisées telles quelles (casse comprise) : jamais deux clés pour la
    même propriété. Un fichier illisible est refusé (aucune écriture).
    """
    path = path or paths.settings_path()
    data, error = settings_mod.read_settings_file(path)
    if error is not None:
        raise AppearanceWriteError(f"settings.json illisible ({error}) : rien n'a été écrit")
    base: dict[str, Any] = dict(data or {})
    appearance_key = _existing_key(base, "appearance") or "appearance"
    raw_appearance = base.get(appearance_key)
    appearance = dict(raw_appearance) if isinstance(raw_appearance, dict) else {}
    companion_key = _existing_key(appearance, "companion") or "companion"
    appearance[companion_key] = app.to_json()
    base[appearance_key] = appearance
    return settings_mod.write_atomic(path, json.dumps(base, ensure_ascii=False, indent=2) + "\n")


# ---- autres surfaces : LECTURE + commande `ultima` équivalente --------------------------------------

@dataclass
class SurfaceInfo:
    key: str                  # hud | tui | compagnon
    title: str
    theme: str
    font: str
    theme_command: str
    font_command: str | None = None
    editable: bool = False    # compagnon : éditable ici ; hud/tui : en lecture seule


def surfaces(source: Any = None) -> list[SurfaceInfo]:
    hud = read_hud(source)
    tui = read_tui(source)
    companion = read_companion(source)

    hud_theme_raw = hud.get("theme")
    hud_theme = hud_theme_raw if isinstance(hud_theme_raw, str) and hud_theme_raw else themes.DEFAULT_ID
    raw_hud_font = hud.get("font")
    hud_font: dict = raw_hud_font if isinstance(raw_hud_font, dict) else {}
    hud_family = fonts.clean_family(hud_font.get("family"))
    hud_size = clamp_size(hud_font.get("size"))
    hud_font_text = " ".join(part for part in (
        hud_family or "police du thème",
        f"{hud_size} px" if hud_size is not None else "",
    ) if part) or "défaut"

    tui_theme_raw = tui.get("theme")
    tui_theme = tui_theme_raw if isinstance(tui_theme_raw, str) and tui_theme_raw else DEFAULT_TUI_THEME
    return [
        SurfaceInfo("hud", "HUD", hud_theme, hud_font_text,
                    f"ultima theme set hud {hud_theme}",
                    f'ultima font set hud "{hud_family or "défaut"}" {hud_size or DEFAULT_FONT_SIZE}'),
        SurfaceInfo("tui", "Console (TUI)", tui_theme, "police du terminal",
                    f"ultima theme set tui {tui_theme}"),
        SurfaceInfo("compagnon", "Compagnon", companion.theme, "éditée ci-dessus",
                    f"ultima theme set compagnon {companion.theme}",
                    "ultima font set compagnon \"<famille>\" <taille>", editable=True),
    ]
