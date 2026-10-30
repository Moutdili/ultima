"""Compatibilité de la grammaire partagée entre surfaces (test croisé du HUD) — AUCUNE couleur.

Le compagnon v1.4 ne lit pas ce fichier : ses tons sont ceux de `tones.py` (`grade.S` …) et ses
couleurs viennent des fichiers de thème (`themes.py`). Il ne garde ici que les noms historiques des
tons des notes, que `overlay/gnome/tests/test-render.js` lit encore pour vérifier que les deux
surfaces parlent la même langue. Aucun hexadécimal : ce module ne peut pas redevenir une palette.
"""

from __future__ import annotations

# Noms de tons de la charte §9 encore employés par le HUD (jamais des couleurs, jamais utilisés ici).
GRADE_TONES = {
    "S": "gold",
    "A": "green",
    "B": "aqua",
    "C": "blue",
    "D": "orange",
    "F": "red",
    "—": "gray",
}
