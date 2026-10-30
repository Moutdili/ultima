import json
import os
import re
import stat
from pathlib import Path

import pytest

from conftest import FIXTURES, REPO
from ultima_companion import paths, settings
from ultima_companion.settings import DEFAULT_ORDER, HudSettings, OverlaySettings

CONTRACT = REPO / "Ultima.Engine" / "Contracts" / "EngineContracts.cs"
# Sortie RÉELLE de System.Text.Json (Web defaults, .NET 9) sur OverlaySettings.Default du contrat v1.3.
DOTNET_DEFAULT = FIXTURES / "settings.v13.default.json"
N = len(DEFAULT_ORDER)


# ---- défauts ≡ contrat ------------------------------------------------------------------------

def test_default_order_is_mio_order():
    s = OverlaySettings.default()
    assert s.ids() == ["deck", "enchanted", "curses", "relics", "potions",
                       "reward", "shop", "event", "paths", "route", "party", "deck_score"]
    assert [m.order for m in s.modules] == list(range(12))
    assert all(m.enabled for m in s.modules)
    assert s.hud.to_json() == {"quality": "verre", "heightFraction": 0.55, "restOpacity": 0.62,
                               "side": "right", "deckScoreInCombat": False, "gradesOnly": False}


def test_default_serialization_identical_to_dotnet():
    # Même texte, octet pour octet, que System.Text.Json sur le record C# (clés, ordre, valeurs).
    expected = DOTNET_DEFAULT.read_text(encoding="utf-8").strip()
    assert json.dumps(OverlaySettings.default().to_json(), separators=(",", ":")) == expected
    assert OverlaySettings.from_text(expected).to_json() == json.loads(expected)


def test_v10_settings_from_sample_upgrade_cleanly(sample):
    # docs/22-state.sample.json est un état v1.0 (9 modules, pas de hud) : lu sans perte.
    s = OverlaySettings.from_json(sample["settings"])
    assert s.ids()[:9] == [m["id"] for m in sample["settings"]["modules"]]
    assert s.ids()[9:] == ["event", "route", "party"]       # modules v1.2 ajoutés à la fin
    assert s.hud.to_json() == HudSettings.default().to_json()


@pytest.mark.skipif(not CONTRACT.exists(), reason="contrat C# absent")
def test_default_matches_csharp_contract():
    src = CONTRACT.read_text(encoding="utf-8")
    mod_block = src[src.index("class ModuleIds"):]
    mod_block = mod_block[:mod_block.index("}")]
    ids = dict(re.findall(r'(\w+) = "(\w+)"', mod_block))
    default_block = src[src.index("OverlaySettings Default"):src.index("});")]
    entries = re.findall(r"new\(ModuleIds\.(\w+), (true|false), (\d+)\)", default_block)
    expected = [{"id": ids[name], "enabled": en == "true", "order": int(o)} for name, en, o in entries]
    assert len(expected) == 12
    assert OverlaySettings.default().to_json()["modules"] == expected
    assert set(ids.values()) == set(DEFAULT_ORDER)
    # HudSettings.Default et Qualities
    m = re.search(r'HudSettings Default \{ get; \} = new\("(\w+)", ([\d.]+), ([\d.]+), "(\w+)", (true|false), (true|false)\)', src)
    assert m, "HudSettings.Default introuvable"
    d = HudSettings.default()
    assert (d.quality, d.height_fraction, d.rest_opacity, d.side) == (m[1], float(m[2]), float(m[3]), m[4])
    assert (d.deck_score_in_combat, d.grades_only) == (m[5] == "true", m[6] == "true")
    q = re.search(r"Qualities = new\[\] \{ ([^}]*) \}", src)
    assert tuple(re.findall(r'"(\w+)"', q[1])) == settings.QUALITIES


# ---- réordonnancement --------------------------------------------------------------------------

def test_move_up_and_down_renumber():
    s = OverlaySettings.default()
    assert s.move_up("relics")
    assert s.ids()[:5] == ["deck", "enchanted", "relics", "curses", "potions"]
    assert [m.order for m in s.modules] == list(range(N))
    assert s.move_down("deck")
    assert s.ids()[:3] == ["enchanted", "deck", "relics"]
    assert [m.order for m in s.modules] == list(range(N))


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
    assert s.ids()[2:] == ["enchanted", "curses", "potions", "reward", "shop", "event", "paths",
                           "route", "party", "deck_score"]
    assert [m.order for m in s.modules] == list(range(N))
    # clé de premier niveau inconnue conservée à l'écriture
    assert s.to_json()["unknownTop"] == {"x": 1}


def test_from_json_keeps_future_module_ids():
    data = {"modules": [{"id": "future_mod", "enabled": True, "order": 0}]}
    s = OverlaySettings.from_json(data)
    assert s.ids()[0] == "future_mod"
    assert len(s.modules) == N + 1
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
    assert list(raw) == ["modules", "hud"]
    assert list(raw["hud"]) == ["quality", "heightFraction", "restOpacity", "side",
                                "deckScoreInCombat", "gradesOnly"]
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


# ---- HudSettings v1.3 ----------------------------------------------------------------------------

def test_hud_missing_or_garbage_is_default():
    for data in ({"modules": []}, {"hud": None}, {"hud": "loupe"}, {"hud": []}, None):
        assert OverlaySettings.from_json(data).hud.to_json() == HudSettings.default().to_json()


@pytest.mark.parametrize("field,value,expected", [
    ("quality", "loupe", "loupe"), ("quality", "eco", "eco"), ("quality", "LOUPE", "verre"),
    ("quality", 3, "verre"),
    ("side", "left", "left"), ("side", "top", "right"), ("side", None, "right"),
    ("heightFraction", 0.8, 0.8), ("heightFraction", 1, 1.0), ("heightFraction", 0, 0.55),
    ("heightFraction", 1.5, 0.55), ("heightFraction", -0.1, 0.55), ("heightFraction", "0.7", 0.55),
    ("heightFraction", True, 0.55),
    ("restOpacity", 0, 0.0), ("restOpacity", 0.3, 0.3), ("restOpacity", 2, 0.62),
    ("deckScoreInCombat", True, True), ("deckScoreInCombat", "yes", False),
    ("gradesOnly", True, True), ("gradesOnly", 1, False),
])
def test_hud_field_variants(field, value, expected):
    hud = HudSettings.from_json({field: value}).to_json()
    assert hud[field] == expected


def test_hud_unknown_keys_preserved_and_order():
    s = OverlaySettings.from_json({"hud": {"gradesOnly": True, "quality": "eco", "glow": 2}, "zz": 1})
    out = s.to_json()
    assert list(out) == ["modules", "hud", "zz"]
    assert list(out["hud"]) == ["quality", "heightFraction", "restOpacity", "side",
                                "deckScoreInCombat", "gradesOnly", "glow"]
    assert out["hud"]["quality"] == "eco" and out["hud"]["gradesOnly"] is True


def test_hud_setters_clamp_and_round():
    h = HudSettings()
    h.set_height(0.1234)
    assert h.height_fraction == settings.HEIGHT_RANGE[0]
    h.set_height(0.6666)
    assert h.height_fraction == 0.67
    h.set_opacity(5)
    assert h.rest_opacity == 1.0
    h.set_quality("loupe")
    h.set_side("left")
    with pytest.raises(ValueError):
        h.set_quality("ultra")
    with pytest.raises(ValueError):
        h.set_side("top")
    assert h.to_json()["heightFraction"] == 0.67 and h.to_json()["side"] == "left"


def test_reset_modules_keeps_hud_and_reset_hud_keeps_modules():
    s = OverlaySettings.default()
    s.hud.set_quality("eco")
    s.move_down("deck")
    s.reset()
    assert s.hud.quality == "eco" and s.ids() == list(DEFAULT_ORDER)
    s.move_down("deck")
    s.reset_hud()
    assert s.hud.to_json() == HudSettings.default().to_json() and s.ids()[0] == "enchanted"


def test_hud_save_load_roundtrip_atomic(isolated_xdg):
    p = paths.settings_path()
    p.parent.mkdir(parents=True)
    # fichier v1.0 existant (sans hud) : la première écriture ajoute hud sans toucher aux modules
    p.write_text(json.dumps({"modules": [{"id": "relics", "enabled": False, "order": 0}]}), encoding="utf-8")
    s = settings.load().settings
    s.hud.set_quality("loupe")
    s.hud.set_height(0.7)
    s.hud.set_opacity(0.4)
    s.hud.set_side("left")
    s.hud.deck_score_in_combat = True
    s.hud.grades_only = True
    settings.save(s)
    raw = json.loads(p.read_text(encoding="utf-8"))
    assert raw["hud"] == {"quality": "loupe", "heightFraction": 0.7, "restOpacity": 0.4, "side": "left",
                          "deckScoreInCombat": True, "gradesOnly": True}
    assert raw["modules"][0] == {"id": "relics", "enabled": False, "order": 0}
    assert sorted(os.listdir(p.parent)) == ["settings.json"]
    assert settings.load().settings.to_json() == s.to_json()


def test_same_applied_compares_hud():
    a, b = OverlaySettings.default(), OverlaySettings.default()
    assert a.same_applied(b)
    b.hud.set_quality("eco")
    assert a.same_modules(b) and not a.same_applied(b)
