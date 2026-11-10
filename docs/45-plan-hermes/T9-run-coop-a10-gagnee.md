# T9 — Ce qu'apprend la première victoire co-op en A10 (chef, 2026-11-04)

> Demande de Mio : « analyse ma dernière partie en multi en A10, on a gagné, utilise-la pour de la data ».
> Run : 3 joueurs (DEFECT, REGENT, NECROBINDER), v0.107.1, 46 étages, 4 100 s. Victoire d'extrême justesse :
> fin à 1/67, 4/104 et 1/95 PV, deux joueurs morts pendant le dernier boss.
>
> **Fixture anonymisée : `fixtures/saves/golden/coop3_a10_win.run`** (identifiants remplacés, seed retirée, joueur
> local = `70000000000000001`). Rangée hors de `history/` pour ne pas changer les comptes de `PersonalStats`.

**Skills** : `sts2-meta-scoring`, `sts2-save-format`, `sts2-map-paths`, `sts2-engine-contract`.

> **Mise à jour du 2026-11-10** : le contrat v1.6 est livré (`docs/51-contrat-v16.md`). Cette fiche garde la
> **revue de run** (§1, voie R, vague 1 ; nouveaux champs `ReviewPoint.Sequence` et `DiedHere`), les **faits** (§2)
> et les **décisions de calcul** (§3, voie C, vague 2). Le double boss (§4), le feu de camp (§5), les surfaces
> (§6) et le §3.4 sont exécutés dans `T10-contrat-v16-applique.md` ; l'index et le §3.7 dans
> `T12-donnees-et-calibration.md`.

**Une victoire ne prouve pas qu'une carte est bonne.** Cette run sert à trouver des cas que le code gère mal, pas à
recalibrer des poids. Tout ce qui suit a été mesuré en passant la run dans le vrai moteur, avec le deck reconstitué.

## 1. Revue de run : `ReviewBuilder` se trompe en co-op (priorité 1)
| # | Défaut | Preuve sur la fixture | Attendu |
|---|---|---|---|
| 1 | `character = run.Character()` est unique : tous les joueurs sont notés avec le personnage de `Players[0]` | REGENT et NECROBINDER notés comme DEFECT | Le personnage de chaque joueur (`RunRecordPlayer.CharacterId`) |
| 2 | Le deck d'un étage est filtré depuis le deck final, et la doc dit les retraits « non reconstituables » : c'est faux | Remonter depuis le deck final (annuler `cards_gained`, `cards_removed`, `cards_transformed`, `cards_enchanted`, `upgraded_cards`) redonne exactement les 3 decks de départ (11 cartes, Ascender's Bane compris), sans aucun échec | Deck exact à chaque étage ; test : retour au deck de départ |
| 3 | Reliques : le set final est utilisé à chaque étage (connaissance du futur) | Code : `relicsOfPlayer` | Filtrer par `RelicInstance.FloorAdded` |
| 4 | Une visite de shop est classée `CardReward` | 9 points `CardReward` sur les étages 22, 37 et 40 ; le « regret » y compare des articles non achetés | `ReviewKind.Shop` |
| 5 | Le type vient de `map_point_type` | Étage 37 : `unknown` sur la carte, `room_type` = `shop` | `rooms[].room_type` fait foi |
| 6 | `CardChoices` n'est pas toujours une seule récompense : seul le premier choix pris est compté | Étages 1 (`KALEIDOSCOPE`) et 34 (`CRYSTAL_SPHERE`) : 2 récompenses, 2 cartes prises. Étages 18, 20, 21, 29 et 33 de NECROBINDER (`DRIFTWOOD`) : 3 rejetées puis 3 nouvelles | Segmenter (voir §2) |
| 7 | Une carte volée puis rendue ressemble à un gain | Étage 18 : `cards_gained` et `cards_removed` contiennent la même carte (avec `floor_added_to_deck` d'origine) | Net 0 : ni prise, ni retrait |

## 2. Faits sur l'historique (à garder en tête dans tout calcul)
- **Ordre de `card_choices`** (`CardReward.cs` l.296-329) : à un reroll, les 3 anciennes (`false`) ; à la fin, la carte
  prise (`true`) puis les autres (`false`). L'ordre n'est donc pas celui de l'écran.
- **Le joueur local et les alliés ne sont pas enregistrés pareil au shop** (47 runs co-op de Mio) :
  | Mesure sur les étages de shop | local | allié |
  |---|---|---|
  | L'or baisse mais `gold_spent` = 0 | 10/97 | **168/197** |
  | Carte gagnée sans `was_picked` | **66** | 12 |
  | `was_picked` présent | 10 | **152** |

  Pour un allié, `gold_spent` n'est pas fiable : calculer depuis `current_gold`. Pour le joueur local, un achat de
  carte se lit dans `cards_gained`, pas dans `card_choices`. **À vérifier** : `tools/index_runs.py` garde
  `rt = map_point_type`, donc un shop sur une case `?` passe pour une récompense et les achats d'alliés pour des prises.
  Vérifier l'effet sur le backtest et la calibration.
- **Mort en co-op** : `Player.ReviveBeforeCombatEnd` soigne 1 PV aux morts en fin de combat si le groupe gagne. Une
  ligne `hp 1, hp_healed 1` = mort pendant ce combat (étages 12, 31, 46 ×2).
- **Comptabilité des PV** : `avant − damage_taken + hp_healed = après` tient sur tous les étages sauf un, **non expliqué** :
  étage 31, REGENT, 62 − 21 + 1 = 42 mais 1 PV réel. Ne pas supposer que `damage_taken` couvre toute perte de PV.
- **Cases `?` de cette run** : 8 événements, 2 combats, 1 trésor, 1 shop.

## 3. Calcul : à trancher par toi (échantillon d'une run, ne pas surinterpréter)
1. **Retrait : toujours F** (F5 à F11, 9 cas sur 9), y compris pour retirer **`NORMALITY` (F11)** et **`DEBT` (F7)**,
   retraits que Mio a effectivement payés. Confirme T8 §2.3. Le terme « Prix du retrait » vaut 0 partout.
2. **« Courbe de coût à 1 énergie −4 »** toujours présente (`THUNDER`+1, `DOUBLE_ENERGY`). Confirme T8 §2.2.
   Sur 201 offres : 178 termes, moyenne −2,1, de −4 à +3.
3. **La méta écrase tout** : terme moyen 40,3 (de 20 à 71) ; les autres termes pèsent 5 au plus en moyenne.
   Notes offertes : S 0, A 8, B 30, C 69, D 61, F 33. Aucun S en 46 étages.
4. **Les notes ignorent le co-op** : même résultat avec et sans `ctx.Allies`. Seul `CoopScorer` lit ce champ, et ni le
   daemon, ni `Scorer`, ni `CardScorer` ne l'appellent. À brancher, ou à déclarer non livré.
5. **Note du deck** avant le dernier boss : DEFECT A76, REGENT S85, NECROBINDER C50 (B68 à l'acte 1), alors que ce
   joueur a le badge `HEALER` et a soigné un allié à l'étage 15. La note ne voit pas la contribution au groupe.
6. **Accord avec les joueurs** : carte prise = meilleure note 30 fois sur 56 (REGENT 14/19, DEFECT 10/20,
   NECROBINDER 6/17). Cartes prises notées F : 5, dont `HOTFIX` (F24 à côté de « passer » F23) et `THUNDER`+1 (F20).
   Ce n'est pas une preuve d'erreur. **À tester sur l'index A10 co-op** : pris/non pris et victoire pour ces cartes.
   `data/index/runs-*.jsonl.gz` est absent de la machine : à reconstruire (`tools/index_runs.py`) ou dire où il est.
7. **`PersonalStats`** agrège solo et co-op, toutes ascensions, sans les séparer. `data/calibration/rooms.json` vient
   du solo uniquement. Décider si le terme « Ton historique » et le risque des salles doivent les distinguer.

## 4. Double boss en A10 (contrat v1.6 livré ; exécution : T10 §2)
- `RunManager.cs` l.686 : au dernier acte, le niveau `DoubleBoss` tire un second boss (visible en jeu,
  `NTopBarBossIcon`). Ici : `QUEEN_BOSS` (étage 45) puis `AEONGLASS_BOSS` (étage 46), **sans soin, ni repos, ni récompense entre les
  deux**.
- Ultima.Saves le lit déjà (`ActInfo.SecondBossId`, `SecondBoss`), mais rien ne s'en sert : `RunView` n'a qu'un boss et
  `ScoringContext.BossId` ne reçoit que le premier.
- Dégâts mesurés à l'acte 3 : 83,6 PV par joueur sur les deux boss, contre 40,4 pour un boss dans `rooms.json` (solo).
- À faire après la v1.6 : adéquation au boss et note du deck sur les deux boss ; risque de route avec les deux combats
  à la suite ; PV conseillés à l'entrée de la paire.

## 5. Feu de camp (complète T8 §3)
- **`MINIATURE_TENT`** : plusieurs actions au même feu (`rest_site_choices: [HEAL, MEND, SMITH]`). Une note par option ne
  suffit pas : proposer la meilleure combinaison (`RestSiteView.ActionsRemaining` et `Plan`, v1.6 ; exécution : T10 §3).
- **Soin d'un allié (`MEND`)** : à noter selon les PV de l'allié (`RestOptionView.TargetPlayer`, v1.6). Mio l'a utilisé 5 fois (étages 23, 25, 27, 30, 39), et le
  soin apparaît dans le `hp_healed` de l'allié, même si cet allié a choisi `SMITH`.
- Options du jeu : `HEAL`, `SMITH`, `MEND`, `LIFT`, `COOK`, `DIG`, `HATCH`, `KINDLE`, `CLONE`. Une option inconnue
  s'affiche sans note, elle ne plante pas.

## 6. Surfaces
- **8 emplacements de potion** (`ALCHEMICAL_COFFER` +4, `POTION_BELT` +2) : tester le module potions du HUD et de la console à 8.
  Mio a utilisé 38 potions sur 55 du groupe.
- **T7** : `DRIFTWOOD` (reroll) remplace les cartes à l'écran, `KALEIDOSCOPE` et `CRYSTAL_SPHERE` enchaînent 2 récompenses.
  Les cases sous les choix doivent suivre la relecture et ne jamais supposer une seule offre par étage.

## 7. Critères
- `tools/verify-all.sh` vert ; tests sur `fixtures/saves/golden/coop3_a10_win.run` : personnage par joueur, retour aux
  decks de départ, reliques par étage, shops classés `Shop` (étage 37 compris), 2 cartes prises aux étages 1 et 34,
  étage 18 sans gain net, morts détectées aux étages 12, 31 et 46.
- Rapport habituel : fait / prouvé / NON prouvé / décisions prises au §3 / demandes de contrat.
