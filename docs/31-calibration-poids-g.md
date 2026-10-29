# 31 — Calibration des poids de G sur les runs réels (S15)

> Écrit le 2026-10-29. À lire avec `docs/27` §7bis (la forme de G), `docs/28` §2.2 (l'index de runs)
> et `docs/29` §2.2 (pourquoi on reconstruit un deck **vers l'arrière**).
> **Aucun fichier C# n'est modifié par cette tâche** : les poids ci-dessous sont une proposition
> soumise à arbitrage, `StrengthWeights.Act1/Act2/Act3` reste la référence à l'exécution.

## 1. Objectif

Le code choisit les poids de G à la main (`Act1 = 0,40/0,30/0,20/0,10`, `Act2 = 0,30/0,35/0,20/0,15`,
`Act3 = 0,30/0,40/0,15/0,15`) et le dit lui-même : « elles ne sont pas encore calibrées sur le
backtest ». Cette tâche les confronte aux 785 121 runs réels de v0.107.1, avec un protocole qui
essaie de ne pas se mentir.

Livrables : `tools/calibration/` (features, ajustement, autotest), `data/calibration/weights.json`
(12,6 Ko, commité), `tools/calibration/report.md` (généré).

## 2. Protocole

| étape | choix | pourquoi |
|---|---|---|
| échantillon | 130 853 runs lus (1 sur 6, systématique), **74 841** retenus dont **71 634** avec des PV exploitables | solo + mode standard + personnages canoniques + deck ≤ 80 cartes ; le co-op (39 % du corpus) a une autre économie |
| état par acte | deck final **moins** les cartes prises après l'acte | l'index n'a que le deck final ; l'accumulation avant ne referme que sur ~11 % des runs (docs/29 §2.2) |
| échantillon par acte | runs ayant **survécu** à l'acte *k* | sinon les PV du dernier enregistrement sont ceux de la mort : « Survie » prédirait la mort par tautologie |
| cible | `win` (postérieure à l'état) | aucun regard sur la suite, sauf le modèle `final_*` gardé comme contre-exemple |
| modèle 1 | logistique `win ~ 4 dimensions + ascension + personnage`, IRLS, L2 = 1 | les parts = coefficients bruts normalisés = la forme d'`ActWeights` |
| modèle 2 | indice `win ~ σ(a·Σ w_i·dim_i + contrôles)`, `w` sur le simplexe | impose la structure du code (poids ≥ 0, somme 1) : si les deux tombent d'accord, la conclusion ne dépend pas de la spécification |
| contrôles | ascension, personnage ; `acte atteint` seulement pour `final_*` | l'acte atteint est postérieur à l'état, donc mauvais contrôle pour les actes |
| split | 80/20 aléatoire, graine 20261029 | pas de split temporel : l'index n'a pas de date fiable par run |
| autotest | 63 vérifications | formules, reconstruction, récupération de coefficients connus |

**Validation croisée avec le moteur** : sur 60 decks réels (actes et ascensions variés), les 4
dimensions et 7 mesures intermédiaires calculées en Python sont **identiques** à
`PlayerStrength.G` exécuté par un vrai `dotnet run` sur `Ultima.Engine` — 660 champs comparés,
0 écart. Les écarts avec le C# sont donc ceux du modèle (améliorations inconnues, boss médian,
statuts du tour 1 absents), pas des erreurs de réimplémentation.

**Ce qui n'est pas identifiable.** L'acte 3 ne peut pas être calibré par ce design : aucun run ne
« survit à l'acte 3 » sans gagner, donc l'échantillon pré-outcome n'a qu'une seule issue.
`weights.json` le déclare (`not_identifiable.act_3`) et le code garde ses constantes pour cet acte.

## 3. Résultats

### 3.1 Pouvoir prédictif

| modèle | n train / test | AUC test | McFadden R² | taux de victoire |
|---|---|---:|---:|---:|
| `act1_dims` (fin d'acte 1, survivants) | 31 221 / 7 805 | **0,688** | 0,077 | 25,8 % |
| `act1_raw` (14 mesures brutes) | 15 000 / 7 805 | 0,737 | 0,121 | 25,7 % |
| `act2_dims` (fin d'acte 2, survivants) | 15 072 / 3 768 | **0,656** | 0,054 | 53,3 % |
| `act2_raw` | 15 000 / 3 768 | 0,675 | 0,071 | 53,3 % |
| `final_dims` (fin de run, indicatif) | 40 000 / 14 326 | 0,782 | 0,159 | 13,9 % |
| `final_raw` | 15 000 / 14 326 | 0,962 | 0,608 | 14,1 % |

Les quatre dimensions **résument correctement** les mesures brutes à l'acte 2 (0,656 contre 0,675)
et un peu moins bien à l'acte 1 (0,688 contre 0,737). Le modèle de fin de run atteint 0,96 avec
l'acte atteint en contrôle : à ce stade, « savoir jusqu'où la run est allée » explique presque tout
— c'est une signature, pas une prévision, d'où son statut purement indicatif.

### 3.2 Poids proposés (parts normalisées, somme = 1)

| acte | méthode | Survie | Offense | Consistance | Couverture |
|---|---|---:|---:|---:|---:|
| 1 | code | 40 % | 30 % | 20 % | 10 % |
| 1 | coefficients libres (IC 95 %) | **92 %** (91–94) | 0 % (0–0,4) | 0 % (0–0) | 8 % (6–9) |
| 1 | indice contraint | 81 % | 5 % | 5 % | 10 % |
| 2 | code | 30 % | 35 % | 20 % | 15 % |
| 2 | coefficients libres (IC 95 %) | **80 %** (76–85) | 0 % (0–0) | 19 % (15–22) | 1 % (0–4) |
| 2 | indice contraint | 71 % | 3 % | 22 % | 5 % |
| 3 | code | 30 % | 40 % | 15 % | 15 % |
| 3 | data | **non identifiable** (aucun run ne survit à l'acte 3 sauf en gagnant) | | | |

Les deux estimateurs concordent sur l'ordre : **Survie ≫ Consistance ≥ Couverture > Offense**.

### 3.3 La donnée brute, sans modèle

Taux de victoire par quartile, runs ayant survécu à l'acte :

| mesure | acte 1 (Q1→Q4) | acte 2 (Q1→Q4) |
|---|---|---|
| Survie | 12 → 20 → 30 → **44 %** | 38 → 50 → 59 → **69 %** |
| bloc/tour | 12 → 20 → 30 → **42 %** | 44 → 53 → 55 → **62 %** |
| pioche/tour | 15 → 23 → 29 → **37 %** | 44 → 51 → 56 → **63 %** |
| énergie/tour | 14 → 22 → 30 → **37 %** | 47 → 52 → 56 → **60 %** |
| PV | 20 → 23 → 27 → 33 % | 41 → 49 → 56 → **68 %** |
| Offense | 27 → 24 → 25 → 28 % | 58 → 54 → 51 → **50 %** |
| dégâts/tour | 27 → 23 → 25 → 29 % | 58 → 52 → 52 → 51 % |
| Consistance | 25 → 22 → 25 → 33 % | 47 → 51 → 55 → 61 % |
| Couverture | 23 → 28 → 24 → 29 % | 56 → 53 → 50 → 54 % |
| taille de deck | 26 → 22 → 24 → 33 % | 57 → 52 → 52 → 51 % |
| malédictions | 26 → 24 → — → 28 % | 57 → 54 → 49 → **45 %** |

(Taux de victoire global : 25,8 % à l'acte 1, 53,3 % à l'acte 2 — la strate change tout, d'où
l'interdiction de comparer un taux d'un acte à l'autre.)

AUC univariées (0,5 = aucune information) :

| mesure | acte 1 | acte 2 | fin de run |
|---|---:|---:|---:|
| Survie | 0,684 | 0,639 | 0,541 |
| bloc/tour | 0,673 | 0,576 | 0,794 |
| pioche/tour | 0,624 | 0,590 | 0,775 |
| énergie/tour | 0,632 | 0,558 | 0,793 |
| PV | 0,569 | 0,618 | 0,814 |
| Offense | 0,500 | 0,459 | 0,360 |
| dégâts/tour | 0,514 | 0,471 | 0,560 |
| Consistance | 0,531 | 0,561 | 0,407 |
| Couverture | 0,523 | 0,492 | 0,684 |

Les corrélations entre dimensions sont faibles (|r| ≤ 0,31 sur les deux actes) : ce n'est donc pas
un artefact de colinéarité, les quatre dimensions portent bien des informations différentes.

## 4. Trois constats, et ce qu'ils ne disent pas

**1. La Survie est très largement sous-pondérée.** 30–40 % dans le code, 71–92 % dans la data selon
l'estimateur. Le gradient de Survie suit celui du bloc/tour brut (0,673 à l'acte 1) bien plus que
celui des PV (0,569) : c'est la **défense**, pas la fortune des points de vie, qui sépare les runs.

**2. L'Offense, telle que le code la mesure, ne sépare rien.** Coefficient négatif aux actes 1 et 2,
AUC univariée 0,500 et 0,459, et les dégâts/tour bruts ne font pas mieux (0,514 et 0,471). Deux
lectures, que ce corpus **ne permet pas de trancher** :

- soit la défense est réellement le facteur dominant de ce patch à ces ascensions ;
- soit la dimension mesure autre chose que la puissance de mise à mort : elle divise par la taille
  du deck (`Σdégâts / N × D × taux`), suppose des cartes jouées **au hasard** et ignore les
  améliorations — ce qui pénalise mécaniquement les decks qui ajoutent des pouvoirs et de l'utilité,
  c'est-à-dire la façon dont on gagne réellement à Spire.

Corriger le poids d'une dimension qui ne mesure pas ce qu'elle prétend mesurer ne réglerait rien :
**il faut d'abord arbitrer la formule d'Offense** (par exemple : dégâts « utiles » plafonnés par les
PV du boss, ou temps de mise à mort estimé, plutôt que densité). C'est une proposition, pas une
décision : elle sort du périmètre de cette tâche.

**3. La Consistance rate son propre signal.** Ses composantes valent mieux qu'elle : pioche/tour
0,624 contre 0,531 pour la dimension à l'acte 1 ; 0,590 contre 0,561 à l'acte 2. La pénalité de
taille (idéal ≤ 25 cartes) et la saturation diluent l'information — d'autant que la taille de deck
n'a pas d'effet stable (AUC 0,526 à l'acte 1, 0,474 à l'acte 2).

### Ce que la data ne dit pas

- **Aucune causalité.** L'état observé est la conséquence des choix passés et du hasard (butin,
  événements, cartes proposées). Un coefficient dit « les runs qui ont cette qualité gagnent plus
  souvent », jamais « ajouter cette qualité fait gagner ». Il n'y a pas de contrefactuel : on ne voit
  jamais le même run avec et sans la carte.
- **Fin de run contaminée.** Dans `final_*`, les PV sont ceux du dernier enregistrement de décision ;
  pour une run morte c'est souvent très bas. C'est pourquoi les poids proposés viennent des modèles
  par acte, pas du modèle de fin de run (où Couverture sort à 61 % — un artefact).
- **PV approximatifs, boss médian, améliorations absentes, reliques en équivalents de tags.** Détail
  complet dans `report.md` §5 et dans `features.py` (`DEVIATIONS`).
- **Solo seulement**, v0.107.1 seulement, split aléatoire (pas temporel).

## 5. Décision demandée (et non prise ici)

`weights.json` contient `recommendation` :

1. ne pas appliquer les parts brutes telles quelles ;
2. actes 1–2 : la Survie doit peser nettement plus que 30–40 % — c'est le seul écart que les deux
   estimateurs confirment avec des IC serrés ;
3. acte 3 : garder les constantes du code (non identifiable) ;
4. Offense : revoir la **formule** avant le poids.

Le C# n'est pas modifié. `Ultima.Engine/Scoring/Strength/PlayerStrength.cs` garde
`StrengthWeights.Act1/Act2/Act3` ; une tâche ultérieure appliquera ce qui sera arbitré.

## 6. Reproduire

```bash
python3 -I tools/calibration/features.py --state both --stride 6 --out /tmp/features.jsonl
python3 -I tools/calibration/fit_weights.py --features /tmp/features.jsonl \
    --scanned 130853 --max-train 40000 --max-test 20000 --max-index-train 12000
python3 -I tools/calibration/selftest.py   # 63 vérifications, sans données
```

Durées mesurées : ~11 min pour les features (541 Mo compressés à décompresser), 34 s pour
l'ajustement complet, < 1 s pour l'autotest.
