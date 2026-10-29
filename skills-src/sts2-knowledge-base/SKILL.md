---
name: sts2-knowledge-base
description: Use when building or querying Ultima.Knowledge — French/English names and descriptions of cards, relics, enchantments (Sown, Swift…), potions, encounters and characters from the game's localization and catalog, BBCode/SmartFormat rendering, ID mapping and curse detection.
---

# ULTIMA — base de connaissance statique (implémente `IKnowledge`)

## Sources (lecture seule)
- Localisation : `game/pck/localization/{fra,eng}/*.json` — clés `<ID>.title`, `<ID>.description`
  (+ `extraCardText`, `eventDescription`…), ex. `"SOWN.title": "Semence"`, `"ASCENDERS_BANE.title":
  "Fléau de l'ascendant"`. Fichiers : `cards`, `relics`, `enchantments`, `potions`, `encounters`,
  `characters`, `powers`, `card_keywords`, `monsters`.
- Catalogue généré : `data/catalog/*.json` (classe C#, coût, type, rareté, `vars`).
- Source : `game/pck/src/Core/Models/{Cards,Relics,Enchantments,Potions}/` (24 enchantements).

## Correspondance d'identifiants
Save/miroir : `CARD.ASCENDERS_BANE`, `RELIC.X`, `POTION.X`, `ENCHANTMENT.X`, `ENCOUNTER.X`, `CHARACTER.X`
→ clé de localisation = partie après le point ; classe C# = PascalCase (catalogue champ `class`).
Lister tout identifiant sans traduction (test) au lieu d'inventer un nom.

## Rendu du texte
- BBCode du jeu : `[gold]`, `[blue]`, `[purple]`, `[red]`… → texte brut + marques sémantiques optionnelles.
- SmartFormat : `{Var:diff()}`, `{Var:plural:a|b}`, `{Amount:energyIcons()}` → substituer avec les `vars`
  du catalogue (valeur de base ; amélioration indiquée si connue), jamais laisser une accolade brute.
- Malédiction : type ou rareté `Curse` (catalogue) → `IsCurse=true`.

## Exigences
Pur, sans jeu ; chargement < 300 ms ; toutes les cartes/reliques/enchantements/potions du catalogue
résolus en FR (couverture chiffrée en test) ; repli EN puis identifiant, signalé.
