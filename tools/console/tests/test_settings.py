"""settings.json : miroir d'OverlaySettings/HudSettings v1.3, même validation que le daemon (SettingsStore)."""

import json
import re

from conftest import REPO, load_fixture

from ultima_console import paths, settings
from ultima_console.settings import DEFAULT_ORDER, HudSettings, OverlaySettings

CONTRACT = (REPO / "Ultima.Engine" / "Contracts" / "EngineContracts.cs").read_text(encoding="utf-8")


def test_default_order_matches_contract():
    ids = dict(re.findall(r'(\w+) = "(\w+)"', CONTRACT[CONTRACT.index("class ModuleIds"):]))
    block = CONTRACT[CONTRACT.index("public static OverlaySettings Default"):CONTRACT.index("public sealed record ModuleSetting")]
    order = [ids[m] for m in re.findall(r"new\(ModuleIds\.(\w+), true, \d+\)", block)]
    assert tuple(order) == DEFAULT_ORDER


def test_hud_default_matches_contract():
    m = re.search(r'HudSettings Default \{ get; \} = new\("(\w+)", ([\d.]+), ([\d.]+), "(\w+)", (\w+), (\w+)\)', CONTRACT)
    d = HudSettings()
    assert (d.quality, d.height_fraction, d.rest_opacity, d.side) == (m[1], float(m[2]), float(m[3]), m[4])
    assert (d.deck_score_in_combat, d.grades_only) == (m[5] == "true", m[6] == "true")


def test_published_settings_roundtrip():
    pub = load_fixture("v13")["settings"]
    assert OverlaySettings.from_json(pub).to_json() == pub


def test_absent_file_is_default_without_error():
    s, err = settings.load()
    assert err is None and [m.id for m in s.modules] == list(DEFAULT_ORDER)


def test_invalid_file_reports_error_and_is_not_rewritten():
    paths.settings_path().parent.mkdir(parents=True)
    paths.settings_path().write_text("{oups")
    s, err = settings.load()
    assert "invalide" in err and s.hud.quality == "verre"
    assert paths.settings_path().read_text() == "{oups"


def test_hud_bounds_match_contract():
    m = re.search(r"HeightMin = ([\d.]+), HeightMax = ([\d.]+), OpacityMin = ([\d.]+), OpacityMax = ([\d.]+);", CONTRACT)
    assert m, "bornes HudSettings introuvables dans EngineContracts.cs"
    assert (settings.HEIGHT_MIN, settings.HEIGHT_MAX, settings.OPACITY_MIN, settings.OPACITY_MAX) == tuple(map(float, m.groups()))


def test_hud_field_by_field_validation_and_clamping():
    h = HudSettings.from_json({"quality": "LOUPE", "heightFraction": 0, "restOpacity": 1.5, "side": "Left",
                               "deckScoreInCombat": "oui", "gradesOnly": True})
    assert (h.quality, h.side) == ("loupe", "left")
    assert (h.height_fraction, h.rest_opacity) == (0.30, 1.0)            # ramenés aux bornes
    assert (h.deck_score_in_combat, h.grades_only) == (False, True)
    h = HudSettings.from_json({"Quality": "eco", "HEIGHTFRACTION": 0.95, "restOpacity": 0.1, "futur": 7})
    assert (h.quality, h.height_fraction, h.rest_opacity) == ("eco", 0.85, 0.35)
    assert h.to_json()["futur"] == 7 and "Quality" not in h.to_json()
    assert HudSettings.from_json(None).to_json() == HudSettings().to_json()
    assert HudSettings.from_json({"heightFraction": float("nan"), "restOpacity": "0.5", "quality": "max"}).to_json() \
        == HudSettings().to_json()                                        # type invalide → défaut du champ
    h = HudSettings()
    for _ in range(50):
        h.step_height(1)
        h.step_opacity(-1)
    assert (h.height_fraction, h.rest_opacity) == (0.85, 0.35)


def test_module_without_enabled_is_true():
    s = OverlaySettings.from_json({"modules": [{"id": "relics", "order": 0}, {"id": "deck", "enabled": False}]})
    assert s.modules[0].id == "relics" and s.modules[0].enabled is True
    assert s.modules[1].enabled is False


def _js_labels():
    js = (REPO / "overlay" / "gnome" / "ultima@mio" / "grammar.js").read_text(encoding="utf-8")
    block = js[js.index("export const MODULE_LABEL"):]
    block = block[: block.index("});")]
    return dict(re.findall(r"(\w+): '([^']*)'", block))


def test_module_labels_match_hud_and_companion():
    import ast
    assert _js_labels() == settings.MODULE_LABELS
    py = (REPO / "overlay" / "companion" / "ultima_companion" / "settings.py").read_text(encoding="utf-8")
    tree = ast.parse(py)
    consts = {n.targets[0].id: n.value.value for n in tree.body
              if isinstance(n, ast.Assign) and isinstance(n.targets[0], ast.Name) and isinstance(n.value, ast.Constant)}
    node = next(n for n in tree.body if isinstance(n, ast.AnnAssign) and getattr(n.target, "id", "") == "MODULE_LABELS")
    companion = {consts[k.id]: v.value for k, v in zip(node.value.keys, node.value.values)}
    assert companion == settings.MODULE_LABELS
    assert list(settings.MODULE_LABELS) == list(DEFAULT_ORDER)       # même ordre que le contrat


def test_same_text_as_companion_writer():
    """Le fichier écrit par le TUI est octet pour octet celui qu'écrirait le compagnon."""
    import importlib.util
    import sys
    import types
    pkg = types.ModuleType("ultima_companion")
    pkg.__path__ = []
    pkg.paths = types.SimpleNamespace(settings_path=lambda: None)
    sys.modules["ultima_companion"] = pkg
    sys.modules["ultima_companion.paths"] = pkg.paths
    spec = importlib.util.spec_from_file_location(
        "ultima_companion.settings", REPO / "overlay" / "companion" / "ultima_companion" / "settings.py")
    comp = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = comp          # requis par @dataclass
    try:
        spec.loader.exec_module(comp)
        mine = OverlaySettings.default()
        mine.toggle("potions")
        mine.move("relics", -3)
        mine.hud.cycle_quality(1)
        mine.hud.step_height(2)
        mine.hud.cycle_side()
        theirs = comp.OverlaySettings.from_json(mine.to_json())
        assert mine.to_text() == theirs.to_text()
    finally:
        for k in ("ultima_companion", "ultima_companion.paths", "ultima_companion.settings"):
            sys.modules.pop(k, None)


def test_modules_normalised():
    raw = {"Modules": [{"Id": "relics", "Enabled": False, "Order": 5}, {"id": "deck", "order": 1},
                       {"id": "deck", "order": 0}, {"id": "futur", "enabled": True, "order": 2, "x": 1}, 3],
           "autre": True}
    s = OverlaySettings.from_json(raw)
    ids = [m.id for m in s.modules]
    assert ids[:3] == ["deck", "futur", "relics"]
    assert set(DEFAULT_ORDER) <= set(ids) and len(ids) == len(set(ids))
    assert [m.order for m in s.modules] == list(range(len(ids)))
    assert not s.modules[2].enabled
    out = s.to_json()
    assert out["autre"] is True and out["modules"][1] == {"id": "futur", "enabled": True, "order": 1, "x": 1}


def test_edit_and_atomic_save():
    s = OverlaySettings.default()
    s.toggle("potions")
    assert s.move("deck_score", -100) == 0
    s.hud.cycle_quality(1)
    s.hud.step_height(100)
    s.hud.step_opacity(-100)
    s.hud.cycle_side()
    p = settings.save(s)
    data = json.loads(p.read_text())
    assert data["modules"][0] == {"id": "deck_score", "enabled": True, "order": 0}
    assert {"id": "potions", "enabled": False, "order": 5} in data["modules"]
    assert data["hud"] == {"quality": "loupe", "heightFraction": settings.HEIGHT_MAX, "restOpacity": settings.OPACITY_MIN,
                           "side": "left", "deckScoreInCombat": False, "gradesOnly": False}
    assert [f.name for f in p.parent.iterdir()] == ["settings.json"]  # aucun fichier temporaire
    again, err = settings.load()
    assert err is None and again.to_json() == data
