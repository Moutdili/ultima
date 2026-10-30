"""Palette du compagnon — SEULE source de couleurs (charte docs/17 §1-§2, skill sts2-design-system §1, §2, §9).

Aucune couleur hors de ce fichier : `theme.py` construit tout le CSS à partir de ces jetons et un test
vérifie qu'aucun autre fichier du compagnon ne contient de code hexadécimal. Mêmes noms que la palette
du HUD (`overlay/gnome/ultima@mio/palette.js`) pour une seule grammaire visuelle (§11).

Source de vérité : game/pck/src/Core/Helpers/StsColors.cs (livré par MegaCrit) + la teinte du verre
#192930 (docs/17 §3) + noir (ombre) et blanc pur (reflet) autorisés par la charte.
"""

from __future__ import annotations

PALETTE: dict[str, str] = {
    "cream": "#FFF6E2",       # StsColors.cream — texte principal
    "gold": "#EFC851",        # StsColors.gold — accent, focus, liseré
    "red": "#FF5555",         # StsColors.red — PV, létal, danger
    "green": "#7FFF00",       # StsColors.green — soin, buff
    "blue": "#87CEEB",        # StsColors.blue — défense
    "orange": "#FFA518",      # StsColors.orange — avertissement
    "purple": "#EE82EE",      # StsColors.purple — debuff, malédiction (+ le mot)
    "aqua": "#2AEBBE",        # StsColors.aqua — énergie, enchantements, valeurs secondaires
    "gray": "#808080",        # StsColors.gray — « — » (non calculé), désactivé
    "lightGray": "#BFBFBF",   # StsColors.lightGray — texte désactivé
    "glassTint": "#192930",   # docs/17 §3 — teinte du verre / fond de fenêtre
    "black": "#000000",       # docs/17 §3 — ombre (#000000 @ 0.35)
    "white": "#FFFFFF",       # docs/17 §2 — blanc pur, reflet spéculaire uniquement
}

# Notes S à F (skill sts2-design-system §9). La lettre est TOUJOURS écrite, jamais la couleur seule.
GRADE_TONES: dict[str, str] = {
    "S": "gold", "A": "green", "B": "aqua", "C": "blue", "D": "orange", "F": "red", "—": "gray",
}

# Couleur d'un joueur = son personnage (docs/17 §1.4, `NameColor`), jamais une couleur arbitraire.
CHARACTER_TONES: dict[str, str] = {
    "IRONCLAD": "red", "SILENT": "green", "DEFECT": "blue", "REGENT": "orange",
    "NECROBINDER": "purple", "DEPRIVED": "gold",
}


def hex_of(token: str) -> str:
    return PALETTE[token]


def rgb(token: str) -> tuple[int, int, int]:
    h = PALETTE[token].lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


def rgba(token: str, alpha: float) -> str:
    """Jeton + alpha → `rgba(r, g, b, a)` (CSS GTK). Alpha borné à [0, 1]."""
    r, g, b = rgb(token)
    a = min(1.0, max(0.0, float(alpha)))
    return f"rgba({r}, {g}, {b}, {a:g})"


def grade_tone(grade: str | None) -> str:
    return GRADE_TONES.get(grade or "—", "gray")


def character_tone(character_id: str | None) -> str | None:
    """`CHARACTER.IRONCLAD` → `red` ; inconnu → None (texte crème, jamais une couleur inventée)."""
    if not character_id:
        return None
    key = character_id.split(".")[-1].upper()
    return CHARACTER_TONES.get(key)
