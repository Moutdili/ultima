"""Bandeau ULTIMA : dessin exact (48 × 8), couleurs du THÈME, repli une ligne (taille, locale), NO_COLOR."""

from ultima_console import CONTRACT_MAJOR, CONTRACT_MINOR
import os
import subprocess
import sys

import pytest
from conftest import CONSOLE

from ultima_console import banner, cli, theme
from ultima_console.palette import Ansi

RES = theme.resolve("sts")

ESC = "\x1b["


def rgb(token):
    h = RES.tones[token]
    return f"38;2;{int(h[1:3], 16)};{int(h[3:5], 16)};{int(h[5:7], 16)}m"


def test_exact_drawing():
    assert banner.WIDTH == 48 and banner.HEIGHT == 8 and len(banner.ART) == 6
    assert max(len(line) for line in banner.ART) == 48 and all(len(line) <= 48 for line in banner.LINES)
    assert banner.ART[0] == "██╗   ██╗██╗     ████████╗██╗███╗   ███╗ █████╗"
    assert banner.ART[5] == " ╚═════╝ ╚══════╝   ╚═╝   ╚═╝╚═╝     ╚═╝╚═╝  ╚═╝"
    assert banner.LINES[6] == "" and banner.SUBTITLE == "           a simple tracker for sts2"
    assert banner.ONE_LINE == "ULTIMA · a simple tracker for sts2"
    assert banner.render(Ansi(False), 80, 40, True) == "\n".join(banner.LINES)


@pytest.mark.parametrize("cols,rows,utf8,is_full", [
    (52, 30, True, True), (200, 60, True, True), (51, 30, True, False), (52, 29, True, False),
    (48, 100, True, False), (200, 60, False, False),
])
def test_fallback_by_size_and_locale(cols, rows, utf8, is_full):
    assert banner.full(cols, rows, utf8) is is_full
    out = banner.render(Ansi(False), cols, rows, utf8)
    assert out == ("\n".join(banner.LINES) if is_full else banner.ONE_LINE)


@pytest.mark.parametrize("env,ok", [
    ({"LANG": "fr_FR.UTF-8"}, True), ({"LANG": "en_US.utf8"}, True), ({"LANG": "C"}, False), ({}, False),
    ({"LANG": "fr_FR.UTF-8", "LC_ALL": "C"}, False), ({"LANG": "C", "LC_CTYPE": "fr_FR.UTF-8"}, True),
    ({"LANG": "fr_FR.ISO-8859-1"}, False), ({"ULTIMA_LOCALE": "C", "LC_CTYPE": "C.UTF-8"}, False),
])
def test_utf8_locale(env, ok):
    assert banner.utf8_locale(env) is ok


def test_colors_are_theme_tones():
    out = banner.render(Ansi(True), 80, 40, True).split("\n")
    for line, tone in zip(out, banner.ROW_TONES):
        assert line.startswith(f"{ESC}1;{rgb(tone)}")
    assert banner.ROW_TONES == ("text", "accent", "accent", "accent", "warn", "warn")
    assert out[7].startswith(f"{ESC}2m")                          # sous-titre atténué (ton « dim »)
    one = banner.render(Ansi(True), 40, 40, True)
    assert one.startswith(f"{ESC}1;{rgb('accent')}ULTIMA")


def test_version_banner_and_no_color(capsys, monkeypatch):
    monkeypatch.setenv("COLUMNS", "80")
    monkeypatch.setenv("LINES", "40")
    assert cli.main(["version", "--color", "always"]) == 0
    out = capsys.readouterr().out
    assert f"{rgb('accent')}{banner.ART[1]}" in out and f"contrat v{CONTRACT_MAJOR}.{CONTRACT_MINOR}" in out
    monkeypatch.setenv("NO_COLOR", "1")
    monkeypatch.setenv("FORCE_COLOR", "1")
    cli.main(["version"])
    out = capsys.readouterr().out
    assert ESC not in out and out.startswith("\n".join(banner.LINES))
    monkeypatch.setenv("COLUMNS", "51")
    cli.main(["banner"])
    assert capsys.readouterr().out == banner.ONE_LINE + "\n"
    monkeypatch.setenv("COLUMNS", "80")
    cli.main(["version", "--json"])
    assert "█" not in capsys.readouterr().out                        # --json : jamais de bandeau


@pytest.mark.parametrize("locale_env,is_full", [({"LANG": "fr_FR.UTF-8"}, True), ({"LANG": "C"}, False),
                                                ({"LC_ALL": "POSIX", "LANG": "fr_FR.UTF-8"}, False)])
def test_launcher_passes_original_locale(locale_env, is_full):
    e = {k: v for k, v in os.environ.items() if not k.startswith(("LC_", "LANG", "ULTIMA_LOCALE"))}
    e.update(locale_env, COLUMNS="80", LINES="40", ULTIMA_PYTHON=sys.executable)
    r = subprocess.run([str(CONSOLE / "ultima"), "banner"], capture_output=True, env=e)
    first = r.stdout.decode("utf-8", "replace").splitlines()[0]
    assert r.returncode == 0 and (first == banner.ART[0]) is is_full


@pytest.mark.parametrize("size,is_full", [((110, 34), True), ((110, 29), False), ((51, 40), False)])
async def test_tui_dashboard_banner(size, is_full):
    pytest.importorskip("textual")
    from ultima_console.tui import UltimaApp, screen_text
    app = UltimaApp(live=False)
    async with app.run_test(size=size) as pilot:
        await pilot.pause()
        text = screen_text(app)
        assert (banner.ART[0] in text) is is_full
        assert ("a simple tracker for sts2" in text)
        if not is_full:
            assert "ULTIMA · a simple tracker for sts2" in text
        assert "Aucun état publié." in text                          # le tableau de bord reste visible
