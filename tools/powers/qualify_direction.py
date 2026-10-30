#!/usr/bin/env python3
"""Qualifie la DIRECTION des pouvoirs à hook de dégâts/armure depuis les gardes du code.

Un pouvoir ne dit pas sa direction dans son nom de hook (Weak et Vulnerable utilisent tous deux
ModifyDamageMultiplicative). Mais la garde du corps de méthode la donne :

    if (target != base.Owner) return 1m;      -> le pouvoir s'applique quand son porteur EST la cible
                                                 => il modifie les dégâts SUBIS   (taken_*)
    if (dealer != base.Owner) return 1m;      -> s'applique quand son porteur INFLIGE
                                                 => dégâts INFLIGÉS               (dealt_*)
    if (base.Owner != target) return 1m;      -> idem pour l'armure (block_*)

Sortie : proposition de direction + ligne de preuve + raison. Rien n'est appliqué ici : le
générateur de règles (build_status_rules.py) reste la seule source des valeurs.

Usage (machine du jeu) :
    python3 tools/powers/qualify_direction.py --root ~/sts2/game/pck/src/Core --out /tmp/directions.json
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

HOOK = re.compile(r"public override decimal (ModifyDamage\w+|ModifyBlock\w+)\s*\(")
TARGET_GUARD = re.compile(r"if\s*\(\s*target\s*!=\s*base\.Owner\s*\)")
TARGET_GUARD_ALT = re.compile(r"if\s*\(\s*base\.Owner\s*!=\s*target\s*\)")
DEALER_GUARD = re.compile(r"if\s*\(\s*dealer\s*!=\s*base\.Owner")
CARD_OWNER_GUARD = re.compile(r"cardSource\.Owner\.Creature\s*!=\s*base\.Owner")


def snake(class_name: str) -> str:
    name = class_name[:-5] if class_name.endswith("Power") else class_name
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


def body_at(text: str, start: int, limit: int = 1400) -> str:
    return text[start:start + limit]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    directory = Path(args.root).expanduser() / "Models" / "Powers"
    if not directory.is_dir():
        print(f"dossier introuvable : {directory}", file=sys.stderr)
        return 1

    rows = []
    for path in sorted(directory.glob("*.cs")):
        text = path.read_text(encoding="utf-8", errors="replace")
        for match in HOOK.finditer(text):
            hook = match.group(1)
            body = body_at(text, match.start())
            line = text.count("\n", 0, match.start()) + 1

            direction = None
            why = None
            if TARGET_GUARD.search(body) or TARGET_GUARD_ALT.search(body):
                direction = "taken_cap" if "Cap" in hook else ("block_mult" if "Block" in hook else "taken_mult")
                why = "garde `target != base.Owner` : le pouvoir agit sur son porteur en tant que CIBLE"
            elif DEALER_GUARD.search(body) or CARD_OWNER_GUARD.search(body):
                direction = "dealt_mult"
                why = "garde `dealer != base.Owner` / `cardSource.Owner` : le pouvoir agit sur son porteur en tant qu'ATTAQUANT"
            elif "Additive" in hook and "Block" not in hook:
                direction = None
                why = "hook additif sans garde de camp : le porteur peut être attaquant ou cible"

            rows.append({
                "id": snake(path.stem),
                "hook": hook,
                "line": line,
                "file": path.name,
                "direction": direction,
                "why": why or "aucune garde de camp reconnue dans les 1400 premiers caractères",
            })

    document = {
        "schema": "ultima.power-directions/1",
        "source": {
            "root": "game/pck/src/Core (relatif au dossier du jeu)",
            "method": "garde de camp (target/dealer/cardSource) dans le corps du hook ; lecture seule",
            "caveat": "une garde absente ne prouve pas l'absence de condition : voir la raison publiée",
        },
        "counts": {
            "powers_with_hook": len(rows),
            "qualified": sum(1 for r in rows if r["direction"]),
            "unqualified": sum(1 for r in rows if not r["direction"]),
        },
        "powers": rows,
    }

    out = Path(args.out)
    out.write_text(json.dumps(document, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"pouvoirs à hook : {len(rows)} | qualifiés : {document['counts']['qualified']} | "
          f"non qualifiés : {document['counts']['unqualified']} -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
