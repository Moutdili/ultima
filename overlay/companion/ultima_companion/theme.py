"""Feuille de style GTK4 du compagnon, générée depuis `palette.py` (pur : aucun import GTK).

Identité = celle du HUD (skill sts2-design-system §3, §8, §9, §11) : fond glassTint, texte crème,
or réservé à l'accent et au focus, « verre » sobre (dégradé très léger, liseré or 1 px à 18 %,
reflet blanc 6 % sur le bord haut, ombre noire 35 %), lettres S-F en badges colorés, malédictions
en violet + le mot. Aucune animation en boucle, aucune couleur hors palette (test : test_theme.py).

Typographie (docs/17 §3) : Kreon (titres, corps), Source Code Pro (lettres, chiffres), chargées depuis
le pck extrait (`fonts.py`), jamais copiées dans le dépôt ; repli Cantarell / monospace.
"""

from __future__ import annotations

from .palette import GRADE_TONES, PALETTE, rgba

BODY_FONT = '"Kreon", "Cantarell", "Adwaita Sans", sans-serif'
MONO_FONT = '"Source Code Pro", "Adwaita Mono", monospace'

# Lettre → classe CSS (« — » n'est pas un identifiant CSS valide).
GRADE_CLASS = {"S": "grade-s", "A": "grade-a", "B": "grade-b", "C": "grade-c", "D": "grade-d",
               "F": "grade-f", "—": "grade-none"}
TONE_CLASS = {tone: f"tone-{tone.lower()}" for tone in PALETTE}


def grade_class(grade: str | None) -> str:
    return GRADE_CLASS.get(grade or "—", "grade-none")


def tone_class(tone: str | None) -> str | None:
    return TONE_CLASS.get(tone) if tone else None


def css(translucent: bool = False) -> str:
    """CSS complet. `translucent` : fenêtre légèrement transparente si le compositeur le permet."""
    c = lambda token, a=1.0: rgba(token, a)  # noqa: E731
    win_alpha = 0.93 if translucent else 1.0
    parts = [f"""
/* ---- variables libadwaita 1.9 : on remplace les couleurs du thème par celles du jeu ---- */
:root {{
  --window-bg-color: {c('glassTint', win_alpha)};
  --window-fg-color: {c('cream')};
  --view-bg-color: {c('glassTint')};
  --view-fg-color: {c('cream')};
  --headerbar-bg-color: transparent;
  --headerbar-fg-color: {c('cream')};
  --headerbar-backdrop-color: transparent;
  --headerbar-shade-color: {c('gold', 0.10)};
  --headerbar-border-color: {c('gold', 0.18)};
  --card-bg-color: {c('white', 0.035)};
  --card-fg-color: {c('cream')};
  --card-shade-color: {c('black', 0.35)};
  --popover-bg-color: {c('glassTint')};
  --popover-fg-color: {c('cream')};
  --dialog-bg-color: {c('glassTint')};
  --dialog-fg-color: {c('cream')};
  --sidebar-bg-color: {c('glassTint')};
  --sidebar-fg-color: {c('cream')};
  --accent-color: {c('gold')};
  --accent-bg-color: {c('gold')};
  --accent-fg-color: {c('glassTint')};
  --destructive-color: {c('red')};
  --destructive-bg-color: {c('red')};
  --destructive-fg-color: {c('glassTint')};
  --success-color: {c('green')};
  --warning-color: {c('orange')};
  --error-color: {c('red')};
  --border-color: {c('gold', 0.18)};
  --dim-opacity: 0.6;
}}

window.ultima {{
  background-color: {c('glassTint', win_alpha)};
  background-image: linear-gradient(180deg, {c('white', 0.035)}, transparent 280px);
  color: {c('cream')};
  font-family: {BODY_FONT};
  font-size: 15px;
}}
window.ultima headerbar {{ box-shadow: inset 0 -1px {c('gold', 0.18)}; }}
window.ultima .title {{ font-family: {BODY_FONT}; font-weight: bold; }}
window.ultima .subtitle {{ color: {c('cream', 0.6)}; }}
window.ultima banner > revealer > widget {{ background-color: {c('orange', 0.16)}; color: {c('cream')}; }}
window.ultima viewswitcher button:checked {{ color: {c('gold')}; }}

/* ---- verre : une plaque, liseré or 1 px, reflet en haut, ombre ---- */
.glass {{
  background-color: {c('white', 0.03)};
  background-image: linear-gradient(180deg, {c('white', 0.05)}, {c('white', 0.0)} 60%);
  border: 1px solid {c('gold', 0.18)};
  border-radius: 10px;
  box-shadow: inset 0 1px {c('white', 0.06)}, 0 6px 18px {c('black', 0.35)};
  padding: 18px 20px;
}}
.glass list.boxed-list, .glass .boxed-list {{ box-shadow: none; background: transparent; }}
list.boxed-list {{
  border: 1px solid {c('gold', 0.12)};
  box-shadow: inset 0 1px {c('white', 0.05)}, 0 4px 12px {c('black', 0.25)};
}}
list.boxed-list > row {{ min-height: 52px; }}
.heading-page {{ font-family: {BODY_FONT}; font-weight: bold; font-size: 20px; }}
.heading-group {{ font-family: {BODY_FONT}; font-weight: bold; font-size: 15px;
                  color: {c('cream', 0.6)}; letter-spacing: 0.04em; }}
.secondary {{ color: {c('cream', 0.6)}; }}
.mono {{ font-family: {MONO_FONT}; font-feature-settings: "tnum"; }}
.value {{ font-family: {MONO_FONT}; font-size: 14px; color: {c('cream', 0.85)}; }}
.dim {{ opacity: 0.45; }}
.alert {{ color: {c('red')}; font-weight: bold; }}
.word {{ font-weight: bold; }}
.empty {{ color: {c('cream', 0.6)}; padding: 10px 0; }}

/* ---- meilleur choix : focus or (pas de pulsation, pas de glow) ---- */
row.best, .best {{
  background-color: {c('gold', 0.07)};
  box-shadow: inset 3px 0 {c('gold', 0.85)};
}}

/* ---- pastille d'état (sources) : la couleur double toujours un mot ---- */
.dot {{ min-width: 9px; min-height: 9px; border-radius: 999px; margin: 0 4px; }}

/* ---- lettre S-F : toujours écrite, la couleur ne fait que la doubler (§9) ---- */
.grade {{
  font-family: {MONO_FONT};
  font-weight: 600;
  font-size: 15px;
  min-width: 30px;
  min-height: 30px;
  border-radius: 7px;
  padding: 0;
}}
.grade.big {{ font-size: 26px; min-width: 52px; min-height: 52px; border-radius: 10px; }}
"""]
    for grade, tone in GRADE_TONES.items():
        cls = GRADE_CLASS[grade]
        parts.append(
            f".grade.{cls} {{ color: {c(tone)}; background-color: {c(tone, 0.13)}; "
            f"border: 1px solid {c(tone, 0.45)}; }}\n")
    for tone, cls in TONE_CLASS.items():
        parts.append(f".{cls} {{ color: {c(tone)}; }}\n.dot.{cls} {{ background-color: {c(tone)}; }}\n")
    return "".join(parts)
