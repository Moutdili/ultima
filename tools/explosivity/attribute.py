#!/usr/bin/env python3
"""Attribution : quel choix a fait « exploser » l'indice de combat ?

Pour chaque run gagnée, on note chaque combat (vitesse + dégâts économisés vs la moyenne de la
rencontre), puis on compare les combats qui SUIVENT une carte prise à ceux qui la précèdent.
Un delta positif = la carte a été suivie de combats plus explosifs (plus courts, moins coûteux).

Sortie : classement des cartes par delta moyen, et les « déclencheurs » des plus gros sauts.

Usage : python3 -I tools/explosivity/attribute.py --wins data/explosivity/wins.jsonl.gz
"""
import argparse
import gzip
import json
import os
import statistics as st
from collections import defaultdict

ENCOUNTERS = "data/catalog/encounters.json"
CARDS = "data/catalog/cards_effects.json"


def load_card_types():
    with open(CARDS, encoding="utf-8") as f:
        data = json.load(f)
    cards = data["cards"] if isinstance(data, dict) and "cards" in data else data
    out = {}
    for c in cards:
        cid = (c.get("id") or "").upper()
        cid = cid[5:] if cid.startswith("CARD.") else cid
        out[cid] = (c.get("type") or "").lower()
    return out


def load_stats():
    with open(ENCOUNTERS, encoding="utf-8") as f:
        data = json.load(f)
    items = data["encounters"] if isinstance(data, dict) else data
    out = {}
    for e in items:
        s = e.get("stats") or {}
        if s.get("avg_turns") and s.get("avg_damage"):
            out[e["id"]] = (s["avg_turns"], s["avg_damage"])
    return out


def fight_score(enc, turns, dmg, stats):
    ref = stats.get(enc)
    if not ref or not turns:
        return None
    exp_turns, exp_dmg = ref
    speed = (exp_turns - turns) / max(1.0, exp_turns)
    defense = (exp_dmg - dmg) / max(1.0, exp_dmg)
    return 0.5 * speed + 0.5 * defense


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--wins", default="data/explosivity/wins.jsonl.gz")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--min-obs", type=int, default=50)
    ap.add_argument("--out", default="data/explosivity/triggers.json")
    args = ap.parse_args()

    stats = load_stats()
    types = load_card_types()
    raw = []          # (acte, carte, delta)
    per_card = defaultdict(list)
    triggers = defaultdict(int)
    runs = 0

    with gzip.open(args.wins, "rt", encoding="utf-8") as f:
        for line in f:
            r = json.loads(line)
            runs += 1
            scored = []
            for a, fl, enc, turns, dmg, deck in r.get("fd") or []:
                s = fight_score(enc, turns, dmg, stats)
                if s is not None:
                    scored.append((a, fl, s, enc))
            if len(scored) < 4:
                continue

            decs = [(a, fl, card) for a, fl, card in (r.get("decs") or [])]

            # (1) delta autour de chaque décision, dans le même acte
            for a, fl, card in decs:
                before = [s for (aa, ff, s, _) in scored if aa == a and ff < fl][-3:]
                after = [s for (aa, ff, s, _) in scored if aa == a and ff > fl][:3]
                if len(before) >= 2 and len(after) >= 2 and types.get(card) not in ("curse", "status"):
                    raw.append((a, card, st.mean(after) - st.mean(before)))

            # (2) déclencheur du plus gros saut de la run
            best_jump, best_idx = 0.0, None
            for i in range(2, len(scored)):
                jump = scored[i][2] - st.mean([scored[j][2] for j in range(max(0, i - 2), i)])
                if jump > best_jump:
                    best_jump, best_idx = jump, i
            if best_idx is not None and best_jump > 0.2:
                a, fl = scored[best_idx][0], scored[best_idx][1]
                prior = [c for (aa, ff, c) in decs
                         if aa == a and ff < fl and types.get(c) not in ("curse", "status")]
                if prior:
                    triggers[prior[-1]] += 1

            if args.limit and runs >= args.limit:
                break

    # Normalisation : on retire la dérive moyenne des décisions du même acte (les combats
    # durcissent avec la progression ; une carte prise tôt paraîtrait sinon « mauvaise »).
    baseline = {}
    for act in sorted({a for (a, _, _) in raw}):
        vals = [d for (a2, _, d) in raw if a2 == act]
        baseline[act] = st.mean(vals) if vals else 0.0
    for a, card, delta in raw:
        per_card[card].append(delta - baseline.get(a, 0.0))

    rows = []
    for card, deltas in per_card.items():
        if len(deltas) < args.min_obs:
            continue
        mean = st.mean(deltas)
        sd = st.pstdev(deltas) if len(deltas) > 1 else 0.0
        rows.append({
            "card": card,
            "n": len(deltas),
            "delta": round(mean, 4),
            "ci95": round(1.96 * sd / (len(deltas) ** 0.5), 4),
            "positive": round(sum(1 for d in deltas if d > 0) / len(deltas), 3),
        })
    rows.sort(key=lambda x: x["delta"], reverse=True)

    payload = {
        "runs": runs,
        "cards": rows,
        "triggers": sorted(triggers.items(), key=lambda kv: kv[1], reverse=True)[:30],
        "baseline": {str(k): round(v, 4) for k, v in baseline.items()},
        "note": "delta = (moyenne des combats après la carte − avant, 3 combats max de chaque côté, même acte) − dérive moyenne de l'acte. Corrélation, pas causalité.",
    }
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)

    print(f"runs analysées : {runs}")
    print(f"cartes avec ≥{args.min_obs} observations : {len(rows)}")
    print("\n--- cartes suivies des combats les plus explosifs ---")
    for x in rows[:12]:
        print(f"  {x['card']:<26} Δ {x['delta']:+.3f} ±{x['ci95']:.3f}  n={x['n']:<5} positifs {x['positive']*100:.0f}%")
    print("\n--- cartes suivies des combats les moins explosifs ---")
    for x in rows[-8:]:
        print(f"  {x['card']:<26} Δ {x['delta']:+.3f} ±{x['ci95']:.3f}  n={x['n']:<5} positifs {x['positive']*100:.0f}%")
    print("\n--- déclencheurs des plus gros sauts ---")
    for card, count in payload["triggers"][:10]:
        print(f"  {card:<26} {count} runs")


if __name__ == "__main__":
    main()
