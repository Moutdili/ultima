"""`docs/personnalisation/README.md` : CHAQUE exemple du guide passe réellement les commandes.

Le README est extrait par marqueur (`<!-- exemple: … -->`), écrit dans un HOME temporaire, puis :
thème → `ultima theme check`, module → `ultima module validate`, plugin → `ultima plugin test`,
`appearance` → schéma. Un exemple qui cesse de marcher casse la porte.
"""

from __future__ import annotations

import json
import re

import pytest
from conftest import REPO

from ultima_console import appearance, cli, paths, schema, theme

README = REPO / "docs" / "personnalisation" / "README.md"
BLOCK = re.compile(r"<!-- exemple: (?P<name>[\w-]+) -->\s*```(?P<lang>\w+)\n(?P<body>.*?)\n```", re.S)


def examples() -> dict[str, str]:
    text = README.read_text(encoding="utf-8")
    found = {m.group("name"): m.group("body") for m in BLOCK.finditer(text)}
    assert set(found) >= {"theme", "module", "plugin-manifest", "plugin-code", "appearance"}
    return found


def run(capsys, *args):
    code = cli.main(list(args))
    return code, capsys.readouterr()


def test_readme_is_written_for_the_user_and_their_agent():
    text = README.read_text(encoding="utf-8")
    assert "Consignes pour un agent" in text
    assert "~/.config/ultima/themes" in text and "~/.local/share/ultima/plugins" in text
    assert "schemas/" in text and "docs/44-personnalisation.md" in text
    for cmd in ("ultima theme check", "ultima module validate", "ultima plugin test"):
        assert cmd in text


def test_theme_example_passes_theme_check(capsys):
    body = examples()["theme"]
    data = json.loads(body)
    assert data["id"] == "brume"
    paths.user_themes_dir().mkdir(parents=True)
    (paths.user_themes_dir() / f"{data['id']}.json").write_text(body + "\n", encoding="utf-8")
    code, out = run(capsys, "theme", "check", data["id"], "--json")
    result = json.loads(out.out)
    assert code == 0 and result["ok"], result
    assert result["contrast"] >= 4.5


def test_module_example_passes_module_validate(capsys, tmp_path):
    body = examples()["module"]
    data = json.loads(body)
    assert data["id"].startswith("x.")
    path = paths.modules_dir() / f"{data['id']}.json"
    path.parent.mkdir(parents=True)
    path.write_text(body + "\n", encoding="utf-8")
    code, out = run(capsys, "module", "validate", str(path), "--json")
    result = json.loads(out.out)
    assert code == 0 and result["ok"], result


def test_plugin_example_passes_plugin_test(capsys):
    blocks = examples()
    manifest = json.loads(blocks["plugin-manifest"])
    folder = paths.plugins_dir() / manifest["id"]
    folder.mkdir(parents=True)
    (folder / "plugin.json").write_text(blocks["plugin-manifest"] + "\n", encoding="utf-8")
    main = folder / "main.py"
    main.write_text(blocks["plugin-code"] + "\n", encoding="utf-8")
    main.chmod(0o755)
    code, out = run(capsys, "plugin", "test", manifest["id"], "--json")
    result = json.loads(out.out)
    assert code == 0 and result["ok"], result
    assert result["mode"] in ("local", "daemon")


def test_appearance_example_matches_the_schema():
    body = examples()["appearance"]
    data = json.loads("{" + body + "}")                 # le README montre la clé, pas le fichier entier
    assert schema.validate(data["appearance"], schema.load("appearance")) == []
    a = appearance.Appearance.from_json(data["appearance"])
    assert a.tui.theme == "terminal" and a.hud.position.width == 360
    assert a.hud.font.family == "Liberation Sans" and a.hud.font.size == 14


def test_every_fenced_command_block_is_runnable_shape():
    """Les blocs bash du README ne contiennent que des commandes `ultima …` ou des commentaires."""
    text = README.read_text(encoding="utf-8")
    for m in re.finditer(r"```bash\n(.*?)\n```", text, re.S):
        for line in m.group(1).splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            assert line.startswith(("ultima ", "tools/", "cd ")), line
