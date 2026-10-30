"""Identité visuelle : aucune couleur hors palette (charte docs/17 §7.3), polices du jeu jamais copiées."""

import re
from pathlib import Path

from conftest import COMPANION
from ultima_companion import fonts, palette, theme

HEX = re.compile(r"#[0-9a-fA-F]{3,8}\b")
RGBA = re.compile(r"rgba\((\d+), (\d+), (\d+), ([\d.]+)\)")
COLOR_FUNCS = re.compile(r"\b(rgb|hsl|hsla|shade|mix|lighter|darker)\(")
NAMED = re.compile(r":\s*(red|green|blue|white|black|gray|grey|yellow|orange|purple|pink)\b")

SOURCES = [p for p in COMPANION.rglob("*")
           if p.is_file() and p.suffix in {".py", ".css", ".desktop", ".ui", ".json"}
           and "__pycache__" not in p.parts and ".pytest_cache" not in p.parts
           and "fixtures" not in p.parts]


def test_palette_is_the_game_palette():
    # StsColors.cs (docs/17 §1.1) + teinte du verre + noir/blanc autorisés : rien d'autre.
    assert palette.PALETTE == {
        "cream": "#FFF6E2", "gold": "#EFC851", "red": "#FF5555", "green": "#7FFF00",
        "blue": "#87CEEB", "orange": "#FFA518", "purple": "#EE82EE", "aqua": "#2AEBBE",
        "gray": "#808080", "lightGray": "#BFBFBF", "glassTint": "#192930",
        "black": "#000000", "white": "#FFFFFF",
    }
    # §9 : S gold, A green, B aqua, C blue, D orange, F red, — gray
    assert palette.GRADE_TONES == {"S": "gold", "A": "green", "B": "aqua", "C": "blue", "D": "orange",
                                   "F": "red", "—": "gray"}
    assert palette.character_tone("CHARACTER.IRONCLAD") == "red"
    assert palette.character_tone("CHARACTER.NOBODY") is None


def test_no_hex_outside_palette_file():
    offenders = []
    for p in SOURCES:
        if p.name == "palette.py" or p.name.startswith("test_"):
            continue
        for m in HEX.finditer(p.read_text(encoding="utf-8")):
            offenders.append(f"{p.relative_to(COMPANION)}: {m.group()}")
    assert not offenders, offenders


def test_generated_css_uses_only_palette_colors():
    allowed = {palette.rgb(t) for t in palette.PALETTE}
    for translucent in (False, True):
        css = theme.css(translucent)
        assert not HEX.search(css)
        assert not COLOR_FUNCS.search(css), COLOR_FUNCS.search(css)
        assert not NAMED.search(css), NAMED.search(css)
        found = RGBA.findall(css)
        assert found
        for r, g, b, a in found:
            assert (int(r), int(g), int(b)) in allowed, (r, g, b)
            assert 0.0 <= float(a) <= 1.0


def test_css_has_every_grade_and_no_loop_animation():
    css = theme.css()
    for grade in ("S", "A", "B", "C", "D", "F", "—"):
        assert f".grade.{theme.grade_class(grade)} " in css
    assert "@keyframes" not in css and "animation" not in css   # §6 : aucune animation en boucle
    assert "Kreon" in css and "Source Code Pro" in css


def test_css_parses_in_gtk():
    import gi
    gi.require_version("Gtk", "4.0")
    from gi.repository import Gtk
    errors = []
    provider = Gtk.CssProvider()
    provider.connect("parsing-error", lambda _p, sec, err: errors.append((sec.to_string(), err.message)))
    provider.load_from_string(theme.css(True))
    assert not errors, errors


# ---- polices : chargées depuis le pck extrait, jamais copiées -----------------------------------------

def test_no_font_file_in_repo():
    assert not [p for p in COMPANION.rglob("*") if p.suffix.lower() in {".ttf", ".otf", ".woff", ".woff2"}]


def test_find_fonts_env_and_worktree(tmp_path):
    main = tmp_path / "repo"
    fdir = main / "game" / "pck" / "fonts"
    fdir.mkdir(parents=True)
    (fdir / "kreon_regular.ttf").write_bytes(b"x")
    (fdir / "source_code_pro_semibold.ttf").write_bytes(b"x")
    (main / ".git" / "worktrees" / "wt").mkdir(parents=True)
    (main / ".git" / "worktrees" / "wt" / "commondir").write_text("../..\n")
    wt = tmp_path / "wt"
    (wt / "overlay" / "companion" / "ultima_companion").mkdir(parents=True)
    (wt / ".git").write_text(f"gitdir: {main / '.git' / 'worktrees' / 'wt'}\n")
    start = wt / "overlay" / "companion" / "ultima_companion" / "fonts.py"
    found = fonts.find_fonts(start, env={})
    assert [f.name for f in found] == ["kreon_regular.ttf", "source_code_pro_semibold.ttf"]
    assert main / "game" / "pck" / "fonts" in fonts.candidate_dirs(start, env={})
    # variable d'environnement prioritaire ; dossier vide → rien, sans erreur
    other = tmp_path / "other"
    other.mkdir()
    (other / "kreon_bold.ttf").write_bytes(b"x")
    assert [f.name for f in fonts.find_fonts(start, env={"ULTIMA_GAME_FONTS": str(other)})] == ["kreon_bold.ttf"]
    assert fonts.find_fonts(tmp_path / "nowhere" / "x.py", env={}) == []


def test_register_nothing_is_harmless():
    assert fonts.register([]) == []


def test_gitignore_keeps_game_dir_out():
    # Le pck extrait (polices, textes du jeu) reste hors du dépôt.
    root_ignore = COMPANION.parent.parent / ".gitignore"
    if root_ignore.exists():
        assert re.search(r"^/?game/?$", root_ignore.read_text(), re.M)
    assert Path(fonts.__file__).parent.name == "ultima_companion"
