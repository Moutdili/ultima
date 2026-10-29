"""Lecture tolérante de `EngineState` (contrat v1, camelCase, enums en chaîne, null explicite).

Règles du contrat (skill sts2-engine-contract) :
- champs inconnus ignorés ;
- `contractVersion` supérieure à la version supportée → refus annoncé (« mettre à jour ULTIMA ») ;
- `score == null` → l'UI affiche « — » (jamais 0) ;
- `health.degraded != null` → affiché en tête.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

from . import paths
from .settings import OverlaySettings

SUPPORTED_CONTRACT_VERSION = 1

MISSING = "missing"    # pas de state.json → daemon non démarré
INVALID = "invalid"    # fichier présent mais illisible / non conforme
TOO_NEW = "too_new"    # contractVersion > SUPPORTED → mettre à jour ULTIMA
OK = "ok"


# ---- utilitaires tolérants -------------------------------------------------------------------

def _str(d: dict, key: str, default: str | None = None) -> str | None:
    v = d.get(key)
    return v if isinstance(v, str) else default


def _int(d: dict, key: str, default: int | None = None) -> int | None:
    v = d.get(key)
    if isinstance(v, bool):
        return default
    if isinstance(v, int):
        return v
    if isinstance(v, float) and v.is_integer():
        return int(v)
    return default


def _float(d: dict, key: str, default: float | None = None) -> float | None:
    v = d.get(key)
    if isinstance(v, bool):
        return default
    if isinstance(v, (int, float)):
        return float(v)
    return default


def _bool(d: dict, key: str, default: bool = False) -> bool:
    v = d.get(key)
    return v if isinstance(v, bool) else default


def _obj(d: dict, key: str) -> dict | None:
    v = d.get(key)
    return v if isinstance(v, dict) else None


def _list(d: dict, key: str) -> list:
    v = d.get(key)
    return v if isinstance(v, list) else []


# ---- vues ------------------------------------------------------------------------------------

@dataclass
class ScoreTerm:
    label: str
    points: int
    source: str


# Notation S à F (décision Mio) — miroir de `Grades.Scale` / `Grades.FromTotal` du contrat.
GRADE_SCALE: tuple[tuple[str, int], ...] = (("S", 85), ("A", 70), ("B", 55), ("C", 40), ("D", 25), ("F", 0))
GRADES = frozenset(g for g, _ in GRADE_SCALE)


def grade_from_total(total: int) -> str:
    for grade, minimum in GRADE_SCALE:
        if total >= minimum:
            return grade
    return "F"


@dataclass
class Score:
    total: int
    terms: list[ScoreTerm]
    confidence: str
    grade: str = ""

    def __post_init__(self) -> None:
        if self.grade not in GRADES:
            self.grade = grade_from_total(self.total)


def parse_score(d: Any) -> Score | None:
    if not isinstance(d, dict):
        return None
    total = _int(d, "total")
    if total is None:
        return None  # un score sans total est inutilisable → traité comme absent (« — »)
    terms = []
    for t in _list(d, "terms"):
        if isinstance(t, dict):
            terms.append(ScoreTerm(_str(t, "label", "?"), _int(t, "points", 0), _str(t, "source", "")))
    # `grade` publié par le daemon ; absent ou hors échelle → recalculé depuis `total`.
    return Score(total, terms, _str(d, "confidence", "?"), _str(d, "grade", "") or "")


@dataclass
class Enchantment:
    id: str
    name: str
    amount: int
    description: str


@dataclass
class Card:
    id: str
    name: str
    type: str
    rarity: str
    cost: int
    upgrade: int
    enchantment: Enchantment | None
    is_curse: bool
    score: Score | None


def parse_card(d: Any) -> Card | None:
    if not isinstance(d, dict):
        return None
    ench = None
    e = _obj(d, "enchantment")
    if e is not None:
        ench = Enchantment(_str(e, "id", "?"), _str(e, "name", _str(e, "id", "?")),
                           _int(e, "amount", 0), _str(e, "description", ""))
    cid = _str(d, "id", "?")
    return Card(
        id=cid, name=_str(d, "name", cid), type=_str(d, "type", "?"), rarity=_str(d, "rarity", "?"),
        cost=_int(d, "cost", 0), upgrade=_int(d, "upgrade", 0), enchantment=ench,
        is_curse=_bool(d, "isCurse"), score=parse_score(d.get("score")),
    )


@dataclass
class Relic:
    id: str
    name: str
    description: str
    score: Score | None


@dataclass
class Potion:
    id: str
    name: str
    description: str
    # PotionView n'a pas de score dans le contrat v1 → toujours None (« — »).
    score: Score | None = None


@dataclass
class Health:
    game_running: bool
    save_ok: bool
    mirror_ok: bool
    mirror_latency_ms: int | None
    degraded: str | None


@dataclass
class Run:
    character_id: str
    character_name: str
    act_index: int
    act_id: str | None
    boss_id: str | None
    boss_name: str | None
    ascension: int
    hp: int
    max_hp: int
    gold: int
    floor: int
    deck: list[Card]
    relics: list[Relic]
    potions: list[Potion]


@dataclass
class DeckEvaluation:
    score: Score | None
    archetype: str | None
    potential_archetypes: list[str]
    curses: list[Card]


@dataclass
class EngineState:
    contract_version: int
    at_raw: str | None
    at: datetime | None
    health: Health
    screen: str
    run: Run | None
    deck: DeckEvaluation | None
    settings: OverlaySettings | None
    has_reward: bool
    has_shop: bool
    has_paths: bool


@dataclass
class StateResult:
    kind: str
    state: EngineState | None = None
    message: str | None = None
    contract_version: int | None = None
    extra: dict = field(default_factory=dict)


def _parse_run(d: dict | None) -> Run | None:
    if d is None:
        return None
    deck = [c for c in (parse_card(x) for x in _list(d, "deck")) if c is not None]
    relics = []
    for r in _list(d, "relics"):
        if isinstance(r, dict):
            rid = _str(r, "id", "?")
            relics.append(Relic(rid, _str(r, "name", rid), _str(r, "description", ""),
                                parse_score(r.get("score"))))
    potions = []
    for p in _list(d, "potions"):
        if isinstance(p, dict):
            pid = _str(p, "id", "?")
            potions.append(Potion(pid, _str(p, "name", pid), _str(p, "description", ""),
                                  parse_score(p.get("score"))))
    cid = _str(d, "characterId", "?")
    return Run(
        character_id=cid, character_name=_str(d, "characterName", cid),
        act_index=_int(d, "actIndex", 0), act_id=_str(d, "actId"),
        boss_id=_str(d, "bossId"), boss_name=_str(d, "bossName"),
        ascension=_int(d, "ascension", 0), hp=_int(d, "hp", 0), max_hp=_int(d, "maxHp", 0),
        gold=_int(d, "gold", 0), floor=_int(d, "floor", 0),
        deck=deck, relics=relics, potions=potions,
    )


def _parse_deck_eval(d: dict | None) -> DeckEvaluation | None:
    if d is None:
        return None
    curses = [c for c in (parse_card(x) for x in _list(d, "curses")) if c is not None]
    return DeckEvaluation(
        score=parse_score(d.get("score")), archetype=_str(d, "archetype"),
        potential_archetypes=[a for a in _list(d, "potentialArchetypes") if isinstance(a, str)],
        curses=curses,
    )


def _parse_at(raw: str | None) -> datetime | None:
    if not raw:
        return None
    try:
        return datetime.fromisoformat(raw.replace("Z", "+00:00"))
    except ValueError:
        return None


def parse_state(data: Any) -> StateResult:
    """Objet JSON décodé → StateResult. Ne lève jamais."""
    if not isinstance(data, dict):
        return StateResult(INVALID, message="état illisible : l'objet racine n'est pas un objet JSON")
    version = _int(data, "contractVersion")
    if version is None:
        return StateResult(INVALID, message="état illisible : contractVersion absent")
    if version > SUPPORTED_CONTRACT_VERSION:
        return StateResult(
            TOO_NEW, contract_version=version,
            message=(f"Le daemon publie le contrat v{version}, ce compagnon ne connaît que la "
                     f"v{SUPPORTED_CONTRACT_VERSION} : mettre à jour ULTIMA."),
        )
    h = _obj(data, "health") or {}
    health = Health(
        game_running=_bool(h, "gameRunning"), save_ok=_bool(h, "saveOk"), mirror_ok=_bool(h, "mirrorOk"),
        mirror_latency_ms=_int(h, "mirrorLatencyMs"), degraded=_str(h, "degraded"),
    )
    settings_raw = _obj(data, "settings")
    at_raw = _str(data, "at")
    state = EngineState(
        contract_version=version, at_raw=at_raw, at=_parse_at(at_raw), health=health,
        screen=_str(data, "screen", "Unknown"),
        run=_parse_run(_obj(data, "run")),
        deck=_parse_deck_eval(_obj(data, "deck")),
        settings=OverlaySettings.from_json(settings_raw) if settings_raw is not None else None,
        has_reward=_obj(data, "reward") is not None,
        has_shop=_obj(data, "shop") is not None,
        has_paths=_obj(data, "paths") is not None,
    )
    return StateResult(OK, state=state, contract_version=version)


def parse_text(text: str) -> StateResult:
    try:
        data = json.loads(text)
    except (ValueError, RecursionError) as e:
        return StateResult(INVALID, message=f"état illisible (JSON invalide : {e})")
    return parse_state(data)


def load(path: Path | None = None) -> StateResult:
    path = path or paths.state_path()
    try:
        text = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return StateResult(MISSING, message=f"Daemon non démarré (pas de {path}).")
    except OSError as e:
        return StateResult(INVALID, message=f"état illisible : {e}")
    return parse_text(text)
