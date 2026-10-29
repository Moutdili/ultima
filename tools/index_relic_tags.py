#!/usr/bin/env python3
"""Complète les tags mécaniques de data/catalog/relics.json à partir des descriptions FR.

Source  : data/catalog/relics.json (id, name_fr, description_fr, tags — généré par
          tools/index_relics.py depuis la localisation FR du jeu).
Sorties :
  - data/catalog/relics.json        : tags COMPLÉTÉS (union tags existants + tags dérivés,
                                      jamais d'écrasement), fichier lu par RelicCatalog.
  - data/catalog/relic_tags.json    : registre de dérivation — vocabulaire, règles avec leur
                                      exemple et leurs gardes, tags par relique, règle(s) qui a
                                      produit chaque tag, statut tagged/no_effect et raison.

Principe : on ne lit que ce que le joueur lit. Aucun effet n'est inventé. Une relique dont la
description ne décrit aucun mécanisme reçoit `no_effect` + la description citée, jamais une
liste vide. Les formulations conditionnelles du type « Lorsque vous ajoutez une carte à votre
Deck » sont des DÉCLENCHEURS, pas des effets de la relique : une garde les exclut (champ
guards), sinon `add_card` serait posé sur les trois œufs.

Vocabulaire : voir VOCABULARY ; l'ordre est la priorité de troncature à MAX_TAGS. Chaque règle
porte un exemple RÉEL du corpus qui doit matcher, et un témoin dans le catalogue : une règle
morte fait échouer le script (self_check).

Usage :
    python3 -I tools/index_relic_tags.py            # écrit les deux fichiers
    python3 -I tools/index_relic_tags.py --check    # n'écrit rien, code 1 si dérive
    python3 -I tools/index_relic_tags.py --dump     # imprime le résultat par relique
"""
from __future__ import annotations

import json
import os
import re
import sys

MAX_TAGS = 8

# tag → (catégorie, sens mécanique). `debuff` = effet négatif infligé AUX ENNEMIS ;
# `penalty` = coût subi PAR LE JOUEUR (PV, statut). La distinction est volontaire : seuls
# block/draw/energy/heal/strength/debuff/curse sont convertis en ΔG par PlayerStrength,
# donc `penalty` n'inflate pas le score d'une relique qui se punit elle-même.
VOCABULARY: list[tuple[str, str, str]] = [
    ("revive", "survie", "évite la mort / ressuscite le joueur"),
    ("maxhp", "ressource", "PV Max (gain ou perte permanente)"),
    ("heal", "ressource", "soigne le joueur"),
    ("block", "défense", "armure / blindage"),
    ("thorns", "défense", "épines : riposte au contact"),
    ("mitigation", "défense", "plafonne ou réduit les dégâts subis (hors armure)"),
    ("damage", "offense", "dégâts infligés par la relique"),
    ("aoe", "offense", "dégâts à TOUS les ennemis"),
    ("poison", "offense", "poison appliqué aux ennemis"),
    ("strength", "offense", "Force"),
    ("dexterity", "offense", "Dextérité"),
    ("focus", "offense", "Focalisation (orbes)"),
    ("energy", "ressource", "énergie gagnée ou conservée"),
    ("draw", "ressource", "pioche supplémentaire"),
    ("gold", "ressource", "or gagné, économisé ou dépensé"),
    ("potion", "ressource", "emplacements ou potions"),
    ("star", "ressource", "étoiles"),
    ("orb", "ressource", "orbes canalisées ou emplacements d'orbe"),
    ("upgrade", "deck", "améliore des cartes"),
    ("enchant", "deck", "envoûte des cartes"),
    ("transform", "deck", "transforme des cartes"),
    ("remove", "deck", "retire des cartes du deck"),
    ("add_card", "deck", "ajoute des cartes au deck / à la main / à la pioche"),
    ("copy", "deck", "copie ou rejoue une carte"),
    ("exhaust", "deck", "épuise des cartes"),
    ("ethereal", "deck", "rend des cartes éthérées"),
    ("retain", "deck", "retient des cartes en main"),
    ("discard", "deck", "défausse des cartes"),
    ("cost", "deck", "modifie le coût des cartes"),
    ("summon", "invocation", "invoque un serviteur"),
    ("forge", "invocation", "mécanique Forge"),
    ("curse", "malus", "ajoute une malédiction au deck"),
    ("penalty", "malus", "coût subi par le joueur (PV, statut négatif)"),
    ("debuff", "contrôle", "effet négatif appliqué aux ennemis"),
    ("shop", "méta", "marchand / boutique"),
    ("rest", "méta", "site de repos (repos, cuisine, fouille)"),
    ("path", "méta", "plan / carte / salles"),
    ("reward", "méta", "récompenses de carte"),
    ("elite", "méta", "élites"),
    ("relic", "méta", "donne ou améliore une relique"),
    ("no_effect", "statut", "aucun effet mécanique décrit dans la description"),
]

# Règles : (tag, regex, exemple RÉEL du corpus, gardes).
# - les regex s'appliquent à la description FR débarrassée des balises couleur ;
# - une garde qui matche la description annule la règle (déclencheur, pas effet) ;
# - l'ordre suit VOCABULARY et sert de priorité de troncature.
RULES: list[tuple[str, list[str], str, list[str]]] = [
    ("revive", [r"PV (?:sont )?réduits à 0", r"PV atteignent 0", r"résurrect", r"vous mourez"],
     "Lorsque vos PV sont réduits à [blue]0[/blue], récupérez [green]{Heal} %[/green] de vos PV max",
     []),
    ("maxhp", [r"PV Max", r"PV maximum"],
     "Lorsque vous obtenez cette relique, augmentez vos PV Max de [blue]{MaxHp}[/blue].",
     []),
    ("heal", [r"récupérez", r"soigne", r"régénération"],
     "À la fin de chaque combat, récupérez [green]{Heal}[/green] PV.",
     []),
    ("block", [r"Armure", r"Blindage"],
     "Démarrez chaque combat avec [blue]{Block}[/blue] d'[gold]Armure[/gold].",
     []),
    ("thorns", [r"Épines"],
     "Démarrez chaque combat avec [blue]{ThornsPower}[/blue] d'[gold]Épines[/gold].",
     []),
    ("mitigation", [r"perdre plus de \d", r"dégâts des ennemis sont réduits",
                    r"PV de moins", r"réduits de moitié", r"dégâts en moins"],
     "Vous ne pouvez perdre plus de [blue]20[/blue] PV en un seul tour.",
     []),
    ("damage", [r"(?<!vous )inflige", r"Vigueur", r"double de dégâts"],
     "Chaque fois que vous [gold]Épuisez[/gold] une carte, infligez [blue]{Damage}[/blue] dégâts à un ennemi aléatoire.",
     [r"Effets négatifs", r"dégâts en moins"]),
    ("aoe", [r"dégâts?.{0,40}à TOUS les ennemis"],
     "Au début de chaque combat, infligez [blue]{Damage}[/blue] dégâts à TOUS les ennemis.",
     []),
    ("poison", [r"Poison"],
     "Au début de chaque combat, appliquez [blue]{PoisonPower}[/blue] de [gold]Poison[/gold] à TOUS les ennemis.",
     []),
    ("strength", [r"\bForce\b"],
     "Démarrez chaque combat avec [blue]{StrengthPower}[/blue] de [gold]Force[/gold].",
     []),
    ("dexterity", [r"Dextérité"],
     "Démarrez chaque combat avec [blue]{DexterityPower}[/blue] de [gold]Dextérité[/gold].",
     []),
    ("focus", [r"Focalisation"],
     "Commencez chaque combat avec [blue]{FocusPower}[/blue] de [gold]Focalisation[/gold].",
     []),
    ("energy", [r"Énergie", r"energyIcons", r"Energy\}"],
     "Gagnez {Energy:energyIcons()} au début de chaque tour.",
     []),
    ("draw", [r"piochez"],
     "Au début de chaque combat, piochez [blue]{Cards}[/blue] {Cards:plural:carte supplémentaire|cartes supplémentaires}.",
     []),
    ("gold", [r"Pièces d'or", r"\{Gold", r"remise"],
     "Lorsque vous obtenez cette relique, gagnez [blue]{Gold}[/blue] [gold]Pièces d'or[/gold].",
     []),
    ("potion", [r"potion"],
     "Lorsque vous obtenez cette relique, gagnez [blue]{PotionSlots}[/blue] nouveaux emplacements de potion",
     []),
    ("star", [r"starIcons", r"Étoiles?\b"],
     "Au début de chaque combat, gagnez {Stars:starIcons()}.",
     []),
    ("orb", [r"Canalisez", r"Orbes?\b", r"[Ee]mplacements? d'Orbe", r"\borbes?\b"],
     "Au début de chaque combat, [gold]Canalisez[/gold] [blue]{Lightning}[/blue] [gold]Orbe kéraunique[/gold].",
     []),
    ("upgrade", [r"Améliorez", r"Améliorée?s?\b", r"Améliorés?\b"],
     "Lorsque vous obtenez cette relique, [gold]Améliorez[/gold] une carte.",
     []),
    ("enchant", [r"Envoûtez", r"Envoûter", r"Envoûtée?s?\b"],
     "Lorsque vous obtenez cette relique, [gold]Envoûtez[/gold] une Compétence avec [purple]Imprégné[/purple].",
     []),
    ("transform", [r"Transformez", r"se transforme"],
     "Lorsque vous obtenez cette relique, [gold]Transformez[/gold] [blue]{Cards}[/blue] {Cards:plural:carte|cartes}.",
     []),
    ("remove", [r"retirez"],
     "Lorsque vous obtenez cette relique, retirez [blue]{Cards}[/blue] {Cards:plural:carte|cartes} de votre [gold]Deck[/gold].",
     []),
    ("add_card", [r"ajoutez[^.]{0,120}\b(?:Main|Pioche|Deck)\b", r"à ajouter à votre",
                  r"ajouter une carte", r"obtenez la carte", r"obtenez \{AncientCard",
                  r"obtenez \d+ cartes?"],
     "Au début de chaque combat, ajoutez [blue]1[/blue] carte parmi [blue]{Cards}[/blue] cartes aléatoires à votre [gold]Main[/gold].",
     [r"Lorsque vous ajoutez", r"Chaque fois que vous ajoutez", r"Si vous ajoutez", r"vous ajoutez"]),
    ("copy", [r"copie", r"jouée une fois de plus"],
     "Lorsque vous obtenez cette relique, obtenez une copie supplémentaire d'une carte dans votre [gold]Deck[/gold].",
     []),
    ("exhaust", [r"Épuisez", r"Épuisé"],
     "La première fois que vous [gold]Épuisez[/gold] une Compétence chaque combat, ajoutez-en une copie à votre [gold]Main[/gold].",
     []),
    ("ethereal", [r"Éthérée?s?\b"],
     "Les [gold]Frappes[/gold] et les [gold]Défenses[/gold] deviennent [gold]Éthérées[/gold].",
     []),
    ("retain", [r"Retenez", r"Retenue", r"ne défaussez pas"],
     "[gold]Retenez[/gold] votre [gold]Main[/gold] le premier tour de chaque combat.",
     []),
    ("discard", [r"défaussez"],
     "Pour chaque carte que vous défaussez pendant votre tour, infligez [blue]{Damage}[/blue] dégâts",
     [r"ne défaussez pas"]),
    ("cost", [r"coût", r"sans coût", r"coûte"],
     "La [blue]5ème[/blue] carte jouée depuis votre main chaque tour peut être jouée sans coût.",
     [r"carte qui coûte"]),
    ("summon", [r"Invoquez"],
     "Au début de votre tour, [gold]Invoquez[/gold] pour [blue]{Summon}[/blue].",
     []),
    ("forge", [r"Forgez"],
     "Au début de chaque combat, [gold]Forgez[/gold] [blue]{Forge}[/blue].",
     []),
    ("curse", [r"Malédiction"],
     "Lorsque vous obtenez cette relique, obtenez une [red]Malédiction[/red] spéciale",
     []),
    ("penalty", [r"perdez[^.]{0,25}\bPV\b", r"Désorienté"],
     "Lorsque vous obtenez cette relique, perdez [blue]{HpLoss}[/blue] PV",
     [r"PV de moins", r"fois que vous perdez", r"vous perdez des PV"]),
    ("debuff", [r"Vulnérable", r"Affaibli", r"Faibli", r"Fatalité", r"Effets négatifs"],
     "Au début de chaque combat, appliquez [gold]Vulnérable[/gold] ([blue]{VulnerablePower}[/blue]) sur TOUS les ennemis.",
     []),
    ("shop", [r"Marchand", r"boutique", r"remise"],
     "Lorsque vous passez chez le [gold]Marchand[/gold], vous obtenez immédiatement TOUT ce qu'il propose.",
     []),
    ("rest", [r"Reposez", r"Sites? de [Rr]epos", r"Cuisiner", r"Fouiller"],
     "Vous pouvez effectuer autant d'actions différentes que vous souhaitez aux [gold]Sites de repos[/gold].",
     []),
    ("path", [r"\bPlan\b", r"chemin", r"carte de l'Acte", r"trajets", r"salles \?"],
     "Lorsque vous obtenez cette relique, marquez [blue]{Combats}[/blue] combats aléatoires sur le Plan.",
     []),
    ("reward", [r"récompense"],
     "Les [gold]Élites[/gold] donnent une récompense de carte [gold]Rare[/gold] supplémentaire.",
     []),
    ("elite", [r"Élites?"],
     "Les [gold]Élites[/gold] donnent une Relique supplémentaire lorsqu'ils sont vaincus.",
     []),
    ("relic", [r"\{Relics\}", r"une Relique\b", r"Reliques? aléatoires?",
               r"Relique supplémentaire", r"Reliques? de cire", r"Relique de départ",
               r"puissante Relique", r"Relique de Neow", r"Relique améliorée"],
     "Lorsque vous obtenez cette relique, obtenez une [gold]Relique[/gold] aléatoire.",
     []),
]

# Statuts forcés : le texte dit lui-même qu'il n'y a rien à mesurer. Prioritaire sur les
# règles (sinon « retirée du jeu » gagne `relic` par simple mention du mot).
NO_EFFECT_PATTERNS: list[tuple[str, str]] = [
    (r"\bSans effet\b", "le texte l'affirme lui-même : « Sans effet. »"),
    (r"retirée du jeu", "relique retirée du jeu : aucun effet en jeu"),
    (r"C'est un diadème", "description purement cosmétique (« C'est un diadème. ») : aucun mécanisme"),
]

TAG_RE = [(tag, [re.compile(p, re.IGNORECASE) for p in pats],
           [re.compile(p, re.IGNORECASE) for p in guards], example)
          for tag, pats, example, guards in RULES]
TAG_ORDER = [t for t, _, _ in VOCABULARY]
KNOWN = set(TAG_ORDER)


def strip_markup(text: str) -> str:
    """Retire les balises couleur ([gold]…[/gold]) pour un extrait lisible."""
    return re.sub(r"\[/?[a-z]+\]", "", text)


def forced_no_effect(text: str) -> str | None:
    """Raison du statut no_effect imposé par le texte lui-même, sinon None."""
    for pattern, reason in NO_EFFECT_PATTERNS:
        if re.search(pattern, text, re.IGNORECASE):
            return reason
    return None


def derive(description: str) -> list[tuple[str, str]]:
    """[(tag, preuve)] — tags dérivés dans l'ordre du vocabulaire, sans doublon."""
    text = strip_markup(description)
    out: list[tuple[str, str]] = []
    for tag, regs, guards, _ in TAG_RE:
        if any(g.search(text) for g in guards):
            continue
        for r in regs:
            m = r.search(text)
            if m:
                start = max(0, m.start() - 30)
                excerpt = text[start:m.end() + 30].strip()
                out.append((tag, f"{r.pattern} → « …{excerpt}… »"))
                break
    return out


def merge(existing: list[str], derived: list[str]) -> tuple[list[str], list[str]]:
    """Union sans écrasement : existants d'abord, puis dérivés par priorité, borné à MAX_TAGS."""
    tags = [t for t in existing if t]
    added = []
    for tag in derived:
        if tag not in tags:
            tags.append(tag)
            added.append(tag)
    return tags[:MAX_TAGS], added


def self_check() -> None:
    """Chaque règle a un tag connu, un exemple réel non bloqué par sa garde, et un témoin."""
    seen = set()
    for tag, regs, guards, example in TAG_RE:
        assert tag in KNOWN, f"tag hors vocabulaire : {tag}"
        assert tag not in seen, f"règle en double : {tag}"
        seen.add(tag)
        text = strip_markup(example)
        assert not any(g.search(text) for g in guards), f"l'exemple de {tag} est bloqué par sa garde"
        assert any(r.search(text) for r in regs), f"exemple non couvert par la règle {tag} : {example}"
    for tag, _, _ in VOCABULARY:
        if tag != "no_effect":
            assert tag in seen, f"tag sans règle : {tag}"
    assert forced_no_effect("Une piètre imitation. Sans effet."), "garde no_effect inerte"


def build(catalog_path: str) -> dict:
    with open(catalog_path, encoding="utf-8") as f:
        catalog = json.load(f)

    entries = []
    dist: dict[str, int] = {}
    before = 0
    added_total = 0
    no_effect = []
    derived_by_id: dict[str, list[str]] = {}

    for relic in catalog.get("relics", []):
        rid = relic["id"]
        desc = relic.get("description_fr", "") or ""
        # `tags_source` = tags produits par tools/index_relics.py, conservés tels quels :
        # c'est ce qui rend la dérivation idempotente (relancer ne change ni existing_tags
        # ni added_tags). Absent après régénération du catalogue → on part de `tags`.
        raw = relic.get("tags_source", relic.get("tags"))
        existing = [t for t in (raw or []) if isinstance(t, str) and t]
        if existing:
            before += 1

        hits = derive(desc)
        derived = [t for t, _ in hits]
        derived_by_id[rid] = derived
        forced = forced_no_effect(strip_markup(desc))

        entry = {"id": rid, "tags": [], "status": "tagged", "existing_tags": existing}

        if forced is not None:
            entry["tags"] = ["no_effect"]
            entry["status"] = "no_effect"
            entry["no_effect_reason"] = forced
            no_effect.append({"id": rid,
                              "name_fr": relic.get("name_fr", rid),
                              "description_fr": desc,
                              "reason": forced})
        else:
            tags, added = merge(existing, derived)
            if not tags:
                reason = ("aucune règle ne s'applique : la description ne nomme aucun mécanisme "
                          "évaluable (citée telle quelle, aucun effet n'est supposé)")
                entry["tags"] = ["no_effect"]
                entry["status"] = "no_effect"
                entry["no_effect_reason"] = reason
                no_effect.append({"id": rid,
                                  "name_fr": relic.get("name_fr", rid),
                                  "description_fr": desc,
                                  "reason": reason})
            else:
                entry["tags"] = tags
                added_total += len(added)
                # Preuve uniquement pour les tags AJOUTÉS : les tags d'origine ont déjà leur
                # source (tools/index_relics.py) et n'ont pas besoin d'être justifiés ici.
                if added:
                    entry["evidence"] = {t: why for t, why in hits if t in added}

        if len(entry["tags"]) > MAX_TAGS:
            raise SystemExit(f"{rid}: {len(entry['tags'])} tags > {MAX_TAGS}")
        for t in entry["tags"]:
            dist[t] = dist.get(t, 0) + 1
        entries.append(entry)

    rules_doc = []
    for tag, regs, guards, example in TAG_RE:
        witness = next((rid for rid, d in derived_by_id.items() if tag in d), None)
        rules_doc.append({
            "tag": tag,
            "patterns": [r.pattern for r in regs],
            "guards": [g.pattern for g in guards],
            "example": example,
            "witness_relic": witness,
        })

    return {
        "schema": "ultima.catalog.relic_tags/1",
        "source": "data/catalog/relics.json (description_fr) — tools/index_relic_tags.py",
        "max_tags": MAX_TAGS,
        "vocabulary": [{"tag": t, "category": c, "meaning": m} for t, c, m in VOCABULARY],
        "no_effect_patterns": [{"pattern": p, "reason": r} for p, r in NO_EFFECT_PATTERNS],
        "rules": rules_doc,
        "coverage": {
            "relics": len(entries),
            "tagged_before": before,
            "tagged_after": len(entries) - len(no_effect),
            "no_effect": len(no_effect),
            "tags_added": added_total,
        },
        "distribution": dict(sorted(dist.items(), key=lambda kv: (-kv[1], kv[0]))),
        "no_effect_relics": no_effect,
        "relics": entries,
    }


def write_outputs(ledger: dict, catalog_path: str) -> None:
    by_id = {e["id"]: e for e in ledger["relics"]}
    with open(catalog_path, encoding="utf-8") as f:
        catalog = json.load(f)
    for relic in catalog["relics"]:
        entry = by_id[relic["id"]]
        relic["tags_source"] = entry["existing_tags"]
        relic["tags"] = entry["tags"]
    with open(catalog_path, "w", encoding="utf-8") as f:
        json.dump(catalog, f, ensure_ascii=False, indent=1)
        f.write("\n")

    # Compact : le budget de données commitées (2 Mo, OfflinePackagingTests) compte ce
    # fichier. Un JSON indenté coûtait ~40 % de plus pour un contenu déjà redondant avec
    # relics.json (les descriptions n'y sont plus dupliquées).
    out = os.path.join(os.path.dirname(catalog_path), "relic_tags.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump(ledger, f, ensure_ascii=False, separators=(",", ":"))
        f.write("\n")


def main(argv: list[str]) -> int:
    self_check()
    catalog_path = os.path.join("data", "catalog", "relics.json")
    if not os.path.isfile(catalog_path):
        print(f"catalogue introuvable : {catalog_path}", file=sys.stderr)
        return 2

    ledger = build(catalog_path)
    cov = ledger["coverage"]

    if "--dump" in argv:
        for e in ledger["relics"]:
            print(f"{e['id']:32} {','.join(e['tags'])}")

    if "--check" in argv:
        out = os.path.join("data", "catalog", "relic_tags.json")
        if not os.path.isfile(out):
            print("relic_tags.json absent — lancer sans --check", file=sys.stderr)
            return 1
        with open(out, encoding="utf-8") as f:
            on_disk = json.load(f)
        if on_disk.get("relics") != ledger["relics"] or on_disk.get("coverage") != cov:
            print("dérive : relancer tools/index_relic_tags.py", file=sys.stderr)
            return 1
        print(f"à jour — {cov['relics']} reliques, {cov['no_effect']} no_effect")
        return 0

    write_outputs(ledger, catalog_path)
    print(f"{cov['relics']} reliques — taguées avant {cov['tagged_before']} → après "
          f"{cov['tagged_after']}, no_effect {cov['no_effect']}, "
          f"{len(ledger['distribution'])} tags distincts, {cov['tags_added']} tags ajoutés")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
