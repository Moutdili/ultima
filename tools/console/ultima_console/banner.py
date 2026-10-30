"""Bandeau « ULTIMA » (dessin fourni par Mio, recopié tel quel : 48 colonnes, 8 lignes).

Où : en tête du tableau de bord du TUI, dans `ultima version`, à la fin de tools/install.sh (`ultima banner`).
Couleurs : dégradé vertical par paliers des ors de la palette du jeu (palette.py / StsColors : cream → gold →
orange, aucune couleur interpolée), sous-titre en crème atténué (ton « dim »). NO_COLOR / --color never : sans
couleur. Repli sur une ligne « ULTIMA · a simple tracker for sts2 » si le terminal fait moins de 52 colonnes
ou moins de 30 lignes (le bandeau ne doit pas manger le tableau de bord), ou si la locale n'est pas UTF-8.
"""

from __future__ import annotations

import os

from .palette import SEP, Ansi

ART: tuple[str, ...] = (
    '██╗   ██╗██╗     ████████╗██╗███╗   ███╗ █████╗',
    '██║   ██║██║     ╚══██╔══╝██║████╗ ████║██╔══██╗',
    '██║   ██║██║        ██║   ██║██╔████╔██║███████║',
    '██║   ██║██║        ██║   ██║██║╚██╔╝██║██╔══██║',
    '╚██████╔╝███████╗   ██║   ██║██║ ╚═╝ ██║██║  ██║',
    ' ╚═════╝ ╚══════╝   ╚═╝   ╚═╝╚═╝     ╚═╝╚═╝  ╚═╝',
)
SUBTITLE = '           a simple tracker for sts2'
LINES: tuple[str, ...] = ART + ("", SUBTITLE)
WIDTH = max(len(line) for line in LINES)        # 48
HEIGHT = len(LINES)                             # 8
ONE_LINE = f"ULTIMA{SEP}a simple tracker for sts2"
MIN_COLS, MIN_ROWS = 52, 30
# Une couleur de la palette par ligne du dessin (du haut vers le bas).
ROW_TONES: tuple[str, ...] = ("cream", "gold", "gold", "gold", "orange", "orange")


def utf8_locale(env: dict | None = None) -> bool:
    """Locale effective en UTF-8 ? ULTIMA_LOCALE (locale d'origine notée par le lanceur, avant la coercition
    C → C.UTF-8 de Python), sinon LC_ALL > LC_CTYPE > LANG. Vide, « C » ou « POSIX » → non."""
    env = os.environ if env is None else env
    if "ULTIMA_LOCALE" in env:
        value = env["ULTIMA_LOCALE"]
    else:
        value = env.get("LC_ALL") or env.get("LC_CTYPE") or env.get("LANG") or ""
    v = value.lower().replace("-", "")
    return "utf8" in v


def full(cols: int, rows: int, utf8: bool | None = None) -> bool:
    """Le bandeau complet tient-il ? Sinon : une seule ligne."""
    if utf8 is None:
        utf8 = utf8_locale()
    return utf8 and cols >= MIN_COLS and rows >= MIN_ROWS


def segments(cols: int, rows: int, utf8: bool | None = None) -> list[list[tuple[str, str | None, bool]]]:
    """Lignes au format textview (texte, ton, gras)."""
    if not full(cols, rows, utf8):
        return [[("ULTIMA", "gold", True), (f"{SEP}a simple tracker for sts2", "dim", False)]]
    out = [[(line, tone, True)] for line, tone in zip(ART, ROW_TONES)]
    out.append([])
    out.append([(SUBTITLE, "dim", False)])
    return out


def render(ansi: Ansi, cols: int, rows: int, utf8: bool | None = None) -> str:
    out = []
    for line in segments(cols, rows, utf8):
        parts = []
        for text, tone, bold in line:
            parts.append(ansi.dim(text) if tone == "dim" else ansi.fg(text, tone, bold=bold))
        out.append("".join(parts))
    return "\n".join(out)
