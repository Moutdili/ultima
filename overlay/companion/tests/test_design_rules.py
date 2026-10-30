"""Application des règles de design §9 à §12 (skill sts2-design-system) au compagnon, sans affichage.

§9 lettres S-F toujours écrites, couleurs du jeu · §10 lisibilité (≥ 14 px), raccourcis Super+Maj, jamais F8/F9 ·
§11 minimalisme : une grammaire commune avec le HUD (libellés, ordre), au plus un accent par ligne, rien sans
valeur pratique (groupes vides masqués, aucun titre décoratif) · §12 : dévoilement bref, aucune animation en boucle.
"""

import copy
import re

from conftest import COMPANION, REPO
from ultima_companion import appearance, settings, state, theme, tones, views

RENDER_JS = REPO / "overlay" / "gnome" / "ultima@mio" / "render.js"
GRAMMAR_JS = REPO / "overlay" / "gnome" / "ultima@mio" / "grammar.js"
GRADES = {"S", "A", "B", "C", "D", "F", "—"}


def _resolved(size=None):
    app = appearance.CompanionAppearance()
    if size is not None:
        app.set_font_size(size)
    return appearance.resolve(app, dirs=[REPO / "themes"], use_fontconfig=False)


def _css(size=None) -> str:
    return theme.css(_resolved(size))


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

def test_grade_letter_always_written_and_toned_from_the_theme(demo, sample):
    for data in (demo, sample):
        for it in _all_items(data):
            assert it.grade in GRADES, it
            assert tones.grade_tone(it.grade) in tones.ALL
            assert theme.grade_class(it.grade) in theme.GRADE_CLASS.values()


def test_curse_is_curse_tone_plus_the_word_and_enchantment_is_enchant_tone_plus_its_name(demo):
    items = _all_items(demo)
    curses = [i for i in items if i.tag_tone == "curse"]
    assert curses and all(i.tag == "Malédiction" for i in curses)
    ench = [i for i in items if i.tag_tone == "enchant"]
    assert ench and all(i.tag and i.tag[-1].isdigit() for i in ench)   # « Vivacité (démo) 1 »


def test_grade_thresholds_are_the_contract_ones():
    assert [state.grade_from_total(t) for t in (100, 85, 84, 70, 69, 55, 54, 40, 39, 25, 24, 0)] == \
        ["S", "S", "A", "A", "B", "B", "C", "C", "D", "D", "F", "F"]


# ---- §10 -----------------------------------------------------------------------------------------------

def test_min_font_size_14px_by_default_and_12px_when_chosen():
    css = _css()
    sizes = [int(s) for s in re.findall(r"font-size:\s*(\d+)px", css)]
    assert sizes and min(sizes) >= 14
    # le contrat autorise 12–24 px : 12 px est honoré pour le corps, le chrome reste ≥ 14 px
    chosen = _css(12)
    assert "font-size: 12px" in chosen
    values = [int(s) for s in re.findall(r"\.value \{[^}]*font-size:\s*(\d+)px", chosen)]
    assert values and min(values) >= 14


def test_shortcuts_are_super_shift_never_function_keys():
    from ultima_companion import ui
    assert all(k.startswith("Super+Maj+") for k, _ in ui.SHORTCUTS)
    for p in (COMPANION / "ultima_companion").glob("*.py"):
        assert not re.search(r"\bF(8|9)\b", p.read_text(encoding="utf-8")), p.name


# ---- §11 -----------------------------------------------------------------------------------------------

def test_module_labels_are_the_hud_labels():
    """Une seule grammaire : le libellé de chaque module du HUD (grammar.js MODULE_LABEL) = celui du compagnon."""
    m = re.search(r"MODULE_LABEL = Object\.freeze\(\{([^}]*)\}", GRAMMAR_JS.read_text(encoding="utf-8"))
    labels = dict(re.findall(r"([a-z_]+): '([^']+)'", m[1]))
    assert {"deck", "enchanted", "curses", "relics", "potions", "reward", "shop", "paths", "deck_score"} <= set(labels)
    for mid, label in labels.items():
        assert settings.module_label(mid) == label, mid


def test_hud_module_ids_are_a_prefix_order_of_the_contract_order():
    m = re.search(r"MODULE_IDS = Object\.freeze\(\[([^\]]*)\]", RENDER_JS.read_text(encoding="utf-8"))
    hud_ids = re.findall(r"'(\w+)'", m[1])
    assert [i for i in settings.DEFAULT_ORDER if i in hud_ids] == hud_ids


def test_at_most_one_accent_per_line(demo):
    for it in _all_items(demo):
        assert not (it.tone and it.tag_tone), it        # nom coloré (personnage) OU mot coloré, jamais les deux


def test_secondary_text_uses_the_theme_muted_tone():
    resolved = _resolved()
    css = theme.css(resolved)
    assert re.search(r"\.secondary \{ color: " + re.escape(tones.rgba(resolved.tones["muted"], 1.0)),
                     css)


def test_nothing_without_practical_value(sample):
    st = state.parse_state(sample).state
    groups = views.run_groups(st)
    keys = [g.key for g in groups]
    # v1.4 (décision de Mio) : la page Run montre TOUS les modules des réglages, même vides ;
    # un bloc vide porte une phrase calme, jamais un titre seul.
    assert keys == ["deck", "enchanted", "curses", "relics", "potions", "party"]
    for g in groups:
        assert g.items or g.empty
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
    css = _css()
    assert "@keyframes" not in css and "animation" not in css
    assert "transition" not in css            # pas d'animation CSS implicite non maîtrisée
    src = (COMPANION / "ultima_companion" / "ui.py").read_text(encoding="utf-8")
    assert "timeout_add" not in src.replace("GLib.timeout_add(SAVE_DELAY_MS", "")   # aucun minuteur d'animation
