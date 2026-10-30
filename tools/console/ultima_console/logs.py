"""Journal du daemon (`~/.local/state/ultima/daemon.log`) : niveaux, filtre, couleurs, suivi, rotation.

Format du daemon (Ultima.Daemon/Diag.cs) : « HH:mm:ss.fff <niveau> <message> » avec niveau ∈ info|warn|debug.
Les autres lignes (exception .NET, message d'erreur de lancement) sont classées « error » si elles en
ont l'air, sinon rattachées au niveau de la ligne précédente (suite d'une trace).
"""

from __future__ import annotations

import os
import re
import time
from collections import deque
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator

from . import paths
from .consolecfg import ConsoleConfig, LEVELS
from .palette import Ansi

_LINE = re.compile(r"^(?P<ts>\d\d:\d\d:\d\d(?:\.\d+)?) (?P<level>info|warn|debug|error|fail|crit)\b ?(?P<msg>.*)$")
_ERRORISH = re.compile(r"(unhandled exception|exception|erreur|error|introuvable|n'a pas démarré|tourne déjà|fatal)",
                       re.IGNORECASE)
_LEVEL_ALIASES = {"fail": "error", "crit": "error"}
LEVEL_RANK = {lvl: i for i, lvl in enumerate(LEVELS)}
ROTATE_BYTES = 5 * 1024 * 1024


@dataclass
class LogLine:
    raw: str
    ts: str | None
    level: str
    msg: str


def parse_line(raw: str, prev_level: str = "info") -> LogLine:
    raw = raw.rstrip("\n")
    m = _LINE.match(raw)
    if m:
        lvl = _LEVEL_ALIASES.get(m.group("level"), m.group("level"))
        return LogLine(raw, m.group("ts"), lvl, m.group("msg"))
    if raw.startswith((" ", "\t")) and prev_level == "error":
        return LogLine(raw, None, "error", raw)
    return LogLine(raw, None, "error" if _ERRORISH.search(raw) else prev_level, raw)


def parse_lines(lines: list[str]) -> list[LogLine]:
    out: list[LogLine] = []
    prev = "info"
    for r in lines:
        ll = parse_line(r, prev)
        prev = ll.level
        out.append(ll)
    return out


def accept(ll: LogLine, min_level: str = "debug", needle: str | None = None) -> bool:
    if LEVEL_RANK.get(ll.level, 1) < LEVEL_RANK.get(min_level, 0):
        return False
    return not needle or needle.lower() in ll.raw.lower()


def tail(path: Path | None = None, n: int = 50) -> list[str]:
    path = path or paths.log_path()
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            return list(deque(f, maxlen=n)) if n > 0 else f.readlines()
    except FileNotFoundError:
        return []


class Follower:
    """Lecture incrémentale non bloquante (comme tail -F : reprend après rotation ou troncature).
    `poll()` renvoie les lignes complètes apparues depuis l'appel précédent ; le premier appel part de
    la fin du fichier (from_end) ou du début."""

    def __init__(self, path: Path | None = None, from_end: bool = True):
        self.path = path or paths.log_path()
        self._ino: int | None = None
        self._pos = 0
        self._partial = ""
        self._first = from_end

    def poll(self) -> list[str]:
        try:
            st = os.stat(self.path)
        except FileNotFoundError:
            self._ino, self._pos, self._partial = None, 0, ""
            self._first = False
            return []
        if self._ino != st.st_ino or st.st_size < self._pos:
            self._pos = st.st_size if (self._first and self._ino is None) else 0
            self._ino = st.st_ino
            self._partial = ""
        self._first = False
        if st.st_size == self._pos:
            return []
        with open(self.path, "r", encoding="utf-8", errors="replace") as f:
            f.seek(self._pos)
            chunk = f.read()
            self._pos = f.tell()
        data = self._partial + chunk
        lines = data.split("\n")
        self._partial = lines.pop()
        return [ln + "\n" for ln in lines]


def follow(path: Path | None = None, interval: float = 0.5, stop=None) -> Iterator[str]:
    fl = Follower(path, from_end=True)
    while stop is None or not stop():
        got = fl.poll()
        yield from got
        if not got:
            time.sleep(interval)


def color_line(ll: LogLine, cfg: ConsoleConfig, ansi: Ansi) -> str:
    if not ansi.enabled:
        return ll.raw
    if ll.ts is None:
        return ansi.fg(ll.raw, cfg.color(ll.level))
    lvl_color = cfg.color(ll.level)
    return (ansi.fg(ll.ts, cfg.color("timestamp")) + " "
            + ansi.fg(f"{ll.level:<5}", lvl_color, bold=ll.level in ("warn", "error"))
            + " " + ansi.fg(ll.msg, lvl_color))


def rotate(path: Path | None = None, limit: int = ROTATE_BYTES) -> bool:
    """daemon.log > limit → daemon.log.1 (une seule génération gardée)."""
    path = path or paths.log_path()
    try:
        if path.stat().st_size <= limit:
            return False
    except FileNotFoundError:
        return False
    os.replace(path, path.with_name(path.name + ".1"))
    return True
