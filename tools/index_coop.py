#!/usr/bin/env python3
"""Génère data/catalog/coop_cards.json : les cartes de SOUTIEN co-op.

Source : data/catalog/cards_effects.json (COMMITÉ), et rien d'autre. On y sélectionne les cartes
dont la cible est `AnyAlly` (un allié choisi) ou `AllAllies` (tous les joueurs, porteur compris) —
c'est la définition mesurable d'une carte de soutien : elle n'agit pas sur le porteur seul.

Sortie : data/catalog/coop_cards.json (commité, quelques Ko), consommé par
Ultima.Engine/Scoring/Team/CoopScorer.cs et par status/hermes-s23.md.

Règles (aucune valeur inventée) :
- un montant absent de `vars` reste `null`/omis : la carte existe, son effet n'est pas chiffré ;
- `powers` = les pouvoirs listés par cards_effects.json, avec leur montant lu dans `vars`
  (`<Power>Power` puis `<Power>`, même règle que CardEffect.PowerAmount côté C#) ;
- `resources` = ce que la carte DONNE à la cible en dehors des pouvoirs : bloc, énergie, cartes,
  invocation — lus dans `vars` (`Block`, `Energy`, `Cards`, `Summon`) ;
- `self_costs` = ce que le porteur paie (`HpLoss`) — ce n'est pas un don ;
- `calculation` = true quand le montant dépend de l'état du board (`CalculationBase` /
  `CalculationExtra`) : la carte donne « autant de bloc que… », donc non chiffrable ici ;
- `unquantified` = true quand ni pouvoir, ni ressource, ni calcul : l'effet existe (canalisation,
  carte aléatoire) mais l'index des effets ne le chiffre pas ;
- `affects_carrier` = true pour `AllAllies` : le texte dit « ALL players », le porteur en profite.

Usage : python3 -I tools/index_coop.py [source] [sortie]
        défauts : <repo>/data/catalog/cards_effects.json → <repo>/data/catalog/coop_cards.json
"""
import json
import os
import sys

SCHEMA = "ultima.catalog.coop-cards/1"

SUPPORT_TARGETS = ("AnyAlly", "AllAllies")

# Ressources données à la cible, lues telles quelles dans `vars` (aucune conversion).
RESOURCE_KEYS = (
    ("block", "Block"),
    ("energy", "Energy"),
    ("cards", "Cards"),
    ("summon", "Summon"),
)

# Coûts payés par le porteur (jamais comptés comme un don).
SELF_COST_KEYS = (
    ("hp", "HpLoss"),
)

FIELD_NOTES = {
    "selection": "cartes de data/catalog/cards_effects.json dont target vaut AnyAlly ou AllAllies ; "
                 "aucune autre carte n'entre dans ce fichier.",
    "target": "AnyAlly = un allié choisi (le porteur ne se cible pas) ; AllAllies = tous les joueurs, "
              "porteur compris.",
    "affects_carrier": "dérivé de target : true si AllAllies (le texte dit « ALL players »).",
    "powers": "pouvoirs appliqués, avec leur montant lu dans vars (<Power>Power puis <Power>) ; "
              "amount = null si l'index ne chiffre pas ce pouvoir.",
    "resources": "ressources données en dehors des pouvoirs (bloc, énergie, cartes, invocation), "
                 "lues dans vars ; clé absente = la carte n'en donne pas.",
    "self_costs": "coût payé par le porteur (HpLoss) ; clé absente = aucun.",
    "calculation": "true = montant calculé depuis l'état du board (CalculationBase/CalculationExtra) : "
                   "non chiffrable sans l'état des joueurs.",
    "unquantified": "true = ni pouvoir, ni ressource, ni calcul : effet réel mais non chiffré par "
                    "l'index des effets (canalisation, carte aléatoire).",
    "upgrade": "dict brut de cards_effects.json (deltas en chaînes) ; non déplié dans ce fichier.",
    "description": "absente ici : les textes vivent dans data/codex/cards.json, non commité. "
                   "Ce fichier reste reproductible depuis le seul catalogue commité.",
}


def amount_of(vars_, power):
    """Montant d'un pouvoir : `<Power>Power` puis `<Power>`, sinon None (même règle qu'en C#)."""
    for key in (power + "Power", power):
        value = vars_.get(key)
        if isinstance(value, int) and not isinstance(value, bool):
            return value
    return None


def coop_entry(card):
    vars_ = card.get("vars")
    vars_ = vars_ if isinstance(vars_, dict) else {}

    powers = []
    for power in card.get("powers") or []:
        if isinstance(power, str) and power and power not in [p["power"] for p in powers]:
            powers.append({"power": power, "amount": amount_of(vars_, power)})

    resources = {}
    for name, key in RESOURCE_KEYS:
        value = vars_.get(key)
        if isinstance(value, int) and not isinstance(value, bool) and value > 0:
            resources[name] = value

    self_costs = {}
    for name, key in SELF_COST_KEYS:
        value = vars_.get(key)
        if isinstance(value, int) and not isinstance(value, bool) and value > 0:
            self_costs[name] = value

    calculation = "CalculationBase" in vars_ or "CalculationExtra" in vars_

    target = card.get("target")

    return {
        "id": card.get("id"),
        "name": card.get("name"),
        "target": target,
        "type": card.get("type"),
        "rarity": card.get("rarity"),
        "color": card.get("color"),
        "cost": card.get("cost"),
        "affects_carrier": target == "AllAllies",
        "powers": powers,
        "resources": resources,
        "self_costs": self_costs,
        "calculation": calculation,
        "unquantified": not powers and not resources and not calculation,
        "upgrade": card.get("upgrade") or {},
    }


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, "data", "catalog", "cards_effects.json")
    out_path = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, "data", "catalog", "coop_cards.json")

    if not os.path.isfile(src):
        print(f"source introuvable : {src}", file=sys.stderr)
        return 2

    with open(src, encoding="utf-8") as f:
        payload = json.load(f)
    cards = payload.get("cards") if isinstance(payload, dict) else payload
    if not isinstance(cards, list):
        print(f"format inattendu (liste de cartes attendue) : {src}", file=sys.stderr)
        return 3

    seen = set()
    coop = []
    for card in cards:
        if not isinstance(card, dict):
            continue
        cid = card.get("id")
        if not isinstance(cid, str) or not cid or cid in seen:
            continue
        if card.get("target") not in SUPPORT_TARGETS:
            continue
        seen.add(cid)
        coop.append(coop_entry(card))

    coop.sort(key=lambda c: c["id"])

    out = {
        "schema": SCHEMA,
        "source": "data/catalog/cards_effects.json (catalogue commité, targets AnyAlly | AllAllies)",
        "generated_by": "tools/index_coop.py",
        "count": len(coop),
        "counts_by_target": {
            target: sum(1 for c in coop if c["target"] == target) for target in SUPPORT_TARGETS
        },
        "field_notes": FIELD_NOTES,
        "cards": coop,
    }

    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
        f.write("\n")

    if not coop:
        print("aucune carte de soutien trouvée : la source ne contient pas les cibles attendues",
              file=sys.stderr)
        return 4

    by_target = out["counts_by_target"]
    quantified = sum(1 for c in coop if c["powers"] or c["resources"])
    print(f"{len(coop)} cartes de soutien "
          f"({by_target['AnyAlly']} AnyAlly, {by_target['AllAllies']} AllAllies), "
          f"{quantified} chiffrées → {out_path} "
          f"({os.path.getsize(out_path) / 1024:.1f} Ko)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
