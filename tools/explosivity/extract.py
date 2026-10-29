#!/usr/bin/env python3
"""Extrait l'empreinte d'une run : états étage par étage, courbe de puissance, explosivité.

Découverte clé : chaque étage porte `rooms[]` avec `model_id` (rencontre exacte), `monster_ids`
(doublons inclus) et **`turns_taken`** — la durée réelle du combat. On peut donc mesurer
l'explosivité (vitesse + dégâts économisés) et la comparer aux moyennes du corpus.

Sortie : data/explosivity/runs.jsonl.gz, une ligne par run :
  identité, courbe de puissance (p0, p_end, pente, courbure), explosivité par acte,
  et l'état final (deck, reliques, PV) — l'empreinte qui doit permettre de retrouver la run.

Usage :
  python3 -I tools/explosivity/extract.py --limit 20000 --wins-only
  python3 -I tools/explosivity/extract.py --limit 20000 --losses-only --out data/explosivity/losses.jsonl.gz
"""
import argparse
import gzip
import json
import math
import os
import sys
import time
from collections import Counter

CARDS = "data/catalog/cards_effects.json"
ENCOUNTERS = "data/catalog/encounters.json"


def bare(value, prefix):
    if not value:
        return ""
    v = value.strip().upper()
    p = prefix + "."
    return v[len(p):] if v.startswith(p) else v


def load_effects():
    with open(CARDS, encoding="utf-8") as f:
        data = json.load(f)
    cards = data["cards"] if isinstance(data, dict) and "cards" in data else data
    out = {}
    for c in cards:
        cid = bare(c.get("id"), "CARD")
        damage = c.get("damage_base") or 0
        block = c.get("block_base") or 0
        draw = c.get("draw_base") or 0
        energy = c.get("energy_base") or 0
        hits = c.get("hits") or 1
        # Puissance d'une carte : dégâts + bloc + valeur de la pioche/énergie.
        out[cid] = damage * max(1, hits) + block + 2 * draw + 3 * energy
    return out


def load_encounters():
    with open(ENCOUNTERS, encoding="utf-8") as f:
        data = json.load(f)
    items = data["encounters"] if isinstance(data, dict) else data
    return {e["id"]: e.get("stats") or {} for e in items}


def fit_curve(values):
    """Pente et courbure (moindres carrés) de la puissance par étage global."""
    n = len(values)
    if n < 3:
        return 0.0, 0.0
    xs = list(range(n))
    mx = sum(xs) / n
    my = sum(values) / n
    sxx = sum((x - mx) ** 2 for x in xs)
    if sxx == 0:
        return 0.0, 0.0
    sxy = sum((x - mx) * (y - my) for x, y in zip(xs, values))
    slope = sxy / sxx
    # courbure : régression quadratique centrée
    sxxxx = sum((x - mx) ** 4 for x in xs)
    sx2y = sum(((x - mx) ** 2 - sxx / n) * (y - my) for x, y in zip(xs, values))
    curv = sx2y / sxxxx if sxxxx else 0.0
    return slope, curv


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", default="data/exports/runs-v0.107.1.jsonl.gz")
    ap.add_argument("--out", default="data/explosivity/runs.jsonl.gz")
    ap.add_argument("--limit", type=int, default=20000)
    ap.add_argument("--wins-only", action="store_true")
    ap.add_argument("--losses-only", action="store_true")
    args = ap.parse_args()

    effects = load_effects()
    stats = load_encounters()
    os.makedirs(os.path.dirname(args.out), exist_ok=True)

    started = time.time()
    seen = kept = 0
    fights = 0
    with gzip.open(args.export, "rt", encoding="utf-8") as src, \
         gzip.open(args.out, "wt", encoding="utf-8", compresslevel=6) as dst:
        for line in src:
            seen += 1
            try:
                d = json.loads(line)
            except json.JSONDecodeError:
                continue
            win = bool(d.get("win"))
            if args.wins_only and not win:
                continue
            if args.losses_only and win:
                continue

            players = d.get("players") or []
            if not players:
                continue
            hero = players[0]
            deck = []
            power_curve = []
            acts = [{"turns": 0, "exp_turns": 0.0, "dmg": 0, "exp_dmg": 0.0, "fights": 0} for _ in range(3)]
            # À A10 (DoubleBoss), l'acte 3 comporte DEUX boss : on garde la liste, pas le dernier.
            bosses: list[list[str]] = [[], [], []]
            fights_detail: list[list] = []
            decisions: list[list] = []
            total_fights = 0

            for act_i, act in enumerate(d.get("map_point_history") or []):
                floors = act if isinstance(act, list) else [act]
                for fl_i, entry in enumerate(floors):
                    if not isinstance(entry, dict):
                        continue
                    ps_list = entry.get("player_stats") or []
                    if not ps_list:
                        continue
                    ps = ps_list[0]
                    for gained in (ps.get("cards_gained") or []):
                        cid = bare(gained.get("id"), "CARD")
                        if cid:
                            deck.append(cid)
                            decisions.append([act_i, fl_i, cid])
                    # Puissance cumulée du deck (somme, pas moyenne) : la moyenne baisse
                    # mécaniquement quand on ajoute des cartes faibles, la somme non — c'est
                    # elle qui porte la croissance.
                    power_curve.append(sum(effects.get(c, 0) for c in deck))

                    room = (entry.get("rooms") or [{}])[0]
                    enc = bare(room.get("model_id"), "ENCOUNTER")
                    turns = room.get("turns_taken")
                    room_type = room.get("room_type") or entry.get("map_point_type")
                    if room_type == "boss" or "BOSS" in enc:
                        bosses[min(act_i, 2)].append(enc)
                    dmg = ps.get("damage_taken") or 0
                    exp = stats.get(enc) or {}
                    if room_type in ("monster", "elite", "boss") and turns:
                        fights_detail.append([act_i, fl_i, enc, turns, dmg, len(deck)])
                        total_fights += 1
                        fights += 1
                        idx = min(act_i, 2)
                        acts[idx]["turns"] += turns
                        acts[idx]["dmg"] += dmg
                        acts[idx]["fights"] += 1
                        if exp.get("avg_turns"):
                            acts[idx]["exp_turns"] += exp["avg_turns"]
                        if exp.get("avg_damage"):
                            acts[idx]["exp_dmg"] += exp["avg_damage"]

            if total_fights == 0:
                continue

            slope, curv = fit_curve(power_curve)
            for a in acts:
                a["speed"] = round(a["exp_turns"] / a["turns"], 3) if (a["turns"] and a["exp_turns"]) else None
                a["defense"] = round(a["exp_dmg"] / a["dmg"], 3) if (a["exp_dmg"] and a["dmg"]) else None
                del a["exp_turns"], a["exp_dmg"]

            total_turns = sum(a["turns"] for a in acts)
            row = {
                "h": d.get("run_hash"),
                "c": bare(hero.get("character"), "CHARACTER"),
                "asc": d.get("ascension"),
                "w": int(win),
                "n": len(players),
                "floors": len(power_curve),
                "fights": total_fights,
                "p0": round(power_curve[0], 2) if power_curve else 0,
                "p1": round(power_curve[-1], 2) if power_curve else 0,
                "growth": round(power_curve[-1] / len(power_curve), 3) if power_curve else None,
                "slope": round(slope, 4),
                "curv": round(curv, 5),
                "turn_total": total_turns,
                "acts": acts,
                "bosses": bosses,
                "fd": fights_detail,
                "decs": decisions,
                "final": {
                    "deck": [bare(c.get("id") if isinstance(c, dict) else c, "CARD") for c in (hero.get("deck") or [])],
                    "rel": [bare(c.get("id") if isinstance(c, dict) else c, "RELIC") for c in (hero.get("relics") or [])],
                    "hp": hero.get("current_hp"),
                    "mhp": hero.get("max_hp"),
                },
            }
            dst.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")
            kept += 1
            if args.limit and kept >= args.limit:
                break

    elapsed = time.time() - started
    size = os.path.getsize(args.out) if os.path.exists(args.out) else 0
    print(f"lignes lues : {seen}")
    print(f"runs gardées: {kept} (combats: {fights})")
    print(f"sortie      : {args.out} ({size/1024/1024:.1f} Mo) en {elapsed:.1f}s")


if __name__ == "__main__":
    sys.exit(main())
