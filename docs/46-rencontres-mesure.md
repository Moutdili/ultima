# 46 — Rencontres : mesure maison des statistiques manquantes

> Point 4 du chef (docs/42) : **37 rencontres sur 87 sans statistiques**. Ce sont des monstres
> d'acte 1 très courants (Cultists, Nibbits, Mawler, Corpse Slugs…) — donc mesurables sur nos
> propres runs, au lieu de rester muets.

## Outil

`tools/encounters/measure.py` → `data/calibration/encounter_measure.json`

    source    data/exports/runs-v0.107.1.jsonl.gz
    corpus    785 121 runs lus · 475 845 runs solo retenus
    seuil     une rencontre n'est publiée qu'à partir de 200 occurrences

Conventions, écrites dans le fichier :

- **solo uniquement** : en co-op les dégâts d'un étage ne sont attribuables à personne ;
- **dégâts de l'étage attribués à la première salle** (jamais comptés deux fois si l'étage a
  plusieurs salles) ;
- `avg_turns` = moyenne des `turns_taken` renseignés ; absent si aucun tour n'est renseigné
  (événements).

## Résultat

    157 rencontres publiées
    35 des 37 manquantes couvertes
     2 restent muettes, sous le seuil : THE_ARCHITECT_EVENT_ENCOUNTER, TUNNELER_NORMAL

## Comment le moteur s'en sert

`ThreatModel.LoadFrom(...)` complète les rencontres dont le catalogue n'a pas de `stats` :

- la mesure **ne remplace jamais** une statistique communautaire ;
- la provenance est portée par `ObservedStats.Source` : `"measured"` pour la mesure, `null` pour
  la communauté ;
- une statistique **par personnage** n'est publiée que si cet échantillon atteint 200 runs ;
  sinon `AverageDamage(rencontre, personnage)` renvoie `null` — jamais la moyenne globale déguisée.

## Réserve

C'est une **observation**, pas une propriété du monstre : les dégâts subis incluent le deck et les
reliques du joueur. À lire comme « ce qu'on encaisse à cet étage », ce qui est exactement ce dont
la route et la survie ont besoin. La note `caveat` du fichier le dit.

## Tests

`EncounterMeasureTests` (3) : complétion d'une rencontre sans stats, statistique par personnage
utilisée quand présente, statistique communautaire jamais écrasée.
