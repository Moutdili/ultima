"""Feuille de style GTK4 du compagnon, générée depuis le thème résolu (docs/44 §3-§4).

Aucune couleur n'est écrite ici : tout passe par les **tons** (`tones.py`) que le thème associe à une
couleur. Le CSS ne contient donc que des `rgba(r, g, b, a)` — un test vérifie qu'aucun hexadécimal
n'existe hors des fichiers de thème.

Identité = celle du HUD (skill sts2-design-system §8, §9, §11) : une plaque (« verre »), liseré du
thème, texte du thème, accent réservé au focus et au meilleur choix, lettres S-F en badges colorés,
malédictions en ton `curse` + le mot. Aucune animation en boucle.
"""

from __future__ import annotations

from typing import TYPE_CHECKING

from . import tones
from .tones import GRADE_CLASS, grade_class, tone_class, tone_classes  # noqa: F401  (réexport)

if TYPE_CHECKING:  # pragma: no cover
    from .appearance import ResolvedAppearance

MONO_FONT = '"Source Code Pro", "Adwaita Mono", monospace'
MIN_CHROME_PX = 14      # lisibilité §10 : aucun texte d'interface sous 14 px (hors taille choisie)


def _color(theme_tones: dict[str, str], tone: str, alpha: float = 1.0) -> str:
    value = theme_tones.get(tone)
    out = tones.rgba(value, alpha) if value else None
    if out is None:
        out = tones.rgba(theme_tones.get(tones.TEXT), alpha)
    return out or "rgba(0, 0, 0, 1)"


def _neutral_css(resolved: "ResolvedAppearance") -> str:
    """Aucun thème exploitable : structure et typographie seules, aucune couleur imposée."""
    size = int(resolved.font_size)
    return f"""
window.ultima {{ font-family: {resolved.font_css}; font-size: {size}px; }}
.glass {{ border-radius: 10px; padding: 18px 20px; }}
.heading-page {{ font-weight: bold; font-size: {size + 5}px; }}
.heading-group {{ font-weight: bold; font-size: {size}px; letter-spacing: 0.04em; }}
.mono {{ font-family: {MONO_FONT}; font-feature-settings: "tnum"; }}
.grade {{ font-family: {MONO_FONT}; font-weight: 600; font-size: {max(MIN_CHROME_PX, size)}px;
          min-width: 30px; min-height: 30px; border-radius: 7px; padding: 0; }}
"""


def css(resolved: "ResolvedAppearance", translucent: bool = False) -> str:
    """CSS complet de la surface, couleurs = tons du thème (surcharges déjà appliquées)."""
    t = resolved.tones
    if not t:
        return _neutral_css(resolved)
    size = int(resolved.font_size)
    win_alpha = 0.93 if translucent else 1.0
    tint = resolved.glass_tint or t.get(tones.BACKGROUND) or t[tones.TEXT]

    def c(tone: str, alpha: float = 1.0) -> str:
        return _color(t, tone, alpha)

    parts = [f"""
/* ---- variables libadwaita : les couleurs du thème remplacent celles du bureau ---- */
:root {{
  --window-bg-color: {tones.rgba(tint, win_alpha)};
  --window-fg-color: {c(tones.TEXT)};
  --view-bg-color: {tones.rgba(tint, win_alpha)};
  --view-fg-color: {c(tones.TEXT)};
  --headerbar-bg-color: transparent;
  --headerbar-fg-color: {c(tones.TEXT)};
  --headerbar-backdrop-color: transparent;
  --headerbar-shade-color: {c(tones.ACCENT, 0.10)};
  --headerbar-border-color: {c(tones.BORDER)};
  --card-bg-color: {c(tones.TEXT, 0.035)};
  --card-fg-color: {c(tones.TEXT)};
  --card-shade-color: {tones.rgba(tint, 0.35)};
  --popover-bg-color: {tones.rgba(tint, win_alpha)};
  --popover-fg-color: {c(tones.TEXT)};
  --dialog-bg-color: {tones.rgba(tint, win_alpha)};
  --dialog-fg-color: {c(tones.TEXT)};
  --sidebar-bg-color: {tones.rgba(tint, win_alpha)};
  --sidebar-fg-color: {c(tones.TEXT)};
  --accent-color: {c(tones.ACCENT)};
  --accent-bg-color: {c(tones.ACCENT)};
  --accent-fg-color: {tones.rgba(tint)};
  --destructive-color: {c(tones.DANGER)};
  --destructive-bg-color: {c(tones.DANGER)};
  --destructive-fg-color: {tones.rgba(tint)};
  --success-color: {c(tones.OK)};
  --warning-color: {c(tones.WARN)};
  --error-color: {c(tones.DANGER)};
  --border-color: {c(tones.BORDER)};
  --dim-opacity: 0.6;
}}

window.ultima {{
  background-color: {tones.rgba(tint, win_alpha)};
  background-image: linear-gradient(180deg, {c(tones.TEXT, 0.03)}, transparent 280px);
  color: {c(tones.TEXT)};
  font-family: {resolved.font_css};
  font-size: {size}px;
}}
window.ultima headerbar {{ box-shadow: inset 0 -1px {c(tones.BORDER)}; }}
window.ultima .title {{ font-family: {resolved.font_css}; font-weight: bold; }}
window.ultima .subtitle {{ color: {c(tones.MUTED)}; }}
window.ultima banner > revealer > widget {{ background-color: {c(tones.WARN, 0.16)}; color: {c(tones.TEXT)}; }}
window.ultima viewswitcher button:checked {{ color: {c(tones.ACCENT)}; }}

/* ---- verre : une plaque, liseré du thème, reflet en haut, ombre ---- */
.glass {{
  background-color: {c(tones.TEXT, 0.03)};
  background-image: linear-gradient(180deg, {c(tones.TEXT, 0.05)}, {c(tones.TEXT, 0.0)} 60%);
  border: 1px solid {c(tones.BORDER)};
  border-radius: 10px;
  box-shadow: inset 0 1px {c(tones.TEXT, 0.06)}, 0 6px 18px {tones.rgba(tint, 0.55)};
  padding: 18px 20px;
}}
.glass list.boxed-list, .glass .boxed-list {{ box-shadow: none; background: transparent; }}
list.boxed-list {{
  border: 1px solid {c(tones.BORDER, 0.75)};
  box-shadow: inset 0 1px {c(tones.TEXT, 0.05)}, 0 4px 12px {tones.rgba(tint, 0.35)};
}}
list.boxed-list > row {{ min-height: 52px; }}
.heading-page {{ font-family: {resolved.font_css}; font-weight: bold; font-size: {size + 5}px; }}
.heading-group {{ font-family: {resolved.font_css}; font-weight: bold; font-size: {size}px;
                  color: {c(tones.MUTED)}; letter-spacing: 0.04em; }}
.secondary {{ color: {c(tones.MUTED)}; }}
.mono {{ font-family: {MONO_FONT}; font-feature-settings: "tnum"; }}
.value {{ font-family: {MONO_FONT}; font-size: {max(MIN_CHROME_PX, size - 1)}px; color: {c(tones.TEXT, 0.85)}; }}
.dim {{ opacity: 0.45; }}
.alert {{ color: {c(tones.DANGER)}; font-weight: bold; }}
.word {{ font-weight: bold; }}
.empty {{ color: {c(tones.MUTED)}; padding: 10px 0; }}

/* ---- meilleur choix : focus accent (pas de pulsation, pas de glow) ---- */
row.best, .best {{
  background-color: {c(tones.ACCENT, 0.07)};
  box-shadow: inset 3px 0 {c(tones.ACCENT, 0.85)};
}}

/* ---- pastille / échantillon : la couleur double toujours un mot ---- */
.dot {{ min-width: 9px; min-height: 9px; border-radius: 999px; margin: 0 4px; }}
.swatch {{ min-width: 22px; min-height: 22px; border-radius: 6px; border: 1px solid {c(tones.BORDER)}; }}

/* ---- lettre S-F : toujours écrite, la couleur ne fait que la doubler (§9) ---- */
.grade {{
  font-family: {MONO_FONT};
  font-weight: 600;
  font-size: {max(MIN_CHROME_PX, size)}px;
  min-width: 30px;
  min-height: 30px;
  border-radius: 7px;
  padding: 0;
}}
.grade.big {{ font-size: {max(26, size + 11)}px; min-width: 52px; min-height: 52px; border-radius: 10px; }}
"""]
    for grade in tones.GRADES:
        tone = f"grade.{grade}"
        cls = GRADE_CLASS[grade]
        parts.append(
            f".grade.{cls} {{ color: {c(tone)}; background-color: {c(tone, 0.13)}; "
            f"border: 1px solid {c(tone, 0.45)}; }}\n")
    parts.append(f".grade.grade-none {{ color: {c(tones.MUTED)}; "
                 f"background-color: {c(tones.MUTED, 0.10)}; border: 1px solid {c(tones.BORDER, 0.5)}; }}\n")
    for tone, cls in tone_classes().items():
        parts.append(f".{cls} {{ color: {c(tone)}; }}\n"
                     f".dot.{cls}, .swatch.{cls} {{ background-color: {c(tone)}; }}\n")
    return "".join(parts)
