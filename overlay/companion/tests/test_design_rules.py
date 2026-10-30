"""Application des règles de design §9 à §12 (skill sts2-design-system) au compagnon, sans affichage.

§9 lettres S-F toujours écrites, couleurs du jeu · §10 lisibilité (≥ 14 px), raccourcis Super+Maj, jamais F8/F9 ·
§11 minimalisme : une grammaire commune avec le HUD (libellés, ordre), au plus un accent par ligne, rien sans
valeur pratique (groupes vides masqués, aucun titre décoratif) · §12 : dévoilement bref, aucune animation en boucle.
"""

import copy
import re

from conftest import COMPANION, REPO
from ultima_companion import palette, settings, state, theme, views

RENDER_JS = REPO / "overlay" / "gnome" / "ultima@mio" / "render.js"
GRADES = {"S", "A", "B", "C", "D", "F", "—"}


def _all_items(data) -> list[views.Item]:
    """Toutes les lignes que le compagnon peut afficher pour cet état, sur chaque écran de décision."""
    items = []
    for screen in ("Reward", "Shop", "Event", "Map", "Combat", "RestSite"):
        d = copy.deepcopy(data)
        d["screen"] = screen
        r = state.parse_state(d)
        items += views.decision(r).items
        items += [i for g in views.run_groups(r.state) for i in g.items]
    return items


# ---- §9 ------------------------------------------------------------------------------------------------

def test_grade_letter_always_written_and_toned_from_the_game_palette(demo, sample):
    for data in (demo, sample):
        for it in _all_items(data):
            assert it.grade in GRADES, it
            assert palette.grade_tone(it.grade) in palette.PALETTE
            assert theme.grade_class(it.grade) in theme.GRADE_CLASS.values()


def test_curse_is_purple_plus_the_word_and_enchantment_is_aqua_plus_its_name(demo):
    items = _all_items(demo)
    curses = [i for i in items if i.tag_tone == "purple"]
    assert curses and all(i.tag == "Malédiction" for i in curses)
    ench = [i for i in items if i.tag_tone == "aqua"]
    assert ench and all(i.tag and i.tag[-1].isdigit() for i in ench)   # « Vivacité (démo) 1 »


def test_grade_thresholds_are_the_contract_ones():
    assert [state.grade_from_total(t) for t in (100, 85, 84, 70, 69, 55, 54, 40, 39, 25, 24, 0)] == \
        ["S", "S", "A", "A", "B", "B", "C", "C", "D", "D", "F", "F"]


# ---- §10 -----------------------------------------------------------------------------------------------

def test_min_font_size_14px():
    sizes = [int(s) for s in re.findall(r"font-size:\s*(\d+)px", theme.css())]
    assert sizes and min(sizes) >= 14


def test_shortcuts_are_super_shift_never_function_keys():
    from ultima_companion import ui
    assert all(k.startswith("Super+Maj+") for k, _ in ui.SHORTCUTS)
    for p in (COMPANION / "ultima_companion").glob("*.py"):
        assert not re.search(r"\bF(8|9)\b", p.read_text(encoding="utf-8")), p.name


# ---- §11 -----------------------------------------------------------------------------------------------

def test_module_labels_are_the_hud_block_titles():
    """Une seule grammaire : le titre de chaque bloc du HUD (render.js) = le libellé du compagnon en capitales."""
    titles = dict(re.findall(r"block\('([a-z_]+)', [`']([^`'·$]+?) ·", RENDER_JS.read_text(encoding="utf-8")))
    titles.pop("title", None)
    assert {"deck", "enchanted", "curses", "relics", "potions", "reward", "shop", "paths", "deck_score"} <= set(titles)
    for mid, title in titles.items():
        assert settings.module_label(mid).upper() == title, mid


def test_hud_module_ids_are_a_prefix_order_of_the_contract_order():
    m = re.search(r"MODULE_IDS = Object\.freeze\(\[([^\]]*)\]", RENDER_JS.read_text(encoding="utf-8"))
    hud_ids = re.findall(r"'(\w+)'", m[1])
    assert [i for i in settings.DEFAULT_ORDER if i in hud_ids] == hud_ids


def test_at_most_one_accent_per_line(demo):
    for it in _all_items(demo):
        assert not (it.tone and it.tag_tone), it        # nom coloré (personnage) OU mot coloré, jamais les deux


def test_secondary_text_is_cream_60_percent():
    css = theme.css()
    assert re.search(r"\.secondary \{ color: " + re.escape(palette.rgba("cream", 0.6)), css)


def test_nothing_without_practical_value(sample):
    st = state.parse_state(sample).state
    keys = [g.key for g in views.run_groups(st)]
    # pas d'enchantée / de malédiction dans cette run → groupes absents, pas un « aucune » inutile
    if not any(c.enchantment for c in st.run.deck):
        assert "enchanted" not in keys
    if not any(c.is_curse for c in st.run.deck):
        assert "curses" not in keys
    assert "\n" not in views.deck_summary(st)                    # résumé : UNE ligne
    for screen in ("Combat", "RestSite", "Treasure"):
        d = copy.deepcopy(sample)
        d["screen"] = screen
        dec = views.decision(state.parse_state(d))
        assert not dec.items and dec.empty                       # rien à décider → une phrase calme, rien d'autre
    assert "ULTIMA" not in " ".join(settings.MODULE_LABELS.values())   # aucun titre décoratif dans les modules


def test_decision_shows_only_the_decision(demo):
    """§10-11 : au moment de décider, la décision occupe le panneau (aucun module de run dedans)."""
    d = copy.deepcopy(demo)
    d["screen"] = "Reward"
    dec = views.decision(state.parse_state(d))
    assert dec.kind == "reward" and all(i.grade in GRADES for i in dec.items)
    assert sum(i.best for i in dec.items) <= 1


# ---- §12 / §4 / §6 ------------------------------------------------------------------------------------

def test_detail_reveal_is_brief_and_nothing_loops():
    from ultima_companion import ui
    assert 150 <= ui.DETAIL_REVEAL_MS <= 220
    css = theme.css()
    assert "@keyframes" not in css and "animation" not in css
    assert "transition" not in css            # pas d'animation CSS implicite non maîtrisée
    src = (COMPANION / "ultima_companion" / "ui.py").read_text(encoding="utf-8")
    assert "timeout_add" not in src.replace("GLib.timeout_add(SAVE_DELAY_MS", "")   # aucun minuteur d'animation
