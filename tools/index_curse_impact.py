#!/usr/bin/env python3
"""Classe les malédictions et statuts par impact mesuré sur la victoire.

Méthode : parmi les runs **entrées en acte 3** (contrôle de la longueur), on compare le taux de
victoire des runs ayant acquis la carte avant l'acte 3 à celui de toutes les runs entrées en acte 3.
Un delta négatif = la carte est associée à moins de victoires.

Limites assumées : observationnel, pas causal. Certaines malédictions viennent d'événements qui
donnent une contrepartie forte (relique, or) : leur delta positif mesure le **bundle**, pas la carte.
Les statuts (Burn, Wither…) sont temporaires et rares dans le deck final : ils sont listés mais
marqués « échantillon insuffisant » quand n < 300.

Sortie : data/catalog/curse_impact.json (commité).
"""
import gzip
import json
import os
import statistics as st

WINS = "data/explosivity/wins.jsonl.gz"
LOSSES = "data/explosivity/losses.jsonl.gz"
CARDS = "data/catalog/cards_effects.json"
OUT = "data/catalog/curse_impact.json"
AC3_START_FLOOR = 33   # au-delà, la run est entrée en acte 3 (mesuré sur la distribution)
MIN_SAMPLE = 300


def bare(v):
    v = (v or "").upper()
    return v[5:] if v.startswith("CARD.") else v


def main():
    with open(CARDS, encoding="utf-8") as f:
        data = json.load(f)
    cards = data["cards"] if isinstance(data, dict) and "cards" in data else data
    types, desc = {}, {}
    for c in cards:
        cid = bare(c.get("id"))
        types[cid] = (c.get("type") or "").lower()
        desc[cid] = c.get("description_fr") or c.get("description") or ""

    rows = []
    for path in (WINS, LOSSES):
        if not os.path.exists(path):
            continue
        with gzip.open(path, "rt", encoding="utf-8") as f:
            rows.extend(json.loads(line) for line in f)

    reached = [r for r in rows if r.get("floors", 0) >= AC3_START_FLOOR]
    base = sum(r["w"] for r in reached) / max(1, len(reached))

    stat = {}
    for r in reached:
        acquired = {c for (a, _f, c) in (r.get("decs") or []) if a < 2}
        for cid in acquired:
            if types.get(cid) not in ("curse", "status"):
                continue
            s = stat.setdefault(cid, [0, 0])
            s[0] += 1
            s[1] += r["w"]

    entries = []
    for cid, (n, wins) in stat.items():
        p = wins / n
        entries.append({
            "id": cid,
            "type": types.get(cid),
            "measured": True,
            "description": desc.get(cid, "")[:200],
            "n": n,
            "win_rate": round(100 * p, 2),
            "delta_pp": round(100 * (p - base), 2),
            "reliable": n >= MIN_SAMPLE,
            # toxicité 0..100 : 50 = neutre, 100 = delta -10 pp, 0 = delta +10 pp
            "toxicity": max(0, min(100, round(50 - 5 * (100 * (p - base))))),
        })
    # Exhaustivité : toute malédiction/statut du catalogue doit avoir une entrée, mesurée ou
    # explicitement « aucune donnée ». Aucune carte ne peut être inconnue du moteur.
    known = {e["id"] for e in entries}
    for cid in sorted({c for c, t in types.items() if t in ("curse", "status")} - known):
        entries.append({
            "id": cid,
            "type": types.get(cid),
            "measured": False,
            "description": desc.get(cid, "")[:200],
            "n": 0,
            "win_rate": None,
            "delta_pp": None,
            "reliable": False,
            "toxicity": None,
            "reason": "absent du corpus (jamais acquis avant l'acte 3) — pénalité par défaut explicite",
        })

    entries.sort(key=lambda x: (x["toxicity"] is None, x["toxicity"] if x["toxicity"] is not None else 0, -x["n"]))

    payload = {
        "schema": "ultima.curse_impact/1",
        "method": "runs entrées en acte 3 (floors >= 33) ; delta de win rate vs baseline",
        "baseline_win_rate": round(100 * base, 2),
        "sample_runs": len(reached),
        "min_sample": MIN_SAMPLE,
        "caveat": "Observationnel. Les deltas positifs mesurent souvent un bundle événement+contrepartie, pas la carte seule.",
        "entries": entries,
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)

    print(f"runs entrées acte 3 : {len(reached)} | baseline {100*base:.1f} %")
    print(f"{'carte':26s} {'type':7s} {'n':>6s} {'win%':>6s} {'delta':>7s} {'tox':>4s}")
    for e in entries[:16]:
        flag = "" if e["reliable"] else "  (n faible)"
        print(f"{e['id'][:26]:26s} {e['type']:7s} {e['n']:6d} {e['win_rate']:6.1f} {e['delta_pp']:+7.1f} {e['toxicity']:4d}{flag}")


if __name__ == "__main__":
    main()
