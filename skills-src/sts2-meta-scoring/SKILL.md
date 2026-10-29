---
name: sts2-meta-scoring
description: Use when scoring Slay the Spire 2 cards at card rewards or in the shop, scoring a deck against relics and the act boss, or proposing potential decks/archetypes for the external tracker. Defines the explainable score formula, data sources and confidence rules.
---

# STS2 — scoring explicable (récompenses, shop, deck)

## Moments d'usage (demande de Mio)
1. **Récompense de cartes** : noter chaque carte proposée + « passer ».
2. **Shop** : noter chaque carte/relique/potion ET le retrait de carte, prix et or compris.
3. **Deck** : note globale vs reliques possédées et boss de l'acte courant ; archétype probable
   et « decks potentiels » (vers quoi tendre).

## Formule : toujours une somme de termes nommés
`score = base + synergie_deck + synergie_reliques + adéquation_boss + courbe + perso`
- `base` : tier communautaire du patch (source + date) sinon heuristique du catalogue.
- `synergie_*` : règles explicites (ex. poison : +k par carte poison au deck), jamais une boîte noire.
- `adéquation_boss` : boss de l'acte (`boss_id`, visible en jeu) : multi-coups, débuffs, scaling.
- `perso` : écart de taux de victoire de Mio quand la carte est prise (history, même `build_id`),
  borné et pondéré par n (intervalle de confiance ; n < 10 → 0 et mention « peu de données »).
Le détail (F9) affiche chaque terme avec sa source. Aucun nombre magique.

## Notation affichée : S à F (décision Mio)
`Grades.FromTotal` (contrat) : S ≥ 85, A ≥ 70, B ≥ 55, C ≥ 40, D ≥ 25, F < 25. Les Total doivent
être calibrés sur cette échelle (tiers data/meta : S≈90, A≈75, B≈60, C≈45, D≈30).

## Données
- Catalogue : `data/catalog/*.json` (régénéré depuis le source), localisation `game/pck/localization/fra`.
- Perso : `history/*.run` (`map_point_history[].player_stats[].card_choices` = proposées/choisies).
- Communauté : `data/meta/*.json` (chaque entrée : `source`, `date`, `patch`).
- Interdits : séquences pré-tirées de la save (voir `sts2-save-format`).

## Validation
10 situations réelles tirées des runs de Mio, notées par lui ; afficher la corrélation.
