"""Environnement isolé pour chaque test : HOME, XDG_* et ULTIMA_* pointent dans tmp_path.

Garde-fou : un test qui résoudrait un chemin d'ULTIMA hors de tmp_path échoue AVANT de s'exécuter
(jamais ~/.config/ultima, ~/.local/state/ultima, $XDG_RUNTIME_DIR/ultima ni ~/.local/bin réels).
gnome-extensions et systemctl sont remplacés par des faux ; aucun vrai daemon n'est contacté.
"""

from __future__ import annotations

import json
import os
import shutil
import socket
import sys
import threading
from pathlib import Path

import pytest

CONSOLE = Path(__file__).resolve().parents[1]
REPO = CONSOLE.parents[1]
FIXTURES = Path(__file__).resolve().parent / "fixtures"
SAMPLE_V13 = REPO / "docs" / "ipc" / "state.v13.sample.json"
sys.path.insert(0, str(CONSOLE))

from ultima_console import paths  # noqa: E402


@pytest.fixture(autouse=True)
def env(tmp_path, monkeypatch):
    home = tmp_path / "home"
    run = tmp_path / "xrun"
    for d in (home, run):
        d.mkdir()
    for var in list(os.environ):
        if var.startswith(("ULTIMA_", "XDG_")) or var in ("NO_COLOR", "FORCE_COLOR", "DOTNET_ROOT"):
            monkeypatch.delenv(var, raising=False)
    monkeypatch.setenv("HOME", str(home))
    monkeypatch.setenv("XDG_CONFIG_HOME", str(home / ".config"))
    monkeypatch.setenv("XDG_DATA_HOME", str(home / ".local" / "share"))
    monkeypatch.setenv("XDG_STATE_HOME", str(home / ".local" / "state"))
    monkeypatch.setenv("XDG_RUNTIME_DIR", str(run))
    monkeypatch.setenv("ULTIMA_NO_SYSTEMCTL", "1")
    monkeypatch.setenv("TERM", "xterm-256color")
    fake = tmp_path / "fake-bin"
    fake.mkdir()
    gx = fake / "gnome-extensions"
    gx.write_text("#!/bin/sh\necho \"$@\" >> \"$(dirname \"$0\")/gnome-extensions.calls\"\n"
                  "if [ \"$1\" = info ]; then printf '%s\\n' 'ultima@mio' '  Enabled: Yes' '  State: ACTIVE'; fi\n")
    gx.chmod(0o755)
    monkeypatch.setenv("ULTIMA_GNOME_EXTENSIONS", str(gx))
    monkeypatch.chdir(tmp_path)
    for p in (paths.config_dir(), paths.settings_path(), paths.log_path(), paths.runtime_dir(), paths.state_path(),
              paths.socket_path(), paths.ultima_home(), paths.bin_dir(), paths.kb_path(), paths.extension_dir(),
              paths.systemd_unit_path(), paths.daemon_capture_path()):
        assert str(p).startswith(str(tmp_path)), f"chemin hors du dossier de test : {p}"
    yield tmp_path
    # Un test qui aurait appelé monkeypatch.undo() aurait rétabli le vrai HOME : on le signale.
    assert os.environ.get("HOME") == str(home), "isolation défaite pendant le test (monkeypatch.undo ?)"


def load_fixture(name: str) -> dict:
    path = SAMPLE_V13 if name == "v13" else FIXTURES / f"state-{name}.json"
    return json.loads(path.read_text(encoding="utf-8"))


def publish(state: dict | str) -> Path:
    """Écrit state.json comme le daemon (dossier IPC 0700)."""
    paths.private_dir(paths.runtime_dir())
    text = state if isinstance(state, str) else json.dumps(state, ensure_ascii=False)
    paths.state_path().write_text(text + "\n", encoding="utf-8")
    return paths.state_path()


class FakeSocket:
    """Faux state.sock : accepte les connexions et envoie `lines` (une ligne JSON par état)."""

    def __init__(self, path: Path, lines: list[str] | None = None):
        self.path = path
        self.lines = lines or []
        path.parent.mkdir(parents=True, exist_ok=True)
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.bind(str(path))
        self.sock.listen(8)
        self.sock.settimeout(0.2)
        self.stop = threading.Event()
        self.clients: list[socket.socket] = []
        self.thread = threading.Thread(target=self._serve, daemon=True)
        self.thread.start()

    def _serve(self):
        while not self.stop.is_set():
            try:
                c, _ = self.sock.accept()
            except (socket.timeout, OSError):
                continue
            self.clients.append(c)
            for ln in self.lines:
                try:
                    c.sendall(ln.encode() + b"\n")
                except OSError:
                    pass

    def close(self, keep_file: bool = False):
        """Arrête le fil d'accept AVANT de fermer (sinon la socket reste à l'écoute jusqu'au délai)."""
        self.stop.set()
        self.thread.join(2)
        for c in self.clients:
            c.close()
        self.sock.close()
        if not keep_file:
            self.path.unlink(missing_ok=True)


@pytest.fixture
def fake_socket():
    made: list[FakeSocket] = []

    def make(lines: list[str] | None = None) -> FakeSocket:
        s = FakeSocket(paths.socket_path(), lines)
        made.append(s)
        return s

    yield make
    for s in made:
        s.close()


@pytest.fixture
def which_python():
    return shutil.which("python3") or sys.executable
