"""Apparence du compagnon (docs/44 §3) : lecture, résolution, écriture CIBLÉE et atomique."""

import json
import os
import stat
from pathlib import Path

import pytest

from conftest import REPO
from ultima_companion import appearance, paths, settings, textview, themes

THEMES = [REPO / "themes"]
STS = json.loads((REPO / "themes" / "sts.json").read_text(encoding="utf-8"))


def _app(**data):
    return appearance.CompanionAppearance.from_json(data or None)


def theme_dir(tmp_path: Path, **sts_extra) -> list[Path]:
    """Dossier de thèmes de test : `sts` (+ champs surchargés), écrit dans un dossier temporaire."""
    folder = tmp_path / "themes"
    folder.mkdir()
    data = dict(STS)
    data.update(sts_extra)
    (folder / "sts.json").write_text(json.dumps(data), encoding="utf-8")
    return [folder]


# ---- modèle ------------------------------------------------------------------------------------

def test_defaults_are_sts_and_no_font_choice():
    a = appearance.CompanionAppearance()
    assert a.theme == "sts" and a.font_family is None and a.font_size is None and a.tones == {}
    assert a.to_json() == {"theme": "sts", "font": {}, "tones": {}}


def test_from_json_reads_theme_font_and_tones_and_keeps_unknown_keys():
    a = appearance.CompanionAppearance.from_json({
        "theme": " ironclad ", "font": {"family": "Fira Sans", "size": 16},
        "tones": {"accent": "#FF0000", "inconnu": "#00FF00", "bad": 3},
        "future": {"x": 1},
    })
    assert a.theme == "ironclad" and a.font_family == "Fira Sans" and a.font_size == 16
    assert a.tones == {"accent": "#FF0000", "inconnu": "#00FF00"}
    out = a.to_json()
    assert out["future"] == {"x": 1}
    assert list(out) == ["theme", "font", "tones", "future"]
    assert list(out["font"]) == ["family", "size"]


@pytest.mark.parametrize("raw,expected", [
    ({"font": {"size": 8}}, 12), ({"font": {"size": 40}}, 24), ({"font": {"size": 14.6}}, 15),
    ({"font": {"size": "16"}}, None), ({"font": {"size": True}}, None), ({"font": {"size": None}}, None),
])
def test_font_size_is_bounded_12_24(raw, expected):
    assert appearance.CompanionAppearance.from_json(raw).font_size == expected


def test_unsafe_family_is_rejected():
    a = appearance.CompanionAppearance.from_json({"font": {"family": 'x"; }'}})
    assert a.font_family is None
    a.set_font_family("Fira Sans")
    assert a.font_family == "Fira Sans"
    a.set_font_family("")
    assert a.font_family is None


def test_set_theme_and_tones_validate():
    a = appearance.CompanionAppearance()
    a.set_theme("nuit")
    assert a.theme == "nuit"
    a.set_theme("")
    assert a.theme == "nuit"
    assert a.set_tone("accent", "#123456") is True
    assert a.tones == {"accent": "#123456"}
    assert a.set_tone("accent", "rouge") is False
    assert a.set_tone("pas-un-ton", "#123456") is False
    a.clear_tone("accent")
    assert a.tones == {}
    a.set_font_size(4)
    assert a.font_size == 12


# ---- lecture depuis settings.json ---------------------------------------------------------------

def test_read_companion_from_settings_object_and_file(isolated_xdg):
    data = {"modules": [], "hud": {}, "appearance": {"companion": {"theme": "nuit"},
                                                     "hud": {"theme": "ardoise"},
                                                     "tui": {"theme": "terminal"}},
            "pluginsEnabled": ["x.a"]}
    s = settings.OverlaySettings.from_json(data)
    assert appearance.read_companion(s).theme == "nuit"
    assert appearance.read_hud(s)["theme"] == "ardoise"
    assert appearance.read_tui(s)["theme"] == "terminal"
    p = paths.settings_path()
    p.parent.mkdir(parents=True)
    p.write_text(json.dumps(data), encoding="utf-8")
    assert appearance.read_companion().theme == "nuit"
    assert appearance.read_companion(p).theme == "nuit"
    assert appearance.read_companion({"appearance": {"companion": {"theme": "defect"}}}).theme == "defect"


def test_read_companion_tolerates_garbage():
    for data in (None, {}, {"appearance": None}, {"appearance": []}, {"appearance": {"companion": 3}}):
        assert appearance.read_companion(data).theme == "sts"


# ---- résolution thème + police -------------------------------------------------------------------

def test_resolve_default_is_sts_and_15px_system_font():
    res = appearance.resolve(_app(), dirs=THEMES, use_fontconfig=False)
    assert res.theme_id == "sts" and res.theme_label == "Slay the Spire"
    assert res.font_size == 15 and res.font_source == "système" and res.font_family is None
    assert res.messages == [] and res.fallback is False


def test_resolve_uses_the_theme_font_when_no_choice(tmp_path):
    """Thème qui porte `font.family` : la police du thème est cherchée dans les dossiers."""
    dirs = theme_dir(tmp_path, font={"family": "Fira Sans"})
    font_dir = tmp_path / "fonts"
    (font_dir / "base").mkdir(parents=True)
    from test_fonts import make_font
    make_font(font_dir / "base" / "f.ttf", "Fira Sans")
    res = appearance.resolve(_app(), dirs=dirs, font_root=font_dir, use_fontconfig=False)
    assert res.font_family == "Fira Sans" and res.font_source == "base"
    assert res.font_file is not None and res.font_css.startswith('"Fira Sans"')


def test_resolve_font_not_found_falls_back_to_theme_then_system(tmp_path):
    dirs = theme_dir(tmp_path, font={"family": "Fira Sans"})
    font_dir = tmp_path / "fonts"
    (font_dir / "perso").mkdir(parents=True)
    res = appearance.resolve(_app(font={"family": "Inexistante"}), dirs=dirs,
                             font_root=font_dir, use_fontconfig=False)
    assert res.font_family == "Fira Sans"              # police du thème
    assert res.font_message and "Inexistante" in res.font_message


def test_resolve_explicit_font_wins(tmp_path):
    font_dir = tmp_path / "fonts"
    (font_dir / "perso").mkdir(parents=True)
    from test_fonts import make_font
    make_font(font_dir / "perso" / "p.ttf", "Ma Police")
    res = appearance.resolve(_app(font={"family": "Ma Police", "size": 20}), dirs=THEMES,
                             font_root=font_dir, use_fontconfig=False)
    assert res.font_family == "Ma Police" and res.font_size == 20
    assert res.font_source == "perso"


def test_resolve_broken_theme_carries_the_visible_message(tmp_path):
    folder = tmp_path / "themes"
    folder.mkdir()
    (folder / "sts.json").write_text(json.dumps(STS), encoding="utf-8")
    (folder / "casse.json").write_text("{ nope", encoding="utf-8")
    res = appearance.resolve(_app(theme="casse"), dirs=[folder], use_fontconfig=False)
    assert res.theme_id == "sts" and res.fallback
    message = textview.appearance_message(res)
    assert message and "casse" in message and "Repli sur « sts »" in message


def test_resolve_auto_perso_uses_the_character(tmp_path):
    folder = tmp_path / "themes"
    folder.mkdir()
    (folder / "sts.json").write_text(json.dumps(STS), encoding="utf-8")
    (folder / "ironclad.json").write_text(json.dumps({
        "id": "ironclad", "label": "Ironclad", "api": 1, "extends": "sts",
        "tones": {"accent": "#FF5555"}}), encoding="utf-8")
    res = appearance.resolve(_app(theme="auto-perso"), run_character="CHARACTER.IRONCLAD",
                             dirs=[folder], use_fontconfig=False)
    assert res.theme_id == "ironclad" and res.tones["accent"] == "#FF5555"
    res = appearance.resolve(_app(theme="auto-perso"), run_character=None,
                             dirs=[folder], use_fontconfig=False)
    assert res.theme_id == "sts"


def test_resolve_tones_override_reaches_the_css():
    from ultima_companion import theme
    res = appearance.resolve(_app(tones={"accent": "#123456"}), dirs=THEMES,
                             use_fontconfig=False)
    assert "rgba(18, 52, 86, 1)" in theme.css(res)


# ---- écriture : uniquement appearance.companion ----------------------------------------------------

def test_save_companion_preserves_everything_else_and_is_atomic(isolated_xdg):
    p = paths.settings_path()
    p.parent.mkdir(parents=True)
    original = {
        "modules": [{"id": "deck", "enabled": True, "order": 0, "screens": ["map"]}],
        "hud": {"quality": "eco", "heightFraction": 0.5, "restOpacity": 0.5, "side": "right",
                "deckScoreInCombat": False, "gradesOnly": False},
        "appearance": {"hud": {"theme": "ardoise", "font": {"family": "Fira Sans", "size": 16}},
                       "tui": {"theme": "terminal", "tones": {"warn": "warn"}},
                       "companion": {"theme": "sts", "font": {}, "tones": {}}},
        "pluginsEnabled": ["x.a"],
        "zz": {"futur": True},
    }
    p.write_text(json.dumps(original), encoding="utf-8")
    inode = p.stat().st_ino
    app = appearance.read_companion(p)
    app.set_theme("nuit")
    app.set_font_family("Kreon")
    app.set_font_size(18)
    app.set_tone("accent", "#112233")
    written = appearance.save_companion(app, p)
    assert written == p
    raw = json.loads(p.read_text(encoding="utf-8"))
    assert raw["appearance"]["companion"] == {"theme": "nuit",
                                              "font": {"family": "Kreon", "size": 18},
                                              "tones": {"accent": "#112233"}}
    assert raw["appearance"]["hud"] == original["appearance"]["hud"]      # jamais modifié
    assert raw["appearance"]["tui"] == original["appearance"]["tui"]
    assert raw["modules"] == original["modules"] and raw["hud"] == original["hud"]
    assert raw["pluginsEnabled"] == ["x.a"] and raw["zz"] == {"futur": True}
    assert p.stat().st_ino != inode                                       # rename atomique
    assert sorted(os.listdir(p.parent)) == ["settings.json"]              # aucun tmp
    assert stat.S_IMODE(p.stat().st_mode) == 0o644


def test_save_companion_creates_the_file_when_absent(isolated_xdg):
    p = appearance.save_companion(_app(theme="nuit"))
    assert p == paths.settings_path()
    raw = json.loads(p.read_text(encoding="utf-8"))
    assert raw == {"appearance": {"companion": {"theme": "nuit", "font": {}, "tones": {}}}}


def test_save_companion_refuses_to_clobber_an_invalid_file(isolated_xdg):
    p = paths.settings_path()
    p.parent.mkdir(parents=True)
    p.write_text("{ pas du json", encoding="utf-8")
    with pytest.raises(appearance.AppearanceWriteError) as err:
        appearance.save_companion(_app(theme="nuit"))
    assert "illisible" in str(err.value)
    assert p.read_text(encoding="utf-8") == "{ pas du json"


def test_save_companion_keeps_unknown_keys_of_appearance_companion(isolated_xdg):
    p = paths.settings_path()
    p.parent.mkdir(parents=True)
    p.write_text(json.dumps({"appearance": {"companion": {"theme": "sts", "future": 7}}}),
                 encoding="utf-8")
    app = appearance.read_companion(p)
    assert app.extra == {"future": 7}
    app.set_theme("nuit")
    appearance.save_companion(app)
    raw = json.loads(p.read_text(encoding="utf-8"))
    assert raw["appearance"]["companion"]["future"] == 7


def test_save_companion_reuses_existing_key_case(isolated_xdg):
    p = paths.settings_path()
    p.parent.mkdir(parents=True)
    p.write_text(json.dumps({"Appearance": {"Companion": {"theme": "nuit"}, "hud": {}}}),
                 encoding="utf-8")
    app = appearance.read_companion(p)
    assert app.theme == "nuit"
    app.set_theme("sts")
    appearance.save_companion(app, p)
    raw = json.loads(p.read_text(encoding="utf-8"))
    assert list(raw) == ["Appearance"]
    assert [k for k in raw["Appearance"] if k.lower() == "companion"] == ["Companion"]
    assert raw["Appearance"]["Companion"]["theme"] == "sts"
    assert raw["Appearance"]["hud"] == {}


def test_background_override_changes_the_window_tint():
    res = appearance.resolve(_app(tones={"background": "#101010"}), dirs=THEMES,
                             use_fontconfig=False)
    assert res.glass_tint == "#101010"
    from ultima_companion import theme
    assert "rgba(16, 16, 16, 1)" in theme.css(res)


# ---- autres surfaces : lecture seule + commande `ultima` --------------------------------------------

def test_surfaces_show_hud_and_tui_read_only_with_commands(isolated_xdg):
    p = paths.settings_path()
    p.parent.mkdir(parents=True)
    p.write_text(json.dumps({"appearance": {
        "hud": {"theme": "nuit", "font": {"family": "Fira Sans", "size": 16}},
        "tui": {"theme": "terminal"},
        "companion": {"theme": "auto-perso"},
    }}), encoding="utf-8")
    rows = {s.key: s for s in appearance.surfaces()}
    assert rows["hud"].editable is False
    assert rows["hud"].theme == "nuit" and "Fira Sans" in rows["hud"].font
    assert rows["hud"].theme_command == "ultima theme set hud nuit"
    assert 'ultima font set hud "Fira Sans" 16' == rows["hud"].font_command
    assert rows["tui"].editable is False and rows["tui"].theme == "terminal"
    assert rows["tui"].theme_command == "ultima theme set tui terminal"
    assert rows["compagnon"].editable is True
    assert rows["compagnon"].theme_command == "ultima theme set compagnon auto-perso"


def test_surfaces_defaults_without_appearance(isolated_xdg):
    rows = {s.key: s for s in appearance.surfaces()}
    assert rows["hud"].theme == "sts" and rows["tui"].theme == "terminal"
    assert "police du thème" in rows["hud"].font


# ---- message et lignes lisibles --------------------------------------------------------------------

def test_appearance_rows_and_message():
    res = appearance.resolve(_app(), dirs=THEMES, use_fontconfig=False)
    rows = textview.appearance_rows(res, appearance.surfaces())
    text = "\n".join(f"{r.title} : {r.subtitle}" for r in rows)
    assert "Thème demandé : sts" in text
    assert "Thème effectif : sts — Slay the Spire" in text
    assert "Police : " in text and "HUD" in text and "ultima theme set hud" in text
    assert textview.appearance_message(res) is None
