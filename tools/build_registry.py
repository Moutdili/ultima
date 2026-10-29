#!/usr/bin/env python3
"""Construit le registre exhaustif des entités du jeu et mesure la couverture.

Rien ne doit être inconnu : chaque carte (attaque, compétence, pouvoir, malédiction, statut),
relique, potion, pouvoir, monstre et rencontre a une entrée, avec :
  - sa source (fichier du jeu / catalogue),
  - ses données disponibles (effets numériques, impact mesuré, contraintes, hooks),
  - un statut explicite « no_data » quand aucune mesure n'existe.

Sortie : data/catalog/registry.json (commité) + rapport console.
Usage : python3 -I tools/build_registry.py
"""
import json
import os
import re
from collections import Counter

CAT = "data/catalog"
OUT = os.path.join(CAT, "registry.json")


def load(name, key=None):
    path = os.path.join(CAT, name)
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    if key and isinstance(data, dict) and key in data:
        return data[key]
    if isinstance(data, dict) and "entries" in data:
        return data["entries"]
    return data


def bare(value):
    v = (value or "").upper()
    return v.split(".")[-1] if "." in v else v


def snake(name):
    s = re.sub(r"(?<!^)(?=[A-Z][a-z])", "_", name)
    s = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", s)
    return s.upper()


def main():
    cards_src = load("cards.json")
    effects = {bare(c.get("id")): c for c in load("cards_effects.json", "cards")}
    impact = {bare(e.get("id")): e for e in load("curse_impact.json", "entries")}
    coop = {bare(c.get("id")) for c in load("coop_cards.json", "cards")}
    relics = {bare(r.get("id")): r for r in load("relics.json", "relics")}
    pots = {bare(p.get("id")): p for p in load("potions.json", "potions")}
    powers = {bare(p.get("id")): p for p in load("powers.json", "powers")}
    monsters = {bare(m.get("id")): m for m in load("monsters.json") if m.get("id")}
    encounters = {bare(e.get("id")): e for e in load("encounters.json", "encounters")}
    constraints = {}
    path = os.path.join(CAT, "constraints.json")
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        src = data.get("constraints") or data.get("monsters") or {}
        if isinstance(src, dict):
            constraints = {bare(k): v for k, v in src.items()}
    relic_curses = {bare(r.get("relic")): r for r in load("relic_curses.json", "entries")}

    card_entries = []
    for c in cards_src:
        cid = snake(c.get("class") or "")
        eff = effects.get(cid)
        ctype = (c.get("type") or "").lower()
        numeric = bool(eff and any(eff.get(k) for k in (
            "damage_base", "block_base", "draw_base", "energy_base", "hits")))
        entry = {
            "id": cid,
            "class": c.get("class"),
            "file": c.get("file"),
            "type": ctype,
            "rarity": (c.get("rarity") or "").lower() or None,
            "cost": c.get("cost"),
            "effects": bool(eff),
            "numeric": numeric,
            "coop": cid in coop,
        }
        if ctype in ("curse", "status"):
            imp = impact.get(cid)
            entry["impact"] = "measured" if imp and imp.get("measured") else "no_data"
            entry["toxicity"] = imp.get("toxicity") if imp else None
        card_entries.append(entry)

    relic_entries = []
    for rid, r in relics.items():
        relic_entries.append({
            "id": rid,
            "name": r.get("name_fr") or r.get("name"),
            "tags": r.get("tags") or [],
            "imposes_curse": rid in relic_curses,
        })

    potion_entries = [{
        "id": pid,
        "name": p.get("name_fr") or p.get("name"),
        "tags": p.get("tags") or [],
    } for pid, p in pots.items()]

    power_entries = []
    for pid, p in powers.items():
        hooks = p.get("hooks") or {}
        power_entries.append({
            "id": pid,
            "class": p.get("class"),
            "hooks": sorted(hooks.keys()) if isinstance(hooks, dict) else [],
            "evaluable": bool(isinstance(hooks, dict) and any(v for v in hooks.values())),
        })

    monster_entries = [{
        "id": mid,
        "type": (m.get("type") or "").lower() or None,
        "constrained": mid in constraints,
    } for mid, m in monsters.items()]

    encounter_entries = [{
        "id": eid,
        "act": e.get("act"),
        "room_type": (e.get("room_type") or "").lower() or None,
        "monsters": len(e.get("monsters") or []),
        "has_stats": bool(e.get("stats")),
    } for eid, e in encounters.items()]

    curses = [e for e in card_entries if e["type"] == "curse"]
    statuses = [e for e in card_entries if e["type"] == "status"]
    gaps = {
        "cards_without_effects": [e["id"] for e in card_entries if not e["effects"] and e["id"] != "DEPRECATED_CARD"],
        "curses_without_impact": [e["id"] for e in curses if e["impact"] == "no_data"],
        "statuses_without_impact": [e["id"] for e in statuses if e["impact"] == "no_data"],
        "powers_without_evaluable_hook": [e["id"] for e in power_entries if not e["evaluable"]],
        "relics_without_tags": [e["id"] for e in relic_entries if not e["tags"]],
        "encounters_without_stats": [e["id"] for e in encounter_entries if not e["has_stats"]],
        "monsters_without_constraint": [e["id"] for e in monster_entries if not e["constrained"]],
    }

    payload = {
        "schema": "ultima.registry/1",
        "counts": {
            "cards": len(card_entries),
            "attacks": sum(1 for e in card_entries if e["type"] == "attack"),
            "skills": sum(1 for e in card_entries if e["type"] == "skill"),
            "powers": sum(1 for e in card_entries if e["type"] == "power"),
            "curses": len(curses),
            "statuses": len(statuses),
            "quests": sum(1 for e in card_entries if e["type"] == "quest"),
            "relics": len(relic_entries),
            "potions": len(potion_entries),
            "power_models": len(power_entries),
            "monsters": len(monster_entries),
            "encounters": len(encounter_entries),
        },
        "coverage": {
            "measured_curses": sum(1 for e in curses if e["impact"] == "measured"),
            "measured_statuses": sum(1 for e in statuses if e["impact"] == "measured"),
            "numeric_cards": sum(1 for e in card_entries if e["numeric"]),
            "evaluable_powers": sum(1 for e in power_entries if e["evaluable"]),
            "constrained_monsters": sum(1 for e in monster_entries if e["constrained"]),
            "encounters_with_stats": sum(1 for e in encounter_entries if e["has_stats"]),
        },
        "gaps": gaps,
        "entities": {
            "card": card_entries,
            "relic": relic_entries,
            "potion": potion_entries,
            "power": power_entries,
            "monster": monster_entries,
            "encounter": encounter_entries,
        },
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, separators=(",", ":"))

    size = os.path.getsize(OUT)
    print(f"registre : {OUT} ({size/1024:.0f} Ko)")
    print("comptes :", json.dumps(payload["counts"], ensure_ascii=False))
    print("couverture :", json.dumps(payload["coverage"], ensure_ascii=False))
    for k, v in gaps.items():
        print(f"  {k:34s} {len(v):4d}  {v[:6]}{'…' if len(v) > 6 else ''}")


if __name__ == "__main__":
    main()
