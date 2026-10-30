"""Modules perso et plugins (EngineState.Custom, contrat v1.4) : lecture, écrans, santé, page Run."""

import copy

import pytest

from ultima_companion import ipc, state, textview, views


def _state(data, **changes):
    payload = copy.deepcopy(data)
    payload.update(changes)
    result = state.parse_state(payload)
    assert result.kind == state.OK, result.message
    return result


CUSTOM = [
    {
        "id": "x.compteur-or", "label": "Or / étage", "source": "module", "screens": ["map", "shop"],
        "rows": [{"text": "116 or · étage 4", "tone": "accent"},
                 {"text": "Shop rentable", "tone": "ok", "grade": "A"}],
        "health": "ok", "problem": None,
    },
    {
        "id": "x.plugin-stats", "label": "Stats du plugin", "source": "plugin",
        "rows": [{"text": "hors délai", "tone": "pas-un-ton", "grade": "Z"}, {"text": 3}],
        "health": "stale", "problem": "dernière sortie conservée",
    },
]


# ---- lecture tolérante -----------------------------------------------------------------------------

def test_custom_detail_parses(demo):
    st = _state(demo, custom=CUSTOM).state
    assert [c.id for c in st.custom] == ["x.compteur-or", "x.plugin-stats"]
    first, second = st.custom
    assert (first.label, first.source, first.screens, first.health, first.problem) == \
        ("Or / étage", "module", ["map", "shop"], "ok", None)
    assert [(r.text, r.tone, r.grade) for r in first.rows] == \
        [("116 or · étage 4", "accent", None), ("Shop rentable", "ok", "A")]
    assert second.screens is None and second.health == "stale" and second.source == "plugin"
    assert second.rows == [state.CustomRow("hors délai", "pas-un-ton", None)]   # texte 3 + note Z ignorés


def test_custom_absent_or_garbage_is_empty(demo):
    assert _state(demo).state.custom == []
    for bad in (None, "custom", 3, [None, 4, "x"], [{"id": "sans-x"}, {"id": 3}]):
        assert _state(demo, custom=bad).state.custom == []


def test_non_x_ids_are_ignored_and_duplicates_dropped(demo):
    st = _state(demo, custom=[{"id": "deck", "label": "faux", "rows": []},
                              {"id": "x.d", "label": "un", "rows": []},
                              {"id": "x.d", "label": "deux", "rows": []}]).state
    assert [c.id for c in st.custom] == ["x.d"] and st.custom[0].label == "un"


def test_unknown_health_becomes_error_and_unknown_source_becomes_module(demo):
    st = _state(demo, custom=[{"id": "x.h", "label": "H", "health": "bizarre", "source": "magie",
                               "rows": []}]).state
    module = st.custom[0]
    assert module.health == "error" and "santé inconnue" in module.problem
    assert module.source == "module" and module.degraded


@pytest.mark.parametrize("health", state.CUSTOM_HEALTH)
def test_every_contract_health_is_accepted(demo, health):
    st = _state(demo, custom=[{"id": "x.h", "label": "H", "health": health, "rows": []}]).state
    assert st.custom[0].health == health


def test_screens_null_is_default_and_empty_list_is_nowhere(demo):
    st = _state(demo, custom=[{"id": "x.a", "label": "A", "screens": None, "rows": []},
                              {"id": "x.b", "label": "B", "screens": [], "rows": []},
                              {"id": "x.c", "label": "C", "screens": ["map", 3], "rows": []}]).state
    assert [c.screens for c in st.custom] == [None, [], ["map"]]


def test_custom_for_filters_by_screen(demo):
    st = _state(demo, custom=CUSTOM).state
    assert [c.id for c in st.custom_for("Map")] == ["x.compteur-or", "x.plugin-stats"]
    assert [c.id for c in st.custom_for("Combat")] == ["x.plugin-stats"]
    assert [c.id for c in st.custom_for(None)] == ["x.compteur-or", "x.plugin-stats"]


# ---- dessin (blocs de même grammaire que les autres) -------------------------------------------------

def test_custom_group_renders_rows_with_grades_and_tones(demo):
    st = _state(demo, custom=CUSTOM).state
    group = views.custom_group(st.custom[0])
    assert group.key == "x.compteur-or" and group.title == "Or / étage"
    assert group.kind == "custom" and group.source == "module"
    assert [i.label for i in group.items] == ["116 or · étage 4", "Shop rentable"]
    assert [i.grade for i in group.items] == ["—", "A"]
    assert [i.tone for i in group.items] == ["accent", "ok"]
    assert group.note == "" and not group.dim


@pytest.mark.parametrize("health,note,dim", [
    ("stale", "en retard (dernière sortie conservée)", False),
    ("error", "erreur · panne", False),
    ("disabled", "désactivé · panne", True),
])
def test_custom_health_is_discreet_and_never_colour_only(demo, health, note, dim):
    st = _state(demo, custom=[{"id": "x.s", "label": "S", "health": health, "problem": "panne",
                               "rows": [{"text": "ligne"}]}]).state
    group = views.custom_group(st.custom[0])
    assert group.note == note and group.dim is dim
    assert group.items and group.items[0].dim is dim


def test_unknown_tone_falls_back_to_text_not_a_broken_class(demo):
    st = _state(demo, custom=CUSTOM).state
    group = views.custom_group(st.custom[1])
    assert group.items[0].tone is None and group.items[0].grade == "—"


def test_custom_groups_follow_settings_order_and_screens(demo):
    st = _state(demo, custom=CUSTOM).state
    order = ["deck", "x.plugin-stats", "relics", "x.compteur-or"]
    assert [g.key for g in views.custom_groups(st, order)] == ["x.plugin-stats", "x.compteur-or"]
    assert [g.key for g in views.custom_groups(st, order, screen="Combat")] == ["x.plugin-stats"]
    assert [g.key for g in views.custom_groups(st, order, screen="Shop")] == \
        ["x.plugin-stats", "x.compteur-or"]


def test_custom_labels(demo):
    st = _state(demo, custom=CUSTOM).state
    assert views.custom_labels(st) == {"x.compteur-or": "Or / étage", "x.plugin-stats": "Stats du plugin"}


# ---- page Run : tous les modules des réglages, dans l'ordre --------------------------------------------

def test_run_blocks_include_deck_score_at_its_settings_place(demo):
    st = _state(demo).state
    order = ["deck", "deck_score", "relics", "potions"]
    blocks = views.run_blocks(st, order)
    assert [b.key for b in blocks][:4] == ["deck", "deck_score", "relics", "potions"]
    assert blocks[1].kind == "deck_score"
    assert views.run_sequence(["deck_score", "deck"]) == ["deck_score", "deck", "enchanted",
                                                          "curses", "relics", "potions", "party"]


def test_run_blocks_show_disabled_modules_with_a_note(demo):
    st = _state(demo).state
    blocks = views.run_blocks(st, ["deck", "relics"], enabled={"deck": True, "relics": False})
    by_key = {b.key: b for b in blocks}
    assert by_key["relics"].note == "désactivé dans le HUD"        # montré quand même
    assert by_key["deck"].note == ""


def test_run_blocks_include_custom_modules_in_order_and_unpublished_ones(demo):
    st = _state(demo, custom=CUSTOM).state
    order = ["deck", "x.compteur-or", "relics", "x.inconnu"]
    blocks = views.run_blocks(st, order, labels={"x.inconnu": "Module à venir"})
    keys = [b.key for b in blocks]
    assert keys[:3] == ["deck", "x.compteur-or", "relics"]
    assert "x.inconnu" in keys
    unpublished = [b for b in blocks if b.key == "x.inconnu"][0]
    assert unpublished.kind == "custom" and unpublished.empty == "Non publié par le daemon"
    assert unpublished.title == "Module à venir"


def test_run_blocks_without_run_is_empty(demo):
    st = _state(demo, run=None).state
    assert views.run_blocks(st, ["deck"]) == []


def test_run_blocks_show_a_published_module_absent_from_settings(demo):
    """La page Run est la vue complète : un module `x.*` publié mais absent des réglages est dessiné."""
    st = _state(demo, custom=[{"id": "x.nouveau", "label": "Nouveau", "rows": [{"text": "ligne"}]}]).state
    blocks = views.run_blocks(st, ["deck", "relics"])
    assert blocks[-1].key == "x.nouveau" and blocks[-1].title == "Nouveau"
    assert [b.key for b in blocks].count("x.nouveau") == 1


def test_status_sections_list_custom_modules(demo):
    st = _state(demo, custom=CUSTOM)
    sections = {s.title: s for s in textview.status_sections(st, ipc.ALIVE, None)}
    rows = {r.title: r.subtitle for r in sections["Modules perso"].rows}
    assert "Or / étage" in rows and "module" in rows["Or / étage"] and "ok" in rows["Or / étage"]
    assert "en retard" in rows["Stats du plugin"] and "dernière sortie" in rows["Stats du plugin"]
    assert "écrans : map, shop" in rows["Or / étage"]
