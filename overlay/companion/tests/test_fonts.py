"""Polices (docs/44 §5) : dossiers perso→jeu→base→systeme, puis fontconfig, puis thème, puis système."""

import struct
from pathlib import Path

from conftest import COMPANION
from ultima_companion import fonts


def make_font(path: Path, family: str, name_id: int = 1) -> Path:
    """Faux fichier sfnt minimal avec une table `name` : suffisant pour la lecture de famille."""
    text = family.encode("utf-16-be")
    record = struct.pack(">HHHHHH", 3, 1, 0x409, name_id, len(text), 0)
    name_table = struct.pack(">HHH", 0, 1, 6 + 12) + record + text
    header = struct.pack(">IHHHH", 0x00010000, 1, 16, 0, 0)
    table = b"name" + struct.pack(">III", 0, 12 + 16, len(name_table))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(header + table + name_table)
    return path


def test_folders_order_is_the_documented_one():
    assert fonts.FOLDERS == ("perso", "jeu", "base", "systeme")
    assert fonts.SUFFIXES[0] == ".ttf"


def test_name_table_is_read(tmp_path):
    path = make_font(tmp_path / "perso" / "fichier-bizarre.ttf", "Fira Sans", name_id=1)
    assert "Fira Sans" in fonts._sfnt_families(path)
    # nameID 16 (typographic family) aussi
    path2 = make_font(tmp_path / "perso" / "x.ttf", "Source Code Pro", name_id=16)
    assert "Source Code Pro" in fonts._sfnt_families(path2)


def test_garbage_file_never_raises(tmp_path):
    bad = tmp_path / "perso" / "casse.ttf"
    bad.parent.mkdir(parents=True)
    bad.write_bytes(b"pas une police")
    assert fonts._sfnt_families(bad) == set()
    assert fonts.find_family("Casse", tmp_path) is not None      # repli par nom de fichier


def test_find_family_matches_internal_name_and_filename(tmp_path):
    make_font(tmp_path / "perso" / "ttf.ttf", "Kreon")
    hit = fonts.find_family("kreon", tmp_path)
    assert hit is not None and hit.folder == "perso" and hit.path.name == "ttf.ttf"
    make_font(tmp_path / "jeu" / "SourceCodePro-Regular.ttf", "Source Code Pro")
    hit = fonts.find_family("Source Code Pro", tmp_path)
    assert hit is not None and hit.folder == "jeu"
    assert fonts.find_family("Absente", tmp_path) is None
    # table `name` illisible → repli par nom de fichier
    broken = tmp_path / "base" / "MaPolice-Bold.ttf"
    broken.parent.mkdir(parents=True)
    broken.write_bytes(b"pas une police")
    assert fonts.find_family("MaPolice", tmp_path).folder == "base"


def test_search_order_perso_before_jeu_before_base(tmp_path):
    make_font(tmp_path / "systeme" / "a.ttf", "Partagee")
    make_font(tmp_path / "base" / "b.ttf", "Partagee")
    make_font(tmp_path / "jeu" / "c.ttf", "Partagee")
    assert fonts.find_family("Partagee", tmp_path).folder == "jeu"
    make_font(tmp_path / "perso" / "d.ttf", "Partagee")
    assert fonts.find_family("Partagee", tmp_path).folder == "perso"


def test_font_files_and_families_by_folder(tmp_path):
    make_font(tmp_path / "perso" / "1.ttf", "Perso Un")
    make_font(tmp_path / "jeu" / "2.otf", "Jeu Deux")
    (tmp_path / "base" / "lisez-moi.txt").parent.mkdir(parents=True)
    (tmp_path / "base" / "lisez-moi.txt").write_text("pas une police")
    files = fonts.font_files(tmp_path)
    assert [f.folder for f in files] == ["perso", "jeu"]
    families = fonts.families_by_folder(tmp_path)
    assert families["perso"] == ["Perso Un"] and families["jeu"] == ["Jeu Deux"]
    assert families["base"] == [] and families["systeme"] == []


def test_resolve_from_folders_then_fontconfig_then_theme_then_system(tmp_path):
    make_font(tmp_path / "base" / "f.ttf", "Fira Sans")
    hit = fonts.resolve_family("Fira Sans", None, tmp_path, fc_families=set())
    assert (hit.family, hit.source) == ("Fira Sans", "base") and hit.file is not None
    # fontconfig connaît la famille, aucun fichier ULTIMA
    hit = fonts.resolve_family("DejaVu Sans", None, tmp_path, fc_families={"DejaVu Sans"})
    assert (hit.family, hit.source, hit.file) == ("DejaVu Sans", "fontconfig", None)
    # introuvable → police du thème (présente dans les dossiers)
    make_font(tmp_path / "jeu" / "k.ttf", "Kreon")
    hit = fonts.resolve_family("Inexistante", "Kreon", tmp_path, fc_families=set())
    assert (hit.family, hit.source) == ("Kreon", "jeu")
    assert hit.message and "Inexistante" in hit.message and "thème" in hit.message
    # introuvable → police système
    hit = fonts.resolve_family("Inexistante", None, tmp_path, fc_families=set())
    assert (hit.family, hit.source) == (None, "système")
    assert hit.message and "système" in hit.message


def test_resolve_ignores_fontconfig_when_asked(tmp_path):
    hit = fonts.resolve_family("DejaVu Sans", None, tmp_path, use_fontconfig=False,
                               fc_families={"DejaVu Sans"})
    assert hit.source == "système"


def test_resolve_rejects_unsafe_family_and_uses_theme(tmp_path):
    make_font(tmp_path / "perso" / "ok.ttf", "Theme Sans")
    hit = fonts.resolve_family('" ; color: red', "Theme Sans", tmp_path, fc_families=set())
    assert hit.family == "Theme Sans" and hit.source == "perso"


def test_css_family_is_quoted_and_safe():
    assert fonts.css_family("Fira Sans") == '"Fira Sans", "Cantarell", "Adwaita Sans", sans-serif'
    assert fonts.css_family(None) == '"Cantarell", "Adwaita Sans", sans-serif'
    assert fonts.css_family("a{}b") == '"Cantarell", "Adwaita Sans", sans-serif'
    assert fonts.css_family('a"; b') == '"Cantarell", "Adwaita Sans", sans-serif'


def test_root_uses_xdg_data_home(isolated_xdg, monkeypatch, tmp_path):
    monkeypatch.setenv("XDG_DATA_HOME", str(tmp_path / "data"))
    assert fonts.root() == tmp_path / "data" / "ultima" / "fonts"
    assert not str(fonts.root()).startswith(str(Path.home() / ".local"))


def test_register_nothing_is_harmless():
    assert fonts.register([]) == []
    assert fonts.register(None) == []


def test_font_source_mentions_no_game_pck():
    source = (COMPANION / "ultima_companion" / "fonts.py").read_text(encoding="utf-8")
    assert "pck" not in source and "GAME_FONTS" not in source
