"""Modules déclaratifs et plugins (docs/44 §6-§7) : lister, valider, créer, tester, activer.

Rien n'est évalué ici : l'évaluateur unique reste le daemon (T1). Ce module est la porte d'entrée des
commandes `ultima module` / `ultima plugin` :

- `module validate` / `plugin test` appellent le daemon (`ultima-daemon module-test|plugin-test`, T1) ;
- s'il est absent ou ne connaît pas la commande, on retombe sur la **validation par schéma** et, pour un
  plugin, sur un **essai local** (un processus, un état d'exemple, une ligne de sortie validée) — le
  résultat est alors marqué `mode: "local"` / `"schema"` et la raison « à brancher » est écrite ;
- un plugin ne s'active (`settings.json → pluginsEnabled`) qu'après un test RÉUSSI (`ultima plugin
  enable` refuse un plugin qui échoue) ;
- `module new` / `plugin new` créent des squelettes qui passent ce test.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from . import appearance, paths, schema

ID_RE = re.compile(r"^x\.[a-z0-9][a-z0-9-]{0,39}$")
SCREENS: tuple[str, ...] = ("menu", "map", "combat", "reward", "shop", "event", "restSite", "treasure")

MODULE_SKELETON = {
    "id": "x.exemple",
    "label": "Exemple",
    "api": 1,
    "screens": ["map", "shop"],
    "rows": [
        {"text": "{run.gold} or · étage {run.floor}", "tone": "accent"},
        {"when": "run.gold >= 150", "text": "Shop rentable", "tone": "ok"},
    ],
}

PLUGIN_MANIFEST = {
    "id": "x.exemple",
    "label": "Exemple",
    "api": 1,
    "version": "1.0",
    "description": "Plugin d'exemple : or et étage, une ligne.",
    "exec": ["python3", "main.py"],
    "screens": ["map", "shop"],
    "timeoutMs": 300,
    "minIntervalMs": 1000,
}

PLUGIN_MAIN = '''#!/usr/bin/env python3
"""Plugin ULTIMA minimal : une ligne JSON d'état sur stdin, une ligne JSON de sortie sur stdout."""
import json
import sys


def render(state):
    run = state.get("run") or {}
    gold = run.get("gold")
    floor = run.get("floor")
    if gold is None:
        return [{"text": "or —", "tone": "muted"}]
    text = f"{gold} or · étage {floor if floor is not None else '—'}"
    tone = "ok" if gold >= 150 else "accent"
    return [{"text": text, "tone": tone}]


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            message = json.loads(line)
        except ValueError:
            continue
        print(json.dumps({"rows": render(message.get("state") or {})}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
'''


class CustomError(Exception):
    pass


@dataclass
class Finding:
    """Un module ou un plugin tel qu'il est sur disque."""
    id: str
    label: str
    path: Path
    kind: str                       # module | plugin
    valid: bool
    problems: list[str] = field(default_factory=list)
    screens: list[str] = field(default_factory=list)
    raw: dict[str, Any] = field(default_factory=dict)
    enabled: bool = False

    def to_json(self) -> dict[str, Any]:
        return {"id": self.id, "label": self.label, "kind": self.kind, "path": str(self.path),
                "valid": self.valid, "problems": self.problems, "screens": self.screens,
                "enabled": self.enabled}


@dataclass
class TestResult:
    ok: bool
    status: str                     # ok | invalid | unavailable | error
    mode: str                       # daemon | local | schema
    detail: str = ""
    output: str = ""
    code: int | None = None

    def to_json(self) -> dict[str, Any]:
        return {"ok": self.ok, "status": self.status, "mode": self.mode, "detail": self.detail,
                "code": self.code, "output": self.output}


# ---------- modules ----------

def module_files() -> list[Path]:
    return sorted(paths.modules_dir().glob("*.json")) if paths.modules_dir().is_dir() else []


def read_module(path: Path) -> Finding:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError, RecursionError) as e:
        return Finding(id=path.stem, label=path.name, path=path, kind="module", valid=False,
                       problems=[f"JSON illisible : {e}"])
    if not isinstance(data, dict):
        return Finding(id=path.stem, label=path.name, path=path, kind="module", valid=False,
                       problems=["objet JSON attendu"])
    problems = schema.validate(data, schema.load("module"))
    screens = [s for s in data.get("screens") or [] if isinstance(s, str)]
    return Finding(id=str(data.get("id") or path.stem), label=str(data.get("label") or path.stem),
                   path=path, kind="module", valid=not problems, problems=problems,
                   screens=screens, raw=data)


def load_modules() -> list[Finding]:
    return [read_module(p) for p in module_files()]


def validate_module(path: Path) -> tuple[Finding, list[str]]:
    """(module, erreurs). Erreurs = schéma + identifiant `x.…` + écrans connus."""
    f = read_module(path)
    errs = list(f.problems)
    if not f.path.is_file():
        return f, [f"fichier introuvable : {path}"]
    mid = f.raw.get("id") if isinstance(f.raw, dict) else None
    if isinstance(mid, str) and not ID_RE.match(mid):
        errs.append(f"id « {mid} » : un module perso commence par « x. » (x.exemple)")
    bad = [s for s in f.screens if s not in SCREENS]
    if bad:
        errs.append("écrans inconnus : " + ", ".join(bad))
    return f, errs


def new_module(module_id: str, label: str | None = None, directory: Path | None = None) -> Path:
    if not ID_RE.match(module_id):
        raise CustomError(f"id invalide « {module_id} » : x.<nom> (minuscules, chiffres, tirets)")
    directory = directory or paths.modules_dir()
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / f"{module_id}.json"
    if path.exists():
        raise CustomError(f"{path} existe déjà")
    data = dict(MODULE_SKELETON)
    data["id"] = module_id
    data["label"] = label or module_id.removeprefix("x.").replace("-", " ").capitalize()
    errs = schema.validate(data, schema.load("module"))
    if errs:                                  # le squelette doit toujours passer : sinon on ne l'écrit pas
        raise CustomError("squelette invalide : " + " ; ".join(errs))
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return path


# ---------- plugins ----------

def plugin_dirs() -> list[Path]:
    root = paths.plugins_dir()
    return sorted(p for p in root.iterdir() if p.is_dir()) if root.is_dir() else []


def read_plugin(folder: Path) -> Finding:
    manifest = folder / "plugin.json"
    if not manifest.is_file():
        return Finding(id=folder.name, label=folder.name, path=folder, kind="plugin", valid=False,
                       problems=["plugin.json absent"])
    try:
        data = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, ValueError, RecursionError) as e:
        return Finding(id=folder.name, label=folder.name, path=folder, kind="plugin", valid=False,
                       problems=[f"plugin.json illisible : {e}"])
    if not isinstance(data, dict):
        return Finding(id=folder.name, label=folder.name, path=folder, kind="plugin", valid=False,
                       problems=["objet JSON attendu"])
    problems = schema.validate(data, schema.load("plugin"))
    exec_ = data.get("exec")
    if isinstance(exec_, list) and exec_ and isinstance(exec_[0], str):
        if not (folder / exec_[0]).exists() and exec_[0].startswith((".", "/")):
            problems.append(f"exécutable introuvable : {exec_[0]}")
    return Finding(id=str(data.get("id") or folder.name), label=str(data.get("label") or folder.name),
                   path=folder, kind="plugin", valid=not problems, problems=problems,
                   screens=[s for s in data.get("screens") or [] if isinstance(s, str)],
                   raw=data, enabled=str(data.get("id")) in appearance.plugins_enabled())


def load_plugins() -> list[Finding]:
    return [read_plugin(p) for p in plugin_dirs()]


def new_plugin(plugin_id: str, label: str | None = None, root: Path | None = None) -> Path:
    if not ID_RE.match(plugin_id):
        raise CustomError(f"id invalide « {plugin_id} » : x.<nom>")
    folder = (root or paths.plugins_dir()) / plugin_id
    if folder.exists():
        raise CustomError(f"{folder} existe déjà")
    folder.mkdir(parents=True)
    manifest = dict(PLUGIN_MANIFEST)
    manifest["id"] = plugin_id
    manifest["label"] = label or plugin_id.removeprefix("x.").replace("-", " ").capitalize()
    (folder / "plugin.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
                                        encoding="utf-8")
    main = folder / "main.py"
    main.write_text(PLUGIN_MAIN, encoding="utf-8")
    main.chmod(0o755)
    errs = schema.validate(manifest, schema.load("plugin"))
    if errs:
        raise CustomError("squelette invalide : " + " ; ".join(errs))
    return folder


def sample_states() -> list[Path]:
    """États d'exemple (`docs/ipc/state.*.sample.json`, v1.3 puis v1.4) : dépôt, installation, ULTIMA_SAMPLES."""
    found: list[Path] = []
    env = os.environ.get("ULTIMA_SAMPLES")
    if env and Path(env).is_dir():
        found.extend(sorted(Path(env).glob("*.sample.json")))
    root = paths.repo_root()
    if root is not None:
        found.extend(sorted((root / "docs" / "ipc").glob("state.*.sample.json")))
    installed = paths.ultima_home() / "samples"
    if installed.is_dir():
        found.extend(sorted(installed.glob("*.sample.json")))
    seen: set[str] = set()
    out: list[Path] = []
    for p in found:
        key = p.name
        if key not in seen and p.is_file():
            seen.add(key)
            out.append(p)
    return out


# ---------- appels au daemon ----------

def _daemon() -> Path | None:
    return paths.find_daemon()


def _run_daemon(argv: list[str], timeout: float = 30.0) -> tuple[str, int | None]:
    """(sortie, code). Code None = daemon absent ou commande inconnue (à brancher)."""
    daemon = _daemon()
    if daemon is None:
        return "", None
    try:
        r = subprocess.run([str(daemon), *argv], capture_output=True, text=True, timeout=timeout,
                           env=paths.daemon_env())
    except (OSError, subprocess.TimeoutExpired) as e:
        return str(e), None
    out = ((r.stdout or "") + (r.stderr or "")).strip()
    if r.returncode not in (0, 2, 3):
        # 1 = usage : le daemon ne connaît pas encore la commande (T1 non fusionnée)
        return out, None
    return out, r.returncode


def test_module(path: Path) -> TestResult:
    finding, errs = validate_module(path)
    if errs:
        return TestResult(False, "invalid", "schema", "; ".join(errs[:6]))
    out, code = _run_daemon(["module-test", str(path)])
    if code is None:
        return TestResult(True, "ok", "schema",
                          "validation par schéma OK ; test du daemon indisponible (à brancher)", out)
    if code == 0:
        return TestResult(True, "ok", "daemon", out.splitlines()[0] if out else "module accepté", out, code)
    return TestResult(False, "invalid", "daemon", out or "module refusé", out, code)


def test_plugin(folder: Path) -> TestResult:
    finding = read_plugin(folder)
    if not finding.valid:
        return TestResult(False, "invalid", "schema", "; ".join(finding.problems[:6]))
    out, code = _run_daemon(["plugin-test", str(folder)])
    if code is not None:
        if code == 0:
            return TestResult(True, "ok", "daemon", out.splitlines()[0] if out else "plugin accepté", out, code)
        return TestResult(False, "invalid", "daemon", out or "sortie du plugin refusée", out, code)
    result = local_plugin_test(folder, finding.raw)
    if result.ok:
        result.detail = f"{result.detail} ; test du daemon indisponible (à brancher)"
    return result


def local_plugin_test(folder: Path, manifest: dict[str, Any]) -> TestResult:
    """Essai LOCAL (aucun daemon) : un état d'exemple → une ligne JSON validée par schéma."""
    exec_ = [e for e in manifest.get("exec") or [] if isinstance(e, str)]
    if not exec_:
        return TestResult(False, "invalid", "local", "exec vide")
    states = sample_states()
    if not states:
        return TestResult(False, "unavailable", "local", "aucun état d'exemple (docs/ipc/*.sample.json)")
    timeout_ms = manifest.get("timeoutMs")
    timeout = (timeout_ms if isinstance(timeout_ms, int) else 200) / 1000 + 3.0
    schema_out = schema.load("plugin-output")
    env = {k: v for k, v in os.environ.items() if not k.startswith("ULTIMA_")}
    env["PYTHONUNBUFFERED"] = "1"
    for state_path in states:
        try:
            state = json.loads(state_path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        line = json.dumps({"api": 1, "state": state}, ensure_ascii=False) + "\n"
        try:
            proc = subprocess.Popen(exec_, cwd=str(folder), stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                    stderr=subprocess.PIPE, text=True, env=env)
        except OSError as e:
            return TestResult(False, "error", "local", f"lancement impossible : {e}")
        try:
            stdout, stderr = proc.communicate(line, timeout=timeout)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.communicate()
            return TestResult(False, "error", "local", f"délai dépassé ({timeout:.1f} s)")
        first = (stdout or "").strip().splitlines()
        if not first:
            err = (stderr or "").strip().splitlines()[-1:] or ["aucune sortie"]
            return TestResult(False, "error", "local", f"processus sans sortie : {err[0]}", stderr or "")
        try:
            answer = json.loads(first[0])
        except ValueError:
            return TestResult(False, "invalid", "local", f"sortie non JSON : {first[0][:120]}", stdout or "")
        errs = schema.validate(answer, schema_out)
        if errs:
            return TestResult(False, "invalid", "local", "; ".join(errs[:6]), stdout or "")
    return TestResult(True, "ok", "local", f"{len(states)} état(s) d'exemple validés")


def enable_plugin(plugin_id: str) -> tuple[bool, TestResult, str]:
    """Active un plugin SEULEMENT après un test réussi. (activé, résultat du test, message)."""
    folder = paths.plugins_dir() / plugin_id
    if not folder.is_dir():
        return False, TestResult(False, "invalid", "schema", f"plugin introuvable : {folder}"), \
            f"plugin « {plugin_id} » introuvable"
    result = test_plugin(folder)
    if not result.ok:
        return False, result, f"test échoué : {result.detail}"
    appearance.enable_plugin(plugin_id)
    return True, result, f"{plugin_id} activé ({result.mode})"


def disable_plugin(plugin_id: str) -> str:
    appearance.disable_plugin(plugin_id)
    return f"{plugin_id} désactivé"
