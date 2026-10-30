"""Modèle de présentation (pages Tableau et Run) : variantes v1.0 / v1.2 / v1.3, états vides."""

import copy

import pytest

from ultima_companion import ipc, state, views


def _st(data):
    r = state.parse_state(data)
    assert r.kind == state.OK, r.message
    return r


# ---- parsing v1.2 / v1.3 ------------------------------------------------------------------------------

def test_demo_v13_parses_everything(demo):
    st = _st(demo).state
    assert (st.contract_version, st.contract_minor) == (1, 3)
    assert st.settings.hud.quality == "verre" and len(st.settings.modules) == 12
    assert st.map.position == (2, 7) and st.map.boss == (3, 15) and len(st.map.nodes) == 5
    assert st.map.room_at(3, 8) == "rest_site" and st.map.room_at(9, 9) is None
    assert st.route.path == [(3, 8), (2, 9), (3, 15)] and st.route.expected_hp_at_boss == 38
    assert st.route.terms[0].evidence == "PV 52/66"
    assert [o.available for o in st.event.options] == [True, True, False]
    assert [a.is_local for a in st.run.party] == [True, False]
    assert [a.name for a in st.run.allies] == ["Allié (démo)"]
    bane = [c for c in st.run.deck if c.is_curse][0]
    assert bane.score.delta_g == -4.5 and bane.score.grade == "F"
    assert st.run.deck[0].description.startswith("Inflige")
    assert st.reward.offered[1].score.terms[1].evidence == "boss à attaques multiples"


def test_v12_sections_wrong_types_tolerated(demo):
    data = copy.deepcopy(demo)
    data["map"] = {"nodes": [None, {"col": "x"}, {"col": 1, "row": 2, "children": [3, {"col": 1}]}],
                   "position": "here", "boss": None}
    data["route"] = {"path": [{"col": 1}, 4], "mayBeLethal": "yes"}
    data["event"] = {"options": [3, {"id": "Z"}]}
    data["run"]["party"] = "solo"
    data["reward"]["offered"] = [None]
    st = _st(data).state
    assert len(st.map.nodes) == 1 and st.map.nodes[0].children == [] and st.map.position is None
    assert st.route.path == [] and st.route.may_be_lethal is False
    assert st.event.options[0].label == "Z" and st.event.options[0].available is True
    assert st.run.party == [] and st.reward.offered == []


# ---- décision du moment -----------------------------------------------------------------------------------

def test_reward_decision(demo):
    d = views.decision(_st(demo))
    assert d.kind == "reward" and d.title == "Récompense"
    assert [i.title for i in d.items] == ["Étincelle (démo)", "Rempart (démo)", "Écho (démo)", "Passer"]
    assert [i.grade for i in d.items] == ["B", "A", "D", "F"]
    assert [i.best for i in d.items] == [False, True, False, False]
    # raison = terme le plus lourd, avec sa preuve ; détail = niveau 2
    assert d.items[1].reason == "Méta +62"
    assert d.items[2].reason == "Méta +45"
    assert any("boss à attaques multiples" in line for line in d.items[1].detail)
    assert d.items[1].detail[0].startswith("Gagne 12")


def test_reward_already_taken_and_unscored(demo):
    data = copy.deepcopy(demo)
    data["reward"]["alreadyTaken"] = True
    for c in data["reward"]["offered"]:
        c["score"] = None
    data["reward"]["skipScore"] = None
    d = views.decision(_st(data))
    assert d.subtitle == "déjà prise"
    assert all(i.grade == "—" and not i.best for i in d.items)
    assert "0" not in [i.grade for i in d.items]


def test_shop_decision_sorted_for_the_next_10_seconds(demo):
    data = copy.deepcopy(demo)
    data["screen"] = "Shop"
    d = views.decision(_st(data))
    assert d.title == "Shop · 131 or"
    titles = [i.title for i in d.items]
    # meilleur achat possible d'abord ; le trop cher (même S) en dernier, atténué
    assert titles[0] == "Faux (démo)" and d.items[0].best and d.items[0].value == "38 or soldé"
    assert titles[-1] == "Relique C (démo)" and d.items[-1].dim and d.items[-1].reason.startswith("trop cher")
    assert "Retrait d'une carte" in titles
    assert sum(i.best for i in d.items) == 1


def test_shop_sample_v10(sample):
    d = views.decision(_st(sample))
    assert d.kind == "shop" and d.items[0].grade == "A" and d.items[0].best
    assert d.items[-1].dim and d.items[-1].value == "181 or"


def test_shop_removal_too_expensive(sample):
    data = copy.deepcopy(sample)
    data["run"]["gold"] = 10
    d = views.decision(_st(data))
    removal = [i for i in d.items if i.title == "Retrait d'une carte"][0]
    assert removal.dim and removal.reason.startswith("trop cher")


def test_event_decision_and_empty(demo):
    data = copy.deepcopy(demo)
    data["screen"] = "Event"
    d = views.decision(_st(data))
    assert d.title == "Événement (démo)" and [i.best for i in d.items] == [True, False, False]
    assert d.items[2].dim and d.items[2].reason == "indisponible"
    data["event"] = None
    d = views.decision(_st(data))
    assert d.items == [] and "v1.2" in d.empty


def test_map_route_and_paths(demo):
    data = copy.deepcopy(demo)
    data["screen"] = "Map"
    st = _st(data)
    assert views.route_rooms(st.state) == ["feu", "?", "boss"]
    d = views.decision(st)
    assert d.title == "Route" and d.subtitle == "23 chemins jusqu'au boss"
    assert d.items[0].reason == "feu › ? › boss" and d.items[0].value == "PV au boss ~38"
    assert d.items[1].title == "Colonne 1 · élite" and d.items[1].value == "dégâts ~14"
    assert d.alert is None
    data["route"]["mayBeLethal"] = True
    assert "LÉTAL" in views.decision(_st(data)).alert


def test_map_without_v12_data_is_calm_empty(sample):
    data = copy.deepcopy(sample)
    data["screen"] = "Map"
    d = views.decision(_st(data))
    assert d.kind == "map" and d.items == [] and d.empty


@pytest.mark.parametrize("screen,kind", [("Combat", "idle"), ("RestSite", "idle"), ("Menu", "idle"),
                                         ("Crystal", "idle")])
def test_no_decision_screens(demo, screen, kind):
    data = copy.deepcopy(demo)
    data["screen"] = screen
    d = views.decision(_st(data))
    assert d.kind == kind and d.items == [] and d.empty


def test_offline_and_no_run(demo):
    assert views.decision(state.StateResult(state.MISSING)).kind == "offline"
    data = copy.deepcopy(demo)
    data["run"] = None
    d = views.decision(_st(data))
    assert d.kind == "idle" and d.title == "Pas de run"


# ---- résumé, santé, page Run ------------------------------------------------------------------------------

def test_deck_summary_is_one_line(demo, sample):
    assert views.deck_summary(_st(demo).state) == \
        "Deck 11 · 1 enchantée · 1 malédiction · reliques 2 · potions 1"
    assert views.deck_summary(_st(sample).state) == \
        "Deck 11 · 1 enchantée · 1 malédiction · reliques 1 · potions 1"
    assert views.run_title(_st(demo).state) == "Personnage (démo) · Acte 1 · Étage 7 · A9"


def test_health_line_variants(demo):
    r = _st(demo)
    assert views.health_line(r, ipc.ALIVE).text == "Sources OK · miroir 96 ms"
    assert views.health_line(r, ipc.REFUSED).tone == "orange"
    assert views.health_line(state.StateResult(state.MISSING), ipc.NO_SOCKET).tone == "red"
    data = copy.deepcopy(demo)
    data["health"]["degraded"] = "miroir indisponible"
    assert views.health_line(_st(data), ipc.ALIVE).text == "Mode dégradé · miroir indisponible"
    data["health"].update(degraded=None, mirrorOk=False)
    assert views.health_line(_st(data), ipc.ALIVE).text == "Source KO : miroir"
    data["health"]["gameRunning"] = False
    assert views.health_line(_st(data), ipc.ALIVE).tone == "gray"


def test_run_groups_order_and_grammar(demo):
    groups = views.run_groups(_st(demo).state)
    assert [g.key for g in groups] == ["curses", "deck", "relics", "potions", "party"]
    curses = groups[0].items
    assert curses[0].tag == "Malédiction" and curses[0].tag_tone == "purple" and curses[0].grade == "F"
    deck = groups[1]
    assert deck.title == "Deck · 11"
    labels = [i.label for i in deck.items]
    # même ordre que le HUD : coût croissant, nom, sans enchantement d'abord
    assert labels == ["4× Garde (démo)", "Garde (démo)", "4× Lame (démo)", "Rituel (démo)+"]
    ench = deck.items[1]
    assert ench.tag == "Vivacité (démo) 1" and ench.tag_tone == "aqua"
    assert deck.items[3].grade == "S" and deck.items[3].reason == "Méta +80"
    relics = groups[2].items
    assert [r.grade for r in relics] == ["A", "—"]
    assert relics[1].detail[-1] == "Note : — (calcul indisponible)"
    party = groups[4].items
    assert party[0].tone == "red" and party[0].value == "61/80 PV" and party[0].reason == "Ironclad"


def test_run_groups_solo_and_v10(sample):
    groups = views.run_groups(_st(sample).state)
    assert [g.key for g in groups] == ["curses", "deck", "relics", "potions"]   # pas d'équipe en solo
    assert all(i.grade != "0" for g in groups for i in g.items)


def test_deck_note(demo, sample):
    n = views.deck_note(_st(demo).state)
    assert n.grade == "B" and n.reason == "Méta des cartes +52" and n.archetype == "Âmes (démo)"
    assert n.boss == "Boss (démo)" and n.detail[0] == "Note B · 61/100 · confiance moyenne"
    data = copy.deepcopy(sample)
    data["deck"] = None
    n = views.deck_note(_st(data).state)
    assert n.grade == "—" and n.reason == "non calculé"


def test_score_detail_delta_g_signed(demo):
    bane = [c for c in _st(demo).state.run.deck if c.is_curse][0]
    head = views.score_detail(bane.score)[0]
    assert "ΔG -4.5" in head and "5/100" in head
