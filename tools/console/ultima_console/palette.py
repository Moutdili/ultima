"""Palette (StsColors du jeu, skill sts2-design-system §1 et §9) et sortie ANSI de la CLI.

Une seule grammaire pour le HUD, le compagnon et la console : mêmes couleurs S-F, mêmes symboles.
"""

from __future__ import annotations

import os
import re
import sys

# StsColors.cs (MegaCrit) + teinte du verre (docs/17 §3) + gris « non calculé » (§9).
PALETTE: dict[str, str] = {
    "cream": "#FFF6E2",
    "gold": "#EFC851",
    "red": "#FF5555",
    "green": "#7FFF00",
    "blue": "#87CEEB",
    "orange": "#FFA518",
    "purple": "#EE82EE",
    "aqua": "#2AEBBE",
    "lightGray": "#BFBFBF",
    "gray": "#808080",
    "glass": "#192930",
}

# §9 : S gold · A green · B aqua · C blue · D orange · F red · « — » gray.
GRADE_TONES: dict[str, str] = {
    "S": "gold", "A": "green", "B": "aqua", "C": "blue", "D": "orange", "F": "red", "—": "gray",
}

# Symboles partagés avec le HUD (overlay/gnome/ultima@mio/render.js).
DASH = "—"          # score non calculé (jamais 0)
SEP = " · "
TIMES = "×"
CURSE_MARK = "!!"
OK_MARK = "✓"
KO_MARK = "✗"
WARN_MARK = "!"

_HEX = re.compile(r"^#[0-9a-fA-F]{6}$")


def resolve(color: str | None, fallback: str = "cream") -> str:
    """Token de palette ou #RRGGBB → #RRGGBB. Inconnu → fallback (jamais une erreur)."""
    if isinstance(color, str):
        if color in PALETTE:
            return PALETTE[color]
        if _HEX.match(color):
            return color.upper()
    return PALETTE.get(fallback, PALETTE["cream"])


def is_color(color: object) -> bool:
    return isinstance(color, str) and (color in PALETTE or bool(_HEX.match(color)))


def grade_tone(grade: str | None) -> str:
    return GRADE_TONES.get(grade or DASH, "gray")


# ---------- ANSI (CLI) ----------

def color_enabled(stream=None, mode: str = "auto") -> bool:
    """auto : couleur si TTY, NO_COLOR absent et TERM ≠ dumb. FORCE_COLOR force."""
    if mode == "never":
        return False
    if mode == "always":
        return True
    if os.environ.get("NO_COLOR", "") != "":
        return False
    if os.environ.get("FORCE_COLOR", "") not in ("", "0"):
        return True
    if os.environ.get("TERM") == "dumb":
        return False
    stream = stream or sys.stdout
    try:
        return stream.isatty()
    except (AttributeError, ValueError):
        return False


class Ansi:
    """Petit pinceau ANSI 24 bits ; no-op quand la couleur est désactivée."""

    def __init__(self, enabled: bool):
        self.enabled = enabled

    def fg(self, text: str, color: str | None, bold: bool = False, dim: bool = False) -> str:
        if not self.enabled or not text:
            return text
        h = resolve(color)
        r, g, b = int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16)
        codes = [f"38;2;{r};{g};{b}"]
        if bold:
            codes.insert(0, "1")
        if dim:
            codes.insert(0, "2")
        return f"\x1b[{';'.join(codes)}m{text}\x1b[0m"

    def dim(self, text: str) -> str:
        """Texte secondaire : crème 60 % (§11) ≈ gris clair."""
        return self.fg(text, "lightGray") if self.enabled else text

    def grade(self, g: str | None) -> str:
        g = g or DASH
        return self.fg(g, grade_tone(g), bold=g != DASH)
