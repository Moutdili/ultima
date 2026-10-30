"""Résolveur de thèmes (docs/44 §3-§4) : extends, auto-perso, surcharges, repli `sts` + message.

Les thèmes de fixture sont écrits dans un dossier TEMPORAIRE : aucun thème de test n'est commité, et
le résolveur charge n'importe quel `themes/*.json` présent dans le dépôt (aujourd'hui `sts.json`).
"""

import json
from pathlib import Path

import pytest

from conftest import REPO
from ultima_companion import themes

STS = {
    "id": "sts", "label": "Slay the Spire", "api": 1,
    "tones": {
        "text": "#FFF6E2", "muted": "#FFF6E299", "accent": "#EFC851", "danger": "#FF5555",
        "ok": "#7FFF00", "warn": "#FFA518", "curse": "#EE82EE", "enchant": "#2AEBBE",
        "info": "#87CEEB",
        "grade.S": "#EFC851", "grade.A": "#7FFF00", "grade.B": "#2AEBBE", "grade.C": "#87CEEB",
        "grade.D": "#FFA518", "grade.F": "#FF5555",
        "background": "#192930", "border": "#EFC85166",
    },
    "glass": {"tint": "#192930", "tintOpacity": 0.55},
    "font": {"family": "Fira Sans"},
}


def write_theme(folder: Path, data, name: str | None = None) -> Path:
    path = folder / f"{name or data['id']}.json"
    path.write_text(json.dumps(data), encoding="utf-8")
    return path


@pytest.fixture
def dirs(tmp_path):
    folder = tmp_path / "themes"
    folder.mkdir()
    write_theme(folder, STS)
    return [folder]


def _resolve(dirs, requested=None, overrides=None, character=None):
    return themes.resolve(requested, overrides, character, dirs=dirs)


# ---- chargement / défauts ---------------------------------------------------------------------

def test_default_theme_is_sts_with_all_tones(dirs):
    res = _resolve(dirs)
    assert res.effective_id == "sts" and res.requested == "sts"
    assert res.label == "Slay the Spire"
    assert set(res.tones) == set(themes.tones.ALL)
    assert res.tones["accent"] == "#EFC851"
    assert res.glass_tint == "#192930" and res.glass_opacity == 0.55
    assert res.theme.font_family == "Fira Sans"
    assert res.messages == [] and res.fallback is False


def test_repo_themes_dir_loads_every_present_file():
    """Le dépôt ne contient que ce que T2 a livré : tout fichier présent doit charger (ou être signalé)."""
    folder = REPO / "themes"
    catalog = themes.load_catalog([folder])
    files = sorted(p.stem for p in folder.glob("*.json"))
    assert "sts" in files and "sts" in catalog.themes
    for stem in files:
        assert stem in catalog.themes or stem in catalog.problems


def test_user_theme_shadows_builtin(dirs):
    shadow = dict(STS, tones=dict(STS["tones"], accent="#000001"))
    folder2 = dirs[0].parent / "user"
    folder2.mkdir()
    write_theme(folder2, shadow)
    catalog = themes.load_catalog([folder2, dirs[0]])
    assert catalog.themes["sts"].tones["accent"] == "#000001"


# ---- extends ------------------------------------------------------------------------------------

def test_extends_inherits_and_overrides(dirs):
    write_theme(dirs[0], {
        "id": "ironclad", "label": "Ironclad", "api": 1, "extends": "sts",
        "tones": {"accent": "#FF5555"}, "glass": {"tint": "#801212"},
        "font": {"family": "Kreon", "size": 16},
    })
    res = _resolve(dirs, "ironclad")
    assert res.effective_id == "ironclad" and res.label == "Ironclad"
    assert res.tones["accent"] == "#FF5555" and len(res.tones) == 17
    assert res.tones["text"] == "#FFF6E2"                    # hérité
    assert res.glass_tint == "#801212"
    assert res.glass_opacity == 0.55                         # hérité
    assert res.theme.font_family == "Kreon" and res.theme.font_size == 16


def test_extends_chain_of_two_levels(dirs):
    write_theme(dirs[0], {"id": "nuit", "label": "Nuit", "api": 1, "extends": "sts",
                          "tones": {"background": "#000010"}})
    write_theme(dirs[0], {"id": "nuit-bleue", "label": "Nuit bleue", "api": 1, "extends": "nuit",
                          "tones": {"accent": "#1010AA"}})
    res = _resolve(dirs, "nuit-bleue")
    assert res.tones["background"] == "#000010" and res.tones["accent"] == "#1010AA"
    assert res.tones["text"] == "#FFF6E2"


def test_cycle_is_refused_and_falls_back(dirs):
    write_theme(dirs[0], {"id": "a", "label": "A", "api": 1, "extends": "b", "tones": {}})
    write_theme(dirs[0], {"id": "b", "label": "B", "api": 1, "extends": "a", "tones": {}})
    res = _resolve(dirs, "a")
    assert res.fallback and res.effective_id == "sts"
    assert any("circulaire" in m and "Repli sur « sts »" in m for m in res.messages)


def test_unknown_extends_is_refused(dirs):
    write_theme(dirs[0], {"id": "orphelin", "label": "Orphelin", "api": 1, "extends": "fantome",
                          "tones": dict(STS["tones"])})
    res = _resolve(dirs, "orphelin")
    assert res.fallback and res.effective_id == "sts"
    assert any("fantome" in m and "Repli sur « sts »" in m for m in res.messages)


# ---- thème cassé → `sts` + message VISIBLE --------------------------------------------------------

@pytest.mark.parametrize("broken,requested,why", [
    ("{pas du json", "casse", "JSON invalide"),
    (json.dumps({"id": "Bad Id", "label": "x", "api": 1, "tones": STS["tones"]}), "casse", "id invalide"),
    (json.dumps({"id": "v2", "label": "x", "api": 2, "tones": STS["tones"]}), "v2", "API 2"),
    (json.dumps({"id": "vide", "label": "x", "api": 1, "tones": {}}), "vide", "tons manquants"),
    (json.dumps({"id": "sanslabel", "api": 1, "tones": STS["tones"]}), "sanslabel", "label"),
    (json.dumps({"label": "x", "api": 1, "tones": STS["tones"]}), "casse", "id invalide"),
    (json.dumps(["pas un objet"]), "casse", "objet JSON"),
])
def test_broken_theme_falls_back_with_a_visible_message(dirs, broken, requested, why):
    (dirs[0] / f"{requested}.json").write_text(broken, encoding="utf-8")
    res = _resolve(dirs, requested)
    assert res.fallback and res.effective_id == "sts"
    assert res.tones["accent"] == "#EFC851"                  # couleurs du repli
    assert len(res.messages) == 1
    assert requested in res.messages[0] and "Repli sur « sts »" in res.messages[0]
    assert why in res.messages[0]


def test_missing_theme_falls_back_with_a_message(dirs):
    res = _resolve(dirs, "inconnu")
    assert res.fallback and res.effective_id == "sts"
    assert "inconnu" in res.messages[0] and "introuvable" in res.messages[0]


def test_one_bad_tone_is_ignored_field_by_field(dirs):
    write_theme(dirs[0], {"id": "presque", "label": "Presque", "api": 1, "extends": "sts",
                          "tones": {"accent": "or", "danger": "#000000", "inconnu": "#111111"}})
    res = _resolve(dirs, "presque")
    assert res.fallback is False
    assert res.tones["accent"] == "#EFC851"                  # hérité, pas inventé
    assert res.tones["danger"] == "#000000"
    assert any("accent" in w for w in res.warnings)
    assert any("inconnu" in w for w in res.warnings)


def test_missing_sts_means_no_theme_and_a_message(tmp_path):
    res = themes.resolve(dirs=[tmp_path])
    assert res.theme is None and res.tones == {}
    assert res.messages and "sts" in res.messages[0]


# ---- surcharges `appearance.companion.tones` ------------------------------------------------------

def test_tones_overrides_apply_over_the_theme(dirs):
    res = _resolve(dirs, "sts", {"accent": "#010203", "grade.S": "#040506AA"})
    assert res.tones["accent"] == "#010203" and res.tones["grade.S"] == "#040506AA"
    assert res.overrides == {"accent": "#010203", "grade.S": "#040506AA"}
    assert res.tones["text"] == "#FFF6E2"


def test_invalid_overrides_are_ignored_and_reported(dirs):
    res = _resolve(dirs, "sts", {"accent": "rouge", "pas-un-ton": "#FFFFFF", "ok": "#00FF00"})
    assert res.tones["accent"] == "#EFC851"
    assert res.tones["ok"] == "#00FF00"
    assert len([w for w in res.warnings if "ignoré" in w]) == 2


# ---- auto-perso ----------------------------------------------------------------------------------

@pytest.mark.parametrize("character,expected", [
    ("CHARACTER.IRONCLAD", "ironclad"),
    ("CHARACTER.SILENT", "silent"),
    ("CHARACTER.DEFECT", "defect"),
    ("CHARACTER.REGENT", "regent"),
    ("CHARACTER.NECROBINDER", "necrobinder"),
    ("Ironclad", "ironclad"),
    ("CHARACTER.NOBODY", "sts"),
    (None, "sts"),
    ("", "sts"),
])
def test_auto_perso_follows_the_running_character(dirs, character, expected):
    for tid in ("ironclad", "silent", "defect", "regent", "necrobinder"):
        write_theme(dirs[0], {"id": tid, "label": tid.title(), "api": 1, "extends": "sts"})
    res = _resolve(dirs, themes.AUTO_ID, character=character)
    assert res.effective_id == expected
    assert res.requested == themes.AUTO_ID
    assert res.fallback is False


def test_auto_perso_without_the_character_theme_falls_back_to_sts(dirs):
    res = _resolve(dirs, themes.AUTO_ID, character="CHARACTER.IRONCLAD")
    assert res.effective_id == "sts"
    assert any("auto-perso" in w for w in res.warnings)


def test_character_theme_id_mapping():
    assert themes.character_theme_id("CHARACTER.IRONCLAD") == "ironclad"
    assert themes.character_theme_id("Necrobinder") == "necrobinder"
    assert themes.character_theme_id("CHARACTER.UNKNOWN") is None
    assert themes.character_theme_id(None) is None
