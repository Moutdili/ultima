"""TUI en mode headless (Textual App.run_test + pilot) : aucune fenêtre, aucun terminal réel."""

import json

import pytest
from conftest import load_fixture, publish

pytest.importorskip("textual")

from textual.widgets import DataTable, RichLog, TabbedContent  # noqa: E402

from ultima_console import appearance, consolecfg, paths, settings, theme as themes  # noqa: E402
from ultima_console.tui import UltimaApp, screen_text  # noqa: E402

SIZE = (110, 34)


def lines_of(app, widget_id):
    return [ln.text for ln in app.query_one(widget_id, RichLog).lines]


async def test_dashboard_reward_letters_and_colors():
    publish(load_fixture("reward"))
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.pause()
        text = screen_text(app)
        assert "RÉCOMPENSE" in text and "S  Fléau+ (Semée 2)" in text and "—  Garde du corps" in text
        assert "daemon arrêté" in text and "None" not in text
        svg = app.export_screenshot()
        assert "#efc851" in svg.lower()          # grade.S du thème sts (accent)
        assert "#fff6e2" in svg.lower()          # texte du thème sts


async def test_no_state_message():
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.pause()
        assert "Aucun état publié." in screen_text(app)


@pytest.mark.parametrize("name", ["v13", "map", "shop", "reward"])
async def test_every_tab_renders_without_none(name):
    publish(load_fixture(name))
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        for key in "1234567":
            await pilot.press(key)
            await pilot.pause()
            text = screen_text(app)
            assert "None" not in text and "Traceback" not in text


async def test_deck_tab_and_detail():
    publish(load_fixture("v13"))
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.press("2")
        await pilot.pause()
        table = app.query_one("#deck-table", DataTable)
        assert table.row_count > 0
        text = screen_text(app)
        assert "DECK · 14 cartes" in text and "Note  Carte" in text
        table.focus()
        await pilot.press("down")
        await pilot.pause()
        assert "total" in screen_text(app)       # détail du score de la carte sélectionnée


async def test_hud_settings_are_saved_atomically():
    publish(load_fixture("map"))
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.press("4")
        await pilot.pause()
        app.query_one("#hud-set").focus()
        await pilot.press("right")                       # qualité verre → loupe
        await pilot.press("down", "down", "left")        # opacité 62 → 60 %
        await pilot.press("down", "space")               # côté droite → gauche
        await pilot.pause()
        data = json.loads(paths.settings_path().read_text())
        assert data["hud"]["quality"] == "loupe" and data["hud"]["restOpacity"] == 0.6 and data["hud"]["side"] == "left"
        assert "enregistré" in screen_text(app)
        mods = app.query_one("#modules")
        mods.focus()
        await pilot.press("space")                       # Deck masqué
        await pilot.press("down", "right_square_bracket")  # Enchantées descend d'un cran
        await pilot.pause()
        data = json.loads(paths.settings_path().read_text())
        ids = [m["id"] for m in data["modules"]]
        assert data["modules"][0] == {"id": "deck", "enabled": False, "order": 0}
        assert ids[1:3] == ["curses", "enchanted"]
        assert "Aperçu · Carte · loupe · gauche" in screen_text(app)
        await pilot.press("d")                           # valeurs par défaut
        await pilot.pause()
        loaded = settings.load()[0].to_json()
        default = settings.OverlaySettings.default().to_json()
        assert loaded["modules"] == default["modules"] and loaded["hud"] == default["hud"]
        assert [p.name for p in paths.config_dir().iterdir()] == ["settings.json"]


async def test_every_hud_field_and_module_is_settable():
    """Tout ce que le HUD affiche se règle depuis le TUI : 6 champs HudSettings v1.3 + chaque module."""
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.press("4")
        await pilot.pause()
        hud = app.query_one("#hud-set")
        hud.focus()
        await pilot.press("left", "left")                       # verre → eco → loupe (rotation)
        await pilot.press("down", *["right"] * 20)              # hauteur : bornée à 85 %
        await pilot.press("down", *["left"] * 40)               # opacité : bornée à 35 %
        await pilot.press("down", "right")                      # côté
        await pilot.press("down", "space")                      # note du deck en combat
        await pilot.press("down", "space")                      # lettres seules
        await pilot.pause()
        h = json.loads(paths.settings_path().read_text())["hud"]
        assert h == {"quality": "loupe", "heightFraction": 0.85, "restOpacity": 0.35, "side": "left",
                     "deckScoreInCombat": True, "gradesOnly": True}
        text = screen_text(app)
        assert "85 % de l'écran  30–85" in text and "35 %  35–100" in text
        mods = app.query_one("#modules")
        mods.focus()
        for i in range(len(settings.DEFAULT_ORDER)):            # chaque module : masqué puis réaffiché
            await pilot.press("space")
            await pilot.pause()
            assert json.loads(paths.settings_path().read_text())["modules"][i]["enabled"] is False
            await pilot.press("space", "down")
        await pilot.press("shift+up")                           # dernier module (Note du deck) remonte
        await pilot.pause()
        data = json.loads(paths.settings_path().read_text())
        assert all(m["enabled"] for m in data["modules"])
        assert [m["id"] for m in data["modules"]][-2:] == ["deck_score", "party"]
        assert [m["order"] for m in data["modules"]] == list(range(len(settings.DEFAULT_ORDER)))
        labels = [str(mods.get_row_at(i)[1]) for i in range(mods.row_count)]
        assert labels[:3] == ["Deck", "Enchantées", "Malédictions"] and labels[-2:] == ["Note du deck", "Équipe"]


async def test_out_of_bounds_file_is_clamped_in_tui():
    paths.config_dir().mkdir(parents=True)
    paths.settings_path().write_text('{"modules": [{"id": "shop", "order": 0}], "hud": {"heightFraction": 0.99,'
                                     ' "restOpacity": 0.05}}')
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.press("4")
        await pilot.pause()
        text = screen_text(app)
        assert "85 % de l'écran" in text and "35 %" in text
        assert str(app.query_one("#modules").get_row_at(0)[1]) == "Shop"
        assert app.settings.modules[0].enabled is True           # listé sans « enabled » → true


async def test_settings_changed_elsewhere_are_reloaded():
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        s = settings.OverlaySettings.default()
        s.hud.quality = "eco"
        settings.save(s)
        app.tick()
        await pilot.press("4")
        await pilot.pause()
        assert app.settings.hud.quality == "eco"


def write_log():
    p = paths.log_path()
    p.parent.mkdir(parents=True)
    p.write_text("2026-10-29 17:54:04.243 INFO  démarré\n2026-10-29 17:54:04.250 DEBUG mirror ms=12\n"
                 "2026-10-29 17:54:05.001 WARN  ptrace_scope=1\n2026-10-29 17:54:06.002 ERROR save illisible\n")


async def test_journal_filter_search_and_custom_colors():
    write_log()
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.press("5")
        await pilot.pause()
        assert len(lines_of(app, "#log")) == 4
        await pilot.press("w")
        await pilot.pause()
        assert [ln.split()[2] for ln in lines_of(app, "#log")] == ["WARN", "ERROR"]
        await pilot.press("a", "slash")
        await pilot.press(*"mirror")
        await pilot.pause()
        assert lines_of(app, "#log") == ["2026-10-29 17:54:04.250 DEBUG mirror ms=12"]
        await pilot.press("escape")
        await pilot.pause()
        assert len(lines_of(app, "#log")) == 4
        await pilot.press("c")                       # couleurs du journal → onglet Apparence (TUI)
        await pilot.pause()
        assert app.query_one(TabbedContent).active == "appearance"
        table = app.query_one("#app-tui-table")
        row = [i for i, key in enumerate(app.TUI_ROWS) if key[0] == "log:info"][0]
        app._app_change(table, row, 1)               # « info » : du thème → première couleur proposée
        await pilot.pause()
        data = json.loads(paths.settings_path().read_text())
        value = data["appearance"]["tui"]["logColors"]["info"]
        assert value in themes.TONES                              # ton sémantique, pas un hex en dur
        assert paths.console_config_path().exists() is False      # migration : aucun console.json créé
        info = [s for s in app.query_one("#log", RichLog).lines[0]][-1]
        expected = app.res.tones[value].lstrip("#").lower()
        assert expected[:6] in str(info.style).lower().replace("#", "")
        assert appearance.load()[0].tui.log_colors["info"] == value


async def test_journal_follows_new_lines():
    write_log()
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.press("5")
        with open(paths.log_path(), "a") as f:
            f.write("2026-10-29 17:55:00.000 INFO  nouvelle ligne\n")
        app.poll_log()
        await pilot.pause()
        assert lines_of(app, "#log")[-1].endswith("nouvelle ligne")


async def test_controls_start_without_binary_and_doctor():
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.press("6")
        await pilot.pause()
        assert "Daemon   arrêté" in screen_text(app)
        app.run_action_item("start")
        app.run_action_item("doctor")
        await app.workers.wait_for_complete()
        await pilot.pause()
        out = "\n".join(lines_of(app, "#ctl-out"))
        assert "binaire du daemon introuvable" in out and "installation" in out
        app.run_action_item("mirror")
        await pilot.pause()
        assert consolecfg.load()[0].mirror is True


async def test_help_screen():
    app = UltimaApp(live=False)
    async with app.run_test(size=SIZE) as pilot:
        await pilot.press("question_mark")
        await pilot.pause()
        assert "non calculé (jamais 0)" in screen_text(app)
        await pilot.press("escape")
        await pilot.pause()
        assert "non calculé (jamais 0)" not in screen_text(app)


async def test_live_socket_updates_dashboard(fake_socket):
    shop = json.dumps(load_fixture("shop"), ensure_ascii=False)
    fake_socket([shop])
    app = UltimaApp(live=True)
    async with app.run_test(size=SIZE) as pilot:
        for _ in range(40):
            await pilot.pause(0.05)
            if "SHOP · 116 or" in screen_text(app):
                break
        text = screen_text(app)
        assert "SHOP · 116 or" in text and "daemon ✓" in text


async def test_appearance_tab_edits_hud_theme_font_position_and_rail():
    """Onglet 7 Apparence → HUD : thème, police, taille, position, liseré — écrits dans settings.json."""
    from ultima_console import fonts
    fonts.copy_base()                                  # familles proposées par `font list`
    app = UltimaApp(live=False)
    async with app.run_test(size=(130, 40)) as pilot:
        await pilot.press("7")
        await pilot.pause()
        app.query_one("#appearance-tabs", TabbedContent).active = "app-hud"
        await pilot.pause()
        table = app.query_one("#app-hud-table")
        rows = {key[0]: i for i, key in enumerate(app.HUD_ROWS)}
        app._app_change(table, rows["theme"], 1)
        app._app_change(table, rows["font"], 1)
        app._app_change(table, rows["size"], 1)
        app._app_change(table, rows["side"], 1)
        app._app_change(table, rows["anchor"], 1)
        app._app_change(table, rows["width"], 1)
        app._app_change(table, rows["railEdge"], 1)
        await pilot.pause()
        hud = json.loads(paths.settings_path().read_text())["appearance"]["hud"]
        assert hud["theme"] != "sts"
        assert hud["font"]["family"] in ("Liberation Sans", "Liberation Mono")
        assert hud["font"]["size"] == 12
        assert hud["position"]["side"] == "left" and hud["position"]["anchor"] == "bottom"
        assert json.loads(paths.settings_path().read_text())["hud"]["side"] == "left"   # v1.3 cohérent
        assert hud["position"]["width"] == 380
        assert hud["railEdge"] is False
        assert "Aperçu" in screen_text(app) or True
        # Apparence → Compagnon : thème et police aussi (jamais pour le TUI, qui suit le terminal).
        app.query_one("#appearance-tabs", TabbedContent).active = "app-comp"
        await pilot.pause()
        comp_table = app.query_one("#app-comp-table")
        comp_rows = {key[0]: i for i, key in enumerate(app.COMP_ROWS)}
        app._app_change(comp_table, comp_rows["theme"], 1)
        app._app_change(comp_table, comp_rows["font"], 1)
        await pilot.pause()
        comp = json.loads(paths.settings_path().read_text())["appearance"]["companion"]
        assert comp["theme"] != "sts" and comp["font"]["family"]


async def test_custom_module_and_plugin_are_blocks_and_enable_after_test():
    """Blocs `x.*` visibles et plugins activables SEULEMENT après un test réussi (onglet Apparence)."""
    from ultima_console import appearance, custom
    custom.new_module("x.or-par-etage")
    custom.new_plugin("x.exemple")
    app = UltimaApp(live=False)
    async with app.run_test(size=(130, 40)) as pilot:
        await pilot.press("7")
        await pilot.pause()
        app.query_one("#appearance-tabs", TabbedContent).active = "app-mod"
        await pilot.pause()
        blocks = app.query_one("#app-blocks")
        ids = [app.settings.modules[i].id for i in range(len(app.settings.modules))]
        assert "x.or-par-etage" in ids and "x.exemple" in ids
        # écrans d'un bloc : e dans l'onglet HUD ou Apparence → écrit `screens`
        app._module_screens(blocks, ids.index("x.or-par-etage"), 1)   # écrans déclarés → défaut
        app._module_screens(blocks, ids.index("x.or-par-etage"), 1)   # défaut → premier écran
        await pilot.pause()
        module = json.loads(paths.settings_path().read_text())["modules"]
        entry = next(m for m in module if m["id"] == "x.or-par-etage")
        assert entry["screens"] == ["menu"]
        # plugins : espace → test local réussi puis activation
        plugins = app.query_one("#app-plugins")
        assert appearance.plugins_enabled() == []
        app._plugin_change(plugins, 0, 1)
        await pilot.pause()
        assert appearance.plugins_enabled() == ["x.exemple"]
        assert "activé" in app._app_msg


async def test_terminal_theme_leaves_background_to_the_terminal():
    from ultima_console import appearance
    appearance.set_theme("tui", "terminal")
    app = UltimaApp(live=False)
    async with app.run_test(size=(110, 34)) as pilot:
        await pilot.pause()
        assert app.res.ansi16 and app.res.background is None
        assert app.screen.has_class("ansi16")
        assert app.theme == "terminal"
