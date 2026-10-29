#!/usr/bin/env python3
"""Features de calibration des poids de G (docs/27 §7bis) depuis l'index de runs.

Ce module **reproduit** les quatre dimensions de `Ultima.Engine/Scoring/Strength/PlayerStrength.cs`
(et non une variante maison), puis expose un vecteur plat par run (ou par run × acte) pour la
régression logistique de `fit_weights.py`. Les écarts avec le C# sont listés dans
`DEVIATIONS` ci-dessous et dans `tools/calibration/report.md`.

Entrées (aucune n'est commitée) :
  data/index/runs-<build>.jsonl.gz      une ligne par run (format docs/28 §2.2)
  data/catalog/cards_effects.json       valeurs des cartes (577 entrées)
  data/catalog/monsters.json            PV + moves par monstre (menace)
  data/catalog/encounters.json          acte / type de salle par rencontre
  data/catalog/relics.json              tags des reliques (équivalents)

Sortie : JSONL de features — une ligne par run (`--state final`), une ligne par run × acte
(`--state act`, reconstruction arrière), ou les deux en un passage (`--state both`, marqueur
`kind` dans chaque ligne).
Python 3 standard library uniquement, lecture en streaming (l'index fait 541 Mo compressés).

    python3 -I tools/calibration/features.py --out /tmp/feat.jsonl --stride 8
    python3 -I tools/calibration/features.py --out /tmp/feat-act.jsonl --state act --stride 8
"""

from __future__ import annotations

import argparse
import gzip
import json
import math
import os
import statistics
import sys

__all__ = [
    "DEVIATIONS",
    "Catalogs",
    "DIMENSIONS",
    "FEATURE_ORDER",
    "Saturate",
    "build_run_features",
    "burst_card_count",
    "burst_damage",
    "dimensions",
    "iter_index",
]

# --------------------------------------------------------------------------- #
# Constantes du modèle — copie des constantes nommées du C# (PlayerStrength.cs)
# Toute valeur modifiée ici doit l'être aussi là-bas : ce fichier n'a pas
# vocation à inventer un modèle, seulement à rejouer le même sur des données.
# --------------------------------------------------------------------------- #
BASE_DRAW_PER_TURN = 5
BASE_ENERGY_PER_TURN = 3
XCOST_ASSUMED_ENERGY = 1.0
AVG_COST_FLOOR = 0.75
ASSUMED_COMBAT_TURNS = 5.0
HEAVY_COST_THRESHOLD = BASE_ENERGY_PER_TURN + 1
IDEAL_DECK_SIZE = 25.0

BLOCK_SHARE_AT_HALF = 0.40
HP_BUFFER_HITS_AT_HALF = 2.5
HEAL_HITS_AT_HALF = 0.5
OFFENSE_KILL_TURNS = 8.0
ENERGY_COVERAGE_AT_HALF = 2.0
DRAW_RATIO_AT_HALF = 1.0
AOE_SHARE_AT_HALF = 0.15
DEBUFF_CARDS_AT_HALF = 2.0
UTILITY_AT_HALF = 3.0

SURVIVAL_BLOCK_WEIGHT = 0.55
SURVIVAL_HP_WEIGHT = 0.35
SURVIVAL_HEAL_WEIGHT = 0.10
CONSISTENCY_DRAW_WEIGHT = 0.35
CONSISTENCY_ENERGY_WEIGHT = 0.35
CONSISTENCY_SIZE_WEIGHT = 0.30
COVERAGE_AOE_WEIGHT = 0.45
COVERAGE_DEBUFF_WEIGHT = 0.35
COVERAGE_UTILITY_WEIGHT = 0.20

CURSE_PENALTY = 6
STATUS_PENALTY = 3
SITUATIONAL_PENALTY = 2
SITUATIONAL_PENALTY_MAX = 10
UNKNOWN_CARD_PENALTY = 1

RELIC_BLOCK_PER_TURN = 2.0
RELIC_DRAW_PER_TURN = 0.5
RELIC_ENERGY_PER_TURN = 0.5
RELIC_HEAL_PER_COMBAT = 5.0
RELIC_SCALING_POINTS = 1.5
RELIC_TAG_CAP = 2
RELIC_CURSE_PENALTY = 4
RELIC_CURSE_CAP = 3

STRENGTH_DAMAGE_PER_POINT = 1.5
POISON_DAMAGE_PER_POINT = 0.8
FOCUS_DAMAGE_PER_POINT = 1.0
DEXTERITY_BLOCK_PER_POINT = 0.8
PLATING_BLOCK_PER_POINT = 0.6
THORNS_BLOCK_PER_POINT = 0.4

DEBUFF_POWERS = frozenset(
    {"vulnerable", "weak", "poison", "doom", "strengthloss", "debilitate", "disintegration", "mind rot"}
)

#: Poids par acte du code (ActWeights Act1/Act2/Act3) — référence du rapport, jamais utilisée
#: pour construire une feature (ce serait circulaire).
CODE_WEIGHTS = {
    1: {"survie": 0.40, "offense": 0.30, "consistance": 0.20, "couverture": 0.10},
    2: {"survie": 0.30, "offense": 0.35, "consistance": 0.20, "couverture": 0.15},
    3: {"survie": 0.30, "offense": 0.40, "consistance": 0.15, "couverture": 0.15},
}

DIMENSIONS = ("survie", "offense", "consistance", "couverture")

#: Ordre canonique des colonnes du vecteur plat. Les contrôles (`asc`, `act_reached`, `n`,
#: `char`) sont portés par l'en-tête de la ligne, pas par une colonne `c_*` : `fit_weights.py`
#: les ajoute au design et ne standardise qu'eux.
FEATURE_ORDER = (
    "s_survie", "s_offense", "s_consistance", "s_couverture",
    "m_block_per_turn", "m_damage_per_turn", "m_damage_density", "m_burst_cards",
    "m_draw_per_turn", "m_energy_per_turn",
    "m_play_rate", "m_avg_cost", "m_scaling", "m_aoe_share", "m_debuff_cards",
    "m_utility_categories", "m_penalties", "m_deck_size", "m_curses",
    "m_hp", "m_max_hp", "m_heal_per_combat", "m_worst_hit", "m_boss_hp",
)

DEVIATIONS = (
    "améliorations inconnues : l'index ne porte pas le niveau des cartes (aucun suffixe '+' dans "
    "les decks). Toutes les cartes sont chiffrées NON améliorées — dégâts/bloc réels ~15-25 % plus "
    "hauts sur les decks qui améliorent. Biais de niveau, pas de classement.",
    "offense en burst : `damage_per_turn` est la somme des k meilleures cartes (k = ceil(cartes "
    "jouées par tour)), comme le C# depuis S17. Les dégâts améliorés ne sont pas mesurables ici ; "
    "la variante « tout amélioré » a été testée dans tools/calibration/diagnose_offense.py et "
    "sépare MOINS bien, donc le biais d'amélioration ne crée pas le signal.",
    "scaling : Force/Poison/Focus des cartes + Force des reliques, ajoutés au burst avant une "
    "saturation unique (le C# excluait les reliques avant S17 : écart corrigé).",
    "PV : `pl[].hp` est null dans l'index ; on prend les PV du DERNIER enregistrement de décision "
    "du joueur (donc pas forcément les PV de fin de run, et jamais ceux d'un combat).",
    "menace : le boss réel n'est pas dans l'index ; on utilise la MÉDIANE des boss de l'acte "
    "comme le fait le C# quand `ctx.BossId` est inconnu (IsActMedian = true).",
    "statuts du tour 1 (Vulnérable/Faiblesse) non connus → PV effectifs = PV brut borné à MaxHp.",
    "reliques : équivalents par tags, comme le C#, mais sur la liste finale (ou reconstruite) — "
    "les tags `maxhp` ne sont pas ajoutés aux PV (déjà dans MaxHp, même règle que le C#).",
    "arrondis : le C# arrondit chaque dimension avec MidpointRounding.AwayFromZero ; idem ici.",
    "X-cost : énergie supposée 1 comme le C#, donc sous-estimation assumée des cartes X.",
)


# --------------------------------------------------------------------------- #
# Petites fonctions numériques (miroir de ScoreMath / PlayerStrengthCalculator)
# --------------------------------------------------------------------------- #
def rnd(value: float) -> int:
    """`Math.Round(x, MidpointRounding.AwayFromZero)` — pas l'arrondi bancaire de Python."""
    return int(math.floor(value + 0.5)) if value >= 0 else -int(math.floor(-value + 0.5))


def clamp(value: float, low: float, high: float) -> float:
    return low if value < low else high if value > high else value


def saturate(value: float, half_point: float) -> float:
    """`100·x/(x+k)` : la saturation qui rend ΔG idempotent (docs/27 §7bis)."""
    if value != value or value <= 0:  # NaN ou ≤ 0
        return 0.0
    k = half_point if half_point > 0 else 1.0
    out = 100.0 * value / (value + k)
    return 0.0 if out != out else min(100.0, out)


Saturate = saturate  # nom public, comme le C#


def median(values: list[float]) -> float:
    return statistics.median(values) if values else 0.0


# --------------------------------------------------------------------------- #
# Catalogues
# --------------------------------------------------------------------------- #
class Catalogs:
    """Charge les catalogues commités et répond aux questions du modèle de G."""

    def __init__(self, catalog_dir: str):
        self.card = {}
        self.relic_tags = {}
        self.monsters = {}
        self.bosses_by_act = {1: [], 2: [], 3: []}
        self._threat_cache = {}
        self._load_cards(os.path.join(catalog_dir, "cards_effects.json"))
        self._load_relics(os.path.join(catalog_dir, "relics.json"))
        self._load_threats(
            os.path.join(catalog_dir, "monsters.json"),
            os.path.join(catalog_dir, "encounters.json"),
        )

    # -- cartes -------------------------------------------------------------
    def _load_cards(self, path: str) -> None:
        with open(path, "r", encoding="utf-8") as handle:
            payload = json.load(handle)
        for entry in payload.get("cards") or ():
            if isinstance(entry, dict) and entry.get("id"):
                self.card[entry["id"].upper()] = entry

    # -- reliques -----------------------------------------------------------
    def _load_relics(self, path: str) -> None:
        if not os.path.exists(path):
            return
        with open(path, "r", encoding="utf-8") as handle:
            payload = json.load(handle)
        for entry in payload.get("relics") or ():
            if isinstance(entry, dict) and entry.get("id"):
                tags = tuple(str(t).lower() for t in (entry.get("tags") or ()))
                self.relic_tags[entry["id"].upper()] = tags

    # -- menace -------------------------------------------------------------
    def _load_threats(self, monsters_path: str, encounters_path: str) -> None:
        if os.path.exists(monsters_path):
            with open(monsters_path, "r", encoding="utf-8") as handle:
                for entry in json.load(handle):
                    if not isinstance(entry, dict) or not entry.get("id"):
                        continue
                    self.monsters[entry["id"].upper()] = entry
        if not os.path.exists(encounters_path):
            return
        with open(encounters_path, "r", encoding="utf-8") as handle:
            payload = json.load(handle)
        for encounter in payload.get("encounters") or ():
            if not isinstance(encounter, dict):
                continue
            act = encounter.get("act")
            room = str(encounter.get("room_type") or "")
            if act in (1, 2, 3) and room.lower() == "boss":
                self.bosses_by_act[act].append(encounter)

    @staticmethod
    def _move_hit(move: dict, ascension: int) -> int:
        damage = move.get("damage") or {}
        if not isinstance(damage, dict):
            return 0
        normal, asc = damage.get("normal"), damage.get("ascension")
        # ThreatRules.AscensionDamageFrom = 10 : `ascension` à partir de A10, sinon `normal`.
        value = (asc if asc is not None else normal) if ascension >= 10 else (normal if normal is not None else asc)
        if not value:
            return 0
        hits = damage.get("hit_count")
        hits = hits if isinstance(hits, int) and hits > 0 else 1
        return int(value) * hits

    def _monster_hp(self, monster: dict, ascension: int) -> int:
        """PV du monstre ; repli `min_hp.values` = plus petit (règle `LegacyHp` du C#)."""
        normal = monster.get("hp_normal")
        asc = monster.get("hp_ascension")
        if normal is None and asc is None:
            min_hp = monster.get("min_hp")
            if isinstance(min_hp, dict):
                values = [v for v in (min_hp.get("values") or ()) if isinstance(v, (int, float))]
                if values:
                    normal = asc = int(min(values))
            elif isinstance(min_hp, (int, float)):
                normal = asc = int(min_hp)
        value = (asc if asc is not None else normal) if ascension > 0 else (normal if normal is not None else asc)
        return int(value) if value else 0

    def threat_reference(self, act: int, ascension: int) -> tuple[int, int]:
        """(pire coup, PV du boss) — médiane des boss de l'acte, comme le repli du C#."""
        ascension = int(ascension or 0)
        # PV des monstres : variante `ascension` dès A1 (`HpAt`), dégâts : seulement dès A10
        # (`ThreatRules.AscensionDamageFrom`). Trois régimes, donc trois clés de cache.
        bucket = 0 if ascension <= 0 else (10 if ascension >= 10 else 1)
        key = (act, bucket)
        if key in self._threat_cache:
            return self._threat_cache[key]
        hits, hps = [], []
        for encounter in self.bosses_by_act.get(act, ()):
            hit = 0
            hp = 0
            for monster_id in encounter.get("monsters") or ():
                monster = self.monsters.get(str(monster_id).upper())
                if monster is None:
                    continue
                # MaxHit du C# : somme, sur les monstres, du move le plus fort de chacun.
                worst = 0
                for move in monster.get("moves") or ():
                    if isinstance(move, dict):
                        worst = max(worst, self._move_hit(move, ascension))
                hit += worst
                hp += self._monster_hp(monster, ascension)
            if hit > 0:
                hits.append(hit)
            if hp > 0:
                hps.append(hp)
        result = (rnd(median(hits)), rnd(median(hps)))
        self._threat_cache[key] = result
        return result

    # -- reliques : équivalents -------------------------------------------------
    def relic_equivalents(self, relic_ids) -> dict:
        block = draw = energy = heal = scaling = debuff = curse = 0.0
        counted_block = counted_draw = counted_energy = counted_heal = counted_scaling = 0
        counted_debuff = counted_curse = 0
        for relic_id in relic_ids or ():
            tags = self.relic_tags.get(str(relic_id).upper())
            if not tags:
                continue
            if "block" in tags and counted_block < RELIC_TAG_CAP:
                counted_block += 1
                block += RELIC_BLOCK_PER_TURN
            if "draw" in tags and counted_draw < RELIC_TAG_CAP:
                counted_draw += 1
                draw += RELIC_DRAW_PER_TURN
            if "energy" in tags and counted_energy < RELIC_TAG_CAP:
                counted_energy += 1
                energy += RELIC_ENERGY_PER_TURN
            if "heal" in tags and counted_heal < RELIC_TAG_CAP:
                counted_heal += 1
                heal += RELIC_HEAL_PER_COMBAT
            if "strength" in tags and counted_scaling < RELIC_TAG_CAP:
                counted_scaling += 1
                scaling += RELIC_SCALING_POINTS
            if "debuff" in tags and counted_debuff < RELIC_TAG_CAP:
                counted_debuff += 1
                debuff += 1
            if "curse" in tags and counted_curse < RELIC_CURSE_CAP:
                counted_curse += 1
                curse += RELIC_CURSE_PENALTY
        return {
            "block": block,
            "draw": draw,
            "energy": energy,
            "heal": heal,
            "scaling": min(scaling, RELIC_TAG_CAP * RELIC_SCALING_POINTS),
            "debuff": debuff,
            "curse_penalty": curse,
        }


# --------------------------------------------------------------------------- #
# Profil de deck (miroir de Profile() + DeckProfile)
# --------------------------------------------------------------------------- #
class Profile:
    __slots__ = ("playable", "unknown", "curses", "statuses", "situational", "damage", "damages",
                 "block", "cost", "draw", "energy_gain", "heal", "aoe_damage", "debuff_cards",
                 "offense_scaling", "defense_scaling", "utility")

    def __init__(self) -> None:
        self.playable = 0
        self.unknown = 0
        self.curses = 0
        self.statuses = 0
        self.situational = 0
        self.damage = 0
        self.damages = []          # dégâts arrondis par carte (burst top-k), comme `DeckProfile.Damages`
        self.block = 0
        self.cost = 0.0
        self.draw = 0
        self.energy_gain = 0
        self.heal = 0.0
        self.aoe_damage = 0
        self.debuff_cards = 0
        self.offense_scaling = 0.0
        self.defense_scaling = 0.0
        self.utility = set()


def _power_amount(effect: dict, power: str):
    variables = effect.get("vars") or {}
    for key in (power + "Power", power):
        if key in variables and isinstance(variables[key], (int, float)):
            return float(variables[key])
    return 0.0


def _has_debuff(effect: dict) -> bool:
    for power in effect.get("powers") or ():
        if str(power).lower() in DEBUFF_POWERS:
            return True
    return False


def _is_situational(effect: dict) -> bool:
    keywords = {str(k).lower() for k in (effect.get("keywords") or ())}
    if "ethereal" in keywords or "sly" in keywords or "unplayable" in keywords:
        return True
    if str(effect.get("type") or "").lower() == "quest":
        return True
    if str(effect.get("rarity") or "") == "Token":
        return True
    cost = 0 if effect.get("x_cost") else (effect.get("cost_up") if effect.get("cost_up") is not None else effect.get("cost") or 0)
    return cost > HEAVY_COST_THRESHOLD


def profile(deck, catalogs: Catalogs) -> tuple[Profile, int]:
    """Profil du deck ; renvoie aussi la taille utilisée pour la division (le C# divise par N)."""
    result = Profile()
    for card_id in deck:
        effect = catalogs.card.get(str(card_id).upper())
        if effect is None:
            result.unknown += 1
            continue
        card_type = str(effect.get("type") or "").lower()
        is_curse = card_type == "curse"
        is_status = card_type == "status"
        if is_curse:
            result.curses += 1
        if is_status:
            result.statuses += 1
        keywords = {str(k).lower() for k in (effect.get("keywords") or ())}
        if "unplayable" in keywords:
            if not is_curse:
                result.situational += 1
            continue

        # Amélioration inconnue dans l'index → niveau 0 (voir DEVIATIONS).
        repeats = XCOST_ASSUMED_ENERGY if effect.get("x_cost") else 1.0
        result.playable += 1
        cost = effect.get("cost")
        result.cost += XCOST_ASSUMED_ENERGY if effect.get("x_cost") else max(0.0, float(cost or 0))

        damage_base = effect.get("damage_base")
        if damage_base:
            hits = effect.get("hits")
            hits = hits if isinstance(hits, int) and hits > 0 else 1
            damage = float(damage_base) * hits * repeats
            result.damage += rnd(damage)
            result.damages.append(rnd(damage))
            if str(effect.get("target") or "").lower() == "allenemies":
                result.aoe_damage += rnd(damage)
            result.utility.add("damage")

        block_base = effect.get("block_base")
        if block_base:
            block = float(block_base) * repeats
            result.block += rnd(block)
            result.utility.add("bloc")

        draw = effect.get("draw_base")
        draw = int(draw) if isinstance(draw, (int, float)) else 0
        if draw > 0:
            result.utility.add("pioche")
        result.draw += draw

        energy = effect.get("energy_base")
        energy = int(energy) if isinstance(energy, (int, float)) else 0
        if energy > 0:
            result.utility.add("énergie")
        result.energy_gain += energy

        variables = effect.get("vars") or {}
        heal = float(variables["Heal"]) if isinstance(variables.get("Heal"), (int, float)) else 0.0
        if heal > 0:
            result.heal += heal
            result.utility.add("soin")

        offense_scaling = (
            _power_amount(effect, "Strength") * STRENGTH_DAMAGE_PER_POINT
            + _power_amount(effect, "Poison") * POISON_DAMAGE_PER_POINT
            + _power_amount(effect, "Focus") * FOCUS_DAMAGE_PER_POINT
        )
        if offense_scaling > 0:
            result.offense_scaling += offense_scaling
            result.utility.add("scaling")

        result.defense_scaling += (
            _power_amount(effect, "Dexterity") * DEXTERITY_BLOCK_PER_POINT
            + _power_amount(effect, "Plating") * PLATING_BLOCK_PER_POINT
            + _power_amount(effect, "Thorns") * THORNS_BLOCK_PER_POINT
        )

        if _has_debuff(effect):
            result.debuff_cards += 1
            result.utility.add("statut")

        if _is_situational(effect):
            result.situational += 1
    return result, len(deck)


# --------------------------------------------------------------------------- #
# Débit et dimensions
# --------------------------------------------------------------------------- #
def draw_per_turn(draw_sum: int, deck_size: int, relic_draw: float) -> float:
    if deck_size <= 0:
        return BASE_DRAW_PER_TURN + relic_draw
    share = min(0.6, draw_sum / float(deck_size))
    draw = BASE_DRAW_PER_TURN / (1.0 - share)
    return min(2.0 * BASE_DRAW_PER_TURN, draw) + relic_draw


def energy_per_turn(energy_sum: int, draw: float, deck_size: int, relic_energy: float) -> float:
    gain = energy_sum * draw / deck_size if deck_size > 0 else 0.0
    return min(2.0 * BASE_ENERGY_PER_TURN, BASE_ENERGY_PER_TURN + gain) + relic_energy


def play_rate(energy: float, draw: float, avg_cost: float) -> float:
    if draw <= 0:
        return 0.0
    demand = draw * max(avg_cost, AVG_COST_FLOOR)
    return 1.0 if demand <= 0 else clamp(energy / demand, 0.0, 1.0)


def burst_card_count(damage_cards: int, cards_per_turn: float) -> int:
    """Nombre de cartes dont on peut sortir les dégâts en un tour (miroir de `BurstCardCount`)."""
    if damage_cards <= 0 or cards_per_turn <= 0:
        return 0
    k = int(math.ceil(cards_per_turn - 1e-9))
    return max(1, min(k, damage_cards))


def burst_damage(damages, burst_cards: int) -> float:
    """Somme des `burst_cards` plus gros dégâts (miroir de `BurstDamage`)."""
    if burst_cards <= 0 or not damages:
        return 0.0
    return float(sum(sorted(damages, reverse=True)[:burst_cards]))


def dimensions(deck, relics, hp, max_hp, act: int, ascension: int, catalogs: Catalogs) -> dict:
    """Les 4 dimensions 0..100 du C# + toutes les mesures intermédiaires (traçabilité)."""
    act = 1 if act is None else max(1, min(3, int(act)))
    ascension = 0 if ascension is None else max(0, int(ascension))
    deck = list(deck or ())
    measures = {}
    if not deck:
        return {
            "survie": 0, "offense": 0, "consistance": 0, "couverture": 0,
            "measures": {"deck": 0, "confidence": "unknown"},
        }

    prof, size = profile(deck, catalogs)
    relic = catalogs.relic_equivalents(relics)

    draw = draw_per_turn(prof.draw, size, relic["draw"])
    energy = energy_per_turn(prof.energy_gain, draw, size, relic["energy"])
    avg_cost = prof.cost / prof.playable if prof.playable > 0 else AVG_COST_FLOOR
    rate = play_rate(energy, draw, avg_cost)
    cards_per_turn = rate * draw

    # S17 : l'offense est un plafond (k meilleures cartes), plus une densité moyenne.
    burst_cards = burst_card_count(len(prof.damages), cards_per_turn)
    damage_per_turn = burst_damage(prof.damages, burst_cards)
    damage_density = rate * draw * prof.damage / size
    scaling = prof.offense_scaling + relic["scaling"]

    card_block_per_turn = rate * draw * prof.block / size
    block_per_turn = card_block_per_turn + prof.defense_scaling + relic["block"]
    heal_per_combat = rate * draw * prof.heal / size * ASSUMED_COMBAT_TURNS + relic["heal"]

    worst_hit, boss_hp = catalogs.threat_reference(act, ascension)

    # PV : inconnus dans l'index → hp/max_hp fournis par l'appelant (dernière décision).
    max_hp = int(max_hp or 0)
    hp = int(hp) if hp is not None else max_hp
    hp = int(clamp(hp, 0, max_hp)) if max_hp > 0 else max(0, hp)

    # --- Survie
    if worst_hit <= 0:
        survie = 0
    else:
        s_block = saturate(block_per_turn / worst_hit, BLOCK_SHARE_AT_HALF)
        s_hp = saturate(hp / float(worst_hit), HP_BUFFER_HITS_AT_HALF)
        s_heal = saturate(heal_per_combat / worst_hit, HEAL_HITS_AT_HALF)
        survie = rnd(clamp(
            SURVIVAL_BLOCK_WEIGHT * s_block + SURVIVAL_HP_WEIGHT * s_hp + SURVIVAL_HEAL_WEIGHT * s_heal,
            0, 100))

    # --- Offense
    if boss_hp <= 0:
        offense = 0
    else:
        # Une seule saturation sur (burst + scaling) : mesurer le scaling à part, avec son propre
        # poids, inversait le signe de la dimension (S17, tools/calibration/report.md §4).
        offense = rnd(clamp(saturate(damage_per_turn + scaling, boss_hp / OFFENSE_KILL_TURNS), 0, 100))

    # --- Consistance
    s_draw = saturate(draw / BASE_DRAW_PER_TURN, DRAW_RATIO_AT_HALF)
    s_energy = saturate(energy / max(avg_cost, AVG_COST_FLOOR), ENERGY_COVERAGE_AT_HALF)
    excess = max(0.0, size - IDEAL_DECK_SIZE)
    s_size = 100.0 * IDEAL_DECK_SIZE / (IDEAL_DECK_SIZE + excess)
    penalties = (
        prof.curses * CURSE_PENALTY
        + prof.statuses * STATUS_PENALTY
        + min(SITUATIONAL_PENALTY_MAX, prof.situational * SITUATIONAL_PENALTY)
        + prof.unknown * UNKNOWN_CARD_PENALTY
        + relic["curse_penalty"]
    )
    consistance = int(clamp(
        rnd(CONSISTENCY_DRAW_WEIGHT * s_draw + CONSISTENCY_ENERGY_WEIGHT * s_energy
            + CONSISTENCY_SIZE_WEIGHT * s_size) - penalties, 0, 100))

    # --- Couverture
    aoe_share = prof.aoe_damage / prof.damage if prof.damage > 0 else 0.0
    s_aoe = saturate(aoe_share, AOE_SHARE_AT_HALF)
    s_debuff = saturate(prof.debuff_cards + relic["debuff"], DEBUFF_CARDS_AT_HALF)
    s_utility = saturate(len(prof.utility), UTILITY_AT_HALF)
    couverture = rnd(clamp(
        COVERAGE_AOE_WEIGHT * s_aoe + COVERAGE_DEBUFF_WEIGHT * s_debuff
        + COVERAGE_UTILITY_WEIGHT * s_utility, 0, 100))

    measures.update({
        "deck": size,
        "block_per_turn": round(block_per_turn, 4),
        "card_block_per_turn": round(card_block_per_turn, 4),
        "damage_per_turn": round(damage_per_turn, 4),
        "damage_density": round(damage_density, 4),
        "burst_cards": burst_cards,
        "draw_per_turn": round(draw, 4),
        "energy_per_turn": round(energy, 4),
        "avg_cost": round(avg_cost, 4),
        "play_rate": round(rate, 4),
        "scaling": round(scaling, 4),
        "scaling_cards": round(prof.offense_scaling, 4),
        "defense_scaling": round(prof.defense_scaling, 4),
        "heal_per_combat": round(heal_per_combat, 4),
        "aoe_share": round(aoe_share, 4),
        "debuff_cards": prof.debuff_cards + int(relic["debuff"]),
        "utility_categories": len(prof.utility),
        "penalties": penalties,
        "curses": prof.curses,
        "statuses": prof.statuses,
        "situational": prof.situational,
        "unknown_cards": prof.unknown,
        "hp": hp,
        "max_hp": max_hp,
        "worst_hit": worst_hit,
        "boss_hp": boss_hp,
        "coverage": round(1.0 - prof.unknown / size, 4) if size else 0.0,
        "confidence": "high" if prof.unknown == 0 and worst_hit > 0 else
                      ("medium" if worst_hit > 0 else "low"),
    })
    return {
        "survie": survie,
        "offense": offense,
        "consistance": consistance,
        "couverture": couverture,
        "measures": measures,
    }


# --------------------------------------------------------------------------- #
# Lecture de l'index
# --------------------------------------------------------------------------- #
def open_index(path: str):
    with open(path, "rb") as probe:
        magic = probe.read(2)
    if magic == b"\x1f\x8b":
        return gzip.open(path, "rt", encoding="utf-8")
    return open(path, "rt", encoding="utf-8")


def iter_index(path: str, stride: int = 1, limit: int = 0):
    """Itère les runs bruts ; `stride` = un run gardé sur N (échantillon systématique)."""
    seen = 0
    kept = 0
    with open_index(path) as stream:
        for line in stream:
            line = line.strip()
            if not line:
                continue
            seen += 1
            if stride > 1 and seen % stride != 0:
                continue
            try:
                yield json.loads(line)
            except json.JSONDecodeError:
                continue
            kept += 1
            if limit and kept >= limit:
                break


# --------------------------------------------------------------------------- #
# Construction des features
# --------------------------------------------------------------------------- #
CANONICAL = ("IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT")


def _character(raw) -> str | None:
    text = str(raw or "").strip().upper()
    if text.startswith("CHARACTER."):
        text = text[10:]
    if "." in text:
        text = text.split(".", 1)[0]
    return text.lower() if text in CANONICAL else None


def _act_reached(decisions) -> int:
    acts = [d.get("a") for d in decisions if isinstance(d.get("a"), int)]
    return (max(acts) + 1) if acts else 1


def _picks(decisions, player_id):
    """Cartes et reliques prises, par acte (index 0-based) — pour la reconstruction arrière."""
    cards, relics = {}, {}
    for dec in decisions:
        if dec.get("p") is not None and str(dec.get("p")) != str(player_id):
            continue
        act = dec.get("a")
        if not isinstance(act, int):
            continue
        kind = dec.get("t")
        if kind == "card" and dec.get("k"):
            cards.setdefault(act, []).append(dec["k"])
        elif kind == "relic" and dec.get("rk"):
            relics.setdefault(act, []).append(dec["rk"])
    return cards, relics


def _state_before_after(final_deck, final_relics, picks_cards, picks_relics, act_index):
    """Deck/reliques à la FIN de l'acte `act_index`, reconstruits vers l'arrière.

    On part du deck final (exact, donné par l'index) et on **retire** les cartes prises après
    l'acte : c'est la méthode que docs/29 §2.2 impose (l'accumulation avant ne referme que sur
    ~11 % des runs). Les retraits/transformations non journalisés restent une imprécision.
    """
    deck = list(final_deck)
    for act, cards in picks_cards.items():
        if act <= act_index:
            continue
        for card in cards:
            try:
                deck.remove(card)
            except ValueError:
                pass  # carte déjà transformée/retirée : on ne peut pas la retirer
    relic_list = list(final_relics)
    for act, relics in picks_relics.items():
        if act <= act_index:
            continue
        for relic in relics:
            try:
                relic_list.remove(relic)
            except ValueError:
                pass
    return deck, relic_list


def _hp_at(decisions, player_id, act_index):
    """Dernier HP/MaxHp connus à la fin de l'acte `act_index` (repli sur les actes précédents)."""
    best = None
    for dec in decisions:
        if dec.get("p") is not None and str(dec.get("p")) != str(player_id):
            continue
        act = dec.get("a")
        if not isinstance(act, int) or act > act_index:
            continue
        if isinstance(dec.get("hp"), int) and isinstance(dec.get("mhp"), int):
            key = (act, dec.get("f") if isinstance(dec.get("f"), int) else 0)
            if best is None or key >= best[0]:
                best = (key, dec["hp"], dec["mhp"])
    return (best[1], best[2]) if best else (None, None)


def build_run_features(raw: dict, catalogs: Catalogs, state: str = "final") -> list[dict]:
    """Features d'une run — une ligne (`state=final`) ou une par acte atteint (`state=act`)."""
    players = raw.get("pl") or ()
    if not players:
        return []
    player = players[0]
    character = _character(player.get("c"))
    if character is None:
        return []
    decisions = raw.get("d") or []
    act_reached = _act_reached(decisions)
    picks_cards, picks_relics = _picks(decisions, player.get("id"))
    ascension = int(raw["asc"]) if isinstance(raw.get("asc"), int) else 0
    base = {
        "h": raw.get("h"),
        "y": int(bool(raw.get("w"))),
        "ab": int(bool(raw.get("ab"))),
        "asc": ascension,
        "n": raw.get("n") or len(players),
        "char": character,
        "act_reached": act_reached,
    }

    if state == "final":
        hp, max_hp = _hp_at(decisions, player.get("id"), 99)
        result = dimensions(player.get("deck"), player.get("rel"), hp, max_hp,
                            act_reached, int(ascension), catalogs)
        row = dict(base)
        row.update(_flat(result, act=act_reached))
        return [row]

    rows = []
    for act_index in range(act_reached):
        deck, relics = _state_before_after(
            player.get("deck") or (), player.get("rel") or (),
            picks_cards, picks_relics, act_index)
        hp, max_hp = _hp_at(decisions, player.get("id"), act_index)
        result = dimensions(deck, relics, hp, max_hp, act_index + 1, int(ascension), catalogs)
        row = dict(base)
        row.update(_flat(result, act=act_index + 1))
        rows.append(row)
    return rows


def _flat(result: dict, act: int) -> dict:
    measures = result.get("measures") or {}
    return {
        "act": act,
        "s_survie": result["survie"],
        "s_offense": result["offense"],
        "s_consistance": result["consistance"],
        "s_couverture": result["couverture"],
        "m_block_per_turn": measures.get("block_per_turn", 0.0),
        "m_damage_per_turn": measures.get("damage_per_turn", 0.0),
        "m_damage_density": measures.get("damage_density", 0.0),
        "m_burst_cards": measures.get("burst_cards", 0),
        "m_draw_per_turn": measures.get("draw_per_turn", 0.0),
        "m_energy_per_turn": measures.get("energy_per_turn", 0.0),
        "m_play_rate": measures.get("play_rate", 0.0),
        "m_avg_cost": measures.get("avg_cost", 0.0),
        "m_scaling": measures.get("scaling", 0.0),
        "m_aoe_share": measures.get("aoe_share", 0.0),
        "m_debuff_cards": measures.get("debuff_cards", 0),
        "m_utility_categories": measures.get("utility_categories", 0),
        "m_penalties": measures.get("penalties", 0),
        "m_deck_size": measures.get("deck", 0),
        "m_curses": measures.get("curses", 0),
        "m_hp": measures.get("hp", 0),
        "m_max_hp": measures.get("max_hp", 0),
        "m_heal_per_combat": measures.get("heal_per_combat", 0.0),
        "m_worst_hit": measures.get("worst_hit", 0),
        "m_boss_hp": measures.get("boss_hp", 0),
    }


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(
        description="Construit les features de calibration des poids de G depuis l'index de runs.")
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    parser.add_argument("--index", default=os.path.join(root, "data", "index", "runs-v0.107.1.jsonl.gz"))
    parser.add_argument("--catalog", default=os.path.join(root, "data", "catalog"))
    parser.add_argument("--out", required=True, help="JSONL de features")
    parser.add_argument("--state", default="final", choices=("final", "act", "both"),
                        help="final = deck de fin de run ; act = une ligne par acte (reconstruction "
                             "arrière) ; both = les deux en un seul passage (marqueur `kind`)")
    parser.add_argument("--stride", type=int, default=1, help="un run gardé sur N")
    parser.add_argument("--limit", type=int, default=0, help="0 = pas de limite")
    parser.add_argument("--mode", default="standard", help="standard | daily | custom | all")
    parser.add_argument("--players", default="solo", help="solo | coop | all")
    parser.add_argument("--max-deck", type=int, default=80,
                        help="écarte les decks > N cartes (runs endless/moddées) ; 0 = aucune borne")
    args = parser.parse_args(argv)

    catalogs = Catalogs(args.catalog)
    seen = kept_runs = kept_rows = skipped = 0
    with open(args.out, "w", encoding="utf-8") as out:
        for raw in iter_index(args.index, stride=max(1, args.stride)):
            seen += 1
            if args.mode != "all" and raw.get("m") != args.mode:
                skipped += 1
                continue
            n_players = raw.get("n") or len(raw.get("pl") or ())
            if args.players == "solo" and n_players != 1:
                skipped += 1
                continue
            if args.players == "coop" and n_players <= 1:
                skipped += 1
                continue
            deck = (raw.get("pl") or [{}])[0].get("deck") or ()
            if args.max_deck and len(deck) > args.max_deck:
                skipped += 1
                continue
            if args.state == "both":
                rows = build_run_features(raw, catalogs, "act")
                for row in rows:
                    row["kind"] = "act"
                rows += [dict(row, kind="final")
                         for row in build_run_features(raw, catalogs, "final")]
            else:
                rows = build_run_features(raw, catalogs, args.state)
                for row in rows:
                    row["kind"] = args.state
            if not rows:
                skipped += 1
                continue
            for row in rows:
                out.write(json.dumps(row, separators=(",", ":")) + "\n")
                kept_rows += 1
            kept_runs += 1
            if args.limit and kept_runs >= args.limit:
                break
    print(f"runs lus       : {seen}")
    print(f"runs retenus   : {kept_runs} (écartés : {skipped})")
    print(f"lignes features: {kept_rows}")
    print(f"sortie         : {args.out} ({os.path.getsize(args.out) / 1e6:.1f} Mo)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
