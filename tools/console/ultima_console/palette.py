"""Symboles partagés et sortie ANSI de la CLI — **aucune couleur en dur** (elles vivent dans `themes/`).

Les modules d'affichage ne connaissent que des **tons sémantiques** (`text`, `accent`, `danger`, …, voir
`theme.TONES`) ; `theme.resolve()` rend la couleur du thème choisi. Ce module ne contient donc plus de
palette : seulement des symboles (identiques à ceux du HUD) et de quoi colorer la sortie de la CLI, qui
accepte un `#RRGGBB(AA)` (24 bits) ou un des 16 noms ANSI du thème `terminal`.
"""

from __future__ import annotations

import os
import re
import sys

DASH = "—"          # score non calculé (jamais 0)
SEP = " · "
TIMES = "×"
CURSE_MARK = "!!"
OK_MARK = "✓"
KO_MARK = "✗"
WARN_MARK = "!"

ANSI16: tuple[str, ...] = (
    "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white",
    "bright_black", "bright_red", "bright_green", "bright_yellow",
    "bright_blue", "bright_magenta", "bright_cyan", "bright_white",
)
_HEX = re.compile(r"^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")
_ANSI_CODES = {name: (30 + i if i < 8 else 90 + i - 8) for i, name in enumerate(ANSI16)}


def is_hex(color: object) -> bool:
    return isinstance(color, str) and bool(_HEX.match(color))


def is_color(color: object) -> bool:
    """Couleur acceptable pour la CLI : hex ou nom ANSI (les tons sont résolus avant)."""
    return is_hex(color) or (isinstance(color, str) and color in ANSI16)


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
    """Petit pinceau ANSI ; no-op quand la couleur est désactivée.

    `#RRGGBB` → 24 bits (38;2;r;g;b) ; un nom de `ANSI16` → 30-37 / 90-97 (le terminal décide)."""

    def __init__(self, enabled: bool):
        self.enabled = enabled

    def fg(self, text: str, color: str | None, bold: bool = False, dim: bool = False) -> str:
        if not self.enabled or not text:
            return text
        codes: list[str] = []
        if bold:
            codes.append("1")
        if dim:
            codes.append("2")
        if is_hex(color):
            h = str(color).lstrip("#")
            r, g, b = int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)
            codes.append(f"38;2;{r};{g};{b}")
        elif color in _ANSI_CODES:
            codes.append(str(_ANSI_CODES[color]))
        if not codes:
            return text
        return f"\x1b[{';'.join(codes)}m{text}\x1b[0m"

    def dim(self, text: str) -> str:
        if not self.enabled:
            return text
        return f"\x1b[2m{text}\x1b[0m"

    def bold(self, text: str) -> str:
        return f"\x1b[1m{text}\x1b[0m" if self.enabled and text else text

    def tone(self, text: str, style: str) -> str:
        """Applique un style simple (« bold <couleur> », « dim », « bright_red »)."""
        if not self.enabled or not text:
            return text
        parts = style.split()
        bold = "bold" in parts
        dim = "dim" in parts
        color = next((p for p in parts if p not in ("bold", "dim")), None)
        return self.fg(text, color, bold=bold, dim=dim)

    def grade(self, g: str | None, tone: str) -> str:
        g = g or DASH
        return self.fg(g, tone, bold=g != DASH)
