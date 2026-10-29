#!/usr/bin/env python3
"""Mesure les options d'événements (salles « ? ») : ce que chaque choix coûte et rapporte.

Source : map_point_history → player_stats.event_choices (clé de l'option choisie) + les deltas
observés dans la salle (PV, or, reliques, cartes). Solo dédupliqué (seed, perso, ascension).

Sortie : data/calibration/events.json (commité).
"""
import argparse
import gzip
import json
from collections import defaultdict

EXPORT = "data/exports/runs-v0.107.1.jsonl.gz"
OUT = "data/calibration/events.json"


def split_key(key):
    """'DENSE_VEGETATION.pages.INITIAL.options.TRUDGE_ON.title' → (event, option)."""
    parts = (key or "").split(".")
    event = parts[0] if parts else ""
    option = ""
    if "options" in parts:
        i = parts.index("options")
        if i + 1 < len(parts):
            option = parts[i + 1]
    return event, option


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--limit", type=int, default=80_000)
    args = ap.parse_args()

    # (event, option) -> n, wins, hp_delta, gold_delta, relics, cards_gained, cards_removed, curses
    stats = defaultdict(lambda: [0, 0, 0, 0, 0, 0, 0, 0])
    seen = set()
    runs = kept = choices = 0

    with gzip.open(EXPORT, "rt") as f:
        for line in f:
            if kept >= args.limit:
                break
            runs += 1
            r = json.loads(line)
            if len(r.get("players") or []) != 1:
                continue
            sig = (r.get("seed"), r["players"][0].get("character"), r.get("ascension"))
            if sig in seen:
                continue
            seen.add(sig)
            kept += 1
            won = 1 if r.get("win") else 0

            prev_gold = 99
            for act_points in (r.get("map_point_history") or []):
                for mp in act_points or []:
                    pss = mp.get("player_stats") or []
                    if not pss:
                        continue
                    ps = pss[0]
                    gold_now = ps.get("current_gold") or 0
                    gold_delta = gold_now - prev_gold
                    prev_gold = gold_now

                    evs = ps.get("event_choices") or []
                    if not evs:
                        continue
                    hp_delta = (ps.get("hp_healed") or 0) - (ps.get("damage_taken") or 0)
                    relics = len(ps.get("relic_choices") or [])
                    gained = len(ps.get("cards_gained") or [])
                    removed = len(ps.get("cards_removed") or [])
                    curses = sum(1 for c in (ps.get("cards_gained") or [])
                                 if "CURSE" in str(c.get("id") or "") .upper())

                    for e in evs:
                        key = (e.get("title") or {}).get("key") or ""
                        event, option = split_key(key)
                        if not option:
                            continue
                        choices += 1
                        cell = stats[(event, option)]
                        cell[0] += 1
                        cell[1] += won
                        cell[2] += hp_delta
                        cell[3] += gold_delta
                        cell[4] += relics
                        cell[5] += gained
                        cell[6] += removed
                        cell[7] += curses

    out = {"schema": "ultima.events/1",
           "source": "player_stats.event_choices + deltas de la salle (export v0.107.1, solo dédupliqué)",
           "runs": kept, "choices": choices, "options": []}
    for (event, option), (n, w, hp, gold, rel, gained, removed, curses) in sorted(stats.items()):
        if n < 20:
            continue
        out["options"].append({
            "event": event, "option": option, "n": n,
            "win_rate": round(100 * w / n, 2),
            "avg_hp_delta": round(hp / n, 2),
            "avg_gold_delta": round(gold / n, 2),
            "relic_rate": round(rel / n, 3),
            "card_gain_rate": round(gained / n, 3),
            "card_remove_rate": round(removed / n, 3),
            "curse_rate": round(curses / n, 3),
        })

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print(f"runs gardés {kept} | choix {choices} | options {len(out['options'])} → {OUT}")


if __name__ == "__main__":
    main()
