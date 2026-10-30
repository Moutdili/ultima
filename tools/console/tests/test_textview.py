"""Mise en texte de l'EngineState : notes S-F, « — » pour tout ce qui est null/absent (jamais None ni 0)."""

import copy

import pytest
from conftest import load_fixture

from ultima_console import textview as tv
from ultima_console.palette import CHARACTER_COLORS, DASH, GRADE_TONES
from ultima_console.settings import OverlaySettings

ALIVE = tv.Health("alive", [], "ok", 1.0)


def text(lines):
    return "\n".join(tv.plain(ln) for ln in lines)


@pytest.mark.parametrize("total,grade", [(100, "S"), (85, "S"), (84, "A"), (70, "A"), (55, "B"), (40, "C"),
                                         (25, "D"), (24, "F"), (0, "F")])
def test_grade_scale_matches_contract(total, grade):
    assert tv.grade_from_total(total) == grade
    assert tv.grade({"total": total, "terms": [], "confidence": "low"}) == grade


def test_grade_prefers_published_letter_and_null_is_dash():
    assert tv.grade({"total": 10, "grade": "A"}) == "A"
    assert tv.grade(None) == DASH
    assert tv.grade({"total": None}) == DASH
    assert tv.grade_seg(None) == (DASH, "gray", False)
    assert GRADE_TONES == {"S": "gold", "A": "green", "B": "aqua", "C": "blue", "D": "orange", "F": "red", "—": "gray"}


@pytest.mark.parametrize("name", ["v13", "map", "reward", "shop"])
def test_dashboard_never_prints_none(name):
    out = text(tv.dashboard(load_fixture(name), ALIVE, 100))
    assert "None" not in out and out.strip()


def test_reward_sorted_by_grade_and_null_score():
    out = text(tv.reward_lines(load_fixture("reward"), 100))
    lines = out.splitlines()
    assert lines[0].startswith("RÉCOMPENSE")
    assert lines[1].lstrip().startswith("S  Fléau+ (Semée 2)")
    assert lines[2].lstrip().startswith("B  Explosion funèbre")
    assert lines[3].lstrip().startswith(f"{DASH}  Garde du corps")
    assert lines[4].lstrip().startswith("D  passer")


def test_v13_null_skip_score_and_null_cost():
    s = load_fixture("reward")
    s["reward"]["skipScore"] = None
    s["reward"]["offered"][0]["cost"] = None
    out = text(tv.reward_lines(s, 100))
    assert f"{DASH}  passer" in out
    assert tv.cost_text({"cost": None}) == DASH
    assert tv.cost_text({"cost": -1}) == DASH
    assert tv.cost_text({"cost": 2}) == "2"
    assert f"coût {DASH}" in text(tv.card_detail({"name": "X", "cost": None, "type": "Skill"}))


def test_all_sections_null():
    s = load_fixture("v13")
    for k in ("run", "reward", "shop", "paths", "deck", "map", "route", "event"):
        s[k] = None
    for screen in ("Reward", "Shop", "Event", "Map", "Combat", "Menu"):
        s["screen"] = screen
        out = text(tv.dashboard(s, ALIVE, 80))
        assert "None" not in out
    s["screen"] = "Event"
    assert "ÉVÉNEMENT · non lu" in text(tv.dashboard(s, ALIVE, 80))
    s["screen"] = "Map"
    assert f"CHEMINS · {DASH}" in text(tv.dashboard(s, ALIVE, 80))
    s["screen"] = "Shop"
    assert "SHOP · non lu" in text(tv.dashboard(s, ALIVE, 80))
    assert "Pas de run en cours" in text(tv.dashboard(s, ALIVE, 80))


def test_null_numbers_inside_sections():
    s = load_fixture("shop")
    s["run"]["floor"] = None
    s["run"]["ascension"] = None
    s["run"]["actIndex"] = None
    s["shop"]["items"][0]["price"] = None
    s["shop"]["removalPrice"] = None
    out = text(tv.dashboard(s, ALIVE, 100))
    assert "None" not in out
    assert f"A{DASH}" in out and f"étage {DASH}" in out and f"acte {DASH}" in out
    assert f"{DASH} or" in out


def test_deck_summary_and_curse_marks():
    s = load_fixture("map")
    out = text(tv.dashboard(s, ALIVE, 100))
    assert "!! Malédiction · Fléau de l'Ascendant" in out
    assert "Défense (Célérité 1)" in out
    assert "note du deck C" in out
    s["deck"] = None
    assert f"note du deck {DASH}" in text([tv.summary_line(s)])


def test_degraded_first():
    s = load_fixture("map")
    lines = tv.dashboard(s, ALIVE, 100)
    assert tv.plain(lines[0]).startswith("DÉGRADÉ · lecture mémoire refusée")


def test_party_line_coop_uses_character_colors():
    s = load_fixture("v13")
    assert tv.party_line(s, 80) is None  # solo : rien
    ally = copy.deepcopy(s["run"]["party"][0])
    ally.update(isLocal=False, name="Ironclad", characterId="CHARACTER.IRONCLAD", hp=None)
    s["run"]["party"].append(ally)
    line = tv.party_line(s, 80)
    tones = {t: tone for t, tone, _ in line}
    assert tones["Ironclad"] == CHARACTER_COLORS["CHARACTER.IRONCLAD"]
    assert any("(toi)" in t for t, _, _ in line)
    assert f" {DASH}/" in tv.plain(line)


def test_missing_state_messages():
    out = text(tv.dashboard(None, tv.Health("absent", [], "missing", None), 80))
    assert "Aucun état publié." in out and "ultima start" in out
    out = text(tv.dashboard(None, tv.Health("absent", [], "too_new", None, "contrat v2"), 80))
    assert "mettre à jour ULTIMA" in out


def test_health_line_one_accent():
    h = tv.Health("absent", [], "ok", 5.0)
    line = tv.health_line(load_fixture("reward"), h)
    accents = [tone for _, tone, _ in line if tone not in (None, "dim")]
    assert accents == ["red"]
    assert "daemon arrêté" in tv.plain(line)


def test_hud_preview_order_visibility_context():
    s = load_fixture("reward")
    st = OverlaySettings.default()
    out = text(tv.hud_preview(s, st))
    # Moment de décision : la récompense prend la place (pas de deck/reliques), malédictions gardées.
    assert out.splitlines()[0].startswith("!! MALÉDICTIONS")
    assert "RÉCOMPENSE  S" in out and "DECK" not in out.replace("NOTE DU DECK", "")
    st.toggle("reward")
    assert "RÉCOMPENSE" not in text(tv.hud_preview(s, st))
    s["screen"] = "Map"
    st = OverlaySettings.default()
    st.move("relics", -3)
    lines = text(tv.hud_preview(s, st)).splitlines()
    assert lines[0].startswith("RELIQUES") and lines[1].startswith("DECK")
    st.hud.grades_only = True
    s = load_fixture("shop")
    shop_line = [ln for ln in text(tv.hud_preview(s, st)).splitlines() if ln.startswith("SHOP")][0]
    assert shop_line.strip() == "SHOP  A"
