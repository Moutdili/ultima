#!/usr/bin/env python3
"""Indexe le source C# de Slay the Spire 2 (extrait du .pck) en catalogues JSON.

Zéro dépendance (python3 >= 3.9, stdlib seule). Lecture seule sur le source du jeu.

Régénération (commande exacte, depuis la racine du dépôt — c'est ce qu'appelle le Makefile) :
    python3 tools/index_source.py --src game/pck/src/Core --out data/catalog --repo .
Depuis un worktree dont game/ n'est pas extrait, pointer --src vers une copie extraite, ex. :
    python3 tools/index_source.py --src ../autre-clone/game/pck/src/Core --out data/catalog --repo .
(aucun chemin utilisateur n'est code en dur : --src est libre, stats.json n'enregistre qu'un
chemin relatif au depot quand le source est sous --repo)

Entrées (sous --src = .../src/Core) :
  - Models/Cards/*.cs      (classes dérivant de CardModel ; Models/Cards/Mocks/ ignoré)
  - Models/Monsters/**/*.cs (classes dérivant de MonsterModel)
  - Models/Powers/*.cs     (pouvoirs exposant ModifyDamage*)

Sorties (sous --out, sauf la doc) :
  - cards.json             toutes les cartes
  - priority_cards.json    TABLEAU des cartes `priority_candidate` OU `first_card_hint`
                           (embarqué dans Ultima.dll : schéma append-only, ne retirer aucune clé)
  - monsters.json          monstres, HP, moves (intents + valeurs résolues)
  - intents_by_monster.json { "<MonsterClass>": [ {move, intent, damage, repeats, args} ] }
                           moves offensifs (SingleAttackIntent / MultiAttackIntent) uniquement,
                           valeurs EXTRAITES de monsters.json (aucun recalcul) ; listes telles que
                           dans monsters.json (ordre [ascension, normal] si scalé), null si non résolu ;
                           repeats = [1] pour SingleAttackIntent (`override int Repeats => 1`).
  - powers_damage.json     pouvoirs modifiant les dégâts (index historique ; le catalogue
                           complet des 260 pouvoirs est data/catalog/powers.json, produit
                           par tools/index_powers.py)
  - stats.json             compteurs
  - <--repo>/docs/11-api-catalog.md (résumé lisible)

Champs carte (cards.json et priority_cards.json) :
  class, file, base, cost, type, rarity, target, vars, upgrade_delta, tags,
  glow_gold, glow_red, turn_conditional, priority_candidate,
  first_card_hint (ajout M3) : true si la carte `override bool ShouldGlowGoldInternal`
                   OU lit `CardPlaysFinished` (pattern de Models/Cards/Fetch.cs) ;
  signals (ajout M3) : sous-liste de ["ShouldGlowGoldInternal", "ShouldGlowRedInternal",
                   "CardPlaysFinished"] — overrides de glow / lecture d'historique détectés.
Champ move (monsters.json), ajout M3 : `extra_intents` (liste {intent, args, values}) quand un
  MoveState déclare plusieurs intents (`params AbstractIntent[]`) ; `intent/args/values` décrivent
  toujours le premier.

Particularités du code du jeu traitées ici :
  - les HP et dégâts passent souvent par
      AscensionHelper.GetValueIfAscension(AscensionLevel.X, <valeurAscension>, <valeurNormale>)
  - les intents référencent des propriétés (`new SingleAttackIntent(KillshotDamage)`)
  - `new MoveState(id, onPerform, params AbstractIntent[] intents)` : parenthèses équilibrées
  - le bonus conditionnel d'une carte se signale via `ShouldGlowGoldInternal`
  - « déjà jouée ce tour » via `CombatHistory.CardPlaysFinished` + `HappenedThisTurn`

Usage:
    python3 tools/index_source.py --src game/pck/src/Core --out data/catalog --repo .
"""
from __future__ import annotations

import argparse
import json
import os
import re
from collections import Counter
from pathlib import Path

CLASS_RE = re.compile(
    r"public\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)\s*:\s*([A-Za-z0-9_.,<>\[\]]+)"
)
CTOR_RE = re.compile(r":\s*base\(([^)]*)\)", re.S)
DAMAGE_VAR_RE = re.compile(r"new\s+(DamageVar|BlockVar)\s*\(\s*([0-9.]+)m?\s*,\s*([A-Za-z0-9_.|]+)\s*\)")
DYN_VAR_RE = re.compile(r'new\s+DynamicVar\s*\(\s*"(\w+)"\s*,\s*([0-9.]+)m?\s*\)')
UPGRADE_RE = re.compile(r"UpgradeValueBy\(\s*(-?[0-9.]+)m?\s*\)")
NUM_RE = re.compile(r"-?\d+")
PROP_INT_RE = re.compile(
    r"(?:private|public|protected|internal)?\s*(?:static\s+)?(?:override\s+)?int\s+(\w+)\s*=>\s*([^;]+);"
)
MOVE_START_RE = re.compile(r"new\s+MoveState\s*\(")
INTENT_EXPR_RE = re.compile(r"new\s+(\w+)\s*\(", re.S)
ATTACK_INTENTS = ("SingleAttackIntent", "MultiAttackIntent")
HP_RE = re.compile(r"override\s+int\s+(MinInitialHp|MaxInitialHp)\s*=>\s*([^;]+);")
TAG_RE = re.compile(r"CardTag\.(\w+)")
MOD_RE = re.compile(r"ModifyDamage(Additive|Multiplicative|Cap)")
RET_DEC_RE = re.compile(r"return\s+(-?[0-9.]+)m?\s*;")
GLOW_GOLD_RE = re.compile(r"ShouldGlowGoldInternal")
GLOW_RED_RE = re.compile(r"ShouldGlowRedInternal")
GLOW_GOLD_OVERRIDE_RE = re.compile(r"override\s+bool\s+ShouldGlowGoldInternal\b")
GLOW_RED_OVERRIDE_RE = re.compile(r"override\s+bool\s+ShouldGlowRedInternal\b")
CARD_PLAYS_FINISHED_RE = re.compile(r"\bCardPlaysFinished\b")
HISTORY_PAYOFF_RE = re.compile(
    r"CardPlaysFinished|CardPlaysStarted|HappenedThisTurn|HasBeenPlayedThisTurn|"
    r"AnyCardsPlayedThisTurn|CardsPlayedThisTurn"
)


def read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def squash(text: str) -> str:
    return re.sub(r"\s+", " ", text)


def numbers(expr: str) -> list[int]:
    return [int(x) for x in NUM_RE.findall(expr)]


def matching_paren(text: str, open_i: int) -> int:
    """Index de la parenthèse fermante associée à text[open_i] == '(' (ignore les chaînes)."""
    depth, i, in_str = 0, open_i, False
    while i < len(text):
        ch = text[i]
        if in_str:
            if ch == "\\":
                i += 2
                continue
            if ch == '"':
                in_str = False
        elif ch == '"':
            in_str = True
        elif ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def split_top(blob: str) -> list[str]:
    """Découpe une liste d'arguments C# sur les virgules de niveau 0."""
    parts, cur, depth, in_str = [], [], 0, False
    for ch in blob:
        if in_str:
            cur.append(ch)
            if ch == '"':
                in_str = False
            continue
        if ch == '"':
            in_str = True
        elif ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append(squash("".join(cur)).strip())
            cur = []
        else:
            cur.append(ch)
    tail = squash("".join(cur)).strip()
    if tail:
        parts.append(tail)
    return parts


def parse_moves(text: str, symbols: dict[str, list[int]]) -> list[dict]:
    """`new MoveState("ID", Method, new XIntent(...), new YIntent(...))` -> moves."""
    moves = []
    for m in MOVE_START_RE.finditer(text):
        close_i = matching_paren(text, m.end() - 1)
        if close_i < 0:
            continue
        parts = split_top(text[m.end():close_i])
        if len(parts) < 3:
            continue
        sm = re.fullmatch(r'"([^"]+)"', parts[0])
        if not sm or not re.fullmatch(r"\w+", parts[1]):
            continue
        intents = []
        for part in parts[2:]:
            im = INTENT_EXPR_RE.match(part)
            if not im:
                continue
            open_i = im.end() - 1
            end_i = matching_paren(part, open_i)
            if end_i < 0:
                continue
            raw_args = split_top(part[open_i + 1:end_i])
            intents.append(
                {
                    "intent": im.group(1),
                    "args": raw_args,
                    "values": [resolve(a, symbols) for a in raw_args],
                }
            )
        if not intents:
            continue
        move = {"state": sm.group(1), "method": parts[1], **intents[0]}
        if len(intents) > 1:
            move["extra_intents"] = intents[1:]
        moves.append(move)
    return moves


def move_intents(mv: dict) -> list[dict]:
    """Tous les intents d'un move (le premier + extra_intents)."""
    first = {"intent": mv["intent"], "args": mv["args"], "values": mv["values"]}
    return [first, *mv.get("extra_intents", [])]


def resolve(arg: str, symbols: dict[str, list[int]]) -> list[int] | None:
    """Résout un argument d'intent : littéral, ou propriété du même fichier."""
    arg = arg.strip()
    if re.fullmatch(r"-?\d+", arg):
        return [int(arg)]
    m = re.fullmatch(r"(\w+)", arg)
    if m and m.group(1) in symbols:
        return symbols[m.group(1)]
    # expression inline (ex: AscensionHelper.GetValueIfAscension(..., 11, 10))
    nums = numbers(arg)
    return nums or None


def parse_cards(root: Path) -> list[dict]:
    out = []
    for path in sorted((root / "Models" / "Cards").glob("*.cs")):
        text = read(path)
        m = CLASS_RE.search(text)
        if not m or "CardModel" not in m.group(2):
            continue
        cls, base = m.group(1), m.group(2)
        card: dict = {"class": cls, "file": f"Models/Cards/{path.name}", "base": base}

        ctor = CTOR_RE.search(squash(text))
        if ctor:
            args = [a.strip() for a in ctor.group(1).split(",")]
            if len(args) >= 4:
                if re.fullmatch(r"\d+", args[0]):
                    card["cost"] = int(args[0])
                for arg in args[1:]:
                    if arg.startswith("CardType."):
                        card["type"] = arg.split(".", 1)[1]
                    elif arg.startswith("CardRarity."):
                        card["rarity"] = arg.split(".", 1)[1]
                    elif arg.startswith("TargetType."):
                        card["target"] = arg.split(".", 1)[1]

        vars_: dict[str, float] = {}
        for name, value, _props in DAMAGE_VAR_RE.findall(text):
            vars_[name.lower()] = float(value)
        for name, value in DYN_VAR_RE.findall(text):
            vars_.setdefault(name.lower(), float(value))
        if vars_:
            card["vars"] = vars_

        upgrades = [float(x) for x in UPGRADE_RE.findall(text)]
        if upgrades:
            card["upgrade_delta"] = upgrades

        tags = sorted(set(TAG_RE.findall(text)))
        if tags:
            card["tags"] = tags

        # signaux de payoff conditionnel
        card["glow_gold"] = bool(GLOW_GOLD_RE.search(text))
        card["glow_red"] = bool(GLOW_RED_RE.search(text))
        card["turn_conditional"] = bool(HISTORY_PAYOFF_RE.search(text))
        card["priority_candidate"] = card["glow_gold"] or card["turn_conditional"]
        signals = []
        if GLOW_GOLD_OVERRIDE_RE.search(text):
            signals.append("ShouldGlowGoldInternal")
        if GLOW_RED_OVERRIDE_RE.search(text):
            signals.append("ShouldGlowRedInternal")
        if CARD_PLAYS_FINISHED_RE.search(text):
            signals.append("CardPlaysFinished")
        card["first_card_hint"] = (
            "ShouldGlowGoldInternal" in signals or "CardPlaysFinished" in signals
        )
        card["signals"] = signals
        out.append(card)
    return out


def parse_monsters(root: Path) -> list[dict]:
    out = []
    for path in sorted((root / "Models" / "Monsters").rglob("*.cs")):
        text = read(path)
        m = CLASS_RE.search(text)
        if not m or "MonsterModel" not in m.group(2):
            continue
        mon: dict = {"class": m.group(1), "file": str(path.relative_to(root))}

        symbols: dict[str, list[int]] = {}
        for name, expr in PROP_INT_RE.findall(text):
            vals = numbers(expr)
            if vals:
                symbols[name] = vals

        for field, expr in HP_RE.findall(text):
            key = "min_hp" if field == "MinInitialHp" else "max_hp"
            mon[key] = {"expr": expr.strip(), "values": numbers(expr)}
        if "min_hp" in mon:
            mon["ascension_scaled"] = "AscensionHelper" in mon["min_hp"]["expr"]

        moves = parse_moves(text, symbols)
        if moves:
            mon["moves"] = moves
        out.append(mon)
    return out


def parse_powers(root: Path) -> list[dict]:
    out = []
    for path in sorted((root / "Models" / "Powers").glob("*.cs")):
        text = read(path)
        m = CLASS_RE.search(text)
        if not m:
            continue
        hooks = MOD_RE.findall(text)
        if not hooks:
            continue
        out.append(
            {
                "class": m.group(1),
                "file": f"Models/Powers/{path.name}",
                "hooks": sorted(set(hooks)),
                "literals": sorted({float(x) for x in RET_DEC_RE.findall(text)}),
            }
        )
    return out


def attack_summary(mon: dict) -> list[str]:
    """Résume les moves offensifs : STATE=intent(valeurs)."""
    out = []
    for mv in mon.get("moves", []):
        for it in move_intents(mv):
            if "Attack" not in it["intent"]:
                continue
            vals = []
            for v in it["values"]:
                vals.append("/".join(str(x) for x in v) if v else "?")
            out.append(f"{mv['state']}={it['intent']}({','.join(it['args'])})→[{','.join(vals)}]")
    return out


def intents_by_monster(monsters: list[dict]) -> dict[str, list[dict]]:
    """Moves offensifs par monstre, valeurs extraites telles quelles de monsters.json."""
    out: dict[str, list[dict]] = {}
    for mon in monsters:
        rows = []
        for mv in mon.get("moves", []):
            for it in move_intents(mv):
                if it["intent"] not in ATTACK_INTENTS:
                    continue
                vals = it["values"]
                damage = vals[0] if vals else None
                if it["intent"] == "SingleAttackIntent":
                    repeats = [1]  # SingleAttackIntent: `public override int Repeats => 1;`
                else:
                    repeats = vals[1] if len(vals) > 1 else None
                rows.append(
                    {
                        "move": mv["state"],
                        "intent": it["intent"],
                        "damage": damage,
                        "repeats": repeats,
                        "args": it["args"],
                    }
                )
        if rows:
            out[mon["class"]] = rows
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default="game/pck/src/Core")
    ap.add_argument("--out", default="data/catalog")
    ap.add_argument("--repo", default=".")
    args = ap.parse_args()

    src = Path(args.src).resolve()
    if not src.is_dir():
        raise SystemExit(f"source introuvable: {src}")
    core = src  # .../src/Core

    outdir = Path(args.out).resolve()
    outdir.mkdir(parents=True, exist_ok=True)

    # Chemin PORTABLE dans stats.json : jamais /home/<user>. Relatif au depot quand c'est
    # possible, sinon relatif avec ../ (le champ reste lisible depuis n'importe quel clone).
    repo_root = Path(args.repo).resolve()
    try:
        source_root = str(src.relative_to(repo_root))
    except ValueError:
        source_root = os.path.relpath(src, repo_root)

    cards = parse_cards(core)
    monsters = parse_monsters(core)
    powers = parse_powers(core)
    priority = [c for c in cards if c.get("priority_candidate") or c.get("first_card_hint")]
    intents = intents_by_monster(monsters)
    offensive = [row for rows in intents.values() for row in rows]

    stats = {
        "source_root": source_root,
        "source_root_note": "relatif a la racine du depot (jeu decompile, non commite)",
        "cards": len(cards),
        "cards_by_type": dict(Counter(c.get("type", "?") for c in cards)),
        "cards_by_rarity": dict(Counter(c.get("rarity", "?") for c in cards)),
        "cards_priority_candidates": len(priority),
        "cards_first_card_hint": sum(1 for c in cards if c.get("first_card_hint")),
        "priority_added_by_first_card_hint": sum(
            1 for c in priority if not c.get("priority_candidate")
        ),
        "monsters": len(monsters),
        "monsters_with_hp": sum(1 for m in monsters if "min_hp" in m),
        "monsters_ascension_scaled": sum(1 for m in monsters if m.get("ascension_scaled")),
        "monsters_with_moves": sum(1 for m in monsters if m.get("moves")),
        "monsters_with_attack_moves": sum(1 for m in monsters if attack_summary(m)),
        "powers_modifying_damage": len(powers),
        "intents_by_monster_monsters": len(intents),
        "offensive_intents": len(offensive),
        "offensive_intents_damage_unresolved": sum(1 for r in offensive if r["damage"] is None),
        "offensive_intents_repeats_unresolved": sum(1 for r in offensive if r["repeats"] is None),
    }

    (outdir / "cards.json").write_text(json.dumps(cards, indent=1, ensure_ascii=False))
    (outdir / "monsters.json").write_text(json.dumps(monsters, indent=1, ensure_ascii=False))
    (outdir / "powers_damage.json").write_text(json.dumps(powers, indent=1, ensure_ascii=False))
    (outdir / "priority_cards.json").write_text(json.dumps(priority, indent=1, ensure_ascii=False))
    (outdir / "intents_by_monster.json").write_text(
        json.dumps(intents, indent=1, ensure_ascii=False)
    )
    (outdir / "stats.json").write_text(json.dumps(stats, indent=1, ensure_ascii=False))
    print(json.dumps(stats, indent=2, ensure_ascii=False))

    md = ["# 11 — Catalogue API & données (généré)\n"]
    md.append("> Généré par `tools/index_source.py` depuis le **source original** du jeu.")
    md.append("> Ne pas éditer à la main : relancer l'indexeur après un patch du jeu.\n")
    md.append(f"Source : `{stats['source_root']}` (v0.107.1)\n")
    md.append("## Volumétrie\n")
    md.append(f"- Cartes : **{stats['cards']}** — {stats['cards_by_type']}")
    md.append(f"- Cartes à payoff conditionnel (glow / historique) : **{stats['cards_priority_candidates']}**")
    md.append(f"- Cartes `first_card_hint` (override ShouldGlowGoldInternal ou CardPlaysFinished) : **{stats['cards_first_card_hint']}**")
    md.append(f"- Monstres : **{stats['monsters']}**, dont {stats['monsters_with_attack_moves']} avec moves offensifs")
    md.append(f"- Intents offensifs (`intents_by_monster.json`) : **{stats['offensive_intents']}** sur {stats['intents_by_monster_monsters']} monstres, dont {stats['offensive_intents_damage_unresolved']} à dégâts non résolus")
    md.append(f"- Monstres à HP ascensionnels : {stats['monsters_ascension_scaled']}")
    md.append(f"- Pouvoirs modifiant les dégâts : **{stats['powers_modifying_damage']}**\n")

    md.append("## Multiplicateurs de dégâts (pouvoirs)\n")
    md.append("| Pouvoir | Hooks | Littéraux retournés |")
    md.append("|---|---|---|")
    for p in powers:
        md.append(f"| `{p['class']}` | {', '.join(p['hooks'])} | {p['literals'] or '—'} |")
    md.append("")

    md.append("## Cartes à payoff conditionnel (priorité de tour)\n")
    md.append("| Carte | Type | Coût | Rareté | GlowGold | Historique | 1re carte | Signaux | Fichier |")
    md.append("|---|---|---|---|---|---|---|---|---|")
    for c in priority:
        md.append(
            f"| `{c['class']}` | {c.get('type','?')} | {c.get('cost','?')} | {c.get('rarity','?')} | "
            f"{'✅' if c.get('glow_gold') else '—'} | {'✅' if c.get('turn_conditional') else '—'} | "
            f"{'✅' if c.get('first_card_hint') else '—'} | {', '.join(c.get('signals') or []) or '—'} | `{c['file']}` |"
        )
    md.append("")

    md.append("## Monstres : moves offensifs résolus\n")
    md.append("Les valeurs sont `valeurAscension/valeurNormale` quand le jeu scale par ascension.\n")
    md.append("| Monstre | HP (asc/normal) | Moves offensifs |")
    md.append("|---|---|---|")
    for mon in monsters:
        atk = attack_summary(mon)
        if not atk:
            continue
        hp = mon.get("min_hp", {}).get("values")
        hps = "/".join(str(v) for v in hp) if hp else "?"
        md.append(f"| `{mon['class']}` | {hps} | {'; '.join(atk[:4])} |")
    md.append("")

    (Path(args.repo) / "docs" / "11-api-catalog.md").write_text("\n".join(md) + "\n")
    print(f"markdown -> {Path(args.repo) / 'docs' / '11-api-catalog.md'}")


if __name__ == "__main__":
    main()
