# T10 — Appliquer le contrat v1.6 : double boss, feu de camp, offres, potions, co-op

**À lire d'abord** : `docs/51-contrat-v16.md`, puis `T9-run-coop-a10-gagnee.md` (§3.4, §4, §5, §6) qui donne les
faits mesurés. **Vague 2** (voir `01-ARCHITECTURE-DE-LA-SUITE.md`). **Dépend de** : vague 1 mergée.
**Fixture** : `fixtures/saves/golden/coop3_a10_win.run` (3 joueurs, A10, double boss, `MINIATURE_TENT`, 8 potions).

Le chef a déjà fait : les champs du contrat, `RunView.SecondBossId/SecondBossName/PotionSlots` et
`ScoringContext.SecondBossId` remplis par `StateAssembler.BuildRun`. Tout le reste est ici.

## 1. Voie D — daemon et miroir
1. **`RewardView.OfferIndex`** : 0 à la première offre d'un étage ; +1 quand les cartes proposées changent (relance)
   ou qu'une nouvelle récompense s'ouvre après une prise ; retour à 0 au changement d'étage.
2. **`RewardView.PendingRewards`** : nombre de récompenses de cartes encore à ouvrir sur l'écran de butin.
   Cartographier dans le miroir (`sts2-game-api` : écran des récompenses). Introuvable ⇒ `null`, et tu le dis.
3. **`RestSiteView`** : options visibles avec leurs ids en majuscules (`RestOptionIds`), `ActionsRemaining` lu
   dans le miroir (introuvable ⇒ `null`). Le daemon appelle `IRestAdvisor.Advise(optionIds, actions, ctx)` et
   recopie `Options[i]` dans `RestOptionView.Score`, `Plan` dans `RestSiteView.Plan`. Pour chaque étape du plan :
   `CardId` ⇒ `RestOptionView.Target` de l'option `SMITH`, `PlayerNetId` ⇒ `TargetPlayer` de l'option `MEND`.
4. Option inconnue du contrat : publiée avec son id, `Label` de la base de connaissance ou `null`, `Score = null`.
5. Aucune lecture mémoire en combat (le test de T7 §6 doit rester vert).

## 2. Voie C — double boss
1. `ctx.SecondBossId != null` ⇒ l'adéquation au boss et la note du deck portent sur **les deux** boss. Le terme
   nomme les deux et donne le plus faible des deux en preuve (`ScoreTerm.Evidence`).
2. Route : le coût du second boss est un terme de `RouteView.Terms` (« Second boss, sans repos »).
   `ExpectedHpAtBoss` reste les PV à l'entrée du premier ; `MayBeLethal` tient compte des deux combats à la suite.
3. Donnée : `data/calibration/rooms.json` ne connaît qu'un boss (40,4 PV à l'acte 3, solo). Mesurer la paire sur
   le corpus (runs avec deux étages de boss au dernier acte) dès que l'index existe (T12 §1). D'ici là : somme des
   deux boss avec la source écrite « estimé, paire non mesurée » et `Confidence = low`. Pas de chiffre inventé.

## 3. Voie C — feu de camp (`IRestAdvisor`)
Implémentation : `Ultima.Engine/Scoring/Rest/RestAdvisor.cs`.
1. Une note S-F par option connue, termes chiffrés et sourcés. `HEAL` : PV manquants, prochain élite ou boss sur
   la route, double boss. `SMITH` : meilleur gain de G parmi les cartes améliorables (réutilise ce qui note une
   amélioration). `MEND` : PV manquants de l'allié le plus bas (`ctx.Allies`) ; sans allié ⇒ option non évaluable.
2. `LIFT`, `COOK`, `DIG`, `HATCH`, `KINDLE`, `CLONE` : note seulement si l'effet est modélisé depuis le source du
   jeu ; sinon `null`. Dis dans le rapport lesquelles sont notées.
3. **Plan** : pour `actions` ≥ 2, la meilleure combinaison **ordonnée** d'options distinctes disponibles. Tenir
   compte de l'effet de la première sur la seconde (après `HEAL`, l'intérêt d'un second soin baisse).
4. Solo : `MEND` absent ou non évaluable, le reste inchangé.

## 4. Voie C — le co-op compte dans les notes
Constat (T9 §3.4) : les notes sont identiques avec et sans `ctx.Allies`. Seul `CoopScorer` lit ce champ, et
personne ne l'appelle.
1. `Scorer.ScoreCard`, `ScoreRelic` et `ScoreShopItem` ajoutent le terme d'équipe de `CoopScorer` quand
   `ctx.Allies` n'est pas vide. Terme nommé « Équipe », avec sa preuve.
2. Note du deck (T9 §3.5) : décide si la contribution au groupe (soin, soutien) entre dans `EvaluateDeck` en co-op.
   Écris ta décision et la mesure qui la fonde. Ne change pas la note en solo.
3. Si le terme d'équipe n'est pas défendable sur les données, **ne le branche pas** : déclare le co-op « non
   livré » dans le rapport et laisse `CoopScorer` hors du chemin. Pas de terme décoratif.

## 5. Voies H et S — surfaces
1. **Second boss** : partout où le boss de l'acte est affiché (HUD, TUI, compagnon), afficher les deux, dans
   l'ordre, quand `secondBossId` n'est pas `null`.
2. **Potions** : dessiner `potionSlots` cases, vides comprises. Tester à 2, 3, 5 et **8**.
3. **Feu de camp** : module `restSite` (écran `restSite`) : le plan en tête (« 1. Forger X  2. Soigner Y »), puis
   une ligne par option avec sa note. Option sans note ⇒ « — ».
4. **Offres** : redessiner quand `offerIndex` change ; les cases de T7 suivent la relance. Afficher « encore N »
   quand `pendingRewards` > 0.
5. Rien de tout cela ne doit planter sur un état v1.5 (champs absents) : test avec `state.v13.sample.json`.

## 6. Critères (un test par ligne)
- `OfferIndex` : séquence 0, 1 (relance), 2 (récompense suivante), puis 0 à l'étage suivant.
- Feu à 3 options et 2 actions : `Plan` a 2 étapes, options distinctes, `Target` ou `TargetPlayer` remplis.
- `MEND` vise l'allié le plus bas en PV ; en solo, aucune option ne vise un joueur.
- Double boss sur la fixture, acte 3 : terme du second boss présent dans la route et dans la note du deck ;
  même contexte avec `SecondBossId = null` ⇒ terme absent.
- Co-op : un test où la note change avec `Allies`, un test où elle ne change pas avec `Allies` vide.
- Surfaces : 8 cases de potion, deux boss affichés, plan du feu affiché, aucun plantage sur un état v1.5.
- `tools/verify-all.sh` vert. Rapport habituel, décisions du §4 écrites.
