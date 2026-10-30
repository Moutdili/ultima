"""Journal du daemon (`~/.local/state/ultima/daemon.log`) : niveaux, filtre, couleurs, suivi, rotation.

Format du daemon (Ultima.Daemon/Diag.cs, contrat IPC v1.3, docs/ipc/README.md §1) :
« AAAA-MM-JJ HH:MM:SS.mmm NIVEAU message », NIVEAU ∈ INFO|WARN|ERROR|DEBUG aligné sur 5 caractères.
L'ancien format « HH:mm:ss.fff niveau message » (minuscules) reste lu. Les autres lignes (exception .NET,
message d'erreur de lancement) sont classées « error » si elles en ont l'air, sinon rattachées au niveau
de la ligne précédente (suite d'une trace). La rotation (> 5 Mio → daemon.log.1) est faite par le daemon.
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
from .consolecfg import LEVELS
from .theme import Resolution
from .palette import Ansi

_LINE = re.compile(r"^(?P<ts>(?:\d{4}-\d\d-\d\d[ T])?\d\d:\d\d:\d\d(?:\.\d+)?) +"
                   r"(?P<level>info|warn|warning|debug|error|fail|crit)\b *(?P<msg>.*)$", re.IGNORECASE)
_ERRORISH = re.compile(r"(unhandled exception|exception|erreur|error|introuvable|n'a pas démarré|tourne déjà|fatal)",
                       re.IGNORECASE)
_LEVEL_ALIASES = {"fail": "error", "crit": "error", "warning": "warn"}
LEVEL_RANK = {lvl: i for i, lvl in enumerate(LEVELS)}


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
        lvl = m.group("level").lower()
        lvl = _LEVEL_ALIASES.get(lvl, lvl)
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


def color_line(ll: LogLine, res: Resolution, ansi: Ansi) -> str:
    """Couleurs du journal depuis le thème (`Resolution.log_style`), jamais depuis un hex en dur."""
    if not ansi.enabled:
        return ll.raw
    if ll.ts is None:
        return ansi.tone(ll.raw, res.log_style(ll.level))
    lvl_style = res.log_style(ll.level, bold=ll.level in ("warn", "error"))
    return (ansi.tone(ll.ts, res.log_style("timestamp")) + " "
            + ansi.tone(f"{ll.level.upper():<5}", lvl_style) + " " + ansi.tone(ll.msg, lvl_style))
