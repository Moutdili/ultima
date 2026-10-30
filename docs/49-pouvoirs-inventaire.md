# 49 — Pouvoirs : inventaire pour le modèle de dégâts

> Suite du plan (étape 1) : avant de modéliser, compter ce que chaque pouvoir expose vraiment.
> `tools/powers/index_inventory.py` (machine du jeu) → `data/catalog/power_inventory.json`.

## Mesure sur les 260 pouvoirs

    hook évaluable        56   multiplicateur/plat de dégâts, plafond, armure, pioche, énergie
    dynamic_only          24   passe par DynamicVars, aucun hook lisible
    conditional_only     151   logique dans des branches (state machine, conditions de tour)
    no_signal             29   ni hook, ni DynamicVars, ni condition

    appliqués par une carte ou un monstre : 227 / 260

Détail des primitives présentes :

    damage_multiplier 21 · draw 8 · damage_flat 7 · card_play_count 6
    energy 5 · block_multiplier 4 · block_flat 2 · damage_cap 2 · status_amplifier 2

C'est cohérent avec le skill combat-math (49 pouvoirs à hook évaluable) : la différence vient du
périmètre (ici j'inclus aussi pioche/énergie/jeu de cartes, qui ne touchent pas un coup).

## Ce que chaque état veut dire

- **hook** : une expression de retour est extraite (`0.75`, `1.5`, `Amount`…). Modélisable.
  Douze de ces pouvoirs ont en plus une direction qualifiée (docs/47) ; les autres restent
  « hook connu, direction à trancher » — publiés non modélisés.
- **dynamic_only** : l'effet est porté par `DynamicVars`, donc par la carte qui l'applique.
  Ce n'est **pas** neutre : c'est une absence de données, pas un multiplicateur à 1.
- **conditional_only** : le comportement est dans les branches. Modéliser demande la machine à
  états du pouvoir (ex. Afterimage : au tour où une carte est jouée).
- **no_signal** : rien d'indexable dans le fichier — à traiter à la main ou à déclarer hors modèle.

## Porte de sortie (étape 3 du plan)

Avant d'écrire les primitives, le backtest doit dire si elles achètent quelque chose : AUC de
prédiction de survie, avant/après activation d'un pouvoir, sur les runs où il est présent. Si
l'AUC ne bouge pas, on s'arrête là et on l'écrit — plutôt que de polluer le moteur.

## Ce que l'inventaire ne dit pas

- **la rotation** (quel pouvoir est actif à quel tour) — hors data, docs/22 l'interdit ;
- **les valeurs** des DynamicVars par carte : elles sont dans `cards_effects.json`, pas ici ;
- un pouvoir peut avoir plusieurs hooks et un seul modélisé : le détail par hook est dans
  `data/catalog/powers.json` (`hooks`, `evaluable`, `resolution`).
