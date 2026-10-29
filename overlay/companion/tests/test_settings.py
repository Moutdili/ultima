import json
import os
import re
import stat
from pathlib import Path

import pytest

from conftest import REPO
from ultima_companion import paths, settings
from ultima_companion.settings import DEFAULT_ORDER, OverlaySettings

CONTRACT = REPO / "Ultima.Engine" / "Contracts" / "EngineContracts.cs"


# ---- défauts ≡ contrat ------------------------------------------------------------------------

def test_default_order_is_mio_order():
    s = OverlaySettings.default()
    assert s.ids() == ["deck", "enchanted", "curses", "relics", "potions",
                       "reward", "shop", "paths", "deck_score"]
    assert [m.order for m in s.modules] == list(range(9))
    assert all(m.enabled for m in s.modules)


def test_default_serialization_identical_to_sample(sample):
    # Le daemon publie ses réglages dans state.settings : même forme exacte (clés, ordre, valeurs).
    assert OverlaySettings.default().to_json() == sample["settings"]
    assert json.dumps(OverlaySettings.default().to_json()) == json.dumps(sample["settings"])


@pytest.mark.skipif(not CONTRACT.exists(), reason="contrat C# absent")
def test_default_matches_csharp_contract():
    src = CONTRACT.read_text(encoding="utf-8")
    ids = dict(re.findall(r'(\w+) = "(\w+)"', src[src.index("class ModuleIds"):]))
    default_block = src[src.index("OverlaySettings Default"):src.index("});")]
    entries = re.findall(r"new\(ModuleIds\.(\w+), (true|false), (\d+)\)", default_block)
    expected = [{"id": ids[name], "enabled": en == "true", "order": int(o)} for name, en, o in entries]
    assert len(expected) == 9
    assert OverlaySettings.default().to_json() == {"modules": expected}
    assert set(ids.values()) == set(DEFAULT_ORDER)


# ---- réordonnancement --------------------------------------------------------------------------

def test_move_up_and_down_renumber():
    s = OverlaySettings.default()
    assert s.move_up("relics")
    assert s.ids()[:5] == ["deck", "enchanted", "relics", "curses", "potions"]
    assert [m.order for m in s.modules] == list(range(9))
    assert s.move_down("deck")
    assert s.ids()[:3] == ["enchanted", "deck", "relics"]
    assert [m.order for m in s.modules] == list(range(9))


def test_move_at_edges_is_noop():
    s = OverlaySettings.default()
    assert not s.move_up("deck")
    assert not s.move_down("deck_score")
    assert s.to_json() == OverlaySettings.default().to_json()


def test_move_unknown_raises():
    with pytest.raises(KeyError):
        OverlaySettings.default().move_up("nope")


def test_set_enabled_and_reset():
    s = OverlaySettings.default()
    s.extra = {"future": 1}
    s.set_enabled("potions", False)
    s.move_down("deck")
    assert s.modules[s.index_of("potions")].enabled is False
    s.reset()
    assert s.modules and s.to_json() == {"future": 1, **OverlaySettings.default().to_json()}


# ---- lecture tolérante ------------------------------------------------------------------------

def test_from_json_ignores_unknown_fields_and_sorts_by_order():
    data = {
        "unknownTop": {"x": 1},
        "modules": [
            {"id": "relics", "enabled": False, "order": 0, "colour": "red"},
            {"id": "deck", "enabled": True, "order": 1},
        ],
    }
    s = OverlaySettings.from_json(data)
    assert s.ids()[:2] == ["relics", "deck"]
    assert s.modules[0].enabled is False
    assert s.modules[0].to_json() == {"id": "relics", "enabled": False, "order": 0}
    # modules connus absents → ajoutés à la fin, activés, ordre défaut conservé
    assert s.ids()[2:] == ["enchanted", "curses", "potions", "reward", "shop", "paths", "deck_score"]
    assert [m.order for m in s.modules] == list(range(9))
    # clé de premier niveau inconnue conservée à l'écriture
    assert s.to_json()["unknownTop"] == {"x": 1}


def test_from_json_keeps_future_module_ids():
    data = {"modules": [{"id": "future_mod", "enabled": True, "order": 0}]}
    s = OverlaySettings.from_json(data)
    assert s.ids()[0] == "future_mod"
    assert len(s.modules) == 10
    assert "inconnu" in settings.module_label("future_mod")


@pytest.mark.parametrize("bad", [
    None, [], "x", 3,
    {"modules": None},
    {"modules": "deck"},
    {"modules": [1, "a", None, {"id": 5}, {"enabled": True}]},
])
def test_from_json_garbage_falls_back_to_defaults(bad):
    assert OverlaySettings.from_json(bad).ids() == list(DEFAULT_ORDER)


def test_from_json_bad_field_types_and_duplicates():
    data = {"modules": [
        {"id": "deck", "enabled": "yes", "order": "1"},
        {"id": "deck", "enabled": False, "order": 0},   # doublon ignoré
        {"id": "curses", "enabled": 0, "order": True},  # types invalides → défauts
    ]}
    s = OverlaySettings.from_json(data)
    assert s.ids()[:2] == ["deck", "curses"]
    assert s.modules[0].enabled is True and s.modules[1].enabled is True


def test_from_text_invalid_json():
    assert OverlaySettings.from_text("{not json").ids() == list(DEFAULT_ORDER)


# ---- fichier : chemin, écriture atomique, relecture -------------------------------------------

def test_settings_path_uses_temp_xdg(isolated_xdg):
    p = paths.settings_path()
    assert p == isolated_xdg["config"] / "ultima" / "settings.json"
    assert not str(p).startswith(str(Path.home() / ".config"))


def test_save_atomic_roundtrip(isolated_xdg):
    s = OverlaySettings.default()
    s.move_up("potions")
    s.set_enabled("paths", False)
    written = settings.save(s)
    assert written == isolated_xdg["config"] / "ultima" / "settings.json"
    raw = json.loads(written.read_text(encoding="utf-8"))
    assert list(raw) == ["modules"]
    assert all(list(m) == ["id", "enabled", "order"] for m in raw["modules"])  # camelCase, ordre C#
    assert raw["modules"][3] == {"id": "potions", "enabled": True, "order": 3}
    # aucun fichier temporaire laissé
    assert sorted(os.listdir(written.parent)) == ["settings.json"]
    assert stat.S_IMODE(written.stat().st_mode) == 0o644
    loaded = settings.load()
    assert loaded.exists and loaded.error is None
    assert loaded.settings.to_json() == s.to_json()


def test_save_replaces_via_rename(isolated_xdg):
    first = settings.save(OverlaySettings.default())
    ino1 = first.stat().st_ino
    s = OverlaySettings.default()
    s.move_down("deck")
    settings.save(s)
    # rename : nouvel inode → un lecteur ne voit jamais un fichier à moitié écrit
    assert first.stat().st_ino != ino1
    assert settings.load().settings.ids()[:2] == ["enchanted", "deck"]


def test_save_failure_leaves_previous_file_and_no_tmp(isolated_xdg, monkeypatch):
    target = settings.save(OverlaySettings.default())
    before = target.read_bytes()

    def boom(*_a, **_k):
        raise OSError("disque plein (simulé)")

    monkeypatch.setattr(settings.os, "replace", boom)
    s = OverlaySettings.default()
    s.set_enabled("deck", False)
    with pytest.raises(OSError):
        settings.save(s)
    assert target.read_bytes() == before
    assert sorted(os.listdir(target.parent)) == ["settings.json"]


def test_load_missing_and_invalid(isolated_xdg):
    r = settings.load()
    assert not r.exists and r.error is None and r.settings.ids() == list(DEFAULT_ORDER)
    p = paths.settings_path()
    p.parent.mkdir(parents=True)
    p.write_text("{oops", encoding="utf-8")
    r = settings.load()
    assert r.exists and r.error and "JSON invalide" in r.error
    assert r.settings.ids() == list(DEFAULT_ORDER)
