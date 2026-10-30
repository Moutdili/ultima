"""Thèmes v1.4 : schéma, héritage, tons, contraste mesuré, `auto-perso`, `terminal` 16 couleurs.

Preuve demandée par T2 : chaque thème livré valide `schemas/theme.schema.json`, les neutres dépassent
7:1, `auto-perso` suit `run.character`, `terminal` n'utilise que des noms ANSI (aucune couleur 24 bits),
et aucun hexadécimal ne subsiste hors de `themes/`.
"""

from __future__ import annotations

import io
import json
import re

import pytest
from conftest import CONSOLE, REPO

from ultima_console import paths, theme

BUILTIN = ("sts", "ironclad", "silent", "defect", "regent", "necrobinder", "nuit", "ardoise", "contraste",
           "auto-perso")
NEUTRAL = ("nuit", "ardoise", "contraste")


def test_every_builtin_theme_is_a_file_and_validates():
    themes, problems = theme.diagnose()
    assert problems == []
    on_disk = sorted(p.stem for p in (REPO / "themes").glob("*.json"))
    assert set(on_disk) == set(BUILTIN) and "sts" in on_disk
    for tid in BUILTIN:
        t = themes[tid]
        assert t.source == "builtin" and t.path is not None
        assert json.loads(t.path.read_text(encoding="utf-8"))["id"] == tid
        assert theme.schema.validate(t.raw, theme.schema.load("theme")) == []


def test_character_and_neutral_theme_tone_coverage():
    for tid in BUILTIN:
        tones, problems, _ = theme._merge_tones(tid, theme.load_all())
        assert problems == [], tid
        assert set(theme.TONES) <= set(tones), tid


def test_neutral_themes_have_seven_to_one_contrast():
    """Contraste MESURÉ (WCAG 2.1) du texte sur le fond, pour les thèmes neutres sobres."""
    for tid in NEUTRAL:
        result = theme.check(tid)
        assert result["ok"] and result["problems"] == [], (tid, result["problems"])
        assert result["contrast"] >= 7.0, (tid, result["contrast"])


def test_contrast_formula_matches_known_values():
    assert theme.contrast("#FFFFFF", "#000000") == 21.0
    assert theme.contrast("#000000", "#FFFFFF") == 21.0
    assert theme.contrast("#777777", "#777777") == 1.0
    assert 4.4 < theme.contrast("#FFFFFF", "#767676") < 4.6


def test_character_themes_use_name_colors():
    expected = {"ironclad": "#FF5555", "silent": "#7FFF00", "defect": "#87CEEB", "regent": "#FFA518",
                "necrobinder": "#EE82EE"}
    for tid, accent in expected.items():
        res = theme.resolve(tid)
        assert res.tones["accent"] == accent
        assert res.raw_tones["accent"] == accent


def test_auto_perso_resolves_from_run_character_with_sts_fallback():
    for character, tid in (("CHARACTER.IRONCLAD", "ironclad"), ("CHARACTER.SILENT", "silent"),
                           ("CHARACTER.DEFECT", "defect"), ("CHARACTER.REGENT", "regent"),
                           ("CHARACTER.NECROBINDER", "necrobinder"), ("Ironclad", "ironclad")):
        res = theme.resolve("auto-perso", character=character)
        assert (res.requested, res.id) == ("auto-perso", tid)
        assert res.tones["accent"] == theme.resolve(tid).tones["accent"]
    assert theme.resolve("auto-perso").id == "sts"
    assert theme.resolve("auto-perso", character="CHARACTER.INCONNU").id == "sts"
    assert theme.resolve("auto-perso", character=None).id == "sts"


def test_extends_inherits_then_overrides():
    paths.user_themes_dir().mkdir(parents=True)
    (paths.user_themes_dir() / "brume.json").write_text(json.dumps({
        "id": "brume", "label": "Brume", "api": 1, "extends": "sts",
        "tones": {"accent": "#8FB3D9", "background": "#1C2230"},
    }))
    res = theme.resolve("brume")
    assert res.tones["accent"] == "#8FB3D9"
    assert res.tones["text"] == theme.resolve("sts").tones["text"]      # hérité
    assert theme.load_all()["brume"].source == "user"                   # thème utilisateur


def test_extends_cycle_is_refused_without_hanging():
    paths.user_themes_dir().mkdir(parents=True)
    for a, b in (("aaa", "bbb"), ("bbb", "aaa")):
        (paths.user_themes_dir() / f"{a}.json").write_text(json.dumps({
            "id": a, "label": a, "api": 1, "extends": b, "tones": {"accent": "#101010"},
        }))
    res = theme.resolve("aaa")
    assert res.id == "aaa" and res.tones["text"]                # pas de récursion infinie
    assert isinstance(theme.check("aaa")["problems"], list)


def test_invalid_theme_file_is_ignored_whole():
    paths.user_themes_dir().mkdir(parents=True)
    (paths.user_themes_dir() / "casse.json").write_text('{"id": "casse", "label": "Cassé", "api": 1,'
                                                        ' "tones": {"accent": "rouge"}}')
    themes, problems = theme.diagnose()
    assert "casse" not in themes
    assert any("casse.json" in p for p in problems)
    res = theme.resolve("casse")                                # repli documenté
    assert res.id == "sts" and any("inconnu" in p for p in res.problems)


def test_terminal_theme_uses_only_the_16_ansi_names():
    res = theme.resolve("terminal")
    assert res.ansi16 and res.background is None and res.tones["background"] == ""
    assert all(v in theme.ANSI16 for v in res.tones.values() if v)
    assert all(v in theme.ANSI16 for v in res.log_colors.values())
    assert not any("#" in v for v in res.tones.values())
    # Une surcharge hex est IGNORÉE pour cette surface (pas de 24 bits mélangé).
    assert theme.resolve("terminal", overrides={"accent": "#123456"}).tones["accent"] in theme.ANSI16


def test_terminal_rendering_emits_no_24_bit_escape():
    """Le rendu Rich d'un ton terminal sort en 4 bits (30-37/90-97), jamais en 38;2;r;g;b."""
    from rich.console import Console
    from rich.text import Text
    res = theme.resolve("terminal")
    console = Console(file=io.StringIO(), force_terminal=True, color_system="truecolor")
    for tone in theme.TONES:
        style = res.style(tone)
        if style and "dim" not in style:
            console.print(Text("x", style=style))
    out = console.file.getvalue()
    assert "38;2;" not in out and "48;2;" not in out
    assert re.search(r"\x1b\[(?:1;)?(?:3[0-7]|9[0-7])m", out)


def test_terminal_textual_theme_uses_ansi_colors_only():
    from ultima_console.tui import textual_theme
    t = textual_theme("terminal", theme.resolve("terminal"))
    for value in (t.primary, t.secondary, t.accent, t.warning, t.error, t.success, t.foreground, t.background):
        assert str(value).startswith("ansi_"), value


def test_contrast_warning_below_4_5_is_reported_but_accepted():
    paths.user_themes_dir().mkdir(parents=True)
    (paths.user_themes_dir() / "fade.json").write_text(json.dumps({
        "id": "fade", "label": "Fade", "api": 1, "extends": "sts",
        "tones": {"text": "#888888", "background": "#808080"},
    }))
    result = theme.check("fade")
    assert result["ok"] and result["contrast"] < 4.5
    assert any("contraste" in p for p in result["problems"])


def test_no_hexadecimal_outside_themes():
    """Plus aucune couleur codée en dur : les hex vivent dans themes/ (et les exemples du README)."""
    files = sorted((CONSOLE / "ultima_console").rglob("*.py")) + [REPO / "tools" / "install.sh",
                                                                 REPO / "tools" / "uninstall.sh"]
    offenders = []
    for p in files:
        for m in re.finditer(r"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?", p.read_text(encoding="utf-8")):
            line = p.read_text(encoding="utf-8")[: m.start()].count("\n") + 1
            offenders.append(f"{p.relative_to(REPO)}:{line} {m.group(0)}")
    assert offenders == []


def test_theme_check_and_list_are_machine_readable():
    result = theme.check("sts")
    assert result["ok"] and result["contrast"] >= 4.5 and result["path"].endswith("sts.json")
    rows = {r["id"]: r for r in theme.list_ids()}
    assert "terminal" in rows and rows["terminal"]["source"] == "virtual"
    assert rows["ironclad"]["extends"] == "sts"


def test_style_and_log_style_composition():
    res = theme.resolve("sts", log_overrides={"warn": "curse", "error": "#123456"})
    assert res.style(None) == res.tones["text"] and res.style(None, True) == f"bold {res.tones['text']}"
    assert res.style("dim") == "dim" and res.style("dim", True) == "bold dim"
    assert res.style("danger", True) == f"bold {res.tones['danger']}"
    assert res.log_style("warn", True) == f"bold {theme.resolve('sts').tones['curse']}"
    assert res.log_style("error") == "#123456"
    assert res.style("perso:CHARACTER.IRONCLAD") == theme.resolve("ironclad").tones["accent"]
