"""Tons sémantiques du compagnon (docs/44 §1.4, `Ultima.Engine.Contracts.Tones`).

Les surfaces ne parlent JAMAIS en hexadécimal : elles nomment un ton (`danger`, `grade.A`…), et le
thème résolu (`themes.py`) fournit la couleur. Ce fichier ne contient donc aucune couleur : c'est la
règle vérifiée par `tests/test_tones.py` (« aucun hex hors des fichiers de thème »).

Un thème associe une couleur à chacun des tons ci-dessous, plus `background` et `border` (réservés
aux thèmes). Les couleurs sont lues dans les fichiers de thème, jamais codées ici.
"""

from __future__ import annotations

import re

# Tones.All du contrat.
TEXT = "text"
MUTED = "muted"
ACCENT = "accent"
DANGER = "danger"
OK = "ok"
WARN = "warn"
CURSE = "curse"
ENCHANT = "enchant"
INFO = "info"
SEMANTIC: tuple[str, ...] = (TEXT, MUTED, ACCENT, DANGER, OK, WARN, CURSE, ENCHANT, INFO)

# Tones.Grades (« grade.S » … « grade.F »).
GRADES: tuple[str, ...] = ("S", "A", "B", "C", "D", "F")
GRADE_TONES: tuple[str, ...] = tuple(f"grade.{g}" for g in GRADES)

# Tons réservés aux thèmes (fond et liseré) : surchargeables eux aussi (docs/44 §4).
BACKGROUND = "background"
BORDER = "border"
SURFACES: tuple[str, ...] = (BACKGROUND, BORDER)

ALL: tuple[str, ...] = SEMANTIC + GRADE_TONES + SURFACES

DASH = "—"

# `#RRGGBB` ou `#RRGGBBAA` (schemas/theme.schema.json).
COLOR_RE = re.compile(r"^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")


def is_known_tone(name: object) -> bool:
    return isinstance(name, str) and name in ALL


def is_color(value: object) -> bool:
    return isinstance(value, str) and bool(COLOR_RE.match(value))


def parse_color(value: object) -> tuple[int, int, int, float] | None:
    """`#RRGGBB[AA]` → (r, g, b, alpha) ; None si la couleur est invalide (jamais d'exception)."""
    if not is_color(value):
        return None
    assert isinstance(value, str)
    h = value.lstrip("#")
    r, g, b = int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)
    a = int(h[6:8], 16) / 255.0 if len(h) == 8 else 1.0
    return r, g, b, a


def rgba(color: object, alpha: float = 1.0) -> str | None:
    """Couleur de thème + alpha → `rgba(r, g, b, a)` (seule écriture de couleur du CSS).

    L'alpha du fichier de thème est multiplié par `alpha` (0x99 à 1.0 reste 0.6) ;
    `None` si la couleur est invalide (l'appelant décide du repli).
    """
    parsed = parse_color(color)
    if parsed is None:
        return None
    r, g, b, base = parsed
    a = min(1.0, max(0.0, float(base) * float(alpha)))
    return f"rgba({r}, {g}, {b}, {a:g})"


def _slug(name: str) -> str:
    return "".join(ch if ch.isalnum() else "-" for ch in name.lower())


def tone_class(tone: str | None) -> str | None:
    """Ton → classe CSS (`.tone-danger`, `.tone-grade-a`). Ton inconnu → None."""
    if not tone or tone not in ALL:
        return None
    return f"tone-{_slug(tone)}"


def tone_classes() -> dict[str, str]:
    return {tone: f"tone-{_slug(tone)}" for tone in ALL}


GRADE_CLASS: dict[str, str] = {**{g: f"grade-{g.lower()}" for g in GRADES}, DASH: "grade-none"}


def grade_class(grade: str | None) -> str:
    """Lettre → classe CSS (« — » n'est pas un identifiant CSS valide)."""
    return GRADE_CLASS.get(grade or DASH, "grade-none")


def grade_tone(grade: str | None) -> str:
    """Lettre → ton (`grade.S`) ; « — »/absent → `muted` (jamais une couleur inventée)."""
    return f"grade.{grade}" if grade in GRADES else MUTED


# Couleur d'un joueur = son personnage (skill design §2), exprimée en tons pour survivre aux thèmes.
CHARACTER_TONES: dict[str, str] = {
    "IRONCLAD": DANGER,
    "SILENT": OK,
    "DEFECT": INFO,
    "REGENT": WARN,
    "NECROBINDER": CURSE,
    "DEPRIVED": ACCENT,
}


def character_tone(character_id: str | None) -> str | None:
    """`CHARACTER.IRONCLAD` → `danger` ; inconnu → None (texte du thème, jamais une couleur inventée)."""
    if not character_id:
        return None
    key = character_id.split(".")[-1].upper()
    return CHARACTER_TONES.get(key)
