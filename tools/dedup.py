#!/usr/bin/env python3
"""Déduplique l'index de runs par signature de contenu, et mesure l'impact.

Pourquoi : le corpus public contient des copies intégrales (même deck, mêmes reliques,
mêmes PV, même trajectoire) avec des `run_hash` différents — cf. docs/32 §3 (38 % de
doublons sur 10 000 runs gagnées). Toute statistique, y compris la calibration des poids,
est gonflée par ces copies : un doublon ne crée pas de signal, il double le poids d'une
observation.

Signature (niveaux, du plus permissif au plus strict) :

  final     personnages + ascension + nb joueurs, deck trié (ids), reliques triées,
            PV/PV max (dernier point connu), issue (win/abandon/tué par)
  picks     final + suite des décisions prises : (type, acte, étage, type de salle,
            joueur, carte/reliq/ancien choisi)  ← niveau par défaut
  strict    picks + les listes de cartes/reliques **proposées** à chaque décision

Le niveau `picks` est celui demandé par le brief ; `final` et `strict` servent de
sensibilité : ce sont des choix de signature, pas une vérité (deux joueurs différents
peuvent produire deux runs au contenu identique, et l'index perd de l'information —
niveaux d'amélioration, choix d'événement, PV finaux exacts).

Entrée  : data/index/runs-v0.107.1.jsonl.gz (1 ligne = 1 run, format docs/28 §2.2)
Sorties : data/index/deduped-v0.107.1.jsonl.gz  (première occurrence de chaque signature)
          data/index/dedup-report.json          (groupes, tailles, métriques avant/après)

Usage :
  python3 -I tools/dedup.py --limit 50000            # échantillon, écrit ailleurs
  python3 -I tools/dedup.py                          # tout l'index
  python3 -I tools/dedup.py --no-write               # statistiques seules
  python3 -I tools/dedup.py --audit-export data/exports/runs-v0.107.1.jsonl.gz \\
                            --audit-limit 100000    # audit de la signature sur l'export brut
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import statistics
import sys
import time
from collections import Counter

LEVELS = ("final", "picks", "strict")
DEFAULT_LEVEL = "picks"


# --------------------------------------------------------------------------- signature
def _canon(obj) -> bytes:
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":")).encode()


def _digest(obj, size: int = 10) -> bytes:
    return hashlib.blake2b(_canon(obj), digest_size=size).digest()


def facets(row: dict) -> dict:
    """Décompose une ligne d'index en facettes de signature (calculées une seule fois)."""
    players = row.get("pl") or []
    # PV par joueur : `pl[].hp` est NULL dans cet export (schéma 9) ; les décisions
    # portent current_hp/max_hp → on prend le dernier point connu de chaque joueur.
    last_hp: dict = {}
    picks: list = []
    offers: list = []
    for d in row.get("d") or []:
        pid = d.get("p")
        if d.get("hp") is not None:
            last_hp[pid] = (d.get("hp"), d.get("mhp"))
        t = d.get("t")
        if t == "card":
            picks.append([t, d.get("a"), d.get("f"), d.get("rt"), pid, d.get("k")])
            offers.append(d.get("o"))
        elif t == "relic":
            picks.append([t, d.get("a"), d.get("f"), d.get("rt"), pid, d.get("rk")])
            offers.append(d.get("ro"))
        else:  # ancien : liste des clés choisies (souvent 1 élément)
            picks.append([t, d.get("a"), d.get("f"), d.get("rt"), pid, d.get("an")])
            offers.append(None)

    pl_facets = []
    for p in players:
        hp, mhp = p.get("hp"), p.get("mhp")
        if hp is None and p.get("id") in last_hp:
            hp, mhp = last_hp[p.get("id")]
        pl_facets.append([
            p.get("c") or "",
            sorted(p.get("deck") or []),   # multiset : les niveaux d'amélioration ne sont PAS
            sorted(p.get("rel") or []),    # dans l'index (perte du champ current_upgrade_level)
            hp, mhp,
        ])

    head = [row.get("m"), row.get("asc"), row.get("n"), row.get("w"),
            row.get("ab"), row.get("kb")]
    return {"head": head, "players": pl_facets, "picks": picks, "offers": offers}


def signatures(f: dict, levels=LEVELS) -> dict:
    base = [f["head"], f["players"]]
    out = {}
    for lvl in levels:
        if lvl == "final":
            out[lvl] = _digest(base)
        elif lvl == "picks":
            out[lvl] = _digest([f["head"], f["players"], f["picks"]])
        else:
            out[lvl] = _digest([f["head"], f["players"], f["picks"], f["offers"]])
    return out


# --------------------------------------------------------------------------- métriques
class Accum:
    """Compteurs pour comparer le corpus avant/après déduplication."""

    def __init__(self):
        self.runs = 0
        self.players = 0
        self.decisions = 0
        self.card_decisions = 0
        self.skips = 0
        self.wins = 0
        self.abandoned = 0
        self.coop = 0
        self.no_decisions = 0
        self.deck_cards = 0
        self.relics = 0
        self.modes: Counter = Counter()
        self.asc: Counter = Counter()
        self.asc_win: Counter = Counter()
        self.chars: Counter = Counter()
        self.chars_win: Counter = Counter()
        self.kills: Counter = Counter()

    def add(self, row: dict) -> None:
        self.runs += 1
        pl = row.get("pl") or []
        self.players += len(pl)
        w = row.get("w") or 0
        self.wins += w
        self.abandoned += row.get("ab") or 0
        if len(pl) > 1:
            self.coop += 1
        if not (row.get("d") or []):
            self.no_decisions += 1
        asc = row.get("asc")
        self.asc[asc] += 1
        self.asc_win[asc] += w
        self.modes[row.get("m")] += 1
        for p in pl:
            c = p.get("c") or "?"
            self.chars[c] += 1
            self.chars_win[c] += w
            self.deck_cards += len(p.get("deck") or [])
            self.relics += len(p.get("rel") or [])
        for d in row.get("d") or []:
            self.decisions += 1
            if d.get("t") == "card":
                self.card_decisions += 1
                if d.get("k") is None:
                    self.skips += 1
        kb = row.get("kb")
        if kb:
            self.kills[kb] += 1

    def dump(self) -> dict:
        r = max(self.runs, 1)
        return {
            "runs": self.runs,
            "joueurs": self.players,
            "decisions": self.decisions,
            "decisions_cartes": self.card_decisions,
            "skips": self.skips,
            "pct_skip": round(100 * self.skips / max(self.card_decisions, 1), 2),
            "win_rate": round(100 * self.wins / r, 3),
            "gagnees": self.wins,
            "perdues": self.runs - self.wins,
            "abandons": self.abandoned,
            "coop": self.coop,
            "pct_coop": round(100 * self.coop / r, 2),
            "runs_sans_decision": self.no_decisions,
            "pct_runs_sans_decision": round(100 * self.no_decisions / r, 2),
            "decisions_par_run": round(self.decisions / r, 3),
            "decisions_par_joueur": round(self.decisions / max(self.players, 1), 3),
            "deck_moyen": round(self.deck_cards / max(self.players, 1), 2),
            "reliques_moyennes": round(self.relics / max(self.players, 1), 2),
            "modes": dict(self.modes.most_common()),
            "ascension": {str(k): v for k, v in sorted(self.asc.items(), key=lambda kv: (kv[0] is None, kv[0]))},
            "win_rate_par_ascension": {
                str(k): round(100 * self.asc_win[k] / v, 2)
                for k, v in sorted(self.asc.items(), key=lambda kv: (kv[0] is None, kv[0]))
            },
            "personnages_distincts": len(self.chars),
            "personnages_top25": dict(self.chars.most_common(25)),
            "win_rate_par_personnage_top25": {
                k: round(100 * self.chars_win[k] / v, 2)
                for k, v in self.chars.most_common(25)
            },
            "tue_par_top10": {k: v for k, v in self.kills.most_common(10)},
        }


CANONICAL_CHARS = {
    "CHARACTER.IRONCLAD", "CHARACTER.SILENT", "CHARACTER.DEFECT",
    "CHARACTER.NECROBINDER", "CHARACTER.REGENT",
}


def _is_calibration_row(row: dict) -> bool:
    """Sous-corpus de calibration S15/S17 : solo, standard, perso canonique, deck ≤ 80."""
    pl = row.get("pl") or []
    if len(pl) != 1 or row.get("m") != "standard":
        return False
    p = pl[0]
    if (p.get("c") or "") not in CANONICAL_CHARS:
        return False
    return len(p.get("deck") or []) <= 80


def _bucket_order(b: str) -> int:
    return {"11-20": 11, "21-50": 21, "51-100": 51, ">100": 101}.get(b, int(b) if b.isdigit() else 999)


def _size_bucket(n: int) -> str:
    if n <= 10:
        return str(n)
    if n <= 20:
        return "11-20"
    if n <= 50:
        return "21-50"
    if n <= 100:
        return "51-100"
    return ">100"


# --------------------------------------------------------------------------- dédup index
def run_dedup(args) -> int:
    src_path = args.input
    started = time.time()
    ctrs = {lvl: {} for lvl in LEVELS} if args.variants else {DEFAULT_LEVEL: {}}
    levels = tuple(ctrs)
    before, after = Accum(), Accum()
    hashes: list = []
    removed_by_asc: Counter = Counter()
    removed_by_n: Counter = Counter()
    runs_by_n: Counter = Counter()
    removed_by_mode: Counter = Counter()
    removed_wins = 0
    sub_counts: dict = {}
    sub_rows = 0

    out = None
    if not args.no_write and args.out:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        out = gzip.open(args.out, "wt", encoding="utf-8", compresslevel=6)

    n = 0
    try:
        with gzip.open(src_path, "rt", encoding="utf-8") as src:
            for line in src:
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    continue
                n += 1
                f = facets(row)
                sigs = signatures(f, levels)
                before.add(row)
                runs_by_n[len(row.get("pl") or [])] += 1
                if _is_calibration_row(row):
                    sub_rows += 1
                    s0 = sigs[DEFAULT_LEVEL]
                    sub_counts[s0] = sub_counts.get(s0, 0) + 1

                first = sigs[DEFAULT_LEVEL] not in ctrs[DEFAULT_LEVEL]
                for lvl in levels:
                    c = ctrs[lvl]
                    v = c.get(sigs[lvl])
                    if v is None:
                        c[sigs[lvl]] = [1, n]
                    else:
                        v[0] += 1

                if first:
                    after.add(row)
                    hashes.append(row.get("h"))
                    if out is not None:
                        out.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")
                else:
                    removed_by_asc[row.get("asc")] += 1
                    removed_by_n[len(row.get("pl") or [])] += 1
                    removed_by_mode[row.get("m")] += 1
                    removed_wins += row.get("w") or 0

                if args.limit and n >= args.limit:
                    break
                if args.progress and n % args.progress == 0:
                    print(f"  {n} lignes… ({time.time() - started:.0f}s)", file=sys.stderr)
    finally:
        if out is not None:
            out.close()

    elapsed = time.time() - started
    c = ctrs[DEFAULT_LEVEL]
    sizes = [v[0] for v in c.values()]
    groups = len(c)
    unique = groups
    dupes = sum(1 for s in sizes if s > 1)
    dist = Counter(_size_bucket(s) for s in sizes if s > 1)
    multi = sorted((s for s in sizes if s > 1), reverse=True)
    par_taille = {}
    for s in multi:
        b = _size_bucket(s)
        e = par_taille.setdefault(b, [0, 0])
        e[0] += 1
        e[1] += s - 1

    top = sorted(c.items(), key=lambda kv: kv[1][0], reverse=True)[: args.top]
    top_groups = [{
        "taille": v[0],
        "hash": hashes[v[1] - 1] if 0 < v[1] <= len(hashes) else None,
    } for _, v in top]

    report = {
        "genere_le": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "source": src_path,
        "sortie": args.out if not args.no_write else None,
        "signature_defaut": DEFAULT_LEVEL,
        "duree_s": round(elapsed, 1),
        "runs_lues": n,
        "runs_uniques": unique,
        "runs_retirees": n - unique,
        "pct_retirees": round(100 * (n - unique) / max(n, 1), 2),
        "groupes": groups,
        "groupes_avec_doublons": dupes,
        "pct_groupes_avec_doublons": round(100 * dupes / max(groups, 1), 2),
        "runs_dans_un_groupe": sum(sizes) - groups,
        "taille_max_groupe": max(sizes) if sizes else 0,
        "taille_moyenne_groupe_multiple": round(statistics.mean(multi), 2) if multi else 0,
        "mediane_groupe_multiple": statistics.median(multi) if multi else 0,
        "p90_groupe_multiple": _pct(multi, 90),
        "p99_groupe_multiple": _pct(multi, 99),
        "distribution_tailles_doublons": dict(sorted(dist.items(), key=lambda kv: -kv[1])),
        "retraits_par_taille_groupe": {
            b: {"groupes": e[0], "runs_retirees": e[1]}
            for b, e in sorted(par_taille.items(), key=lambda kv: _bucket_order(kv[0]))
        },
        "top_groupes": top_groups,
        "retraits_par_ascension": {str(k): v for k, v in sorted(
            removed_by_asc.items(), key=lambda kv: (kv[0] is None, kv[0]))},
        "retraits_par_n_joueurs": {str(k): v for k, v in sorted(removed_by_n.items())},
        "runs_par_n_joueurs": {str(k): v for k, v in sorted(runs_by_n.items())},
        "taux_retrait_par_n_joueurs": {
            str(k): round(100 * removed_by_n.get(k, 0) / v, 2)
            for k, v in sorted(runs_by_n.items())
        },
        "retraits_par_mode": dict(removed_by_mode.most_common()),
        "gagnees_retirees": removed_wins,
        "dedup_par_issue": {
            "gagnees": {
                "avant": before.wins, "apres": after.wins, "retirees": before.wins - after.wins,
                "pct_retirees": round(100 * (before.wins - after.wins) / max(before.wins, 1), 2),
            },
            "perdues": {
                "avant": before.runs - before.wins,
                "apres": after.runs - after.wins,
                "retirees": (before.runs - before.wins) - (after.runs - after.wins),
                "pct_retirees": round(100 * ((before.runs - before.wins) - (after.runs - after.wins))
                                      / max(before.runs - before.wins, 1), 2),
            },
        },
        "sous_corpus_calibration": {
            "definition": "solo, mode standard, personnages canoniques (5), deck ≤ 80",
            "runs": sub_rows,
            "signatures": len(sub_counts),
            "doublons_retires": sub_rows - len(sub_counts),
            "pct_retires": round(100 * (sub_rows - len(sub_counts)) / max(sub_rows, 1), 2),
        },
        "variantes": {},
        "avant": before.dump(),
        "apres": after.dump(),
    }
    for lvl in levels:
        cc = ctrs[lvl]
        u = len(cc)
        report["variantes"][lvl] = {
            "runs_uniques": u,
            "runs_retirees": n - u,
            "pct_retirees": round(100 * (n - u) / max(n, 1), 2),
            "taille_max_groupe": max((v[0] for v in cc.values()), default=0),
        }
    if args.report:
        os.makedirs(os.path.dirname(os.path.abspath(args.report)), exist_ok=True)
        with open(args.report, "w", encoding="utf-8") as fh:
            json.dump(report, fh, ensure_ascii=False, indent=2)
            fh.write("\n")

    print(f"lignes lues        : {n}")
    print(f"runs uniques       : {unique} ({100*unique/max(n,1):.1f} %)")
    print(f"runs retirées      : {n-unique} ({report['pct_retirees']:.1f} %)")
    print(f"groupes > 1        : {dupes}  taille max {report['taille_max_groupe']}")
    print(f"win rate           : {report['avant']['win_rate']:.2f} % → {report['apres']['win_rate']:.2f} %")
    if len(levels) > 1:
        print("variantes          : " + "  ".join(
            f"{k}={report['variantes'][k]['pct_retirees']:.1f}%" for k in levels))
    print(f"durée              : {elapsed:.0f}s")
    if args.report:
        print(f"rapport            : {args.report}")
    if args.out and not args.no_write:
        print(f"index dédupliqué   : {args.out} ({os.path.getsize(args.out)/1e6:.0f} Mo)")
    return 0


def _pct(values: list, p: float):
    if not values:
        return 0
    s = sorted(values)
    k = max(0, min(len(s) - 1, int(round((p / 100) * (len(s) - 1)))))
    return s[k]


# --------------------------------------------------------------------------- audit export
def _raw_signature(d: dict, with_upgrades: bool) -> tuple:
    """Signature depuis l'export brut + seed + hash (contrôle de la signature d'index)."""
    players = d.get("players") or []
    pl_facets = []
    last_hp: dict = {}
    picks = []
    acts = d.get("map_point_history") or []
    for a_i, act in enumerate(acts):
        floors = act if isinstance(act, list) else [act]
        for f_i, entry in enumerate(floors):
            if not isinstance(entry, dict):
                continue
            for ps in entry.get("player_stats") or []:
                if ps.get("current_hp") is not None:
                    last_hp[ps.get("player_id")] = (ps.get("current_hp"), ps.get("max_hp"))
                for c in ps.get("card_choices") or []:
                    cid = (c.get("card") or {}).get("id") or ""
                    if cid.startswith("CARD."):
                        cid = cid[5:]
                    picks.append(["card", a_i, f_i, entry.get("map_point_type"),
                                  ps.get("player_id"), cid if c.get("was_picked") else None])
                for r in ps.get("relic_choices") or []:
                    rid = (r.get("choice") or "")
                    if rid.startswith("RELIC."):
                        rid = rid[6:]
                    picks.append(["relic", a_i, f_i, entry.get("map_point_type"),
                                  ps.get("player_id"), rid if r.get("was_picked") else None])
    for p in players:
        deck = []
        for c in p.get("deck") or []:
            cid = (c.get("id") or "")
            if cid.startswith("CARD."):
                cid = cid[6:]
            lvl = c.get("current_upgrade_level") or 0
            deck.append(f"{cid}+{lvl}" if with_upgrades and lvl else cid)
        rel = []
        for r in p.get("relics") or []:
            rid = (r.get("id") or "")
            if rid.startswith("RELIC."):
                rid = rid[7:]
            rel.append(rid)
        hp = last_hp.get(p.get("id"), (None, None))
        pl_facets.append([p.get("character") or "", sorted(deck), sorted(rel), hp[0], hp[1]])
    head = [d.get("game_mode"), d.get("ascension"), len(players),
            int(bool(d.get("win"))), int(bool(d.get("was_abandoned"))), d.get("killed_by_encounter")]
    return _digest([head, pl_facets, picks]), d.get("seed"), d.get("run_hash")


def run_audit(args) -> int:
    """Vérifie sur l'export brut : doublons de contenu = même seed ? + effet des upgrades.

    Si deux lignes ont le même contenu mais des seeds différentes, ce sont deux parties
    distinctes (convergence) : la signature de contenu les fusionne à tort. Si elles ont
    la même seed, c'est une copie (re-upload) — la fusion est légitime.
    """
    started = time.time()
    groups_up: dict = {}
    groups_no: dict = {}
    seeds_seen: dict = {}
    n = 0
    with gzip.open(args.audit_export, "rt", encoding="utf-8") as src:
        for line in src:
            try:
                d = json.loads(line)
            except json.JSONDecodeError:
                continue
            n += 1
            s_up, seed, _ = _raw_signature(d, True)
            s_no, _, _ = _raw_signature(d, False)
            groups_up.setdefault(s_up, []).append(seed)
            groups_no.setdefault(s_no, []).append(seed)
            seeds_seen.setdefault(seed, set()).add(s_up)
            if args.audit_limit and n >= args.audit_limit:
                break

    def summarize(g):
        sizes = [len(v) for v in g.values()]
        dup = [(s, v) for s, v in g.items() if len(v) > 1]
        un_seed = sum(1 for _, v in dup if len(set(v)) == 1)
        multi_seed = len(dup) - un_seed
        runs_multi_seed = sum(len(v) for _, v in dup if len(set(v)) > 1)
        return {
            "runs": n,
            "signatures": len(g),
            "pct_doublons": round(100 * (n - len(g)) / max(n, 1), 2),
            "groupes_multiples": len(dup),
            "groupes_meme_seed": un_seed,
            "groupes_seeds_differentes": multi_seed,
            "runs_dans_groupes_seeds_differentes": runs_multi_seed,
            "pct_groupes_multiples_meme_seed": round(100 * un_seed / max(len(dup), 1), 2),
            "taille_max": max(sizes) if sizes else 0,
        }

    recap = {
        "audit_export": args.audit_export,
        "runs_lues": n,
        "seeds_distinctes": len(seeds_seen),
        "seeds_multi_signatures": sum(1 for s in seeds_seen.values() if len(s) > 1),
        "avec_upgrades": summarize(groups_up),
        "sans_upgrades": summarize(groups_no),
        "duree_s": round(time.time() - started, 1),
    }
    print(json.dumps(recap, ensure_ascii=False, indent=2))
    if args.audit_report:
        with open(args.audit_report, "w", encoding="utf-8") as fh:
            json.dump(recap, fh, ensure_ascii=False, indent=2)
            fh.write("\n")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--in", dest="input", default="data/index/runs-v0.107.1.jsonl.gz")
    ap.add_argument("--out", default="data/index/deduped-v0.107.1.jsonl.gz")
    ap.add_argument("--report", default="data/index/dedup-report.json")
    ap.add_argument("--limit", type=int, default=0, help="0 = tout l'index")
    ap.add_argument("--top", type=int, default=20, help="nb de groupes listés dans le rapport")
    ap.add_argument("--variants", action="store_true", default=True,
                    help="calcule aussi les niveaux final/strict (défaut)")
    ap.add_argument("--no-variants", dest="variants", action="store_false")
    ap.add_argument("--no-write", action="store_true", help="ne pas écrire l'index dédupliqué")
    ap.add_argument("--progress", type=int, default=100000)
    ap.add_argument("--audit-export", default=None)
    ap.add_argument("--audit-limit", type=int, default=100000)
    ap.add_argument("--audit-report", default=None)
    args = ap.parse_args()
    if args.audit_export:
        return run_audit(args)
    return run_dedup(args)


if __name__ == "__main__":
    sys.exit(main())
