"""Journal du daemon : format v1.3 (Diag.cs), ancien format, filtre, couleurs personnalisables, suivi."""

import os

import pytest

from ultima_console import appearance, consolecfg, logs, paths, theme
from ultima_console.palette import Ansi

LINES = [
    "2026-10-29 17:54:04.243 INFO  ULTIMA daemon démarré\n",
    "2026-10-29 17:54:04.250 DEBUG mirror ms=12\n",
    "2026-10-29 17:54:05.001 WARN  lecture mémoire refusée : ptrace_scope=1\n",
    "2026-10-29 17:54:06.002 ERROR save illisible\n",
    "   at Ultima.Saves.Read()\n",
    "17:54:07.100 info ancien format\n",
    "Unhandled exception. System.IO.IOException: disque\n",
    "suite sans niveau\n",
]


def test_parse_levels_and_formats():
    got = logs.parse_lines(LINES)
    assert [g.level for g in got] == ["info", "debug", "warn", "error", "error", "info", "error", "error"]
    assert got[0].ts == "2026-10-29 17:54:04.243" and got[0].msg == "ULTIMA daemon démarré"
    assert got[2].msg.startswith("lecture mémoire refusée")
    assert got[5].ts == "17:54:07.100" and got[5].msg == "ancien format"
    assert got[4].ts is None


@pytest.mark.parametrize("level,needle,expected", [
    ("debug", None, 8), ("info", None, 7), ("warn", None, 5), ("error", None, 4), ("debug", "MIRROR", 1),
])
def test_filter(level, needle, expected):
    assert sum(logs.accept(ll, level, needle) for ll in logs.parse_lines(LINES)) == expected


def test_color_line_uses_theme_and_overrides():
    res = theme.resolve("sts", log_overrides={"warn": "curse", "timestamp": "#123456"})
    ll = logs.parse_line(LINES[2])
    out = logs.color_line(ll, res, Ansi(True))
    assert "\x1b[38;2;18;52;86m2026-10-29 17:54:05.001\x1b[0m" in out          # #123456
    assert "\x1b[1;38;2;238;130;238mWARN \x1b[0m" in out                       # ton curse, gras
    assert logs.color_line(ll, res, Ansi(False)) == LINES[2].rstrip("\n")


def test_terminal_theme_log_colors_are_ansi16():
    res = theme.resolve("terminal")
    assert all(v in theme.ANSI16 for v in res.log_colors.values())


def test_console_config_keeps_launch_options_and_legacy_keys():
    """v1.4 : console.json ne sert plus qu'au lancement ; theme/logColors restent LUS (migration) et GARDÉS."""
    paths.console_config_path().parent.mkdir(parents=True)
    paths.console_config_path().write_text('{"theme": "nord", "logColors": {"info": "aqua", "error": "rouge",'
                                           ' "debug": "#ABCDEF"}, "daemon": {"mirror": true}, "x": 1}')
    cfg, err = consolecfg.load()
    assert err is None and cfg.mirror is True and cfg.has_legacy_appearance()
    assert cfg.theme == "nord" and cfg.log_colors["info"] == "aqua" and cfg.log_colors["error"] == "danger"
    assert cfg.log_colors["debug"] == "#ABCDEF"                 # hex conservé pour la migration
    consolecfg.save(cfg)
    again, _ = consolecfg.load()
    assert again.to_json() == cfg.to_json() and again.to_json()["x"] == 1
    assert cfg.daemon_args() == []
    cfg.mirror = False
    assert cfg.daemon_args() == ["--no-mirror"]


def test_tail_and_follower_rotation(tmp_path):
    p = tmp_path / "d.log"
    assert logs.tail(p) == []
    p.write_text("".join(LINES[:3]))
    assert logs.tail(p, 2) == LINES[1:3]
    f = logs.Follower(p, from_end=True)
    assert f.poll() == []
    with open(p, "a") as fh:
        fh.write(LINES[3] + "partiel")
    assert f.poll() == [LINES[3]]
    with open(p, "a") as fh:
        fh.write(" fin\n")
    assert f.poll() == ["partiel fin\n"]
    os.replace(p, tmp_path / "d.log.1")          # rotation par le daemon
    p.write_text(LINES[5])
    assert f.poll() == [LINES[5]]
    p.write_text("")                              # troncature
    assert f.poll() == []
