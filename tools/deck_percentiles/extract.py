#!/usr/bin/env python3
"""Extrait du corpus les decks finaux utilisables pour les percentiles.

Sortie compacte (une ligne par run, solo uniquement) :
  {"c":"SILENT","a":9,"k":2,"f":17,"hp":41,"mhp":70,"w":0,"d":[["CARD.STRIKE_SILENT",0],...]}

- `k` = nombre d'actes traversés (1..3+), `f` = étage atteint.
- Les runs co-op sont exclues : le même run y apparaît une fois par joueur.
"""
import gzip
import json
import os
import sys

SRC = "data/exports/runs-v0.107.1.jsonl.gz"
OUT = "tmp/decks.jsonl.gz"


def bare(value):
    if not isinstance(value, str):
        return ""
    return value.split(".")[-1].strip().upper()


def main():
    if not os.path.exists(SRC):
        sys.exit(f"corpus introuvable : {SRC}")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    kept = read = 0
    with gzip.open(SRC, "rt", encoding="utf-8") as src, gzip.open(OUT, "wt", encoding="utf-8") as out:
        for line in src:
            read += 1
            if read % 100000 == 0:
                print(f"  {read} lus, {kept} gardés", file=sys.stderr, flush=True)
            try:
                run = json.loads(line)
            except json.JSONDecodeError:
                continue

            players = run.get("players") or []
            if len(players) != 1:
                continue
            player = players[0]
            deck = player.get("deck") or []
            if not deck:
                continue

            actes = run.get("map_point_history") or []
            act = max(1, len(actes))
            floor = sum(len(a) for a in actes)

            hp = mhp = None
            if actes and actes[-1]:
                stats = actes[-1][-1].get("player_stats") or []
                if stats:
                    hp = stats[0].get("current_hp")
                    mhp = stats[0].get("max_hp")

            cards = []
            for card in deck:
                cid = card.get("id")
                if not cid:
                    continue
                cards.append([cid if "." in cid else f"CARD.{bare(cid)}",
                              int(card.get("current_upgrade_level") or 0)])

            if not cards:
                continue

            out.write(json.dumps({
                "c": bare(player.get("character")),
                "a": int(run.get("ascension") or 0),
                "k": act,
                "f": floor,
                "hp": hp,
                "mhp": mhp,
                "w": int(bool(run.get("win"))),
                "d": cards,
            }, separators=(",", ":")) + "\n")
            kept += 1

    print(f"runs lus : {read} | decks gardés : {kept} -> {OUT}")


if __name__ == "__main__":
    main()
