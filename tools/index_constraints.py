#!/usr/bin/env python3
"""Extrait les CONTRAINTES D'ENNEMIS du code source et publie `data/catalog/constraints.json`.

Une contrainte est une mécanique qui **annule une partie du calcul de dégâts** ou **allonge
le combat** : plafond de dégâts subis (Intangible = 1, HardToKill = Amount) et respawns.
C'est ce qui rend une stratégie « gros burst » inopérante : le plafond est une propriété
de la créature, donc du monstre, et se propage à la rencontre.

Entrées :
  - sources du jeu en LECTURE SEULE (ssh vers `fedora`, cf. docs/32 §4) ; seuls les
    fichiers utiles sont lus :
      * `Models/Powers/*.cs`   qui surchargent `ModifyDamageCap` (sémantique du plafond) ;
      * `Models/Monsters/**/*.cs` qui appliquent un pouvoir (`PowerCmd.Apply<…>`) ou
        déclarent `ShouldDisappearFromDoom => Respawns >= N` (nombre de respawns) ;
  - `data/catalog/monsters.json`   (classe décompilée → id codex) ;
  - `data/catalog/encounters.json` (rencontre → monstres).

Sortie (COMMITÉE, < 100 Ko) `data/catalog/constraints.json` :
  - `monsters`   : par monstre  `{damage_cap, hard_to_kill, respawns, powers[], cap_scope,
                    cap_power, source, sources{}}` ;
  - `encounters` : par rencontre, agrégé (plafond le plus contraignant, respawn maximal),
                    avec `cap_monsters` — un plafond ne concerne QUE la créature qui le porte.

Règles :
  - **Rien n'est inventé.** Chaque valeur porte sa référence `fichier:ligne` du code du jeu.
  - Extraction automatique quand la source est joignable ; sinon repli sur `MANUAL`
    ci-dessous, qui contient les mêmes faits avec les mêmes références. Le champ
    `extraction` dit, **fait par fait**, lequel des deux a servi (`auto`, `auto+manual`
    quand la valeur vient d'une chaîne de pouvoirs lue à la main, `manual` hors ligne).
  - Quand la source est joignable, l'extraction est **croisée** avec `MANUAL` : toute
    divergence est signalée sur stderr (`--strict` fait échouer le script). La table
    manuelle sert donc aussi de garde-fou contre une extraction qui dériverait.
  - Aucune rotation de moves n'est reconstruite : `cap_scope` dit seulement *quand* le
    plafond s'applique (`always` dès l'entrée en salle, `periodic` sur un move de la
    rotation, `late_phase` après un respawn).

Usage :
  python3 -I tools/index_constraints.py                 # ssh auto, repli manuel si injoignable
  python3 -I tools/index_constraints.py --offline       # aucune source : table manuelle seule
  python3 -I tools/index_constraints.py --source-dir game/pck/src/Core
  python3 -I tools/index_constraints.py --strict        # échoue si extraction ≠ table manuelle
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shlex
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

DEFAULT_SSH_HOST = os.environ.get("ULTIMA_SRC_SSH", "mio@100.123.83.40")
DEFAULT_REMOTE_CORE = os.environ.get("ULTIMA_SRC_CORE", "~/sts2/game/pck/src/Core")

# Plafond sous lequel un burst est considéré inopérant (25 % du coup attendu) — même seuil
# que `ThreatRules.BurstIneffectiveCapRatio` côté moteur.
BURST_RATIO = 0.25

# --------------------------------------------------------------------------------------
# Table manuelle : repli hors ligne ET garde-fou de l'extraction.
#
# Chaque fait porte le fichier et la ligne lus dans `~/sts2/game/pck/src/Core` (fedora).
# Elle ne contient QUE des monstres réellement rencontrés (les mocks sont exclus).
# --------------------------------------------------------------------------------------
MANUAL: dict[str, dict] = {
    "SoulFysh": {
        # FadeMove : PowerCmd.Apply<IntangiblePower>(…, 2m, …) — 2 tours d'Intangible,
        # un move sur cinq (BECKON → DE_GAS → GAZE → FADE → SCREAM).
        "damage_cap": 1,
        "cap_scope": "periodic",
        "cap_power": "INTANGIBLE",
        "respawns": 0,
        "powers": [
            {"id": "INTANGIBLE", "power": "IntangiblePower", "amount": 2,
             "scope": "move", "move": "FADE",
             "source": "Models/Monsters/SoulFysh.cs:203"},
        ],
        "sources": {
            "damage_cap": "Models/Powers/IntangiblePower.cs:46-52",
            "powers": "Models/Monsters/SoulFysh.cs:203",
            "respawns": "Models/Monsters/SoulFysh.cs (aucun Respawns)",
        },
    },
    "Exoskeleton": {
        # AfterAddedToRoom : HardToKill 9 appliqué à l'entrée en salle (permanent).
        "hard_to_kill": 9,
        "cap_scope": "always",
        "cap_power": "HARD_TO_KILL",
        "respawns": 0,
        "powers": [
            {"id": "HARD_TO_KILL", "power": "HardToKillPower", "amount": 9,
             "scope": "room", "move": None,
             "source": "Models/Monsters/Exoskeleton.cs:49"},
        ],
        "sources": {
            "hard_to_kill": "Models/Monsters/Exoskeleton.cs:49; Models/Powers/HardToKillPower.cs:14-21",
            "powers": "Models/Monsters/Exoskeleton.cs:49",
            "respawns": "Models/Monsters/Exoskeleton.cs (aucun Respawns)",
        },
    },
    "TestSubject": {
        # RespawnMove : Respawns++ puis 2e forme (PainfulStabs), 2e respawn → 3e forme (Nemesis).
        # `ShouldDisappearFromDoom => Respawns >= 2` : il meurt donc 3 fois, respawn 2 fois.
        # NemesisPower bascule Intangible 1 un tour sur deux (AfterSideTurnEnd).
        "damage_cap": 1,
        "cap_scope": "late_phase",
        "cap_power": "NEMESIS>INTANGIBLE",
        "respawns": 2,
        "powers": [
            # Seuls les pouvoirs porteurs de contrainte (Nemesis → Intangible 1).
            # PainfulStabs (2e forme, :272) n'est pas une contrainte et n'est donc pas listé.
            {"id": "NEMESIS", "power": "NemesisPower", "amount": 1,
             "scope": "move", "move": "RESPAWN",
             "source": "Models/Monsters/TestSubject.cs:274"},
        ],
        "sources": {
            "respawns": "Models/Monsters/TestSubject.cs:164 (Respawns >= 2), :254-256 (Respawns++)",
            "damage_cap": "Models/Powers/NemesisPower.cs:20-33; Models/Monsters/TestSubject.cs:274",
            "powers": "Models/Monsters/TestSubject.cs:266-276",
        },
    },
}

# Chaînes de pouvoirs : un pouvoir qui accorde un autre pouvoir plafonneur.
# Vérifié dans le source, non déductible du catalogue codex (qui ne liste que les pouvoirs
# appliqués directement par les moves).
CHAINS: dict[str, dict] = {
    "NEMESIS": {
        "grants": "INTANGIBLE",
        "amount": 1,
        "cap_scope": "late_phase",
        "source": "Models/Powers/NemesisPower.cs:20-33",
        "note": "bascule Intangible 1 (Amount = 1) un tour sur deux sur son porteur",
    },
}


# --------------------------------------------------------------------------------------
# Utilitaires
# --------------------------------------------------------------------------------------
def snake(power_class: str) -> str:
    """`HardToKillPower` → `HARD_TO_KILL` (forme des `power_id` du catalogue)."""
    name = re.sub(r"Power$", "", power_class or "")
    name = re.sub(r"(?<!^)(?=[A-Z])", "_", name)
    return name.upper()


def codex_key(monster: dict) -> str:
    return str(monster.get("id") or monster.get("class") or "").upper()


def load_json(path: str):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: str, document) -> int:
    text = json.dumps(document, indent=1, ensure_ascii=False) + "\n"
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)
    return len(text.encode("utf-8"))


# --------------------------------------------------------------------------------------
# Accès source (lecture seule)
# --------------------------------------------------------------------------------------
def ssh_run(host: str, command: str, timeout: int = 90) -> str:
    proc = subprocess.run(
        ["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host, command],
        capture_output=True, text=True, timeout=timeout)
    if proc.returncode != 0:
        raise RuntimeError((proc.stderr or "").strip() or f"ssh exit {proc.returncode}")
    return proc.stdout


def cd_clause(core: str) -> str:
    if core.startswith("~"):
        return 'cd "$HOME"' + core[1:]
    return "cd " + shlex.quote(core)


def list_source_files(host: str, core: str, pattern: str, directory: str) -> list[str]:
    command = (f'{cd_clause(core)} && grep -rlE {shlex.quote(pattern)} '
               f'{directory} --include="*.cs" | sort')
    return [line.strip() for line in ssh_run(host, command).splitlines() if line.strip()]


def dump_files(host: str, core: str, files: list[str]) -> dict[str, str]:
    """Un seul aller-retour : contenu de chaque fichier, séparé par `@@FILE@@ <chemin>`."""
    if not files:
        return {}
    quoted = " ".join(shlex.quote(f) for f in files)
    command = f'{cd_clause(core)} && for f in {quoted}; do echo "@@FILE@@ $f"; cat "$f"; done'
    out = ssh_run(host, command)

    chunks: dict[str, str] = {}
    current: str | None = None
    buffer: list[str] = []
    for line in out.splitlines():
        if line.startswith("@@FILE@@ "):
            if current is not None:
                chunks[current] = "\n".join(buffer)
            current = line[len("@@FILE@@ "):].strip()
            buffer = []
        else:
            buffer.append(line)
    if current is not None:
        chunks[current] = "\n".join(buffer)
    return chunks


def read_local_files(core: str, files: list[str]) -> dict[str, str]:
    out = {}
    for rel in files:
        with open(os.path.join(core, rel), encoding="utf-8") as handle:
            out[rel] = handle.read()
    return out


# --------------------------------------------------------------------------------------
# Extraction
# --------------------------------------------------------------------------------------
_METHOD = re.compile(r"^\s*(?:public|private|protected|internal)[^;{=]*?\b(\w+)\s*\([^;]*\)\s*$")
_APPLY = re.compile(r"PowerCmd\.Apply<(\w+)>\(([^;]*)\)")
_NUMBER_ARG = re.compile(r"\b(\d+(?:\.\d+)?)m\b")
_MOVE_STATE = re.compile(r'new MoveState\("([A-Z0-9_]+)",\s*(\w+)')
_RESPAWN = re.compile(r"ShouldDisappearFromDoom\s*=>\s*Respawns\s*>=\s*(\d+)")
_CLASS = re.compile(r"\bclass\s+(\w+)\s*:\s*MonsterModel")
_CAP_METHOD = re.compile(r"override\s+decimal\s+ModifyDamageCap")


def parse_cap_power(path: str, text: str) -> dict | None:
    """Sémantique d'un pouvoir plafonneur : fixe (`return 1m`) ou `base.Amount`."""
    lines = text.splitlines()
    start = next((i for i, line in enumerate(lines) if _CAP_METHOD.search(line)), None)
    if start is None:
        return None

    depth = 0
    body: list[str] = []
    opened = False
    for line in lines[start:]:
        depth += line.count("{") - line.count("}")
        body.append(line)
        if "{" in line:
            opened = True
        if opened and depth <= 0:
            break

    joins = "\n".join(body)
    base_line = start + 1
    amount = re.search(r"return\s+base\.Amount\s*;", joins)
    if amount:
        return {"kind": "amount", "value": None,
                "source": f"{path}:{base_line}-{base_line + len(body) - 1}"}

    literal = re.search(r"return\s+(\d+(?:\.\d+)?)m\s*;", joins)
    if literal:
        value = float(literal.group(1))
        return {"kind": "fixed", "value": int(value) if value.is_integer() else value,
                "source": f"{path}:{base_line}-{base_line + len(body) - 1}"}

    if re.search(r"return\s+decimal\.MaxValue\s*;", joins):
        return {"kind": "none", "value": None,
                "source": f"{path}:{base_line}-{base_line + len(body) - 1}"}
    return None


def parse_monster(path: str, text: str, cap_powers: dict[str, dict]) -> dict | None:
    """Applique-t-il un pouvoir, et combien de respawns ? Aucune valeur par défaut inventée."""
    class_match = _CLASS.search(text)
    if class_match is None:
        return None

    lines = text.splitlines()
    move_by_method = {m.group(2): re.sub(r"_MOVE$", "", m.group(1))
                      for m in _MOVE_STATE.finditer(text)}

    powers = []
    method = None
    for index, line in enumerate(lines, start=1):
        declaration = _METHOD.match(line)
        if declaration:
            method = declaration.group(1)
            continue

        for apply in _APPLY.finditer(line):
            power_class = apply.group(1)
            args = apply.group(2)
            # Pouvoirs que le monstre s'applique À LUI-MÊME : un pouvoir posé sur le joueur
            # (Vulnerable…) ne contraint pas les dégâts du monstre.
            if "base.Creature" not in args:
                continue
            # Seuls les pouvoirs qui portent une contrainte sont catalogueés (plafonneur ou
            # chaîne vers un plafonneur) : un buff de Force n'est pas une contrainte.
            if snake(power_class) not in cap_powers and snake(power_class) not in CHAINS:
                continue
            number = _NUMBER_ARG.search(args)
            amount = None
            if number:
                value = float(number.group(1))
                amount = int(value) if value.is_integer() else value
            scope = "room" if method == "AfterAddedToRoom" else "move"
            move = None
            if scope == "move" and method:
                move = move_by_method.get(method)
                if move is None:
                    move = re.sub(r"Move$", "", method).upper()
            powers.append({
                "id": snake(power_class),
                "power": power_class,
                "amount": amount,
                "scope": scope,
                "move": move,
                "source": f"{path}:{index}",
            })

    respawns = None
    respawn_line = None
    for index, line in enumerate(lines, start=1):
        found = _RESPAWN.search(line)
        if found:
            respawns = int(found.group(1))
            respawn_line = index
            break

    return {
        "class": class_match.group(1),
        "respawns": respawns,
        "respawn_line": respawn_line,
        "powers": powers,
        "file": path,
    }


def caps_for(monster: dict, cap_powers: dict[str, dict]) -> dict:
    """Traduit les pouvoirs portés par un monstre en contraintes de plafond."""
    damage_cap: int | float | None = None
    hard_to_kill: int | float | None = None
    cap_scope = None
    cap_power = None
    best: int | float | None = None
    via_chain = False
    sources = []

    for power in monster["powers"]:
        pid = power["id"]
        entry = None
        is_chain = False
        if pid in cap_powers and cap_powers[pid]["kind"] != "none":
            semantics = cap_powers[pid]
            value = semantics["value"] if semantics["kind"] == "fixed" else power["amount"]
            if value is None:
                continue
            field = "damage_cap" if semantics["kind"] == "fixed" else "hard_to_kill"
            scope = "always" if power["scope"] == "room" else "periodic"
            entry = {"field": field, "value": value, "scope": scope, "power": pid,
                     "source": f"{power['source']}; {semantics['source']}"}
        elif pid in CHAINS:
            chain = CHAINS[pid]
            semantics = cap_powers.get(chain["grants"])
            if semantics is None or semantics["kind"] == "none":
                continue
            value = semantics["value"] if semantics["kind"] == "fixed" else chain["amount"]
            if value is None:
                continue
            field = "damage_cap" if semantics["kind"] == "fixed" else "hard_to_kill"
            is_chain = True
            entry = {"field": field, "value": value, "scope": chain["cap_scope"],
                     "power": f"{pid}>{chain['grants']}",
                     "source": f"{power['source']}; {chain['source']}"}
        if entry is None:
            continue

        if entry["field"] == "damage_cap":
            damage_cap = entry["value"] if damage_cap is None else min(damage_cap, entry["value"])
        else:
            hard_to_kill = (entry["value"] if hard_to_kill is None
                            else min(hard_to_kill, entry["value"]))
        if best is None or entry["value"] < best:
            best = entry["value"]
            cap_scope = entry["scope"]
            cap_power = entry["power"]
            via_chain = is_chain
            sources = [entry["source"]]
        elif entry["value"] == best and entry["source"] not in sources:
            sources.append(entry["source"])

    return {"damage_cap": damage_cap, "hard_to_kill": hard_to_kill,
            "cap_scope": cap_scope, "cap_power": cap_power,
            "via_chain": via_chain, "cap_sources": sources}


def build_monsters(catalog_monsters: list[dict], extracted: dict[str, dict],
                   cap_powers: dict[str, dict],
                   offline: bool) -> tuple[dict[str, dict], list[str], int, list[str]]:
    by_class = {m.get("class"): m for m in catalog_monsters
                if isinstance(m, dict) and m.get("class")}
    key_of = {cls: codex_key(m) for cls, m in by_class.items()}

    out: dict[str, dict] = {}
    skipped: list[str] = []
    auto_facts = 0
    mismatches: list[str] = []

    for monster in sorted(extracted.values(), key=lambda m: m["class"]):
        cls = monster["class"]
        if "Mocks/" in monster["file"]:
            skipped.append(f"{cls} ({monster['file']} — monstre de test)")
            continue
        if cls not in by_class:
            skipped.append(f"{cls} ({monster['file']} — absent du catalogue codex)")
            continue

        caps = caps_for(monster, cap_powers)
        # Aucune contrainte (ni plafond, ni respawn) : rien à publier pour ce monstre.
        if caps["damage_cap"] is None and caps["hard_to_kill"] is None \
                and not monster["respawns"]:
            continue
        entry: dict = {
            "id": key_of[cls],
            "class": cls,
            "damage_cap": caps["damage_cap"],
            "hard_to_kill": caps["hard_to_kill"],
            "respawns": monster["respawns"] or 0,
            "cap_scope": caps["cap_scope"],
            "cap_power": caps["cap_power"],
            "powers": monster["powers"],
            "extraction": {},
        }

        sources: dict[str, str] = {}
        if caps["damage_cap"] is not None:
            sources["damage_cap"] = "; ".join(caps["cap_sources"]) or ""
        if caps["hard_to_kill"] is not None:
            sources["hard_to_kill"] = "; ".join(caps["cap_sources"]) or ""
        if monster["powers"]:
            sources["powers"] = "; ".join(p["source"] for p in monster["powers"])
        if monster["respawns"]:
            sources["respawns"] = f"{monster['file']}:{monster['respawn_line']}"
        entry["sources"] = sources
        entry["source"] = (sources.get("damage_cap") or sources.get("hard_to_kill")
                           or sources.get("respawns") or "")

        for fact in ("damage_cap", "hard_to_kill", "respawns", "powers"):
            value = entry.get(fact)
            if value in (None, 0, []) and fact != "respawns":
                continue
            if fact == "respawns" and not value:
                continue
            if offline:
                entry["extraction"][fact] = "manual"
            elif fact == "damage_cap" and caps["via_chain"]:
                # Plafond déduit d'un pouvoir qui en accorde un autre (Nemesis → Intangible) :
                # la chaîne elle-même est une lecture manuelle du source, documentée et sourcée.
                entry["extraction"][fact] = "auto+manual"
            else:
                entry["extraction"][fact] = "auto"
            auto_facts += 1

        if not offline and cls in MANUAL:
            mismatches.extend(cross_check(cls, entry, MANUAL[cls]))

        out[entry["id"]] = entry

    # Monstres connus uniquement par la table manuelle (source injoignable, ou monstre
    # dont le fichier ne contient ni Apply ni Respawns).
    for cls, manual in MANUAL.items():
        if cls not in by_class:
            skipped.append(f"{cls} (table manuelle — absent du catalogue codex)")
            continue
        key = key_of[cls]
        if key in out:
            continue
        entry = {
            "id": key,
            "class": cls,
            "damage_cap": manual.get("damage_cap"),
            "hard_to_kill": manual.get("hard_to_kill"),
            "respawns": manual.get("respawns", 0),
            "cap_scope": manual.get("cap_scope"),
            "cap_power": manual.get("cap_power"),
            "powers": manual.get("powers", []),
            "sources": dict(manual.get("sources", {})),
            "source": (manual.get("sources", {}).get("damage_cap")
                       or manual.get("sources", {}).get("hard_to_kill")
                       or manual.get("sources", {}).get("respawns") or ""),
            "extraction": {fact: "manual" for fact in
                           ("damage_cap", "hard_to_kill", "respawns", "powers")
                           if manual.get(fact) not in (None, 0, [], {})},
        }
        if entry["respawns"] == 0:
            entry["extraction"].pop("respawns", None)
        out[key] = entry
        for _ in entry["extraction"]:
            auto_facts += 1

    return out, skipped, auto_facts, mismatches


def cross_check(cls: str, entry: dict, manual: dict) -> list[str]:
    problems = []
    for fact in ("damage_cap", "hard_to_kill", "respawns"):
        expected = manual.get(fact, 0 if fact == "respawns" else None)
        actual = entry.get(fact)
        if expected != actual:
            problems.append(f"{cls}.{fact}: extrait {actual!r} ≠ table {expected!r}")
    manual_powers = sorted((p["id"], p["amount"]) for p in manual.get("powers", []))
    actual_powers = sorted((p["id"], p["amount"]) for p in entry.get("powers", []))
    if manual_powers != actual_powers:
        problems.append(f"{cls}.powers: extrait {actual_powers} ≠ table {manual_powers}")
    return problems


def build_encounters(catalog_encounters: list[dict], monsters: dict[str, dict]) -> dict:
    out: dict[str, dict] = {}
    for encounter in catalog_encounters:
        if not isinstance(encounter, dict) or not encounter.get("id"):
            continue
        ids = [str(m).upper() for m in encounter.get("monsters") or []]
        constrained = [monsters[mid] for mid in ids if mid in monsters]
        capping = [m for m in constrained if m.get("damage_cap") is not None
                   or m.get("hard_to_kill") is not None]
        respawning = [m for m in constrained if m.get("respawns")]
        if not capping and not respawning:
            continue

        def cap_of(m: dict) -> int:
            values = [v for v in (m.get("damage_cap"), m.get("hard_to_kill"))
                      if isinstance(v, (int, float))]
            return int(min(values)) if values else 0

        tightest = min(capping, key=cap_of) if capping else None
        damage_caps = [m["damage_cap"] for m in capping if m.get("damage_cap") is not None]
        htks = [m["hard_to_kill"] for m in capping if m.get("hard_to_kill") is not None]

        out[encounter["id"]] = {
            "id": encounter["id"],
            "damage_cap": min(damage_caps) if damage_caps else None,
            "hard_to_kill": min(htks) if htks else None,
            "respawns": max((m["respawns"] for m in constrained), default=0),
            "cap_scope": tightest.get("cap_scope") if tightest else None,
            "cap_monsters": [m["id"] for m in capping],
            "monsters": ids,
            "powers": sorted({p["id"] for m in constrained for p in m.get("powers", [])}),
            "source": "; ".join(
                f"{m['id']} → {m['sources'].get('damage_cap') or m['sources'].get('hard_to_kill')}"
                for m in capping) or "; ".join(
                f"{m['id']} → {m['sources'].get('respawns', '')}" for m in respawning),
        }
    return out


# --------------------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------------------
def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--catalog", default=os.path.join(ROOT, "data", "catalog"))
    parser.add_argument("--out", default=None,
                        help="défaut : <catalog>/constraints.json")
    parser.add_argument("--source-dir", default=None,
                        help="checkout local de Core (sinon ssh, sinon table manuelle)")
    parser.add_argument("--ssh", default=DEFAULT_SSH_HOST,
                        help=f"hôte ssh des sources (défaut : {DEFAULT_SSH_HOST})")
    parser.add_argument("--core", default=DEFAULT_REMOTE_CORE,
                        help=f"chemin de Core sur l'hôte (défaut : {DEFAULT_REMOTE_CORE})")
    parser.add_argument("--offline", action="store_true",
                        help="ne lit aucune source : table manuelle seule (reproductible sans ssh)")
    parser.add_argument("--strict", action="store_true",
                        help="échoue si l'extraction diverge de la table manuelle")
    parser.add_argument("--max-kb", type=int, default=100,
                        help="taille maximale du catalogue (défaut : 100 Ko)")
    args = parser.parse_args()

    monsters_path = os.path.join(args.catalog, "monsters.json")
    encounters_path = os.path.join(args.catalog, "encounters.json")
    for path in (monsters_path, encounters_path):
        if not os.path.isfile(path):
            print(f"catalogue introuvable : {path}", file=sys.stderr)
            return 2

    catalog_monsters = [m for m in load_json(monsters_path) if isinstance(m, dict)]
    catalog_encounters = load_json(encounters_path).get("encounters") or []

    cap_powers: dict[str, dict] = {}
    extracted: dict[str, dict] = {}
    offline = args.offline
    source_note = "table manuelle (aucune source lue)"

    if not offline:
        try:
            if args.source_dir:
                power_files = [os.path.relpath(p, args.source_dir)
                               for p in _local_match(args.source_dir, "Models/Powers",
                                                     _CAP_METHOD.pattern)]
                monster_files = [os.path.relpath(p, args.source_dir)
                                 for p in _local_match(args.source_dir, "Models/Monsters",
                                                       r"PowerCmd\.Apply<|ShouldDisappearFromDoom")]
                files = read_local_files(args.source_dir, power_files + monster_files)
            else:
                power_files = list_source_files(args.ssh, args.core, _CAP_METHOD.pattern,
                                                "Models/Powers")
                monster_files = list_source_files(args.ssh, args.core,
                                                  r"PowerCmd\.Apply<|ShouldDisappearFromDoom",
                                                  "Models/Monsters")
                files = dump_files(args.ssh, args.core, power_files + monster_files)

            for path, text in files.items():
                if "/Powers/" not in path:
                    continue
                semantics = parse_cap_power(path, text)
                if semantics:
                    cap_powers[snake(os.path.basename(path)[:-3])] = semantics
            for path, text in files.items():
                if "/Powers/" in path:
                    continue
                monster = parse_monster(path, text, cap_powers)
                if monster:
                    extracted[monster["class"]] = monster
            if not cap_powers:
                raise RuntimeError("aucun pouvoir plafonneur trouvé (ModifyDamageCap)")
            source_note = ("ssh " + args.ssh + ":" + args.core) if not args.source_dir \
                else "local " + args.source_dir
        except (RuntimeError, OSError, subprocess.SubprocessError) as error:
            print(f"source injoignable ({error}) → repli sur la table manuelle", file=sys.stderr)
            offline = True
            extracted = {}
            cap_powers = {}

    if offline:
        cap_powers = _manual_cap_powers()

    monsters, skipped, facts, mismatches = build_monsters(
        catalog_monsters, extracted, cap_powers, offline)
    encounters = build_encounters(catalog_encounters, monsters)

    for problem in mismatches:
        print(f"DIVERGENCE  {problem}", file=sys.stderr)

    document = {
        "generated_by": "tools/index_constraints.py",
        "sources": ([source_note] if not offline else []) +
                   ["data/catalog/monsters.json", "data/catalog/encounters.json",
                    "Models/Powers/IntangiblePower.cs",
                    "Models/Powers/HardToKillPower.cs",
                    "Models/Powers/NemesisPower.cs",
                    "Models/Monsters/SoulFysh.cs",
                    "Models/Monsters/Exoskeleton.cs",
                    "Models/Monsters/TestSubject.cs"],
        "note": (
            "contraintes d'ennemis lues dans le code du jeu (docs/32 §4). "
            "damage_cap = plafond de dégâts subis (Intangible = 1) ; hard_to_kill = plafond "
            "= Amount (HardToKill) ; respawns = nombre de fois où le monstre revient. "
            "cap_scope : always (dès l'entrée en salle) | periodic (un move de la rotation) | "
            "late_phase (après un respawn). Un plafond ne concerne que la créature qui le "
            "porte : les rencontres exposent cap_monsters. Toute entrée absente = aucune "
            "contrainte connue (jamais une valeur par défaut)."
        ),
        "burst_ineffective_ratio": BURST_RATIO,
        "extraction": {
            "mode": "manual" if offline else "auto",
            "source": source_note,
            "facts": facts,
            "manual_cross_check": {"mismatches": len(mismatches)},
            "skipped": skipped,
        },
        "counts": {"monsters": len(monsters), "encounters": len(encounters)},
        "monsters": monsters,
        "encounters": encounters,
    }

    out_path = args.out or os.path.join(args.catalog, "constraints.json")
    size = write_json(out_path, document)

    print(f"monstres contraints : {len(monsters)} "
          f"({', '.join(sorted(monsters)) or 'aucun'})")
    print(f"rencontres contraintes : {len(encounters)} "
          f"({', '.join(sorted(encounters)) or 'aucune'})")
    print(f"mode : {'manuel' if offline else 'auto'} — faits : {facts} — divergences : {len(mismatches)}")
    for item in skipped:
        print(f"  ignoré : {item}", file=sys.stderr)
    print(f"écrit {out_path} ({size / 1024:.1f} Ko)")

    if size > args.max_kb * 1024:
        print(f"catalogue trop gros : {size / 1024:.1f} Ko > {args.max_kb} Ko", file=sys.stderr)
        return 4
    if args.strict and mismatches:
        return 3
    return 0


def _local_match(core: str, directory: str, pattern: str) -> list[str]:
    import pathlib

    regex = re.compile(pattern)
    root = pathlib.Path(core) / directory
    hits = []
    for path in sorted(root.rglob("*.cs")):
        text = path.read_text(encoding="utf-8", errors="replace")
        if regex.search(text):
            hits.append(str(path))
    return hits


def _manual_cap_powers() -> dict[str, dict]:
    """Sémantique des deux pouvoirs plafonneurs, telle que lue dans le source (repli)."""
    return {
        "INTANGIBLE": {"kind": "fixed", "value": 1,
                       "source": "Models/Powers/IntangiblePower.cs:46-52"},
        "HARD_TO_KILL": {"kind": "amount", "value": None,
                         "source": "Models/Powers/HardToKillPower.cs:14-21"},
    }


if __name__ == "__main__":
    raise SystemExit(main())
