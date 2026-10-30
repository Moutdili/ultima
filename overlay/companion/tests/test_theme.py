"""Identité visuelle v1.4 : aucun hexadécimal hors des fichiers de thème, tout le CSS passe par les tons."""

import json
import re
from pathlib import Path

from conftest import COMPANION, REPO
from ultima_companion import appearance, theme, themes, tones

HEX = re.compile(r"#[0-9a-fA-F]{3,8}\b")
RGBA = re.compile(r"rgba\((\d+), (\d+), (\d+), ([\d.]+)\)")
COLOR_FUNCS = re.compile(r"\b(rgb|hsl|hsla|shade|mix|lighter|darker)\(")
NAMED = re.compile(r":\s*(red|green|blue|white|black|gray|grey|yellow|orange|purple|pink)\b")

SOURCES = [p for p in COMPANION.rglob("*")
           if p.is_file() and p.suffix in {".py", ".css", ".desktop", ".ui"}
           and "__pycache__" not in p.parts and ".pytest_cache" not in p.parts
           and "tests" not in p.relative_to(COMPANION).parts]


def _sts() -> themes.Theme:
    return themes.load_catalog([REPO / "themes"]).themes["sts"]


def _resolved(**overrides):
    app = appearance.CompanionAppearance.from_json(overrides or None)
    return appearance.resolve(app, dirs=[REPO / "themes"], use_fontconfig=False)


# ---- tons (vocabulaire du contrat) -----------------------------------------------------------------

def test_tone_vocabulary_is_the_contract():
    assert tones.SEMANTIC == ("text", "muted", "accent", "danger", "ok", "warn", "curse", "enchant", "info")
    assert tones.GRADES == ("S", "A", "B", "C", "D", "F")
    assert tones.GRADE_TONES == ("grade.S", "grade.A", "grade.B", "grade.C", "grade.D", "grade.F")
    assert tones.SURFACES == ("background", "border")
    assert len(tones.ALL) == 17
    assert tones.COLOR_RE.match("#FFF6E2") and tones.COLOR_RE.match("#FFF6E299")
    assert not tones.COLOR_RE.match("FFF6E2") and not tones.COLOR_RE.match("#FFF")


def test_grade_and_character_context_helpers():
    assert tones.grade_tone("S") == "grade.S" and tones.grade_tone("—") == "muted"
    assert tones.grade_tone(None) == "muted" and tones.grade_tone("Z") == "muted"
    assert tones.grade_class("S") == "grade-s" and tones.grade_class("—") == "grade-none"
    assert tones.tone_class("grade.S") == "tone-grade-s" and tones.tone_class("nope") is None
    assert tones.character_tone("CHARACTER.IRONCLAD") == "danger"
    assert tones.character_tone("CHARACTER.SILENT") == "ok"
    assert tones.character_tone("CHARACTER.NOBODY") is None
    assert tones.character_tone(None) is None


def test_parse_color_and_rgba():
    assert tones.parse_color("#FFF6E2") == (255, 246, 226, 1.0)
    r, g, b, a = tones.parse_color("#FFF6E299")
    assert (r, g, b) == (255, 246, 226) and abs(a - 0.6) < 0.01
    assert tones.parse_color("red") is None and tones.parse_color(None) is None
    # l'alpha du fichier de thème est MULTIPLIÉ par celui demandé
    assert tones.rgba("#FFF6E299", 1.0) == "rgba(255, 246, 226, 0.6)"
    assert tones.rgba("#FFF6E2", 0.5) == "rgba(255, 246, 226, 0.5)"
    assert tones.rgba("#FFF6E299", 0.5) == "rgba(255, 246, 226, 0.3)"
    assert tones.rgba("pas une couleur", 1.0) is None


# ---- aucun hex hors des fichiers de thème -----------------------------------------------------------

def test_no_hex_in_companion_sources():
    offenders = []
    for p in SOURCES:
        for m in HEX.finditer(p.read_text(encoding="utf-8")):
            offenders.append(f"{p.relative_to(COMPANION)}: {m.group()}")
    assert not offenders, offenders


def test_theme_files_are_the_only_color_source():
    files = sorted((REPO / "themes").glob("*.json"))
    assert files, "les thèmes intégrés doivent être dans themes/"
    for path in files:
        data = json.loads(path.read_text(encoding="utf-8"))
        if data.get("id") == "auto-perso":
            # Sélecteur (docs/44 §3) : résolu à l'affichage vers le thème du personnage, aucune couleur propre.
            assert "tones" not in data and data.get("extends") == "sts", path
            continue
        assert isinstance(data.get("tones"), dict), path


def test_palette_module_is_only_a_colourless_compatibility_shim():
    """`palette.py` ne sert qu'au test croisé du HUD : aucune couleur, aucun usage dans le compagnon."""
    path = COMPANION / "ultima_companion" / "palette.py"
    assert path.exists()
    assert not HEX.search(path.read_text(encoding="utf-8"))
    for source in (COMPANION / "ultima_companion").glob("*.py"):
        if source.name == "palette.py":
            continue
        assert "palette" not in source.read_text(encoding="utf-8"), source.name


# ---- CSS généré depuis le thème ---------------------------------------------------------------------

def test_css_uses_only_theme_colors_and_no_hex():
    resolved = _resolved()
    allowed = {tones.parse_color(v)[:3] for v in resolved.tones.values() if tones.parse_color(v)}
    for translucent in (False, True):
        css = theme.css(resolved, translucent)
        assert not HEX.search(css), HEX.search(css)
        assert not COLOR_FUNCS.search(css), COLOR_FUNCS.search(css)
        assert not NAMED.search(css), NAMED.search(css)
        found = RGBA.findall(css)
        assert found
        for r, g, b, a in found:
            assert (int(r), int(g), int(b)) in allowed, (r, g, b)
            assert 0.0 <= float(a) <= 1.0


def test_css_has_every_grade_tone_and_interaction():
    resolved = _resolved()
    css = theme.css(resolved)
    for grade in ("S", "A", "B", "C", "D", "F", "—"):
        assert f".grade.{theme.grade_class(grade)} " in css
    for tone in tones.ALL:
        assert f".{theme.tone_class(tone)} " in css
    assert "@keyframes" not in css and "animation" not in css
    assert "transition" not in css
    assert resolved.font_css in css


def test_css_follows_the_resolved_font_and_size():
    resolved = appearance.resolve(appearance.CompanionAppearance.from_json(
        {"theme": "sts", "font": {"family": "Fira Sans", "size": 18}}),
        dirs=[REPO / "themes"], fc_families={"Fira Sans"})
    css = theme.css(resolved)
    assert resolved.font_family == "Fira Sans"
    assert '"Fira Sans"' in css
    assert "font-size: 18px" in css


def test_css_of_a_tone_override_changes_the_color():
    base = theme.css(_resolved())
    over = theme.css(_resolved(theme="sts", tones={"accent": "#123456"}))
    assert "rgba(18, 52, 86, 1)" in over
    assert over != base
    assert "rgba(239, 200, 81, 1)" in base       # accent du thème sts


def test_css_parses_in_gtk():
    import gi
    gi.require_version("Gtk", "4.0")
    from gi.repository import Gtk
    resolved = _resolved()
    errors = []
    provider = Gtk.CssProvider()
    provider.connect("parsing-error", lambda _p, sec, err: errors.append((sec.to_string(), err.message)))
    provider.load_from_string(theme.css(resolved, True))
    assert not errors, errors


def test_neutral_css_when_no_theme_at_all(tmp_path):
    """Aucun thème utilisable : le CSS reste valide (pas de couleur inventée), message à l'écran."""
    resolved = appearance.resolve(appearance.CompanionAppearance(), dirs=[tmp_path],
                                  use_fontconfig=False)
    assert resolved.tones == {} and resolved.messages
    css = theme.css(resolved)
    assert not HEX.search(css) and "rgba" not in css
    assert "font-family" in css


def test_sts_theme_has_the_game_palette():
    sts = _sts()
    assert sts.tones["text"] == "#FFF6E2" and sts.tones["accent"] == "#EFC851"
    assert sts.tones["danger"] == "#FF5555" and sts.tones["ok"] == "#7FFF00"
    assert sts.tones["warn"] == "#FFA518" and sts.tones["curse"] == "#EE82EE"
    assert sts.tones["enchant"] == "#2AEBBE" and sts.tones["info"] == "#87CEEB"
    assert sts.tones["background"] == "#192930" and sts.tones["border"].startswith("#EFC851")
    assert sts.glass_tint == "#192930" and sts.glass_opacity == 0.55
    assert sts.api == 1 and sts.label


def test_no_font_file_in_repo():
    assert not [p for p in COMPANION.rglob("*")
                if p.suffix.lower() in {".ttf", ".otf", ".woff", ".woff2"}]


def test_no_game_pck_read_anymore():
    """La lecture de `game/pck/fonts` de la v1.3 est supprimée (docs/44 §5)."""
    source = (COMPANION / "ultima_companion" / "fonts.py").read_text(encoding="utf-8")
    assert "game/pck" not in source and "pck/fonts" not in source
    assert Path(appearance.__file__).parent.name == "ultima_companion"
