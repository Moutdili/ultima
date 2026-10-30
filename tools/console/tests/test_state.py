"""Lecture de l'état publié (contrat v1.3) : docs/ipc/state.v13.sample.json, fixtures, cas limites."""

import json

from conftest import SAMPLE_V13, load_fixture, publish

from ultima_console import CONTRACT_MAJOR, CONTRACT_MINOR, paths, state


def test_contract_constants_match_engine():
    eng = (SAMPLE_V13.parents[2] / "Ultima.Engine" / "Contracts" / "EngineContracts.cs").read_text()
    v12 = (SAMPLE_V13.parents[2] / "Ultima.Engine" / "Contracts" / "EngineContractsV12.cs").read_text()
    assert f"public const int CurrentVersion = {CONTRACT_MAJOR};" in eng
    assert f"public const int CurrentMinor = {CONTRACT_MINOR};" in v12


def test_sample_v13_parses():
    r = state.parse(SAMPLE_V13.read_text(encoding="utf-8"))
    assert r.kind == state.OK
    s = r.state
    assert (s["contractVersion"], s["contractMinor"]) == (1, 3)
    assert s["settings"]["hud"]["quality"] == "verre"
    assert state.get(s, "reward") is None and state.get(s, "shop") is None and state.get(s, "event") is None
    assert state.get(s, "run", "party")[0]["isLocal"] is True


def test_load_published_file():
    publish(load_fixture("shop"))
    r = state.load()
    assert r.kind == state.OK and r.path == paths.state_path() and r.age_s is not None
    assert r.state["screen"] == "Shop"


def test_missing_invalid_too_new():
    assert state.load().kind == state.MISSING
    publish("{pas du json")
    assert state.load().kind == state.INVALID
    publish("[1, 2]")
    assert state.load().kind == state.INVALID
    publish('{"screen": "Map"}')
    r = state.load()
    assert r.kind == state.INVALID and "contractVersion" in r.error
    publish(json.dumps({"contractVersion": CONTRACT_MAJOR + 1, "contractMinor": 0}))
    r = state.load()
    assert r.kind == state.TOO_NEW and "mettre à jour ULTIMA" in r.error


def test_unknown_fields_and_newer_minor_are_accepted():
    s = load_fixture("v13")
    s["contractMinor"] = CONTRACT_MINOR + 5
    s["champFutur"] = {"x": 1}
    s["run"]["nouveau"] = [1, 2]
    publish(s)
    assert state.load().kind == state.OK


def test_tolerant_accessors():
    assert state.get({"a": {"b": None}}, "a", "b", default="—") == "—"
    assert state.get({"a": 3}, "a", "b") is None
    assert state.arr(None) == [] and state.arr({"x": 1}) == []
    assert state.num(True) is None and state.num("3") is None and state.num(2.6) == 3


def test_probe_socket(fake_socket):
    assert state.probe_socket() == state.ABSENT
    s = fake_socket()
    assert state.probe_socket() == state.ALIVE
    s.close(keep_file=True)  # le fichier reste, personne n'écoute
    assert state.probe_socket() == state.REFUSED
