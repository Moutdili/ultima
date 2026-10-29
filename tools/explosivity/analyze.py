#!/usr/bin/env python3
"""Analyse des empreintes de runs : croissance, explosivité, et test d'identification.

Trois sorties :
  1. comparaison gagnées / perdues (croissance, vitesse, dégâts) ;
  2. corrélation entre puissance finale et explosivité ;
  3. test de reconnaissance : un état final tiré au hasard, retrouvé parmi N runs.

Usage : python3 -I tools/explosivity/analyze.py [--n 50 500 5000]
"""
import argparse
import gzip
import json
import math
import random
import statistics as st


def load(path, limit=None):
    rows = []
    with gzip.open(path, "rt", encoding="utf-8") as f:
        for line in f:
            rows.append(json.loads(line))
            if limit and len(rows) >= limit:
                break
    return rows


def med(xs):
    xs = [x for x in xs if x is not None]
    return round(st.median(xs), 3) if xs else None


def pct(xs, p):
    xs = sorted(x for x in xs if x is not None)
    if not xs:
        return None
    i = min(len(xs) - 1, int(p * len(xs)))
    return round(xs[i], 3)


def spearman(a, b):
    pairs = [(x, y) for x, y in zip(a, b) if x is not None and y is not None]
    if len(pairs) < 30:
        return None
    n = len(pairs)
    ra = _ranks([p[0] for p in pairs])
    rb = _ranks([p[1] for p in pairs])
    ma, mb = sum(ra) / n, sum(rb) / n
    num = sum((x - ma) * (y - mb) for x, y in zip(ra, rb))
    da = math.sqrt(sum((x - ma) ** 2 for x in ra))
    db = math.sqrt(sum((y - mb) ** 2 for y in rb))
    return round(num / (da * db), 3) if da and db else None


def _ranks(xs):
    order = sorted(range(len(xs)), key=lambda i: xs[i])
    ranks = [0.0] * len(xs)
    i = 0
    while i < len(order):
        j = i
        while j + 1 < len(order) and xs[order[j + 1]] == xs[order[i]]:
            j += 1
        avg = (i + j) / 2 + 1
        for k in range(i, j + 1):
            ranks[order[k]] = avg
        i = j + 1
    return ranks


def jaccard_multiset(a, b):
    ca, cb = {}, {}
    for x in a:
        ca[x] = ca.get(x, 0) + 1
    for x in b:
        cb[x] = cb.get(x, 0) + 1
    inter = sum(min(ca[k], cb.get(k, 0)) for k in ca)
    union = sum(max(ca.get(k, 0), cb.get(k, 0)) for k in set(ca) | set(cb))
    return inter / union if union else 0.0


def similarity(q, r):
    deck = jaccard_multiset(q["final"]["deck"], r["final"]["deck"])
    rel = jaccard_multiset(q["final"]["rel"], r["final"]["rel"])
    return 0.8 * deck + 0.2 * rel


def identify(rows, n, trials=200, seed=7, keep=0.5):
    """Retrouve une run à partir d'un état final PARTIEL (une fraction des cartes, sans reliques).

    L'état complet est ambigu : 36 % des runs partagent exactement le même couple
    deck + reliques (builds convergents, mêmes seeds). On teste donc la vraie question :
    avec la moitié des cartes, sait-on retrouver la run parmi N ?
    """
    rnd = random.Random(seed)
    if len(rows) < n:
        return None, None
    hits = 0
    top5 = 0
    for _ in range(trials):
        pool = rnd.sample(rows, n)
        qi = rnd.randrange(n)
        q = pool[qi]
        deck = list(dict.fromkeys(q["final"]["deck"]))
        if len(deck) < 4:
            continue
        k = max(2, int(len(deck) * keep))
        query = set(rnd.sample(deck, k))

        scored = []
        for i, r in enumerate(pool):
            if i == qi:
                continue
            cand = set(r["final"]["deck"])
            overlap = len(query & cand) / len(query)
            scored.append((overlap, i))
        scored.sort(reverse=True)
        hits += bool(scored) and scored[0][0] == 1.0 and scored[0][1] == qi
        top5 += any(scored[j][1] == qi for j in range(min(5, len(scored))))
    return round(hits / trials, 4), round(top5 / trials, 4)


def describe(r):
    a = r["acts"]
    parts = [f"{r['c']} A{r['asc']}", f"{r['floors']} étages", f"{r['fights']} combats"]
    for i, act in enumerate(a, 1):
        if act["fights"]:
            sp = act["speed"]
            df = act["defense"]
            bits = [f"{act['fights']} combats", f"{act['turns']} tours"]
            if sp is not None:
                bits.append(f"vitesse {sp:.2f}× la moyenne")
            if df is not None:
                bits.append(f"dégâts {df:.2f}× la moyenne")
            parts.append(f"acte {i}: " + ", ".join(bits))
    parts.append(f"puissance {r['p0']}→{r['p1']} (×{r['growth']}), pente {r['slope']}, courbure {r['curv']}")
    return " | ".join(parts)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--wins", default="data/explosivity/wins.jsonl.gz")
    ap.add_argument("--losses", default="data/explosivity/losses.jsonl.gz")
    ap.add_argument("--sizes", nargs="*", type=int, default=[50, 500, 5000])
    args = ap.parse_args()

    wins = load(args.wins)
    try:
        losses = load(args.losses, limit=len(wins))
    except FileNotFoundError:
        losses = []

    print(f"gagnées {len(wins)} | perdues {len(losses)}\n")

    def block(name, rows):
        print(f"--- {name} ---")
        print(f"  croissance médiane   : {med([r['growth'] for r in rows])}")
        print(f"  pente médiane        : {med([r['slope'] for r in rows])}")
        print(f"  courbure médiane     : {med([r['curv'] for r in rows])}")
        print(f"  étages médians       : {med([r['floors'] for r in rows])}")
        print(f"  puissance finale     : méd {med([r['p1'] for r in rows])} · p90 {pct([r['p1'] for r in rows], 0.9)}")
        for i in range(3):
            sp = [r["acts"][i]["speed"] for r in rows if r["acts"][i]["fights"]]
            df = [r["acts"][i]["defense"] for r in rows if r["acts"][i]["fights"]]
            print(f"  acte {i+1}: vitesse méd {med(sp)} · dégâts méd {med(df)} · n={len(sp)}")
        print()

    block("GAGNÉES", wins)
    if losses:
        block("PERDUES", losses)

    # Corrélations sur les gagnées
    p1 = [r["p1"] for r in wins]
    sp3 = [r["acts"][2]["speed"] for r in wins]
    df3 = [r["acts"][2]["defense"] for r in wins]
    print("--- corrélations (gagnées, Spearman) ---")
    print(f"  puissance finale vs vitesse acte 3 : {spearman(p1, sp3)}")
    print(f"  puissance finale vs dégâts acte 3  : {spearman(p1, df3)}")
    print(f"  vitesse acte 3 vs dégâts acte 3    : {spearman(sp3, df3)}")
    print()

    # Top / flop explosivité (acte 3)
    scored = [r for r in wins if r["acts"][2]["fights"] and r["acts"][2]["speed"] is not None]
    scored.sort(key=lambda r: r["acts"][2]["speed"], reverse=True)
    print("--- expression, meilleure explosivité ---")
    print(" ", describe(scored[0]))
    print("--- expression, plus lente ---")
    print(" ", describe(scored[-1]))
    print()

    print("--- identification par un état final PARTIEL (50 % des cartes, sans reliques) ---")
    for n in args.sizes:
        top1, top5 = identify(wins, n)
        if top1 is None:
            continue
        print(f"  N={n:<5} top-1 {top1*100:.1f}%  top-5 {top5*100:.1f}%  (hasard {100/n:.1f}%)")


if __name__ == "__main__":
    main()
