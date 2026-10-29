#!/usr/bin/env python3
"""Indexe les runs de l'export spire-codex en un JSONL compact, une ligne par run.

Entrée  : data/exports/runs-v<build>.jsonl.gz (export officiel, JSON complet du jeu)
Sortie  : data/index/runs-<build>.jsonl.gz

Chaque ligne contient : identité de la run, joueurs (deck/reliques/potions), et **chaque décision**
(récompense de cartes, relique, choix d'Ancien) avec l'état du joueur à ce moment (PV, or, acte,
type de salle) et ce qui a été pris. C'est la matière du backtest : on rejoue le contexte, on
compare la recommandation au choix réel.

Usage :
  python3 -I tools/index_runs.py --limit 20000
  python3 -I tools/index_runs.py                  # tout l'export
Options : --build v0.107.1, --modes standard,daily, --coop-only, --out DIR
"""
import argparse
import gzip
import json
import os
import sys
import time
from collections import Counter


def bare(card_id: str | None) -> str:
    if not card_id:
        return ""
    cid = card_id.strip().upper()
    for prefix in ("CARD.", "RELIC.", "POTION."):
        if cid.startswith(prefix):
            return cid[len(prefix):]
    return cid


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", default="data/exports/runs-v0.107.1.jsonl.gz")
    ap.add_argument("--out", default="data/index")
    ap.add_argument("--limit", type=int, default=0, help="0 = tout")
    ap.add_argument("--modes", default="standard,daily,custom")
    ap.add_argument("--coop-only", action="store_true")
    args = ap.parse_args()

    allowed_modes = {m.strip() for m in args.modes.split(",") if m.strip()}
    os.makedirs(args.out, exist_ok=True)
    build_tag = os.path.basename(args.export).replace("runs-", "").replace(".jsonl.gz", "")
    dest = os.path.join(args.out, f"runs-{build_tag}.jsonl.gz")

    started = time.time()
    seen = kept = coop = decisions = skips = 0
    wins = 0
    modes = Counter()
    grad = Counter()
    chars = Counter()

    with gzip.open(args.export, "rt", encoding="utf-8") as src, \
         gzip.open(dest, "wt", encoding="utf-8", compresslevel=6) as dst:
        for line in src:
            seen += 1
            try:
                d = json.loads(line)
            except json.JSONDecodeError:
                continue

            mode = d.get("game_mode") or "standard"
            players = d.get("players") or []
            if mode not in allowed_modes:
                continue
            if args.coop_only and len(players) < 2:
                continue

            rec_players = []
            for i, p in enumerate(players):
                rec_players.append({
                    "id": p.get("id", i + 1),
                    "c": bare(p.get("character")),
                    "hp": p.get("current_hp"),
                    "mhp": p.get("max_hp"),
                    "deck": [bare(x.get("id")) for x in (p.get("deck") or [])],
                    "rel": [bare(x.get("id") if isinstance(x, dict) else x) for x in (p.get("relics") or [])],
                    "pot": [bare(x.get("id") if isinstance(x, dict) else x) for x in (p.get("potions") or [])],
                    "pslot": p.get("max_potion_slot_count"),
                })

            decs = []
            acts = d.get("map_point_history") or []
            for act_i, act in enumerate(acts):
                floors = act if isinstance(act, list) else [act]
                for fl_i, entry in enumerate(floors):
                    if not isinstance(entry, dict):
                        continue
                    room = entry.get("map_point_type")
                    for ps in (entry.get("player_stats") or []):
                        cc = ps.get("card_choices") or []
                        rc = ps.get("relic_choices") or []
                        an = ps.get("ancient_choice") or []
                        if not (cc or rc or an):
                            continue

                        offered, picked = [], None
                        for c in cc:
                            card = c.get("card") or {}
                            cid = bare(card.get("id"))
                            if cid:
                                offered.append(cid)
                            if c.get("was_picked"):
                                picked = cid
                        relic_offered, relic_picked = [], None
                        for r in rc:
                            rid = bare(r.get("choice"))
                            if rid:
                                relic_offered.append(rid)
                            if r.get("was_picked"):
                                relic_picked = rid
                        ancient = [bare(a.get("TextKey")) for a in an if a.get("was_chosen")]

                        dec = {
                            "t": "card" if cc else ("relic" if rc else "ancient"),
                            "a": act_i, "f": fl_i, "rt": room,
                            "p": ps.get("player_id"), "hp": ps.get("current_hp"),
                            "mhp": ps.get("max_hp"), "g": ps.get("current_gold"),
                        }
                        if cc:
                            dec["o"] = offered            # cartes proposées
                            dec["k"] = picked             # carte prise (null = skip)
                            decisions += 1
                            if picked is None:
                                skips += 1
                        if rc:
                            dec["ro"] = relic_offered
                            dec["rk"] = relic_picked
                        if ancient:
                            dec["an"] = ancient
                        decs.append(dec)

            row = {
                "h": d.get("run_hash"),
                "b": d.get("build_id"),
                "m": mode,
                "asc": d.get("ascension"),
                "n": len(players),
                "w": int(bool(d.get("win"))),
                "ab": int(bool(d.get("was_abandoned"))),
                "kb": bare(d.get("killed_by_encounter")),
                "rt": d.get("run_time"),
                "pl": rec_players,
                "d": decs,
            }
            dst.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")

            kept += 1
            wins += row["w"]
            modes[mode] += 1
            if len(players) > 1:
                coop += 1
            for p in rec_players:
                chars[p["c"]] += 1

            if args.limit and kept >= args.limit:
                break

    elapsed = time.time() - started
    size = os.path.getsize(dest) if os.path.exists(dest) else 0
    print(f"lignes lues      : {seen}")
    print(f"runs indexées    : {kept} (dont co-op: {coop})  win: {wins} ({100*wins/max(kept,1):.1f} %)")
    print(f"décisions cartes : {decisions} (skips: {skips}, {100*skips/max(decisions,1):.1f} %)")
    print(f"modes            : {dict(modes)}")
    print(f"personnages      : {dict(chars.most_common(6))}")
    print(f"sortie           : {dest} ({size/1024/1024:.1f} Mo) en {elapsed:.1f}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
