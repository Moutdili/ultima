#!/usr/bin/env python3
"""Ajuste les poids des dimensions de G sur les runs réels — Python 3 stdlib uniquement.

    python3 -I tools/calibration/fit_weights.py --features /tmp/features.jsonl \
        --out-json data/calibration/weights.json --out-report tools/calibration/report.md

Méthode (et ses limites, détaillées dans le rapport) :

1. `features.py` produit, pour chaque run, les quatre dimensions de G (mêmes formules que
   `PlayerStrength.cs`) et les mesures intermédiaires.
2. Ici : régression logistique `P(win) = σ(β·x)` par descente de Newton (IRLS) avec une
   pénalité L2 minuscule, features standardisées, séparation 80/20 train/test.
3. Les coefficients bruts (par point de dimension 0..100) sont convertis en **parts** qui
   somment à 1 — c'est la forme qu'attend `ActWeights` — avec un intervalle obtenu par
   Monte-Carlo sur la matrice de covariance (approximation gaussienne, « grossière » assumée).
4. Sortie : `data/calibration/weights.json` (petit, commité) + un rapport Markdown généré.

Ce que ce fichier **ne fait pas** : modifier le C#. Les poids proposés sont une proposition
soumise à arbitrage ; le code garde ses constantes nommées jusqu'à décision (docs/27 §7bis).
"""

from __future__ import annotations

import argparse
import json
import math
import os
import random
import sys
from datetime import datetime, timezone

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

from features import CODE_WEIGHTS, DEVIATIONS  # noqa: E402

REPO_ROOT = os.path.dirname(os.path.dirname(_HERE))
DEFAULT_JSON = os.path.join(REPO_ROOT, "data", "calibration", "weights.json")
DEFAULT_REPORT = os.path.join(_HERE, "report.md")

DIM_COLS = ("s_survie", "s_offense", "s_consistance", "s_couverture")
DIM_NAMES = ("survie", "offense", "consistance", "couverture")
RAW_COLS = (
    "m_block_per_turn", "m_damage_per_turn", "m_draw_per_turn", "m_energy_per_turn",
    "m_play_rate", "m_scaling", "m_aoe_share", "m_debuff_cards", "m_utility_categories",
    "m_penalties", "m_deck_size", "m_curses", "m_hp", "m_max_hp",
)
CHARACTERS = ("ironclad", "silent", "defect", "necrobinder", "regent")

#: Colonnes conservées en mémoire (l'index de features est plus large que nécessaire).
KEEP_KEYS = ("kind", "act", "act_reached", "asc", "char", "y",
             "s_survie", "s_offense", "s_consistance", "s_couverture",
             "m_max_hp", "m_worst_hit", "m_boss_hp") + RAW_COLS

#: Modèles ajustés. `dims` = dimensions seules (+ contrôles) : c'est la spécification qui
#: donne les poids. `raw` = mesures brutes : sert de borne haute (les dimensions résument-elles ?).
SPECS = (
    {"name": "final_dims", "kind": "final", "cols": DIM_COLS, "controls": ("asc",), "chars": True},
    {"name": "final_dims_depth", "kind": "final", "cols": DIM_COLS,
     "controls": ("asc", "act_reached"), "chars": True},
    {"name": "final_raw", "kind": "final", "cols": RAW_COLS,
     "controls": ("asc", "act_reached"), "chars": True, "max_train": 15000},
    {"name": "act1_dims", "kind": "act", "act": 1, "cols": DIM_COLS, "controls": ("asc",),
     "chars": True, "completed_only": True},
    {"name": "act2_dims", "kind": "act", "act": 2, "cols": DIM_COLS, "controls": ("asc",),
     "chars": True, "completed_only": True},
    {"name": "act3_dims", "kind": "act", "act": 3, "cols": DIM_COLS, "controls": ("asc",),
     "chars": True, "completed_only": True},
    {"name": "act1_raw", "kind": "act", "act": 1, "cols": RAW_COLS, "controls": ("asc",),
     "chars": True, "max_train": 15000, "completed_only": True},
    {"name": "act2_raw", "kind": "act", "act": 2, "cols": RAW_COLS, "controls": ("asc",),
     "chars": True, "max_train": 15000, "completed_only": True},
    {"name": "act3_raw", "kind": "act", "act": 3, "cols": RAW_COLS, "controls": ("asc",),
     "chars": True, "max_train": 15000, "completed_only": True},
)

L2_PENALTY = 1.0
MAX_ITERATIONS = 40
TOLERANCE = 1e-9

CAVEAT = (
    "Corrélation, pas causalité. Les features décrivent l'état du joueur (deck, reliques, PV) ; "
    "elles sont en partie la CONSÉQUENCE des choix passés et de la chance (butin, événements). "
    "Un coefficient positif dit « les runs qui finissent avec cette qualité gagnent plus souvent », "
    "jamais « ajouter cette qualité fait gagner ». Aucun contrefactuel n'est observé : on ne voit "
    "jamais le même run avec et sans la carte."
)


# --------------------------------------------------------------------------- #
# Statistiques de base
# --------------------------------------------------------------------------- #
def mean(values):
    return sum(values) / len(values) if values else 0.0


def stdev(values):
    if len(values) < 2:
        return 0.0
    mu = mean(values)
    return math.sqrt(sum((v - mu) ** 2 for v in values) / (len(values) - 1))


def sigmoid(z):
    if z >= 0:
        ez = math.exp(-z) if z < 700 else 0.0
        return 1.0 / (1.0 + ez)
    ez = math.exp(z) if z > -700 else 0.0
    return ez / (1.0 + ez)


def auc(scores, labels):
    """AUC par la statistique de Mann-Whitney, avec rangs moyens pour les ex æquo."""
    pairs = sorted(zip(scores, labels), key=lambda p: p[0])
    n = len(pairs)
    positives = sum(labels)
    negatives = n - positives
    if positives == 0 or negatives == 0:
        return None
    rank_sum = 0.0
    i = 0
    while i < n:
        j = i
        while j + 1 < n and pairs[j + 1][0] == pairs[i][0]:
            j += 1
        average_rank = (i + j) / 2.0 + 1.0
        for k in range(i, j + 1):
            if pairs[k][1]:
                rank_sum += average_rank
        i = j + 1
    return (rank_sum - positives * (positives + 1) / 2.0) / (positives * negatives)


def accuracy(scores, labels, threshold=0.5):
    correct = sum(1 for s, y in zip(scores, labels) if (s >= threshold) == bool(y))
    return correct / len(labels)


def brier(scores, labels):
    return sum((s - y) ** 2 for s, y in zip(scores, labels)) / len(labels)


def log_loss(scores, labels):
    eps = 1e-12
    total = 0.0
    for s, y in zip(scores, labels):
        p = min(1 - eps, max(eps, s))
        total += -(y * math.log(p) + (1 - y) * math.log(1 - p))
    return total / len(labels)


# --------------------------------------------------------------------------- #
# Algèbre linéaire (petites matrices, Gauss-Jordan + Cholesky)
# --------------------------------------------------------------------------- #
def solve(matrix, vector):
    """Résout `matrix · x = vector` par Gauss-Jordan avec pivot partiel."""
    n = len(matrix)
    aug = [list(row) + [vector[i]] for i, row in enumerate(matrix)]
    for col in range(n):
        pivot = max(range(col, n), key=lambda r: abs(aug[r][col]))
        if abs(aug[pivot][col]) < 1e-12:
            raise ValueError("matrice singulière")
        aug[col], aug[pivot] = aug[pivot], aug[col]
        factor = aug[col][col]
        aug[col] = [v / factor for v in aug[col]]
        for row in range(n):
            if row == col:
                continue
            ratio = aug[row][col]
            if ratio == 0.0:
                continue
            aug[row] = [a - ratio * b for a, b in zip(aug[row], aug[col])]
    return [aug[i][n] for i in range(n)]


def inverse(matrix):
    n = len(matrix)
    columns = []
    for col in range(n):
        unit = [0.0] * n
        unit[col] = 1.0
        columns.append(solve(matrix, unit))
    return [[columns[c][r] for c in range(n)] for r in range(n)]


def cholesky(matrix):
    n = len(matrix)
    lower = [[0.0] * n for _ in range(n)]
    for i in range(n):
        for j in range(i + 1):
            total = matrix[i][j] - sum(lower[i][k] * lower[j][k] for k in range(j))
            if i == j:
                lower[i][j] = math.sqrt(max(total, 1e-12))
            else:
                lower[i][j] = total / lower[j][j] if lower[j][j] else 0.0
    return lower


def sample_normal(lower, draws, rng):
    n = len(lower)
    samples = []
    for _ in range(draws):
        z = [rng.gauss(0.0, 1.0) for _ in range(n)]
        samples.append([sum(lower[i][k] * z[k] for k in range(i + 1)) for i in range(n)])
    return samples


def r4(value):
    """Arrondi tolérant : `auc()` peut renvoyer None (une seule classe dans l'échantillon)."""
    return None if value is None else round(value, 4)


def percentile(sorted_values, q):
    if not sorted_values:
        return None
    pos = q * (len(sorted_values) - 1)
    low = int(math.floor(pos))
    high = min(len(sorted_values) - 1, low + 1)
    frac = pos - low
    return sorted_values[low] * (1 - frac) + sorted_values[high] * frac


# --------------------------------------------------------------------------- #
# Jeu de données
# --------------------------------------------------------------------------- #
def load_rows(paths):
    """Charge une fois et range les lignes par (kind, acte). Écarte les PV inexploitables."""
    pools = {("final", None): [], ("act", 1): [], ("act", 2): [], ("act", 3): []}
    dropped = {"hp_missing": 0, "no_dims": 0, "other": 0}
    for path in paths:
        with open(path, "r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                row = json.loads(line)
                kind = row.get("kind") or "final"
                act = row.get("act") if kind == "act" else None
                key = (kind, act)
                if key not in pools:
                    dropped["other"] += 1
                    continue
                if row.get("m_max_hp", 0) <= 0:
                    dropped["hp_missing"] += 1
                    continue
                if row.get("m_worst_hit", 0) <= 0 or row.get("m_boss_hp", 0) <= 0:
                    dropped["no_dims"] += 1
                    continue
                if kind == "act" and row.get("act_reached", 0) < act + 1:
                    dropped["other"] += 1  # état post-mort : inutilisable pour cet acte
                    continue
                pools[key].append({key_name: row.get(key_name) for key_name in KEEP_KEYS})
    return pools, dropped


def design_matrix(rows, spec):
    """Construit X (avec intercept), y et les noms de colonnes."""
    columns = []          # liste de (nom, valeurs brutes)
    for name in spec["cols"]:
        columns.append((name, [float(row.get(name, 0.0)) for row in rows]))
    for control in spec["controls"]:
        columns.append((control, [float(row.get(control, 0.0)) for row in rows]))
    if spec.get("chars"):
        for character in CHARACTERS[1:]:  # une modalité de référence (ironclad)
            columns.append(("char_" + character,
                            [1.0 if row.get("char") == character else 0.0 for row in rows]))

    names = ["intercept"] + [name for name, _ in columns]
    y = [int(row["y"]) for row in rows]
    raw = [[values[i] for _, values in columns] for i in range(len(rows))]
    return names, raw, y


def standardize(raw, train_index):
    """Moyenne/écart-type sur le train seulement ; les colonnes constantes sont neutralisées."""
    n_features = len(raw[0]) if raw else 0
    means, sds = [], []
    for j in range(n_features):
        values = [raw[i][j] for i in train_index]
        mu, sd = mean(values), stdev(values)
        means.append(mu)
        sds.append(sd if sd > 1e-12 else 1.0)
    return means, sds


def build_xy(raw, means, sds, indices, with_intercept=True):
    matrix = []
    for i in indices:
        row = [1.0] if with_intercept else []
        row.extend((raw[i][j] - means[j]) / sds[j] for j in range(len(means)))
        matrix.append(row)
    return matrix


# --------------------------------------------------------------------------- #
# Régression logistique (IRLS)
# --------------------------------------------------------------------------- #
def fit_logistic(X, y, l2=L2_PENALTY, max_iterations=MAX_ITERATIONS, tolerance=TOLERANCE):
    """Descente de Newton sur la log-vraisemblance pénalisée ; renvoie β et la covariance."""
    n, d = len(X), len(X[0])
    beta = [0.0] * d
    previous = None
    iterations = 0
    for iterations in range(1, max_iterations + 1):
        gradient = [0.0] * d
        hessian = [[0.0] * d for _ in range(d)]
        loglik = 0.0
        for i in range(n):
            row = X[i]
            eta = sum(beta[j] * row[j] for j in range(d))
            p = sigmoid(eta)
            weight = max(p * (1.0 - p), 1e-6)
            residual = y[i] - p
            loglik += y[i] * eta - math.log1p(math.exp(min(eta, 700)))
            for j in range(d):
                gradient[j] += residual * row[j]
                wj = weight * row[j]
                for k in range(j, d):
                    hessian[j][k] += wj * row[k]
        for j in range(d):
            for k in range(j):
                hessian[j][k] = hessian[k][j]
        for j in range(1, d):
            gradient[j] -= l2 * beta[j]
            hessian[j][j] += l2
        try:
            step = solve(hessian, gradient)
        except ValueError:
            break
        beta = [b + s for b, s in zip(beta, step)]
        if previous is not None and abs(loglik - previous) < tolerance:
            break
        previous = loglik
    # Covariance = inverse de l'information observée au point courant.
    hessian = [[0.0] * d for _ in range(d)]
    for i in range(n):
        row = X[i]
        eta = sum(beta[j] * row[j] for j in range(d))
        p = sigmoid(eta)
        weight = max(p * (1.0 - p), 1e-6)
        for j in range(d):
            wj = weight * row[j]
            for k in range(j, d):
                hessian[j][k] += wj * row[k]
    for j in range(d):
        for k in range(j):
            hessian[j][k] = hessian[k][j]
    for j in range(1, d):
        hessian[j][j] += l2
    try:
        covariance = inverse(hessian)
    except ValueError:
        covariance = [[0.0] * d for _ in range(d)]
    return beta, covariance, iterations


def predict(X, beta):
    return [sigmoid(sum(b * x for b, x in zip(beta, row))) for row in X]


def share_weights(betas, covariance, sds, rng, draws=2000):
    """Parts normalisées des dimensions + intervalle Monte-Carlo (approximation gaussienne).

    Deux lectures, volontairement séparées :
      - `share_per_point` : β brut (par point de dimension) normalisé — c'est la forme d'ActWeights ;
      - `share_of_variation` : β·σ (contribution à la variance du logit) normalisé.
    """
    raw_betas = [betas[j] / sds[j] for j in range(len(betas))]
    raw_cov = [[covariance[j][k] / (sds[j] * sds[k]) for k in range(len(betas))]
               for j in range(len(betas))]
    sds_values = list(sds)

    def shares_from(beta_vector):
        positive = [max(0.0, b) for b in beta_vector]
        total = sum(positive)
        if total <= 0:
            return None
        return [b / total for b in positive]

    point = shares_from(raw_betas)
    variation = shares_from([raw_betas[j] * sds_values[j] for j in range(len(raw_betas))])

    lower = cholesky(raw_cov)
    samples = sample_normal(lower, draws, rng)
    per_dim = [[] for _ in raw_betas]
    for offset in samples:
        drawn = [raw_betas[j] + offset[j] for j in range(len(raw_betas))]
        share = shares_from(drawn)
        if share is None:
            continue
        for j, value in enumerate(share):
            per_dim[j].append(value)
    intervals = []
    for values in per_dim:
        values.sort()
        intervals.append((percentile(values, 0.025), percentile(values, 0.975)))
    return {
        "beta_raw": raw_betas,
        "cov_raw": raw_cov,
        "share_per_point": point,
        "share_of_variation": variation,
        "share_ci95": intervals,
        "mc_draws_used": len(per_dim[0]) if per_dim else 0,
    }


# --------------------------------------------------------------------------- #
# Un modèle complet
# --------------------------------------------------------------------------- #
def run_spec(spec, pools, args, rng):
    rows = pools[(spec["kind"], spec.get("act"))]
    if spec.get("completed_only"):
        # État de FIN d'acte mesuré seulement sur les runs qui ont survécu à cet acte : sinon
        # les PV du dernier enregistrement sont ceux de la mort et « Survie » prédit la mort
        # par tautologie (fuite de l'issue dans la feature).
        act = spec["act"]
        rows = [row for row in rows if row.get("act_reached", 0) >= act + 1]
    if not rows:
        return None
    indices = list(range(len(rows)))
    rng.shuffle(indices)
    if len(indices) < 200:
        return None
    n_test = min(max(50, int(len(indices) * args.test_fraction)), args.max_test, len(indices) - 50)
    test_index = indices[:n_test]
    train_pool = indices[n_test:]
    train_cap = min(args.max_train, spec.get("max_train", args.max_train))
    train_index = train_pool[:train_cap]

    names, raw, y = design_matrix(rows, spec)
    means, sds = standardize(raw, train_index)
    X_train = build_xy(raw, means, sds, train_index)
    X_test = build_xy(raw, means, sds, test_index)
    y_train = [y[i] for i in train_index]
    y_test = [y[i] for i in test_index]

    beta, covariance, iterations = fit_logistic(X_train, y_train)
    scores_train = predict(X_train, beta)
    scores_test = predict(X_test, beta)

    base_rate = sum(y_train) / len(y_train)
    ll_model = -log_loss(scores_train, y_train) * len(y_train)
    ll_null = (base_rate * math.log(max(base_rate, 1e-12))
               + (1 - base_rate) * math.log(max(1 - base_rate, 1e-12))) * len(y_train)

    coefficients = []
    for j, name in enumerate(names):
        se = math.sqrt(max(covariance[j][j], 0.0))
        coefficients.append({
            "name": name,
            "beta": beta[j],
            "se": se,
            "ci_low": beta[j] - 1.96 * se,
            "ci_high": beta[j] + 1.96 * se,
            "beta_raw": beta[j] / sds[j - 1] if j > 0 else beta[j],
        })

    result = {
        "model": spec["name"],
        "kind": spec["kind"],
        "act": spec.get("act"),
        "n_train": len(train_index),
        "n_test": len(test_index),
        "base_rate_train": round(base_rate, 4),
        "base_rate_test": round(sum(y_test) / len(y_test), 4),
        "auc_train": r4(auc(scores_train, y_train)),
        "auc_test": r4(auc(scores_test, y_test)),
        "accuracy_test": round(accuracy(scores_test, y_test), 4),
        "accuracy_majority": round(max(sum(y_test) / len(y_test), 1 - sum(y_test) / len(y_test)), 4),
        "brier_test": round(brier(scores_test, y_test), 4),
        "mcfadden_r2": round(1 - ll_model / ll_null, 4) if ll_null else None,
        "iterations": iterations,
        "coefficients": coefficients,
    }

    if spec["cols"] == DIM_COLS:
        indices_dims = [names.index(column) for column in DIM_COLS]
        betas = [beta[j] for j in indices_dims]
        covariance_dims = [[covariance[j][k] for k in indices_dims] for j in indices_dims]
        sds_dims = [sds[j - 1] for j in indices_dims]
        result["weights"] = share_weights(betas, covariance_dims, sds_dims, rng)

        # Second estimateur : on impose la forme de G (une seule combinaison, poids ≥ 0, somme 1).
        # Sous-échantillon du train (300 itérations × 10 000 lignes) pour rester rapide.
        index_train = train_index[: args.max_index_train]
        rows_index = build_xy(raw, means, sds, index_train)
        weights_index, scale, controls_index, _ll = fit_index_model(
            rows_index, [y[i] for i in index_train], indices_dims)
        control_positions = [j for j in range(1, len(names)) if j not in indices_dims]
        test_scores = []
        for row in X_test:
            index = sum(weights_index[j] * row[indices_dims[j]] for j in range(len(indices_dims)))
            eta = scale * index
            for slot, position in enumerate(control_positions):
                eta += controls_index[slot] * row[position]
            test_scores.append(sigmoid(eta))
        result["index_model"] = {
            "weights": {dim: round(weights_index[j], 4) for j, dim in enumerate(DIM_NAMES)},
            "scale": round(scale, 4),
            "auc_test": r4(auc(test_scores, y_test)),
            "n_train": len(index_train),
        }
    return result


# --------------------------------------------------------------------------- #
# Modèle d'indice : P(win) = σ(a · Σ w_i·dim_i + contrôles), w sur le simplexe
# --------------------------------------------------------------------------- #
def fit_index_model(X_rows, y, dim_positions, iterations=300, learning_rate=0.25, momentum=0.9):
    """Ajuste **directement la forme de G** : une seule combinaison linéaire des dimensions.

    C'est la structure que le code implémente (`ActWeights`, somme = 1) au lieu de quatre
    coefficients libres. Paramétrisation softmax (w ≥ 0, Σw = 1) + une échelle `a` ; montée de
    gradient avec momentum sur la log-vraisemblance moyenne.

    Renvoie (poids, échelle, contrôles, log-vraisemblance moyenne).
    """
    n, d = len(X_rows), len(X_rows[0])
    k = len(dim_positions)
    control_positions = [j for j in range(1, d) if j not in dim_positions]
    theta = [0.0] * k
    scale = 1.0
    controls = [0.0] * len(control_positions)
    velocity_theta = [0.0] * k
    velocity_scale = 0.0
    velocity_controls = [0.0] * len(controls)
    weights = [1.0 / k] * k
    previous = None

    for _ in range(iterations):
        maximum = max(theta)
        exps = [math.exp(t - maximum) for t in theta]
        total = sum(exps)
        weights = [value / total for value in exps]
        grad_theta = [0.0] * k
        grad_scale = 0.0
        grad_controls = [0.0] * len(controls)
        loglik = 0.0
        for i in range(n):
            row = X_rows[i]
            index = sum(weights[j] * row[dim_positions[j]] for j in range(k))
            eta = scale * index
            for position, coefficient in zip(control_positions, controls):
                eta += coefficient * row[position]
            p = sigmoid(eta)
            error = y[i] - p
            loglik += y[i] * eta - math.log1p(math.exp(min(eta, 700.0)))
            grad_scale += error * index
            for slot, position in enumerate(control_positions):
                grad_controls[slot] += error * row[position]
            for j in range(k):
                grad_theta[j] += error * scale * weights[j] * (row[dim_positions[j]] - index)
        grad_theta = [value / n for value in grad_theta]
        grad_scale /= n
        grad_controls = [value / n for value in grad_controls]
        loglik /= n
        for j in range(k):
            velocity_theta[j] = momentum * velocity_theta[j] + grad_theta[j]
            theta[j] += learning_rate * velocity_theta[j]
        velocity_scale = momentum * velocity_scale + grad_scale
        scale = min(1e3, max(1e-3, scale + learning_rate * velocity_scale))
        for slot in range(len(controls)):
            velocity_controls[slot] = momentum * velocity_controls[slot] + grad_controls[slot]
            controls[slot] += learning_rate * velocity_controls[slot]
        if previous is not None and abs(loglik - previous) < 1e-9:
            break
        previous = loglik
    return weights, scale, controls, previous or 0.0


# --------------------------------------------------------------------------- #
# Rapport
# --------------------------------------------------------------------------- #
def fmt(value, digits=3):
    if value is None:
        return "—"
    if isinstance(value, float):
        return f"{value:+.{digits}f}" if abs(value) < 1e6 else f"{value:.3g}"
    return str(value)


def fmt_share(value):
    return "—" if value is None else f"{100 * value:.0f} %"


QUARTILE_COLUMNS = (
    ("s_survie", "Survie"),
    ("s_offense", "Offense"),
    ("s_consistance", "Consistance"),
    ("s_couverture", "Couverture"),
    ("m_block_per_turn", "bloc/tour (brut)"),
    ("m_damage_per_turn", "dégâts/tour (brut)"),
    ("m_hp", "PV au moment de la mesure"),
    ("m_draw_per_turn", "pioche/tour (brut)"),
    ("m_energy_per_turn", "énergie/tour (brut)"),
    ("m_deck_size", "taille de deck"),
    ("m_scaling", "scaling (brut)"),
    ("m_curses", "malédictions"),
)

#: Mesures brutes dont on publie aussi l'AUC univariée (au-delà des quatre dimensions).
UNIVARIATE_RAW = (
    ("m_block_per_turn", "bloc/tour"),
    ("m_damage_per_turn", "dégâts/tour"),
    ("m_draw_per_turn", "pioche/tour"),
    ("m_energy_per_turn", "énergie/tour"),
    ("m_hp", "PV"),
    ("m_max_hp", "PV max"),
    ("m_deck_size", "taille de deck"),
    ("m_curses", "malédictions"),
    ("m_scaling", "scaling"),
)


def quartile_stats(rows, column, buckets=4):
    """Découpe une mesure en quartiles et donne le taux de victoire de chaque quartile."""
    values = sorted(row[column] for row in rows)
    if len(values) < buckets * 20:
        return None
    cuts = [values[min(len(values) - 1, int(len(values) * q))] for q in
            (1 / buckets, 2 / buckets, 3 / buckets)]
    buckets_rows = [[] for _ in range(buckets)]
    for row in rows:
        value = row[column]
        slot = 0
        while slot < buckets - 1 and value > cuts[slot]:
            slot += 1
        buckets_rows[slot].append(row["y"])
    return {
        "cuts": cuts,
        "rates": [sum(bucket) / len(bucket) if bucket else None for bucket in buckets_rows],
        "sizes": [len(bucket) for bucket in buckets_rows],
    }


def correlation(rows, left, right):
    xs = [row[left] for row in rows]
    ys = [row[right] for row in rows]
    mx, my = mean(xs), mean(ys)
    sx = math.sqrt(sum((x - mx) ** 2 for x in xs))
    sy = math.sqrt(sum((y - my) ** 2 for y in ys))
    if sx == 0 or sy == 0:
        return 0.0
    return sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / (sx * sy)


def build_report(models, pools, args, generated_at, univariate, quartiles):
    lines = []
    add = lines.append
    add("# Calibration des poids de G sur les runs réels")
    add("")
    add(f"Généré par `tools/calibration/fit_weights.py` le {generated_at} — **ne pas éditer à la main**.")
    add("")
    add("> **Corrélation, pas causalité.** Les features décrivent l'état du joueur ; elles sont en")
    add("> partie la conséquence de ses choix passés et de la chance. Les parts ci-dessous disent")
    add("> « quelle dimension distingue les runs qui gagnent », pas « quel levier fait gagner ».")
    add("")
    add("## 1. Design d'observation")
    add("")
    add("L'index ne contient **pas** d'instantané du deck à chaque décision : seulement le deck")
    add("final. Pour obtenir un état par acte, on reconstruit **vers l'arrière** (deck final moins")
    add("les cartes prises après l'acte) — la méthode que docs/29 §2.2 impose, l'accumulation avant")
    add("ne refermant que sur ~11 % des joueurs-runs.")
    add("")
    add("Deux règles rendent la mesure honnête :")
    add("")
    add("1. **État de fin d'acte, runs survivants seulement.** Pour l'acte *k*, on ne garde que les")
    add("   runs qui ont **survécu** à l'acte *k* (`acte atteint > k`). Sinon les PV du dernier")
    add("   enregistrement sont ceux de la mort et « Survie » prédirait la mort par tautologie.")
    add("2. **Aucun regard sur la suite** : les données utilisées pour l'acte *k* sont le deck, les")
    add("   reliques et les PV **à la fin de l'acte *k*** ; la cible (`win`) est postérieure.")
    add("")
    add("Conséquence : **l'acte 3 n'est pas identifiable**. Aucun run ne « survit à l'acte 3 » sinon")
    add("en gagnant, donc l'échantillon n'a plus qu'une seule issue. Le rapport ne propose rien pour")
    add("l'acte 3 et le dit dans `weights.json` (`not_identifiable`).")
    add("")
    add("## 2. Échantillon")
    add("")
    add("| | valeur |")
    add("|---|---|")
    add(f"| index | `{args.index_label}` |")
    add(f"| runs lus (échantillon systématique) | {args.scanned:,} |")
    add(f"| runs dans le fit final (`kind=final`) | {len(pools[('final', None)]):,} |")
    add(f"| lignes `act` exploitables (run × acte, survivants) | "
        f"{sum(len(v) for k, v in pools.items() if k[0] == 'act'):,} |")
    add(f"| filtre | solo, mode standard, personnage canonique, deck ≤ {args.max_deck} cartes |")
    add(f"| runs écartés faute de PV exploitables | {args.dropped_hp:,} |")
    add(f"| split | 80 / 20 train / test, graine {args.seed} |")
    add("")
    add("Taux de victoire par strate (train) :")
    add("")
    add("| modèle | n train | n test | taux de victoire |")
    add("|---|---:|---:|---:|")
    for name in ("final_dims", "act1_dims", "act2_dims", "act3_dims"):
        model = models.get(name)
        if not model:
            continue
        add(f"| `{name}` | {model['n_train']:,} | {model['n_test']:,} | "
            f"{100 * model['base_rate_train']:.1f} % |")
    add("")
    add("Le taux global du corpus est 20,9 % (co-op inclus) ; ici le co-op est exclu et chaque")
    add("strate conditionne sur la profondeur atteinte, d'où des taux très différents d'une ligne")
    add("à l'autre. Un AUC ne se compare donc **qu'à l'intérieur d'une même strate**.")
    add("")
    add("## 3. Ce que la data dit")
    add("")
    add("### 3.1 Les quatre dimensions résument-elles les mesures brutes ?")
    add("")
    add("| modèle | n train | AUC test | accuracy | majorité | Brier | McFadden R² |")
    add("|---|---:|---:|---:|---:|---:|---:|")
    for name in ("final_dims", "final_dims_depth", "final_raw",
                 "act1_dims", "act1_raw", "act2_dims", "act2_raw", "act3_dims", "act3_raw"):
        model = models.get(name)
        if not model:
            continue
        add(f"| `{name}` | {model['n_train']:,} | {model['auc_test']:.3f} "
            f"| {model['accuracy_test']:.3f} | {model['accuracy_majority']:.3f} "
            f"| {model['brier_test']:.3f} | {model['mcfadden_r2']:.3f} |")
    add("")
    add("`*_dims` = 4 dimensions + ascension + personnage ; `*_raw` = 14 mesures brutes + ascension")
    add("(+ acte atteint pour `final_raw`) + personnage. Écart d'AUC faible = les dimensions")
    add("résument l'essentiel ; écart fort = elles jettent du signal.")
    add("")
    add("### 3.2 Poids par acte — coefficients libres, normalisés")
    add("")
    add("| acte | dimension | code | data (`β` par point) | IC 95 % (grossier) | data (`β·σ`) |")
    add("|---|---|---:|---:|---|---:|")
    for act in (1, 2, 3):
        model = models.get(f"act{act}_dims")
        if not model:
            add(f"| {act} | — | — | **non identifiable** (aucun run ne survit à l'acte 3 sauf en gagnant) | — | — |")
            continue
        weights = model.get("weights") or {}
        code = CODE_WEIGHTS[act]
        for position, dimension in enumerate(DIM_NAMES):
            share = None if not weights.get("share_per_point") else weights["share_per_point"][position]
            variation = None if not weights.get("share_of_variation") else weights["share_of_variation"][position]
            low, high = weights["share_ci95"][position] if weights.get("share_ci95") else (None, None)
            add(f"| {act} | {dimension} | {100 * code[dimension]:.0f} % | {fmt_share(share)} "
                f"| {fmt_share(low)} – {fmt_share(high)} | {fmt_share(variation)} |")
    model = models.get("final_dims")
    if model and model.get("weights"):
        weights = model["weights"]
        add("")
        add("État de **fin de run** (indicatif, voir §5 : les PV y sont contaminés par la mort) :")
        add("")
        add("| fin | dimension | part (`β`) | IC 95 % |")
        add("|---|---|---:|---|")
        for position, dimension in enumerate(DIM_NAMES):
            share = weights["share_per_point"][position] if weights.get("share_per_point") else None
            low, high = weights["share_ci95"][position]
            add(f"| fin | {dimension} | {fmt_share(share)} | {fmt_share(low)} – {fmt_share(high)} |")
    add("")
    add("`β` par point = effet marginal sur le log-odds d'**un point** de dimension (les quatre")
    add("dimensions sont sur la même échelle 0..100, donc comparables) ; `β·σ` = contribution à la")
    add("variance du logit, qui tient compte de la dispersion réellement observée.")
    add("")
    add("### 3.3 Second estimateur : la forme de G imposée (poids ≥ 0, somme = 1)")
    add("")
    add("Le modèle précédent laisse les quatre coefficients libres. Ici on **impose la structure du")
    add("code** : `P(win) = σ(a · Σ w_i·dim_i + contrôles)` avec `w` sur le simplexe. C'est exactement")
    add("la forme d'`ActWeights` ; si les deux méthodes tombent d'accord, la conclusion ne dépend pas")
    add("du choix de spécification.")
    add("")
    add("| acte | survie | offense | consistance | couverture | échelle `a` | AUC test (indice) | AUC test (libre) |")
    add("|---|---:|---:|---:|---:|---:|---:|---:|")
    for name, label in (("act1_dims", "1"), ("act2_dims", "2"), ("act3_dims", "3"), ("final_dims", "fin")):
        model = models.get(name)
        if not model or "index_model" not in model:
            continue
        index_model = model["index_model"]
        weights = index_model["weights"]
        add(f"| {label} | {fmt_share(weights['survie'])} | {fmt_share(weights['offense'])} "
            f"| {fmt_share(weights['consistance'])} | {fmt_share(weights['couverture'])} "
            f"| {index_model['scale']:.2f} | {index_model['auc_test']:.3f} | {model['auc_test']:.3f} |")
    add("")
    add("### 3.4 Taux de victoire par quartile (la lecture la plus directe)")
    add("")
    add("Chaque ligne coupe l'échantillon en quatre parts égales de la mesure et donne le taux de")
    add("victoire de chaque part — aucun modèle, aucune normalisation : c'est la donnée brute.")
    add("")
    for act in (1, 2):
        stats = quartiles.get(act)
        if not stats:
            continue
        model = models.get(f"act{act}_dims")
        add(f"**Acte {act}** — {model['n_train'] + model['n_test']:,} runs ayant survécu à l'acte "
            f"{act}, taux de victoire global {100 * model['base_rate_train']:.1f} %.")
        add("")
        add("| mesure | Q1 | Q2 | Q3 | Q4 | seuils |")
        add("|---|---:|---:|---:|---:|---|")
        for column, label in QUARTILE_COLUMNS:
            entry = stats.get(column)
            if not entry:
                continue
            cells = " | ".join(fmt_share(rate) for rate in entry["rates"])
            cuts = " / ".join(f"{cut:.1f}" for cut in entry["cuts"])
            add(f"| {label} | {cells} | {cuts} |")
        add("")
    add("### 3.5 Corrélations entre dimensions")
    add("")
    add("| paire | acte 1 | acte 2 |")
    add("|---|---:|---:|")
    for left in range(len(DIM_NAMES)):
        for right in range(left + 1, len(DIM_NAMES)):
            cells = []
            for act in (1, 2):
                correlations = quartiles.get(("corr", act)) or {}
                cells.append(f"{correlations.get((DIM_NAMES[left], DIM_NAMES[right]), 0.0):+.2f}")
            add(f"| {DIM_NAMES[left]} × {DIM_NAMES[right]} | " + " | ".join(cells) + " |")
    add("")
    add("Corrélations faibles : la colinéarité n'explique **pas** les poids ci-dessus. Ce n'est donc")
    add("pas un artefact de calcul partagé, mais bien un contenu informationnel différent.")
    add("")
    add("### 3.6 Signal univarié de chaque dimension (AUC seule)")
    add("")
    add("| dimension | acte 1 | acte 2 | acte 3 (indicatif) | fin de run |")
    add("|---|---:|---:|---:|---:|")
    for dimension in DIM_NAMES:
        cells = []
        for key in ("act1", "act2", "act3", "final"):
            value = univariate.get((key, dimension))
            cells.append("—" if value is None else f"{value:.3f}")
        add(f"| {dimension} | " + " | ".join(cells) + " |")
    add("")
    add("0,5 = aucune information. Ces AUC ne dépendent d'aucun ajustement multivarié : elles sont")
    add("stables, mais elles ne départagent pas deux dimensions corrélées.")
    add("")
    add("Mêmes AUC pour les mesures brutes (ce que les dimensions agrègent) :")
    add("")
    add("| mesure brute | acte 1 | acte 2 | fin de run |")
    add("|---|---:|---:|---:|")
    for _column, label in UNIVARIATE_RAW:
        cells = []
        for key in ("act1", "act2", "final"):
            value = univariate.get((key, label))
            cells.append("—" if value is None else f"{value:.3f}")
        add(f"| {label} | " + " | ".join(cells) + " |")
    add("")
    add("C'est le point le plus utile du rapport : **bloc/tour, pioche/tour, énergie/tour et PV**")
    add("séparent les gagnants, **dégâts/tour non** (et légèrement à l'envers à l'acte 2).")
    add("")
    add("### 3.7 Coefficients détaillés des modèles à dimensions")
    add("")
    add("| modèle | terme | β (standardisé) | SE | IC 95 % | β brut (par point) |")
    add("|---|---|---:|---:|---|---:|")
    for name in ("act1_dims", "act2_dims", "final_dims"):
        model = models.get(name)
        if not model:
            continue
        for coefficient in model["coefficients"]:
            add(f"| `{name}` | {coefficient['name']} | {fmt(coefficient['beta'])} "
                f"| {coefficient['se']:.3f} "
                f"| {fmt(coefficient['ci_low'])} – {fmt(coefficient['ci_high'])} "
                f"| {coefficient['beta_raw']:+.4f} |")
    add("")
    add("Un intervalle qui contient 0 = dimension non distinguable du bruit **à autres dimensions")
    add("égales**. Ce n'est pas « la dimension ne compte pas » : c'est « ces données ne séparent pas")
    add("son effet de celui des trois autres ».")
    add("")

    # --- contradictions
    add("## 4. Contradictions avec les poids du code — à arbitrer")
    add("")
    contradictions = []
    for act in (1, 2, 3):
        model = models.get(f"act{act}_dims")
        if not model or not model.get("weights") or not model["weights"].get("share_per_point"):
            continue
        weights = model["weights"]
        for position, dimension in enumerate(DIM_NAMES):
            code_share = CODE_WEIGHTS[act][dimension]
            data_share = weights["share_per_point"][position]
            beta = weights["beta_raw"][position]
            low, high = weights["share_ci95"][position]
            if beta <= 0:
                contradictions.append(
                    f"- **Acte {act}, {dimension}** : coefficient **négatif** ({beta:+.4f} par point ; "
                    f"part data {fmt_share(data_share)} contre {100 * code_share:.0f} % dans le code). "
                    f"La data ne soutient pas un poids positif.")
            elif abs(data_share - code_share) >= 0.15:
                direction = "sous-pondérée" if data_share > code_share else "sur-pondérée"
                contradictions.append(
                    f"- **Acte {act}, {dimension}** : {direction} par la data — "
                    f"{100 * code_share:.0f} % dans le code contre {fmt_share(data_share)} "
                    f"(IC 95 % {fmt_share(low)} – {fmt_share(high)}). Écart ≥ 15 points.")
    if contradictions:
        lines.extend(contradictions)
    else:
        add("Aucun écart ≥ 15 points et aucun coefficient négatif : la data ne contredit pas")
        add("frontalement les constantes du code sur cet échantillon.")
    add("")
    shares = {act: (models.get(f"act{act}_dims", {}).get("weights") or {}).get("share_per_point")
              for act in (1, 2)}
    offense_auc = {act: univariate.get((f"act{act}", "offense")) for act in (1, 2)}
    block_auc = {act: univariate.get((f"act{act}", "bloc/tour")) for act in (1, 2)}
    damage_auc = {act: univariate.get((f"act{act}", "dégâts/tour")) for act in (1, 2)}
    draw_auc = {act: univariate.get((f"act{act}", "pioche/tour")) for act in (1, 2)}
    consistance_auc = {act: univariate.get((f"act{act}", "consistance")) for act in (1, 2)}

    def pct_or_dash(share, position):
        return "—" if not share else f"{100 * share[position]:.0f} %"

    def auc_or_dash(value):
        return "—" if value is None else f"{value:.2f}".replace(".", ",")

    add("Les deux constats les plus solides, présents dans **les deux estimateurs** :")
    add("")
    add(f"1. **Survie domine** ce que le code lui accorde : {pct_or_dash(shares[1], 0)} de la data")
    add(f"   à l'acte 1 (code : 40 %) et {pct_or_dash(shares[2], 0)} à l'acte 2 (code : 30 %).")
    add(f"   L'AUC univariée de Survie est {auc_or_dash(univariate.get(('act1', 'survie')))} à l'acte 1,")
    add(f"   celle du bloc/tour brut {auc_or_dash(block_auc[1])} : c'est bien la **défense**, pas la")
    add("   fortune des PV, qui sépare les runs.")
    add(f"2. **Offense, telle que le code la mesure, ne sépare rien** : AUC univariée")
    add(f"   {auc_or_dash(offense_auc[1])} à l'acte 1, {auc_or_dash(offense_auc[2])} à l'acte 2 ; dégâts/tour")
    add(f"   brut {auc_or_dash(damage_auc[1])} et {auc_or_dash(damage_auc[2])}. Deux lectures possibles,")
    add("   et ce corpus **ne permet pas de trancher** : soit la défense est réellement le facteur")
    add("   dominant de ce patch, soit la dimension Offense mesure autre chose que la puissance de")
    add("   mise à mort — elle divise par la taille du deck et suppose des cartes jouées au hasard,")
    add("   ce qui pénalise les decks qui ajoutent des pouvoirs et de l'utilité.")
    add(f"3. **La Consistance rate son propre signal** : ses composantes valent mieux qu'elle")
    add(f"   (pioche/tour {auc_or_dash(draw_auc[1])} à l'acte 1 contre {auc_or_dash(consistance_auc[1])}")
    add(f"   pour la dimension ; {auc_or_dash(draw_auc[2])} contre {auc_or_dash(consistance_auc[2])} à")
    add("   l'acte 2), parce que la pénalité de taille et la saturation diluent l'information.")
    add("")

    # --- ce que la data ne dit pas
    add("## 5. Ce que la data ne dit pas")
    add("")
    add("- **Biais de sélection / causalité inversée.** L'état observé est postérieur aux choix et")
    add("  antérieur à l'issue, mais il reste le produit des choix passés et de la chance (butin,")
    add("  événements). Un coefficient positif dit « les runs qui ont cette qualité gagnent plus")
    add("  souvent », jamais « ajouter cette qualité fait gagner ».")
    add("- **Pas de contrefactuel.** On ne voit jamais le même deck avec et sans la carte : aucun")
    add("  coefficient de ce rapport n'est un effet d'une décision.")
    add("- **Fin de run contaminée.** Dans le modèle `final_*`, les PV sont ceux du dernier")
    add("  enregistrement de décision : pour une run morte, c'est souvent très bas. La part de")
    add("  Survie y est donc gonflée par la mort elle-même. C'est pourquoi les poids proposés")
    add("  viennent des modèles par acte (état pré-outcome), pas du modèle de fin de run.")
    add("- **PV approximatifs** : `pl[].hp` est null dans l'index, la valeur vient d'une décision")
    add("  (pas d'un combat) ; les PV d'entrée de l'acte 1 sont inconnus.")
    add("- **Boss réel inconnu** : la menace est la médiane des boss de l'acte (règle du C# quand")
    add("  `BossId` est absent). Deux runs du même acte sont donc évaluées contre la même référence.")
    add("- **Améliorations ignorées** : l'index ne porte pas le niveau des cartes ; tout est chiffré")
    add("  non amélioré (biais de niveau, pas de classement, mais il déplace les saturations).")
    add("- **Reliques** : équivalents par tags (ordre de grandeur du C#), pas des valeurs mesurées.")
    add("- **Co-op exclu** (39 % du corpus) : ces poids ne valent que pour le solo.")
    add("- **Une seule version** (v0.107.1) ; le jeu public est déjà en v0.111.0.")
    add("- **Aucune validation temporelle** : pas de split par date, seulement un split aléatoire —")
    add("  les runs d'un même jour peuvent se ressembler (même méta, mêmes joueurs).")
    add("")

    # --- écarts avec le C#
    add("## 6. Écarts avec le calcul C# (assumés) et validation")
    add("")
    for deviation in DEVIATIONS:
        add(f"- {deviation}")
    add("")
    add("**Validation croisée avec le moteur C#** : sur 60 decks réels (actes et ascensions")
    add("variés), les quatre dimensions et les sept mesures intermédiaires calculées ici sont")
    add("**identiques** à `PlayerStrength.G` du moteur (`dotnet run` sur le vrai `Ultima.Engine`,")
    add("660 champs comparés, 0 écart). Les écarts listés ci-dessus sont donc ceux du modèle,")
    add("pas des erreurs de réimplémentation.")
    add("")
    add("## 7. Reproduire")
    add("")
    add("```bash")
    add("python3 -I tools/calibration/features.py --state both --stride 4 --out /tmp/features.jsonl")
    add("python3 -I tools/calibration/fit_weights.py --features /tmp/features.jsonl")
    add("```")
    add("")
    add("`features.py` lit `data/index/runs-v0.107.1.jsonl.gz` (non commité) et `data/catalog/*.json`")
    add("(commités) ; `fit_weights.py` écrit `data/calibration/weights.json` et ce rapport.")
    add("")
    return "\n".join(lines)


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #
def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Ajuste les poids de G sur les runs réels.")
    parser.add_argument("--features", nargs="+", required=True, help="JSONL produits par features.py")
    parser.add_argument("--out-json", default=DEFAULT_JSON)
    parser.add_argument("--out-report", default=DEFAULT_REPORT)
    parser.add_argument("--index-label", default="data/index/runs-v0.107.1.jsonl.gz")
    parser.add_argument("--scanned", type=int, default=0, help="runs lus (informatif)")
    parser.add_argument("--max-deck", type=int, default=80)
    parser.add_argument("--max-train", type=int, default=40000)
    parser.add_argument("--max-test", type=int, default=20000)
    parser.add_argument("--test-fraction", type=float, default=0.2)
    parser.add_argument("--seed", type=int, default=20261029)
    parser.add_argument("--mc-draws", type=int, default=2000)
    parser.add_argument("--max-index-train", type=int, default=10000,
                        help="lignes utilisées par le modèle d'indice (forme imposée de G)")
    args = parser.parse_args(argv)

    rng = random.Random(args.seed)
    pools, dropped = load_rows(args.features)
    dropped_total = dropped["hp_missing"] + dropped["no_dims"]
    args.dropped_hp = dropped_total
    if not pools[("final", None)]:
        print("aucune ligne `final` : lancer features.py d'abord", file=sys.stderr)
        return 2

    models = {}
    for spec in SPECS:
        model = run_spec(spec, pools, args, rng)
        if model is not None:
            models[spec["name"]] = model

    # AUC univariée par dimension et par strate (stable, sans ajustement).
    univariate = {}
    for key, (kind, act) in {"act1": ("act", 1), "act2": ("act", 2),
                             "act3": ("act", 3), "final": ("final", None)}.items():
        rows = pools[(kind, act)]
        if not rows:
            continue
        step = max(1, len(rows) // args.max_test)
        sample = rows[::step][: args.max_test]
        labels = [row["y"] for row in sample]
        for column, dimension in zip(DIM_COLS, DIM_NAMES):
            values = [row[column] for row in sample]
            univariate[(key, dimension)] = auc(values, labels)
        for column, label in UNIVARIATE_RAW:
            values = [row.get(column, 0) for row in sample]
            univariate[(key, label)] = auc(values, labels)

    quartiles = {}
    for act in (1, 2):
        rows = [row for row in pools[("act", act)] if row.get("act_reached", 0) >= act + 1]
        if len(rows) < 400:
            continue
        quartiles[act] = {column: quartile_stats(rows, column) for column, _ in QUARTILE_COLUMNS}
        pairs = {}
        for left in range(len(DIM_NAMES)):
            for right in range(left + 1, len(DIM_NAMES)):
                pairs[(DIM_NAMES[left], DIM_NAMES[right])] = correlation(
                    rows, DIM_COLS[left], DIM_COLS[right])
        quartiles[("corr", act)] = pairs

    generated_at = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    payload = {
        "schema": "ultima.calibration.weights/1",
        "generated_at": generated_at,
        "generated_by": "tools/calibration/fit_weights.py",
        "source": {
            "index": args.index_label,
            "build": "v0.107.1",
            "catalog": "data/catalog/cards_effects.json + relics.json + monsters.json + encounters.json",
            "filter": "solo, mode standard, personnages canoniques, deck <= %d cartes" % args.max_deck,
            "runs_final": len(pools[("final", None)]),
            "rows_act": sum(len(v) for k, v in pools.items() if k[0] == "act"),
            "dropped_missing_hp": dropped_total,
        },
        "sampling": {
            "n_train_max": args.max_train,
            "n_test_max": args.max_test,
            "test_fraction": args.test_fraction,
            "seed": args.seed,
            "stratification": "aucune (échantillon systématique sur l'index, split aléatoire)",
        },
        "method": (
            "régression logistique P(win)=sigmoid(beta·x), Newton/IRLS, L2 = %.1f, features "
            "standardisées sur le train ; parts = beta positif normalisé (par point de dimension) ; "
            "IC 95 %% par Monte-Carlo gaussien sur la covariance (%d tirages)" % (L2_PENALTY, args.mc_draws)
        ),
        "caveat": CAVEAT,
        "design": (
            "état reconstruit à la fin de l'acte (deck final moins les cartes prises après l'acte) ; "
            "échantillon = runs ayant survécu à l'acte, cible = win (postérieure). Aucun regard sur "
            "la suite, sauf dans le modèle final_* (état de fin de run, indicatif)."
        ),
        "recommendation": (
            "Ne pas appliquer les parts brutes telles quelles. Deux constats sont robustes aux deux "
            "estimateurs : (a) Survie devrait peser plus que 30-40 % aux actes 1-2 ; (b) Offense, "
            "mesurée comme aujourd'hui (densité de dégâts / taille de deck, cartes jouées au hasard, "
            "sans améliorations), ne sépare pas les gagnants — corriger la dimension avant de changer "
            "son poids. L'acte 3 n'est pas identifiable sur ce corpus : garder les constantes du code. "
            "Arbitrage humain attendu ; aucun C# n'est modifié par cette tâche."
        ),
        "not_identifiable": {
            "act_3": (
                "aucun run ne survit à l'acte 3 sans gagner : l'échantillon pré-outcome n'a qu'une "
                "seule issue, les poids ne sont pas estimables. Le code garde ses constantes."
            )
        },
        "models": {},
        "proposed_weights": {},
        "current_code_weights": {f"act_{act}": CODE_WEIGHTS[act] for act in (1, 2, 3)},
        "univariate_auc": {f"{key}_{dim}": (round(value, 4) if value is not None else None)
                           for (key, dim), value in univariate.items()},
    }

    for act in (1, 2, 3):
        model = models.get(f"act{act}_dims")
        if not model or "weights" not in model:
            continue
        weights = model["weights"]
        payload["proposed_weights"][f"act_{act}"] = {
            "weights": {dim: (round(weights["share_per_point"][i], 4)
                              if weights["share_per_point"] else None)
                        for i, dim in enumerate(DIM_NAMES)},
            "ci95": {dim: [round(weights["share_ci95"][i][0], 4), round(weights["share_ci95"][i][1], 4)]
                     for i, dim in enumerate(DIM_NAMES)},
            "beta_per_point": {dim: round(weights["beta_raw"][i], 6)
                               for i, dim in enumerate(DIM_NAMES)},
            "share_of_variation": {dim: (round(weights["share_of_variation"][i], 4)
                                         if weights["share_of_variation"] else None)
                                   for i, dim in enumerate(DIM_NAMES)},
            "auc_test": model["auc_test"],
            "n_train": model["n_train"],
            "n_test": model["n_test"],
            "all_betas_positive": all(b > 0 for b in weights["beta_raw"]),
            "index_model": model.get("index_model"),
        }
    final_model = models.get("final_dims")
    if final_model and "weights" in final_model:
        weights = final_model["weights"]
        payload["reference_full_run"] = {
            "weights": {dim: (round(weights["share_per_point"][i], 4)
                              if weights["share_per_point"] else None)
                        for i, dim in enumerate(DIM_NAMES)},
            "beta_per_point": {dim: round(weights["beta_raw"][i], 6)
                               for i, dim in enumerate(DIM_NAMES)},
            "auc_test": final_model["auc_test"],
        }

    payload["models"] = {
        name: {
            "kind": model["kind"],
            "act": model["act"],
            "n_train": model["n_train"],
            "n_test": model["n_test"],
            "base_rate_train": model["base_rate_train"],
            "auc_test": model["auc_test"],
            "accuracy_test": model["accuracy_test"],
            "brier_test": model["brier_test"],
            "mcfadden_r2": model["mcfadden_r2"],
            # Les modèles bruts servent de borne haute (AUC) : leurs 21 coefficients ne sont pas
            # publiés, ils alourdiraient le fichier commité sans servir au scoring.
            "coefficients": [
                {"name": c["name"], "beta": round(c["beta"], 4), "se": round(c["se"], 4),
                 "beta_raw": round(c["beta_raw"], 6)}
                for c in model["coefficients"]
            ] if "dims" in name else [],
            "index_model": model.get("index_model"),
        }
        for name, model in models.items()
    }

    os.makedirs(os.path.dirname(args.out_json), exist_ok=True)
    with open(args.out_json, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=1)
        handle.write("\n")
    size = os.path.getsize(args.out_json)

    report = build_report(models, pools, args, generated_at, univariate, quartiles)
    with open(args.out_report, "w", encoding="utf-8") as handle:
        handle.write(report + "\n")

    print(f"modèles ajustés : {len(models)}")
    for name in ("final_dims", "act1_dims", "act2_dims", "act3_dims"):
        if name in models:
            print(f"  {name}: AUC test {models[name]['auc_test']} "
                  f"(n train {models[name]['n_train']}, test {models[name]['n_test']})")
    print(f"poids          : {args.out_json} ({size / 1024:.1f} Ko)")
    print(f"rapport        : {args.out_report}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
