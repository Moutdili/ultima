# 51 — Contrat v1.6 (chef, 2026-11-10)

Source : la première victoire co-op en A10 (`docs/45-plan-hermes/T9-run-coop-a10-gagnee.md`) et les promesses
de `docs/46` §2 (statistiques). Tout est ADDITIF (`ContractInfo.CurrentMinor = 6`). Code :
`Ultima.Engine/Contracts/EngineContractsV16.cs` + champs ajoutés dans `EngineContracts.cs`,
`EngineContractsV12.cs`, `EngineContractsV15.cs`, `EngineInterfaces.cs`. Aucun schéma JSON ne change.

Les tâches qui appliquent ce contrat : `docs/45-plan-hermes/T10-contrat-v16-applique.md` (double boss, feu de
camp, offres, potions, co-op) et `T11-statistiques.md` (`stats.open`).

## 1. Champs ajoutés
| Champ | Type | Sens | Rempli par |
|---|---|---|---|
| `RunView.SecondBossId`, `SecondBossName` | `string?` | Second boss de l'acte (palier « double boss »), enchaîné sans repos ni récompense. `null` = un seul boss. | daemon (**déjà fait**, depuis `ActInfo.SecondBossId`) |
| `ScoringContext.SecondBossId` | `string?` | Même donnée, pour le calcul. | daemon (**déjà fait**) |
| `RunView.PotionSlots` | `int?` | Emplacements de potion du joueur local (jusqu'à 8 vus). | daemon (**déjà fait**, `MaxPotionSlotCount`) |
| `RewardView.OfferIndex` | `int` | Rang de l'offre à cet étage, 0 = première. Augmente à chaque relance ou récompense suivante. | daemon (T10) |
| `RewardView.PendingRewards` | `int?` | Récompenses de cartes encore à ouvrir, telles que l'écran les montre. | miroir + daemon (T10) |
| `RestSiteView.ActionsRemaining` | `int?` | Actions encore possibles à ce feu. | miroir + daemon (T10) |
| `RestSiteView.Plan` | `IReadOnlyList<RestStepView>?` | Meilleure combinaison pour les actions restantes, dans l'ordre. | calcul via `IRestAdvisor` (T10) |
| `RestOptionView.TargetPlayer` | `ulong?` | Allié conseillé pour une option qui vise un joueur (`MEND`), = `AllyView.NetId`. | calcul (T10) |
| `ReviewPoint.Sequence` | `int` | Rang du choix à cet étage pour ce joueur. Un point par offre. | `ReviewBuilder` (T9 §1.6) |
| `ReviewPoint.DiedHere` | `bool` | Mort pendant ce combat puis relevé à 1 PV (co-op). | `ReviewBuilder` (T9 §2) |

Nouveaux types : `RestOptionIds` (les 9 options connues), `RestStepView(OptionId, CardId, PlayerNetId)`,
`RestAdvice`, `IRestAdvisor`, et le bloc statistiques du §3.

## 2. Décisions
1. **Second boss** : publié parce que le jeu l'affiche (`NTopBarBossIcon`). Un acte futur non révélé reste masqué
   (`Mapper.cs` le fait déjà). `RouteView.ExpectedHpAtBoss` garde son sens : PV à l'entrée du **premier** boss.
   Le coût du second boss passe par un terme nommé dans `RouteView.Terms`, pas par un nouveau champ.
2. **Ids du feu de camp** : en majuscules, comme dans la save (`HEAL`, `SMITH`, `MEND`…). Le commentaire v1.5 qui
   disait « rest », « smith » est caduc. Une option inconnue est publiée telle quelle avec `Score = null`.
3. **Plan du feu de camp** : `Plan` contient au plus `ActionsRemaining` étapes. Si `ActionsRemaining` est `null`,
   le calcul suppose 1 action. Les surfaces affichent le plan en tête, puis une note par option.
4. **Offres** : `OfferIndex` repart à 0 à chaque changement d'étage. `AlreadyTaken` porte sur l'offre courante
   seulement. Les surfaces ne supposent jamais une seule offre par étage.
5. **Co-op dans les notes** : aucun champ nouveau. `ScoringContext.Allies` existe depuis la v1.2 et le daemon le
   remplit ; toute implémentation d'`IScorer` doit s'en servir (T10 §4). En solo (`Allies` vide) les notes ne
   changent pas : c'est un critère de test.
6. **Potions** : `RunView.Potions` reste la liste des potions possédées ; `PotionSlots` donne le nombre de cases.
   Les surfaces dessinent les cases vides.
7. **`combatReads`** reste un réglage hors contrat (`settings.json`, faux par défaut) tant que la mesure FPS n'est
   pas faite. `EngineState.Combat` reste `null` d'ici là.

## 3. Statistiques : op `stats.open` (jamais dans `EngineState`)
Même mécanique que `review.open` (`Ultima.Daemon/Publish/ReviewService.cs`) : calcul hors ligne, refusé pendant
un combat, résultat écrit dans un fichier et chemin renvoyé sur le socket.

    → {"op":"stats.open","scope":{"mode":"coop","characterId":null,"ascensionMin":10,"ascensionMax":10}}
    ← {"op":"stats.open","ok":true,"path":"…/ultima/stats/<clé du filtre>.json"}
    ← {"op":"stats.open","ok":false,"error":"…"}

- `StatsScope(Mode, CharacterId, AscensionMin, AscensionMax)` ; `Mode` ∈ `all | solo | coop`. `scope` absent ⇒
  `all`, sans autre filtre.
- `StatsView(StatsVersion, Scope, Runs, Wins, ByCharacter, ByAscension, ByArchetype, Encounters, History, DataBasis)`.
  `StatsBucket` = clé, libellé, runs, victoires, taux, étage moyen. `EncounterStats` = dégâts moyens et médians par
  rencontre, morts. `RunSummary` = une ligne de l'historique.
- **Solo et co-op ne sont jamais mélangés en silence** : `Scope` est toujours renvoyé et affiché.
- `StatsVersion` commence à 1. `DataBasis` dit d'où viennent les chiffres (« 247 runs locales, v0.107.1 »).
- Vie privée : aucun pseudo, aucun identifiant Steam, aucune seed dans `StatsView`.

## 4. Compatibilité
- Les consommateurs ignorent les champs inconnus. `tools/console` : `CONTRACT_MINOR = 6` (déjà posé).
- `docs/ipc/state.v14.sample.json` passe à `contractMinor` 6. `ContractShapeTests` tolère les champs v1.6.
- Demande d'un champ : `docs/45-plan-hermes/demandes-de-contrat.md`, section « v1.6 ». Ne pas modifier le contrat.
