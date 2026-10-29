"""Sonde de vie du daemon (stdlib uniquement, testable sans affichage).

state.json peut survivre au daemon (fichier dans $XDG_RUNTIME_DIR) : sa seule présence ne prouve
pas que le daemon tourne. La socket state.sock, elle, n'accepte une connexion que si le daemon
écoute. On se connecte puis on referme aussitôt (le daemon peut envoyer une ligne : ignorée).
"""

from __future__ import annotations

import socket
import stat
from pathlib import Path

ALIVE = "alive"          # connexion acceptée
NO_SOCKET = "no_socket"  # pas de fichier socket
REFUSED = "refused"      # fichier présent mais personne n'écoute (daemon arrêté / planté)


def probe_socket(path: Path, timeout: float = 0.2) -> str:
    try:
        st = path.stat()
    except OSError:
        return NO_SOCKET
    if not stat.S_ISSOCK(st.st_mode):
        return NO_SOCKET
    s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    s.settimeout(timeout)
    try:
        s.connect(str(path))
        return ALIVE
    except OSError:
        return REFUSED
    finally:
        s.close()
