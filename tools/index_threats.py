#!/usr/bin/env python3
"""Génère les catalogues du MODÈLE DE MENACE (dégâts entrants, PV effectifs).

Entrées (jamais commitées, cf. docs/28 §2.3) :
  - data/codex/monsters.json         115 monstres, chaque move avec damage.normal,
                                     damage.ascension, hit_count, block, heal, powers
  - data/codex/encounters.json       87 rencontres → monstres, acte, type de salle
  - data/codex/encounter_stats.json  50 combats observés (avg_damage, avg_turns, fatal…)

Sorties (commitées, < 400 Ko à deux) :
  - data/catalog/monsters.json       catalogue monstre, forme historique CONSERVÉE
                                     (`class`, `min_hp.values`, `moves[].values`) et
                                     enrichie des valeurs codex (`id`, `type`,
                                     `hp_normal/ascension`, `moves[].damage`,
                                     `moves[].block/heal/powers`). Les deux lecteurs
                                     existants (BossProfile, BossCatalog) continuent de
                                     fonctionner sans modification.
  - data/catalog/encounters.json     rencontre → ids de monstres + statistiques observées.

Règles :
  - **Aucune rotation de moves n'est inventée.** Le codex donne les moves un par un, pas
    leur ordre de jeu : le catalogue expose donc « pire coup par tour » (somme des
    meilleurs coups de chaque monstre, `hit_count` inclus) et « dégâts moyens observés »,
    jamais une prédiction tour par tour.
  - Aucune valeur déduite : un move sans `damage` reste sans dégâts dans le catalogue
    (le moteur ne le compte pas), au lieu d'un chiffre plausible.
  - Idempotent : relancer le script sur sa propre sortie redonne exactement le même JSON
    (les champs historiques sont recopiés tels quels, les champs codex réécrits).

Usage :
  python3 -I tools/index_threats.py [--codex DIR] [--catalog DIR] [--base FICHIER]
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# champ historique → champ codex, pour les moves : le codex nomme « EBB » ce que la
# décompilation nomme « EBB_MOVE » / « EbbMove ».
_SUFFIX = re.compile(r"(MOVE|DAMAGE)$")


def norm(text: str | None) -> str:
    """Clé de comparaison : majuscules alphanumériques seules."""
    return re.sub(r"[^A-Z0-9]", "", (text or "").upper())


def move_keys(move: dict) -> list[str]:
    """Clés candidates d'un move, par priorité décroissante et SANS doublon.

    Priorité aux champs d'origine (`state`/`method`, produits par la décompilation) sur
    `id`/`name` : ces derniers sont ajoutés par ce script, et un même move peut avoir un
    id codex voisin (TACKLE vs TACKLE_2) sans changer de state. L'ordre est fixe, donc
    le script est idempotent (aucune itération de `set`).
    """
    keys: list[str] = []
    for field in ("state", "method", "id", "name"):
        k = norm(move.get(field))
        if not k:
            continue
        for candidate in (k, _SUFFIX.sub("", k)):
            if candidate and candidate not in keys:
                keys.append(candidate)
    return keys


def codex_move_index(codex: dict) -> dict[str, dict]:
    """Index clé → move, en écartant les clés ambiguës (deux moves distincts)."""
    buckets: dict[str, dict[str, dict]] = {}
    for move in codex.get("moves") or []:
        for key in move_keys(move):
            buckets.setdefault(key, {})[move.get("id") or ""] = move
    return {key: next(iter(found.values()))
            for key, found in buckets.items() if len(found) == 1}


def match_codex(monster: dict, by_name: dict[str, dict], by_id: dict[str, dict]) -> dict | None:
    return by_name.get(norm(monster.get("class"))) or by_id.get(norm(monster.get("class")))


def css_class(name: str | None, fallback: str | None) -> str:
    """Nom de classe « historique » : PascalCase sans ponctuation (Waterfall Giant → WaterfallGiant)."""
    words = re.findall(r"[A-Za-z0-9]+", name or fallback or "")
    return "".join(w[:1].upper() + w[1:] for w in words)


def legacy_values(damage: dict | None) -> list | None:
    """Vue « historique » des dégâts : [[ascension, normal], [hit_count]] (ordre du codex source)."""
    if not damage:
        return None
    normal, ascension, hits = damage.get("normal"), damage.get("ascension"), damage.get("hit_count")
    if normal is None and ascension is None:
        return None
    values = [[ascension if ascension is not None else normal,
               normal if normal is not None else ascension]]
    if hits is not None:
        values.append([hits])
    return values


def enrich(base: dict, codex: dict) -> dict:
    """Recopie l'entrée historique et y ajoute les valeurs codex (jamais l'inverse)."""
    out = dict(base)
    out["id"] = codex["id"]
    out["name"] = codex.get("name") or base.get("class")
    out["type"] = codex.get("type")
    out["hp_normal"] = codex.get("min_hp")
    out["hp_ascension"] = codex.get("min_hp_ascension")

    moves = []
    index = codex_move_index(codex)
    for move in base.get("moves") or []:
        entry = dict(move)
        found = None
        for key in move_keys(move):
            found = index.get(key)
            if found:
                break
        if found is not None:
            entry["id"] = found.get("id")
            if found.get("damage"):
                entry["damage"] = found["damage"]
            if found.get("block") is not None:
                entry["block"] = found["block"]
            if found.get("heal") is not None:
                entry["heal"] = found["heal"]
            if found.get("powers"):
                entry["powers"] = found["powers"]
            if found.get("intent") and norm(found.get("intent")) != norm(move.get("intent")):
                entry["intent_codex"] = found["intent"]
        moves.append(entry)
    if moves:
        out["moves"] = moves
    return out


def from_codex(codex: dict) -> dict:
    """Monstre absent de la décompilation locale (4 cas) : entrée minimale, même forme."""
    moves = []
    for move in codex.get("moves") or []:
        entry = {
            "id": move.get("id"),
            "name": move.get("name"),
            # intent lisible ; les lecteurs historiques testent des sous-chaînes (« Attack »,
            # « Buff », « Status », « Heal ») donc la forme codex leur convient.
            "intent": move.get("intent"),
            "values": legacy_values(move.get("damage")),
        }
        if move.get("damage"):
            entry["damage"] = move["damage"]
        if move.get("block") is not None:
            entry["block"] = move["block"]
        if move.get("heal") is not None:
            entry["heal"] = move["heal"]
        if move.get("powers"):
            entry["powers"] = move["powers"]
        moves.append(entry)

    return {
        "class": css_class(codex.get("name"), codex.get("id")),
        "id": codex.get("id"),
        "name": codex.get("name"),
        "type": codex.get("type"),
        "hp_normal": codex.get("min_hp"),
        "hp_ascension": codex.get("min_hp_ascension"),
        "moves": moves,
    }


def act_number(act: str | None) -> int | None:
    match = re.match(r"Act\s+(\d+)", act or "")
    return int(match.group(1)) if match else None


def build_monsters(base_monsters: list[dict], codex_monsters: list[dict]) -> tuple[list[dict], dict]:
    by_name = {norm(m.get("name")): m for m in codex_monsters}
    by_id = {norm(m.get("id")): m for m in codex_monsters}

    matched_ids: set[str] = set()
    out: list[dict] = [{
        "_meta": {
            "generated_by": "tools/index_threats.py",
            "sources": ["data/codex/monsters.json", "data/codex/encounters.json"],
            "note": "moves un par un (normal + ascension + hit_count) ; aucune rotation inventée",
        }
    }]

    for base in base_monsters:
        if not isinstance(base, dict) or not base.get("class"):
            continue
        codex = match_codex(base, by_name, by_id)
        if codex is None:
            # Monstre de décompilation sans équivalent codex (mocks, deprecated) : conservé tel quel.
            out.append(dict(base))
            continue
        matched_ids.add(codex["id"])
        out.append(enrich(base, codex))

    for codex in codex_monsters:
        if codex["id"] in matched_ids:
            continue
        out.append(from_codex(codex))

    stats = {
        "monsters": len(out) - 1,
        "from_codex": len(matched_ids),
        "codex_only": len([m for m in codex_monsters if m["id"] not in matched_ids]),
        "kept_legacy_only": len([b for b in base_monsters
                                 if isinstance(b, dict) and b.get("class")
                                 and match_codex(b, by_name, by_id) is None]),
        "bosses": len([m for m in codex_monsters if m.get("type") == "Boss"]),
    }
    return out, stats


def build_encounters(codex_encounters: list[dict], stats_encounters: list[dict],
                     known_monsters: set[str]) -> tuple[dict, dict]:
    by_id = {s.get("encounter_id"): s for s in stats_encounters if s.get("encounter_id")}

    encounters = []
    unknown_links = []
    for enc in codex_encounters:
        monsters = [m.get("id") for m in enc.get("monsters") or [] if m.get("id")]
        for mid in monsters:
            if mid not in known_monsters:
                unknown_links.append({"encounter_id": enc.get("id"), "monster_id": mid})

        observed = by_id.get(enc.get("id"))
        entry = {
            "id": enc.get("id"),
            "name": enc.get("name"),
            "act": act_number(enc.get("act")),
            "act_name": enc.get("act"),
            "room_type": enc.get("room_type"),
            "is_weak": bool(enc.get("is_weak")),
            "monsters": monsters,
        }
        if observed:
            entry["stats"] = {
                "total": observed.get("total"),
                "fatal": observed.get("fatal"),
                "abandoned": observed.get("abandoned"),
                "avg_damage": observed.get("avg_damage"),
                "avg_turns": observed.get("avg_turns"),
                "characters": {
                    c["character"]: {
                        "total": c.get("total"),
                        "fatal": c.get("fatal"),
                        "avg_damage": c.get("avg_damage"),
                        "avg_turns": c.get("avg_turns"),
                    }
                    for c in observed.get("characters") or []
                    if c.get("character")
                },
            }
        encounters.append(entry)

    encounters.sort(key=lambda e: (e["act"] is None, e["act"] or 0, e["id"] or ""))
    with_stats = sum(1 for e in encounters if "stats" in e)

    document = {
        "generated_by": "tools/index_threats.py",
        "sources": ["data/codex/encounters.json", "data/codex/encounter_stats.json"],
        "note": ("stats = combats réellement observés (avg_damage / avg_turns), "
                 "absentes pour les rencontres hors échantillon"),
        "count": len(encounters),
        "stats_covered": with_stats,
        "stats_missing": len(encounters) - with_stats,
        "encounters": encounters,
    }
    return document, {"unknown_links": unknown_links, "stats_covered": with_stats}


def load(path: str):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def write(path: str, document) -> int:
    text = json.dumps(document, indent=1, ensure_ascii=False) + "\n"
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)
    return len(text.encode("utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--codex", default=os.path.join(ROOT, "data", "codex"))
    parser.add_argument("--catalog", default=os.path.join(ROOT, "data", "catalog"))
    parser.add_argument("--base", default=None,
                        help="catalogue monstre historique à enrichir "
                             "(défaut : <catalog>/monsters.json s'il existe)")
    args = parser.parse_args()

    codex_monsters_path = os.path.join(args.codex, "monsters.json")
    codex_encounters_path = os.path.join(args.codex, "encounters.json")
    codex_stats_path = os.path.join(args.codex, "encounter_stats.json")
    for path in (codex_monsters_path, codex_encounters_path):
        if not os.path.isfile(path):
            print(f"source introuvable : {path}", file=sys.stderr)
            return 2

    base_path = args.base or os.path.join(args.catalog, "monsters.json")
    base_monsters = load(base_path) if os.path.isfile(base_path) else []
    if not isinstance(base_monsters, list):
        print(f"catalogue de base inattendu (attendu : liste) : {base_path}", file=sys.stderr)
        return 2

    codex_monsters = load(codex_monsters_path)
    codex_encounters = load(codex_encounters_path)
    stats_doc = load(codex_stats_path) if os.path.isfile(codex_stats_path) else {}
    stats_encounters = stats_doc.get("encounters") or []

    monsters, monster_stats = build_monsters(base_monsters, codex_monsters)
    known = {m["id"] for m in codex_monsters if m.get("id")}
    encounters, encounter_stats = build_encounters(codex_encounters, stats_encounters, known)

    os.makedirs(args.catalog, exist_ok=True)
    size_monsters = write(os.path.join(args.catalog, "monsters.json"), monsters)
    size_encounters = write(os.path.join(args.catalog, "encounters.json"), encounters)

    print(f"monstres : {monster_stats['monsters']} "
          f"(codex {monster_stats['from_codex']}, codex seuls {monster_stats['codex_only']}, "
          f"historiques seuls {monster_stats['kept_legacy_only']}, boss {monster_stats['bosses']})")
    print(f"rencontres : {encounters['count']} dont {encounter_stats['stats_covered']} avec stats "
          f"({encounters['stats_missing']} sans)")
    for link in encounter_stats["unknown_links"]:
        print(f"  lien monstre inconnu : {link['encounter_id']} → {link['monster_id']}", file=sys.stderr)
    print(f"tailles : monsters.json {size_monsters / 1024:.1f} Ko, "
          f"encounters.json {size_encounters / 1024:.1f} Ko, "
          f"total {(size_monsters + size_encounters) / 1024:.1f} Ko")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
