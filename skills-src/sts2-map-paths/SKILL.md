---
name: sts2-map-paths
description: Use when computing Slay the Spire 2 map paths for the external tracker — saved_map DAG, path enumeration from the current node to the boss, per-path room counts, node traversal frequency, and probabilistic risk without using hidden pre-rolled data.
---

# STS2 — chemins sur la carte (probabiliste uniquement)

## Graphe
`acts[current_act_index].saved_map` : 7 colonnes × 16 rangées ; `start` (rangée 0, `ancient`) ;
`points[]` avec `children[]` (coords de la rangée suivante) ; `boss` (rangée 16).
Position = dernier `visited_map_coords`. DAG orienté vers le haut : énumération exacte par DFS +
mémoïsation (nombre de chemins = somme des chemins des enfants).

## Sorties attendues
- Par chemin (ou par classe de chemins équivalents) : nb `monster`, `elite`, `unknown`,
  `rest_site`, `shop`, `treasure` jusqu'au boss.
- Par case : **fréquence de passage** = nb de chemins qui la traversent / nb total de chemins
  (« sur tes 23 chemins possibles, 17 passent par ce feu »).
- Choix immédiat (enfants de la position) : pour chaque enfant, distribution (min/médiane/max)
  des comptes sur ses chemins + risque.

## Risque (décision Mio : jamais de tirage exact)
- Interdit : `normal_encounter_ids`, `elite_encounter_ids`, `event_ids` et compteurs `*_visited`.
- Autorisé : dégâts moyens subis par TYPE de salle et par acte, calculés sur l'historique
  personnel (`history/*.run`, même `build_id`, même personnage si n suffisant), PV actuels, boss de l'acte.
- `?` : cotes PUBLIQUES de base lues dans le source du jeu (pas `odds` de la save).
- Toujours afficher n (taille d'échantillon) et l'hypothèse ; « peu de données » si n < 10.
