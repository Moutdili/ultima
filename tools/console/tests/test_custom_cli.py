"""Commandes v1.4 : theme, font, module, plugin — `--json`, NO_COLOR, validation par schéma/daemon.

Preuve demandée par T2 : les commandes existent, valident réellement, et appellent les commandes de
test du daemon de T1 quand il est là (`module-test`/`plugin-test`) ; sinon elles le disent
(« à brancher ») et retombent sur le schéma et, pour un plugin, sur un essai local.
"""

from __future__ import annotations

import json
import os
import stat

import pytest

from ultima_console import appearance, cli, custom, paths, theme

BAD_MODULE = {"id": "x.mauvais", "label": "Mauvais", "api": 1, "rows": [{"text": "{run.gold}", "tone": "rose"}]}
BAD_PLUGIN_MAIN = "import sys\nprint('pas du json', flush=True)\n"


def run(capsys, *args):
    code = cli.main(list(args))
    out = capsys.readouterr()
    return code, out.out, out.err


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False) if not isinstance(data, str) else data,
                    encoding="utf-8")


def fake_daemon(tmp_path, code: int, message: str = "ok") -> str:
    script = tmp_path / "fake-daemon"
    script.write_text(f"#!/bin/sh\necho '{message}'\nexit {code}\n")
    script.chmod(0o755)
    return str(script)


# ---------- module ----------

def test_module_new_validate_list(capsys):
    code, out, _ = run(capsys, "module", "new", "x.or-par-etage", "--label", "Or / étage")
    assert code == 0 and "x.or-par-etage" in out
    path = paths.modules_dir() / "x.or-par-etage.json"
    assert path.is_file()
    code, out, _ = run(capsys, "module", "validate", str(path), "--json")
    result = json.loads(out)
    assert code == 0 and result["ok"] and result["mode"] == "schema"
    assert "à brancher" in result["detail"]
    code, out, _ = run(capsys, "module", "list", "--json")
    rows = json.loads(out)
    assert code == 0 and rows[0]["id"] == "x.or-par-etage" and rows[0]["valid"] is True


def test_module_new_refuses_bad_id_and_existing(capsys):
    assert run(capsys, "module", "new", "pas-un-x")[0] == 1
    assert run(capsys, "module", "new", "x.ok")[0] == 0
    code, _, err = run(capsys, "module", "new", "x.ok")
    assert code == 1 and "existe déjà" in err


def test_module_validate_reports_schema_errors(capsys, tmp_path):
    bad = tmp_path / "mauvais.json"
    write(bad, BAD_MODULE)
    code, out, _ = run(capsys, "module", "validate", str(bad), "--json")
    result = json.loads(out)
    assert code == 1 and result["ok"] is False and result["status"] == "invalid"
    assert any("tone" in p or "hors des valeurs" in p for p in [result["detail"]])
    assert run(capsys, "module", "validate", str(tmp_path / "absent.json"))[0] == 1
    write(tmp_path / "casse.json", "{oups")
    assert run(capsys, "module", "validate", str(tmp_path / "casse.json"))[0] == 1


def test_module_validate_uses_the_daemon_when_present(capsys, tmp_path, monkeypatch):
    custom.new_module("x.daemon")
    m = paths.modules_dir() / "x.daemon.json"
    monkeypatch.setenv("ULTIMA_DAEMON", fake_daemon(tmp_path, 0, "module accepté"))
    code, out, _ = run(capsys, "module", "validate", str(m), "--json")
    result = json.loads(out)
    assert code == 0 and result["mode"] == "daemon" and "module accepté" in result["detail"]
    monkeypatch.setenv("ULTIMA_DAEMON", fake_daemon(tmp_path, 2, "module refusé"))
    code, out, _ = run(capsys, "module", "validate", str(m), "--json")
    assert code == 1 and json.loads(out)["status"] == "invalid"


# ---------- plugin ----------

def test_plugin_new_test_enable_disable(capsys):
    code, out, _ = run(capsys, "plugin", "new", "x.exemple")
    assert code == 0 and "x.exemple" in out
    assert (paths.plugins_dir() / "x.exemple" / "main.py").is_file()
    code, out, _ = run(capsys, "plugin", "test", "x.exemple", "--json")
    result = json.loads(out)
    assert code == 0 and result["ok"] and result["mode"] == "local"
    assert "à brancher" in result["detail"]

    assert appearance.plugins_enabled() == []
    code, out, _ = run(capsys, "plugin", "enable", "x.exemple", "--json")
    assert code == 0 and appearance.plugins_enabled() == ["x.exemple"]
    assert json.loads(out)["ok"] is True
    code, out, _ = run(capsys, "plugin", "list", "--json")
    rows = json.loads(out)
    assert code == 0 and rows[0]["id"] == "x.exemple" and rows[0]["enabled"] is True
    code, _, _ = run(capsys, "plugin", "disable", "x.exemple")
    assert code == 0 and appearance.plugins_enabled() == []


def test_plugin_test_rejects_bad_output_and_enable_refuses(capsys):
    folder = custom.new_plugin("x.casse")
    (folder / "main.py").write_text(BAD_PLUGIN_MAIN, encoding="utf-8")
    code, out, _ = run(capsys, "plugin", "test", "x.casse", "--json")
    result = json.loads(out)
    assert code == 1 and result["ok"] is False and result["status"] == "invalid"
    code, out, _ = run(capsys, "plugin", "enable", "x.casse", "--json")
    data = json.loads(out)
    assert code == 1 and data["ok"] is False and appearance.plugins_enabled() == []
    assert "test échoué" in data["message"]


def test_plugin_manifest_invalid_is_reported(capsys):
    folder = paths.plugins_dir() / "x.manifeste"
    folder.mkdir(parents=True)
    write(folder / "plugin.json", {"id": "x.manifeste", "api": 1})         # label et exec manquants
    code, out, _ = run(capsys, "plugin", "test", "x.manifeste", "--json")
    result = json.loads(out)
    assert code == 1 and result["status"] == "invalid" and result["mode"] == "schema"
    assert run(capsys, "plugin", "enable", "x.inconnu")[0] == 1


def test_plugin_test_uses_the_daemon_when_present(capsys, monkeypatch, tmp_path):
    custom.new_plugin("x.daemon")
    monkeypatch.setenv("ULTIMA_DAEMON", fake_daemon(tmp_path, 3, "sortie invalide"))
    code, out, _ = run(capsys, "plugin", "test", "x.daemon", "--json")
    result = json.loads(out)
    assert code == 1 and result["mode"] == "daemon" and result["code"] == 3
    monkeypatch.setenv("ULTIMA_DAEMON", fake_daemon(tmp_path, 0, "plugin accepté"))
    code, out, _ = run(capsys, "plugin", "test", "x.daemon", "--json")
    result = json.loads(out)
    assert code == 0 and result["mode"] == "daemon"


# ---------- theme / font ----------

def test_theme_commands(capsys):
    code, out, _ = run(capsys, "theme", "list", "--json")
    data = json.loads(out)
    ids = {t["id"] for t in data["themes"]}
    assert code == 0 and {"sts", "ironclad", "silent", "defect", "regent", "necrobinder", "nuit", "ardoise",
                          "contraste", "auto-perso", "terminal"} <= ids
    assert data["appearance"][0]["surface"] == "tui"

    code, out, _ = run(capsys, "theme", "show", "sts", "--json")
    shown = json.loads(out)
    assert code == 0 and shown["theme"]["id"] == "sts" and shown["resolved"]["accent"] == "#EFC851"

    code, out, _ = run(capsys, "theme", "check", "nuit", "--json")
    result = json.loads(out)
    assert code == 0 and result["ok"] and result["contrast"] >= 7.0

    code, out, _ = run(capsys, "theme", "set", "hud", "ardoise")
    assert code == 0 and appearance.load()[0].hud.theme == "ardoise"
    assert run(capsys, "theme", "set", "hud", "inconnu")[0] == 1
    assert run(capsys, "theme", "set", "tui")[0] == 2
    assert run(capsys, "theme", "show", "inconnu")[0] == 1
    assert run(capsys, "theme", "nimporte")[0] == 2


def test_theme_check_reports_a_low_contrast(capsys):
    paths.user_themes_dir().mkdir(parents=True)
    write(paths.user_themes_dir() / "fade.json", {"id": "fade", "label": "Fade", "api": 1, "extends": "sts",
                                                  "tones": {"text": "#888888", "background": "#808080"}})
    code, out, _ = run(capsys, "theme", "check", "fade", "--color", "never")
    assert code == 0 and "contraste" in out
    write(paths.user_themes_dir() / "casse.json", {"id": "casse", "label": "C", "api": 1, "tones": {"x": "y"}})
    code, out, _ = run(capsys, "theme", "check", "casse", "--json")
    assert code == 1 and json.loads(out)["ok"] is False


def test_font_commands(capsys):
    code, out, _ = run(capsys, "font", "list", "--json")
    data = json.loads(out)
    assert code == 0 and set(data["fonts"]) == {"base", "systeme", "jeu", "perso"}
    assert data["baseProblems"] == []
    code, out, _ = run(capsys, "font", "refresh", "--json")
    info = json.loads(out)
    assert code == 0 and len(info["links"]) == 2 and paths.fontconfig_path().is_file()
    code, out, _ = run(capsys, "font", "list", "--color", "never")
    assert code == 0 and "Liberation Sans" in out


def test_no_color_and_json_are_respected(capsys, monkeypatch):
    monkeypatch.setenv("NO_COLOR", "1")
    monkeypatch.setenv("FORCE_COLOR", "1")
    for args in (("theme", "list"), ("font", "list"), ("module", "list"), ("plugin", "list")):
        code, out, _ = run(capsys, *args)
        assert "\x1b[" not in out
    code, out, _ = run(capsys, "theme", "list", "--color", "always")
    assert "\x1b[" in out


def test_readme_mentions_new_command_groups():
    text = (paths.repo_root() / "docs" / "personnalisation" / "README.md").read_text(encoding="utf-8")
    for cmd in ("ultima theme list", "ultima font list", "ultima module validate", "ultima plugin test"):
        assert cmd in text
