#!/usr/bin/env python3
"""Autotest de `tools/calibration` — aucune dépendance, aucun index requis.

    python3 -I tools/calibration/selftest.py

Vérifie ce qui peut l'être sans données ni réseau :
  - les primitives numériques (saturation, arrondi « away from zero », débit) ;
  - les invariants des quatre dimensions sur des decks réels du catalogue ;
  - la reconstruction arrière (l'état d'un acte ne contient pas les cartes prises après) ;
  - la régression logistique : elle doit retrouver des coefficients connus sur des données
    synthétiques, et l'AUC d'un classifieur parfait doit valoir 1 ;
  - le pliage des parts de poids (simplexe) et leurs bornes.

Le selftest est volontairement modeste : il attrape les régressions de formule, pas les
erreurs de modèle (le rapport les documente).
"""

from __future__ import annotations

import json
import math
import os
import random
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import features  # noqa: E402
import fit_weights  # noqa: E402

REPO_ROOT = os.path.dirname(os.path.dirname(_HERE))
CATALOG = os.path.join(REPO_ROOT, "data", "catalog")

CHECKS = {"passed": 0, "failed": 0}
FAILURES = []


def check(name, condition, detail=""):
    if condition:
        CHECKS["passed"] += 1
    else:
        CHECKS["failed"] += 1
        FAILURES.append(f"{name} {detail}".strip())
        print(f"ÉCHEC  {name} {detail}")


def close(a, b, tolerance=1e-6):
    return abs(a - b) <= tolerance


# --------------------------------------------------------------------------- #
def test_primitives():
    check("saturation(0) = 0", features.saturate(0, 2) == 0.0)
    check("saturation(1,1) = 50", close(features.saturate(1, 1), 50.0))
    check("saturation monotone", features.saturate(3, 1) > features.saturate(2, 1))
    check("saturation bornée < 100", features.saturate(1e9, 1) <= 100.0)
    check("saturation négative = 0", features.saturate(-5, 1) == 0.0)
    check("saturation k=0 -> k=1", close(features.saturate(1, 0), 50.0))
    check("arrondi 2,5 -> 3", features.rnd(2.5) == 3)
    check("arrondi -2,5 -> -3", features.rnd(-2.5) == -3)
    check("arrondi 2,4 -> 2", features.rnd(2.4) == 2)
    check("clamp bas", features.clamp(-3, 0, 100) == 0)
    check("clamp haut", features.clamp(130, 0, 100) == 100)

    # Débit : une carte qui pioche augmente la pioche par tour, plafonnée à 2× la base.
    check("pioche sans carte", close(features.draw_per_turn(0, 10, 0), 5.0))
    check("pioche avec 2 cartes", features.draw_per_turn(2, 10, 0) > 5.0)
    check("pioche plafonnée", features.draw_per_turn(10, 10, 0) <= 10.0 + 1e-9)
    check("énergie sans gain", close(features.energy_per_turn(0, 5, 10, 0), 3.0))
    check("énergie plafonnée", features.energy_per_turn(50, 10, 10, 0) <= 6.0 + 1e-9)
    check("taux de jeu borné", 0.0 <= features.play_rate(3, 5, 1.5) <= 1.0)
    check("taux de jeu plein", close(features.play_rate(100, 5, 1.0), 1.0))


def test_dimensions(catalogs):
    strike = ["STRIKE_IRONCLAD"] * 5 + ["DEFEND_IRONCLAD"] * 4 + ["BASH"]
    result = features.dimensions(strike, [], 70, 70, 1, 0, catalogs)
    dims = [result["survie"], result["offense"], result["consistance"], result["couverture"]]
    for name, value in zip(features.DIMENSIONS, dims):
        check(f"starter {name} dans 0..100", 0 <= value <= 100, f"= {value}")
    measures = result["measures"]
    check("starter : bloc/tour > 0", measures["block_per_turn"] > 0)
    check("starter : dégâts/tour > 0", measures["damage_per_turn"] > 0)
    check("starter : deck = 10", measures["deck"] == 10)
    check("starter : consistance > offense", result["consistance"] > result["offense"])
    check("starter : confiance high", measures["confidence"] == "high")

    # Plus de bloc => plus de survie (à menace constante).
    more_block = strike + ["DEFEND_IRONCLAD"] * 6
    after = features.dimensions(more_block, [], 70, 70, 1, 0, catalogs)
    check("bloc ajouté -> survie en hausse", after["survie"] >= result["survie"],
          f"{result['survie']} -> {after['survie']}")
    check("bloc ajouté -> offense en baisse", after["offense"] <= result["offense"],
          f"{result['offense']} -> {after['offense']}")

    # Plus de PV => plus de survie, saturation (doubler ne double pas).
    low = features.dimensions(strike, [], 20, 70, 1, 0, catalogs)
    high = features.dimensions(strike, [], 70, 70, 1, 0, catalogs)
    check("PV bas -> survie plus basse", low["survie"] < high["survie"])
    check("saturation PV : 100 PV < 100", features.dimensions(strike, [], 100, 100, 1, 0, catalogs)["survie"] < 100)

    # Malédiction réelle du catalogue : consistance en baisse, rien d'autre ne bouge.
    curse = next((card_id for card_id, card in catalogs.card.items()
                  if str(card.get("type") or "").lower() == "curse"), None)
    if curse:
        penalized = features.dimensions(strike + [curse], [], 70, 70, 1, 0, catalogs)
        check("malédiction -> consistance ≤", penalized["consistance"] <= result["consistance"],
              f"carte {curse}")
    else:
        check("catalogue : au moins une malédiction", False, "aucune carte de type Curse")

    # Deck vide : zéro partout, jamais d'exception.
    empty = features.dimensions([], [], 70, 70, 1, 0, catalogs)
    check("deck vide -> 0", empty["survie"] == 0 and empty["offense"] == 0)
    check("deck vide -> confidence unknown", empty["measures"]["confidence"] == "unknown")

    # Carte inconnue : comptée, pas d'exception, confiance dégradée.
    unknown = features.dimensions(strike + ["CARTE_QUI_N_EXISTE_PAS"], [], 70, 70, 1, 0, catalogs)
    check("carte inconnue -> unknown_cards = 1", unknown["measures"]["unknown_cards"] == 1)
    check("carte inconnue -> confiance ≠ high", unknown["measures"]["confidence"] != "high")

    # La menace de l'acte 3 doit être plus dure que celle de l'acte 1.
    check("menace acte 3 > acte 1",
          catalogs.threat_reference(3, 0)[0] > catalogs.threat_reference(1, 0)[0])
    check("PV boss acte 3 > acte 1",
          catalogs.threat_reference(3, 0)[1] > catalogs.threat_reference(1, 0)[1])


def test_reconstruction():
    final_deck = ["A", "B", "A", "C"]
    final_relics = ["R1", "R2"]
    picks_cards = {0: ["A"], 1: ["C"], 2: ["B"]}
    picks_relics = {0: ["R1"], 2: ["R2"]}
    deck0, relics0 = features._state_before_after(final_deck, final_relics, picks_cards, picks_relics, 0)
    check("reconstruction acte 1 retire les gains postérieurs", deck0 == ["A", "A"], str(deck0))
    check("reconstruction reliques acte 1", relics0 == ["R1"], str(relics0))
    deck2, relics2 = features._state_before_after(final_deck, final_relics, picks_cards, picks_relics, 2)
    check("reconstruction dernier acte = deck final", deck2 == final_deck)
    check("reconstruction dernier acte = reliques finales", relics2 == final_relics)
    # Un gain postérieur déjà transformé/retiré ne doit pas lever.
    deck_missing, _ = features._state_before_after(["A"], [], {2: ["Z"]}, {}, 0)
    check("reconstruction tolérante aux cartes absentes", deck_missing == ["A"])


def test_metrics():
    labels = [1, 1, 0, 0]
    check("AUC parfait", close(fit_weights.auc([0.9, 0.8, 0.2, 0.1], labels), 1.0, 1e-9))
    check("AUC inversé", close(fit_weights.auc([0.1, 0.2, 0.8, 0.9], labels), 0.0, 1e-9))
    check("AUC constant = 0,5", close(fit_weights.auc([0.5] * 4, labels), 0.5, 1e-9))
    check("AUC une seule classe = None", fit_weights.auc([0.5, 0.6], [1, 1]) is None)
    scores = [0.9 if label else 0.1 for label in labels]
    check("accuracy parfaite", close(fit_weights.accuracy(scores, labels), 1.0))
    check("Brier parfait ~ 0", fit_weights.brier(scores, labels) < 0.03)
    check("log-loss parfaite < 0,2", fit_weights.log_loss(scores, labels) < 0.2)


def test_logistic():
    """Sur données synthétiques, la régression doit retrouver les coefficients générateurs."""
    rng = random.Random(11)
    n = 30000
    X, y = [], []
    for _ in range(n):
        x1 = rng.gauss(0, 1)
        x2 = rng.gauss(0, 1)
        eta = 1.2 * x1 - 0.8 * x2 + 0.1
        p = 1.0 / (1.0 + math.exp(-eta))
        X.append([1.0, x1, x2])
        y.append(1 if rng.random() < p else 0)
    beta, covariance, iterations = fit_weights.fit_logistic(X, y, l2=0.001)
    check("logistique : β1 ≈ 1,2", abs(beta[1] - 1.2) < 0.06, f"= {beta[1]:.4f}")
    check("logistique : β2 ≈ −0,8", abs(beta[2] + 0.8) < 0.06, f"= {beta[2]:.4f}")
    check("logistique : intercept ≈ 0,1", abs(beta[0] - 0.1) < 0.06, f"= {beta[0]:.4f}")
    check("logistique : SE > 0", covariance[1][1] > 0)
    check("logistique : convergence en < 40 itérations", iterations < 40, f"= {iterations}")
    fitted_auc = fit_weights.auc(fit_weights.predict(X, beta), y)
    check("logistique : AUC > 0,7", fitted_auc is not None and fitted_auc > 0.7)


def test_share_weights():
    betas = [0.02, 0.01, 0.01, 0.06]
    covariance = [[1e-6 if i == j else 0.0 for j in range(4)] for i in range(4)]
    sds = [8.0, 7.0, 7.0, 16.0]
    rng = random.Random(3)
    result = fit_weights.share_weights(betas, covariance, sds, rng, draws=200)
    total = sum(result["share_per_point"])
    check("parts : somme = 1", close(total, 1.0, 1e-9), f"= {total}")
    check("parts : survivie/survie > 0", result["share_per_point"][0] > 0)
    check("parts : β négatif -> part 0",
          fit_weights.share_weights([-0.01, 0.02, 0.0, 0.01], covariance, sds, rng, 50)
          ["share_per_point"][0] == 0.0)


def test_index_model():
    rng = random.Random(5)
    rows, y = [], []
    for _ in range(4000):
        dims = [rng.uniform(0, 100) for _ in range(4)]
        eta = 0.05 * dims[0] + 0.0 * dims[1] + 0.02 * dims[2] + 0.01 * dims[3] - 4.0
        rows.append([1.0] + dims)
        y.append(1 if rng.random() < 1.0 / (1.0 + math.exp(-eta)) else 0)
    weights, scale, controls, _ll = fit_weights.fit_index_model(rows, y, [1, 2, 3, 4], iterations=400)
    check("indice : somme des poids = 1", close(sum(weights), 1.0, 1e-9), f"= {sum(weights)}")
    check("indice : poids ≥ 0", all(w >= 0 for w in weights))
    check("indice : dimension la plus forte = survie", weights[0] == max(weights),
          f"= {[round(w, 3) for w in weights]}")
    check("indice : échelle > 0", scale > 0)


def main() -> int:
    if not os.path.exists(os.path.join(CATALOG, "cards_effects.json")):
        print(f"catalogue absent : {CATALOG}", file=sys.stderr)
        return 2
    catalogs = features.Catalogs(CATALOG)
    test_primitives()
    test_dimensions(catalogs)
    test_reconstruction()
    test_metrics()
    test_logistic()
    test_share_weights()
    test_index_model()
    print(f"\n{CHECKS['passed']} vérifications OK, {CHECKS['failed']} échec(s)")
    if CHECKS["failed"]:
        for failure in FAILURES:
            print(f"  - {failure}")
        return 1
    print("selftest PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
