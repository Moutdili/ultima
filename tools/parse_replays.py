#!/usr/bin/env python3
"""Parse les replays NDJSON de spire-codex en un index de combats exploitable.

Entrée  : data/replays/<hash>.ndjson (produits par tools/fetch_replays.py, gitignorés)
Sorties : data/replays/index.json  — un objet par run, un objet par combat, un objet par tour
          data/replays/report.md    — rapport lisible (une ligne par combat)

Le format des événements est documenté dans docs/30-replays-format.md. Rien n'est deviné :
les événements non modélisés sont comptés dans `ignored` et aucune valeur absente du NDJSON
n'est inventée (`null` quand la donnée n'existe pas).

Conventions vérifiées sur les 22 replays (60 920 événements) :
  · `hit.dmg`      = PV réellement perdus par la cible (après blocage) ;
    `hit.blocked`  = dégâts absorbés par le bloc ; `hit.overkill` = excédent au-delà des PV.
    Dégâts bruts d'un coup = dmg + blocked + overkill.
  · `hit.src`/`dst` valent "player" pour le camp joueur — en co-op, les deux joueurs sont
    fusionnés sous "player" : la répartition par joueur n'est PAS dans le NDJSON.
  · Les PV du joueur s'ancrent sur les événements `hp` (valeur absolue `hp`), puis décroissent
    des `hit.dmg` subis. `hp.d` est un delta (négatif pour une perte), `src` ∈ heal/loss/set.
  · Un `turn` ouvre un tour (`round`, `side` ∈ player/enemy) ; `end_turn` le ferme.
  · `intent.at` peut rejouer la même intention (turn_start puis turn_end) : la dernière valeur
    avant le `move` est la bonne (elle tient compte de Faiblesse/Vulnérable appliqués entre-temps).

Usage :
    python3 -I tools/parse_replays.py
    python3 -I tools/parse_replays.py --replays-dir data/replays --hash 7287113b54a6745b
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import sys
import time
from collections import Counter, OrderedDict

MAX_TURNS_KEPT = 60  # garde-fou : un combat anormal ne doit pas gonfler l'index

# Types d'événements modélisés dans l'index (le reste est compté, jamais ignoré en silence).
MODELLED = {
    "header", "combat_start", "combat_end", "turn", "end_turn", "play", "hit", "block",
    "intent", "move", "energy", "hp", "hp_loss", "power", "power_lost", "death",
}


def load_ndjson(path: str) -> list[dict]:
    out = []
    with open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                out.append(json.loads(line))
            except json.JSONDecodeError as e:
                raise SystemExit(f"{path}:{n} JSON invalide ({e})")
    return out


def new_combat(start: dict, hp: float | None) -> dict:
    return {
        "combatId": start.get("combat_id"),
        "attempt": start.get("attempt_id"),
        "encounter": start.get("encounter"),
        "act": start.get("act"),
        "floor": start.get("floor"),
        "enemies": [
            {"cid": e.get("cid"), "id": e.get("id"), "hp": e.get("hp"), "maxHp": e.get("max_hp")}
            for e in (start.get("enemies") or [])
        ],
        "allies": [a.get("id") for a in (start.get("allies") or [])],
        "hpStart": hp,
        "hpEnd": None,
        "result": None,
        "turnsReported": None,
        "roundsReported": None,
        "hpLostTotal": None,
        "damageDealt": 0,      # dégâts bruts infligés par le camp joueur (dmg+blocked+overkill)
        "damageTaken": 0,      # PV perdus par le camp joueur
        "damageBlocked": 0,    # dégâts absorbés par le bloc du camp joueur
        "healing": 0,
        "cardsPlayed": {},
        "turns": [],
    }


def parse_replay(path: str) -> dict:
    events = load_ndjson(path)
    h = next((e for e in events if e.get("t") == "header"), None)
    if h is None:
        raise SystemExit(f"{path}: pas de header")
    heads = [e for e in events if e.get("t") == "header"]

    run = {
        "hash": os.path.basename(path)[: -len(".ndjson")],
        "build": h.get("build_id"),
        "character": h.get("character"),
        "ascension": h.get("ascension"),
        "players": h.get("player_count"),
        "gameMode": h.get("game_mode"),
        "modifiers": h.get("modifiers"),
        "seed": h.get("seed"),
        "replayVersion": h.get("replay_version"),
        "runSchemaVersion": h.get("run_schema_version"),
        "modVersion": h.get("mod_version"),
        "mods": [m.get("id") for m in (h.get("mods") or []) if isinstance(m, dict)],
        "reloads": h.get("reloads"),
        "segments": len(heads),
        "events": len(events),
        "floorMax": max((e.get("floor") or 0) for e in events),
        "evidence": [],
        "combats": [],
        "ignored": {},
    }

    hp = None
    combat = None
    turn = None
    ignored: Counter = Counter()
    intents: dict = {}       # (cid, move) -> last intent payload of the current turn
    last_intent_by_src: dict = {}

    def close_turn():
        nonlocal turn
        if combat is not None and turn is not None:
            turn["intents"] = list(last_intent_by_src.values())
            if len(combat["turns"]) < MAX_TURNS_KEPT:
                combat["turns"].append(turn)
        turn = None
        last_intent_by_src.clear()

    def close_combat(end: dict | None):
        nonlocal combat
        if combat is None:
            return
        close_turn()
        if end is not None:
            combat["result"] = end.get("result")
            combat["turnsReported"] = end.get("turns")
            combat["roundsReported"] = end.get("rounds")
            combat["hpLostTotal"] = end.get("hp_lost_total")
        combat["hpEnd"] = hp
        combat["cardsPlayed"] = dict(combat["cardsPlayed"])
        run["combats"].append(combat)
        combat = None

    for e in events:
        t = e.get("t")
        if t == "header":
            continue
        if t in ("resume", "map", "map_room", "room", "room_exit", "act"):
            continue  # hors combat, documentés dans docs/30

        if t == "hp":
            if e.get("dst") == "player":
                hp = e.get("hp")
                d = e.get("d") or 0
                if d > 0 and e.get("src") in ("heal", "set") and combat is not None:
                    combat["healing"] += d
            continue

        if t == "combat_start":
            close_combat(None)
            combat = new_combat(e, hp)
            turn = None
            continue

        if t == "combat_end":
            close_combat(e)
            continue

        if combat is None:
            ignored[t] += 1
            continue

        if t == "turn":
            close_turn()
            turn = {
                "round": e.get("round"),
                "side": e.get("side"),
                "hpStart": hp,
                "hpEnd": None,
                "energy": None,
                "blockGained": 0,
                "damageDealt": 0,
                "damageTaken": 0,
                "cards": [],
                "intents": [],
            }
            continue

        if t == "end_turn":
            if turn is not None:
                turn["hpEnd"] = hp
            close_turn()
            continue

        if t == "energy":
            if turn is not None and e.get("energy") is not None:
                turn["energy"] = e.get("energy")
            continue

        if t == "play":
            cid = e.get("id")
            if turn is not None:
                turn["cards"].append({
                    "id": cid, "up": e.get("up") or 0, "cost": e.get("cost_paid"),
                    "x": e.get("x_value"), "auto": bool(e.get("auto")),
                    "target": e.get("target"),
                })
            combat["cardsPlayed"][cid] = combat["cardsPlayed"].get(cid, 0) + 1
            continue

        if t == "hit":
            dmg = e.get("dmg") or 0
            blocked = e.get("blocked") or 0
            over = e.get("overkill") or 0
            if e.get("src") == "player":
                combat["damageDealt"] += dmg + blocked + over
                combat["damageBlocked"] += blocked  # blocage des monstres
                if turn is not None:
                    turn["damageDealt"] += dmg + blocked + over
            elif e.get("dst") == "player":
                combat["damageTaken"] += dmg
                combat["damageBlocked"] += blocked
                if turn is not None:
                    turn["damageTaken"] += dmg
                if hp is not None:
                    hp = max(0, hp - dmg)
            continue

        if t == "hp_loss":
            dmg = e.get("dmg") or 0
            if e.get("dst") == "player" or e.get("src") == "player":
                combat["damageTaken"] += dmg
                if turn is not None:
                    turn["damageTaken"] += dmg
                if hp is not None:
                    hp = max(0, hp - dmg)
            continue

        if t == "block":
            if e.get("src") == "player" and e.get("reason") == "gained":
                n = e.get("n") or 0
                if turn is not None:
                    turn["blockGained"] += n
            continue

        if t == "intent":
            src = e.get("src")
            if src and e.get("id"):
                last_intent_by_src[src] = {
                    "src": src, "cid": e.get("src_cid"), "move": e.get("id"),
                    "at": e.get("at"), "dmg": e.get("dmg"), "hits": e.get("hits"),
                    "intents": e.get("intents"),
                }
            continue

        if t in ("move", "power", "power_lost", "death"):
            continue  # modélisés ailleurs (validation) ou simples traces

        ignored[t] += 1

    close_combat(None)  # replay tronqué : on garde ce qui a été vu

    run["ignored"] = dict(sorted(ignored.items()))
    run["summary"] = summarise(run)
    return run


def summarise(run: dict) -> dict:
    combats = run["combats"]
    dealt = sum(c["damageDealt"] for c in combats)
    taken = sum(c["damageTaken"] for c in combats)
    cards: Counter = Counter()
    for c in combats:
        cards.update(c["cardsPlayed"])
    return {
        "combats": len(combats),
        "victories": sum(1 for c in combats if c["result"] == "victory"),
        "defeats": sum(1 for c in combats if c["result"] not in (None, "victory")),
        "damageDealt": dealt,
        "damageTaken": taken,
        "turns": sum(len(c["turns"]) for c in combats),
        "cardsPlayed": sum(cards.values()),
        "topCards": cards.most_common(5),
    }


def write_report(runs: list[dict], path: str) -> None:
    lines = [
        "# Replays — rapport lisible",
        "",
        f"Généré le {time.strftime('%Y-%m-%d %H:%M')} par `tools/parse_replays.py`.",
        "Dégâts « infligés » = bruts (dmg + bloqué + overkill) ; « subis » = PV réellement perdus.",
        "En co-op, le camp joueur est fusionné sous `player` : les chiffres sont ceux de l'équipe.",
        "",
    ]
    for r in runs:
        s = r["summary"]
        lines += [
            f"## {r['hash']} — {str(r['character']).replace('CHARACTER.', '').title()} "
            f"A{r['ascension']}, {r['build']}, {r['players']} joueur(s)",
            "",
            f"{s['combats']} combats · {s['victories']} victoires · {s['turns']} tours-joueur · "
            f"{s['damageDealt']} dégâts infligés · {s['damageTaken']} PV perdus · "
            f"{s['cardsPlayed']} cartes jouées · {r['events']} événements",
            "",
        ]
        if r["ignored"]:
            lines.append(f"Événements non modélisés (comptés, ignorés) : "
                         f"{', '.join(f'{k}×{v}' for k, v in r['ignored'].items())}.")
            lines.append("")
        lines.append("| # | étage | rencontre | issue | tours | infligés | subis | PV | cartes |")
        lines.append("|---|-------|-----------|-------|-------|----------|-------|----|--------|")
        for i, c in enumerate(r["combats"]):
            hp = f"{c['hpStart']}→{c['hpEnd']}" if c["hpStart"] is not None else "?"
            cards = ", ".join(f"{k}×{v}" for k, v in
                              Counter(c["cardsPlayed"]).most_common(4))
            lines.append(
                f"| {i} | {c['floor']} | {c['encounter']} | {c['result']} | "
                f"{len(c['turns'])} | {c['damageDealt']} | {c['damageTaken']} | {hp} | {cards} |")
        lines.append("")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Parse les replays NDJSON en index de combats.")
    ap.add_argument("--replays-dir", default=os.path.join("data", "replays"))
    ap.add_argument("--out", default=os.path.join("data", "replays", "index.json"))
    ap.add_argument("--report", default=os.path.join("data", "replays", "report.md"))
    ap.add_argument("--hash", action="append", default=None, help="limite à un ou plusieurs hashes")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    paths = sorted(glob.glob(os.path.join(args.replays_dir, "*.ndjson")))
    if args.hash:
        wanted = set(args.hash)
        paths = [p for p in paths if os.path.basename(p)[: -len(".ndjson")] in wanted]
    if not paths:
        print(f"aucun replay dans {args.replays_dir}", file=sys.stderr)
        return 1

    runs = []
    for p in paths:
        run = parse_replay(p)
        runs.append(run)
        if not args.quiet:
            s = run["summary"]
            print(f"{run['hash']} {run['build']} {run['character']} A{run['ascension']} : "
                  f"{s['combats']} combats, {s['turns']} tours, "
                  f"{s['damageDealt']} infligés / {s['damageTaken']} subis")

    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    index = {
        "generated": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "source": args.replays_dir,
        "runs": runs,
        "totals": {
            "runs": len(runs),
            "combats": sum(r["summary"]["combats"] for r in runs),
            "turns": sum(r["summary"]["turns"] for r in runs),
            "events": sum(r["events"] for r in runs),
            "damageDealt": sum(r["summary"]["damageDealt"] for r in runs),
            "damageTaken": sum(r["summary"]["damageTaken"] for r in runs),
        },
    }
    with open(args.out + ".part", "w", encoding="utf-8") as f:
        json.dump(index, f, ensure_ascii=False, separators=(",", ":"))
        f.write("\n")
    os.replace(args.out + ".part", args.out)
    write_report(runs, args.report)

    t = index["totals"]
    print(f"\n{len(runs)} runs · {t['combats']} combats · {t['turns']} tours · "
          f"{t['events']} événements → {args.out} ({os.path.getsize(args.out)/1024:.0f} Ko) "
          f"+ {args.report}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
