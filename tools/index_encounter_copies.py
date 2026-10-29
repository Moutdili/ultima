#!/usr/bin/env python3
"""Catalogue des **copies de monstres** par rencontre : effectifs réellement observés.

Pourquoi ce fichier existe (docs/33 §3, omission n°3) : `ThreatModel.MaxHit` additionne **un**
`WorstHit` par monstre listé dans `data/catalog/encounters.json` — donc une fois par *type*.
Or les combats réels alignent des copies : `rooms[].monster_ids` de l'export contient
`["MONSTER.EXOSKELETON", "MONSTER.EXOSKELETON", "MONSTER.EXOSKELETON", ...]`, doublons inclus.
La menace réelle d'une rencontre à 3 exosquelettes est jusqu'à 3 fois celle que calcule le moteur.

Entrée
------
`data/exports/runs-<build>.jsonl.gz` (export spire-codex, **non commité**) : chaque étage
(`map_point_history[acte][étage]`) porte `rooms[]` avec `model_id` (`ENCOUNTER.*`) et
`monster_ids` (doublons inclus).

`data/index/runs-<build>.jsonl.gz` **ne contient pas** les rencontres : l'indexeur
(`tools/index_runs.py`) ne garde que les décisions (`d`) et l'état des joueurs. Le script
détecte ce cas (aucune clé `rooms`/`monster_ids` dans la première ligne) et bascule sur
l'export au lieu de produire un catalogue vide ou faux.

Sortie
------
`data/catalog/encounter_copies.json` (**commité**, < 200 Ko) :

    {"encounters": {"EXOSKELETONS_WEAK": {
        "total_fights": 1234,          # combats observés pour cette rencontre
        "max_instances": 3,            # max de copies d'UN MÊME monstre dans un combat
        "max_total_monsters": 4,       # max d'instances tous monstres confondus
        "monsters": {"EXOSKELETON": {"1": 12, "2": 100, "3": 1122}}}}}

`max_instances` est le multiplicateur qui compte pour la menace (c'est lui qui borne le
« pire coup × copies ») ; `max_total_monsters` sert au budget d'actions de la rencontre.

Filtre « combat vanilla »
-------------------------
Le corpus n'est pas propre : les runs moddées sont légion (1 021 ids de monstres hors
catalogue, des mods qui **ajoutent** des ennemis aux rencontres vanilla — mesuré :
`EXOSKELETONS_WEAK` passe de 3 à 4 instances). Un combat n'est donc compté que si sa
rencontre est au catalogue **et** que tous ses `monster_ids` y sont : les combats écartés
sont rapportés (`skipped_fights_unknown_*`), jamais recyclés en copies.

Usage
-----
  python3 -I tools/index_encounter_copies.py --limit 100000
  python3 -I tools/index_encounter_copies.py --limit 0          # tout l'export
  python3 -I tools/index_encounter_copies.py --coop             # inclure le multijoueur
  python3 -I tools/index_encounter_copies.py --report-json /tmp/copies-report.json

Options : --export/--index (corpus non commités), --catalog, --out, --modes, --limit,
--min-fights (seuil du top 10), --all-ascensions (rapport à A0 et A10+, A0 seul par défaut).
"""

from __future__ import annotations

import argparse
import gzip
import json
import os
import sys
import time
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Types de salle qui portent un combat (`entry.rooms[].room_type`).
FIGHT_ROOM_TYPES = ("monster", "elite", "boss")


def bare(value: str | None, prefix: str = "MONSTER") -> str:
    """`MONSTER.EXOSKELETON` → `EXOSKELETON` (majuscules, préfixe retiré s'il est présent)."""
    if not value:
        return ""
    text = value.strip().upper()
    head = prefix.upper() + "."
    return text[len(head):] if text.startswith(head) else text


def open_text(path: str):
    if path.endswith(".gz"):
        return gzip.open(path, "rt", encoding="utf-8")
    return open(path, encoding="utf-8")


def has_rooms(path: str) -> bool:
    """Vrai si le fichier porte `rooms`/`monster_ids` (export) — faux pour l'index compact."""
    try:
        with open_text(path) as f:
            first = f.readline()
    except OSError:
        return False
    return '"monster_ids"' in first or '"rooms"' in first


def load_catalog(catalog_dir: str) -> tuple[dict[str, dict], dict[str, dict]]:
    """Monstres (id → entrée) et rencontres (id → entrée) du catalogue commité."""
    monsters: dict[str, dict] = {}
    with open(os.path.join(catalog_dir, "monsters.json"), encoding="utf-8") as f:
        for entry in json.load(f):
            if not isinstance(entry, dict) or "_meta" in entry:
                continue
            mid = bare(entry.get("id") or entry.get("class"), "MONSTER")
            if mid:
                monsters[mid] = entry

    encounters: dict[str, dict] = {}
    with open(os.path.join(catalog_dir, "encounters.json"), encoding="utf-8") as f:
        data = json.load(f)
    items = data["encounters"] if isinstance(data, dict) else data
    for entry in items:
        if not isinstance(entry, dict) or "_meta" in entry:
            continue
        eid = bare(entry.get("id"), "ENCOUNTER")
        if eid:
            encounters[eid] = entry
    return monsters, encounters


def worst_hit(monster: dict, ascension: int) -> int:
    """Pire coup du monstre — même règle que `ThreatModel.MoveThreat.HitAt`.

    Le jeu applique la variante `ascension` à partir de A10 (`ThreatRules.AscensionDamageFrom`),
    la variante `normal` en dessous, avec repli sur l'autre quand elle manque.
    """
    worst = 0
    for move in monster.get("moves") or []:
        damage = move.get("damage") or {}
        normal, asc = damage.get("normal"), damage.get("ascension")
        if ascension >= 10:
            value = asc if asc is not None else (normal or 0)
        else:
            value = normal if normal is not None else (asc or 0)
        hits = damage.get("hit_count")
        hit = int(value) * max(1, int(hits or 1))
        if hit > worst:
            worst = hit
    return worst


def model_hit(encounter: dict, monsters: dict[str, dict], ascension: int) -> int:
    """Ce que `ThreatModel.MaxHit` compte : une fois par type listé dans la rencontre."""
    total = 0
    for mid in encounter.get("monsters") or []:
        monster = monsters.get(bare(mid, "MONSTER"))
        if monster is not None:
            total += worst_hit(monster, ascension)
    return total


def iter_fights(record: dict):
    """(rencontre, instances) pour chaque salle de combat d'une run, ordre du jeu."""
    for act in record.get("map_point_history") or []:
        floors = act if isinstance(act, list) else [act]
        for entry in floors:
            if not isinstance(entry, dict):
                continue
            for room in entry.get("rooms") or []:
                if not isinstance(room, dict):
                    continue
                model_id = room.get("model_id") or ""
                if not model_id.upper().startswith("ENCOUNTER."):
                    continue
                room_type = (room.get("room_type") or entry.get("map_point_type") or "").lower()
                if room_type not in FIGHT_ROOM_TYPES:
                    continue
                ids = [bare(m, "MONSTER") for m in (room.get("monster_ids") or [])]
                ids = [m for m in ids if m]
                if not ids:
                    continue
                yield bare(model_id, "ENCOUNTER"), ids


def weighted_median(values: list[tuple[float, int]]) -> float | None:
    """Médiane pondérée de (valeur, poids), sans déplier les combats."""
    items = sorted((v, w) for v, w in values if w > 0)
    total = sum(w for _, w in items)
    if not total:
        return None
    target = total / 2.0
    seen = 0
    for value, weight in items:
        seen += weight
        if seen >= target:
            return value
    return items[-1][0]


def percentile(values: list[tuple[float, int]], fraction: float) -> float | None:
    items = sorted((v, w) for v, w in values if w > 0)
    total = sum(w for _, w in items)
    if not total:
        return None
    target = total * fraction
    seen = 0
    for value, weight in items:
        seen += weight
        if seen >= target:
            return value
    return items[-1][0]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", default=os.path.join(ROOT, "data/exports/runs-v0.107.1.jsonl.gz"))
    ap.add_argument("--index", default=os.path.join(ROOT, "data/index/runs-v0.107.1.jsonl.gz"))
    ap.add_argument("--catalog", default=os.path.join(ROOT, "data/catalog"))
    ap.add_argument("--out", default=os.path.join(ROOT, "data/catalog/encounter_copies.json"))
    ap.add_argument("--modes", default="standard", help="modes séparés par virgule, 'all' pour tout")
    ap.add_argument("--coop", action="store_true", help="inclure les runs à plus d'un joueur")
    ap.add_argument("--limit", type=int, default=100_000, help="0 = tout l'export")
    ap.add_argument("--min-fights", type=int, default=100, help="seuil du top 10 des sous-estimations")
    ap.add_argument("--report-json", default="", help="écrit le rapport de mesure en JSON")
    args = ap.parse_args()

    source = args.export
    if has_rooms(args.index):
        source = args.index
        print(f"source      : index ({args.index}) — il porte bien rooms/monster_ids")
    else:
        print(f"source      : export ({args.export}) — l'index ne porte pas rooms/monster_ids")

    if not os.path.exists(source):
        print(f"erreur: corpus absent: {source}", file=sys.stderr)
        return 2

    monsters, encounters = load_catalog(args.catalog)
    # Pires coups précalculés : la boucle de scan ne relit pas les moves à chaque combat.
    worst: dict[str, tuple[int, int]] = {
        mid: (worst_hit(monster, 0), worst_hit(monster, 10))
        for mid, monster in monsters.items()
    }
    model_cache: dict[str, tuple[int, int]] = {}
    members: dict[str, set[str]] = {}
    for eid, encounter in encounters.items():
        model_cache[eid] = (
            model_hit(encounter, monsters, 0),
            model_hit(encounter, monsters, 10),
        )
        members[eid] = {bare(m, "MONSTER") for m in (encounter.get("monsters") or [])}
    allowed_modes = {m.strip().lower() for m in args.modes.split(",") if m.strip()}
    limit_all = allowed_modes == {"all"}

    # Agrégats : par rencontre, distribution marginale des effectifs par monstre + les
    # couples (dégâts réels, dégâts comptés par le moteur) de chaque combat.
    dist: dict[str, dict[str, Counter]] = defaultdict(lambda: defaultdict(Counter))
    fights: Counter = Counter()
    copies_fights: Counter = Counter()
    total_monsters: dict[str, Counter] = defaultdict(Counter)
    pairs: dict[int, dict[str, Counter]] = {0: defaultdict(Counter), 10: defaultdict(Counter)}
    skipped_encounter = 0
    skipped_monster = 0
    skipped_empty = 0

    runs = kept = 0
    started = time.time()
    with open_text(source) as src:
        for line in src:
            runs += 1
            try:
                record = json.loads(line)
            except json.JSONDecodeError:
                continue
            mode = (record.get("game_mode") or "standard").lower()
            if not limit_all and mode not in allowed_modes:
                continue
            players = record.get("players") or []
            if not players:
                continue
            if not args.coop and len(players) > 1:
                continue

            for encounter_id, ids in iter_fights(record):
                # Filtre « combat vanilla » : rencontre au catalogue ET tous les monstres
                # connus. Le corpus contient des runs moddées (1 021 ids de monstres hors
                # catalogue, qui ajoutent des ennemis aux rencontres vanilla) ; compter ces
                # combats gonflerait les effectifs et fabriquerait des copies inexistantes.
                if encounter_id not in encounters:
                    skipped_encounter += 1
                    continue
                if any(m not in monsters for m in ids):
                    skipped_monster += 1
                    continue
                # `monster_ids` contient aussi les pets et invocations (BYRDPIP, OSTY,
                # GUARDBOT, PAELS_LEGION…) : seuls les monstres DÉCLARÉS par la rencontre
                # comptent comme ennemis — sinon la « menace » inclurait les alliés.
                ids = [m for m in ids if m in members[encounter_id]]
                if not ids:
                    skipped_empty += 1
                    continue
                fights[encounter_id] += 1
                counts = Counter(ids)
                copied = any(c >= 2 for c in counts.values())
                if copied:
                    copies_fights[encounter_id] += 1
                for monster_id, copies in counts.items():
                    dist[encounter_id][monster_id][copies] += 1
                total_monsters[encounter_id][len(ids)] += 1
                for asc_index, asc in ((0, 0), (1, 10)):
                    real = sum(worst[m][asc_index] for m in ids if m in worst)
                    model = model_cache.get(encounter_id, (0, 0))[asc_index]
                    pairs[asc][encounter_id][(real, model, copied)] += 1
            kept += 1
            if args.limit and kept >= args.limit:
                break

    elapsed = time.time() - started
    if not fights:
        print("erreur: aucun combat rencontré — corpus vide ou filtre trop strict", file=sys.stderr)
        return 1

    observed_fights = sum(fights.values())
    out_encounters: dict[str, dict] = {}
    for encounter_id in sorted(fights):
        per_monster = {}
        max_instances = 0
        for monster_id in sorted(dist[encounter_id]):
            counts = dist[encounter_id][monster_id]
            per_monster[monster_id] = {str(k): counts[k] for k in sorted(counts)}
            max_instances = max(max_instances, max(counts))
        out_encounters[encounter_id] = {
            "total_fights": fights[encounter_id],
            "fights_with_copies": copies_fights[encounter_id],
            "copies_share": round(copies_fights[encounter_id] / fights[encounter_id], 4),
            "max_instances": max_instances,
            "max_total_monsters": max(total_monsters[encounter_id]),
            "monsters": per_monster,
        }

    copies_encounters = [
        eid for eid, entry in out_encounters.items()
        if any(k != "1" for m in entry["monsters"].values() for k in m)
    ]
    copies_fight_total = sum(copies_fights.values())

    payload = {
        "generated_by": "tools/index_encounter_copies.py",
        "sources": [os.path.basename(source)],
        "note": (
            "Effectifs observés dans rooms[].monster_ids (doublons inclus), restreints aux "
            "monstres DÉCLARÉS par la rencontre : les pets et invocations qui apparaissent "
            "aussi dans monster_ids (BYRDPIP, OSTY, GUARDBOT, PAELS_LEGION…) sont ignorés. "
            "Les distributions par monstre sont MARGINALES : la somme des « 1 » d'une rencontre "
            "à plusieurs types n'est pas le nombre de combats sans copie. max_instances = "
            "nombre max de copies d'UN MÊME monstre (c'est le multiplicateur de menace) ; "
            "max_total_monsters = instances tous monstres confondus. fights_with_copies = "
            "combats où au moins un monstre apparaît ≥ 2 fois (compté exactement)."
        ),
        "filters": {
            "modes": sorted(allowed_modes) if not limit_all else ["all"],
            "solo_only": not args.coop,
            "runs_scanned": runs,
            "runs_kept": kept,
            "limit": args.limit,
        },
        "count": len(out_encounters),
        "total_fights": observed_fights,
        "encounters_with_copies": len(copies_encounters),
        "fights_with_copies": copies_fight_total,
        "skipped_fights_unknown_encounter": skipped_encounter,
        "skipped_fights_unknown_monster": skipped_monster,
        "skipped_fights_no_enemy": skipped_empty,
        "encounters": out_encounters,
    }

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1, sort_keys=False)
        f.write("\n")
    size = os.path.getsize(args.out)

    # ---- rapport d'impact -------------------------------------------------------------
    report: dict = {
        "source": source,
        "runs_scanned": runs,
        "runs_kept": kept,
        "fights": observed_fights,
        "encounters_observed": len(out_encounters),
        "encounters_in_catalog": len(encounters),
        "encounters_with_copies": len(copies_encounters),
        "fights_with_copies": copies_fight_total,
        "skipped_fights_unknown_encounter": skipped_encounter,
        "skipped_fights_unknown_monster": skipped_monster,
        "skipped_fights_no_enemy": skipped_empty,
        "by_ascension": {},
    }

    for asc in (0, 10):
        per_encounter = []
        global_ratios: list[tuple[float, int]] = []
        global_copied: list[tuple[float, int]] = []
        global_uncopied: list[tuple[float, int]] = []
        sum_real = sum_model = 0
        for eid, entry in out_encounters.items():
            counter = pairs[asc].get(eid)
            if not counter:
                continue
            for (real, model, copied), n in counter.items():
                sum_real += real * n
                sum_model += model * n
                if model <= 0:
                    continue
                ratio = real / model
                global_ratios.append((ratio, n))
                (global_copied if copied else global_uncopied).append((ratio, n))
            if entry["total_fights"] < args.min_fights:
                continue
            ratios = [(r / m, n) for (r, m, _), n in counter.items() if m > 0]
            if not ratios:
                continue  # rencontre sans monstre catalogué : le moteur compte 0, pas de ratio
            per_encounter.append({
                "encounter": eid,
                "fights": entry["total_fights"],
                "fights_with_copies": copies_fights[eid],
                "max_instances": entry["max_instances"],
                "has_copies": copies_fights[eid] > 0,
                "model_hit": weighted_median([(m, n) for (_, m, _), n in counter.items()]),
                "real_hit": weighted_median([(r, n) for (r, _, _), n in counter.items()]),
                "median_delta": weighted_median([(r - m, n) for (r, m, _), n in counter.items()]),
                "median_ratio": weighted_median(ratios),
                "p90_ratio": percentile(ratios, 0.90),
            })

        with_copies = [row for row in per_encounter if row["has_copies"]]
        all_ratios = [(row["median_ratio"], row["fights"]) for row in per_encounter]
        total_weight = sum(w for _, w in all_ratios)
        report["by_ascension"][str(asc)] = {
            "encounters_measured": len(per_encounter),
            "encounters_with_copies": len(with_copies),
            "fights": sum(w for _, w in global_ratios),
            "sum_real_worst_hits": sum_real,
            "sum_model_worst_hits": sum_model,
            "aggregate_ratio": (sum_real / sum_model) if sum_model else None,
            "fights_weighted_mean_ratio": (
                sum(r * w for r, w in all_ratios) / total_weight if total_weight else None
            ),
            "median_ratio_all": weighted_median(all_ratios),
            "median_ratio_fights": weighted_median(global_ratios),
            "median_ratio_fights_with_copies": weighted_median(global_copied),
            "median_ratio_fights_without_copies": weighted_median(global_uncopied),
            "p90_ratio_fights_with_copies": percentile(global_copied, 0.90),
            "median_ratio_encounters_with_copies": weighted_median(
                [(row["median_ratio"], 1) for row in with_copies]
            ),
            "median_ratio_encounters_copies_dominant": weighted_median(
                [(row["median_ratio"], 1) for row in with_copies
                 if out_encounters[row["encounter"]]["copies_share"] >= 0.5]
            ),
            "top10_underestimated": sorted(
                with_copies, key=lambda row: (-row["median_ratio"], -row["median_delta"])
            )[:10],
            "encounters": sorted(per_encounter, key=lambda row: -row["median_ratio"]),
        }

    print(f"corpus      : {runs} lignes lues, {kept} runs gardées, {observed_fights} combats")
    print(f"rencontres  : {len(out_encounters)} observées / {len(encounters)} au catalogue")
    print(f"copies      : {len(copies_encounters)} rencontres avec ≥1 combat à copies")
    print(f"combats écartés : {skipped_encounter} (rencontre hors catalogue) · "
          f"{skipped_monster} (monstre hors catalogue, runs moddées) · "
          f"{skipped_empty} (aucun ennemi déclaré par la rencontre)")
    for asc in ("0", "10"):
        block = report["by_ascension"][asc]
        top = block["top10_underestimated"]
        print(f"A{asc:<2}         : médiane des ratios par combat, tous combats = "
              f"{block['median_ratio_fights']}")
        print(f"             agrégat somme(réel)/somme(modèle) = {block['aggregate_ratio']:.3f} · "
              f"rencontres à copies dominantes (n={sum(1 for r in block['encounters'] if out_encounters[r['encounter']]['copies_share'] >= 0.5)}) "
              f"médiane ×{block['median_ratio_encounters_copies_dominant']}")
        print(f"             dont combats à copies = {block['median_ratio_fights_with_copies']} "
              f"(p90 {block['p90_ratio_fights_with_copies']}) · "
              f"sans copie = {block['median_ratio_fights_without_copies']} · "
              f"médiane par rencontre (celles à copies) = "
              f"{block['median_ratio_encounters_with_copies']}")
        for row in top:
            print(f"  A{asc} {row['encounter']:<32} ×{row['median_ratio']:.2f} "
                  f"(+{row['median_delta']} dégâts, {row['fights']} combats, "
                  f"{row['fights_with_copies']} à copies, max {row['max_instances']})")
    print(f"sortie      : {args.out} ({size/1024:.1f} Ko) en {elapsed:.1f}s")
    if size > 200 * 1024:
        print(f"attention: {size/1024:.1f} Ko > budget 200 Ko", file=sys.stderr)

    if args.report_json:
        with open(args.report_json, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
            f.write("\n")
        print(f"rapport     : {args.report_json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
