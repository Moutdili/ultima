#!/usr/bin/env python3
"""Génère data/catalog/potions.json depuis la localisation FR du jeu (lecture seule).

Source : game/pck/localization/fra/potions.json (clés "<ID>.title" / "<ID>.description").
Sortie : data/catalog/potions.json — id, name_fr, description_fr, tags déduits des mots-clés.

Aucune donnée cachée : uniquement ce que le joueur lit dans l'inventaire.
Usage : python3 -I tools/index_potions.py [chemin/vers/potions.json]
"""
import json
import os
import re
import sys

# mots-clés FR → tag mécanique (ordre d'évaluation ; un tag n'est posé qu'une fois)
RULES = [
    # "revive" avant "heal" : une fée en bouteille n'est pas un soin de confort.
    ("revive", [r"PV atteignent 0", r"vous mourez", r"résurrect"]),
    ("heal", [r"récupérez", r"soigne", r"régénération"]),
    ("maxhp", [r"PV Max"]),
    ("damage", [r"infligez", r"dégâts?", r"perd .*PV"]),
    ("block", [r"armure", r"blindage", r"intangible", r"épines"]),
    ("energy", [r"énergie", r"energyIcons", r"étoiles?", r"starIcons"]),
    # "Main" est la main du joueur, pas la pioche : ne pas le compter comme pioche.
    ("draw", [r"piochez", r"\bpioche\b"]),
    ("debuff", [r"poison", r"faibli", r"affaibli", r"vulnérable", r"fatalité", r"entrave"]),
    ("buff", [r"force", r"dextérité", r"focalisation", r"rituel", r"tampon"]),
    ("tempo", [r"jouez", r"une fois de plus", r"coûte", r"défaussez", r"mélangez"]),
    ("cards", [r"ajoutez", r"choisissez", r"améliorez", r"forgez", r"canalisez", r"invoquez"]),
    ("gold", [r"\bor\b", r"riche"]),
]

TAG_RE = [(tag, [re.compile(p, re.IGNORECASE) for p in pats]) for tag, pats in RULES]


def tags_for(description: str) -> list[str]:
    out = []
    for tag, regs in TAG_RE:
        if any(r.search(description) for r in regs):
            out.append(tag)
    return out


def main() -> int:
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        "game", "pck", "localization", "fra", "potions.json")
    if not os.path.isfile(src):
        print(f"source introuvable : {src}", file=sys.stderr)
        return 2

    with open(src, encoding="utf-8") as f:
        loc = json.load(f)

    ids = sorted({k.split(".")[0] for k in loc})
    potions = []
    for pid in ids:
        desc = loc.get(pid + ".description", "")
        potions.append({
            "id": pid,
            "name_fr": loc.get(pid + ".title", pid),
            "description_fr": desc,
            "tags": tags_for(desc),
        })

    out_dir = os.path.join("data", "catalog")
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, "potions.json")
    payload = {
        "schema": "ultima.catalog.potions/1",
        "source": "game/pck/localization/fra/potions.json",
        "generated_by": "tools/index_potions.py",
        "count": len(potions),
        "potions": potions,
    }
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)
        f.write("\n")

    with_tags = sum(1 for p in potions if p["tags"])
    print(f"{len(potions)} potions -> {out_path} ({with_tags} avec au moins un tag)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
