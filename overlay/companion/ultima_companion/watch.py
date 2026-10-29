"""Surveillance de state.json (Gio.FileMonitor) + sonde de vie du daemon.

- Gio.FileMonitor sur le chemin du fichier : le daemon remplace state.json par rename (tmp → final),
  ce qui se traduit par CREATED / CHANGED / RENAMED / MOVED_IN : tout événement déclenche une relecture.
- Filet de sécurité : relecture périodique bon marché (stat) toutes les POLL_SECONDS, pour couvrir le
  cas où $XDG_RUNTIME_DIR/ultima n'existe pas encore au démarrage (inotify ne peut pas surveiller un
  dossier absent). On ne relit le contenu que si (mtime, taille, inode) a changé.
- La sonde de socket (ipc.probe_socket) est rejouée au même rythme : daemon arrêté → bandeau.

N'importe que Gio/GLib : aucune fenêtre, aucun affichage requis.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import Callable

import gi

gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib  # noqa: E402

from . import ipc, paths, state  # noqa: E402

POLL_SECONDS = 2
PROBE_EVERY_N_POLLS = 2  # sonde socket toutes les ~4 s


def _signature(path: Path):
    try:
        st = path.stat()
    except OSError:
        return None
    return (st.st_ino, st.st_mtime_ns, st.st_size)


class StateWatcher:
    """Appelle `on_update(result: state.StateResult, daemon: str)` à chaque changement observable."""

    def __init__(self, on_update: Callable[[state.StateResult, str], None],
                 state_file: Path | None = None, socket_file: Path | None = None):
        self._on_update = on_update
        self._state_file = state_file or paths.state_path()
        self._socket_file = socket_file or paths.socket_path()
        self._monitor: Gio.FileMonitor | None = None
        self._timer: int | None = None
        self._sig = object()  # forcer la première lecture
        self._daemon = ipc.NO_SOCKET
        self._polls = 0
        self.last: state.StateResult | None = None

    @property
    def daemon(self) -> str:
        return self._daemon

    # ---- cycle de vie ---------------------------------------------------------------------
    def start(self) -> None:
        self._arm_monitor()
        self.refresh(force=True)
        self._timer = GLib.timeout_add_seconds(POLL_SECONDS, self._on_poll)

    def stop(self) -> None:
        if self._monitor is not None:
            self._monitor.cancel()
            self._monitor = None
        if self._timer is not None:
            GLib.source_remove(self._timer)
            self._timer = None

    # ---- interne ----------------------------------------------------------------------------
    def _arm_monitor(self) -> None:
        if self._monitor is not None:
            return
        if not self._state_file.parent.is_dir():
            return  # réarmé au prochain poll quand le daemon aura créé le dossier
        try:
            gfile = Gio.File.new_for_path(str(self._state_file))
            mon = gfile.monitor_file(Gio.FileMonitorFlags.WATCH_MOVES, None)
        except GLib.Error:
            return
        mon.connect("changed", self._on_changed)
        self._monitor = mon

    def _on_changed(self, _mon, _file, _other, _event) -> None:
        # Pas de sonde socket ici : le daemon peut publier souvent (combat), on ne le sollicite pas.
        self.refresh(probe=False)

    def _on_poll(self) -> bool:
        self._arm_monitor()
        self._polls += 1
        probe = self._polls % PROBE_EVERY_N_POLLS == 0
        self.refresh(probe=probe)
        return GLib.SOURCE_CONTINUE

    def refresh(self, force: bool = False, probe: bool = True) -> None:
        daemon = ipc.probe_socket(self._socket_file) if (probe or force) else self._daemon
        sig = _signature(self._state_file)
        if not force and sig == self._sig and daemon == self._daemon:
            return
        changed_file = force or sig != self._sig
        self._sig = sig
        self._daemon = daemon
        if changed_file or self.last is None:
            self.last = state.load(self._state_file)
        self._on_update(self.last, daemon)


def debug_env() -> str:
    """Résumé des chemins utilisés (affiché par `ultima-companion --paths`)."""
    return "\n".join([
        f"state.json    : {paths.state_path()}",
        f"state.sock    : {paths.socket_path()}",
        f"settings.json : {paths.settings_path()}",
        f"XDG_RUNTIME_DIR={os.environ.get('XDG_RUNTIME_DIR', '')}",
    ])
