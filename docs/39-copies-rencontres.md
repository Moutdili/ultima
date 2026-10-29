# 34 — Copies de monstres : les effectifs réels par rencontre

> Mesuré le 2026-10-29 sur **459 386 runs solo « standard »** (5 296 129 combats) de l'export
> spire-codex v0.107.1. Script `tools/index_encounter_copies.py` → `data/catalog/encounter_copies.json`
> (23 Ko, 86 rencontres observées sur 87).

## 1. Le problème : un type compté une fois, trois exosquelettes alignés

`ThreatModel.MaxHit` additionne **un** `WorstHit` par monstre listé dans `data/catalog/encounters.json`,
donc une fois par *type*. Or les combats réels alignent des copies, et l'export les donne :

```json
{"model_id": "ENCOUNTER.EXOSKELETONS_WEAK",
 "monster_ids": ["MONSTER.EXOSKELETON", "MONSTER.EXOSKELETON", "MONSTER.EXOSKELETON"],
 "room_type": "monster", "turns_taken": 5}
```

Mesure sur le corpus : **118 574 combats sur 118 574** de `EXOSKELETONS_WEAK` alignent exactement
3 exosquelettes ; `SCROLLS_OF_BITING_NORMAL` en aligne 4 sur 100 % de ses 23 949 combats. Le moteur
compte 1 × le pire coup, la rencontre en porte 3 ou 4.

L'index compact (`data/index/runs-<build>.jsonl.gz`) **ne contient pas** les rencontres : une ligne
n'a que `h, b, m, asc, n, w, ab, kb, rt, pl, d` (identité, joueurs, décisions). Le script le détecte
(`monster_ids`/`rooms` absents de la première ligne) et bascule sur l'export, au lieu de produire un
catalogue vide.

## 2. Méthode

| Étape | Règle | Effet mesuré |
|---|---|---|
| Corpus | export `data/exports/runs-v0.107.1.jsonl.gz`, `rooms[].monster_ids` (doublons inclus) | 785 121 runs lues |
| Mode | `game_mode == standard`, **solo** (1 joueur) | 459 386 runs gardées |
| Combat vanilla | rencontre au catalogue **et** tous les `monster_ids` connus des 128 monstres | 5 296 129 combats gardés · 8 917 écartés (rencontre hors catalogue) · 9 489 (monstre de mod) · 4 (aucun ennemi déclaré) |
| Ennemis | seuls les monstres **déclarés par la rencontre** comptent | pets et invocations retirés |

Le dernier filtre est indispensable : `monster_ids` contient aussi les **pets et invocations**
(`BYRDPIP` apparaît dans 1 203 combats de `QUEEN_BOSS`, `OSTY` dans 3 904 combats de
`SCROLLS_OF_BITING_NORMAL`, `GUARDBOT`/`NOISEBOT` dans la moitié des combats de `FABRICATOR_NORMAL`).
Les additionner reviendrait à compter les dégâts du joueur comme menace ennemie — et à inventer des
« copies » qui sont des familiers. Les combats dont un monstre inconnu du catalogue apparaît (mods)
sont écartés, jamais recyclés.

Sortie committée, par rencontre : `total_fights`, `fights_with_copies`, `copies_share`,
`max_instances` (copies d'un **même** monstre — le multiplicateur de menace), `max_total_monsters`
(instances tous monstres confondus) et la distribution `monsters: {ID: {nombre: combats}}`.

## 3. Ce que la data dit

- **42 rencontres sur 86** ont au moins un combat à copies ; **17** en ont dans ≥ 50 % de leurs
  combats (en pratique ~100 %) ; 44 n'en ont **jamais** — le modèle y est exact.
- **1 050 258 combats à copies = 19,8 %** de tous les combats du corpus.
- Distribution de `max_instances` : 44 rencontres à 1, **31 à 2**, 5 à 3, **6 à 4**.
- La composition n'est **pas aléatoire** : dans les combats vanilla elle est fixe. Les seules
  variations viennent des rencontres à plusieurs types (`SLITHERING_STRANGLER_NORMAL` : 1 à 3
  serpents, 16,6 % de combats avec un doublon) et des événements.

| Acte | Rencontres | dont copies dominantes | Combats | Combats à copies |
|---|---:|---:|---:|---:|
| 1 (Overgrowth) | 42 | 8 | 3 015 671 | 20,5 % |
| 2 (Hive) | 20 | 4 | 1 504 491 | 19,3 % |
| 3 (Glory) | 18 | 3 | 736 085 | 17,5 % |
| Événements (sans acte) | 6 | 2 | 39 882 | 33,8 % |

Les 17 rencontres où les copies sont la règle (A0, pire coup du modèle → pire coup réel) :

| Rencontre | Type | max | Combats | Modèle | Réel | ×  |
|---|---|---:|---:|---:|---:|---:|
| SCROLLS_OF_BITING_NORMAL | Monster (a3) | 4 | 23 949 | 14 | 56 | **4,0** |
| EXOSKELETONS_NORMAL | Monster (a2) | 4 | 53 577 | 8 | 32 | **4,0** |
| PHANTASMAL_GARDENERS_ELITE | Elite (a1) | 4 | 98 376 | 7 | 28 | **4,0** |
| DENSE_VEGETATION_EVENT_ENCOUNTER | Événement | 4 | 7 654 | 6 | 24 | **4,0** |
| SCROLLS_OF_BITING_WEAK | Monster (a3) | 4 | 77 019 | 14 | 42 | 3,0 |
| EXOSKELETONS_WEAK | Monster (a2) | 3 | 118 574 | 8 | 24 | 3,0 |
| INKLETS_NORMAL | Monster (a1) | 3 | 42 647 | 10 | 30 | 3,0 |
| CORPSE_SLUGS_NORMAL | Monster (a1) | 3 | 41 180 | 8 | 24 | 3,0 |
| TWO_TAILED_RATS_NORMAL | Monster (a1) | 4 | 45 013 | 8 | 24 | 3,0 |
| CHOMPERS_NORMAL | Monster (a2) | 2 | 58 680 | 16 | 32 | 2,0 |
| NIBBITS_NORMAL | Monster (a1) | 2 | 41 955 | 12 | 24 | 2,0 |
| MYTES_NORMAL | Monster (a2) | 2 | 58 951 | 13 | 26 | 2,0 |
| TOADPOLES_WEAK | Monster (a1) | 3 | 146 181 | 9 | 18 | 2,0 |
| CORPSE_SLUGS_WEAK | Monster (a1) | 3 | 145 490 | 8 | 16 | 2,0 |
| PUNCH_OFF_EVENT_ENCOUNTER | Événement | 2 | 5 821 | 14 | 28 | 2,0 |
| CONSTRUCT_MENAGERIE_NORMAL | Monster (a3) | 2 | 27 956 | 24 | 34 | 1,42 |
| **THE_KIN_BOSS** | **Boss (a1)** | 2 | 50 431 | 14 | 19 | **1,36** |

`max_instances` est le **maximum observé**, pas la valeur typique : `CORPSE_SLUGS_WEAK` monte à 3
mais sa composition modale est 2 (médiane ×2) ; `TOADPOLES_WEAK` idem. La distribution du JSON donne
la valeur typique, `max_instances` la borne.

## 4. Impact : +11,8 % de menace agrégée, ×2 médian sur les combats à copies

Trois lectures, toutes au pire coup cumulé (`Σ copies × WorstHit` contre `Σ types × WorstHit`) :

| Mesure | A0 | A10+ |
|---|---:|---:|
| Agrégat `Σ réel / Σ modèle` sur 5,3 M combats | **1,118** | 1,118 |
| Médiane des ratios par combat — combats à copies | **×2,0** (p90 ×4,0) | ×2,0 (p90 ×4,0) |
| Médiane par rencontre — les 17 à copies dominantes | **×3,0** | ×3,0 |
| Rencontres sans aucune copie | 44 / 86 (ratio 1,0) | 44 / 86 |

L'agrégat ne vaut que **+11,8 %** parce que les copies ne concernent qu'un combat sur cinq ; la
sous-estimation est concentrée : ×2 à ×4 sur les rencontres concernées, nulle ailleurs. Dit
autrement, un cinquième des combats est joué avec un budget de bloc calculé sur une menace deux à
quatre fois trop faible.

L'ascension ne change pas les effectifs : sur 150 000 runs, aucune rencontre ne change de composition
modale entre A0, A1-A9 et A10+ (les modificateurs d'ascension modifient PV et dégâts, pas le nombre
d'ennemis dans cet échantillon). Le catalogue peut donc être poolé.

**Où cela mord aujourd'hui.** `MaxHit` n'alimente que `PlayerStrength.ResolveThreat` : le pire coup
du **boss** de l'acte (ou la médiane des boss de l'acte). Sur les 12 boss, un seul a des copies —
`THE_KIN_BOSS` (2 `KIN_FOLLOWER` + 1 `KIN_PRIEST`), ×1,36. Les rencontres à ×4 sont des monstres et
des élites : elles n'entrent dans le score que par `encounter_stats.avg_damage`, qui mesure les dégâts
réels et n'est donc pas affecté. Le catalogue est en revanche l'entrée exacte dès que les copies sont
branchées dans `MaxHit` par rencontre (tâche en cours) : sans lui, un deck de bloc serait jugé sur
8 dégâts au lieu de 24 contre `EXOSKELETONS_WEAK`.

## Branchement dans le moteur

`ThreatModel.MaxHit` multiplie désormais le pire coup de chaque monstre par son **nombre
d'instances observé** (`Instances(encounter, monster)`), sans compter deux fois un monstre déjà
listé plusieurs fois dans la rencontre. Effets mesurés par les tests :

- `SCROLLS_OF_BITING_NORMAL` : pire tour **×4** ;
- `AEONGLASS_BOSS` : inchangé (1 instance) ;
- `THE_KIN_BOSS` : 2 `KIN_FOLLOWER` + 1 `KIN_PRIEST`, strictement supérieur à la version sans copies.

## Limites

- **Distributions marginales, pas jointes.** Pour une rencontre à plusieurs types, la somme des « 1 »
  n'est pas le nombre de combats sans copie et on ne peut pas reconstruire la composition exacte
  depuis le JSON. Les médianes par rencontre du rapport (`--report-json`, non commité) lèvent
  l'ambiguïté ; le catalogue, lui, reste lisible par monstre.
- **Invocations et phases invisibles.** `monster_ids` liste des participants (y compris des
  invocations, qu'on retire), mais pas ce qui apparaît **en cours** de combat : respawn de
  `TEST_SUBJECT_BOSS`, vagues de `FABRICATOR`, transformations de boss. Ces menaces restent hors
  modèle (docs/33, omissions 2 et 4).
- **Mods.** 9 489 combats écartés parce qu'ils contiennent un monstre hors des 128 du catalogue ; il
  reste un risque résiduel qu'un mod ajoute un monstre **connu** (visible : `AXEBOTS_NORMAL` a 0,1 %
  de combats à 2 exemplaires, `QUEEN_BOSS` 0,0 %).
- **Périmètre.** Solo « standard » seulement : le co-op (39 % des runs de l'export) est exclu, et
  `THE_ARCHITECT_EVENT_ENCOUNTER` n'apparaît jamais dans le corpus (86 / 87 rencontres).
- **Bornes, pas des moyennes.** `Σ copies × pire coup` est un majorant statique : une copie qui meurt
  au tour 1 ou n'attaque pas le même tour ne frappe pas réellement. C'est la même convention que
  `MaxHit`, donc la comparaison est cohérente — mais ce n'est pas une prévision de dégâts par tour.

## 6. Ce que ça change dans le dépôt

- `data/catalog/encounter_copies.json` est commité (23 Ko, budget des données : 1,63 Mo / 2 Mo) et
  lisible hors ligne comme les autres catalogues.
- Aucun C# n'est touché ici : `ThreatModel` est modifié par la tâche qui branche les copies. Le
  contrat de lecture est le JSON ci-dessus ; `max_instances` est la borne, la distribution donne le
  mode.
- Cas de test qui doit changer de verdict : `EXOSKELETONS_WEAK` → `MaxHit` = 3 × 8 = **24** à A0
  (aujourd'hui 8) ; `SCROLLS_OF_BITING_NORMAL` → 4 × 14 = **56** (aujourd'hui 14).
