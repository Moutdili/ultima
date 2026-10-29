#!/usr/bin/env python3
"""CLI du harnais de backtest hors ligne.

    python3 -I tools/backtest/run.py --runs 100000 --policy highest_meta --out /tmp/bt.json

`-I` (mode isolé) retire le dossier du script de `sys.path` : le bootstrap
ci-dessous le remet explicitement, sinon `import loader` échoue. Les modules
voisins sont importés par nom simple pour rester utilisables aussi bien en
script qu'en module (`python3 -m tools.backtest.run`).
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from datetime import datetime, timezone

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import loader  # noqa: E402
import policies  # noqa: E402
from metrics import Metrics  # noqa: E402

REPO_ROOT = os.path.dirname(os.path.dirname(_HERE))
DEFAULT_INDEX = os.path.join(REPO_ROOT, "data", "index", "runs-v0.107.1.jsonl.gz")
DEFAULT_META = os.path.join(REPO_ROOT, "data", "meta", "card_tiers.json")
DEFAULT_BUILD = "v0.107.1"
TARGET_RUNS = 100_000
TARGET_SECONDS = 120.0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="run.py",
        description="Backtest hors ligne d'une politique de choix de carte "
        "sur l'index de runs (corrélation, pas causalité).",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--runs", type=int, default=TARGET_RUNS,
                        help="nombre de runs retenus à traiter (0 = tout l'index)")
    parser.add_argument("--policy", default="highest_meta",
                        help="politique : " + ", ".join(sorted(policies.POLICIES)))
    parser.add_argument("--out", default=None, help="écrit les métriques JSON à ce chemin")
    parser.add_argument("--print-json", action="store_true",
                        help="écrit le JSON complet sur stdout")
    parser.add_argument("--index", default=DEFAULT_INDEX, help="index .jsonl(.gz)")
    parser.add_argument("--meta", default=DEFAULT_META, help="card_tiers.json")
    parser.add_argument("--mode", default="standard",
                        help="standard | daily | custom | all")
    parser.add_argument("--ascensions", default=None,
                        help="liste explicite, ex. 0,9,10 (prioritaire sur --min/--max)")
    parser.add_argument("--min-asc", type=int, default=None, dest="min_asc")
    parser.add_argument("--max-asc", type=int, default=None, dest="max_asc")
    parser.add_argument("--players", default="all", help="solo | coop | all")
    parser.add_argument("--build", default=None,
                        help=f"build exact (défaut : aucun filtre, index {DEFAULT_BUILD})")
    parser.add_argument("--allow-modded-characters", action="store_true",
                        help="accepte les personnages moddés (hors tables de méta)")
    parser.add_argument("--seed", type=int, default=7, help="graine de la politique random")
    parser.add_argument("--skip-below", type=float, default=None, dest="skip_below",
                        help="seuil de note sous lequel highest_meta conseille de passer")
    parser.add_argument("--exclude-abandoned", action="store_true",
                        help="exclut les runs abandonnés du calcul de win rate")
    parser.add_argument("--min-decisions-run", type=int, default=10,
                        dest="min_decisions_run",
                        help="décisions avec avis minimales pour la tranche run-level "
                             "`guarded` (neutralise les runs courts/abandonnés)")
    parser.add_argument("--list-policies", action="store_true",
                        help="liste les politiques disponibles et sort")
    parser.add_argument("--quiet", action="store_true", help="pas de résumé humain")
    return parser


def _pct(value: float | None) -> str:
    if value is None:
        return "n/a"
    return f"{100 * value:.1f} %".replace(".", ",")


def _num(value: int | float | None) -> str:
    if value is None:
        return "n/a"
    return f"{value:,}".replace(",", " ")


def _mean(value: float | None) -> str:
    if value is None:
        return "n/a"
    return f"{value:.1f}".replace(".", ",")


def _pp(value: float | None) -> str:
    if value is None:
        return "n/a"
    return f"{value:+.1f} pp".replace(".", ",")


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.runs < 0:
        parser.error("--runs doit être >= 0")
    if args.list_policies:
        for name in sorted(policies.POLICIES):
            print(name)
        parser.exit(0)
    if not os.path.exists(args.index):
        parser.error(
            f"index introuvable : {args.index}\n"
            "  il est gitignoré : `python3 -I tools/index_runs.py --help`"
        )
    if args.policy == "highest_meta" and not os.path.exists(args.meta):
        parser.error(f"méta introuvable : {args.meta}")
    return args


def make_filter(args: argparse.Namespace) -> loader.RunFilter:
    ascensions = None
    if args.ascensions:
        try:
            ascensions = frozenset(
                int(part) for part in args.ascensions.replace(" ", "").split(",") if part
            )
        except ValueError:
            raise SystemExit(f"--ascensions invalide : {args.ascensions!r}")
    return loader.RunFilter(
        mode=args.mode,
        ascensions=ascensions,
        min_ascension=args.min_asc,
        max_ascension=args.max_asc,
        players=args.players,
        build=args.build,
        canonical_only=not args.allow_modded_characters,
    )


def make_policy(args: argparse.Namespace):
    kwargs: dict = {}
    if args.policy == "random":
        kwargs["seed"] = args.seed
    else:
        kwargs["skip_below"] = args.skip_below
    return policies.make_policy(args.policy, meta_path=args.meta, **kwargs)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    if args.policy not in policies.POLICIES:
        raise SystemExit(
            f"politique inconnue : {args.policy!r} "
            f"(disponibles : {', '.join(sorted(policies.POLICIES))})"
        )

    run_filter = make_filter(args)
    policy = make_policy(args)
    metrics = Metrics(
        policy_name=policy.name,
        policy_description=policy.describe(),
        filters=run_filter.describe(),
        exclude_abandoned=args.exclude_abandoned,
        min_opined_for_run_split=args.min_decisions_run,
    )

    limit = None if args.runs == 0 else args.runs
    stats: dict = {}
    started = time.perf_counter()
    scan_started = None
    for run in loader.iter_runs(
        args.index, run_filter=run_filter, limit=limit, kinds=("card",), stats=stats
    ):
        if scan_started is None:
            scan_started = time.perf_counter()
        metrics.score_run(run, policy)
        if not args.quiet and stats["used"] % 100_000 == 0:
            print(
                f"  … {_num(stats['used'])} runs retenus "
                f"({_num(stats['scanned'])} lus, {time.perf_counter() - started:.0f} s)",
                file=sys.stderr,
            )
    scan_seconds = time.perf_counter() - (scan_started or started)
    total_seconds = time.perf_counter() - started

    document = build_document(args, policy, run_filter, metrics, stats,
                              scan_seconds, total_seconds)

    if args.out:
        directory = os.path.dirname(os.path.abspath(args.out))
        if directory:
            os.makedirs(directory, exist_ok=True)
        with open(args.out, "w", encoding="utf-8") as handle:
            json.dump(document, handle, ensure_ascii=False, indent=2)
            handle.write("\n")

    if not args.quiet:
        print_summary(document, args)
    if args.print_json:
        json.dump(document, sys.stdout, ensure_ascii=False, indent=2)
        sys.stdout.write("\n")
    return 0


def build_document(args, policy, run_filter, metrics, stats, scan_seconds, total_seconds):
    used = stats.get("used", 0)
    runs_per_second = used / scan_seconds if scan_seconds > 0 else None
    within_target = None
    if args.runs and args.runs >= TARGET_RUNS:
        within_target = total_seconds <= TARGET_SECONDS
    return {
        "tool": "tools/backtest/run.py",
        "generated": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "policy": policy.describe(),
        "filters": run_filter.describe(),
        "index": {
            "path": args.index,
            "size_bytes": os.path.getsize(args.index),
            "scanned": stats.get("scanned", 0),
            "used": used,
            "rejected": stats.get("rejected", 0),
            "malformed": stats.get("malformed", 0),
            "modded_players": stats.get("modded_players", 0),
            "no_players": stats.get("no_players", 0),
            "limit": None if args.runs == 0 else args.runs,
        },
        "timing": {
            "seconds_scan": round(scan_seconds, 3),
            "seconds_total": round(total_seconds, 3),
            "runs_per_second": round(runs_per_second, 1) if runs_per_second else None,
            "decisions_per_second": (
                round(metrics.totals.decisions / scan_seconds, 1)
                if scan_seconds > 0
                else None
            ),
            "target_runs": TARGET_RUNS,
            "target_seconds": TARGET_SECONDS,
            "within_target": within_target,
        },
        "metrics": metrics.to_dict(),
        "caveat": (
            "corrélation, pas causalité : les runs où la politique est d'accord ne sont "
            "pas un échantillon aléatoire (biais de sélection, compétence du joueur, "
            "archétype). Aucun effet causal n'est mesuré ici."
        ),
    }


def print_summary(document: dict, args) -> None:
    index = document["index"]
    timing = document["timing"]
    decisions = document["metrics"]["decisions"]
    run_level = document["metrics"]["run_level"]
    runs = document["metrics"]["runs"]

    print(f"=== backtest {document['policy']['name']} ===")
    print(f"index    {index['path']} ({index['size_bytes'] / 1e6:.1f} Mo)")
    filters = document["filters"]
    print(
        "filtres  mode={mode} asc={asc} joueurs={players} build={build} canoniques={canon}"
        .format(
            mode=filters["mode"] or "all",
            asc="all" if filters["ascensions"] is None else ",".join(
                str(a) for a in filters["ascensions"]
            ),
            players=filters["players"] or "all",
            build=filters["build"] or "all",
            canon="non" if not filters["canonical_only"]
            else "oui",
        )
    )
    print(
        f"runs     {_num(index['used'])} retenus / {_num(index['scanned'])} lus "
        f"en {timing['seconds_total']:.1f} s ({_num(timing['runs_per_second'])} runs/s)"
    )
    if timing["within_target"] is not None:
        verdict = "OK" if timing["within_target"] else "DÉPASSÉ"
        print(
            f"cible    {_num(timing['target_runs'])} runs < {timing['target_seconds']:.0f} s : "
            f"{verdict} ({timing['seconds_total']:.1f} s)"
        )
    print(
        f"décisions {_num(decisions['decisions'])} cartes — avis "
        f"{_num(decisions['opined'])}, abstention {_pct(decisions['abstain_rate'])}"
    )
    print(
        f"accord   {_pct(decisions['agreement_rate'])} "
        f"(prises {_pct(decisions['pick']['rate'])} sur {_num(decisions['pick']['opined'])}, "
        f"skips {_pct(decisions['skip']['rate'])} sur {_num(decisions['skip']['opined'])})"
    )
    print(
        f"skip     humain {_pct(decisions['human_skip_rate'])} | "
        f"conseillé {_pct(decisions['policy_skip_rate'])}"
    )
    contexts = document["metrics"].get("by_context", {})
    reward = contexts.get("reward", {})
    shop = contexts.get("shop", {})
    if reward or shop:
        print(
            f"accord*  récompense 3 cartes {_pct(reward.get('agreement_rate'))} "
            f"({_num(reward.get('opined'))} déc.) | boutique {_pct(shop.get('agreement_rate'))} "
            f"({_num(shop.get('opined'))} déc.)"
        )
    win = decisions["win_rate"]
    print(
        f"win/déc  accord {_pct(win['agree']['rate'])} "
        f"({_num(win['agree']['decisions'])} déc.) vs désaccord {_pct(win['disagree']['rate'])} "
        f"({_num(win['disagree']['decisions'])} déc.) : {_pp(win['delta_pp'])}"
    )
    threshold = f"accord≥{run_level['threshold']:.0%}"
    for key, label in (("all", f"win/run  {threshold}"), ("guarded", f"win/run* {threshold}")):
        split = run_level[key]
        print(
            f"{label} : {_pct(split['agree']['win_rate'])} "
            f"({_num(split['agree']['runs'])} runs) vs désaccord "
            f"{_pct(split['disagree']['win_rate'])} ({_num(split['disagree']['runs'])} runs) : "
            f"{_pp(split['delta_pp'])}"
        )
    guarded = run_level["guarded"]
    print(
        f"         * non abandonnés, ≥{run_level['min_opined']} décisions avec avis "
        f"(longueur moyenne {_mean(guarded['agree']['mean_opined'])} vs "
        f"{_mean(guarded['disagree']['mean_opined'])} décisions)"
    )
    print(f"réf      win rate global du corpus filtré : {_pct(runs['win_rate'])}")
    print("note     corrélation, pas causalité")
    if args.out:
        print(f"json     {args.out}")


if __name__ == "__main__":
    sys.exit(main())
