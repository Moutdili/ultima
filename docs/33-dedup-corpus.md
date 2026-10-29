# 33 — Déduplication du corpus de runs (v0.107.1)

> Mesuré le 2026-10-29 sur `data/index/runs-v0.107.1.jsonl.gz` (785 121 runs, 19,0 M décisions
> de cartes). Outil : `tools/dedup.py`. Rapports machine : `docs/data/dedup-report-v0.107.1.json`
> (copie de `data/index/dedup-report.json`, gitignoré) et `docs/data/dedup-audit-sample100k.json`.
> Complète `docs/32` §3, qui annonçait « 38 % de doublons » sur 10 000 runs **gagnées**.

## 1. Ce qui est mesuré, et avec quelle signature

Une run est identifiée par son **contenu**, pas par son `run_hash` (tous différents) :

| Élément | Source dans l'index | Remarque |
|---|---|---|
| mode, ascension, nombre de joueurs | `m`, `asc`, `n` | une run co-op et une run solo ne se confondent pas |
| issue | `w`, `ab`, `kb` | gagner et mourir sur le même deck = deux parties |
| personnage, **deck trié**, **reliques triées** | `pl[]` | multiset ; les niveaux d'amélioration ne sont pas dans l'index (cf. §5) |
| PV / PV max | dernier `d[].hp` / `d[].mhp` du joueur | `pl[].hp` est **NULL** dans l'export (schéma 9) |
| **suite des décisions** | `d[]` → (type, acte, étage, type de salle, joueur, carte/relique/ancien **choisi**) | ~33 décisions par run en moyenne |

Trois niveaux sont calculés dans le même passage, pour mesurer la sensibilité au choix de signature :

| niveau | contenu | runs uniques | % retirés |
|---|---|---:|---:|
| `final` | état final seul (deck + reliques + PV + issue), sans trajectoire | 570 070 | 27,39 % |
| **`picks`** (défaut) | `final` + suite des choix | **574 569** | **26,82 %** |
| `strict` | `picks` + les cartes/reliques **proposées** à chaque décision | 575 784 | 26,66 % |

Les trois niveaux donnent 26,7–27,4 % : la déduplication n'est pas un artefact du niveau de détail.
Le fait que `strict` ne retire presque rien de plus que `picks` (1 215 runs) est une **preuve de
copie** : deux runs qui partagent déjà deck + reliques + PV + tous les choix partagent aussi les
mêmes offrandes, ce qui n'arrive pas par convergence.

La **première** occurrence de chaque signature est conservée (ordre du fichier source), les suivantes
sont comptées puis supprimées. Le hash de signature est un blake2b-80 bits sur un JSON canonique
(listes triées, séparateurs fixes) — collision négligeable à 785 k lignes.

## 2. Résultat global

| | valeur |
|---|---:|
| runs lues | 785 121 |
| **runs uniques** | **574 569 (73,18 %)** |
| runs retirées | 210 552 (26,82 %) |
| signatures distinctes (= groupes) | 574 569 |
| groupes avec ≥ 2 copies | 131 709 (22,92 % des signatures) |
| taille du plus gros groupe | **802** |
| taille moyenne d'un groupe multiple | 2,60 (médiane 2, p99 6) |

Distribution : 90 232 groupes de 2, 28 724 de 3, 10 863 de 4 → **98,6 % des groupes font 2 à 4
copies et portent 85,6 % des retraits**. La queue est mince mais lourde : 50 groupes de plus de
100 copies pèsent 12 080 retraits (5,7 %), dont un groupe de 802 copies (Ironclad asc. 10, abandon
après une seule décision).

Le chiffre de `docs/32` (38 %) est **exact mais spécifique aux victoires** : 36,51 % des runs
gagnées sont des doublons, contre 24,25 % des runs perdues. Sur tout le corpus, c'est 26,8 %.

## 3. Où sont les doublons

**Le co-op explique presque tout — et la cause est arithmétique.** Le taux de retrait par nombre de
joueurs suit exactement `(n−1)/n` :

| joueurs | runs | retraits | taux observé | `(n−1)/n` |
|---:|---:|---:|---:|---:|
| 1 (solo) | 475 845 | 29 109 | **6,1 %** | — |
| 2 | 183 060 | 93 466 | 51,1 % | 50,0 % |
| 3 | 85 961 | 57 616 | 67,0 % | 66,7 % |
| 4 | 38 552 | 28 970 | 75,1 % | 75,0 % |
| 5 | 1 180 | 949 | 80,4 % | 80,0 % |
| 6 | 330 | 276 | 83,6 % | 83,3 % |
| 7 | 168 | 144 | 85,7 % | 85,7 % |

Autrement dit : une session co-op est enregistrée **une fois par joueur** (chaque client soumet la
partie complète, avec les `player_stats` de tous les participants). Le fichier contient donc `n`
copies de la même partie, et la déduplication en garde une — c'est le comportement correct, pas une
fusion abusive. Le co-op pèse 39,4 % du corpus mais **86,2 % des retraits** ; après dédup il tombe à
22,25 %. Hors effet co-op, le doublon résiduel est de **6,1 % en solo** (re-uploads, relances).

Les runs sans aucune décision (mort ou abandon avant la première récompense) sont 19 415 (2,5 %)
avant, 2 089 après : 17 326 retraits, soit 8,2 % du total. Ces runs ne portent presque aucune
information et gonflent les groupes géants.

Par ascension :

| asc. | runs avant | part | runs après | part | % retirés | win rate avant → après |
|---:|---:|---:|---:|---:|---:|---|
| 0 | 145 590 | 18,5 % | 111 840 | 19,5 % | 23,2 % | 18,42 → 15,93 |
| 1 | 79 358 | 10,1 % | 56 227 | 9,8 % | 29,2 % | 31,80 → 28,15 |
| 5 | 45 661 | 5,8 % | 32 759 | 5,7 % | 28,3 % | 27,63 → 24,01 |
| 9 | 77 361 | 9,9 % | 58 450 | 10,2 % | 24,5 % | 9,24 → 7,48 |
| 10 | 180 107 | 22,9 % | 127 372 | 22,2 % | 29,3 % | 12,52 → 11,12 |

La **distribution d'ascension bouge peu** (± 1 pp), mais les win rates par ascension baissent de
1,4 à 3,7 pp : le corpus brut surestime la difficulté gagnable parce que les victoires sont les runs
les plus dupliquées.

## 4. Impact sur les statistiques de base

| métrique | avant | après | Δ |
|---|---:|---:|---:|
| runs | 785 121 | 574 569 | −26,8 % |
| joueurs | 1 263 321 | 750 959 | −40,6 % |
| décisions (toutes) | 25 486 197 | 14 583 745 | −42,8 % |
| décisions de cartes | 19 039 107 | 10 977 417 | −42,3 % |
| **win rate** | **20,92 %** | **18,15 %** | **−2,77 pp** |
| part co-op | 39,39 % | 22,25 % | −17,14 pp |
| décisions / run | 32,46 | 25,38 | −21,8 % |
| décisions / joueur | 20,17 | 19,42 | −3,7 % |
| taux de skip (cartes) | 28,34 % | 29,49 % | +1,15 pp |
| deck moyen | 26,49 | 25,61 | −0,88 carte |
| reliques moyennes | 10,82 | 10,25 | −0,57 |

Lecture : la déduplication **ne renverse aucune conclusion** mais corrige trois biais mesurables —
un win rate gonflé de 2,8 points, un co-op sur-représenté d'un facteur ~1,8, et un volume de
décisions gonflé de 43 % (chaque session co-op comptant une fois par joueur). Les grandeurs
« par joueur » bougent peu (−3,7 %) : la duplication est surtout une duplication de **runs
entières**, pas de joueurs.

## 5. La signature est un choix, pas une vérité

Contrôle fait sur l'**export brut** (`tools/dedup.py --audit-export … --audit-limit 100000`), qui
contient les seeds — absentes de l'index :

| mesure (100 000 runs) | valeur |
|---|---:|
| doublons de contenu | 25,07 % (cohérent avec 25,6 % sur les 20 000 premières lignes de l'index) |
| groupes multiples dont **toutes** les copies partagent la même seed | **96,72 %** (15 631 / 16 161) |
| groupes multiples à seeds différentes (= convergence, fusion à tort) | 530 groupes · 3 164 runs (3,2 %) |
| signatures en plus si l'on ajoute le niveau d'amélioration des cartes | **+2 sur 74 926** (0,003 %) |
| seeds produisant plusieurs runs distinctes | 585 sur 74 792 |

Ce que ça prouve et ce que ça ne prouve pas :

- **Ce qui est solide.** 97 % des groupes sont la même seed re-uploadée : ce sont de vraies copies,
  pas des builds convergents. L'ajout des niveaux d'amélioration ne scinde que 2 signatures — les
  copies sont identiques au bit près, l'index qui les ignore ne fusionne donc rien d'utile.
- **Ce qui reste un choix.** ~3 % des groupes fusionnent deux seeds différentes : deux joueurs ont
  produit le même contenu (même deck, mêmes reliques, mêmes PV, mêmes choix). La signature les
  traite comme des doublons. C'est le taux de fusion abusive mesurable ; il est faible mais non nul,
  et il est **plus élevé pour les runs courtes** (peu de décisions ⇒ peu de pouvoir discriminant) :
  le groupe de 802 copies est une run asc. 10 abandonnée après une décision, où « même contenu »
  veut dire « même relique de Neow et même mort ».
- **Ce que l'index ne peut pas voir.** Les PV finaux exacts (`pl[].hp` NULL ; on prend le dernier
  point de décision), les choix d'événement (non indexés), les potions (transitoires), les
  `rooms[]`/`tours par combat` (absents de l'index — seul l'export brut les a), `run_time`.
  Deux runs qui ne diffèrent que par un choix d'événement sont fusionnées.
- **Le seed n'est pas une clé de dédup** : 585 seeds donnent plusieurs runs distinctes (relances,
  co-op à plusieurs joueurs, choix différents). Il sert de **contrôle**, pas de signature.

Conséquence pratique : le chiffre « 26,8 % de doublons » est une borne **haute** de la déduplication
souhaitable (on préfère fusionner deux copies identiques que compter deux fois la même), avec ~3 %
de fusions abusives identifiées.

## 6. Ce que ça change pour la calibration

Le sous-corpus utilisé par S15/S17 (solo, mode standard, personnages canoniques Ironclad/Silent/
Defect/Necrobinder/Regent, deck ≤ 80) est **beaucoup moins touché** que le corpus global :

| | valeur |
|---|---:|
| runs du sous-corpus | 449 825 (57,3 % du corpus) |
| signatures distinctes | 421 864 |
| doublons retirés | 27 961 (**6,22 %**) |

Autrement dit, les 71 634 runs de fin de run utilisés par S17 sont gonflés d'environ **6 %**, pas de
38 % : le 38 % de `docs/32` venait des victoires, et les victoires co-op. Deux options pour la
recalibration :

1. travailler sur `data/index/deduped-v0.107.1.jsonl.gz` (même filtre appliqué ensuite) ;
2. garder l'index brut mais **pondérer chaque ligne par 1/taille du groupe** — équivalent, et cela
   préserve les choix de signature explicites.

## 7. Reproduire

```bash
# index dédupliqué + rapport (5-6 min, ~480 Mo)
python3 -I tools/dedup.py \
  --in  data/index/runs-v0.107.1.jsonl.gz \
  --out data/index/deduped-v0.107.1.jsonl.gz \
  --report data/index/dedup-report.json

# contrôle sur l'export brut : doublons = même seed ? (100 k runs, ~95 s)
python3 -I tools/dedup.py \
  --audit-export data/exports/runs-v0.107.1.jsonl.gz --audit-limit 100000

# vérification : re-dédupliquer le fichier dédupliqué doit donner 0 % de retraits
python3 -I tools/dedup.py --in data/index/deduped-v0.107.1.jsonl.gz --out /tmp/x.jsonl.gz --no-variants
```

L'index original n'est jamais modifié ; `data/index/` reste gitignoré (docs/28 §3).
