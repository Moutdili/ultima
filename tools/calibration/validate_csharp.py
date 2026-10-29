#!/usr/bin/env python3
"""Validation croisée `features.py` ↔ `PlayerStrength.G` (moteur C#) sur des decks réels.

Pourquoi : `features.py` **rejoue** la formule du C# en Python. Une correction de formule (comme
celle d'Offense en S17) peut silencieusement diverger des deux côtés — le rapport de calibration
n'aurait alors plus rien à voir avec le score que le moteur calcule. Ce script compare les deux
implémentations champ par champ sur des decks réels de l'index.

Méthode : on tire N decks réels (actes et ascensions variés, reconstruction arrière de
`features.py`), on écrit un petit harnais console qui appelle le **vrai**
`PlayerStrengthCalculator`, puis on compare dimensions (entiers) et mesures (flottants).

    python3 -I tools/calibration/validate_csharp.py --decks 60

Sortie : nombre de decks comparés, nombre de champs comparés, écarts (0 attendu). Le harnais vit
dans un répertoire temporaire (rien n'est commité, rien n'est ajouté à la solution).
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

from features import (  # noqa: E402
    Catalogs,
    _act_reached,
    _character,
    _hp_at,
    _picks,
    _state_before_after,
    dimensions,
    iter_index,
)

REPO_ROOT = os.path.dirname(os.path.dirname(_HERE))

CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{engine}" />
  </ItemGroup>
</Project>
"""

PROGRAM = """using System.Text.Json;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Strength;
using Ultima.Saves.Model;

var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(args[0]), options) ?? new();
var calc = new PlayerStrengthCalculator();
var lines = new List<string>();
foreach (var e in entries)
{
    var deck = (e.Deck ?? new List<string>())
        .Select(id => new CardInstance(id, e.Upgrades ?? 0, null, null)).ToArray();
    var ctx = new ScoringContext(e.Character ?? "CHARACTER.IRONCLAD", e.ActIndex, e.Boss, e.Asc,
        10, deck, e.Relics ?? new List<string>(), 99, e.Hp, e.MaxHp);
    var g = calc.Compute(ctx);
    lines.Add(JsonSerializer.Serialize(new
    {
        id = e.Id, survie = g.Survie, offense = g.Offense, consistance = g.Consistance,
        couverture = g.Couverture, total = g.Total, measures = g.Measures,
    }));
}
File.WriteAllLines(args[1], lines);
Console.WriteLine($"{lines.Count} deck(s)");
internal sealed class Entry
{
    public string? Id { get; set; }
    public List<string>? Deck { get; set; }
    public List<string>? Relics { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int ActIndex { get; set; }
    public int Asc { get; set; }
    public string? Boss { get; set; }
    public string? Character { get; set; }
    public int? Upgrades { get; set; }
}
"""

#: Mesures comparées (celles que features.py publie et que le rapport utilise).
MEASURES = (
    ("damagePerTurn", "damage_per_turn"),
    ("damageDensityPerTurn", "damage_density"),
    ("burstCards", "burst_cards"),
    ("blockPerTurn", "block_per_turn"),
    ("drawPerTurn", "draw_per_turn"),
    ("energyPerTurn", "energy_per_turn"),
    ("avgCost", "avg_cost"),
    ("playRate", "play_rate"),
    ("scaling", "scaling"),
    ("scalingCards", "scaling_cards"),
    ("healPerCombat", "heal_per_combat"),
    ("threat", "worst_hit"),
    ("bossHp", "boss_hp"),
)


def collect(index: str, catalog_dir: str, target: int) -> list[dict]:
    """Tire des decks réels variés : fin d'acte 1/2 et fin de run, actes et ascensions mélangés."""
    catalogs = Catalogs(catalog_dir)
    entries: list[dict] = []
    seen = 0
    # Pas systématique : on veut ~target decks en parcourant peu (l'index est énorme).
    for raw in iter_index(index, stride=997):
        seen += 1
        if raw.get("m") != "standard" or (raw.get("n") or len(raw.get("pl") or ())) != 1:
            continue
        player = (raw.get("pl") or [{}])[0]
        character = _character(player.get("c"))
        if character is None:
            continue
        deck_final = player.get("deck") or ()
        if not deck_final or len(deck_final) > 80:
            continue
        decisions = raw.get("d") or []
        ascension = int(raw["asc"]) if isinstance(raw.get("asc"), int) else 0
        act_reached = _act_reached(decisions)
        picks_cards, picks_relics = _picks(decisions, player.get("id"))
        marker = f"run{seen}"

        for act_index in range(min(2, act_reached - 1) + 1):
            deck, relics = _state_before_after(deck_final, player.get("rel") or (),
                                               picks_cards, picks_relics, act_index)
            hp, max_hp = _hp_at(decisions, player.get("id"), act_index)
            if not max_hp or not deck:
                continue
            entries.append({
                "id": f"{marker}-act{act_index + 1}",
                "deck": list(deck), "relics": list(relics),
                "hp": hp, "maxHp": max_hp, "actIndex": act_index, "asc": ascension,
                "character": "CHARACTER." + character.upper(), "boss": None,
            })
        hp, max_hp = _hp_at(decisions, player.get("id"), 99)
        if max_hp:
            entries.append({
                "id": f"{marker}-final",
                "deck": list(deck_final), "relics": list(player.get("rel") or ()),
                "hp": hp, "maxHp": max_hp, "actIndex": act_reached - 1, "asc": ascension,
                "character": "CHARACTER." + character.upper(), "boss": None,
            })
        if len(entries) >= target:
            break
    return entries[:target]


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Compare features.py au moteur C# sur des decks réels.")
    parser.add_argument("--index", default=os.path.join(REPO_ROOT, "data", "index", "runs-v0.107.1.jsonl.gz"))
    parser.add_argument("--catalog", default=os.path.join(REPO_ROOT, "data", "catalog"))
    parser.add_argument("--decks", type=int, default=60)
    #: features.py arrondit les mesures à 4 décimales : 5e-5 est la tolérance juste.
    parser.add_argument("--tolerance", type=float, default=5e-5)
    parser.add_argument("--keep", action="store_true", help="conserve le harnais temporaire")
    args = parser.parse_args(argv)

    if not os.path.exists(args.index):
        print(f"index absent : {args.index}", file=sys.stderr)
        return 2

    entries = collect(args.index, args.catalog, args.decks)
    if not entries:
        print("aucun deck collecté", file=sys.stderr)
        return 2

    workdir = tempfile.mkdtemp(prefix="ultima-parity-")
    engine = os.path.join(REPO_ROOT, "Ultima.Engine", "Ultima.Engine.csproj")
    with open(os.path.join(workdir, "parity.csproj"), "w", encoding="utf-8") as handle:
        handle.write(CSPROJ.format(engine=engine))
    with open(os.path.join(workdir, "Program.cs"), "w", encoding="utf-8") as handle:
        handle.write(PROGRAM)
    decks_path = os.path.join(workdir, "decks.json")
    cs_path = os.path.join(workdir, "cs.json")
    with open(decks_path, "w", encoding="utf-8") as handle:
        json.dump(entries, handle, ensure_ascii=False)

    env = dict(os.environ)
    env["PATH"] = os.path.expanduser("~/.dotnet") + os.pathsep + env.get("PATH", "")
    build = subprocess.run(["dotnet", "run", "--project", workdir, "-c", "Release", "--",
                            decks_path, cs_path],
                           capture_output=True, text=True, env=env)
    if build.returncode != 0:
        print(build.stdout[-4000:])
        print(build.stderr[-4000:], file=sys.stderr)
        print("échec du harnais C#", file=sys.stderr)
        return 1

    catalogs = Catalogs(args.catalog)
    with open(cs_path, "r", encoding="utf-8") as handle:
        computed = {row["id"]: row for row in map(json.loads, handle)}

    compared = 0
    mismatches = []
    for entry in entries:
        expected = computed.get(entry["id"])
        if expected is None:
            mismatches.append(f"{entry['id']} : absent du harnais C#")
            continue
        result = dimensions(entry["deck"], entry["relics"], entry["hp"], entry["maxHp"],
                            entry["actIndex"] + 1, entry["asc"], catalogs)
        for key in ("survie", "offense", "consistance", "couverture"):
            compared += 1
            if expected[key] != result[key]:
                mismatches.append(f"{entry['id']} {key} : C# {expected[key]} ≠ py {result[key]}")
        measures = result["measures"]
        for cs_key, py_key in MEASURES:
            compared += 1
            left = float(expected["measures"].get(cs_key, 0.0))
            right = float(measures.get(py_key, 0.0))
            if abs(left - right) > args.tolerance:
                mismatches.append(f"{entry['id']} {cs_key} : C# {left} ≠ py {right}")

    print(f"decks comparés : {len(entries)}")
    print(f"champs comparés: {compared}")
    print(f"écarts         : {len(mismatches)}")
    for line in mismatches[:20]:
        print("  " + line)
    if not args.keep:
        shutil.rmtree(workdir, ignore_errors=True)
    return 0 if not mismatches else 1


if __name__ == "__main__":
    sys.exit(main())
