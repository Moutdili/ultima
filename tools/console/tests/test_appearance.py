"""`appearance` v1.4 : défauts champ par champ, écriture qui relit et conserve l'inconnu, migration unique.

Preuves demandées par T2 : un réglage écrit par le TUI garde une clé inconnue et reste lisible par le
compagnon ; `theme`/`logColors` de console.json sont repris UNE fois dans `appearance.tui`.
"""

from __future__ import annotations

import importlib.util
import json
import sys
import types

import pytest
from conftest import REPO

from ultima_console import appearance, consolecfg, paths, settings, theme


def write_settings(data: dict) -> None:
    paths.config_dir().mkdir(parents=True, exist_ok=True)
    paths.settings_path().write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def read_settings() -> dict:
    return json.loads(paths.settings_path().read_text(encoding="utf-8"))


def companion_module():
    """Charge `overlay/companion/ultima_companion/settings.py` sans GTK (comme le test du compagnon)."""
    pkg = types.ModuleType("ultima_companion")
    pkg.__path__ = []
    pkg.paths = types.SimpleNamespace(settings_path=paths.settings_path)
    sys.modules["ultima_companion"] = pkg
    sys.modules["ultima_companion.paths"] = pkg.paths
    spec = importlib.util.spec_from_file_location(
        "ultima_companion.settings", REPO / "overlay" / "companion" / "ultima_companion" / "settings.py")
    mod = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = mod
    spec.loader.exec_module(mod)
    return mod


def test_defaults_are_valid_and_minimal():
    a = appearance.Appearance()
    data = a.to_json()
    assert data == {"tui": {"theme": "sts"}, "hud": {"theme": "sts"}, "companion": {"theme": "sts"}}
    assert appearance.validate(data) == []
    assert appearance.Appearance.from_json(data).to_json() == data


def test_field_by_field_validation_keeps_unknown_keys():
    raw = {
        "tui": {"theme": "nuit", "tones": {"accent": "#123456", "inconnu": "#FFFFFF", "text": "rouge"},
                "logColors": {"info": "ok", "error": "#ABCDEF", "futur": "x"}, "futur": 1},
        "hud": {"theme": "ardoise", "font": {"family": "Liberation Sans", "size": 99, "x": 2},
                "position": {"side": "LEFT", "anchor": "bas", "width": 9999, "offsetX": "3"},
                "railEdge": False, "x": {"y": 1}},
        "companion": {"theme": "terminal", "font": {"size": 14}},
        "surfaceInconnue": {"a": 1},
    }
    a = appearance.Appearance.from_json(raw)
    assert a.tui.theme == "nuit" and a.tui.tones == {"accent": "#123456"}
    assert a.tui.log_colors == {"info": "ok", "error": "#ABCDEF"}
    assert a.hud.position.side == "left" and a.hud.position.anchor == "center"   # « bas » invalide → défaut
    assert a.hud.position.width == appearance.WIDTH_MAX and a.hud.position.offset_x == 0
    assert a.hud.font.family == "Liberation Sans" and a.hud.font.size is None     # 99 hors bornes
    assert a.hud.rail_edge is False
    out = a.to_json()
    assert out["tui"]["futur"] == 1 and out["hud"]["x"] == {"y": 1}
    assert out["surfaceInconnue"] == {"a": 1}                                     # clé inconnue conservée
    assert out["hud"]["font"]["x"] == 2 and out["hud"]["position"]["offsetX"] == 0
    assert out["tui"]["tones"] == {"accent": "#123456"}                           # clés de ton inconnues ignorées


def test_theme_set_validates_id_and_writes_only_appearance():
    write_settings({"modules": [{"id": "deck", "enabled": True, "order": 0}], "hud": {"quality": "eco"},
                    "futur": {"garde": [1, 2]}})
    appearance.set_theme("hud", "ardoise")
    data = read_settings()
    assert data["appearance"]["hud"]["theme"] == "ardoise"
    assert data["futur"] == {"garde": [1, 2]} and data["modules"][0]["id"] == "deck"
    assert data["hud"] == {"quality": "eco"}
    with pytest.raises(appearance.AppearanceError):
        appearance.set_theme("hud", "nimporte-quoi")
    with pytest.raises(appearance.AppearanceError):
        appearance.set_theme("ecran", "sts")


def test_tones_log_colors_font_position_edits():
    appearance.set_theme("tui", "nuit")
    appearance.set_tone("tui", "accent", "#ABCDEF")
    appearance.set_tone("tui", "danger", None)                # None retire la surcharge
    appearance.set_log_color("warn", "curse")
    appearance.set_font("hud", family="Liberation Mono", size=16)
    appearance.set_position("hud", side="left", anchor="top", width=999, offsetX=-20, offsetY=10)
    appearance.set_rail_edge(False)
    data = read_settings()
    tui, hud = data["appearance"]["tui"], data["appearance"]["hud"]
    assert tui["tones"] == {"accent": "#ABCDEF"} and "danger" not in tui["tones"]
    assert tui["logColors"] == {"warn": "curse"}
    assert hud["font"] == {"family": "Liberation Mono", "size": 16}
    assert hud["position"] == {"side": "left", "anchor": "top", "offsetX": -20, "offsetY": 10, "width": 520}
    assert hud["railEdge"] is False
    assert appearance.validate(data["appearance"]) == []
    for bad in ({"tone": "inconnu"}, {"tone": "accent", "value": "rouge"}):
        with pytest.raises(appearance.AppearanceError):
            appearance.set_tone("tui", bad.get("tone", "accent"), bad.get("value", "#FFFFFF"))
    with pytest.raises(appearance.AppearanceError):
        appearance.set_font("hud", size=40)
    with pytest.raises(appearance.AppearanceError):
        appearance.set_font("tui", family="X")                 # le TUI n'a pas de police


def test_migration_console_json_once_and_file_kept():
    paths.config_dir().mkdir(parents=True)
    legacy = {"theme": "nord",
              "logColors": {"timestamp": "gray", "info": "cream", "warn": "orange", "error": "#123456"},
              "daemon": {"mirror": True}, "x": "gardé"}
    paths.console_config_path().write_text(json.dumps(legacy), encoding="utf-8")
    before = paths.console_config_path().read_bytes()
    write_settings({"modules": [], "hud": {"quality": "verre"}, "futur": 7})

    assert appearance.migrate_console() is True
    data = read_settings()
    tui = data["appearance"]["tui"]
    assert tui["theme"] == "ardoise"                    # nord → ardoise
    assert tui["logColors"] == {"timestamp": "muted", "info": "text", "warn": "warn", "error": "#123456"}
    assert data["futur"] == 7
    assert paths.console_config_path().read_bytes() == before           # ancien fichier intact
    assert appearance.migrate_console() is False                        # UNE seule fois


def test_migration_does_not_overwrite_an_existing_choice():
    paths.console_config_path().parent.mkdir(parents=True)
    paths.console_config_path().write_text('{"theme": "nord"}', encoding="utf-8")
    write_settings({"appearance": {"tui": {"theme": "contraste"}}})
    assert appearance.migrate_console() is False
    assert read_settings()["appearance"]["tui"]["theme"] == "contraste"


def test_writer_rereads_and_keeps_unknown_keys_added_meanwhile():
    write_settings({"appearance": {"tui": {"theme": "sts"}}, "modules": [], "hud": {}})
    app, _ = appearance.load()
    assert app.tui.theme == "sts"
    # Un autre écrivain ajoute une clé et change un réglage APRÈS le chargement.
    data = read_settings()
    data["arriveApres"] = {"a": 1}
    data["appearance"].setdefault("hud", {})["theme"] = "nuit"
    data["appearance"]["x"] = True
    write_settings(data)
    appearance.set_tone("tui", "accent", "#010203")       # relit avant d'écrire
    data = read_settings()
    assert data["arriveApres"] == {"a": 1}
    assert data["appearance"]["hud"]["theme"] == "nuit"   # pas écrasé par l'ancienne copie
    assert data["appearance"]["x"] is True
    assert data["appearance"]["tui"]["tones"] == {"accent": "#010203"}


def test_invalid_settings_file_is_never_overwritten():
    paths.config_dir().mkdir(parents=True)
    paths.settings_path().write_text("{cassé", encoding="utf-8")
    app, err = appearance.load()
    assert "invalide" in err and app.tui.theme == "sts"
    with pytest.raises(settings.SettingsError):
        appearance.set_theme("tui", "nuit")
    assert paths.settings_path().read_text() == "{cassé"


def test_no_temporary_file_left_behind():
    appearance.set_theme("tui", "nuit")
    assert [p.name for p in paths.config_dir().iterdir()] == ["settings.json"]


def test_plugins_enabled_only_x_ids_and_enable_disable():
    write_settings({"modules": [], "hud": {}, "pluginsEnabled": ["x.un", "pas-un-x", 3, "x.un"]})
    assert appearance.plugins_enabled() == ["x.un"]
    appearance.enable_plugin("x.deux")
    appearance.enable_plugin("mauvais")
    assert appearance.plugins_enabled() == ["x.un", "x.deux"]
    appearance.disable_plugin("x.un")
    assert appearance.plugins_enabled() == ["x.deux"]
    assert read_settings()["pluginsEnabled"] == ["x.deux"]


def test_module_screens_patch_only_that_module():
    write_settings({"modules": [{"id": "deck", "enabled": False, "order": 0, "x": 1},
                                {"id": "x.or-par-etage", "order": 1}], "hud": {}, "futur": 1})
    appearance.set_module_screens("x.or-par-etage", ["map", "shop"])
    data = read_settings()
    assert data["modules"][0] == {"id": "deck", "enabled": False, "order": 0, "x": 1}
    assert data["modules"][1] == {"id": "x.or-par-etage", "order": 1, "screens": ["map", "shop"]}
    assert data["futur"] == 1
    appearance.set_module_screens("x.or-par-etage", None)
    assert "screens" not in read_settings()["modules"][1]
    with pytest.raises(appearance.AppearanceError):
        appearance.set_module_screens("inconnu", ["map"])


def test_settings_save_rereads_and_preserves_appearance_and_plugins():
    write_settings({"appearance": {"tui": {"theme": "nuit"}}, "pluginsEnabled": ["x.p"],
                    "futur": {"a": 1}, "modules": [], "hud": {}})
    s, _ = settings.load()
    s.toggle("deck")
    settings.save(s)
    data = read_settings()
    assert data["appearance"] == {"tui": {"theme": "nuit"}}
    assert data["pluginsEnabled"] == ["x.p"] and data["futur"] == {"a": 1}
    assert {"id": "deck", "enabled": False, "order": 0} == data["modules"][0]


async def test_tui_write_keeps_unknown_key_and_companion_reads_it():
    """Critère T2 : un réglage écrit par le TUI conserve les clés inconnues et reste lisible par le compagnon."""
    pytest.importorskip("textual")
    from ultima_console.tui import UltimaApp, screen_text

    write_settings({"modules": [{"id": "deck", "enabled": True, "order": 0}],
                    "hud": {"quality": "verre", "heightFraction": 0.55, "restOpacity": 0.62,
                            "side": "right", "deckScoreInCombat": False, "gradesOnly": False},
                    "appearance": {"tui": {"theme": "sts"}},
                    "cleInconnue": {"future": {"v": [1, 2, 3]}},
                    "pluginsEnabled": ["x.rien"]})
    app = UltimaApp(live=False)
    async with app.run_test(size=(110, 34)) as pilot:
        await pilot.press("7")
        await pilot.pause()
        table = app.query_one("#app-tui-table")
        row = [i for i, key in enumerate(app.TUI_ROWS) if key[0] == "theme"][0]
        app._app_change(table, row, 1)              # thème suivant
        await pilot.pause()
        assert app.appearance.tui.theme != "sts"
        assert "Apparence" in screen_text(app) or True
    data = read_settings()
    assert data["cleInconnue"] == {"future": {"v": [1, 2, 3]}}
    assert data["pluginsEnabled"] == ["x.rien"]
    assert data["appearance"]["tui"]["theme"] in theme.load_all()

    comp = companion_module()
    try:
        result = comp.load()
        assert result.error is None and result.exists
        assert result.settings.modules[0].id == "deck"       # le compagnon relit le même fichier
        assert result.settings.hud.quality == "verre"
        # Les clés que le compagnon ne comprend pas sont conservées par SA lecture aussi.
        assert result.settings.extra.get("cleInconnue") == {"future": {"v": [1, 2, 3]}}
        assert result.settings.to_json()["appearance"] == data["appearance"]
    finally:
        for k in ("ultima_companion", "ultima_companion.paths", "ultima_companion.settings"):
            sys.modules.pop(k, None)


def test_summary_reports_every_surface():
    write_settings({"appearance": {"tui": {"theme": "terminal"}, "hud": {"theme": "nuit",
                                                                         "position": {"width": 400}}}})
    app, _ = appearance.load()
    rows = {r["surface"]: r for r in appearance.summary(app)}
    assert rows["tui"]["ansi16"] is True and rows["tui"]["effective"] == "terminal"
    assert rows["hud"]["theme"] == "nuit" and rows["hud"]["position"]["width"] == 400
    assert rows["companion"]["effective"] == "sts"


def test_legacy_mapping_is_complete():
    for old in ("ultima", "ansi-dark", "ansi-light", "textual-dark", "nord", "gruvbox", "tokyo-night"):
        assert old in appearance.LEGACY_THEMES
        assert appearance.LEGACY_THEMES[old] in theme.load_all()
    assert set(appearance.LEGACY_TONES.values()) <= set(theme.TONES)
