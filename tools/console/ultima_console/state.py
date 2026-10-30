"""Lecture de l'état publié par le daemon (contrat v1.x) : `state.json` + sonde de `state.sock`.

Règles client (docs/ipc/README.md §4) : ignorer les champs inconnus ; refuser une version MAJEURE
supérieure en l'annonçant ; afficher `health.degraded` en tête ; `null` → « — ».
"""

from __future__ import annotations

import json
import os
import socket
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from . import CONTRACT_MAJOR, paths

OK, MISSING, INVALID, TOO_NEW = "ok", "missing", "invalid", "too_new"
ALIVE, REFUSED, ABSENT = "alive", "refused", "absent"


@dataclass
class StateResult:
    kind: str
    state: dict[str, Any] | None = None
    error: str | None = None
    path: Path | None = None
    mtime: float | None = None

    @property
    def age_s(self) -> float | None:
        return None if self.mtime is None else max(0.0, time.time() - self.mtime)


def parse(text: str, path: Path | None = None, mtime: float | None = None) -> StateResult:
    try:
        data = json.loads(text)
    except (ValueError, RecursionError) as e:
        return StateResult(INVALID, error=f"JSON invalide : {e}", path=path, mtime=mtime)
    if not isinstance(data, dict):
        return StateResult(INVALID, error="objet JSON attendu", path=path, mtime=mtime)
    v = data.get("contractVersion")
    if not isinstance(v, int) or isinstance(v, bool):
        return StateResult(INVALID, error="contractVersion absent", path=path, mtime=mtime)
    if v > CONTRACT_MAJOR:
        return StateResult(TOO_NEW, state=data, error=f"contrat v{v} — mettre à jour ULTIMA",
                           path=path, mtime=mtime)
    return StateResult(OK, state=data, path=path, mtime=mtime)


def load(path: Path | None = None) -> StateResult:
    path = path or paths.state_path()
    try:
        st = path.stat()
        text = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return StateResult(MISSING, path=path)
    except OSError as e:
        return StateResult(INVALID, error=str(e), path=path)
    return parse(text, path, st.st_mtime)


def probe_socket(path: Path | None = None, timeout: float = 0.3) -> str:
    """alive : connexion acceptée ; refused : fichier présent, personne n'écoute ; absent."""
    path = path or paths.socket_path()
    if not os.path.exists(path):
        return ABSENT
    s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    s.settimeout(timeout)
    try:
        s.connect(str(path))
        return ALIVE
    except OSError:
        return REFUSED
    finally:
        s.close()


# ---------- accès tolérants ----------

def get(d: Any, *keys: str, default: Any = None) -> Any:
    cur = d
    for k in keys:
        if not isinstance(cur, dict):
            return default
        cur = cur.get(k)
    return default if cur is None else cur


def arr(v: Any) -> list:
    return v if isinstance(v, list) else []


def num(v: Any) -> int | None:
    if isinstance(v, bool) or not isinstance(v, (int, float)):
        return None
    return int(round(v))
