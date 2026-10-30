"""start / kill / restart avec un FAUX daemon (jamais le vrai) : dossier IPC, journal et PID temporaires.

Le faux : un lien `ultima-daemon` → python3 qui exécute le script `run` du dossier courant, si bien que
/proc/PID/cmdline vaut « …/ultima-daemon run --no-mirror --log … » comme un vrai daemon.
"""

import os
import shutil
import signal
import subprocess
import time

import pytest
from conftest import publish, load_fixture

from ultima_console import cli, consolecfg, paths, procs
from ultima_console.state import ALIVE, probe_socket

FAKE = r'''
import os, signal, socket, sys, time
args = sys.argv[1:]
rt = os.path.join(os.environ["XDG_RUNTIME_DIR"], "ultima"); os.makedirs(rt, exist_ok=True)
log = args[args.index("--log") + 1]
if os.environ.get("FAKE_FAIL"):
    print("runtime .NET introuvable", file=sys.stderr, flush=True); sys.exit(3)
sock = os.path.join(rt, "state.sock")
if os.path.exists(sock): os.unlink(sock)
with open(os.path.join(rt, "daemon.lock"), "w") as f: f.write(str(os.getpid()))
s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM); s.bind(sock); s.listen(4); s.settimeout(0.1)
with open(log, "a") as f: f.write("2026-10-30 12:00:00.000 INFO  faux daemon " + " ".join(args) + "\n")
print("2026-10-30 12:00:00.000 INFO  copie stderr", file=sys.stderr, flush=True)
def term(*_):
    os.unlink(sock); sys.exit(0)
signal.signal(signal.SIGTERM, signal.SIG_IGN if os.environ.get("FAKE_STUBBORN") else term)
while True:
    try:
        c, _ = s.accept(); c.close()
    except socket.timeout:
        pass
'''


@pytest.fixture
def fake_daemon(env, monkeypatch):
    bindir = env / "fakebin-daemon"
    bindir.mkdir()
    exe = bindir / "ultima-daemon"
    exe.symlink_to(os.path.realpath(shutil.which("python3")))
    (env / "run").write_text(FAKE)
    monkeypatch.setenv("ULTIMA_DAEMON", str(exe))
    started: list[int] = []
    yield started
    for pid in procs.owned_pids() + started:
        try:
            os.kill(pid, signal.SIGKILL)
        except ProcessLookupError:
            pass


def test_is_daemon_argv():
    assert procs.is_daemon_argv(["/x/ultima-daemon", "run", "--no-mirror"])
    assert procs.is_daemon_argv(["dotnet", "/x/ultima-daemon.dll", "run"])
    assert not procs.is_daemon_argv(["/x/ultima-daemon", "dump"])
    assert not procs.is_daemon_argv(["python3", "run"])
    assert not procs.is_daemon_argv([])


def test_daemon_argv_adds_log_once():
    cfg = consolecfg.ConsoleConfig()
    argv = procs.daemon_argv(paths.ultima_home() / "d", cfg, [])
    assert argv[1:] == ["run", "--no-mirror", "--log", str(paths.log_path())]
    argv = procs.daemon_argv(paths.ultima_home() / "d", cfg, ["--log=/x"])
    assert "--log" not in argv and argv[-1] == "--log=/x"


def test_start_status_kill(fake_daemon, capsys):
    r = procs.start(consolecfg.ConsoleConfig(), wait_s=5)
    assert r.ok and r.pids, r.message
    fake_daemon.extend(r.pids)
    pid = r.pids[0]
    assert probe_socket() == ALIVE and procs.owned_pids() == [pid]
    assert paths.pid_path().read_text().strip() == str(pid)
    log = paths.log_path().read_text()
    assert f"faux daemon --no-mirror --log {paths.log_path()}" in log
    assert "copie stderr" not in log                                   # stderr → fichier de capture à part
    assert "copie stderr" in paths.daemon_capture_path().read_text()
    assert procs.start(consolecfg.ConsoleConfig()).message == "ULTIMA tourne déjà"
    publish(load_fixture("map"))
    assert cli.main(["status"]) == 0
    assert "daemon ✓" in capsys.readouterr().out
    r = procs.stop(grace_s=3)
    assert r.pids == [pid] and "arrêté" in r.message
    assert not procs._alive(pid) and probe_socket() != ALIVE and not paths.pid_path().exists()
    assert procs.stop().message == "ULTIMA ne tourne pas"
    assert cli.main(["status"]) == 3


def test_cli_start_restart_kill(fake_daemon, capsys):
    assert cli.main(["start", "--color", "never"]) == 0
    first = procs.owned_pids()
    fake_daemon.extend(first)
    assert cli.main(["restart"]) == 0
    second = procs.owned_pids()
    fake_daemon.extend(second)
    assert second and second != first and not procs._alive(first[0])
    assert cli.main(["kill"]) == 0
    out = capsys.readouterr().out
    assert "ULTIMA lancé" in out and "ULTIMA arrêté" in out
    assert procs.owned_pids() == []


def test_start_failure_is_reported(fake_daemon, monkeypatch):
    monkeypatch.setenv("FAKE_FAIL", "1")
    r = procs.start(consolecfg.ConsoleConfig(), wait_s=5)
    assert not r.ok and "code 3" in r.message and "runtime .NET introuvable" in r.message
    assert not paths.pid_path().exists()


def test_stubborn_daemon_gets_sigkill(fake_daemon, monkeypatch):
    monkeypatch.setenv("FAKE_STUBBORN", "1")
    r = procs.start(consolecfg.ConsoleConfig(), wait_s=5)
    fake_daemon.extend(r.pids)
    t = time.monotonic()
    r2 = procs.stop(grace_s=0.5)
    assert "SIGKILL" in r2.message and time.monotonic() - t < 3 and not procs._alive(r.pids[0])


def test_stop_never_signals_a_foreign_process(env):
    other = subprocess.Popen(["sleep", "30"])
    try:
        paths.private_dir(paths.runtime_dir())
        paths.lock_path().write_text(str(other.pid))          # PID étranger dans le verrou
        assert procs.owned_pids() == []
        assert procs.stop_pids([other.pid]).pids == []
        assert other.poll() is None                           # toujours vivant
    finally:
        other.kill()
        other.wait()


def test_no_daemon_binary(capsys):
    assert cli.main(["start"]) == 1
    assert "binaire du daemon introuvable" in capsys.readouterr().err
    assert cli.main(["dump"]) == 1
