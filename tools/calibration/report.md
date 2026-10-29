# Calibration des poids de G sur les runs réels

Généré par `tools/calibration/fit_weights.py` le 2026-10-29T14:36:05Z — **ne pas éditer à la main**.

> **Corrélation, pas causalité.** Les features décrivent l'état du joueur ; elles sont en
> partie la conséquence de ses choix passés et de la chance. Les parts ci-dessous disent
> « quelle dimension distingue les runs qui gagnent », pas « quel levier fait gagner ».

## 1. Design d'observation

L'index ne contient **pas** d'instantané du deck à chaque décision : seulement le deck
final. Pour obtenir un état par acte, on reconstruit **vers l'arrière** (deck final moins
les cartes prises après l'acte) — la méthode que docs/29 §2.2 impose, l'accumulation avant
ne refermant que sur ~11 % des joueurs-runs.

Deux règles rendent la mesure honnête :

1. **État de fin d'acte, runs survivants seulement.** Pour l'acte *k*, on ne garde que les
   runs qui ont **survécu** à l'acte *k* (`acte atteint > k`). Sinon les PV du dernier
   enregistrement sont ceux de la mort et « Survie » prédirait la mort par tautologie.
2. **Aucun regard sur la suite** : les données utilisées pour l'acte *k* sont le deck, les
   reliques et les PV **à la fin de l'acte *k*** ; la cible (`win`) est postérieure.

Conséquence : **l'acte 3 n'est pas identifiable**. Aucun run ne « survit à l'acte 3 » sinon
en gagnant, donc l'échantillon n'a plus qu'une seule issue. Le rapport ne propose rien pour
l'acte 3 et le dit dans `weights.json` (`not_identifiable`).

## 2. Échantillon

| | valeur |
|---|---|
| index | `data/index/runs-v0.107.1.jsonl.gz` |
| runs lus (échantillon systématique) | 130,853 |
| runs dans le fit final (`kind=final`) | 71,634 |
| lignes `act` exploitables (run × acte, survivants) | 57,885 |
| filtre | solo, mode standard, personnage canonique, deck ≤ 80 cartes |
| runs écartés faute de PV exploitables | 6,510 |
| split | 80 / 20 train / test, graine 20261029 |

Taux de victoire par strate (train) :

| modèle | n train | n test | taux de victoire |
|---|---:|---:|---:|
| `final_dims` | 40,000 | 14,326 | 13.9 % |
| `act1_dims` | 31,221 | 7,805 | 25.8 % |
| `act2_dims` | 15,072 | 3,768 | 53.3 % |

Le taux global du corpus est 20,9 % (co-op inclus) ; ici le co-op est exclu et chaque
strate conditionne sur la profondeur atteinte, d'où des taux très différents d'une ligne
à l'autre. Un AUC ne se compare donc **qu'à l'intérieur d'une même strate**.

## 3. Ce que la data dit

### 3.1 Les quatre dimensions résument-elles les mesures brutes ?

| modèle | n train | AUC test | accuracy | majorité | Brier | McFadden R² |
|---|---:|---:|---:|---:|---:|---:|
| `final_dims` | 40,000 | 0.782 | 0.861 | 0.859 | 0.104 | 0.159 |
| `final_dims_depth` | 40,000 | 0.957 | 0.908 | 0.862 | 0.058 | 0.596 |
| `final_raw` | 15,000 | 0.962 | 0.916 | 0.858 | 0.055 | 0.608 |
| `act1_dims` | 31,221 | 0.688 | 0.749 | 0.744 | 0.174 | 0.077 |
| `act1_raw` | 15,000 | 0.737 | 0.758 | 0.742 | 0.167 | 0.121 |
| `act2_dims` | 15,072 | 0.656 | 0.618 | 0.537 | 0.230 | 0.054 |
| `act2_raw` | 15,000 | 0.675 | 0.634 | 0.539 | 0.226 | 0.071 |

`*_dims` = 4 dimensions + ascension + personnage ; `*_raw` = 14 mesures brutes + ascension
(+ acte atteint pour `final_raw`) + personnage. Écart d'AUC faible = les dimensions
résument l'essentiel ; écart fort = elles jettent du signal.

### 3.2 Poids par acte — coefficients libres, normalisés

| acte | dimension | code | data (`β` par point) | IC 95 % (grossier) | data (`β·σ`) |
|---|---|---:|---:|---|---:|
| 1 | survie | 40 % | 92 % | 91 % – 94 % | 87 % |
| 1 | offense | 30 % | 0 % | 0 % – 0 % | 0 % |
| 1 | consistance | 20 % | 0 % | 0 % – 0 % | 0 % |
| 1 | couverture | 10 % | 8 % | 6 % – 9 % | 13 % |
| 2 | survie | 30 % | 80 % | 76 % – 85 % | 74 % |
| 2 | offense | 35 % | 0 % | 0 % – 0 % | 0 % |
| 2 | consistance | 20 % | 19 % | 15 % – 22 % | 23 % |
| 2 | couverture | 15 % | 1 % | 0 % – 4 % | 2 % |
| 3 | — | — | **non identifiable** (aucun run ne survit à l'acte 3 sauf en gagnant) | — | — |

État de **fin de run** (indicatif, voir §5 : les PV y sont contaminés par la mort) :

| fin | dimension | part (`β`) | IC 95 % |
|---|---|---:|---|
| fin | survie | 39 % | 36 % – 42 % |
| fin | offense | 0 % | 0 % – 0 % |
| fin | consistance | 0 % | 0 % – 0 % |
| fin | couverture | 61 % | 58 % – 64 % |

`β` par point = effet marginal sur le log-odds d'**un point** de dimension (les quatre
dimensions sont sur la même échelle 0..100, donc comparables) ; `β·σ` = contribution à la
variance du logit, qui tient compte de la dispersion réellement observée.

### 3.3 Second estimateur : la forme de G imposée (poids ≥ 0, somme = 1)

Le modèle précédent laisse les quatre coefficients libres. Ici on **impose la structure du
code** : `P(win) = σ(a · Σ w_i·dim_i + contrôles)` avec `w` sur le simplexe. C'est exactement
la forme d'`ActWeights` ; si les deux méthodes tombent d'accord, la conclusion ne dépend pas
du choix de spécification.

| acte | survie | offense | consistance | couverture | échelle `a` | AUC test (indice) | AUC test (libre) |
|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | 81 % | 5 % | 5 % | 10 % | 0.65 | 0.688 | 0.688 |
| 2 | 71 % | 3 % | 22 % | 5 % | 0.73 | 0.652 | 0.656 |
| fin | 7 % | 2 % | 3 % | 88 % | 0.39 | 0.687 | 0.782 |

### 3.4 Taux de victoire par quartile (la lecture la plus directe)

Chaque ligne coupe l'échantillon en quatre parts égales de la mesure et donne le taux de
victoire de chaque part — aucun modèle, aucune normalisation : c'est la donnée brute.

**Acte 1** — 39,026 runs ayant survécu à l'acte 1, taux de victoire global 25.8 %.

| mesure | Q1 | Q2 | Q3 | Q4 | seuils |
|---|---:|---:|---:|---:|---|
| Survie | 12 % | 20 % | 30 % | 44 % | 40.0 / 46.0 / 52.0 |
| Offense | 27 % | 24 % | 25 % | 28 % | 19.0 / 25.0 / 31.0 |
| Consistance | 25 % | 22 % | 25 % | 33 % | 61.0 / 65.0 / 70.0 |
| Couverture | 23 % | 28 % | 24 % | 29 % | 29.0 / 37.0 / 53.0 |
| bloc/tour (brut) | 12 % | 20 % | 30 % | 42 % | 6.6 / 8.8 / 11.1 |
| dégâts/tour (brut) | 27 % | 23 % | 25 % | 29 % | 7.7 / 10.6 / 14.2 |
| PV au moment de la mesure | 20 % | 23 % | 27 % | 33 % | 15.0 / 27.0 / 41.0 |
| pioche/tour (brut) | 15 % | 23 % | 29 % | 37 % | 5.7 / 6.3 / 7.2 |
| énergie/tour (brut) | 14 % | 22 % | 30 % | 37 % | 3.4 / 3.8 / 4.3 |
| taille de deck | 26 % | 22 % | 24 % | 33 % | 19.0 / 22.0 / 25.0 |
| scaling (brut) | 26 % | — | 26 % | 25 % | 0.0 / 0.0 / 3.0 |
| malédictions | 26 % | 24 % | — | 28 % | 0.0 / 1.0 / 1.0 |

**Acte 2** — 18,840 runs ayant survécu à l'acte 2, taux de victoire global 53.3 %.

| mesure | Q1 | Q2 | Q3 | Q4 | seuils |
|---|---:|---:|---:|---:|---|
| Survie | 38 % | 50 % | 59 % | 69 % | 34.0 / 39.0 / 44.0 |
| Offense | 58 % | 54 % | 51 % | 50 % | 15.0 / 20.0 / 26.0 |
| Consistance | 47 % | 51 % | 55 % | 61 % | 56.0 / 63.0 / 68.0 |
| Couverture | 56 % | 53 % | 50 % | 54 % | 32.0 / 44.0 / 57.0 |
| bloc/tour (brut) | 44 % | 53 % | 55 % | 62 % | 8.4 / 10.5 / 12.8 |
| dégâts/tour (brut) | 58 % | 52 % | 52 % | 51 % | 7.9 / 11.4 / 15.4 |
| PV au moment de la mesure | 41 % | 49 % | 56 % | 68 % | 16.0 / 30.0 / 45.0 |
| pioche/tour (brut) | 44 % | 51 % | 56 % | 63 % | 6.2 / 6.8 / 7.8 |
| énergie/tour (brut) | 47 % | 52 % | 56 % | 60 % | 3.8 / 4.2 / 4.8 |
| taille de deck | 57 % | 52 % | 52 % | 51 % | 25.0 / 28.0 / 32.0 |
| scaling (brut) | 55 % | — | 53 % | 50 % | 0.0 / 0.0 / 4.0 |
| malédictions | 57 % | 54 % | 49 % | 45 % | 0.0 / 1.0 / 2.0 |

### 3.5 Corrélations entre dimensions

| paire | acte 1 | acte 2 |
|---|---:|---:|
| survie × offense | +0.05 | +0.00 |
| survie × consistance | +0.12 | +0.12 |
| survie × couverture | +0.04 | +0.03 |
| offense × consistance | +0.09 | -0.00 |
| offense × couverture | +0.31 | +0.29 |
| consistance × couverture | -0.08 | -0.12 |

Corrélations faibles : la colinéarité n'explique **pas** les poids ci-dessus. Ce n'est donc
pas un artefact de calcul partagé, mais bien un contenu informationnel différent.

### 3.6 Signal univarié de chaque dimension (AUC seule)

| dimension | acte 1 | acte 2 | acte 3 (indicatif) | fin de run |
|---|---:|---:|---:|---:|
| survie | 0.684 | 0.639 | 0.614 | 0.541 |
| offense | 0.500 | 0.459 | 0.614 | 0.360 |
| consistance | 0.531 | 0.561 | 0.534 | 0.407 |
| couverture | 0.523 | 0.492 | 0.557 | 0.684 |

0,5 = aucune information. Ces AUC ne dépendent d'aucun ajustement multivarié : elles sont
stables, mais elles ne départagent pas deux dimensions corrélées.

Mêmes AUC pour les mesures brutes (ce que les dimensions agrègent) :

| mesure brute | acte 1 | acte 2 | fin de run |
|---|---:|---:|---:|
| bloc/tour | 0.673 | 0.576 | 0.794 |
| dégâts/tour | 0.514 | 0.471 | 0.560 |
| pioche/tour | 0.624 | 0.590 | 0.775 |
| énergie/tour | 0.632 | 0.558 | 0.793 |
| PV | 0.569 | 0.618 | 0.814 |
| PV max | 0.528 | 0.517 | 0.655 |
| taille de deck | 0.526 | 0.474 | 0.844 |
| malédictions | 0.515 | 0.462 | 0.512 |
| scaling | 0.485 | 0.475 | 0.591 |

C'est le point le plus utile du rapport : **bloc/tour, pioche/tour, énergie/tour et PV**
séparent les gagnants, **dégâts/tour non** (et légèrement à l'envers à l'acte 2).

### 3.7 Coefficients détaillés des modèles à dimensions

| modèle | terme | β (standardisé) | SE | IC 95 % | β brut (par point) |
|---|---|---:|---:|---|---:|
| `act1_dims` | intercept | -1.181 | 0.014 | -1.209 – -1.153 | -1.1809 |
| `act1_dims` | s_survie | +0.739 | 0.016 | +0.708 – +0.770 | +0.0898 |
| `act1_dims` | s_offense | -0.024 | 0.015 | -0.053 – +0.005 | -0.0030 |
| `act1_dims` | s_consistance | -0.035 | 0.015 | -0.064 – -0.006 | -0.0047 |
| `act1_dims` | s_couverture | +0.111 | 0.015 | +0.081 – +0.140 | +0.0073 |
| `act1_dims` | asc | -0.027 | 0.015 | -0.057 – +0.002 | -0.0071 |
| `act1_dims` | char_silent | +0.146 | 0.017 | +0.112 – +0.180 | +0.3502 |
| `act1_dims` | char_defect | +0.134 | 0.018 | +0.099 – +0.170 | +0.3400 |
| `act1_dims` | char_necrobinder | +0.026 | 0.017 | -0.007 – +0.060 | +0.0691 |
| `act1_dims` | char_regent | +0.081 | 0.016 | +0.049 – +0.114 | +0.2162 |
| `act2_dims` | intercept | +0.143 | 0.017 | +0.110 – +0.176 | +0.1429 |
| `act2_dims` | s_survie | +0.510 | 0.018 | +0.474 – +0.546 | +0.0732 |
| `act2_dims` | s_offense | -0.131 | 0.019 | -0.168 – -0.094 | -0.0171 |
| `act2_dims` | s_consistance | +0.161 | 0.019 | +0.125 – +0.197 | +0.0171 |
| `act2_dims` | s_couverture | +0.016 | 0.019 | -0.021 – +0.054 | +0.0011 |
| `act2_dims` | asc | -0.030 | 0.018 | -0.066 – +0.005 | -0.0081 |
| `act2_dims` | char_silent | +0.138 | 0.022 | +0.095 – +0.182 | +0.3305 |
| `act2_dims` | char_defect | +0.070 | 0.022 | +0.026 – +0.114 | +0.1770 |
| `act2_dims` | char_necrobinder | +0.065 | 0.021 | +0.024 – +0.106 | +0.1767 |
| `act2_dims` | char_regent | +0.066 | 0.021 | +0.024 – +0.107 | +0.1738 |
| `final_dims` | intercept | -2.232 | 0.019 | -2.270 – -2.194 | -2.2323 |
| `final_dims` | s_survie | +0.262 | 0.017 | +0.228 – +0.296 | +0.0321 |
| `final_dims` | s_offense | -0.751 | 0.018 | -0.786 – -0.716 | -0.1100 |
| `final_dims` | s_consistance | -0.331 | 0.015 | -0.361 – -0.301 | -0.0450 |
| `final_dims` | s_couverture | +0.803 | 0.018 | +0.768 – +0.839 | +0.0492 |
| `final_dims` | asc | -0.407 | 0.017 | -0.441 – -0.374 | -0.1043 |
| `final_dims` | char_silent | -0.089 | 0.020 | -0.128 – -0.050 | -0.2093 |
| `final_dims` | char_defect | +0.155 | 0.020 | +0.115 – +0.195 | +0.4044 |
| `final_dims` | char_necrobinder | -0.146 | 0.020 | -0.186 – -0.106 | -0.3775 |
| `final_dims` | char_regent | -0.083 | 0.019 | -0.120 – -0.046 | -0.2249 |

Un intervalle qui contient 0 = dimension non distinguable du bruit **à autres dimensions
égales**. Ce n'est pas « la dimension ne compte pas » : c'est « ces données ne séparent pas
son effet de celui des trois autres ».

## 4. Contradictions avec les poids du code — à arbitrer

- **Acte 1, survie** : sous-pondérée par la data — 40 % dans le code contre 92 % (IC 95 % 91 % – 94 %). Écart ≥ 15 points.
- **Acte 1, offense** : coefficient **négatif** (-0.0030 par point ; part data 0 % contre 30 % dans le code). La data ne soutient pas un poids positif.
- **Acte 1, consistance** : coefficient **négatif** (-0.0047 par point ; part data 0 % contre 20 % dans le code). La data ne soutient pas un poids positif.
- **Acte 2, survie** : sous-pondérée par la data — 30 % dans le code contre 80 % (IC 95 % 76 % – 85 %). Écart ≥ 15 points.
- **Acte 2, offense** : coefficient **négatif** (-0.0171 par point ; part data 0 % contre 35 % dans le code). La data ne soutient pas un poids positif.

Les deux constats les plus solides, présents dans **les deux estimateurs** :

1. **Survie domine** ce que le code lui accorde : 92 % de la data
   à l'acte 1 (code : 40 %) et 80 % à l'acte 2 (code : 30 %).
   L'AUC univariée de Survie est 0,68 à l'acte 1,
   celle du bloc/tour brut 0,67 : c'est bien la **défense**, pas la
   fortune des PV, qui sépare les runs.
2. **Offense, telle que le code la mesure, ne sépare rien** : AUC univariée
   0,50 à l'acte 1, 0,46 à l'acte 2 ; dégâts/tour
   brut 0,51 et 0,47. Deux lectures possibles,
   et ce corpus **ne permet pas de trancher** : soit la défense est réellement le facteur
   dominant de ce patch, soit la dimension Offense mesure autre chose que la puissance de
   mise à mort — elle divise par la taille du deck et suppose des cartes jouées au hasard,
   ce qui pénalise les decks qui ajoutent des pouvoirs et de l'utilité.
3. **La Consistance rate son propre signal** : ses composantes valent mieux qu'elle
   (pioche/tour 0,62 à l'acte 1 contre 0,53
   pour la dimension ; 0,59 contre 0,56 à
   l'acte 2), parce que la pénalité de taille et la saturation diluent l'information.

## 5. Ce que la data ne dit pas

- **Biais de sélection / causalité inversée.** L'état observé est postérieur aux choix et
  antérieur à l'issue, mais il reste le produit des choix passés et de la chance (butin,
  événements). Un coefficient positif dit « les runs qui ont cette qualité gagnent plus
  souvent », jamais « ajouter cette qualité fait gagner ».
- **Pas de contrefactuel.** On ne voit jamais le même deck avec et sans la carte : aucun
  coefficient de ce rapport n'est un effet d'une décision.
- **Fin de run contaminée.** Dans le modèle `final_*`, les PV sont ceux du dernier
  enregistrement de décision : pour une run morte, c'est souvent très bas. La part de
  Survie y est donc gonflée par la mort elle-même. C'est pourquoi les poids proposés
  viennent des modèles par acte (état pré-outcome), pas du modèle de fin de run.
- **PV approximatifs** : `pl[].hp` est null dans l'index, la valeur vient d'une décision
  (pas d'un combat) ; les PV d'entrée de l'acte 1 sont inconnus.
- **Boss réel inconnu** : la menace est la médiane des boss de l'acte (règle du C# quand
  `BossId` est absent). Deux runs du même acte sont donc évaluées contre la même référence.
- **Améliorations ignorées** : l'index ne porte pas le niveau des cartes ; tout est chiffré
  non amélioré (biais de niveau, pas de classement, mais il déplace les saturations).
- **Reliques** : équivalents par tags (ordre de grandeur du C#), pas des valeurs mesurées.
- **Co-op exclu** (39 % du corpus) : ces poids ne valent que pour le solo.
- **Une seule version** (v0.107.1) ; le jeu public est déjà en v0.111.0.
- **Aucune validation temporelle** : pas de split par date, seulement un split aléatoire —
  les runs d'un même jour peuvent se ressembler (même méta, mêmes joueurs).

## 6. Écarts avec le calcul C# (assumés) et validation

- améliorations inconnues : l'index ne porte pas le niveau des cartes (aucun suffixe '+' dans les decks). Toutes les cartes sont chiffrées NON améliorées — dégâts/bloc réels ~15-25 % plus hauts sur les decks qui améliorent. Biais de niveau, pas de classement.
- PV : `pl[].hp` est null dans l'index ; on prend les PV du DERNIER enregistrement de décision du joueur (donc pas forcément les PV de fin de run, et jamais ceux d'un combat).
- menace : le boss réel n'est pas dans l'index ; on utilise la MÉDIANE des boss de l'acte comme le fait le C# quand `ctx.BossId` est inconnu (IsActMedian = true).
- statuts du tour 1 (Vulnérable/Faiblesse) non connus → PV effectifs = PV brut borné à MaxHp.
- reliques : équivalents par tags, comme le C#, mais sur la liste finale (ou reconstruite) — les tags `maxhp` ne sont pas ajoutés aux PV (déjà dans MaxHp, même règle que le C#).
- arrondis : le C# arrondit chaque dimension avec MidpointRounding.AwayFromZero ; idem ici.
- X-cost : énergie supposée 1 comme le C#, donc sous-estimation assumée des cartes X.

**Validation croisée avec le moteur C#** : sur 60 decks réels (actes et ascensions
variés), les quatre dimensions et les sept mesures intermédiaires calculées ici sont
**identiques** à `PlayerStrength.G` du moteur (`dotnet run` sur le vrai `Ultima.Engine`,
660 champs comparés, 0 écart). Les écarts listés ci-dessus sont donc ceux du modèle,
pas des erreurs de réimplémentation.

## 7. Reproduire

```bash
python3 -I tools/calibration/features.py --state both --stride 4 --out /tmp/features.jsonl
python3 -I tools/calibration/fit_weights.py --features /tmp/features.jsonl
```

`features.py` lit `data/index/runs-v0.107.1.jsonl.gz` (non commité) et `data/catalog/*.json`
(commités) ; `fit_weights.py` écrit `data/calibration/weights.json` et ce rapport.

