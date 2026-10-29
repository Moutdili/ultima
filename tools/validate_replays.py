#!/usr/bin/env python3
"""Valide les chiffres du moteur contre les combats réels des replays.

Trois contrôles, chacun avec son dénominateur explicite :

  A. MENACE — `ThreatModel.MaxHit(rencontre, ascension)` (pire tour simultané) comparé aux
     dégâts réellement portés au tour 1. MaxHit est un **majorant**, pas une rotation : le
     contrôle attend donc `observé ≤ MaxHit` et mesure combien de fois le monstre a justement
     joué son pire coup.
  B. CATALOGUE DE MONSTRES — chaque attaque résolue est comparée au move du catalogue
     (`damage.normal` / `damage.ascension` × `hit_count`) et à l'intention que le jeu a
     affichée (`intent.dmg` × `intent.hits`). C'est le contrôle le plus net : il isole une
     valeur de move, sans agrégation.
  C. CARTES — `CardEffects.Damage(id, upgrade)` × nombre de coups comparé aux dégâts bruts
     observés (`hit.dmg + hit.blocked + hit.overkill`). Les écarts sont classés : plafonnement
     par les PV restants (overkill), Force (STRENGTH_POWER), Faiblesse/Vulnérable, carte à
     dégâts dynamiques (le codex porte `CalculationBase`/`ExtraDamage`), carte absente du
     catalogue, ou inexpliqué.

Le calcul de MaxHit est ré-implémenté ici (règle de `ThreatModel.cs` : somme des pires coups de
chaque monstre, variante ascension si ascension > 0, `hit_count` inclus) pour que le script soit
reproductible sans .NET. `--engine-truth <dump.json>` compare cette réplique au moteur C# réel sur
les 87 rencontres × 21 ascensions ; le rapport affiche le nombre de valeurs identiques.

Usage :
    python3 -I tools/validate_replays.py
    python3 -I tools/validate_replays.py --engine-truth /tmp/engine_truth.json
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import sys
from collections import Counter, defaultdict

# Cartes dont les dégâts ne sont pas une constante du catalogue (calculés en jeu).
DYNAMIC_MARKERS = ("CalculationBase", "ExtraDamage", "CalculationExtra", "CalculatedDamage", "Repeat")


# ---------------------------------------------------------------- catalogue / réplique

def norm_id(value) -> str:
    """Même normalisation que `ThreatModel.NormalizeId` : sans « ENCOUNTER. », alphanumérique majuscule."""
    if not value:
        return ""
    text = str(value).strip()
    if text.upper().startswith("ENCOUNTER."):
        text = text[len("ENCOUNTER."):]
    return "".join(c for c in text if c.isalnum()).upper()


def load_catalog(catalog_dir: str) -> dict:
    monsters = {}
    with open(os.path.join(catalog_dir, "monsters.json"), encoding="utf-8") as f:
        raw = json.load(f)
    for m in raw:
        if not isinstance(m, dict) or not m.get("id"):
            continue
        moves = {}
        for mv in m.get("moves") or []:
            if not isinstance(mv, dict):
                continue
            mid = mv.get("id") or mv.get("state")
            if not mid:
                continue
            dmg = mv.get("damage") or {}
            hits = dmg.get("hit_count")
            if not hits or hits <= 1:
                hits = legacy_repeats(mv) or (hits or 1)
            moves[str(mid).upper()] = {
                "id": str(mid).upper(),
                "normal": dmg.get("normal"),
                "ascension": dmg.get("ascension"),
                "hits": max(1, int(hits)),
                "intent": mv.get("intent_codex") or mv.get("intent"),
            }
        entry = {"id": str(m["id"]).upper(), "class": m.get("class"),
                 "moves": moves, "hp": hp_of(m)}
        monsters[entry["id"]] = entry
        if m.get("class"):
            monsters[str(m["class"]).upper()] = entry

    encounters = {}
    with open(os.path.join(catalog_dir, "encounters.json"), encoding="utf-8") as f:
        raw = json.load(f)
    for e in raw.get("encounters") or []:
        if isinstance(e, dict) and e.get("id"):
            encounters[norm_id(e["id"])] = {
                "id": e["id"], "monsters": [str(x).upper() for x in (e.get("monsters") or [])],
                "act": e.get("act"), "roomType": e.get("room_type"),
            }
    return {"monsters": monsters, "encounters": encounters}


def legacy_repeats(move: dict):
    values = move.get("values")
    if not isinstance(values, list) or len(values) < 2 or not isinstance(values[1], list):
        return None
    for v in values[1]:
        if isinstance(v, int) and v > 0:
            return v
    return None


def hp_of(monster: dict):
    hp = monster.get("min_hp")
    if isinstance(hp, int):
        return hp
    if isinstance(hp, dict) and isinstance(hp.get("values"), list):
        nums = [v for v in hp["values"] if isinstance(v, int)]
        return min(nums) if nums else None
    return None


def move_damage(move: dict, ascension: int):
    """`MoveThreat.DamageAt` : variante ascension si ascension > 0, repli sur l'autre."""
    if ascension > 0:
        return move["ascension"] if move["ascension"] is not None else move["normal"]
    return move["normal"] if move["normal"] is not None else move["ascension"]


def replica_max_hit(catalog: dict, encounter_id: str, ascension: int):
    enc = catalog["encounters"].get(norm_id(encounter_id))
    if enc is None:
        return None
    total = 0
    worst = None
    for mid in enc["monsters"]:
        monster = catalog["monsters"].get(mid.upper())
        if monster is None:
            continue
        best = 0
        best_move = None
        for move in monster["moves"].values():
            d = move_damage(move, ascension)
            if d is None:
                continue
            hit = d * move["hits"]
            if hit > best:
                best, best_move = hit, move["id"]
        total += best
        if best_move and (worst is None or best > worst[2]):
            worst = (monster["id"], best_move, best)
    return {"damage": total, "worst": worst}


# ---------------------------------------------------------------- replays

def load_events(path: str) -> list[dict]:
    with open(path, encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def combat_blocks(events: list[dict]):
    """Découpe en combats : (start, list_of_events, end)."""
    cur = None
    for e in events:
        t = e.get("t")
        if t == "combat_start":
            if cur:
                yield cur[0], cur[1], None
            cur = (e, [])
        if cur is not None:
            cur[1].append(e)
        if t == "combat_end" and cur is not None:
            yield cur[0], cur[1], e
            cur = None
    if cur:
        yield cur[0], cur[1], None


def turn_sequence(events: list[dict]):
    """Liste des tours : (round, side, events)."""
    turns = []
    cur = None
    for e in events:
        t = e.get("t")
        if t == "turn":
            cur = {"round": e.get("round"), "side": e.get("side"), "events": []}
            turns.append(cur)
        elif t == "end_turn" and cur is not None:
            cur["events"].append(e)
            cur = None
        elif cur is not None:
            cur["events"].append(e)
    return turns


# ---------------------------------------------------------------- contrôles

def instance_max_hit(catalog, enemies, ascension: int):
    """Pire tour calculé sur les monstres RÉELLEMENT présents (cid distincts).

    `ThreatModel.MaxHit` somme les monstres listés par la rencontre, une fois chacun. Or un
    combat peut contenir 2 à 4 copies du même monstre (`EXOSKELETONS_NORMAL` → 4 exosquelettes) :
    la somme par instance est le seul majorant correct.
    """
    total = 0
    seen = set()
    for e in enemies:
        cid = e.get("cid")
        if cid in seen:
            continue
        seen.add(cid)
        monster = catalog["monsters"].get(str(e.get("id") or "").upper())
        if not monster:
            continue
        best = 0
        for move in monster["moves"].values():
            d = move_damage(move, ascension)
            if d is None:
                continue
            best = max(best, d * move["hits"])
        total += best
    return total


def check_max_hit(catalog, replay_runs, samples: int = 5):
    """A. MaxHit vs dégâts observés au tour 1 (premier tour ennemi)."""
    rows = []
    for run in replay_runs:
        for start, events, end in combat_blocks(run["events"]):
            enc = start.get("encounter")
            asc = run["ascension"] or 0
            ref = replica_max_hit(catalog, enc, asc) if enc else None
            if not ref:
                continue
            turns = turn_sequence(events)
            enemy_turns = [t for t in turns if t["side"] == "enemy"]
            if not enemy_turns:
                continue
            first = enemy_turns[0]
            observed = 0
            moves = []
            moves_per_src = Counter()
            for e in first["events"]:
                if e.get("t") == "hit" and e.get("dst") == "player":
                    observed += (e.get("dmg") or 0) + (e.get("blocked") or 0)
            for e in first["events"]:
                if e.get("t") == "move" and e.get("src") and e.get("dest") is None:
                    moves.append((e.get("src"), e.get("id")))
                    moves_per_src[e.get("src_cid") or e.get("src")] += 1
            instances = len({e.get("cid") for e in start.get("enemies") or []})
            catalog_instances = len({str(m).upper() for m in
                                     (catalog["encounters"].get(norm_id(enc)) or {}).get("monsters", [])})
            rows.append({
                "hash": run["hash"], "floor": start.get("floor"), "encounter": enc,
                "ascension": asc, "observed": observed, "maxHit": ref["damage"],
                "instanceMaxHit": instance_max_hit(catalog, start.get("enemies") or [], asc),
                "instances": instances, "catalogInstances": catalog_instances,
                "worst": ref["worst"], "moves": moves,
                "maxMovesPerMonster": max(moves_per_src.values()) if moves_per_src else 0,
                "ratio": round(observed / ref["damage"], 3) if ref["damage"] else None,
            })

    over = [r for r in rows if r["observed"] > r["maxHit"]]
    over_instance = [r for r in rows if r["observed"] > r["instanceMaxHit"]]
    equal = [r for r in rows if r["observed"] == r["maxHit"]]
    duplicates = [r for r in rows if r["instances"] > r["catalogInstances"]]
    chained = [r for r in rows if r.get("maxMovesPerMonster", 0) > 1]
    unknown_moves = [m for r in rows for m in r["moves"]
                     if (m[0] or "").upper() not in {k for k in catalog["monsters"]}]
    # échantillon lisible : égalités d'abord (le monstre a joué son pire coup), puis un cas où
    # MaxHit est dépassé, puis des cas « sous le maximum » — un par rencontre.
    pick = []
    seen = set()

    def take(candidates, quota):
        added = 0
        for r in sorted(candidates, key=lambda r: (r["encounter"] or "", r["floor"] or 0)):
            if added >= quota or len(pick) >= samples:
                return
            if r["encounter"] in seen:
                continue
            seen.add(r["encounter"])
            pick.append(r)
            added += 1

    take([r for r in rows if r["observed"] == r["maxHit"]], 2)
    take([r for r in rows if r["observed"] > r["maxHit"]], 1)
    take([r for r in rows if r["observed"] < r["maxHit"]], 2)
    return {"rows": rows, "samples": pick, "exceed": over, "exceedInstance": over_instance,
            "equal": equal, "duplicates": duplicates, "chained": chained,
            "unknownMoveSources": unknown_moves}


def measure_ascension_model(catalog, replay_runs):
    """Quel barème d'ascension le jeu applique-t-il réellement, par niveau ?

    Le catalogue ne connaît que deux valeurs (`normal`, `ascension`) et `ThreatModel.DamageAt`
    prend la variante ascension dès `ascension > 0`. Les replays, eux, donnent l'intention
    chiffrée du jeu. On compte, par niveau d'ascension, combien d'attaques non modifiées
    tombent sur `normal` ou sur `ascension` : majorité = barème utilisé.
    """
    tally = defaultdict(Counter)
    for run in replay_runs:
        asc = run["ascension"] or 0
        for start, events, end in combat_blocks(run["events"]):
            prev = {}
            for t in turn_sequence(events):
                if t["side"] == "player":
                    prev = {(e.get("src_cid") or e.get("src")): e for e in t["events"]
                            if e.get("t") == "intent" and e.get("src") and e.get("dmg") is not None}
                    continue
                died = any(e.get("t") == "death" and e.get("tgt") == "player" for e in t["events"]) or \
                       any(e.get("t") == "hp" and e.get("dst") == "player" and e.get("hp") == 0
                           for e in t["events"])
                grouped = {}
                for e in t["events"]:
                    if e.get("t") == "hit" and e.get("dst") == "player":
                        grouped.setdefault(e.get("src_cid") or e.get("src"), []).append(e)
                for key, hits in grouped.items():
                    src = hits[0].get("src")
                    intent = prev.get(key)
                    if not intent or any(h.get("mods") for h in hits):
                        continue
                    monster = catalog["monsters"].get((src or "").upper())
                    if not monster:
                        continue
                    mid = str(intent.get("id") or "").upper()
                    move = monster["moves"].get(mid) or monster["moves"].get(mid.replace("_MOVE", ""))
                    if not move or move["normal"] == move["ascension"] or move["normal"] is None:
                        continue
                    value = (intent.get("dmg") or 0) * (intent.get("hits") or 1)
                    if value == move["normal"]:
                        tally[asc]["normal"] += 1
                    elif value == move["ascension"]:
                        tally[asc]["ascension"] += 1
    model = {}
    for asc, counts in tally.items():
        model[asc] = "ascension" if counts["ascension"] > counts["normal"] else "normal"
    return model, {a: dict(c) for a, c in sorted(tally.items())}


def check_monster_moves(catalog, replay_runs, asc_model=None):
    """B. Move du catalogue vs intention du jeu vs dégâts réellement portés.

    Une « attaque » = un monstre dans un tour ennemi ; ses coups multiples sont sommés
    (sinon un move à 3 coups compterait comme 3 attaques comparées à un seul coup).
    Deux barèmes sont comparés : la règle du moteur (`ascension > 0` ⇒ variante ascension) et
    le barème réellement observé niveau par niveau (`asc_model`).
    """
    stat = Counter()
    diffs = Counter()
    by_move = defaultdict(Counter)
    unexplained = []
    for run in replay_runs:
        asc = run["ascension"] or 0
        measured_variant = (asc_model or {}).get(asc) if asc_model else None
        for start, events, end in combat_blocks(run["events"]):
            turns = turn_sequence(events)
            prev_intents = {}
            for t in turns:
                if t["side"] == "player":
                    prev_intents = {}
                    for e in t["events"]:
                        if e.get("t") == "intent" and e.get("src") and e.get("id"):
                            prev_intents[e.get("src_cid") or e.get("src")] = e
                    continue
                died = any(e.get("t") == "death" and e.get("tgt") == "player" for e in t["events"]) or \
                       any(e.get("t") == "hp" and e.get("dst") == "player" and e.get("hp") == 0
                           for e in t["events"])
                grouped = {}
                for e in t["events"]:
                    if e.get("t") == "hit" and e.get("dst") == "player":
                        grouped.setdefault(e.get("src_cid") or e.get("src"), []).append(e)
                for key, hits in grouped.items():
                    src = hits[0].get("src")
                    monster = catalog["monsters"].get((src or "").upper())
                    if monster is None:
                        stat["monstre_inconnu"] += 1
                        continue
                    intent = prev_intents.get(key)
                    if intent is None:
                        stat["sans_intention"] += 1
                        continue
                    mid = str(intent.get("id") or "").upper()
                    move = monster["moves"].get(mid) or monster["moves"].get(mid.replace("_MOVE", ""))
                    if move is None:
                        stat["move_inconnu"] += 1
                        continue
                    catalog_dmg = move_damage(move, asc)
                    if catalog_dmg is None:
                        stat["move_sans_degats"] += 1
                        continue
                    observed = sum((h.get("dmg") or 0) + (h.get("blocked") or 0) for h in hits)
                    over = sum(h.get("overkill") or 0 for h in hits if h.get("killed"))
                    observed += over
                    mods = set()
                    for h in hits:
                        mods |= set(h.get("mods") or ())
                    stat["attaques"] += 1
                    if len(hits) != move.get("hits", 1):
                        stat["coups_differents"] += 1
                    expected = catalog_dmg * move.get("hits", 1)
                    key = "modifie" if mods else "brut"
                    stat[key] += 1
                    d = observed - expected
                    diffs[(key, d)] += 1
                    by_move[move["id"]][d] += 1
                    if key == "brut":
                        if d == 0:
                            stat["brut_exact_regle_moteur"] += 1
                        if measured_variant and move.get(measured_variant) is not None:
                            measured = move[measured_variant] * move.get("hits", 1)
                            if measured == observed:
                                stat["brut_exact_bareme_mesure"] += 1
                            elif observed < measured:
                                stat["brut_tronque_bareme_mesure"] += 1
                                if died:
                                    stat["brut_tronque_avec_mort"] += 1
                            else:
                                stat["brut_superieur_bareme_mesure"] += 1
                    # intention affichée par le jeu, quand elle est chiffrée
                    if intent.get("dmg") is not None:
                        it = (intent.get("dmg") or 0) * (intent.get("hits") or 1)
                        stat["intention_comparee"] += 1
                        if it == observed:
                            stat["intention_exacte"] += 1
                        else:
                            stat["intention_ecart"] += 1
                            if len(unexplained) < 8:
                                unexplained.append({"kind": "intent", "src": src, "move": mid,
                                                    "asc": asc, "intent": it, "observed": observed,
                                                    "mods": sorted(mods)})
                    if d != 0 and not mods and len(unexplained) < 8:
                        unexplained.append({"kind": "catalog", "src": src, "move": mid, "asc": asc,
                                            "catalog": expected, "observed": observed,
                                            "coups": len(hits), "hitsAttendus": move.get("hits", 1)})
    return {"stat": dict(stat), "diffs": diffs, "byMove": by_move, "unexplained": unexplained}


def card_is_dynamic(cards_raw: dict, cid: str) -> bool:
    raw = cards_raw.get(cid)
    if not raw:
        return False
    variables = raw.get("vars") or {}
    if any(k in variables for k in DYNAMIC_MARKERS):
        return True
    desc = (raw.get("description") or "").lower()
    return "less damage" in desc or "add its damage" in desc or "for each" in desc


def check_cards(catalog, replay_runs, cards_raw, effects):
    """C. CardEffects.Damage vs dégâts bruts observés, écarts classés.

    Deux populations séparées, parce qu'elles ne disent pas la même chose :
      · `comparables`  — aucun pouvoir n'a modifié le coup : l'écart ne peut venir que de la
        carte ou du plafonnement par les PV restants. C'est le taux à citer.
      · `avec_modificateur` — Force/Faiblesse/Vulnérable : on recalcule la valeur attendue
        avec les piles suivies depuis les événements `power` et on dit si la formule tombe juste.
    """
    stat = Counter()
    deltas = Counter()
    by_card = defaultdict(Counter)
    examples = defaultdict(list)
    for run in replay_runs:
        state = {"strength": 0, "weakPlayer": 0, "vuln": defaultdict(int), "weakMonster": defaultdict(int)}
        character = str(run["character"] or "").replace("CHARACTER.", "").upper()
        for start, events, end in combat_blocks(run["events"]):
            cur = None
            for e in events:
                t = e.get("t")
                if t in ("power", "power_lost"):
                    pid = e.get("id")
                    tgt_cid = e.get("tgt_cid")
                    tgt = str(e.get("tgt") or "").upper()
                    amount = e.get("amount")
                    if amount is None:
                        amount = None
                    sign = -1 if t == "power_lost" else 1
                    val = amount if amount is not None else None
                    if pid == "STRENGTH_POWER" and tgt in (character, "PLAYER"):
                        state["strength"] = val if val is not None else state["strength"] + sign * (e.get("n") or 0)
                    elif pid == "WEAK_POWER" and tgt in (character, "PLAYER"):
                        state["weakPlayer"] = val if val is not None else max(0, state["weakPlayer"] + sign * (e.get("n") or 0))
                    elif pid == "VULNERABLE_POWER" and tgt_cid is not None:
                        state["vuln"][tgt_cid] = val if val is not None else max(0, state["vuln"][tgt_cid] + sign * (e.get("n") or 0))
                    elif pid == "WEAK_POWER" and tgt_cid is not None:
                        state["weakMonster"][tgt_cid] = val if val is not None else max(0, state["weakMonster"][tgt_cid] + sign * (e.get("n") or 0))
                    continue
                if t == "play":
                    if cur is not None:
                        evaluate_card(cur, run, cards_raw, effects, state, stat, deltas, by_card, examples)
                    cur = {"play": e, "hits": [], "strength": state["strength"],
                           "weakPlayer": state["weakPlayer"], "vuln": dict(state["vuln"])}
                    continue
                if t == "hit" and cur is not None and e.get("card") == cur["play"].get("id"):
                    cur["hits"].append(e)
                    continue
                if t in ("turn", "end_turn", "combat_end"):
                    if cur is not None:
                        evaluate_card(cur, run, cards_raw, effects, state, stat, deltas, by_card, examples)
                    cur = None
            if cur is not None:
                evaluate_card(cur, run, cards_raw, effects, state, stat, deltas, by_card, examples)
    return {"stat": dict(stat), "deltas": deltas, "byCard": by_card, "examples": examples}


def evaluate_card(g, run, cards_raw, effects, state, stat, deltas, by_card, examples):
    if not g["hits"]:
        return
    cid = g["play"].get("id")
    effect = effects.get(cid)
    up = 1 if (g["play"].get("up") or 0) else 0
    if effect is None:
        stat["carte_absente_du_catalogue"] += 1
        by_card[cid]["absente"] += 1
        return
    base = effect["damage_up"] if up else effect["damage_base"]
    if base is None:
        stat["carte_sans_degats_fixes"] += 1
        return
    if effect.get("x_cost"):
        stat["carte_X"] += 1
        return
    if card_is_dynamic(cards_raw, cid):
        stat["carte_dynamique"] += 1
        by_card[cid]["dynamique"] += 1
        return
    if any(h.get("src") != "player" for h in g["hits"]):
        stat["source_non_joueur"] += 1
        return

    hits = len(g["hits"])
    got = sum((h.get("dmg") or 0) + (h.get("blocked") or 0) for h in g["hits"])
    over = sum(h.get("overkill") or 0 for h in g["hits"] if h.get("killed"))
    observed = got + over
    mods = set()
    for h in g["hits"]:
        mods |= set(h.get("mods") or ())

    # valeur attendue avec les piles suivies (Force, Faiblesse du joueur, Vulnérable de la cible)
    per_hit = base + g["strength"]
    target_cid = g["play"].get("target_cid") or (g["hits"][0].get("dst_cid") if g["hits"] else None)
    if target_cid is not None and g["vuln"].get(target_cid, 0) > 0:
        per_hit = int(per_hit * 1.5)
    if g["weakPlayer"] > 0:
        per_hit = int(per_hit * 0.75)
    expected_modified = per_hit * hits
    expected_raw = base * hits

    if not mods:
        stat["comparables"] += 1
        delta = observed - expected_raw
        deltas[("sans_modificateur", delta)] += 1
        if delta == 0:
            stat["exact"] += 1
            if got != expected_raw:
                stat["exact_apres_plafonnement"] += 1
            return
        by_card[cid][f"écart {delta}"] += 1
        stat["ecart"] += 1
        if len(examples[delta]) < 3:
            examples[delta].append({"hash": run["hash"], "card": cid, "up": up, "base": base,
                                    "hits": hits, "expected": expected_raw, "got": got,
                                    "overkill": over, "mods": []})
        return

    stat["avec_modificateur"] += 1
    deltas[("avec_modificateur", observed - expected_modified)] += 1
    if observed == expected_modified:
        stat["modificateur_explique"] += 1
        if "STRENGTH_POWER" in mods:
            stat["dont_force"] += 1
        if "VULNERABLE_POWER" in mods:
            stat["dont_vulnerable"] += 1
        if "WEAK_POWER" in mods:
            stat["dont_faiblesse"] += 1
        return
    stat["modificateur_non_explique"] += 1
    by_card[cid]["modificateur non expliqué"] += 1
    if len(examples[observed - expected_modified]) < 2:
        examples[observed - expected_modified].append(
            {"hash": run["hash"], "card": cid, "up": up, "base": base, "hits": hits,
             "expectedModifie": expected_modified, "got": got, "overkill": over, "mods": sorted(mods),
             "strength": g["strength"]})


def check_hp_consistency(replay_runs):
    """D. Contrôle interne : hp_lost_total du NDJSON vs somme des hit.dmg sur le joueur."""
    ok = bad = 0
    rows = []
    for run in replay_runs:
        for start, events, end in combat_blocks(run["events"]):
            if end is None or end.get("hp_lost_total") is None:
                continue
            taken = sum((e.get("dmg") or 0) for e in events
                        if e.get("t") == "hit" and e.get("dst") == "player")
            taken += sum((e.get("dmg") or 0) for e in events
                         if e.get("t") == "hp_loss" and e.get("dst") == "player")
            declared = end["hp_lost_total"]
            if taken == declared:
                ok += 1
            else:
                bad += 1
                if len(rows) < 10:
                    rows.append({"hash": run["hash"], "floor": start.get("floor"),
                                 "declared": declared, "sumHits": taken,
                                 "delta": taken - declared, "players": run["players"]})
    return {"exact": ok, "different": bad, "examples": rows}


# ---------------------------------------------------------------- rapport

def pct(a, b):
    return f"{100.0 * a / b:.1f} %" if b else "—"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Valide les dégâts du moteur contre les replays.")
    ap.add_argument("--replays-dir", default=os.path.join("data", "replays"))
    ap.add_argument("--catalog", default=os.path.join("data", "catalog"))
    ap.add_argument("--codex", default=os.path.join("data", "codex"))
    ap.add_argument("--engine-truth", default=None,
                    help="dump JSON du moteur C# (MaxHit 0..20 et cartes) pour vérifier la réplique")
    ap.add_argument("--out", default=os.path.join("data", "replays", "validation.json"))
    ap.add_argument("--report", default=os.path.join("data", "replays", "validation.md"))
    ap.add_argument("--samples", type=int, default=5)
    args = ap.parse_args(argv)

    catalog = load_catalog(args.catalog)
    effects_raw = json.load(open(os.path.join(args.catalog, "cards_effects.json"), encoding="utf-8"))
    effects = {str(c.get("id")): c for c in effects_raw.get("cards") or [] if c.get("id")}
    cards_raw = {}
    codex_cards = os.path.join(args.codex, "cards.json")
    if os.path.exists(codex_cards):
        raw = json.load(open(codex_cards, encoding="utf-8"))
        items = raw if isinstance(raw, list) else raw.get("cards", [])
        cards_raw = {str(c.get("id")): c for c in items if isinstance(c, dict) and c.get("id")}

    paths = sorted(glob.glob(os.path.join(args.replays_dir, "*.ndjson")))
    if not paths:
        print(f"aucun replay dans {args.replays_dir}", file=sys.stderr)
        return 1
    runs = []
    for p in paths:
        events = load_events(p)
        h = next((e for e in events if e.get("t") == "header"), {})
        runs.append({
            "hash": os.path.basename(p)[: -len(".ndjson")],
            "ascension": h.get("ascension") or 0,
            "character": h.get("character"),
            "players": h.get("player_count") or 1,
            "build": h.get("build_id"),
            "events": events,
        })

    # ---- réplique vs moteur
    engine_check = None
    if args.engine_truth:
        truth = json.load(open(args.engine_truth, encoding="utf-8"))
        same = diff = missing = 0
        mismatches = []
        for eid, enc in truth["encounters"].items():
            for asc in range(0, 21):
                want = (enc.get("maxHit") or {}).get(str(asc))
                got = replica_max_hit(catalog, eid, asc)
                if want is None or got is None:
                    missing += 1
                    continue
                if want == got["damage"]:
                    same += 1
                else:
                    diff += 1
                    if len(mismatches) < 10:
                        mismatches.append({"encounter": eid, "asc": asc, "engine": want, "replica": got["damage"]})
        engine_check = {"same": same, "different": diff, "missing": missing,
                        "encounters": len(truth["encounters"]), "mismatches": mismatches,
                        "monsters": truth.get("monsterCount"), "cards": truth.get("cardCount")}

    a = check_max_hit(catalog, runs, args.samples)
    asc_model, asc_tally = measure_ascension_model(catalog, runs)
    b = check_monster_moves(catalog, runs, asc_model)
    c = check_cards(catalog, runs, cards_raw, effects)
    d = check_hp_consistency(runs)

    # ---- verdicts courts
    verdicts = []
    rows = a["rows"]
    verdicts.append(f"A. `MaxHit(rencontre)` ≥ observé : {len(rows) - len(a['exceed'])}/{len(rows)} "
                    f"({pct(len(rows) - len(a['exceed']), len(rows))}) ; au niveau des instances "
                    f"réellement présentes : {len(rows) - len(a['exceedInstance'])}/{len(rows)} "
                    f"({pct(len(rows) - len(a['exceedInstance']), len(rows))}) ; "
                    f"égalité exacte au pire coup : {len(a['equal'])}/{len(rows)} ; "
                    f"{len(a['duplicates'])} combats contiennent plus de copies que la rencontre n'en liste.")
    mb = b["stat"]
    brut = mb.get("brut", 0)
    verdicts.append(f"B. Move du catalogue vs attaque observée sans modificateur : "
                    f"{mb.get('brut_exact_regle_moteur', 0)}/{brut} "
                    f"({pct(mb.get('brut_exact_regle_moteur', 0), brut)}) avec la règle du moteur "
                    f"(ascension > 0 ⇒ variante ascension) ; "
                    f"{mb.get('brut_exact_bareme_mesure', 0)}/{brut} "
                    f"({pct(mb.get('brut_exact_bareme_mesure', 0), brut)}) avec le barème mesuré ; "
                    f"reste {mb.get('brut_tronque_bareme_mesure', 0)} attaques tronquées par la mort du "
                    f"joueur (l'excédent n'est pas journalisé), {mb.get('brut_superieur_bareme_mesure', 0)} "
                    f"au-dessus du catalogue. Intention du jeu vs observé : {mb.get('intention_exacte', 0)}/"
                    f"{mb.get('intention_comparee', 0)} "
                    f"({pct(mb.get('intention_exacte', 0), mb.get('intention_comparee', 0))}) ; "
                    f"{mb.get('coups_differents', 0)}/{mb.get('attaques', 0)} attaques n'ont pas le nombre "
                    f"de coups annoncé par `hit_count`.")
    cs = c["stat"]
    verdicts.append(f"C. CardEffects.Damage, coups non modifiés (plafonnement par les PV déduit) : "
                    f"{cs.get('exact', 0)}/{cs.get('comparables', 0)} "
                    f"({pct(cs.get('exact', 0), cs.get('comparables', 0))}) ; dont "
                    f"{cs.get('exact_apres_plafonnement', 0)} exacts seulement après ajout de l'overkill. "
                    f"Coups modifiés par un pouvoir : {cs.get('modificateur_explique', 0)}/"
                    f"{cs.get('avec_modificateur', 0)} retrouvés par Force/Faiblesse/Vulnérable "
                    f"({pct(cs.get('modificateur_explique', 0), cs.get('avec_modificateur', 0))}). "
                    f"Non comparables : {cs.get('carte_dynamique', 0)} dynamiques, "
                    f"{cs.get('carte_absente_du_catalogue', 0)} absentes du catalogue, "
                    f"{cs.get('source_non_joueur', 0)} coups de sbires, "
                    f"{cs.get('carte_X', 0)} X-cost.")
    verdicts.append(f"D. Cohérence interne (hp_lost_total vs somme des hit.dmg) : {d['exact']}/"
                    f"{d['exact'] + d['different']} combats identiques.")

    payload = {
        "engineCheck": engine_check,
        "verdicts": verdicts,
        "ascensionModel": {"model": asc_model, "tally": asc_tally},
        "A_maxHit": {k: a[k] for k in ("samples", "rows", "exceed", "exceedInstance", "equal", "duplicates", "chained")},
        "B_monsterMoves": {"stat": b["stat"],
                           "deltasRaw": {f"{k[0]}:{k[1]}": v for k, v in b["diffs"].items()},
                           "byMove": {k: dict(v) for k, v in b["byMove"].items()},
                           "unexplained": b["unexplained"]},
        "C_cards": {"stat": c["stat"],
                    "deltas": {f"{k[0]}:{k[1]}": v for k, v in c["deltas"].items()},
                    "byCard": {k: dict(v) for k, v in c["byCard"].items()},
                    "examples": {str(k): v for k, v in c["examples"].items()}},
        "D_hpConsistency": d,
        "totals": {"runs": len(runs), "events": sum(len(r["events"]) for r in runs)},
    }
    with open(args.out + ".part", "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)
        f.write("\n")
    os.replace(args.out + ".part", args.out)

    lines = ["# Validation des dégâts — replays vs moteur", "",
             f"{len(runs)} replays, {payload['totals']['events']} événements.", ""]
    if engine_check:
        lines.append(f"Réplique Python de `ThreatModel.MaxHit` vs dump du moteur C# : "
                     f"{engine_check['same']}/{engine_check['same'] + engine_check['different']} "
                     f"valeurs identiques sur {engine_check['encounters']} rencontres × 21 ascensions.")
        lines.append("")
    for v in verdicts:
        lines.append(f"- {v}")
    lines += ["", "## Barème d'ascension réellement appliqué (mesuré sur les intentions)", "",
              "| ascension | = valeur normale | = valeur ascension | barème retenu |",
              "|---|---|---|---|"]
    for asc in sorted(asc_tally):
        t = asc_tally[asc]
        lines.append(f"| A{asc} | {t.get('normal', 0)} | {t.get('ascension', 0)} | {asc_model.get(asc)} |")
    lines += ["", "## A. Échantillon MaxHit vs tour 1", "",
              "| run | étage | rencontre | asc | observé T1 | MaxHit | pire move | verdict |",
              "|---|---|---|---|---|---|---|---|"]
    for r in a["samples"]:
        w = r["worst"][1] if r["worst"] else "—"
        verdict = "égal" if r["observed"] == r["maxHit"] else ("≤" if r["observed"] <= r["maxHit"] else "DÉPASSE")
        lines.append(f"| {r['hash'][:8]} | {r['floor']} | {r['encounter']} | {r['ascension']} | "
                     f"{r['observed']} | {r['maxHit']} | {w} | {verdict} |")
    lines += ["", "## C. Écarts de cartes", ""]
    for cid, cnt in sorted(c["byCard"].items(), key=lambda kv: -sum(kv[1].values()))[:12]:
        lines.append(f"- `{cid}` : {dict(cnt)}")
    lines += ["", "## C. Exemples d'écarts (les plus fréquents)", ""]
    ranked = sorted(c["examples"].items(), key=lambda kv: -sum(cnt for cnt in c["deltas"].values() if False) - len(kv[1]))
    shown = 0
    for delta, ex in ranked:
        if shown >= 12:
            break
        for e in ex[:1]:
            shown += 1
            lines.append(f"- écart {delta} : `{e['card']}` up={e['up']} base={e['base']} "
                         f"×{e['hits']} attendu={e.get('expected', e.get('expectedModifie'))} "
                         f"observé={e['got']}+{e['overkill']} mods={e['mods']}")
    lines += ["", "## B. Écarts de moves de monstres (sans modificateur)", ""]
    for mid, cnt in sorted(b["byMove"].items(), key=lambda kv: -sum(kv[1].values()))[:8]:
        lines.append(f"- `{mid}` : {dict(cnt)}")
    with open(args.report, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    print("\n".join(verdicts))
    print(f"\n→ {args.out} + {args.report}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
