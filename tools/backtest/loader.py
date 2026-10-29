"""Lecture en streaming de l'index compact de runs (`data/index/runs-*.jsonl.gz`).

L'index est produit par `tools/index_runs.py` (une ligne = un run JSON compact) :

    {"h": hash, "b": build, "m": mode, "asc": ascension, "n": joueurs, "w": 0|1,
     "ab": abandon, "kb": tué par, "rt": ?, "pl": [...], "d": [...]}

    pl[i] = {"id", "c", "hp", "mhp", "deck", "rel", "pot", "pslot"}
    d[j]  = {"t": "card"|"relic"|"ancient", "a": acte, "f": étage, "rt": type de
             salle, "p": player_id, "hp", "mhp", "g": or,
             "o": [cartes proposées], "k": carte prise (null = skip),
             "ro": [reliques proposées], "rk": relique prise,
             "an": [choix d'ancien]}

Aucune dépendance hors bibliothèque standard. Le fichier peut être gzippé ou
non : la détection se fait sur les octets magiques, pas sur l'extension.
"""

from __future__ import annotations

import gzip
import json
import os
from dataclasses import dataclass, field
from typing import Iterable, Iterator

__all__ = [
    "CANONICAL_CHARACTERS",
    "Decision",
    "Player",
    "Run",
    "RunFilter",
    "canonical_character",
    "iter_raw",
    "iter_runs",
    "open_index",
    "read_index_head",
]

#: Les cinq personnages du jeu de base, tels qu'employés comme clés dans
#: `data/meta/card_tiers.json` (`characters.<perso>`).
CANONICAL_CHARACTERS: tuple[str, ...] = (
    "ironclad",
    "silent",
    "defect",
    "necrobinder",
    "regent",
)

#: Valeurs de `m` observées dans l'export v0.107.1.
MODES: tuple[str, ...] = ("standard", "daily", "custom")


# --------------------------------------------------------------------------- #
# Normalisation
# --------------------------------------------------------------------------- #
def canonical_character(raw: object) -> str | None:
    """`CHARACTER.SILENT` → `"silent"` ; renvoie None pour les mods/inconnus.

    Les exports contiennent des centaines d'identifiants de personnages moddés
    (`CHARACTER.WATCHER-WATCHER`, `CHARACTER.MANOSABA-...`). On ne les rejette
    pas silencieusement plus loin : `canonical_only` les écarte en le comptant.
    """
    if not isinstance(raw, str) or not raw:
        return None
    text = raw.strip().lower()
    if text.startswith("character."):
        text = text[len("character.") :]
    # `CHARACTER.WATCHER-WATCHER` : la variante après le tiret n'est pas un
    # personnage canonique, le préfixe non plus.
    if "." in text:
        text = text.split(".", 1)[0]
    return text if text in CANONICAL_CHARACTERS else None


def _int(value: object) -> int | None:
    if value is None or isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _str_tuple(value: object) -> tuple[str, ...]:
    if not isinstance(value, (list, tuple)):
        return ()
    return tuple(item for item in value if isinstance(item, str) and item)


# --------------------------------------------------------------------------- #
# Modèle
# --------------------------------------------------------------------------- #
@dataclass(frozen=True, slots=True)
class Player:
    """Un joueur d'un run (1 en solo, jusqu'à 4 en co-op)."""

    id: str
    character: str | None  # canonique : "silent", "ironclad", ... sinon None
    raw_character: str
    hp: int | None
    max_hp: int | None
    deck: tuple[str, ...]
    relics: tuple[str, ...]
    potions: tuple[str, ...]
    potion_slots: int | None

    @property
    def is_canonical(self) -> bool:
        return self.character is not None

    def hp_fraction(self) -> float | None:
        if self.hp is None or not self.max_hp:
            return None
        return self.hp / self.max_hp


@dataclass(frozen=True, slots=True)
class Decision:
    """Une décision ponctuelle (récompense de combat, boutique, ancien…)."""

    type: str  # "card" | "relic" | "ancient"
    act: int | None
    floor: int | None
    room_type: str | None
    player_id: str | None
    hp: int | None
    max_hp: int | None
    gold: int | None
    offered: tuple[str, ...]
    picked: str | None
    relics_offered: tuple[str, ...] = ()
    relic_picked: str | None = None
    ancients: tuple[str, ...] = ()

    @property
    def is_card(self) -> bool:
        return self.type == "card"

    @property
    def is_relic(self) -> bool:
        return self.type == "relic"

    @property
    def skipped(self) -> bool:
        """Vrai si le joueur a refusé explicitement (skip), faux si abstention."""
        return self.picked is None

    @property
    def hp_fraction(self) -> float | None:
        if self.hp is None or not self.max_hp:
            return None
        return self.hp / self.max_hp

    def picked_in_offer(self) -> bool:
        return self.picked is not None and self.picked in self.offered


@dataclass(frozen=True, slots=True)
class Run:
    """Un run complet, avec ses décisions déjà désérialisées."""

    hash: str
    build: str
    mode: str
    ascension: int | None
    n_players: int
    win: bool
    abandoned: bool
    killed_by: str | None
    players: tuple[Player, ...]
    decisions: tuple[Decision, ...]
    # Index interne (non comparé, non affiché) : évite un scan linéaire par décision.
    players_by_id: dict[str, Player] = field(
        repr=False, compare=False, default_factory=dict
    )

    @property
    def solo(self) -> bool:
        return self.n_players <= 1

    @property
    def coop(self) -> bool:
        return self.n_players > 1

    @property
    def character(self) -> str | None:
        """Personnage du joueur 1 (`CHARACTER.*` normalisé), None si moddé."""
        return self.players[0].character if self.players else None

    def player(self, player_id: object) -> Player | None:
        if player_id is None:
            return None
        return self.players_by_id.get(str(player_id))

    def card_decisions(self) -> Iterator[Decision]:
        for decision in self.decisions:
            if decision.type == "card":
                yield decision


# --------------------------------------------------------------------------- #
# Filtres
# --------------------------------------------------------------------------- #
def _as_set(values: Iterable[int] | None) -> frozenset[int] | None:
    if values is None:
        return None
    return frozenset(int(v) for v in values)


@dataclass(frozen=True, slots=True)
class RunFilter:
    """Sélection des runs, appliquée avant toute construction d'objet.

    - `mode` : "standard" (défaut), "daily", "custom" ou None (tous).
    - `ascensions` : ensemble explicite ; sinon bornes `min_ascension`/`max_ascension`.
    - `players` : "solo" (n == 1), "coop" (n > 1) ou None.
    - `build` : correspondance exacte sur `b` (ex. "v0.107.1") ou None.
    - `canonical_only` : n'accepte que les runs dont **tous** les joueurs sont
      des personnages canoniques (les mods cassent les tables de méta).
    - `require_decisions` : ignore les runs sans aucune décision (exports tronqués).
    """

    mode: str | None = "standard"
    ascensions: frozenset[int] | None = None
    min_ascension: int | None = None
    max_ascension: int | None = None
    players: str | None = None
    build: str | None = None
    canonical_only: bool = True
    require_decisions: bool = False

    def __post_init__(self) -> None:
        if self.mode is not None:
            mode = self.mode.strip().lower()
            if mode in ("", "all", "*"):
                object.__setattr__(self, "mode", None)
            elif mode not in MODES:
                raise ValueError(
                    f"mode inconnu : {self.mode!r} (attendu : {', '.join(MODES)} ou all)"
                )
            else:
                object.__setattr__(self, "mode", mode)
        if self.players is not None:
            players = self.players.strip().lower()
            aliases = {"1": "solo", "single": "solo", "multi": "coop", "co-op": "coop"}
            players = aliases.get(players, players)
            if players not in ("solo", "coop", "all"):
                raise ValueError(
                    f"players inconnu : {self.players!r} (attendu : solo, coop, all)"
                )
            object.__setattr__(self, "players", None if players == "all" else players)
        if self.ascensions is not None and not isinstance(self.ascensions, frozenset):
            object.__setattr__(self, "ascensions", _as_set(self.ascensions))

    def accepts_head(self, head: dict) -> bool:
        """Filtre bon marché sur les champs d'en-tête (avant les joueurs)."""
        if self.build is not None and head.get("b") != self.build:
            return False
        if self.mode is not None and head.get("m") != self.mode:
            return False
        ascension = head.get("asc")
        if self.ascensions is not None:
            if ascension not in self.ascensions:
                return False
        else:
            if self.min_ascension is not None and (ascension is None or ascension < self.min_ascension):
                return False
            if self.max_ascension is not None and (ascension is None or ascension > self.max_ascension):
                return False
        n_players = _int(head.get("n")) or 1
        if self.players == "solo" and n_players != 1:
            return False
        if self.players == "coop" and n_players <= 1:
            return False
        if self.require_decisions and not head.get("d"):
            return False
        return True

    def describe(self) -> dict:
        return {
            "mode": self.mode,
            "ascensions": sorted(self.ascensions) if self.ascensions is not None else None,
            "min_ascension": self.min_ascension,
            "max_ascension": self.max_ascension,
            "players": self.players,
            "build": self.build,
            "canonical_only": self.canonical_only,
            "require_decisions": self.require_decisions,
        }


# --------------------------------------------------------------------------- #
# Ouverture / itération
# --------------------------------------------------------------------------- #
def open_index(path: str):
    """Ouvre l'index en texte, gzip ou non (détection sur les octets magiques)."""
    with open(path, "rb") as probe:
        magic = probe.read(2)
    if magic == b"\x1f\x8b":
        return gzip.open(path, "rt", encoding="utf-8")
    return open(path, "rt", encoding="utf-8")


def iter_raw(path: str) -> Iterator[dict]:
    """Itère les runs bruts (dict JSON) sans construire d'objets."""
    with open_index(path) as stream:
        for line in stream:
            line = line.strip()
            if not line:
                continue
            yield json.loads(line)


def _build_player(raw: dict) -> Player:
    return Player(
        id=str(raw.get("id", "")),
        character=canonical_character(raw.get("c")),
        raw_character=str(raw.get("c") or ""),
        hp=_int(raw.get("hp")),
        max_hp=_int(raw.get("mhp")),
        deck=_str_tuple(raw.get("deck")),
        relics=_str_tuple(raw.get("rel")),
        potions=_str_tuple(raw.get("pot")),
        potion_slots=_int(raw.get("pslot")),
    )


def _build_decision(raw: dict, kinds: tuple[str, ...] | None) -> Decision | None:
    kind = raw.get("t")
    if not isinstance(kind, str):
        return None
    if kinds is not None and kind not in kinds:
        return None
    picked = raw.get("k") if kind == "card" else raw.get("rk")
    return Decision(
        type=kind,
        act=_int(raw.get("a")),
        floor=_int(raw.get("f")),
        room_type=raw.get("rt") if isinstance(raw.get("rt"), str) else None,
        player_id=None if raw.get("p") is None else str(raw.get("p")),
        hp=_int(raw.get("hp")),
        max_hp=_int(raw.get("mhp")),
        gold=_int(raw.get("g")),
        offered=_str_tuple(raw.get("o")),
        picked=picked if isinstance(picked, str) else None,
        relics_offered=_str_tuple(raw.get("ro")),
        relic_picked=raw.get("rk") if isinstance(raw.get("rk"), str) else None,
        ancients=_str_tuple(raw.get("an")),
    )


def _build_run(head: dict, kinds: tuple[str, ...] | None) -> Run:
    players = tuple(_build_player(p) for p in head.get("pl") or () if isinstance(p, dict))
    decisions = []
    for raw in head.get("d") or ():
        if not isinstance(raw, dict):
            continue
        decision = _build_decision(raw, kinds)
        if decision is not None:
            decisions.append(decision)
    return Run(
        hash=str(head.get("h", "")),
        build=str(head.get("b", "")),
        mode=str(head.get("m", "")),
        ascension=_int(head.get("asc")),
        n_players=_int(head.get("n")) or len(players) or 1,
        win=bool(head.get("w")),
        abandoned=bool(head.get("ab")),
        killed_by=head.get("kb") if isinstance(head.get("kb"), str) else None,
        players=players,
        decisions=tuple(decisions),
        players_by_id={p.id: p for p in players},
    )


def iter_runs(
    path: str,
    run_filter: RunFilter | None = None,
    limit: int | None = None,
    kinds: tuple[str, ...] | None = None,
    stats: dict | None = None,
    strict: bool = False,
) -> Iterator[Run]:
    """Itère les runs acceptés par `run_filter`, en streaming.

    `limit` borne le nombre de runs **retenus** (pas seulement lus) : la lecture
    s'arrête dès que la limite est atteinte, sans décompresser la suite.

    `stats` (dict fourni par l'appelant) est rempli au passage : `scanned`,
    `used`, `rejected`, `malformed`, `no_players`, `modded_players`. Rien n'est
    écarté en silence — c'est ce qui permet au rapport de dire sur quoi il porte.
    """
    if stats is None:
        stats = {}
    stats.setdefault("scanned", 0)
    stats.setdefault("used", 0)
    stats.setdefault("rejected", 0)
    stats.setdefault("malformed", 0)
    stats.setdefault("no_players", 0)
    stats.setdefault("modded_players", 0)
    stats.setdefault("bytes", 0)

    with open_index(path) as stream:
        for line in stream:
            text = line.strip()
            if not text:
                continue
            stats["scanned"] += 1
            stats["bytes"] += len(text) + 1
            try:
                head = json.loads(text)
            except ValueError:
                stats["malformed"] += 1
                if strict:
                    raise
                continue
            if not isinstance(head, dict):
                stats["malformed"] += 1
                if strict:
                    raise ValueError("ligne d'index : objet JSON attendu")
                continue
            if run_filter is not None and not run_filter.accepts_head(head):
                stats["rejected"] += 1
                continue
            if run_filter is not None and run_filter.canonical_only:
                raw_players = head.get("pl") or ()
                if any(
                    canonical_character(p.get("c")) is None
                    for p in raw_players
                    if isinstance(p, dict)
                ):
                    stats["modded_players"] += 1
                    stats["rejected"] += 1
                    continue
            run = _build_run(head, kinds)
            if not run.players:
                stats["no_players"] += 1
                stats["rejected"] += 1
                continue
            stats["used"] += 1
            yield run
            if limit is not None and stats["used"] >= limit:
                return


def read_index_head(path: str, runs: int = 1) -> list[dict]:
    """Renvoie les `runs` premiers enregistrements bruts (inspection/tests)."""
    out = []
    for raw in iter_raw(path):
        out.append(raw)
        if len(out) >= runs:
            break
    return out


def index_path(root: str, build: str = "v0.107.1") -> str:
    """Chemin conventionnel de l'index dans un dépôt donné."""
    return os.path.join(root, "data", "index", f"runs-{build}.jsonl.gz")
