"""Polices v1.4 : `fonts/base` libre et vérifié, liens système, extraction locale, fontconfig, manifeste.

Critère T2 : install → uninstall dans un HOME temporaire rend un arbre IDENTIQUE (polices et fontconfig
compris) ; aucune police du jeu ni non libre n'est versionnée.
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess

import pytest
from conftest import CONSOLE, REPO

from ultima_console import fonts, installer, paths


def snapshot(root):
    out = {}
    for dirpath, dirs, files in os.walk(root):
        for name in dirs + files:
            p = os.path.join(dirpath, name)
            rel = os.path.relpath(p, root)
            st = os.lstat(p)
            if os.path.islink(p):
                out[rel] = ("l", os.readlink(p))
            elif os.path.isdir(p):
                out[rel] = ("d", oct(st.st_mode & 0o777))
            else:
                out[rel] = ("f", oct(st.st_mode & 0o777), hashlib.sha256(open(p, "rb").read()).hexdigest())
    return out


def test_shipped_fonts_are_free_small_and_checksummed():
    base = REPO / "fonts" / "base"
    assert base.is_dir()
    assert fonts.verify_base(base) == []
    files = sorted(p for p in base.iterdir() if p.suffix.lower() in fonts.FONT_SUFFIXES)
    assert files, "au moins une police livrée"
    assert sum(p.stat().st_size for p in files) <= fonts.MAX_BASE_BYTES
    manifest = fonts.base_manifest(base)
    assert set(manifest) == {p.name for p in files}
    for p in files:
        assert fonts.sha256(p) == manifest[p.name]
    licence = [n for n in fonts.LICENSE_NAMES if (base / n).is_file()]
    assert licence, "licence à côté des polices"
    text = (base / licence[0]).read_text(encoding="utf-8", errors="replace")
    assert "SIL OPEN FONT LICENSE" in text or "Apache License" in text


def test_verify_base_detects_a_corrupted_font(tmp_path):
    base = tmp_path / "base"
    shutil.copytree(REPO / "fonts" / "base", base)
    victim = next(p for p in base.iterdir() if p.suffix.lower() in fonts.FONT_SUFFIXES)
    victim.write_bytes(victim.read_bytes() + b"\0")
    problems = fonts.verify_base(base)
    assert any("sha256" in p for p in problems)


def test_refresh_creates_links_never_copies():
    info = fonts.refresh()
    systeme = paths.fonts_dir() / "systeme"
    links = list(systeme.iterdir())
    assert len([x for x in links if x.is_symlink()]) == 2          # faux fc-list du conftest
    for link in links:
        if link.is_symlink():
            assert not link.is_symlink() or link.resolve().is_file()
    index = json.loads((systeme / "index.json").read_text())
    assert len(index["fonts"]) == 2 and {f["family"] for f in index["fonts"]} == {"Liberation Sans",
                                                                                 "Liberation Mono"}
    assert info["warnings"] == []


def test_fontconfig_file_lists_the_four_folders():
    path = fonts.write_fontconfig()
    text = path.read_text(encoding="utf-8")
    assert path == paths.fontconfig_path() and path.is_file()
    for name in fonts.FOLDERS:
        assert str(paths.fonts_dir() / name) in text
    assert "DOCTYPE fontconfig" in text


def test_list_fonts_reports_each_folder():
    fonts.refresh()
    fonts.copy_base()
    listing = fonts.list_fonts()
    assert set(listing) == set(fonts.FOLDERS)
    assert {e["family"] for e in listing["base"]} == {"Liberation Sans", "Liberation Mono"}
    assert listing["perso"] == [] and listing["jeu"] == []
    assert {e["family"] for e in listing["systeme"]} == {"Liberation Sans", "Liberation Mono"}


def test_game_fonts_are_extracted_locally_and_never_committed(tmp_path):
    game = tmp_path / "game-fonts"
    game.mkdir()
    (game / "Kreon.ttf").write_bytes(b"\x00\x01\x00\x00fake")
    copied = fonts.extract_game_fonts(game)
    assert [p.name for p in copied] == ["Kreon.ttf"]
    assert (paths.fonts_dir() / "jeu" / "Kreon.ttf").is_file()
    assert not (REPO / "fonts" / "jeu").exists()                  # rien dans le dépôt
    ignore = (REPO / "fonts" / ".gitignore").read_text()
    assert "jeu/" in ignore and "systeme/" in ignore


def test_fonts_are_never_committed_by_git():
    """Le dépôt ne suit QUE fonts/base (libre) : ni jeu/ ni systeme/."""
    try:
        r = subprocess.run(["git", "-C", str(REPO), "ls-files", "fonts"], capture_output=True, text=True)
    except OSError:
        pytest.skip("git indisponible")
    if r.returncode != 0:
        pytest.skip("dépôt git indisponible")
    tracked = [line for line in r.stdout.splitlines() if line]
    if not tracked:
        pytest.skip("fichiers pas encore suivis par git (test lancé avant le commit)")
    assert all(line.startswith("fonts/base/") or line == "fonts/.gitignore" for line in tracked)
    assert not any(line.lower().endswith((".ttf", ".otf")) and "/base/" not in line for line in tracked)


@pytest.fixture
def fake_repo(tmp_path):
    """Dépôt minimal, avec les polices livrées et les schémas/thèmes (comme une vraie installation)."""
    repo = tmp_path / "repo"
    c = repo / "tools" / "console"
    c.mkdir(parents=True)
    shutil.copy2(CONSOLE / "ultima", c / "ultima")
    shutil.copy2(CONSOLE / "requirements.txt", c / "requirements.txt")
    shutil.copytree(CONSOLE / "ultima_console", c / "ultima_console", ignore=shutil.ignore_patterns("__pycache__"))
    (repo / "data" / "meta").mkdir(parents=True)
    (repo / "data" / "meta" / "x.json").write_text("{}")
    shutil.copytree(REPO / "schemas", repo / "schemas")
    shutil.copytree(REPO / "themes", repo / "themes")
    shutil.copytree(REPO / "fonts", repo / "fonts")
    stage = tmp_path / "stage"
    for name, exe in (("daemon", "ultima-daemon"), ("mirror", "ultima-mirror")):
        (stage / name).mkdir(parents=True)
        (stage / name / exe).write_text("#!/bin/sh\necho fake\n")
        (stage / name / exe).chmod(0o755)
    return repo, stage


def test_install_uninstall_fonts_identity(fake_repo):
    repo, stage = fake_repo
    before = snapshot(paths.home())
    installer.install(repo, stage, venv=False, extension=False, log=lambda m: None)
    dirs = {n: paths.fonts_dir() / n for n in fonts.FOLDERS}
    assert all(d.is_dir() for d in dirs.values())
    assert dirs["perso"].is_dir() and not list(dirs["perso"].iterdir())
    assert (dirs["base"] / "SHA256SUMS").is_file() and (dirs["base"] / "LICENSE").is_file()
    assert (dirs["systeme"] / "index.json").is_file()
    assert paths.fontconfig_path().is_file()
    assert fonts.verify_installed() == []
    assert (paths.ultima_home() / "themes" / "sts.json").is_file()
    assert (paths.ultima_home() / "schemas" / "theme.schema.json").is_file()
    installer.uninstall(log=lambda m: None)
    assert snapshot(paths.home()) == before
    assert not paths.fonts_dir().exists() and not paths.fontconfig_path().exists()
