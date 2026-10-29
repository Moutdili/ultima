#!/usr/bin/env python3
"""Tests du harnais de backtest — fixture synthétique de 3 runs, sans réseau.

    python3 -I tools/backtest/selftest.py

Les valeurs attendues sont calculées à la main sur le fixture (voir les
commentaires) : le test ne compare pas le code à lui-même. Il vérifie aussi le
vrai `data/meta/card_tiers.json` quand il est présent (piège des clés JSON
`CamelCase` vs identifiants `UPPER_SNAKE`), et le CLI en mode `-I` via
sous-processus — c'est ce mode qui casse `sys.path` si le bootstrap manque.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import loader  # noqa: E402
import policies  # noqa: E402
from metrics import Metrics  # noqa: E402

REPO_ROOT = os.path.dirname(os.path.dirname(_HERE))
FIXTURE = os.path.join(_HERE, "fixtures", "synthetic-runs.jsonl")
RUN_PY = os.path.join(_HERE, "run.py")
REAL_META = os.path.join(REPO_ROOT, "data", "meta", "card_tiers.json")

#: Table de méta du fixture. Les clés sont des identifiants d'index ; `colorless`
#: sert de repli pour les cartes sans propriétaire (ALCHEMIZE).
META_DOC = {
    "silent": {
        "NOXIOUS_FUMES": 56,
        "LEADING_STRIKE": 40,
        "POISONED_STAB": 45,
        "BUBBLE_BUBBLE": 50,
    },
    "defect": {"DEFECT_ZAP": 30, "DEFECT_BALL_LIGHTNING": 60},
    "colorless": {"ALCHEMIZE": 70},
}

#: Accord attendu, décision par décision (politique = meilleure note offerte,
#: ALCHEMIZE via colorless, jamais de skip) :
#:   r1d1 NOXIOUS/NOXIOUS  accord   (w=1)
#:   r1d2 POISONED/skip    désaccord
#:   r1d3 ALCHEMIZE/BUBBLE désaccord
#:   r2d1 NOXIOUS/LEADING  désaccord (w=0)
#:   r2d2 (aucune notée)   abstention
#:   r2d3 POISONED/POISONED accord
#:   r3d1 NOXIOUS/NOXIOUS  accord   (w=1)
#:   r3d2 BALL_LIGHTNING/skip désaccord
EXPECTED = {
    "decisions": 8,
    "opined": 7,
    "abstained": 1,
    "agree": 3,
    "agreement_rate": 0.4286,
    "pick_opined": 5,
    "pick_agree": 3,
    "pick_rate": 0.6,
    "skip_opined": 2,
    "skip_agree": 0,
    "skip_rate": 0.0,
    "human_pick": 6,
    "human_skip": 2,
    "abstain_rate": 0.125,
    "win_agree_n": 3,
    "win_agree_rate": 0.6667,
    "win_disagree_n": 4,
    "win_disagree_rate": 0.75,
    "win_delta_pp": -8.33,
    "run_agree_runs": 2,
    "run_agree_rate": 0.5,
    "run_disagree_runs": 1,
    "run_disagree_rate": 1.0,
    "run_delta_pp": -50.0,
}

_failures: list[str] = []
_checks = 0


def check(label: str, condition: bool, detail: str = "") -> None:
    global _checks
    _checks += 1
    if condition:
        print(f"  ok   {label}")
    else:
        print(f"  FAIL {label}" + (f" — {detail}" if detail else ""))
        _failures.append(label)


def eq(label: str, actual, expected) -> None:
    check(label, actual == expected, f"attendu {expected!r}, obtenu {actual!r}")


def load_fixture(kinds=None):
    stats: dict = {}
    runs = list(loader.iter_runs(FIXTURE, kinds=kinds, stats=stats))
    return runs, stats


def meta() -> policies.CardMeta:
    return policies.CardMeta.from_doc(META_DOC)


# --------------------------------------------------------------------------- #
def test_loader() -> None:
    print("loader")
    runs, stats = load_fixture()
    eq("3 runs lus", len(runs), 3)
    eq("stats scanned", stats["scanned"], 3)
    eq("stats used", stats["used"], 3)
    eq("stats malformed", stats["malformed"], 0)

    by_hash = {run.hash: run for run in runs}
    r1 = by_hash["r1"]
    eq("r1 joueurs", len(r1.players), 1)
    eq("r1 personnage normalisé", r1.character, "silent")
    eq("r1 raw personnage", r1.players[0].raw_character, "CHARACTER.SILENT")
    eq("r1 solo", r1.solo, True)
    eq("r1 décisions (cartes + relique)", len(r1.decisions), 4)
    eq("r1 décisions cartes", len(list(r1.card_decisions())), 3)
    eq("r1 deck", len(r1.players[0].deck), 3)
    eq("r1 or de la 1re décision", r1.decisions[0].gold, 99)
    eq("r1 skip détecté", r1.decisions[1].skipped, True)
    eq("r1 pick non-skip", r1.decisions[0].skipped, False)
    eq("r1 relique prise", r1.decisions[3].relic_picked, "BOOMING_CONCH")
    eq("r1 relique proposée", r1.decisions[3].relics_offered, ("BOOMING_CONCH",))

    r3 = by_hash["r3"]
    eq("r3 co-op", r3.coop, True)
    eq("r3 joueurs", r3.n_players, 2)
    player_two = r3.player("2")
    eq("r3 joueur 2 present", player_two is not None, True)
    eq("r3 joueur 2 defect", player_two.character if player_two else None, "defect")
    eq("r3 joueur inconnu", r3.player("9"), None)
    player_two_int = r3.player(2)
    eq("r3 joueur 2 par id entier", player_two_int.character if player_two_int else None, "defect")

    cards_only, _ = load_fixture(kinds=("card",))
    eq("kinds=card filtre les reliques", len(cards_only[0].decisions), 3)


def test_filters() -> None:
    print("filtres")

    def count(**kwargs):
        stats: dict = {}
        runs = list(loader.iter_runs(FIXTURE, run_filter=loader.RunFilter(**kwargs), stats=stats))
        return len(runs), stats

    eq("défaut (standard, canoniques)", count()[0], 3)
    eq("mode daily", count(mode="daily")[0], 0)
    eq("mode all", count(mode="all")[0], 3)
    eq("ascension 10", count(ascensions=frozenset({10}))[0], 2)
    eq("min_asc 10", count(min_ascension=10)[0], 2)
    eq("max_asc 0", count(max_ascension=0)[0], 1)
    eq("solo", count(players="solo")[0], 2)
    eq("coop", count(players="coop")[0], 1)
    eq("coop + asc 10", count(players="coop", min_ascension=10)[0], 1)
    eq("build inexistant", count(build="v0.999.0")[0], 0)
    eq("build réel", count(build="v0.107.1")[0], 3)
    eq("limite 2 runs", loader.iter_runs(FIXTURE, limit=2).__next__().hash, "r1")
    eq("limite 2 runs (suite)", len(list(loader.iter_runs(FIXTURE, limit=2))), 2)

    stats: dict = {}
    list(loader.iter_runs(FIXTURE, run_filter=loader.RunFilter(mode="daily"), stats=stats))
    eq("daily : tout rejeté", stats["rejected"], 3)

    try:
        loader.RunFilter(mode="ranked")
        check("mode inconnu rejeté", False)
    except ValueError:
        check("mode inconnu rejeté", True)


def test_policies() -> None:
    print("politiques")
    table = meta()
    highest = policies.HighestMeta(table)
    runs, _ = load_fixture()
    r1, r2, r3 = runs

    eq("méta : NOXIOUS silent", table.score("NOXIOUS_FUMES", "silent"), 56.0)
    eq("méta : colorless pour silent", table.score("ALCHEMIZE", "silent"), 70.0)
    eq("méta : carte non notée", table.score("UNRATED_ALPHA", "silent"), None)
    eq("méta : perso non concerné", table.score("NOXIOUS_FUMES", "defect"), None)
    eq("méta : insensible à la casse", table.score("noxious_fumes", "silent"), 56.0)

    eq("r1d1 prend la meilleure note", highest.choose(r1.decisions[0], r1), "NOXIOUS_FUMES")
    eq("r2d1 prend la meilleure note", highest.choose(r2.decisions[0], r2), "NOXIOUS_FUMES")
    eq("r2d2 s'abstient", highest.choose(r2.decisions[1], r2), None)
    eq("repli colorless", highest.choose(r1.decisions[2], r1), "ALCHEMIZE")
    eq("scope personnage (defect)", highest.choose(r3.decisions[1], r3), "DEFECT_BALL_LIGHTNING")

    # Une carte d'un autre personnage n'est pas notée pour ce joueur : abstention,
    # pas de note empruntée.
    foreign = loader.Decision(
        type="card", act=0, floor=1, room_type="monster", player_id="2",
        hp=60, max_hp=75, gold=0, offered=("NOXIOUS_FUMES",),
        picked="NOXIOUS_FUMES",
    )
    eq("pas de note empruntée", highest.choose(foreign, r3), None)

    # Départage déterministe : mêmes notes → ordre alphabétique.
    tie = loader.Decision(
        type="card", act=0, floor=1, room_type="monster", player_id="1",
        hp=70, max_hp=70, gold=0, offered=("POISONED_STAB", "BUBBLE_BUBBLE"),
        picked=None,
    )
    eq("égalité départagée par id", highest.choose(tie, r1), "BUBBLE_BUBBLE")

    # Skip optionnel : 56 < 57 → passer ; 70 > 57 → prendre.
    skipping = policies.HighestMeta(table, skip_below=57)
    eq("skip si note < seuil", skipping.decide(r1.decisions[0], r1), (None, True))
    eq("pas de skip au-dessus du seuil", skipping.decide(r1.decisions[2], r1), ("ALCHEMIZE", False))
    eq("baseline sans modèle de skip", highest.has_skip_model, False)
    eq("skip_below active le modèle", skipping.has_skip_model, True)

    # Contrôle aléatoire : reproductible et toujours dans l'offre.
    first = policies.RandomPolicy(seed=3)
    second = policies.RandomPolicy(seed=3)
    picks_a = [first.choose(d, r1) for d in r1.card_decisions()]
    picks_b = [second.choose(d, r1) for d in r1.card_decisions()]
    eq("random reproductible", picks_a, picks_b)
    check(
        "random dans l'offre",
        all(pick in d.offered for pick, d in zip(picks_a, r1.card_decisions())),
        str(picks_a),
    )
    eq("resolution : skip non conseillé", policies.resolve(highest, r1.decisions[1], r1), "POISONED_STAB")


def score_fixture() -> dict:
    table = meta()
    highest = policies.HighestMeta(table)
    runs, _ = load_fixture()
    accumulator = Metrics(
        policy_name=highest.name,
        policy_description=highest.describe(),
        min_opined_for_run_split=0,
    )
    for run in runs:
        accumulator.score_run(run, highest)
    return accumulator.to_dict()


def test_metrics() -> None:
    print("métriques")
    document = score_fixture()
    decisions = document["decisions"]
    eq("total décisions", decisions["decisions"], EXPECTED["decisions"])
    eq("avec avis", decisions["opined"], EXPECTED["opined"])
    eq("abstentions", decisions["abstained"], EXPECTED["abstained"])
    eq("taux d'abstention", decisions["abstain_rate"], EXPECTED["abstain_rate"])
    eq("accords", decisions["agree"], EXPECTED["agree"])
    eq("taux d'accord", decisions["agreement_rate"], EXPECTED["agreement_rate"])
    eq("prises avec avis", decisions["pick"]["opined"], EXPECTED["pick_opined"])
    eq("accord sur prise", decisions["pick"]["agree"], EXPECTED["pick_agree"])
    eq("taux accord prise", decisions["pick"]["rate"], EXPECTED["pick_rate"])
    eq("skips avec avis (abstention exclue)", decisions["skip"]["opined"], EXPECTED["skip_opined"])
    eq("accord sur skip", decisions["skip"]["agree"], EXPECTED["skip_agree"])
    eq("taux accord skip", decisions["skip"]["rate"], EXPECTED["skip_rate"])
    eq("picks humains", decisions["human_pick"], EXPECTED["human_pick"])
    eq("skips humains", decisions["human_skip"], EXPECTED["human_skip"])
    eq("skip conseillé (aucun)", decisions["policy_skip"], 0)
    eq("taux de skip humain", decisions["human_skip_rate"], round(2 / 7, 4))

    win = decisions["win_rate"]
    eq("win accord : décisions", win["agree"]["decisions"], EXPECTED["win_agree_n"])
    eq("win accord : taux", win["agree"]["rate"], EXPECTED["win_agree_rate"])
    eq("win désaccord : décisions", win["disagree"]["decisions"], EXPECTED["win_disagree_n"])
    eq("win désaccord : taux", win["disagree"]["rate"], EXPECTED["win_disagree_rate"])
    eq("win delta pp", win["delta_pp"], EXPECTED["win_delta_pp"])

    run_level = document["run_level"]
    all_split = run_level["all"]
    eq("runs d'accord", all_split["agree"]["runs"], EXPECTED["run_agree_runs"])
    eq("win runs d'accord", all_split["agree"]["win_rate"], EXPECTED["run_agree_rate"])
    eq("runs en désaccord", all_split["disagree"]["runs"], EXPECTED["run_disagree_runs"])
    eq("win runs désaccord", all_split["disagree"]["win_rate"], EXPECTED["run_disagree_rate"])
    eq("run delta pp", all_split["delta_pp"], EXPECTED["run_delta_pp"])
    eq("longueur moyenne accord", all_split["agree"]["mean_opined"], 2.0)
    eq("longueur moyenne désaccord", all_split["disagree"]["mean_opined"], 3.0)
    eq("excl_abandoned = all (aucun abandon)", run_level["excl_abandoned"]["agree"]["runs"], 2)

    by_act = document["by_act"]
    eq("acte 0 : décisions", by_act["0"]["decisions"], 6)
    eq("acte 0 : avis", by_act["0"]["opined"], 5)
    eq("acte 0 : accord", by_act["0"]["agree"], 3)
    eq("acte 0 : taux", by_act["0"]["agreement_rate"], 0.6)
    eq("acte 0 : picks humains", by_act["0"]["human_pick"], 5)
    eq("acte 1 : décisions", by_act["1"]["decisions"], 2)
    eq("acte 1 : accord", by_act["1"]["agree"], 0)
    eq("acte 1 : win désaccord", by_act["1"]["win_rate"]["disagree"]["rate"], 1.0)
    eq("acte 1 : win accord indéfini", by_act["1"]["win_rate"]["agree"]["rate"], None)

    by_asc = document["by_ascension"]
    eq("asc 10 : décisions", by_asc["10"]["decisions"], 5)
    eq("asc 10 : accord", by_asc["10"]["agreement_rate"], 0.4)
    eq("asc 0 : décisions", by_asc["0"]["decisions"], 3)
    eq("asc 0 : abstentions", by_asc["0"]["abstained"], 1)
    eq("asc 0 : accord", by_asc["0"]["agreement_rate"], 0.5)

    eq("silent : décisions", document["by_character"]["silent"]["decisions"], 7)
    # silent : 7 décisions (3 runs), 6 avec avis (r2d2 abstention), 3 accords
    # (r1d1, r2d3, r3d1) → 0.5. defect : r3d2 seulement.
    eq("silent : accord", document["by_character"]["silent"]["agreement_rate"], 0.5)
    eq("defect : décisions", document["by_character"]["defect"]["decisions"], 1)
    eq("solo : décisions", document["by_players"]["solo"]["decisions"], 6)
    eq("coop : décisions", document["by_players"]["coop"]["decisions"], 2)
    eq("coop : accord", document["by_players"]["coop"]["agreement_rate"], 0.5)
    eq("salle monster : décisions", document["by_room"]["monster"]["decisions"], 4)
    eq("salle monster : opined", document["by_room"]["monster"]["opined"], 3)
    eq("salle shop : décisions", document["by_room"]["shop"]["decisions"], 2)
    eq("salle unknown : accord", document["by_room"]["unknown"]["agreement_rate"], 1.0)
    # Contexte : r1d1 seule récompense à 3 cartes ; r1d3 et r3d2 boutiques ;
    # le reste = offres à 2 cartes (taille inhabituelle) → `other`.
    eq("contexte reward : décisions", document["by_context"]["reward"]["decisions"], 1)
    eq("contexte reward : accord", document["by_context"]["reward"]["agreement_rate"], 1.0)
    eq("contexte shop : décisions", document["by_context"]["shop"]["decisions"], 2)
    eq("contexte shop : accord", document["by_context"]["shop"]["agreement_rate"], 0.0)
    eq("contexte other : décisions", document["by_context"]["other"]["decisions"], 5)

    noxious = document["per_card"]["NOXIOUS_FUMES"]
    eq("carte NOXIOUS offerte", noxious["offered"], 3)
    eq("carte NOXIOUS prise par la politique", noxious["policy_pick"], 3)
    eq("carte NOXIOUS prise par l'humain", noxious["human_pick"], 2)
    eq("carte NOXIOUS commune", noxious["both"], 2)
    eq("carte NOXIOUS précision", noxious["precision"], 0.6667)
    eq("carte LEADING_STRIKE offerte", document["per_card"]["LEADING_STRIKE"]["offered"], 5)
    eq("carte POISONED_STAB prise", document["per_card"]["POISONED_STAB"]["policy_pick"], 2)
    eq("carte non notée comptée", document["per_card"]["UNRATED_ALPHA"]["offered"], 1)

    runs = document["runs"]
    eq("runs évalués", runs["scored"], 3)
    eq("win rate global", runs["win_rate"], 0.6667)  # r1 et r3 gagnés
    eq("runs sans avis", runs["runs_without_opinion"], 0)

    check("notes : corrélation", any("corrélation" in note for note in document["notes"]))
    check("JSON sérialisable", isinstance(json.dumps(document, ensure_ascii=False), str))


def test_skip_metrics() -> None:
    """Un modèle de skip déplace les compteurs : accord sur skip, pas sur abstention."""
    print("métriques + skip")
    table = meta()
    skipping = policies.HighestMeta(table, skip_below=57)
    runs, _ = load_fixture()
    accumulator = Metrics(policy_name="highest_meta@57")
    for run in runs:
        accumulator.score_run(run, skipping)
    decisions = accumulator.to_dict()["decisions"]
    # Best-scores : r1d1 56 → skip, r1d2 45 → skip, r1d3 70 → pick,
    # r2d1 56 → skip, r2d2 abstention, r2d3 45 → skip, r3d1 56 → skip,
    # r3d2 60 → pick. Donc 5 skips conseillés, 2 picks, 1 abstention.
    eq("skips conseillés", decisions["policy_skip"], 5)
    eq("picks conseillés", decisions["opined"] - decisions["policy_skip"], 2)
    eq("skips humains", decisions["skip"]["opined"], 2)
    # Seuls r1d2 (45) et r3d2 (60) sont des skips humains : r1d2 d'accord,
    # r3d2 non (la politique prend BALL_LIGHTNING à 60).
    eq("accord skip quand les deux passent", decisions["skip"]["agree"], 1)


def test_run_level_guard() -> None:
    """La tranche `guarded` écarte les runs trop courts (confondant de longueur)."""
    print("run-level gardé")
    highest = policies.HighestMeta(meta())
    runs, _ = load_fixture()
    accumulator = Metrics(policy_name="guarded", min_opined_for_run_split=3)
    for run in runs:
        accumulator.score_run(run, highest)
    document = accumulator.to_dict()["run_level"]
    eq("guarded : seuil publié", document["min_opined"], 3)
    # r1 (3 décisions avec avis) → désaccord ; r2 et r3 (2) sont sous le seuil.
    eq("guarded : runs d'accord", document["guarded"]["agree"]["runs"], 0)
    eq("guarded : runs en désaccord", document["guarded"]["disagree"]["runs"], 1)
    eq("guarded : win désaccord", document["guarded"]["disagree"]["win_rate"], 1.0)
    eq("guarded : win accord indéfini", document["guarded"]["agree"]["win_rate"], None)
    eq("runs sous le seuil", document["runs_below_min_opined"], 2)
    check("run_level explique le biais", "bruitée" in document["note"])


def test_real_meta() -> None:
    print("méta réelle")
    if not os.path.exists(REAL_META):
        print(f"  skip data/meta absent ({REAL_META})")
        return
    with open(REAL_META, "r", encoding="utf-8") as handle:
        doc = json.load(handle)
    table = policies.CardMeta.from_file(REAL_META)
    check("≥ 400 cartes notées", len(table) >= 400, str(len(table)))
    check("personnages canoniques présents", all(c in table.characters() for c in loader.CANONICAL_CHARACTERS))

    # Piège vérifié : la clé JSON est le nom de classe, l'index emploie `id`.
    mismatched = [
        (key, entry["id"])
        for cards in doc["characters"].values()
        for key, entry in cards.items()
        if isinstance(entry, dict) and entry.get("id") not in (None, key)
    ]
    check("clés JSON ≠ identifiants (piège réel)", len(mismatched) > 0, str(len(mismatched)))
    if mismatched:
        key, card_id = mismatched[0]
        check(
            f"résolution par id ({card_id}, clé {key})",
            table.score(card_id) is not None,
        )
        check(
            "résolution par id insensible à la casse",
            table.score(card_id.lower()) == table.score(card_id),
        )


def test_cli() -> None:
    print("CLI (-I, sous-processus)")
    meta_path = os.path.join(tempfile.mkdtemp(prefix="bt-meta-"), "card_tiers.json")
    with open(meta_path, "w", encoding="utf-8") as handle:
        json.dump({"characters": {
            character: {
                card_id: {"id": card_id, "consensus": {"score": score, "n_sources": 1}}
                for card_id, score in cards.items()
            }
            for character, cards in META_DOC.items()
        }}, handle)

    out_path = os.path.join(tempfile.mkdtemp(prefix="bt-out-"), "bt.json")
    result = subprocess.run(
        [sys.executable, "-I", RUN_PY,
         "--index", FIXTURE, "--meta", meta_path, "--runs", "0",
         "--out", out_path, "--quiet"],
        capture_output=True, text=True, timeout=120,
    )
    eq("code de sortie", result.returncode, 0)
    if result.returncode != 0:
        print(result.stdout[-2000:])
        print(result.stderr[-2000:])
        return
    with open(out_path, "r", encoding="utf-8") as handle:
        document = json.load(handle)
    eq("CLI runs retenus", document["index"]["used"], 3)
    eq("CLI accord", document["metrics"]["decisions"]["agreement_rate"], EXPECTED["agreement_rate"])
    eq("CLI limite", document["index"]["limit"], None)
    check("CLI caveat corrélation", "corrélation" in document["caveat"])
    check(
        "CLI timing mesuré",
        isinstance(document["timing"]["seconds_total"], float)
        and document["timing"]["seconds_total"] >= 0,
        str(document["timing"]),
    )

    listed = subprocess.run(
        [sys.executable, "-I", RUN_PY, "--list-policies"],
        capture_output=True, text=True, timeout=60,
    )
    eq("CLI --list-policies exit", listed.returncode, 0)
    eq(
        "CLI --list-policies complet",
        sorted(listed.stdout.split()),
        sorted(policies.POLICIES),
    )

    limited = subprocess.run(
        [sys.executable, "-I", RUN_PY, "--index", FIXTURE, "--meta", meta_path,
         "--runs", "2", "--players", "solo"],
        capture_output=True, text=True, timeout=120,
    )
    eq("CLI --runs 2 exit", limited.returncode, 0)
    check("CLI résumé humain", "accord" in limited.stdout)
    check("CLI cible affichée", "cible" not in limited.stdout)  # --runs < 100k → pas de verdict


def main() -> int:
    print(f"selftest backtest — fixture {os.path.relpath(FIXTURE, REPO_ROOT)}")
    test_loader()
    test_filters()
    test_policies()
    test_metrics()
    test_skip_metrics()
    test_run_level_guard()
    test_real_meta()
    test_cli()
    print()
    if _failures:
        print(f"ÉCHEC : {len(_failures)} test(s) sur {_checks}")
        for label in _failures:
            print(f"  - {label}")
        return 1
    print(f"PASS : {_checks} vérifications")
    return 0


if __name__ == "__main__":
    sys.exit(main())
