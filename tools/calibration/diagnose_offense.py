#!/usr/bin/env python3
"""Diagnostic de la dimension **Offense** de G (docs/27 §7bis, docs/31).

Contexte (S15) : sur 71 634 runs, la dimension Offense du code ne sépare rien —
AUC univariée 0,500 (acte 1) et 0,459 (acte 2), coefficient β négatif. Deux hypothèses :
(a) la défense domine réellement ; (b) la formule d'Offense est fausse.

La formule actuelle (`PlayerStrengthCalculator`, `damagePerTurn`) vaut
`taux × pioche × Σdégâts / N` : une **densité moyenne** de dégâts par carte du deck. Elle suppose
donc des cartes jouées au hasard, ignore les améliorations et dilue un deck qui ajoute des
pouvoirs (une carte à 0 dégât fait baisser la moyenne alors qu'elle fait gagner).

Ce script compare plusieurs définitions candidates sur le **même corpus** que S15 (même filtre,
même reconstruction arrière, même échantillon systématique) et sort, pour chacune :

  - l'AUC univariée contre `win` (actes 1 et 2 + fin de run indicative) ;
  - le β logistique ajusté (ascension + personnage), standardisé, avec son erreur-type ;
  - le taux de victoire par quartile de la mesure composite 0..100.

Deux phases, séparées pour ne pas repayer la décompression des 541 Mo :

    python3 -I tools/calibration/diagnose_offense.py extract --out /tmp/s17-cand.jsonl
    python3 -I tools/calibration/diagnose_offense.py analyze --features /tmp/s17-cand.jsonl \
        --out-report /tmp/s17-diagnostic.md

`extract` n'écrit **rien** dans le dépôt (fichier temporaire) ; `analyze` non plus par défaut.
Aucune donnée n'est commitée : `data/index` est gitignoré.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

from features import (  # noqa: E402
    AVG_COST_FLOOR,
    Catalogs,
    OFFENSE_KILL_TURNS,
    SCALING_AT_HALF,
    _act_reached,
    _character,
    _hp_at,
    _picks,
    _state_before_after,
    clamp,
    draw_per_turn,
    energy_per_turn,
    iter_index,
    play_rate,
    rnd,
    saturate,
)

REPO_ROOT = os.path.dirname(os.path.dirname(_HERE))

#: Énergie supposée d'une carte X (même constante que le C# : plancher conservateur).
XCOST = 1.0

#: Niveau d'amélioration supposé quand on teste l'hypothèse « améliorations ignorées ».
#: L'index ne porte AUCUN niveau d'amélioration (aucun suffixe « + ») : on ne peut donc pas
#: mesurer le vrai niveau, seulement comparer « tout non amélioré » (ce que fait S15) à
#: « tout amélioré » (borne haute). Les deux sont des bornes, pas la vérité.
UPGRADED = 1


# --------------------------------------------------------------------------- #
# Agrégats de deck utiles à l'Offense
# --------------------------------------------------------------------------- #
class OffenseProfile:
    """Ce dont les définitions candidates ont besoin : dégâts, coûts, scaling, structure."""

    __slots__ = ("size", "playable", "cost", "damage", "damage_up", "damage_list",
                 "damage_up_list", "scaling", "aoe", "powers", "rares", "max_hit", "unknown")

    def __init__(self) -> None:
        self.size = 0
        self.playable = 0
        self.cost = 0.0
        self.damage = 0
        self.damage_up = 0
        self.damage_list = []
        self.damage_up_list = []
        self.scaling = 0.0
        self.aoe = 0
        self.powers = 0
        self.rares = 0
        self.max_hit = 0
        self.unknown = 0


def offense_profile(deck, catalogs: Catalogs) -> OffenseProfile:
    """Parcourt le deck une fois et agrège dégâts (base et améliorés), coûts, scaling."""
    from features import (
        FOCUS_DAMAGE_PER_POINT,
        POISON_DAMAGE_PER_POINT,
        STRENGTH_DAMAGE_PER_POINT,
        _power_amount,
    )

    out = OffenseProfile()
    out.size = len(deck)
    for card_id in deck:
        effect = catalogs.card.get(str(card_id).upper())
        if effect is None:
            out.unknown += 1
            continue
        keywords = {str(k).lower() for k in (effect.get("keywords") or ())}
        if "unplayable" in keywords:
            continue

        repeats = XCOST if effect.get("x_cost") else 1.0
        out.playable += 1
        cost = effect.get("cost")
        out.cost += XCOST if effect.get("x_cost") else max(0.0, float(cost or 0))

        hits = effect.get("hits")
        hits = hits if isinstance(hits, int) and hits > 0 else 1
        base = effect.get("damage_base")
        up = effect.get("damage_up")
        damage = float(base) * hits * repeats if base else 0.0
        damage_up = float(up if up is not None else base) * hits * repeats if (base or up) else 0.0
        if damage > 0 or damage_up > 0:
            out.damage += rnd(damage)
            out.damage_up += rnd(damage_up)
            # Une seule entrée par carte : la liste sert au « burst » (meilleures cartes).
            out.damage_list.append(rnd(damage))
            out.damage_up_list.append(rnd(damage_up))
            out.max_hit = max(out.max_hit, rnd(damage_up))
            if str(effect.get("target") or "").lower() == "allenemies":
                out.aoe += rnd(damage)

        out.scaling += (
            _power_amount(effect, "Strength") * STRENGTH_DAMAGE_PER_POINT
            + _power_amount(effect, "Poison") * POISON_DAMAGE_PER_POINT
            + _power_amount(effect, "Focus") * FOCUS_DAMAGE_PER_POINT
        )

        if str(effect.get("type") or "").lower() == "power":
            out.powers += 1
        if str(effect.get("rarity") or "").lower() in ("rare", "uncommon"):
            out.rares += 1 if str(effect.get("rarity")).lower() == "rare" else 0
    return out


# --------------------------------------------------------------------------- #
# Définitions candidates
# --------------------------------------------------------------------------- #
def candidates(deck, relics, act: int, ascension: int, catalogs: Catalogs,
               hp: int | None = 0) -> dict:
    """Calcule toutes les définitions candidates pour un deck à un acte donné.

    Chaque valeur est une **mesure brute** (dégâts/tour ou équivalent) sauf les clés `s_*`,
    qui sont des scores 0..100 saturés (la forme finale de la dimension).
    """
    from features import ASSUMED_COMBAT_TURNS, profile  # profil complet : miroir du C#

    prof, size = profile(deck, catalogs)
    off = offense_profile(deck, catalogs)
    relic = catalogs.relic_equivalents(relics)

    draw = draw_per_turn(prof.draw, size, relic["draw"])
    energy = energy_per_turn(prof.energy_gain, draw, size, relic["energy"])
    avg_cost = prof.cost / prof.playable if prof.playable > 0 else AVG_COST_FLOOR
    rate = play_rate(energy, draw, avg_cost)
    cards_per_turn = rate * draw
    worst_hit, boss_hp = catalogs.threat_reference(act, ascension)
    target = boss_hp / OFFENSE_KILL_TURNS if boss_hp > 0 else 0.0

    # Défense : sert au contrôle « la défense domine-t-elle par elle-même ? » (§6 du rapport).
    block_per_turn = cards_per_turn * prof.block / max(1, size) + prof.defense_scaling + relic["block"]

    n = max(1, size)
    # Le code C# n'inclut PAS les reliques dans le scaling d'Offense (reliquat : elles ne servent
    # qu'au texte). On teste les deux : `m_scaling` = cartes seules (fidèle au code), et la version
    # « cartes + reliques » utilisée dans les composites candidats.
    scaling_cartes = off.scaling
    scaling = off.scaling + relic["scaling"]

    # --- définitions brutes -------------------------------------------------
    # 1. densité moyenne du code : Σdégâts / N, temporisée par pioche × taux de jeu.
    densite = cards_per_turn * off.damage / n
    # 2. même densité mais tout amélioré (borne haute de l'hypothèse « upgrades ignorés »).
    densite_up = cards_per_turn * off.damage_up / n
    # 3. dégâts utiles par énergie : on convertit toute l'énergie disponible au rendement moyen.
    par_energie = (off.damage / prof.cost * energy) if prof.cost > 0 else 0.0
    par_energie_up = (off.damage_up / prof.cost * energy) if prof.cost > 0 else 0.0
    # 4. burst : on ne joue que les k meilleures cartes de dégâts du deck (k = cartes/tour).
    k = max(1, int(math.ceil(cards_per_turn))) if cards_per_turn > 0 else 0
    burst = float(sum(sorted(off.damage_list, reverse=True)[:k])) if k else 0.0
    burst_up = float(sum(sorted(off.damage_up_list, reverse=True)[:k])) if k else 0.0
    # 5. burst + scaling : la carte Force/Poison/Focus compte comme un dégât récurrent.
    burst_scaling = burst + scaling
    # 6. dégâts soutenus : densité moyenne + scaling (la version « continue » du burst).
    soutenu = densite + scaling
    # 7. densité sans dilution par les cartes injouables (malédictions/statuts restent comptés).
    densite_sans_statut = cards_per_turn * off.damage / max(1, off.playable)
    # 8. mesures de structure (contrôles, pas des définitions d'Offense).
    par_carte = off.damage / n
    aoe = cards_per_turn * off.aoe / n
    max_hit = float(off.max_hit)
    # 9. temps de mise à mort (monotone décroissant de la densité : même AUC, lit plus vite).
    kill_turns = (boss_hp / densite) if densite > 0 else float("inf")

    # --- scores 0..100 (saturation 100·x/(x+k)) ----------------------------
    def score(*parts) -> int:
        """Somme pondérée de mesures saturées : (poids, valeur, point à 50)."""
        total = sum(weight * saturate(value, half) for weight, value, half in parts)
        return rnd(clamp(total, 0, 100))

    return {
        "deck": size,
        "worst_hit": worst_hit,
        "boss_hp": boss_hp,
        "cible": round(target, 4),
        "m_densite": round(densite, 4),
        "m_densite_up": round(densite_up, 4),
        "m_par_energie": round(par_energie, 4),
        "m_par_energie_up": round(par_energie_up, 4),
        "m_burst": round(burst, 4),
        "m_burst_up": round(burst_up, 4),
        "m_burst_scaling": round(burst_scaling, 4),
        "m_soutenu": round(soutenu, 4),
        "m_densite_sans_statut": round(densite_sans_statut, 4),
        "m_par_carte": round(par_carte, 4),
        "m_aoe": round(aoe, 4),
        "m_max_hit": max_hit,
        "m_kill_turns": round(kill_turns, 2) if kill_turns != float("inf") else 999.0,
        "m_scaling": round(scaling_cartes, 4),
        "m_scaling_relic": round(scaling, 4),
        "m_powers": off.powers,
        "m_rares": off.rares,
        "m_cards_per_turn": round(cards_per_turn, 4),
        "m_block": round(block_per_turn, 4),
        "m_hp": int(hp or 0),
        "m_cards": off.playable,
        "s_actuel": score((0.80, densite, target), (0.20, scaling_cartes, SCALING_AT_HALF)),
        "s_densite_up": score((0.80, densite_up, target), (0.20, scaling_cartes, SCALING_AT_HALF)),
        "s_burst": score((0.80, burst, target), (0.20, scaling_cartes, SCALING_AT_HALF)),
        "s_burst_up": score((0.80, burst_up, target), (0.20, scaling_cartes, SCALING_AT_HALF)),
        "s_burst_scaling": score((1.00, burst_scaling, target)),
        "s_soutenu": score((1.00, soutenu, target)),
        "s_par_energie": score((0.80, par_energie, target), (0.20, scaling_cartes, SCALING_AT_HALF)),
        "s_par_carte": score((0.80, par_carte, target), (0.20, scaling_cartes, SCALING_AT_HALF)),
    }


# --------------------------------------------------------------------------- #
# Phase 1 — extraction
# --------------------------------------------------------------------------- #
def extract(args) -> int:
    catalogs = Catalogs(args.catalog)
    seen = kept = rows_out = 0
    with open(args.out, "w", encoding="utf-8") as out:
        for raw in iter_index(args.index, stride=max(1, args.stride)):
            seen += 1
            if args.mode != "all" and raw.get("m") != args.mode:
                continue
            n_players = raw.get("n") or len(raw.get("pl") or ())
            if args.players == "solo" and n_players != 1:
                continue
            player = (raw.get("pl") or [{}])[0]
            character = _character(player.get("c"))
            if character is None:
                continue
            deck_final = player.get("deck") or ()
            if args.max_deck and len(deck_final) > args.max_deck:
                continue
            decisions = raw.get("d") or []
            ascension = int(raw["asc"]) if isinstance(raw.get("asc"), int) else 0
            act_reached = _act_reached(decisions)
            if act_reached < 1:
                continue
            kept += 1
            base = {"y": int(bool(raw.get("w"))), "asc": ascension, "char": character,
                    "act_reached": act_reached}

            if args.state in ("final", "both"):
                hp, max_hp = _hp_at(decisions, player.get("id"), 99)
                if max_hp:
                    row = dict(base, kind="final", act=act_reached)
                    row.update(candidates(deck_final, player.get("rel") or (), act_reached,
                                          ascension, catalogs, hp))
                    out.write(json.dumps(row, separators=(",", ":")) + "\n")
                    rows_out += 1
            if args.state == "final":
                if args.limit and kept >= args.limit:
                    break
                continue

            picks_cards, picks_relics = _picks(decisions, player.get("id"))
            for act_index in range(min(1, act_reached - 1) + 1):
                act = act_index + 1
                deck, relics = _state_before_after(deck_final, player.get("rel") or (),
                                                   picks_cards, picks_relics, act_index)
                hp, max_hp = _hp_at(decisions, player.get("id"), act_index)
                if not max_hp:
                    continue
                row = dict(base, kind="act", act=act)
                row.update(candidates(deck, relics, act, ascension, catalogs, hp))
                out.write(json.dumps(row, separators=(",", ":")) + "\n")
                rows_out += 1
            if args.limit and kept >= args.limit:
                break
    print(f"runs lus       : {seen}")
    print(f"runs retenus   : {kept}")
    print(f"lignes écrites : {rows_out} -> {args.out} ({os.path.getsize(args.out)/1e6:.1f} Mo)")
    return 0


# --------------------------------------------------------------------------- #
# Phase 2 — analyse
# --------------------------------------------------------------------------- #
#: Définitions comparées : (clé, libellé, formule courte).
DEFINITIONS = (
    ("m_densite", "densité actuelle", "taux × pioche × Σdégâts / N"),
    ("m_densite_up", "densité, tout amélioré", "idem avec damage_up (borne haute)"),
    ("m_par_energie", "dégâts par énergie", "Σdégâts / Σcoût × énergie/tour"),
    ("m_par_energie_up", "dégâts par énergie, amélioré", "idem avec damage_up"),
    ("m_burst", "burst top-k", "somme des k meilleures cartes (k = cartes/tour)"),
    ("m_burst_up", "burst top-k, amélioré", "idem avec damage_up"),
    ("m_burst_scaling", "burst + scaling", "burst + Force/Poison/Focus"),
    ("m_soutenu", "dégâts soutenus", "densité + Force/Poison/Focus"),
    ("m_densite_sans_statut", "densité hors injouables", "Σdégâts / cartes jouables"),
    ("m_par_carte", "dégâts par carte", "Σdégâts / N (sans tempo)"),
    ("m_aoe", "dégâts de zone", "taux × pioche × ΣAOE / N"),
    ("m_max_hit", "plus gros coup", "max(damage_up) du deck"),
    ("m_kill_turns", "temps de mise à mort", "PV boss / densité (inversé)"),
    ("m_scaling", "scaling seul", "Force/Poison/Focus équivalents"),
    ("m_powers", "cartes Pouvoir", "nombre de Power (structure, contrôle)"),
    ("m_rares", "cartes Rares", "nombre de Rares (structure, contrôle)"),
    ("s_actuel", "**score actuel**", "0,8·sat(densité) + 0,2·sat(scaling)"),
    ("s_densite_up", "score densité améliorée", "0,8·sat(densité_up) + 0,2·sat(scaling)"),
    ("s_burst", "score burst", "0,7·sat(burst) + 0,3·sat(scaling)"),
    ("s_burst_up", "score burst amélioré", "0,7·sat(burst_up) + 0,3·sat(scaling)"),
    ("s_burst_scaling", "score burst+scaling", "sat(burst + scaling)"),
    ("s_soutenu", "score soutenu", "sat(densité + scaling)"),
    ("s_par_energie", "score par énergie", "0,7·sat(par_énergie) + 0,3·sat(scaling)"),
    ("s_par_carte", "score par carte", "0,8·sat(Σdégâts/N) + 0,2·sat(scaling)"),
)

CHARACTERS = ("ironclad", "silent", "defect", "necrobinder", "regent")


def load(path: str) -> dict:
    pools = {("act", 1): [], ("act", 2): [], ("final", None): []}
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            row = json.loads(line)
            key = ("act", row["act"]) if row.get("kind") == "act" else ("final", None)
            if key not in pools:
                continue
            if key[0] == "act" and row.get("act_reached", 0) < key[1] + 1:
                continue  # état post-mort : inutilisable pour cet acte (même règle que S15)
            pools[key].append(row)
    return pools


def univariate_auc(values, labels):
    from fit_weights import auc

    return auc(values, labels)


def adj_beta(rows, column, extra_controls=()):
    """β standardisé de `column` dans `win ~ column + contrôles + ascension + personnage`.

    `extra_controls` = colonnes supplémentaires standardisées (ex. `m_rares`, `m_block`).
    Retourne (β, SE) ; les colonnes constantes sont neutralisées (β = 0).
    """
    from fit_weights import fit_logistic, mean, stdev

    def z(column_name):
        values = [float(row[column_name]) for row in rows]
        mu, sd = mean(values), stdev(values)
        return [(value - mu) / sd if sd > 1e-12 else 0.0 for value in values]

    main = z(column)
    controls = [z(name) for name in extra_controls]
    design = []
    for i, row in enumerate(rows):
        line = [1.0, main[i]]
        line.extend(control[i] for control in controls)
        line.append(float(row.get("asc") or 0))
        line.extend(1.0 if row.get("char") == character else 0.0 for character in CHARACTERS[1:])
        design.append(line)
    labels = [int(row["y"]) for row in rows]
    beta, covariance, _ = fit_logistic(design, labels)
    se = math.sqrt(max(covariance[1][1], 0.0))
    return beta[1], se


def quartiles(rows, column, buckets=4):
    values = sorted(float(row[column]) for row in rows)
    if len(values) < buckets * 20:
        return None
    cuts = [values[min(len(values) - 1, int(len(values) * q))] for q in
            (1 / buckets, 2 / buckets, 3 / buckets)]
    buckets_rows = [[] for _ in range(buckets)]
    for row in rows:
        value = float(row[column])
        slot = 0
        while slot < buckets - 1 and value > cuts[slot]:
            slot += 1
        buckets_rows[slot].append(row["y"])
    return [sum(bucket) / len(bucket) if bucket else None for bucket in buckets_rows], cuts


def pct(value) -> str:
    return "—" if value is None else f"{100 * value:.0f} %"


def analyze(args) -> int:
    pools = load(args.features)
    lines = ["# Diagnostic Offense — comparaison de définitions", ""]
    add = lines.append
    add("Généré par `tools/calibration/diagnose_offense.py` — **ne pas éditer à la main**.")
    add("")

    summary = {}
    for key, label, formula in DEFINITIONS:
        for act in (1, 2):
            rows = pools[("act", act)]
            values = [float(row[key]) for row in rows]
            labels = [row["y"] for row in rows]
            beta, se = adj_beta(rows, key)
            summary[(key, act)] = {
                "auc": univariate_auc(values, labels),
                "beta": beta,
                "se": se,
                "n": len(rows),
                "positives": sum(labels),
            }

    add("## 1. Échantillon")
    add("")
    add("| strate | n | victoires | taux |")
    add("|---|---:|---:|---:|")
    for act in (1, 2):
        rows = pools[("act", act)]
        add(f"| acte {act} (survivants) | {len(rows):,} | {sum(r['y'] for r in rows):,} | "
            f"{pct(sum(r['y'] for r in rows) / len(rows)) if rows else '—'} |")
    rows = pools[("final", None)]
    add(f"| fin de run | {len(rows):,} | {sum(r['y'] for r in rows):,} | "
        f"{pct(sum(r['y'] for r in rows) / len(rows)) if rows else '—'} |")
    add("")
    add("## 2. AUC univariée et β ajusté par définition")
    add("")
    add("`β ajusté` = logistique `win ~ mesure standardisée + ascension + personnage` ; entre"
        " parenthèses, l'erreur-type. Une mesure qui « sépare » a une AUC > 0,5 **et** un β > 0"
        " avec |β| > 2·SE.")
    add("")
    add("| définition | formule | AUC a1 | β a1 | AUC a2 | β a2 | AUC fin |")
    add("|---|---|---:|---:|---:|---:|---:|")
    for key, label, formula in DEFINITIONS:
        cells = []
        for act in (1, 2):
            entry = summary[(key, act)]
            beta = "—" if entry["beta"] is None else f"{entry['beta']:+.3f} ({entry['se']:.3f})"
            cells.append(f"{entry['auc']:.3f}" if entry["auc"] is not None else "—")
            cells.append(beta)
        final_rows = pools[("final", None)]
        final_auc = (univariate_auc([float(row[key]) for row in final_rows],
                                    [row["y"] for row in final_rows]) if final_rows else None)
        cells.append(f"{final_auc:.3f}" if final_auc is not None else "—")
        add(f"| {label} | {formula} | " + " | ".join(cells) + " |")
    add("")

    # --- meilleure définition (AUC moyenne sur les deux actes, β positif exigé)
    def rank(entry):
        return (entry["auc"] or 0.0) if entry["beta"] is not None and entry["beta"] > 0 else 0.0

    best_key, best_label = None, None
    best_mean = -1.0
    for key, label, _ in DEFINITIONS:
        score = (rank(summary[(key, 1)]) + rank(summary[(key, 2)])) / 2.0
        if score > best_mean:
            best_key, best_label, best_mean = key, label, score

    add("## 3. Lecture")
    add("")
    add(f"- Meilleure définition au sens « AUC moyenne des actes 1 et 2, β positif » : "
        f"**{best_label}** (`{best_key}`), AUC moyenne {best_mean:.3f}.")
    for act in (1, 2):
        entry = summary[("m_densite", act)]
        add(f"- Densité actuelle à l'acte {act} : AUC {entry['auc']:.3f}, "
            f"β {entry['beta']:+.3f} ± {entry['se']:.3f}.")
    add("")

    # --- quartiles de la meilleure définition
    add("## 4. Taux de victoire par quartile (meilleure définition)")
    add("")
    for act in (1, 2):
        entry = quartiles(pools[("act", act)], best_key)
        if not entry:
            continue
        rates, cuts = entry
        add(f"- Acte {act} : " + " → ".join(pct(rate) for rate in rates) +
            " (seuils " + " / ".join(f"{cut:.1f}" for cut in cuts) + ")")
    add("")

    # --- mélanges burst/densité, recalculés depuis les mesures brutes stockées
    add("## 5. Mélanges burst × densité (rendements décroissants plus doux)")
    add("")
    add("Le burst seul plafonne dur : ajouter une carte de dégâts redondante ne change pas la"
        " somme des k meilleures. Un peu de densité rend la courbe plus lisse mais réintroduit"
        " la dilution. Testé ici en recalculant depuis les mesures brutes déjà extraites.")
    add("")
    add("| mélange | AUC a1 | β a1 | AUC a2 | β a2 |")
    add("|---|---:|---:|---:|---:|")
    blends = (
        ("burst + scaling (référence)", lambda r: r["m_burst"] + r["m_scaling"], 1.0, 0.0),
        ("0,75 burst + 0,25 densité + scaling", lambda r: r["m_burst"] + r["m_scaling"], 0.75, 0.25),
        ("0,50 burst + 0,50 densité + scaling", lambda r: r["m_burst"] + r["m_scaling"], 0.50, 0.50),
        ("0,83 burst + 0,17 densité + scaling", lambda r: r["m_burst"] + r["m_scaling"], 0.83, 0.17),
    )
    for label, base, w_burst, w_density in blends:
        cells = []
        for act in (1, 2):
            rows = pools[("act", act)]
            values = [w_burst * r["m_burst"] + w_density * r["m_densite"] + r["m_scaling"]
                      for r in rows]
            auc_value = univariate_auc(values, [r["y"] for r in rows])
            # β ajusté sur la mesure brute standardisée (même protocole que §2)
            mu = sum(values) / len(values)
            sd = (sum((v - mu) ** 2 for v in values) / (len(values) - 1)) ** 0.5
            cells.append(f"{auc_value:.3f}")
            if sd <= 1e-12:
                cells.append("—")
                continue
            from fit_weights import fit_logistic
            design = [[1.0, (v - mu) / sd, float(r.get("asc") or 0)] +
                      [1.0 if r.get("char") == c else 0.0 for c in CHARACTERS[1:]]
                      for v, r in zip(values, rows)]
            beta, cov, _ = fit_logistic(design, [r["y"] for r in rows])
            cells.append(f"{beta[1]:+.3f} ({max(cov[1][1], 0.0) ** 0.5:.3f})")
        add(f"| {label} | " + " | ".join(cells) + " |")
    add("")

    # --- la qualité de deck explique-t-elle le signal apparent de l'offense ?
    add("## 6. L'offense ajoute-t-elle quelque chose à la qualité de deck ?")
    add("")
    add("Contrôle : nombre de cartes Rares et de Pouvoir (structure du deck, pas des dégâts).")
    add("Si le β de l'offense s'effondre une fois ces deux contrôles ajoutés, le signal de"
        " l'acte 1 n'était pas de la puissance de mise à mort.")
    add("")
    add("| modèle | β offense (a1) | β offense (a2) |")
    add("|---|---:|---:|")
    for column, label in (("m_burst", "burst"), ("m_densite", "densité"), ("s_actuel", "score actuel")):
        cells = []
        for act in (1, 2):
            beta, se = adj_beta(pools[("act", act)], column, ("m_rares", "m_powers"))
            cells.append(f"{beta:+.3f} ({se:.3f})")
        add(f"| {label} + rares + pouvoirs | " + " | ".join(cells) + " |")
    add("")
    add("Et le test de la lecture (a) — la défense domine-t-elle **à cause** d'un arbitrage avec"
        " l'offense ? On met bloc/tour et PV dans le modèle : si le β de l'offense redevient"
        " positif, les deux se compensent dans la composition du deck ; s'il reste négatif, la"
        " défense n'est pas la seule explication.")
    add("")
    add("| modèle | β a1 | β a2 |")
    add("|---|---:|---:|")
    for column, label, controls in (
        ("m_burst", "burst (contrôlé bloc + PV)", ("m_block", "m_hp")),
        ("m_block", "bloc/tour (contrôlé burst + PV)", ("m_burst", "m_hp")),
        ("m_hp", "PV (contrôlé burst + bloc)", ("m_burst", "m_block")),
    ):
        cells = []
        for act in (1, 2):
            beta, se = adj_beta(pools[("act", act)], column, controls)
            cells.append(f"{beta:+.3f} ({se:.3f})")
        add(f"| {label} | " + " | ".join(cells) + " |")
    add("")

    text = "\n".join(lines)
    if args.out_report:
        with open(args.out_report, "w", encoding="utf-8") as handle:
            handle.write(text + "\n")
        print(f"rapport : {args.out_report}")
    print(text)
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Diagnostic de la dimension Offense de G.")
    sub = parser.add_subparsers(dest="command", required=True)

    ex = sub.add_parser("extract", help="calcule les définitions candidates sur l'index")
    ex.add_argument("--index", default=os.path.join(REPO_ROOT, "data", "index", "runs-v0.107.1.jsonl.gz"))
    ex.add_argument("--catalog", default=os.path.join(REPO_ROOT, "data", "catalog"))
    ex.add_argument("--out", required=True)
    ex.add_argument("--state", default="both", choices=("final", "both"))
    ex.add_argument("--stride", type=int, default=6)
    ex.add_argument("--limit", type=int, default=0)
    ex.add_argument("--mode", default="standard")
    ex.add_argument("--players", default="solo")
    ex.add_argument("--max-deck", type=int, default=80)
    ex.set_defaults(func=extract)

    an = sub.add_parser("analyze", help="AUC et β ajusté par définition")
    an.add_argument("--features", required=True)
    an.add_argument("--out-report", default="")
    an.set_defaults(func=analyze)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
