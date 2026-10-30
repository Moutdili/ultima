#!/usr/bin/env python3
"""Méga vérification des données d'ULTIMA (aucune dépendance lourde).

Contrôle, fichier par fichier, les invariants annoncés dans les docs : couvertures, bornes,
cohérence entre sources, absence de chemin absolu, budget de clone.

Usage : python3 tools/verify_data.py
Sortie : un rapport lisible + code retour 1 si une vérification échoue.
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

DATA = Path("data")
BUDGET = 2_097_152
MIN_PUBLISH = 200          # seuil de publication des buckets et des rencontres (docs/43, docs/46)
REQUIRED_META = ("weight_profile.json", "card_tiers.json", "relic_tiers.json", "card_archetypes.json")

failures: list[str] = []
checks = 0


def check(condition: bool, label: str, detail: str = "") -> None:
    global checks
    checks += 1
    if not condition:
        failures.append(f"{label}{' — ' + detail if detail else ''}")


def load(path: Path):
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def main() -> int:
    # ---------- budget & chemins absolus ----------
    committed = [p for p in (DATA / "catalog").glob("*.json")]
    committed += [DATA / "meta" / name for name in REQUIRED_META if (DATA / "meta" / name).exists()]
    total = sum(p.stat().st_size for p in committed)
    check(total <= BUDGET, "budget de clone", f"{total} octets > {BUDGET}")

    for path in (DATA / "catalog").glob("*.json"):
        text = path.read_text(encoding="utf-8", errors="replace")
        check("/home/" not in text, f"chemin absolu dans {path.name}")
        check("C:\\\\" not in text, f"chemin Windows dans {path.name}")

    # ---------- percentiles de deck ----------
    pct = load(DATA / "calibration" / "deck_percentiles.json")
    check(pct["schema"] == "ultima.deck-percentiles/1", "schema percentiles")
    check(len(pct["buckets"]) >= 18, "buckets percentiles", str(len(pct["buckets"])))
    for bucket in pct["buckets"]:
        check(bucket["p10"] <= bucket["p25"] <= bucket["p50"] <= bucket["p75"] <= bucket["p90"],
              f"ordre des percentiles {bucket['key']}")
        check(bucket["n"] >= MIN_PUBLISH, f"seuil n {bucket['key']}", str(bucket["n"]))
        check(0 <= bucket["p10"] and bucket["p90"] <= 100, f"bornes percentile {bucket['key']}")
    stars = [b for b in pct["buckets"] if b["key"].startswith("*|")]
    check(len(stars) >= 3, "buckets « * » par acte", str(len(stars)))
    # Les actes sous le seuil de publication (n < 200) ne sortent pas : l'écart doit rester faible.
    shortfall = pct["source"]["runs"] - sum(b["n"] for b in stars)
    check(0 <= shortfall <= 0.01 * pct["source"]["runs"],
          "decks hors buckets publiés < 1 %", f"{shortfall} decks")

    # ---------- rencontre mesurées ----------
    measure = load(DATA / "calibration" / "encounter_measure.json")
    catalog = load(DATA / "catalog" / "encounters.json")
    missing = {e["id"] for e in catalog["encounters"] if not e.get("stats")}
    measured = {e["id"] for e in measure["encounters"]}
    covered = len(missing & measured)
    check(measure["count"] == len(measure["encounters"]), "count mesure = entrées")
    check(all(e["n"] >= measure["source"]["min_n"] for e in measure["encounters"]),
          "seuil n rencontres")
    check(all(e["avg_damage"] is None or e["avg_damage"] >= 0 for e in measure["encounters"]),
          "avg_damage positif")
    check(all(c["n"] >= measure["source"]["min_n"]
              for e in measure["encounters"] for c in e.get("by_character", {}).values()),
          "seuil n par personnage")
    print(f"rencontres : {covered}/{len(missing)} des manquantes couvertes "
          f"({len(measured)} publiées, {covered and 100.0 * covered / len(missing):.0f} %)")

    # ---------- statuts ----------
    sources = load(DATA / "catalog" / "status_sources.json")
    rules = load(DATA / "catalog" / "status_rules.json")
    powers = load(DATA / "catalog" / "powers.json")
    power_ids = {p["id"] for p in (powers.get("powers", powers) if isinstance(powers, dict) else powers)}

    applies = [a for family in ("cards", "monsters") for row in sources[family] for a in row["applies"]]
    check(len(applies) == sources["counts"]["applies"], "applications = compteur")
    unknown = {a["p"] for a in applies if a.get("p")} - power_ids
    check(not unknown, "pouvoirs appliqués inconnus du catalogue", str(sorted(unknown)[:5]))
    check(all(a["l"] > 0 for a in applies), "ligne de code renseignée")

    modelled = [r for r in rules["rules"] if r["modelled"]]
    check(len(modelled) == rules["counts"]["modelled"], "modélisés = compteur")
    check(all(r["direction"] for r in modelled), "direction renseignée sur les modélisés")
    check(all(not r["modelled"] for r in rules["rules"] if not r.get("direction")),
          "non modélisé ⇔ pas de direction")
    check(all(isinstance(r["value"], (int, float)) for r in rules["rules"]), "valeurs numériques")

    # ---------- trésors ----------
    rarity = load(DATA / "catalog" / "relic_rarity.json")
    relics = load(DATA / "catalog" / "relics.json")
    relic_ids = {r["id"].upper() for r in (relics.get("relics", relics) if isinstance(relics, dict) else relics)}
    pools = {"Common": 0, "Uncommon": 0, "Rare": 0, "Shop": 0, "Event": 0, "Ancient": 0, "Starter": 0}
    orphans: list[str] = []
    for relic, pool in rarity["relics"].items():
        check(pool in pools, f"pool inconnu pour {relic}", str(pool))
        if pool in pools:
            pools[pool] += 1
        # Un fichier de relique peut exister sans traduction au catalogue : toléré, mais compté.
        if relic.upper() not in relic_ids:
            orphans.append(relic)
    check(len(rarity["relics"]) + len(rarity.get("exceptions", {})) == rarity["counts"]["relics"],
          "rareté : total = compteur")
    for name, count in rarity["counts"]["by_rarity"].items():
        if name in pools:
            check(pools[name] == count, f"pool {name}", f"{pools[name]} vs {count}")
    print(f"trésors : {len(rarity['relics'])} reliques classées "
          f"({sum(pools.values())} dans les pools réels, {len(rarity.get('exceptions', {}))} exceptions, "
          f"{len(orphans)} hors catalogue : {', '.join(sorted(orphans)[:4])})")

    # ---------- reliques ----------
    tags = load(DATA / "catalog" / "relic_tags.json")
    tagged = tags.get("relics", tags) if isinstance(tags, dict) else tags
    check(len(relic_ids) >= 300, "reliques au catalogue", str(len(relic_ids)))

    # ---------- cartes / poids / méta ----------
    cards = load(DATA / "catalog" / "cards.json")
    card_list = cards if isinstance(cards, list) else cards.get("cards", [])
    check(len(card_list) >= 500, "cartes au catalogue", str(len(card_list)))
    weights = load(DATA / "calibration" / "weights.json")
    for act, block in (weights.get("proposed_weights") or {}).items():
        values = [v for v in (block.get("weights") or {}).values() if isinstance(v, (int, float))]
        if values:
            check(abs(sum(values) - 1.0) < 0.02, f"poids {act} somment à 1", f"{sum(values):.4f}")
    current = weights.get("current_code_weights") or {}
    for act, block in current.items():
        values = [v for v in block.values() if isinstance(v, (int, float))]
        if values:
            check(abs(sum(values) - 1.0) < 0.02, f"poids code {act} somment à 1", f"{sum(values):.4f}")

    # ---------- rapport ----------
    print(f"budget : {total} / {BUDGET} octets ({100.0 * total / BUDGET:.1f} %)")
    print(f"vérifications : {checks} | échecs : {len(failures)}")
    for failure in failures:
        print(f"  ÉCHEC  {failure}")
    if not failures:
        print("PASS — toutes les données sont cohérentes")
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
