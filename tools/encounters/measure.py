#!/usr/bin/env python3
"""Mesure les statistiques des rencontres sur le corpus de runs (source : exports spire-codex).

Sortie : data/calibration/encounter_measure.json — n, dégâts moyens, tours moyens, morts.
Sert aux 37 rencontres que la source communautaire ne couvre pas (docs/46).

Conventions écrites dans le fichier :
  - solo uniquement (un seul joueur) : en co-op les dégâts ne sont pas attribuables à un étage ;
  - les dégâts d'un étage sont attribués à la PREMIÈRE salle de l'étage (jamais comptés deux fois) ;
  - une rencontre n'est publiée qu'à partir de MIN_N occurrences.
"""
import collections
import gzip
import json
import sys
from pathlib import Path

SRC = "data/exports/runs-v0.107.1.jsonl.gz"
OUT = "data/calibration/encounter_measure.json"
MIN_N = 200


def bare(value: str | None) -> str:
    if not value:
        return ""
    return value.split(".", 1)[1] if "." in value else value


def main() -> int:
    acc: dict[str, dict] = {}
    runs = solo = 0

    with gzip.open(SRC, "rt", encoding="utf-8") as handle:
        for line in handle:
            runs += 1
            if runs % 50_000 == 0:
                print(f"  {runs} runs lus…", flush=True)

            run = json.loads(line)
            players = run.get("players") or []
            if len(players) != 1:
                continue
            solo += 1

            character = bare(players[0].get("character"))
            killed_by = bare(run.get("killed_by_encounter"))
            acts = run.get("map_point_history") or []

            for act_index, act in enumerate(acts):
                for floor in act:
                    stats = floor.get("player_stats") or []
                    rooms = floor.get("rooms") or []
                    if not stats or not rooms:
                        continue

                    damage = stats[0].get("damage_taken") or 0
                    for position, room in enumerate(rooms):
                        model = bare(room.get("model_id"))
                        if not model:
                            continue

                        entry = acc.setdefault(model, {
                            "id": model,
                            "act": act_index + 1,
                            "n": 0,
                            "damage": 0,
                            "turns": 0,
                            "turns_n": 0,
                            "fatal": 0,
                            "characters": collections.Counter(),
                            "per_character": {},
                        })
                        entry["n"] += 1
                        entry["characters"][character] += 1
                        per = entry["per_character"].setdefault(character, {"n": 0, "damage": 0, "turns": 0, "turns_n": 0})
                        per["n"] += 1
                        if position == 0:
                            entry["damage"] += damage
                            per["damage"] += damage
                        turns = room.get("turns_taken") or 0
                        if turns:
                            entry["turns"] += turns
                            entry["turns_n"] += 1
                            per["turns"] += turns
                            per["turns_n"] += 1
                        if model == killed_by:
                            entry["fatal"] += 1

    published = []
    for model, entry in sorted(acc.items()):
        if entry["n"] < MIN_N:
            continue
        published.append({
            "id": model,
            "act": entry["act"],
            "n": entry["n"],
            "avg_damage": round(entry["damage"] / entry["n"], 2),
            "avg_turns": round(entry["turns"] / entry["turns_n"], 2) if entry["turns_n"] else None,
            "fatal": entry["fatal"],
            "fatal_rate": round(entry["fatal"] / entry["n"], 4),
            "by_character": {
                name: {
                    "n": values["n"],
                    "avg_damage": round(values["damage"] / values["n"], 2),
                    "avg_turns": round(values["turns"] / values["turns_n"], 2) if values["turns_n"] else None,
                }
                for name, values in sorted(entry["per_character"].items())
                if values["n"] >= MIN_N
            },
        })

    # Absence EXPLICITE (T5 §3), en format COMPACT : le corpus contient des centaines de modèles
    # internes sous le seuil (1444 au dernier passage) ; publier une ligne par modèle coûtait
    # 137 Ko et cassait le budget de 2 Mo. On publie donc :
    #   - les rencontres du CATALOGUE absentes ou sous le seuil, avec leur n (celles qu'un joueur
    #     peut croiser, et les deux trous documentés) ;
    #   - le volume global des autres (compte + n max), pour que le silence reste mesuré.
    catalog_path = Path("data/catalog/encounters.json")
    known: set[str] = set()
    if catalog_path.exists():
        catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
        known = {row["id"] for row in catalog.get("encounters", []) if row.get("id")}

    below = [(model, entry) for model, entry in sorted(acc.items()) if entry["n"] < MIN_N]
    visible = {model: entry["n"] for model, entry in below if model in known}
    absent = sorted(known - set(acc))
    under = {
        "below_threshold_total": len(below),
        "below_threshold_max_n": max((entry["n"] for _, entry in below), default=0),
        "catalog_below_threshold": visible,
        "absent_from_corpus": absent,
    }

    document = {
        "schema": "ultima.encounter-measure/1",
        "source": {
            "file": SRC,
            "runs": runs,
            "solo": solo,
            "min_n": MIN_N,
            "method": "damage_taken de l'étage attribué à la première salle ; solo uniquement",
            "caveat": (
                "mesure d'observation : les dégâts incluent les cartes et reliques du joueur, "
                "pas seulement la rencontre ; à lire comme « dégâts subis à cet étage », "
                "jamais comme une propriété intrinsèque du monstre"
            ),
        },
        "count": len(published),
        "encounters": published,
        "not_measured_count": under["below_threshold_total"] + len(under["absent_from_corpus"]),
        "not_measured": under,
    }

    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump(document, handle, ensure_ascii=False, separators=(",", ":"))

    print(f"runs lus : {runs} | solo : {solo} | publiées : {len(published)} | "
          f"sous seuil : {under['below_threshold_total']} | absentes : {len(under['absent_from_corpus'])}"
          f" -> {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
