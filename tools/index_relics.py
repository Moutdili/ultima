#!/usr/bin/env python3
"""Génère data/catalog/relics.json depuis la localisation FR du jeu (lecture seule).

Sortie : id, name_fr, description_fr, tags mécaniques déduits des mots-clés.
Sert au scoring contextuel : une relique de PV max ne vaut pas la même chose à 70/70 PV
qu'à 20/70. Aucune donnée cachée, uniquement le texte que lit le joueur.

ATTENTION : cette passe ne produit que les tags « bruts ». `tools/index_relic_tags.py` les
COMPLÈTE (jamais ne les écrase) et ajoute `tags_source` + `relic_tags.json` ; le relancer
après toute régénération, sinon 140 reliques reviennent sans tag.
Usage : python3 -I tools/index_relics.py [chemin/vers/relics.json]
"""
import json
import os
import re
import sys

RULES = [
    ("revive", [r"PV atteignent 0", r"vous mourez", r"résurrect", r"surviv"]),
    ("maxhp", [r"PV Max", r"PV maximum"]),
    ("heal", [r"soigne", r"récupérez", r"régénération"]),
    ("block", [r"armure", r"blindage", r"intangible", r"épines", r"bloc"]),
    ("energy", [r"énergie", r"energyIcons"]),
    ("draw", [r"piochez", r"\bpioche\b", r"cartes? suppl"]),
    ("strength", [r"force"]),
    ("dexterity", [r"dextérité"]),
    ("debuff", [r"poison", r"faibli", r"affaibli", r"vulnérable", r"fatalité"]),
    ("gold", [r"\bor\b", r"pièces d'or", r"riche"]),
    ("shop", [r"marchand", r"boutique"]),
    ("curse", [r"malédiction"]),
    ("potion", [r"potion"]),
]

TAG_RE = [(tag, [re.compile(p, re.IGNORECASE) for p in pats]) for tag, pats in RULES]


def tags_for(description: str) -> list[str]:
    return [tag for tag, regs in TAG_RE if any(r.search(description) for r in regs)]


def main() -> int:
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        "game", "pck", "localization", "fra", "relics.json")
    if not os.path.isfile(src):
        print(f"source introuvable : {src}", file=sys.stderr)
        return 2

    with open(src, encoding="utf-8") as f:
        loc = json.load(f)

    relics = []
    for rid in sorted({k.split(".")[0] for k in loc}):
        desc = loc.get(rid + ".description", "")
        relics.append({
            "id": rid,
            "name_fr": loc.get(rid + ".title", rid),
            "description_fr": desc,
            "tags": tags_for(desc),
        })

    os.makedirs(os.path.join("data", "catalog"), exist_ok=True)
    out = os.path.join("data", "catalog", "relics.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump({
            "schema": "ultima.catalog.relics/1",
            "source": "game/pck/localization/fra/relics.json",
            "generated_by": "tools/index_relics.py",
            "count": len(relics),
            "relics": relics,
        }, f, ensure_ascii=False, indent=1)
        f.write("\n")

    tagged = sum(1 for r in relics if r["tags"])
    print(f"{len(relics)} reliques -> {out} ({tagged} avec au moins un tag)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
