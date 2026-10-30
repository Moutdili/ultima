#!/usr/bin/env python3
"""Extrait la rareté (et le pool déclaré) de chaque relique depuis le code du jeu.

Chaque relique déclare `public override RelicRarity Rarity => RelicRarity.X;`. C'est la seule
source autoritaire : aucune rareté n'est devinée. Les reliques sans déclaration sont publiées
`rarity: null` avec la raison — jamais classées d'office.

Sortie : data/catalog/relic_rarity.json
Usage (machine du jeu) :
    python3 tools/treasures/index_relics.py --root ~/sts2/game/pck/src/Core --out data/catalog/relic_rarity.json
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

RARITY = re.compile(r"RelicRarity\s+Rarity\s*=>\s*RelicRarity\.(\w+)")
POOL = re.compile(r"(?:RelicPool|Pool)\s+\w+\s*=>\s*([\w\.\(\)\"\s]+);")


def snake(class_name: str) -> str:
    return re.sub(r"(?<!^)(?=[A-Z])", "_", class_name).upper()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--relics", default="data/catalog/relics.json")
    args = parser.parse_args()

    root = Path(args.root).expanduser()
    directory = root / "Models" / "Relics"
    if not directory.is_dir():
        print(f"dossier introuvable : {directory}", file=sys.stderr)
        return 1

    known = {}
    catalog = Path(args.relics)
    if catalog.exists():
        data = json.loads(catalog.read_text(encoding="utf-8"))
        entries = data.get("relics", data) if isinstance(data, dict) else data
        for entry in entries:
            if isinstance(entry, dict) and entry.get("id"):
                known[entry["id"].upper()] = entry["id"]

    rows = []
    for path in sorted(directory.glob("*.cs")):
        text = path.read_text(encoding="utf-8", errors="replace")
        match = RARITY.search(text)
        class_name = path.stem
        relic_id = known.get(snake(class_name), snake(class_name))
        pool = POOL.search(text)
        rarity = match.group(1) if match else None
        if rarity == "None":
            rarity = None          # RelicRarity.None : mécanique spéciale, pas un pool de trésor
        rows.append({
            "id": relic_id,
            "class": class_name,
            "rarity": rarity,
            "pool_expr": pool.group(1).strip()[:80] if pool else None,
        })

    by_rarity: dict[str, int] = {}
    for row in rows:
        key = row["rarity"] or "unknown"
        by_rarity[key] = by_rarity.get(key, 0) + 1

    document = {
        "schema": "ultima.relic-rarity/1",
        "source": {
            "root": "game/pck/src/Core (relatif au dossier du jeu)",
            "method": "RelicRarity Rarity => RelicRarity.X ; lecture seule",
            "caveat": "la rareté définit les pools ; les probabilités de coffre ne sont pas dans ce code indexé",
        },
        "counts": {"relics": len(rows), "by_rarity": by_rarity},
        # Carte compacte id -> rareté : le budget de clone est strict (2 Mo).
        "relics": {row["id"]: row["rarity"] for row in rows if row["rarity"]},
        "exceptions": {row["id"]: "aucune déclaration RelicRarity" for row in rows if not row["rarity"]},
        "pools": {row["id"]: row["pool_expr"] for row in rows if row.get("pool_expr")},
    }

    out = Path(args.out)
    out.write_text(json.dumps(document, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"reliques : {len(rows)} | raretés : {by_rarity} -> {out} ({out.stat().st_size} octets)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
