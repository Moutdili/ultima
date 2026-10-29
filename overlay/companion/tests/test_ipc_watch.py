import json
import os
import socket
import time

from gi.repository import GLib

from ultima_companion import ipc, paths, state, watch


def test_probe_socket(tmp_path):
    p = tmp_path / "s.sock"
    assert ipc.probe_socket(p) == ipc.NO_SOCKET
    p.write_text("pas une socket")
    assert ipc.probe_socket(p) == ipc.NO_SOCKET
    p.unlink()
    srv = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    srv.bind(str(p))
    srv.listen(1)
    try:
        assert ipc.probe_socket(p) == ipc.ALIVE
    finally:
        srv.close()
    # fichier socket resté après la mort du daemon
    assert p.exists() and ipc.probe_socket(p) == ipc.REFUSED


def _atomic_write(p, data):
    tmp = p.with_name(p.name + ".tmp")
    tmp.write_text(json.dumps(data), encoding="utf-8")
    os.replace(tmp, p)


def _pump_until(cond, timeout=5.0):
    ctx = GLib.MainContext.default()
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        while ctx.pending():
            ctx.iteration(False)
        if cond():
            return True
        time.sleep(0.02)
    return False


def test_watcher_sees_atomic_rename_and_deletion(isolated_xdg, sample):
    calls = []
    sp = paths.state_path()
    sp.parent.mkdir()
    w = watch.StateWatcher(lambda r, d: calls.append((r.kind, d)))
    w.start()
    try:
        assert calls[-1] == (state.MISSING, ipc.NO_SOCKET)
        _atomic_write(sp, sample)
        assert _pump_until(lambda: calls[-1][0] == state.OK), calls
        data = dict(sample, screen="Map")
        _atomic_write(sp, data)
        assert _pump_until(lambda: w.last.state is not None and w.last.state.screen == "Map")
        sp.unlink()
        assert _pump_until(lambda: calls[-1][0] == state.MISSING), calls
    finally:
        w.stop()


def test_watcher_dir_created_later_picked_by_poll(isolated_xdg, sample):
    calls = []
    w = watch.StateWatcher(lambda r, d: calls.append(r.kind))
    w.start()
    try:
        assert calls == [state.MISSING]
        sp = paths.state_path()
        sp.parent.mkdir()
        _atomic_write(sp, sample)
        w._on_poll()  # tick du filet de sécurité (sans attendre POLL_SECONDS)
        assert calls[-1] == state.OK
        assert w._monitor is not None  # moniteur armé dès que le dossier existe
    finally:
        w.stop()


def test_watcher_no_duplicate_updates(isolated_xdg, sample):
    calls = []
    sp = paths.state_path()
    sp.parent.mkdir()
    _atomic_write(sp, sample)
    w = watch.StateWatcher(lambda r, d: calls.append(r.kind))
    w.start()
    try:
        n = len(calls)
        w._on_poll()
        w._on_poll()
        assert len(calls) == n  # rien n'a changé → aucun rafraîchissement inutile
    finally:
        w.stop()
