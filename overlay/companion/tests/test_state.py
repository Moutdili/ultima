import copy
import json

import pytest

from ultima_companion import paths, state
from ultima_companion.settings import OverlaySettings
from ultima_companion.state import INVALID, MISSING, OK, TOO_NEW, grade_from_total


def test_sample_parses(sample):
    r = state.parse_state(sample)
    assert r.kind == OK, r.message
    st = r.state
    assert st.contract_version == 1
    assert st.screen == "Shop"
    assert st.health.game_running and st.health.save_ok and st.health.mirror_ok
    assert st.health.mirror_latency_ms == 108 and st.health.degraded is None
    assert st.at is not None and st.at.utcoffset().total_seconds() == 3600
    run = st.run
    assert run.character_name == "Nécrolieuse" and run.boss_name == "Bête cérémonielle"
    assert (run.hp, run.max_hp, run.gold, run.floor, run.ascension) == (66, 66, 116, 4, 9)
    assert len(run.deck) == 11
    swift = [c for c in run.deck if c.enchantment][0]
    assert swift.enchantment.name == "Célérité" and swift.enchantment.amount == 1
    assert [c.id for c in run.deck if c.is_curse] == ["CARD.ASCENDERS_BANE"]
    assert all(c.score is None for c in run.deck)
    assert run.relics[0].score.total == 62 and run.relics[0].score.grade == "B"
    assert [t.points for t in run.relics[0].score.terms] == [60, 2]
    assert run.potions[0].name == "Potion d'attaque" and run.potions[0].score is None
    assert st.deck.score.total == 48 and st.deck.score.grade == "C"
    assert st.deck.potential_archetypes == ["Âmes (exemple)"]
    assert [c.id for c in st.deck.curses] == ["CARD.ASCENDERS_BANE"]
    # Sample v1.0 : 9 modules sans hud → lu sans perte, modules v1.2 ajoutés, hud par défaut.
    assert st.settings.ids()[:9] == [m["id"] for m in sample["settings"]["modules"]]
    assert st.settings.hud.to_json() == OverlaySettings.default().hud.to_json()
    assert st.has_shop and not st.has_reward and not st.has_paths
    assert st.contract_minor == 0 and st.map is None and st.route is None and st.event is None
    assert run.party == [] and run.allies == []
    assert st.shop.removal_price == 75 and [i.kind for i in st.shop.items] == ["card", "card", "relic", "potion"]
    assert st.shop.items[2].affordable is False and st.shop.items[1].on_sale is True


def test_sample_file_load(sample_path):
    assert state.load(sample_path).kind == OK


# ---- notation S à F ------------------------------------------------------------------------

@pytest.mark.parametrize("total,grade", [
    (100, "S"), (85, "S"), (84, "A"), (70, "A"), (69, "B"), (55, "B"), (54, "C"), (40, "C"),
    (39, "D"), (25, "D"), (24, "F"), (0, "F"), (-5, "F"),
])
def test_grade_scale_matches_contract(total, grade):
    assert grade_from_total(total) == grade


def test_grade_absent_is_computed_from_total(sample):
    data = copy.deepcopy(sample)
    del data["run"]["relics"][0]["score"]["grade"]
    data["deck"]["score"]["grade"] = "Z"  # hors échelle → recalculé
    st = state.parse_state(data).state
    assert st.run.relics[0].score.grade == "B"
    assert st.deck.score.grade == "C"


def test_grade_published_is_used(sample):
    data = copy.deepcopy(sample)
    data["run"]["relics"][0]["score"]["grade"] = "A"
    assert state.parse_state(data).state.run.relics[0].score.grade == "A"


# ---- variantes ---------------------------------------------------------------------------------

def test_null_sections(sample):
    data = copy.deepcopy(sample)
    for k in ("run", "reward", "shop", "paths", "deck"):
        data[k] = None
    r = state.parse_state(data)
    assert r.kind == OK
    assert r.state.run is None and r.state.deck is None and not r.state.has_shop


def test_degraded(sample):
    data = copy.deepcopy(sample)
    data["health"].update({"mirrorOk": False, "mirrorLatencyMs": None,
                           "degraded": "miroir indisponible : saves seules"})
    h = state.parse_state(data).state.health
    assert h.degraded == "miroir indisponible : saves seules"
    assert not h.mirror_ok and h.mirror_latency_ms is None


def test_future_major_version_refused(sample):
    data = copy.deepcopy(sample)
    data["contractVersion"] = 2
    r = state.parse_state(data)
    assert r.kind == TOO_NEW and r.state is None and r.contract_version == 2
    assert "mettre à jour ULTIMA" in r.message


def test_unknown_fields_ignored_everywhere(sample):
    data = copy.deepcopy(sample)
    data["newTopLevel"] = {"a": 1}
    data["health"]["cpu"] = 3
    data["run"]["stars"] = 7
    data["run"]["deck"][0]["art"] = "x.png"
    data["run"]["relics"][0]["score"]["terms"][0]["weight"] = 0.5
    data["deck"]["synergies"] = []
    r = state.parse_state(data)
    assert r.kind == OK
    assert r.state.run.deck[0].name == "Frappe"


def test_unknown_screen_kept_raw(sample):
    data = copy.deepcopy(sample)
    data["screen"] = "Crystal"
    assert state.parse_state(data).state.screen == "Crystal"


def test_wrong_types_tolerated(sample):
    data = copy.deepcopy(sample)
    data["health"] = "bad"
    data["run"]["deck"] = [None, 3, {"id": "CARD.X"}]
    data["run"]["relics"][0]["score"] = {"terms": []}  # total absent → score inutilisable → « — »
    data["deck"]["potentialArchetypes"] = ["ok", 3]
    data["settings"] = None
    r = state.parse_state(data)
    assert r.kind == OK
    st = r.state
    assert st.health.game_running is False and st.health.degraded is None
    assert [c.id for c in st.run.deck] == ["CARD.X"] and st.run.deck[0].name == "CARD.X"
    assert st.run.relics[0].score is None
    assert st.deck.potential_archetypes == ["ok"]
    assert st.settings is None


def test_dotnet_timestamp_formats(sample):
    data = copy.deepcopy(sample)
    data["at"] = "2026-10-29T12:00:00.1234567Z"
    st = state.parse_state(data).state
    assert st.at is not None and st.at.utcoffset().total_seconds() == 0
    data["at"] = "pas une date"
    st = state.parse_state(data).state
    assert st.at is None and st.at_raw == "pas une date"


@pytest.mark.parametrize("text", ["", "{", "[]", "null", '{"health": {}}', '{"contractVersion": "1"}'])
def test_invalid(text):
    assert state.parse_text(text).kind == INVALID


def test_missing_file_means_daemon_not_started(isolated_xdg):
    assert paths.state_path() == isolated_xdg["runtime"] / "ultima" / "state.json"
    r = state.load()
    assert r.kind == MISSING and "Daemon non démarré" in r.message


def test_load_from_runtime_dir(isolated_xdg, sample):
    p = paths.state_path()
    p.parent.mkdir()
    p.write_text(json.dumps(sample), encoding="utf-8")
    assert state.load().kind == OK
