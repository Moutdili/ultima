#!/usr/bin/env python3
"""Génère data/catalog/cards_effects.json depuis data/codex/cards.json (API spire-codex).

Source : data/codex/cards.json (577 cartes, non commité — voir docs/28 §2.3).
Sortie : data/catalog/cards_effects.json (commité, < 400 Ko) — les effets NUMÉRIQUES des cartes,
c'est-à-dire ce que les descriptions ne disent pas : dégâts, bloc, nombre de coups, pioche,
énergie, pouvoirs appliqués et valeurs d'amélioration.

Règles (aucune valeur inventée) :
- un champ absent de l'API reste `null` ; on ne le remplace jamais par 0 ;
- `upgrade` de l'API porte des DELTAS sous forme de chaînes ("+3", "-1") pour damage/block/
  cards/energy, mais un COST ABSOLU (entier) pour `cost` (ex. Banshee's Cry 9 → 7) ;
- `damage_up` / `block_up` / `draw_up` / `energy_up` = base + delta, et restent `null` si la base
  est inconnue (delta sans base = non prouvé) ;
- `hits` = `hit_count` brut ; `null` signifie « l'API ne le dit pas » (coup unique implicite, ou X) ;
- `powers` = noms des pouvoirs appliqués, dans l'ordre de l'API ; les MONTANTS sont dans `vars`
  (clés `<Power>` / `<Power>Power`) ;
- `x_cost` = `is_x_cost` de l'API : le nombre de répétitions vaut l'énergie dépensée, donc un
  `damage_base` de Whirlwind (5) se lit « 5 dégâts × X ».

Usage : python3 -I tools/index_card_effects.py [source] [sortie]
        défauts : <repo>/data/codex/cards.json → <repo>/data/catalog/cards_effects.json
"""
import json
import os
import sys

SCHEMA = "ultima.catalog.card-effects/1"

# Champs dont `upgrade` porte un delta sous forme de chaîne ("+3"), pas une valeur absolue.
DELTA_FIELDS = (
    ("damage", "damage"),
    ("block", "block"),
    ("cards", "cards_draw"),
    ("energy", "energy_gain"),
)


def parse_delta(value):
    """'+3' → 3, '-1' → -1, 2 → 2 ; tout le reste → None (jamais deviné)."""
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, str):
        try:
            return int(value.strip())
        except ValueError:
            return None
    return None


def as_int(value):
    """Entier de l'API ou None. Les floats ne sont pas attendus dans cards.json."""
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    return None


def power_names(card):
    """Noms des pouvoirs appliqués, ordre de l'API, sans doublon."""
    out = []
    for p in card.get("powers_applied") or []:
        if not isinstance(p, dict):
            continue
        name = p.get("power") or p.get("power_key")
        if isinstance(name, str) and name and name not in out:
            out.append(name)
    return out


def effect_of(card):
    upgrade = card.get("upgrade")
    upgrade = upgrade if isinstance(upgrade, dict) else {}

    damage_base = as_int(card.get("damage"))
    block_base = as_int(card.get("block"))
    draw_base = as_int(card.get("cards_draw"))
    energy_base = as_int(card.get("energy_gain"))
    cost = as_int(card.get("cost"))

    def upgraded(base, key):
        # Valeur APRÈS amélioration : base + delta si l'API en donne un ; sinon la base (upgrade ne
        # touche pas ce champ, ex. Seven Stars n'améliore que son coût). null si la base est null.
        if base is None:
            return None
        if key not in upgrade:
            return base
        delta = parse_delta(upgrade.get(key))
        return None if delta is None else base + delta

    vars_raw = card.get("vars")
    variables = {}
    if isinstance(vars_raw, dict):
        for k, v in vars_raw.items():
            if as_int(v) is not None:
                variables[str(k)] = as_int(v)

    keywords = [k for k in (card.get("keywords") or []) if isinstance(k, str) and k]

    # `cost` d'amélioration = coût ABSOLU (entier), pas un delta : 0 signifie « coûte 0 ».
    cost_up = as_int(upgrade.get("cost"))

    return {
        "id": card.get("id"),
        "name": card.get("name"),
        "cost": cost,
        "cost_up": cost if cost_up is None else cost_up,
        "x_cost": bool(card.get("is_x_cost")),
        "type": card.get("type"),
        "rarity": card.get("rarity"),
        "color": card.get("color"),
        "target": card.get("target"),
        "damage_base": damage_base,
        "damage_up": upgraded(damage_base, "damage"),
        "block_base": block_base,
        "block_up": upgraded(block_base, "block"),
        "hits": as_int(card.get("hit_count")),
        "draw_base": draw_base,
        "draw_up": upgraded(draw_base, "cards"),
        "energy_base": energy_base,
        "energy_up": upgraded(energy_base, "energy"),
        "powers": power_names(card),
        "vars": variables,
        "keywords": keywords,
        "upgrade": upgrade,
    }


FIELD_NOTES = {
    "null": "null = la valeur n'existe pas dans data/codex/cards.json ; rien n'est deviné ni mis à 0.",
    "damage_base": "dégâts par coup (`damage` de l'API). null = la carte n'inflige pas de dégâts fixes.",
    "damage_up": "valeur APRÈS amélioration : damage_base + delta upgrade.damage s'il existe, sinon damage_base (l'amélioration ne touche pas les dégâts) ; null si la base est null.",
    "block_base": "bloc gagné (`block` de l'API). null = la carte ne donne pas de bloc.",
    "block_up": "block_base + delta upgrade.block s'il existe, sinon block_base ; null si la base est null.",
    "hits": "`hit_count` brut : nombre de coups. null = non fourni par l'API (coup unique implicite ou X fois).",
    "draw_base": "cartes piochées (`cards_draw`). null = la carte ne pioche pas.",
    "draw_up": "draw_base + delta upgrade.cards s'il existe, sinon draw_base ; null si la base est null.",
    "energy_base": "énergie gagnée (`energy_gain`). null = la carte ne rend pas d'énergie.",
    "energy_up": "energy_base + delta upgrade.energy s'il existe, sinon energy_base ; null si la base est null.",
    "upgrade_other_keys": "un upgrade porté par une autre clé (repeat, extradamage, ostydamage, calculationbase, summon, strength…) n'est PAS répercuté dans damage_up/block_up : voir upgrade et vars.",
    "cost": "coût en énergie ; -1 pour les malédictions/statuts/quetes injouables et pour la carte X (CASCADE).",
    "cost_up": "upgrade.cost de l'API est un coût ABSOLU (ex. Banshee's Cry 9 → 7), pas un delta ; reporté tel quel, sinon = cost.",
    "x_cost": "is_x_cost de l'API : les répétitions valent l'énergie dépensée (damage_base = dégâts par répétition).",
    "powers": "noms des pouvoirs appliqués, ordre de l'API ; les montants sont dans vars (<Power> et <Power>Power).",
    "vars": "dict brut de l'API (montants réels, y compris calculés : CalculatedDamage, Repeat, ExtraDamage…).",
    "upgrade": "dict brut de l'API ; deltas en chaînes ('+3') pour damage/block/cards/energy, absolu pour cost.",
}


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, "data", "codex", "cards.json")
    out_path = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, "data", "catalog", "cards_effects.json")

    if not os.path.isfile(src):
        print(f"source introuvable : {src}", file=sys.stderr)
        return 2

    with open(src, encoding="utf-8") as f:
        cards = json.load(f)
    if not isinstance(cards, list):
        print(f"format inattendu (liste attendue) : {src}", file=sys.stderr)
        return 3

    effects = []
    seen = set()
    for card in cards:
        if not isinstance(card, dict):
            continue
        cid = card.get("id")
        if not isinstance(cid, str) or not cid or cid in seen:
            continue
        seen.add(cid)
        effects.append(effect_of(card))

    effects.sort(key=lambda e: e["id"])

    payload = {
        "schema": SCHEMA,
        "source": "data/codex/cards.json (API spire-codex, non commité)",
        "generated_by": "tools/index_card_effects.py",
        "count": len(effects),
        "field_notes": FIELD_NOTES,
        "cards": effects,
    }

    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)
        f.write("\n")

    dmg = sum(1 for e in effects if e["damage_base"] is not None)
    blk = sum(1 for e in effects if e["block_base"] is not None)
    multi = sum(1 for e in effects if e["hits"] is not None and e["hits"] > 1)
    xcost = sum(1 for e in effects if e["x_cost"])
    powers = sum(1 for e in effects if e["powers"])
    size_kb = os.path.getsize(out_path) / 1024
    print(f"{len(effects)} cartes -> {out_path} ({size_kb:.1f} Ko)")
    print(f"  damage {dmg}, block {blk}, multi-coups {multi}, X-cost {xcost}, pouvoirs {powers}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
