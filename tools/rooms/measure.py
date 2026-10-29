#!/usr/bin/env python3
"""Mesure la valeur des salles : win rate conditionnel par acte/type/PV, dégâts, or, repos.

Source : data/exports/runs-v0.107.1.jsonl.gz (map_point_history).
Dédoublonnage : on ne garde que le solo (les doublons viennent du co-op, une soumission par
joueur) et on déduplique par (seed, personnage, ascension) pour rester prudent.

Sortie : data/calibration/rooms.json (commité).
"""
import argparse
import gzip
import json
from collections import defaultdict

EXPORT = "data/exports/runs-v0.107.1.jsonl.gz"
OUT = "data/calibration/rooms.json"
BUCKETS = [(0, 20), (20, 35), (35, 50), (50, 70), (70, 101)]


def bucket(hp_pct):
    for lo, hi in BUCKETS:
        if lo <= hp_pct < hi:
            return f"{lo}-{hi - 1}" if hi <= 100 else f"{lo}+"
    return "100+"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--limit", type=int, default=200_000)
    args = ap.parse_args()

    # (acte, type, bucket) -> [n, wins, damage_sum, gold_sum, hp_before_sum]
    rooms = defaultdict(lambda: [0, 0, 0, 0, 0, 0])   # n, wins, damage, gold_delta, hp_before, gold_before
    rest = defaultdict(lambda: [0, 0, 0])          # (acte, choix) -> n, wins, healed
    seen = set()
    runs = kept = 0

    with gzip.open(EXPORT, "rt") as f:
        for line in f:
            if kept >= args.limit:
                break
            runs += 1
            r = json.loads(line)
            if len(r.get("players") or []) != 1:
                continue
            sig = (r.get("seed"), (r["players"][0].get("character")), r.get("ascension"))
            if sig in seen:
                continue
            seen.add(sig)
            kept += 1

            won = 1 if r.get("win") else 0
            prev_gold = 99
            for act_i, act_points in enumerate(r.get("map_point_history") or []):
                act = act_i + 1
                for mp in act_points or []:
                    t = mp.get("map_point_type")
                    pss = mp.get("player_stats") or []
                    if not pss or not t:
                        continue
                    ps = pss[0]
                    max_hp = ps.get("max_hp") or 0
                    if max_hp <= 0:
                        continue
                    before = (ps.get("current_hp") or 0) + (ps.get("damage_taken") or 0) - (ps.get("hp_healed") or 0)
                    before = max(0, min(max_hp, before))
                    pct = 100.0 * before / max_hp
                    b = bucket(pct)
                    cell = rooms[(act, t, b)]
                    cell[0] += 1
                    cell[1] += won
                    cell[2] += min(999, ps.get("damage_taken") or 0)
                    gold_now = ps.get("current_gold") or 0
                    # Or borné : certains runs portent des valeurs trafiquées (max observé
                    # 100 000 017) qui écrasent la moyenne. On borne à ±2 000 pour la mesure.
                    cell[3] += max(-1000, min(2000, gold_now - prev_gold))
                    cell[4] += before
                    cell[5] += gold_now
                    prev_gold = gold_now

                    if t == "rest_site":
                        choices = ps.get("rest_site_choices") or []
                        c = (choices[0] if choices else "?").upper()
                        rc = rest[(act, c)]
                        rc[0] += 1
                        rc[1] += won
                        rc[2] += ps.get("hp_healed") or 0

    out = {
        "schema": "ultima.rooms/1",
        "source": "map_point_history de l'export v0.107.1, solo dédupliqué (seed, perso, ascension)",
        "runs": kept,
        "buckets": [f"{lo}-{hi - 1}" if hi <= 100 else f"{lo}+" for lo, hi in BUCKETS],
        "rooms": [],
        "rest": [],
    }
    for (act, t, b), (n, w, dmg, gold, hp, _g) in sorted(rooms.items()):
        if n < 30:
            continue
        out["rooms"].append({
            "act": act, "type": t, "hp_bucket": b, "n": n,
            "win_rate": round(100 * w / n, 2),
            "avg_damage": round(dmg / n, 2),
            "avg_gold_delta": round(gold / n, 2),
            "avg_hp_before": round(hp / n, 1),
        })
    for (act, c), (n, w, healed) in sorted(rest.items()):
        if n < 30:
            continue
        out["rest"].append({
            "act": act, "choice": c, "n": n,
            "win_rate": round(100 * w / n, 2),
            "avg_healed": round(healed / n, 2),
        })

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)

    print(f"runs solo gardés : {kept} / {runs} lus")
    print(f"salles : {len(out['rooms'])} cellules | repos : {len(out['rest'])}")
    print(f"écrit {OUT}")


if __name__ == "__main__":
    main()
