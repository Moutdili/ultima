# S20 — brancher les poids de G calibrés (repli sûr, preuve que G change)

Branche `hermes/s20-weights`, worktree `~/wt-s20`, depuis `main` `0118298`. La calibration S15/S17
avait produit des parts par acte mais le code gardait `Act1 = (0.40, 0.30, 0.20, 0.10)` : l'algo
n'utilisait pas ce que la data avait appris. Cette tâche branche le fichier, sans exception possible
et sans casser la reproductibilité.

## 1. Ce qui a été branché

`Ultima.Engine/Scoring/Strength/StrengthWeights.cs` (nouveau, les types `ActWeights` /
`StrengthWeights` y sont déplacés depuis `PlayerStrength.cs`) :

| élément | comportement |
|---|---|
| `StrengthWeights.Load(path?)` | lit `data/calibration/weights.json`, valide et renvoie un `WeightCalibration`. **Jamais d'exception** ; ne touche pas au cache (les tests peuvent valider un fichier arbitraire). |
| `StrengthWeights.Parse(json, path?)` | même contrat depuis une chaîne (tests, outils). |
| `StrengthWeights.Current` | cache statique `Lazy<WeightCalibration>` en `ExecutionAndPublication` : **un seul chargement par processus**. |
| `StrengthWeights.Source` | « calibré (n=71 634 runs, 2026-10-29) » ou « constantes par défaut ». |
| `StrengthWeights.Act1/Act2/Act3` | poids **effectifs** (calibrés si le fichier est valide). Les constantes restent accessibles via `DefaultAct1/2/3` et servent de repli. |
| `WeightCalibration.Injected(acts, libellé)` | poids explicites pour les tests/outils, avec leur provenance. |
| `DataPaths.CalibrationFile()` | localise le fichier (`ULTIMA_DATA`, voisin de `data/catalog`, remontée depuis l'assembly) ; null si absent. |

Validation appliquée (tout écart ⇒ constantes, raison dans `Problem`) :

- schéma exact `ultima.calibration.weights/1` ;
- quatre poids `survie/offense/consistance/couverture` numériques et finis, chacun dans [0, 1] ;
- somme à 1 ± 1e-3, puis **renormalisation** à 1 exactement (le fit arrondit à 4 décimales :
  0,8180 + 0 + 0,1798 + 0,0021 = 0,9999) ;
- `source.runs_final` > 0 et `generated_at` parseable (ils alimentent la provenance affichée) ;
- `not_identifiable.act_3` respecté : l'acte 3 garde les constantes même si un fit en proposait un.

Poids réellement appliqués (fichier du dépôt, 71 634 runs) :

| acte | Survie | Offense | Consistance | Couverture | somme |
|---|---:|---:|---:|---:|---:|
| 1 | 0,8513 | 0,1005 | 0 | 0,0482 | 1 |
| 2 | 0,8181 | 0 | 0,1798 | 0,0021 | 1 (après renormalisation de 0,9999) |
| 3 | 0,30 | 0,40 | 0,15 | 0,15 | constantes (non identifiable) |

`PlayerStrength` : `Weights` prend les poids effectifs par défaut, et `WeightSource` porte la
provenance (« poids calibrés sur 71 634 runs (2026-10-29) »). `PlayerStrengthCalculator` accepte un
`WeightCalibration? calibration` explicite pour les tests (les catalogues restent injectables comme
avant). L'explication ne change pas de libellés : chaque terme de dimension finit par
`poids calibrés sur 71 634 runs (2026-10-29) : 0.85` (ou `poids par défaut (constantes écrites à la
main) : 0.40`), les labels `Survie` / `Offense` / `Consistance` / `Couverture` sont intacts.

## 2. Preuve : G avant/après sur des decks réels

Dimensions inchangées (les poids ne recombinent que le total) ; mêmes entrées, mêmes catalogues.
« Avant » = constantes écrites à la main injectées via `WeightCalibration.Injected`.

Deck réel issu de `fixtures/saves/.../current_run.save` (Silent, 14 cartes, 2 reliques) :

| deck | acte | dims S/O/C/V | G avant | G après | Δ |
|---|---|---:|---:|---:|---:|
| Silent fixture 70/70 | 1 | 54 / 44 / 70 / 45 | 53 | 53 | 0 |
| Silent fixture **1/70** | 1 | 32 / 44 / 70 / 45 | 45 | **34** | −11 |
| Silent fixture 70/70 | 2 | 54 / 44 / 70 / 45 | 52 | **57** | +5 |

Autres decks (mêmes cartes que les tests, ids du catalogue) :

| deck | acte | dims S/O/C/V | G avant | G après | Δ |
|---|---|---:|---:|---:|---:|
| Ironclad starter (11 cartes) | 1 | 49 / 40 / 68 / 22 | 47 | 47 | 0 |
| Ironclad starter | 2 | 49 / 40 / 68 / 22 | 46 | **52** | +6 |
| 10 Defend | 1 | 62 / 0 / 69 / 5 | 39 | **53** | +14 |
| 10 Defend | 2 | 62 / 0 / 69 / 5 | 33 | **63** | +30 |
| Bash ×6 | 1 | 23 / 35 / 63 / 34 | 36 | **25** | −11 |
| Bash ×6 | 2 | 23 / 35 / 63 / 34 | 37 | **30** | −7 |
| mixte 20 cartes | 1 | 48 / 47 / 68 / 34 | 50 | **47** | −3 |
| mixte 20 cartes | 2 | 48 / 47 / 68 / 34 | 50 | **52** | +2 |

Lecture : à l'acte 1, la défense monte et l'offense baisse — exactement le sens des parts calibrées
(85 % / 10 %). À l'acte 2, la survie pèse encore 82 % mais la consistance remonte à 18 %, ce qui
récompense les decks propres. Le deck réel à 1 PV perd 11 points : c'est le prix assumé d'un poids
de Survie à 85 %, la donnée ne soutient pas l'ancien 40 %.

Effet de bord visible sur les notes de cartes (mêmes dimensions, autre combinaison) :

- **Crash Landing** dans 12 Big Bang (Regent, 70/70) : ΔG 17 → 6, fit 20 → 12, total 73 **A** → 65 **B**.
  La carte est purement offensive ; l'offense ne pèse plus que 10 % à l'acte 1.
- **Impervious** dans un deck couvert : fit 2 → 4, note B → **A** (survie 85 %). L'amplitude du fit
  (×2, plafond 20) n'a pas été retouchée : c'est ΔG qui a changé de valeur.

## 3. Tests

- `Ultima.Engine.Tests/Scoring/StrengthWeightsTests.cs` (11 tests) : fichier réel chargé (somme 1,
  actes 1..3, valeurs 85/10/0/5 et 82/0/18/0, provenance et date), cache unique, fichier absent,
  JSON corrompu (5 formes), poids négatif, somme ≠ 1, schéma inconnu, métadonnées manquantes,
  renormalisation d'un fichier valide à 0,9999, G par défaut = poids du cache, et **G change quand
  on injecte deux jeux de poids opposés** (dimensions identiques, totaux différents) — c'est la
  preuve que le branchement est effectif et pas seulement que le fichier est lu.
- Trois assertions existantes dépendaient des anciens poids, mises à jour avec justification :
  - `NeedTests.Hp_need_grows_as_hp_drops` : la monotonie était testée sur le **total**, qui inclut
    le fit (2·ΔG arrondi). Sous 85 % de survie, le fit varie de 2 à 3 points selon les PV et le
    total fait 67 → 73 → 78 → **76** alors que le besoin, lui, croît 1 → 7 → 10 → 10. Le test
    vérifie maintenant le terme L2 (son sujet) et garde `total(7 PV) > total(34 PV)`.
  - `FitTests.Same_card_scores_higher_when_the_need_is_not_covered` : fit résiduel 0..2 → 0..4
    (ΔG +1 → +2 : +3 de survie à 85 % valent +2,55) ; la note du deck couvert passe B → A, ce qui
    est la conséquence directe du nouveau poids.
  - `FitTests.Low_meta_card_reaches_A_through_fit` → renommé
    `Low_meta_offense_card_fit_shrinks_under_calibrated_weights` : Crash Landing ne peut plus
    atteindre A par le seul fit (73 A → 65 B) ; un test compagnon
    `Low_meta_block_card_still_reaches_max_fit` vérifie que l'acceptation tient toujours pour une
    carte qui remplit la dimension dominante (Taunt, C-tier, fit 20, A à PV bas).
- Suite complète : **157 tests passés, 0 échec** (`dotnet test Ultima.Engine.Tests -c Release`).
- `bash tools/hermes-verify.sh` : **PASS** (build warnings=errors, tests, champs interdits, stubs,
  contrats intacts).

## 4. Ce que ce n'est pas / non prouvé

- **Les poids viennent de runs peut-être dupliquées.** L'index v0.107.1 n'a pas d'identifiant de
  run fiable ; la recalibration (dédoublonnage) est prévue et remplacera le fichier sans toucher au
  code — c'est tout l'intérêt du chargement par fichier.
- **Corrélation, pas causalité.** Un poids dit « les runs qui ont cette qualité gagnent plus
  souvent », pas « ajouter cette qualité fait gagner ». Aucun contrefactuel n'est observé.
- **Acte 3 non identifiable** : aucun run ne survit à l'acte 3 sans gagner. Les constantes y sont
  conservées, pas calibrées.
- **Solo, v0.107.1, split aléatoire** : pas de validation temporelle, pas de co-op.
- **Le poids d'Offense à l'acte 2 vaut 0** parce que la data ne le soutient pas (AUC 0,48) ; ce
  n'est pas un oubli. Une carte offensive ne pèse donc presque plus dans G à l'acte 2.
- **L'échelle du fit (×2, plafond 20) date des anciens poids.** Elle n'a pas été recalibrée : les
  cartes offensives perdent du fit à l'acte 1 (Crash Landing A → B). C'est un arbitrage produit à
  faire (baisser le plafond, ou changer la note méta), pas une erreur de branchement.
- **Pas de rechargement à chaud** : la calibration est lue une fois par processus ; modifier
  `weights.json` exige un redémarrage (voulu : reproductibilité d'un run de scoring).
- Le fichier `data/calibration/weights.json` n'est **pas** modifié par cette tâche (lecture seule).

## 5. Fichiers touchés

- `Ultima.Engine/Scoring/Strength/StrengthWeights.cs` (nouveau) — `ActWeights`, `WeightCalibration`
  (validation + provenance), `StrengthWeights` (chargement, cache, repli).
- `Ultima.Engine/Scoring/Strength/PlayerStrength.cs` — types déplacés, `WeightSource`, injection
  `calibration`, suffixe de provenance dans les quatre termes. Aucun libellé changé.
- `Ultima.Engine/Scoring/Deck/DataPaths.cs` — `CalibrationFile()`.
- `Ultima.Engine.Tests/Scoring/StrengthWeightsTests.cs` (nouveau, 11 tests).
- `Ultima.Engine.Tests/Scoring/FitTests.cs`, `NeedTests.cs` — trois assertions dépendantes des
  anciens poids, justifiées ci-dessus.
- `docs/31-calibration-poids-g.md` §8 — ce qui est branché, et ce qui ne l'est pas (acte 3).

Hors périmètre et inchangés : `Ultima.Engine/Contracts/**`, `Ultima.Saves/**`, `overlay/**`,
`data/calibration/weights.json`.
