#!/usr/bin/env python3
"""Inventaire des pouvoirs : ce que chacun expose au moteur (docs/49).

Pour les 260 pouvoirs (`Models/Powers/*.cs`) :
  - hooks surchargés + évaluabilité (déjà extraits dans powers.json) ;
  - primitives correspondantes (multiplicateur, plat, plafond, pioche, énergie, jeu de carte) ;
  - signaux de comportement : DynamicVars, branches conditionnelles, pouvoir qu'il applique ;
  - qui l'applique (depuis status_sources.json : cartes, monstres).

Sortie : data/catalog/power_inventory.json
Usage (machine du jeu) :
    python3 tools/powers/index_inventory.py --root ~/sts2/game/pck/src/Core --out data/catalog/power_inventory.json
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

PRIMITIVES = {
    "ModifyDamageMultiplicative": "damage_multiplier",
    "ModifyDamageAdditive": "damage_flat",
    "ModifyBlockMultiplicative": "block_multiplier",
    "ModifyBlockAdditive": "block_flat",
    "ModifyDamageCap": "damage_cap",
    "ModifyHandDraw": "draw",
    "ModifyMaxEnergy": "energy",
    "ModifyEnergyGain": "energy",
    "ModifyCardPlayCount": "card_play_count",
    "ModifyVulnerableMultiplier": "status_amplifier",
    "ModifyWeakMultiplier": "status_amplifier",
}

CODES = {
    "damage_multiplier": "dm", "damage_flat": "df", "damage_cap": "dc",
    "block_multiplier": "bm", "block_flat": "bf",
    "draw": "dr", "energy": "en", "card_play_count": "cp", "status_amplifier": "sa",
}

DYNAMIC = re.compile(r"DynamicVars")
CONDITION = re.compile(r"\bif\s*\(")
APPLY = re.compile(r"PowerCmd\.Apply<(\w+)>")


def snake(name: str) -> str:
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--powers", default="data/catalog/powers.json")
    parser.add_argument("--sources", default="data/catalog/status_sources.json")
    args = parser.parse_args()

    root = Path(args.root).expanduser() / "Models" / "Powers"
    if not root.is_dir():
        print(f"dossier introuvable : {root}", file=sys.stderr)
        return 1

    powers_doc = json.loads(Path(args.powers).read_text(encoding="utf-8"))
    powers = powers_doc.get("powers", powers_doc) if isinstance(powers_doc, dict) else powers_doc
    indexed = {p["id"]: p for p in powers if isinstance(p, dict) and p.get("id")}

    applied_by: dict[str, dict] = {}
    sources_path = Path(args.sources)
    if sources_path.exists():
        sources = json.loads(sources_path.read_text(encoding="utf-8"))
        for family, receiver in (("cards", "card"), ("monsters", "monster")):
            for row in sources.get(family, []):
                for item in row.get("applies", []):
                    power = item.get("p")
                    if not power:
                        continue
                    bucket = applied_by.setdefault(power, {"cards": 0, "monsters": 0})
                    bucket["cards" if receiver == "card" else "monsters"] += 1

    rows = []
    for path in sorted(root.glob("*.cs")):
        text = path.read_text(encoding="utf-8", errors="replace")
        power_id = snake(path.stem.replace("Power", "")) if path.stem.endswith("Power") else snake(path.stem)
        entry = indexed.get(power_id, {})

        hooks = entry.get("hooks") or {}
        primitives = sorted({PRIMITIVES[h] for h in hooks if h in PRIMITIVES})

        applies = sorted({snake(m.group(1).replace("Power", "")) for m in APPLY.finditer(text)})
        dynamic = len(DYNAMIC.findall(text))
        conditions = len(CONDITION.findall(text))

        if primitives:
            state = "hook"
        elif dynamic:
            state = "dynamic_only"
        elif conditions:
            state = "conditional_only"
        else:
            state = "no_signal"

        # Compact : les hooks vivent dans powers.json, les compteurs se recalculent.
        rows.append({
            "id": power_id,
            "s": {"hook": "h", "dynamic_only": "d", "conditional_only": "c", "no_signal": "n"}[state],
            "pr": [CODES[p] for p in primitives],
        })

    codes = {code: name for name, code in CODES.items()}
    counts: dict[str, object] = {
        "powers": len(rows),
        "with_hook": sum(1 for r in rows if r["pr"]),
        "dynamic_only": sum(1 for r in rows if r["s"] == "d"),
        "conditional_only": sum(1 for r in rows if r["s"] == "c"),
        "no_signal": sum(1 for r in rows if r["s"] == "n"),
    }
    primitives_count: dict[str, int] = {}
    for row in rows:
        for code in row["pr"]:
            name = codes.get(code, code)
            primitives_count[name] = primitives_count.get(name, 0) + 1
    counts["by_primitive"] = dict(sorted(primitives_count.items()))

    document = {
        "schema": "ultima.power-inventory/1",
        "source": {
            "root": "game/pck/src/Core (relatif au dossier du jeu)",
            "method": "hooks de powers.json + DynamicVars/if/Apply comptés dans le fichier du pouvoir",
            "caveat": "« dynamic_only » n'est pas « neutre » : effet lisible dans le code, pas encore modélisé",
        },
        "counts": counts,
        "powers": rows,
    }

    out = Path(args.out)
    out.write_text(json.dumps(document, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"pouvoirs : {len(rows)} | hook : {counts['with_hook']} | dynamic : {counts['dynamic_only']} | "
          f"conditionnel : {counts['conditional_only']} | sans signal : {counts['no_signal']} "
          f"-> {out} ({out.stat().st_size} octets)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
