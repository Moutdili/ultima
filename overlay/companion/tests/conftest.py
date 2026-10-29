"""Garde-fous des tests : aucun affichage, aucun fichier réel de Mio.

- AVANT tout import GTK : DISPLAY retiré et WAYLAND_DISPLAY pointé vers un chemin inexistant.
  (Retirer WAYLAND_DISPLAY ne suffit pas : GTK 4 retente alors $XDG_RUNTIME_DIR/wayland-0.)
  Aucun test ne peut donc se connecter au bureau de Mio : pas d'écran, aucune fenêtre possible.
- XDG_CONFIG_HOME et XDG_RUNTIME_DIR pointent vers des dossiers temporaires pour CHAQUE test.
- En fin de session, on vérifie que le vrai ~/.config/ultima/settings.json n'a pas bougé.
"""

import json
import os
import sys
from pathlib import Path

import pytest

NO_DISPLAY = "/nonexistent/ultima-tests-no-display"
os.environ.pop("DISPLAY", None)
os.environ["WAYLAND_DISPLAY"] = NO_DISPLAY

HERE = Path(__file__).resolve().parent
COMPANION = HERE.parent
REPO = COMPANION.parent.parent
sys.path.insert(0, str(COMPANION))

REAL_SETTINGS = Path.home() / ".config" / "ultima" / "settings.json"


def _sig(p: Path):
    try:
        st = p.stat()
        return (st.st_mtime_ns, st.st_size)
    except FileNotFoundError:
        return None


_REAL_BEFORE = _sig(REAL_SETTINGS)


@pytest.fixture(autouse=True)
def isolated_xdg(tmp_path, monkeypatch):
    cfg = tmp_path / "config"
    run = tmp_path / "run"
    cfg.mkdir()
    run.mkdir(mode=0o700)
    monkeypatch.setenv("XDG_CONFIG_HOME", str(cfg))
    monkeypatch.setenv("XDG_RUNTIME_DIR", str(run))
    yield {"config": cfg, "runtime": run}


@pytest.fixture
def sample_path() -> Path:
    return REPO / "docs" / "22-state.sample.json"


@pytest.fixture
def sample(sample_path) -> dict:
    return json.loads(sample_path.read_text(encoding="utf-8"))


def pytest_sessionfinish(session, exitstatus):
    after = _sig(REAL_SETTINGS)
    if after != _REAL_BEFORE:
        raise RuntimeError(f"Le vrai {REAL_SETTINGS} a été modifié pendant les tests !")
