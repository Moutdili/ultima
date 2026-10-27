#!/usr/bin/env python3
"""Indexe le source C# de Slay the Spire 2 (extrait du .pck) en catalogues JSON.

Zéro dépendance. Lit `game/pck/src/Core`, écrit `data/catalog/*.json` + `docs/11-api-catalog.md`.

Particularités du code du jeu traitées ici :
  - les HP et dégâts passent souvent par
      AscensionHelper.GetValueIfAscension(AscensionLevel.X, <valeurAscension>, <valeurNormale>)
  - les intents référencent des propriétés (`new SingleAttackIntent(KillshotDamage)`)
  - le bonus conditionnel d'une carte se signale via `ShouldGlowGoldInternal`
  - « déjà jouée ce tour » via `CombatHistory.CardPlaysFinished` + `HappenedThisTurn`

Usage:
    python3 tools/index_source.py --src game/pck/src/Core --out data/catalog
"""
from __future__ import annotations

import argparse
import json
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
MOVE_RE = re.compile(
    r'new\s+MoveState\s*\(\s*"([^"]+)"\s*,\s*(\w+)\s*,\s*new\s+(\w+)\s*\(([^;]*?)\)\s*\)'
)
HP_RE = re.compile(r"override\s+int\s+(MinInitialHp|MaxInitialHp)\s*=>\s*([^;]+);")
TAG_RE = re.compile(r"CardTag\.(\w+)")
MOD_RE = re.compile(r"ModifyDamage(Additive|Multiplicative|Cap)")
RET_DEC_RE = re.compile(r"return\s+(-?[0-9.]+)m?\s*;")
GLOW_GOLD_RE = re.compile(r"ShouldGlowGoldInternal")
GLOW_RED_RE = re.compile(r"ShouldGlowRedInternal")
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

        moves = []
        for state, method, intent, args_blob in MOVE_RE.findall(text):
            raw_args = [a.strip() for a in args_blob.split(",") if a.strip()]
            resolved = [resolve(a, symbols) for a in raw_args]
            moves.append(
                {
                    "state": state,
                    "method": method,
                    "intent": intent,
                    "args": raw_args,
                    "values": resolved,
                }
            )
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
        if "Attack" not in mv["intent"]:
            continue
        vals = []
        for v in mv["values"]:
            vals.append("/".join(str(x) for x in v) if v else "?")
        out.append(f"{mv['state']}={mv['intent']}({','.join(mv['args'])})→[{','.join(vals)}]")
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

    cards = parse_cards(core)
    monsters = parse_monsters(core)
    powers = parse_powers(core)
    priority = [c for c in cards if c.get("priority_candidate")]

    stats = {
        "source_root": str(src),
        "cards": len(cards),
        "cards_by_type": dict(Counter(c.get("type", "?") for c in cards)),
        "cards_by_rarity": dict(Counter(c.get("rarity", "?") for c in cards)),
        "cards_priority_candidates": len(priority),
        "monsters": len(monsters),
        "monsters_with_hp": sum(1 for m in monsters if "min_hp" in m),
        "monsters_ascension_scaled": sum(1 for m in monsters if m.get("ascension_scaled")),
        "monsters_with_moves": sum(1 for m in monsters if m.get("moves")),
        "monsters_with_attack_moves": sum(1 for m in monsters if attack_summary(m)),
        "powers_modifying_damage": len(powers),
    }

    (outdir / "cards.json").write_text(json.dumps(cards, indent=1, ensure_ascii=False))
    (outdir / "monsters.json").write_text(json.dumps(monsters, indent=1, ensure_ascii=False))
    (outdir / "powers.json").write_text(json.dumps(powers, indent=1, ensure_ascii=False))
    (outdir / "priority_cards.json").write_text(json.dumps(priority, indent=1, ensure_ascii=False))
    (outdir / "stats.json").write_text(json.dumps(stats, indent=1, ensure_ascii=False))
    print(json.dumps(stats, indent=2, ensure_ascii=False))

    md = ["# 11 — Catalogue API & données (généré)\n"]
    md.append("> Généré par `tools/index_source.py` depuis le **source original** du jeu.")
    md.append("> Ne pas éditer à la main : relancer l'indexeur après un patch du jeu.\n")
    md.append(f"Source : `{stats['source_root']}` (v0.107.1)\n")
    md.append("## Volumétrie\n")
    md.append(f"- Cartes : **{stats['cards']}** — {stats['cards_by_type']}")
    md.append(f"- Cartes à payoff conditionnel (glow / historique) : **{stats['cards_priority_candidates']}**")
    md.append(f"- Monstres : **{stats['monsters']}**, dont {stats['monsters_with_attack_moves']} avec moves offensifs")
    md.append(f"- Monstres à HP ascensionnels : {stats['monsters_ascension_scaled']}")
    md.append(f"- Pouvoirs modifiant les dégâts : **{stats['powers_modifying_damage']}**\n")

    md.append("## Multiplicateurs de dégâts (pouvoirs)\n")
    md.append("| Pouvoir | Hooks | Littéraux retournés |")
    md.append("|---|---|---|")
    for p in powers:
        md.append(f"| `{p['class']}` | {', '.join(p['hooks'])} | {p['literals'] or '—'} |")
    md.append("")

    md.append("## Cartes à payoff conditionnel (priorité de tour)\n")
    md.append("| Carte | Type | Coût | Rareté | GlowGold | Historique | Fichier |")
    md.append("|---|---|---|---|---|---|---|")
    for c in priority:
        md.append(
            f"| `{c['class']}` | {c.get('type','?')} | {c.get('cost','?')} | {c.get('rarity','?')} | "
            f"{'✅' if c.get('glow_gold') else '—'} | {'✅' if c.get('turn_conditional') else '—'} | `{c['file']}` |"
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
