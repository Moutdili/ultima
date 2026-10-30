"""TUI en mode headless (Textual App.run_test + pilot) : aucune fenêtre, aucun terminal réel."""

import json

import pytest
from conftest import load_fixture, publish

pytest.importorskip("textual")

from textual.widgets import DataTable, RichLog  # noqa: E402

from ultima_console import consolecfg, paths, settings  # noqa: E402
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
        assert "#efc851" in svg.lower()          # S en or (palette du jeu)
        assert "#808080" in svg.lower()          # « — » en gris


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
        for key in "123456":
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
        assert settings.load()[0].to_json() == settings.OverlaySettings.default().to_json()
        assert [p.name for p in paths.config_dir().iterdir()] == ["settings.json"]


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
        await pilot.press("c")                       # couleurs du journal
        await pilot.pause()
        await pilot.press("down", "down", "down", "right")   # ligne « info » : cream → lightGray
        await pilot.pause()
        cfg = json.loads(paths.console_config_path().read_text())
        assert cfg["logColors"]["info"] == "lightGray"
        await pilot.press("escape")
        await pilot.pause()
        info = [s for s in app.query_one("#log", RichLog).lines[0]][-1]
        assert "bfbfbf" in str(info.style).lower()   # nouvelle couleur appliquée aussitôt
        assert consolecfg.load()[0].color("info") == "#BFBFBF"


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
