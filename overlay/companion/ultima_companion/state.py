"""Lecture tolérante de `EngineState` (contrat v1.3, camelCase, enums en chaîne, null explicite).

v1.1 : `CardView.description`, `PotionView.score`. v1.2 (additif) : `contractMinor`, `map`, `route`,
`event`, `run.party`, `score.deltaG`, `terms[].evidence`. v1.3 : `settings.hud`,
`reward.skipScore` et `card.cost` nullables. Toute section absente
(daemon plus ancien) est simplement None / vide : l'UI montre alors un état vide propre.

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
    evidence: str | None = None   # v1.2


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
    delta_g: float | None = None   # v1.2 : effet signé (malédiction < 0) ; Total reste 0..100

    def __post_init__(self) -> None:
        if self.grade not in GRADES:
            self.grade = grade_from_total(self.total)


def parse_score(d: Any) -> Score | None:
    if not isinstance(d, dict):
        return None
    total = _int(d, "total")
    if total is None:
        return None  # un score sans total est inutilisable → traité comme absent (« — »)
    # `grade` publié par le daemon ; absent ou hors échelle → recalculé depuis `total`.
    return Score(total, parse_terms(_list(d, "terms")), _str(d, "confidence", "?"),
                 _str(d, "grade", "") or "", _float(d, "deltaG"))


def parse_terms(raw: list) -> list[ScoreTerm]:
    terms = []
    for t in raw:
        if isinstance(t, dict):
            terms.append(ScoreTerm(_str(t, "label", "?"), _int(t, "points", 0), _str(t, "source", ""),
                                   _str(t, "evidence")))
    return terms


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
    cost: int | None              # v1.3 : null = coût X ou injouable
    upgrade: int
    enchantment: Enchantment | None
    is_curse: bool
    score: Score | None
    description: str | None = None   # v1.1 (IKnowledge, FR)


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
        cost=_int(d, "cost"), upgrade=_int(d, "upgrade", 0), enchantment=ench,
        is_curse=_bool(d, "isCurse"), score=parse_score(d.get("score")),
        description=_str(d, "description"),
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
class Ally:
    """v1.2 `AllyView` : un joueur de la partie (le local inclus, is_local)."""
    net_id: int
    name: str
    character_id: str
    is_local: bool
    hp: int
    max_hp: int
    deck: list[Card]
    relics: list[Relic]


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
    party: list[Ally] = field(default_factory=list)   # v1.2 ; solo = le joueur local seul

    @property
    def allies(self) -> list[Ally]:
        """Les AUTRES joueurs (co-op). Vide en solo ou si le daemon ne publie pas `party`."""
        return [a for a in self.party if not a.is_local]


@dataclass
class DeckEvaluation:
    score: Score | None
    archetype: str | None
    potential_archetypes: list[str]
    curses: list[Card]


# ---- vues de décision -------------------------------------------------------------------------

@dataclass
class Reward:
    offered: list[Card]
    skip_score: Score | None
    already_taken: bool


@dataclass
class ShopItem:
    kind: str          # card | relic | potion
    id: str
    name: str
    price: int | None
    on_sale: bool
    affordable: bool
    card: Card | None
    score: Score | None


@dataclass
class Shop:
    items: list[ShopItem]
    removal_price: int | None
    removal_used: bool
    removal_score: Score | None


@dataclass
class PathChoice:
    col: int
    row: int
    room_type: str
    path_count: int
    expected_damage: float | None


@dataclass
class Paths:
    total_paths: int | None
    next_choices: list[PathChoice]


@dataclass
class MapNode:
    col: int
    row: int
    room_type: str
    children: list[tuple[int, int]]
    visited: bool


@dataclass
class MapState:
    """v1.2 `MapView` : seulement ce que le joueur voit."""
    act_index: int
    width: int
    height: int
    nodes: list[MapNode]
    position: tuple[int, int] | None
    boss: tuple[int, int] | None

    def room_at(self, col: int, row: int) -> str | None:
        for n in self.nodes:
            if n.col == col and n.row == row:
                return n.room_type
        return None


@dataclass
class Route:
    """v1.2 `RouteView` : route conseillée de la position au boss."""
    path: list[tuple[int, int]]
    expected_hp_at_boss: int | None
    may_be_lethal: bool
    terms: list[ScoreTerm]
    basis: str


@dataclass
class EventOption:
    id: str
    label: str
    description: str | None
    available: bool
    score: Score | None


@dataclass
class Event:
    """v1.2 `EventView` : événement courant et ses options."""
    event_id: str
    name: str
    options: list[EventOption]


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
    reward: Reward | None = None
    shop: Shop | None = None
    paths: Paths | None = None
    contract_minor: int = 0
    map: MapState | None = None        # v1.2
    route: Route | None = None         # v1.2
    event: Event | None = None         # v1.2

    @property
    def has_reward(self) -> bool:
        return self.reward is not None

    @property
    def has_shop(self) -> bool:
        return self.shop is not None

    @property
    def has_paths(self) -> bool:
        return self.paths is not None


@dataclass
class StateResult:
    kind: str
    state: EngineState | None = None
    message: str | None = None
    contract_version: int | None = None
    extra: dict = field(default_factory=dict)


def _cards(raw: list) -> list[Card]:
    return [c for c in (parse_card(x) for x in raw) if c is not None]


def _relics(raw: list) -> list[Relic]:
    relics = []
    for r in raw:
        if isinstance(r, dict):
            rid = _str(r, "id", "?")
            relics.append(Relic(rid, _str(r, "name", rid), _str(r, "description", ""),
                                parse_score(r.get("score"))))
    return relics


def _parse_party(raw: list) -> list[Ally]:
    party = []
    for a in raw:
        if not isinstance(a, dict):
            continue
        cid = _str(a, "characterId", "?")
        party.append(Ally(
            net_id=_int(a, "netId", 0), name=_str(a, "name", cid), character_id=cid,
            is_local=_bool(a, "isLocal"), hp=_int(a, "hp", 0), max_hp=_int(a, "maxHp", 0),
            deck=_cards(_list(a, "deck")), relics=_relics(_list(a, "relics")),
        ))
    return party


def _parse_run(d: dict | None) -> Run | None:
    if d is None:
        return None
    deck = _cards(_list(d, "deck"))
    relics = _relics(_list(d, "relics"))
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
        deck=deck, relics=relics, potions=potions, party=_parse_party(_list(d, "party")),
    )


def _parse_reward(d: dict | None) -> Reward | None:
    if d is None:
        return None
    return Reward(_cards(_list(d, "offered")), parse_score(d.get("skipScore")), _bool(d, "alreadyTaken"))


def _parse_shop(d: dict | None) -> Shop | None:
    if d is None:
        return None
    items = []
    for it in _list(d, "items"):
        if not isinstance(it, dict):
            continue
        iid = _str(it, "id", "?")
        items.append(ShopItem(
            kind=_str(it, "kind", "?"), id=iid, name=_str(it, "name", iid), price=_int(it, "price"),
            on_sale=_bool(it, "onSale"), affordable=_bool(it, "affordable", True),
            card=parse_card(it.get("card")), score=parse_score(it.get("score")),
        ))
    return Shop(items, _int(d, "removalPrice"), _bool(d, "removalUsed"), parse_score(d.get("removalScore")))


def _parse_paths(d: dict | None) -> Paths | None:
    if d is None:
        return None
    choices = []
    for c in _list(d, "nextChoices"):
        if isinstance(c, dict):
            choices.append(PathChoice(_int(c, "col", 0), _int(c, "row", 0), _str(c, "roomType", "?"),
                                      _int(c, "pathCount", 0), _float(c, "expectedDamage")))
    return Paths(_int(d, "totalPaths"), choices)


def _coord(d: Any) -> tuple[int, int] | None:
    if not isinstance(d, dict):
        return None
    col, row = _int(d, "col"), _int(d, "row")
    if col is None or row is None:
        return None
    return (col, row)


def _parse_map(d: dict | None) -> MapState | None:
    if d is None:
        return None
    nodes = []
    for n in _list(d, "nodes"):
        if not isinstance(n, dict):
            continue
        c = _coord(n)
        if c is None:
            continue
        kids = [k for k in (_coord(x) for x in _list(n, "children")) if k is not None]
        nodes.append(MapNode(c[0], c[1], _str(n, "roomType", "?"), kids, _bool(n, "visited")))
    return MapState(_int(d, "actIndex", 0), _int(d, "width", 0), _int(d, "height", 0), nodes,
                    _coord(d.get("position")), _coord(d.get("boss")))


def _parse_route(d: dict | None) -> Route | None:
    if d is None:
        return None
    path = [c for c in (_coord(x) for x in _list(d, "path")) if c is not None]
    return Route(path, _int(d, "expectedHpAtBoss"), _bool(d, "mayBeLethal"),
                 parse_terms(_list(d, "terms")), _str(d, "basis", ""))


def _parse_event(d: dict | None) -> Event | None:
    if d is None:
        return None
    options = []
    for o in _list(d, "options"):
        if isinstance(o, dict):
            oid = _str(o, "id", "?")
            options.append(EventOption(oid, _str(o, "label", oid), _str(o, "description"),
                                       _bool(o, "available", True), parse_score(o.get("score"))))
    eid = _str(d, "eventId", "?")
    return Event(eid, _str(d, "name", eid), options)


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
        reward=_parse_reward(_obj(data, "reward")),
        shop=_parse_shop(_obj(data, "shop")),
        paths=_parse_paths(_obj(data, "paths")),
        contract_minor=_int(data, "contractMinor", 0),
        map=_parse_map(_obj(data, "map")),
        route=_parse_route(_obj(data, "route")),
        event=_parse_event(_obj(data, "event")),
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
