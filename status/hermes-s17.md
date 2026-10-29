# S17 — la dimension Offense mesurait mal : diagnostic, correction, recalibration

Branche `hermes/s17-offense`, worktree `~/wt-s17` (depuis `main` 446af2a). Le résultat de S15 était
net : sur 71 634 runs, Offense **ne séparait rien** (β négatif aux actes 1 et 2, AUC 0,500 / 0,459).
Deux lectures possibles — (a) la défense domine réellement, (b) la formule est fausse. Cette tâche
tranche par la mesure : **les deux sont vraies, mais (b) était réparable**.

## 1. Ce qui a été mesuré

`tools/calibration/diagnose_offense.py` (nouveau) rejoue **24 définitions candidates** sur le même
corpus que S15 : même filtre (solo, standard, personnages canoniques, deck ≤ 80), même reconstruction
arrière du deck, même stride 6, et les mêmes strates (39 026 lignes d'acte 1, 18 840 d'acte 2,
71 634 de fin de run). Pour chacune : AUC univariée contre `win`, puis β d'une logistique
`win ~ mesure standardisée + ascension + personnage` (erreur-type entre parenthèses).

### 1.1 Le tableau (actes 1 et 2 = les seules strates identifiables)

| définition | formule | AUC a1 | β a1 | AUC a2 | β a2 |
|---|---|---:|---:|---:|---:|
| densité actuelle | taux × pioche × Σdégâts / N | 0,511 | +0,078 (0,013) | 0,471 | −0,097 (0,017) |
| densité, tout amélioré | idem avec `damage_up` (borne haute) | 0,503 | +0,032 (0,013) | 0,469 | −0,110 (0,017) |
| dégâts par énergie | Σdégâts / Σcoût × énergie/tour | 0,511 | +0,088 (0,013) | 0,470 | −0,092 (0,017) |
| dégâts par énergie, amélioré | idem avec `damage_up` | 0,502 | +0,044 (0,013) | 0,468 | −0,105 (0,016) |
| burst top-k | somme des k meilleures cartes | 0,545 | +0,161 (0,012) | 0,485 | −0,054 (0,016) |
| burst top-k, amélioré | idem avec `damage_up` | 0,540 | +0,135 (0,012) | 0,484 | −0,060 (0,016) |
| **burst + scaling** | burst + Force/Poison/Focus | **0,549** | **+0,164 (0,012)** | **0,480** | **−0,070 (0,016)** |
| dégâts soutenus | densité + Force/Poison/Focus | 0,525 | +0,091 (0,012) | 0,468 | −0,124 (0,016) |
| densité hors injouables | Σdégâts / cartes jouables | 0,510 | +0,080 (0,013) | 0,470 | −0,100 (0,017) |
| dégâts par carte | Σdégâts / N, sans tempo | 0,454 | −0,200 (0,014) | 0,443 | −0,235 (0,017) |
| dégâts de zone | taux × pioche × ΣAOE / N | 0,506 | +0,057 (0,012) | 0,486 | −0,019 (0,015) |
| plus gros coup | max(`damage_up`) du deck | 0,514 | +0,034 (0,012) | 0,479 | −0,069 (0,015) |
| temps de mise à mort | PV boss / densité (inversé) | 0,490 | +0,071 (0,011) | 0,530 | +0,110 (0,020) |
| scaling seul | Force/Poison/Focus | 0,494 | −0,071 (0,013) | 0,475 | −0,136 (0,016) |
| cartes Pouvoir (contrôle) | nombre de Power | 0,591 | +0,344 (0,012) | 0,547 | +0,164 (0,015) |
| cartes Rares (contrôle) | nombre de Rares | 0,610 | +0,421 (0,013) | 0,560 | +0,226 (0,017) |
| **score du code (S15)** | 0,8·sat(densité) + 0,2·sat(scaling) | 0,503 | −0,012 (0,012) | 0,459 | −0,160 (0,016) |
| score densité améliorée | 0,8·sat(densité_up) + 0,2·sat(scaling) | 0,496 | −0,054 (0,012) | 0,457 | −0,175 (0,016) |
| score burst | 0,8·sat(burst) + 0,2·sat(scaling) | 0,528 | +0,055 (0,012) | 0,468 | −0,128 (0,015) |
| score burst amélioré | 0,8·sat(burst_up) + 0,2·sat(scaling) | 0,520 | +0,015 (0,012) | 0,467 | −0,134 (0,015) |
| **score burst+scaling** | sat(burst + scaling) | **0,547** | **+0,113 (0,012)** | 0,478 | −0,106 (0,016) |
| score soutenu | sat(densité + scaling) | 0,523 | +0,060 (0,012) | 0,467 | −0,140 (0,016) |
| score par énergie | 0,8·sat(par énergie) + 0,2·sat(scaling) | 0,503 | −0,005 (0,012) | 0,458 | −0,158 (0,016) |
| score par carte | 0,8·sat(Σdégâts/N) + 0,2·sat(scaling) | 0,469 | −0,147 (0,013) | 0,453 | −0,178 (0,016) |

Lecture : au mieux **0,549** à l'acte 1 (burst + scaling) contre 0,503 pour la dimension du code ;
à l'acte 2, **aucune** définition de dégâts ne dépasse 0,486 et les meilleures sont négatives.

Le burst plafonne dur (une carte de dégâts redondante n'ajoute rien). Des mélanges burst/densité ont
donc été testés pour lisser la courbe : `0,83·burst + 0,17·densité` donne 0,539 (+0,134) et
`0,75/0,25` 0,538 (+0,131) à l'acte 1, contre 0,541 (+0,140) pour le burst pur — **moins bien**, et
cela réintroduit la dilution qu'on corrige. Le burst pur est retenu ; la monotonie est préservée par
le tempo (k augmente avec la pioche et l'énergie) et par le scaling.

### 1.2 Deux défauts de l'ancienne formule, établis et non supposés

1. **La division par N et les cartes jouées au hasard.** `Σdégâts / N` fait baisser la mesure
   quand on ajoute un pouvoir (0 dégât) — exactement la façon de gagner à Spire. Preuve chiffrée :
   `Σdégâts / N` sans tempo vaut 0,454 (β −0,200), la densité temporisée 0,511, le burst top-k 0,545.
2. **Le poids séparé de 20 % sur le scaling.** Le score du code (β −0,012) est **moins** informatif
   que sa propre composante densité (β +0,078) : la composante scaling seule a β −0,071, et la
   pondérer à 20 % annule le signal. Fusionner le scaling dans la mesure (une seule saturation)
   remonte à +0,113.

### 1.3 Les hypothèses (a) et (b) ne s'excluent pas

Contrôles croisés (β standardisé) :

| modèle | β a1 | β a2 |
|---|---:|---:|
| burst, contrôlé par bloc/tour + PV | +0,164 (0,013) | −0,043 (0,017) |
| bloc/tour, contrôlé par burst + PV | +0,593 (0,013) | +0,258 (0,016) |
| PV, contrôlé par burst + bloc | +0,238 (0,012) | +1,663 (0,060) |
| burst, contrôlé par Rares + Pouvoirs | +0,104 (0,013) | −0,095 (0,017) |
| densité, contrôlée par Rares + Pouvoirs | +0,149 (0,014) | −0,078 (0,018) |
| score du code, contrôlé par Rares + Pouvoirs | +0,008 (0,013) | −0,163 (0,016) |

- **La défense domine réellement** (bloc/tour reste +0,59 à l'acte 1 et +0,26 à l'acte 2, même à
  offense et PV égaux) — l'hypothèse (a) de S15 est confirmée.
- **Mais la formule était bien fausse** : une fois la dilution et le poids séparé du scaling
  retirés, Offense porte un signal positif et significatif à l'acte 1 (+0,164, 13 SE). L'ancienne
  mesure l'effaçait.
- **Ce qui sépare le mieux n'est pas de la puissance de mise à mort** : le nombre de Rares (0,610)
  et de Pouvoirs (0,591) battent toutes les définitions de dégâts. C'est de la *composition* de
  deck (qualité du butin), pas de l'offense — et ça **n'a pas été implémenté comme Offense** : ce
  serait confondre deux concepts, et le corpus n'autorise aucune lecture causale.

## 2. Formule retenue (implémentée)

```c
// PlayerStrengthCalculator, dimension Offense uniquement
cardsPerTurn = playRate × drawPerTurn;              // cartes réellement jouables par tour
k            = clamp(ceil(cardsPerTurn), 1, nbCartesDeDégâts);
burst        = somme des k plus gros dégâts du deck; // pas la moyenne du deck
scaling      = Force/Poison/Focus des cartes + Force des reliques;
Offense      = clamp(round(100 × (burst + scaling) / (burst + scaling + PV_boss / 8)), 0, 100);
```

- **Rendements décroissants** : une seule saturation `100·x/(x+k)` avec `k = PV_boss/8` (tuer en
  8 tours = 50 points), comme avant. Une carte redondante n'ajoute rien au plafond (idempotence de
  docs/27 §7bis) ; une carte plus forte que la k-ième, si.
- **Explicabilité** : le terme nomme le burst, k, le scaling et la cible (`PV_boss/8`) ; la densité
  moyenne reste exposée comme mesure de diagnostic (`damageDensityPerTurn`), plus utilisée.
- **Le tempo reste dans la mesure** : k monte avec la pioche et l'énergie, donc un deck qui investit
  dans le débit compte plus de cartes.
- Le bloc reste une densité moyenne (il faut bloquer *chaque* tour) : c'est la mesure qui sépare le
  mieux (AUC 0,673 à l'acte 1) et elle n'est pas touchée.
- Constantes supprimées : `OffenseDamageWeight`, `OffenseScalingWeight`, `ScalingAtHalf` (elles
  n'avaient de sens qu'avec deux saturations séparées). Nouvelles mesures : `burstCards`,
  `damageDensityPerTurn`, `scalingCards` ; `scaling` inclut désormais les reliques.

## 3. Nouveaux poids (proposés, `data/calibration/weights.json`)

Calibration relancée sur le même corpus (130 853 runs lus, 71 634 dans le fit final, split 80/20,
graine 20261029). AUC des modèles : `act1_dims` **0,689**, `act2_dims` **0,653**, `final_dims`
0,804 (0,782 avant : effet de bord du correctif, non revendiqué comme un gain — le modèle final est
contaminé par les PV de fin de run).

| acte | méthode | Survie | Offense | Consistance | Couverture |
|---|---|---:|---:|---:|---:|
| 1 | code actuel | 40 % | 30 % | 20 % | 10 % |
| 1 | data, coefficients libres (IC 95 %) | **85 %** (82–88) | **10 %** (7–13) | 0 % (0–0) | 5 % (3–7) |
| 1 | data, indice contraint | **74 %** | **11 %** | 5 % | 10 % |
| 2 | code actuel | 30 % | 35 % | 20 % | 15 % |
| 2 | data, coefficients libres | **82 %** (77–86) | **0 %** (0–0) | 18 % (14–22) | 0 % (0–3) |
| 2 | data, indice contraint | **70 %** | **3 %** | 23 % | 4 % |
| 3 | — | non identifiable (aucun run ne survit à l'acte 3 sans gagner) | | | |

AUC univariée d'Offense : **0,548** à l'acte 1 (0,500 avant), **0,478** à l'acte 2 (0,459 avant).
β de la dimension Offense dans le modèle à coefficients libres : **+0,097 ± 0,015** à l'acte 1
(6,5 SE, positif), **−0,069 ± 0,019** à l'acte 2.

**Conséquence honnête : à l'acte 2, le poids proposé pour Offense est 0.** La correction de la
mesure ne fabrique pas de signal là où il n'y en a pas ; forcer 0,35 (code) ou même 0,05 serait
inventer. À l'acte 1, Offense vaut ~10 % (les deux estimateurs concordent : 10 % et 11 %).

`StrengthWeights.Act1/Act2/Act3` **n'est pas modifié** : les poids restent une proposition soumise à
arbitrage (docs/27 §7bis, docs/31 §5). Seule la *mesure* change dans cette tâche.

## 4. Tests C# modifiés (et pourquoi)

| test | changement | justification |
|---|---|---|
| `PlayerStrengthTests.Adding_strikes_raises_offense` | scindé en `Adding_a_stronger_damage_card_raises_offense` + `Redundant_strikes_do_not_raise_offense` | 5 Strike de plus dans 11 cartes ne changent pas les 3 cartes jouées par tour : le burst est insensible à la redondance (idempotence). Le test d'origine vérifiait l'inverse, propriété de l'ancienne densité. Remplacé par la propriété réelle : un 2ᵉ Bash (plus fort que la k-ième carte) monte l'offense. |
| `PlayerStrengthTests.Power_with_scaling_raises_offense_without_dealing_damage` | **ajouté** | Verrouille le défaut corrigé : Inflame (0 dégât, +2 Force) augmente l'offense. L'ancienne densité la pénalisait. |
| `PlayerStrengthTests.Saturated_offense_plateaus_instead_of_growing` | **ajouté** | 20 vs 40 Strike : même plafond de dégâts/tour, offense identique à ±1 ; la saturation reste bornée < 100. |
| `FitTests.Same_card_scores_higher_when_the_need_is_not_covered` | `Assert.Null(fit couvert)` → `Assert.InRange(fit, 0, 2)` | ΔG mesuré = +1 pt (survie +3, consistance −2) : 2 points sur un plafond de 20, soit 10× moins que le deck sans bloc. Ce +1 existait avant S17, il était masqué par la pénalité de dilution de l'ancienne Offense (une carte à 0 dégât faisait baisser la densité) ; le burst ne dilue plus. L'idempotence reste vérifiée par `Second_defend_is_worth_less_than_the_first` (ΔG décroissant) et par le facteur 10. |

Idempotence et bornes conservées : `Starter_deck_gives_bounded_reproducible_dimensions`,
`Saturated_defense_plateaus...`, `Empty_deck_is_zero_without_throwing`,
`Total_is_the_weighted_sum_of_the_dimensions`, `Upgraded_cards_improve_the_dimensions...` passent
sans modification. Suite complète : **137 tests, 0 échec** (134 + 3 ajoutés).

## 5. Parité C# ↔ Python

`tools/calibration/validate_csharp.py` (nouveau) tire 60 decks réels, écrit un harnais `dotnet run`
qui appelle le **vrai** `PlayerStrengthCalculator`, et compare champ par champ avec `features.py` :

```
decks comparés : 60
champs comparés: 1020      (4 dimensions + 13 mesures, dont burst et densité)
écarts         : 0
```

Sans ça, une correction de formule appliquée d'un seul côté rendrait la calibration (et le rapport)
étrangers au score réellement calculé.

## 6. Fichiers

| fichier | nature |
|---|---|
| `Ultima.Engine/Scoring/Strength/PlayerStrength.cs` | `BurstCardCount` / `BurstDamage`, `Offense` = sat(burst + scaling), mesures `burstCards` / `damageDensityPerTurn` / `scalingCards`, doc de classe réécrite (pourquoi un plafond) |
| `Ultima.Engine.Tests/Scoring/PlayerStrengthTests.cs` | 2 tests ajoutés, 1 scindé (§4) |
| `Ultima.Engine.Tests/Scoring/FitTests.cs` | 1 assertion assouplie avec justification (§4) |
| `tools/calibration/diagnose_offense.py` | **nouveau** — 24 définitions, AUC + β ajusté, mélanges burst/densité, contrôles défense et qualité de deck |
| `tools/calibration/validate_csharp.py` | **nouveau** — parité champ par champ avec le moteur |
| `tools/calibration/features.py` | miroir exact de la nouvelle formule (`burst_card_count`, `burst_damage`, `scaling` avec reliques), `DEVIATIONS` mises à jour |
| `tools/calibration/fit_weights.py` | libellés et prose du rapport (Offense = burst), `offense_measure` dans `weights.json`, sous-section §3.8 |
| `tools/calibration/selftest.py` | 63 → **69 vérifications** (burst : k, somme des k plus grosses, bornes) |
| `tools/calibration/README.md` | commandes et rôle des deux nouveaux scripts |
| `data/calibration/weights.json` | **régénéré** (14,2 Ko) |
| `tools/calibration/report.md` | **régénéré** |
| `docs/31-calibration-poids-g.md` | §7 : ce que S17 a corrigé, avec le tableau des définitions |

`data/index` n'est pas commité (gitignoré) et n'a pas été touché.

## 7. Non prouvé / limites

- **Aucune causalité.** On observe « les runs qui ont cette qualité gagnent plus souvent », jamais
  « ajouter cette carte fait gagner ». Pas de contrefactuel.
- **Acte 2 : rien.** Aucune des 24 définitions ne dépasse 0,486 ; burst contrôlé par bloc et PV
  tombe à −0,043. Le poids 0 proposé est une absence de preuve, pas une preuve d'absence d'effet.
- **Le burst est un plafond optimiste** : il suppose que le joueur joue ses k meilleures cartes
  chaque tour. C'est un choix de mesure (l'ancienne densité supposait l'inverse), pas une
  simulation de combat ; le biais est le même pour tous les decks, donc ΔG reste comparable.
- **Améliorations inconnues dans l'index** : tout est chiffré non amélioré. La variante « tout
  amélioré » a été testée et sépare **moins bien** (0,540 vs 0,545), donc le biais ne crée pas le
  signal — mais il déplace les valeurs absolues.
- **Composition du deck** (Rares 0,610, Pouvoirs 0,591) sépare mieux qu'Offense. Non implémenté :
  ce n'est pas de l'offense, et en faire une dimension demanderait sa propre tâche (et sa propre
  justification). C'est la piste la plus prometteuse laissée ouverte.
- **Solo, v0.107.1, split aléatoire** (pas temporel), menace = médiane des boss de l'acte.
- **Le pouvoir prédictif global ne bouge pas** : `act1_dims` passe de 0,688 à 0,689, `act2_dims`
  de 0,656 à 0,653. Le gain est dans la *mesure* d'une dimension (et dans son interprétation), pas
  dans la capacité du modèle à prédire `win` — Offense reste la dimension la plus faible.

## 8. Vérifier

```bash
export PATH="$HOME/.dotnet:$PATH"
bash tools/hermes-verify.sh          # build -warnaserror, 137 tests, contrats : PASS
python3 -I tools/calibration/selftest.py          # 69 vérifications
python3 -I tools/calibration/validate_csharp.py --decks 60    # 1020 champs, 0 écart
```
