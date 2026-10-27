# CombatSolver — analyse de réutilisation pour ULTIMA

**Dépôt analysé** : `/home/mio/sts2/research/repos/CombatSolver` — commit `5c773caaec742bc55588bc9ee21c361326a732f0` (2026-10-07), version manifeste `0.50.1`, 922 fichiers `.cs`, 207 135 lignes.
**Cible déclarée par le dépôt** : Slay the Spire 2 **0.111.0**, .NET 9, Godot 4.5.1 (`CombatSolver.csproj:1,20`, `CombatSolver.json:10`). **ULTIMA tourne en 0.107.1** → écart de version à traiter en §7 et §9.
**Licence** : MIT (voir §10).
**Méthode** : lecture seule. Chaque affirmation porte un `fichier:ligne`. `ABSENT` signale une recherche infructueuse, jamais une supposition.

---

## 1. Architecture globale

Le dépôt suit une règle de dépendance stricte documentée : `Runtime` capture la racine et déploie, `Search` décide, le moteur (`Engine`) exécute les sémantiques génériques, `Prediction` détient les compensations de domaine, `UI` ne consomme qu'un snapshot en lecture seule (`docs/ARCHITECTURE.md:7-10`, `AGENTS.md:62`). Interdiction explicite : `Search` ne doit pas référencer `SolverSettings.Current`, `Entry.Logger`, `SolverController`, l'UI ni le runner de test (`AGENTS.md:98`).

| Répertoire | Fichiers `.cs` | Rôle réel | Classes pivots |
|---|---:|---|---|
| `src/Engine/Common` | 23 | Plomberie de données partagée : clonage, identité d'objets, side-tables, caches de modèles. **Aucune règle de combat.** | `PredictionForkContext`, `PredictionStateStore`, `PredictedCard`, `SimCardPile`, `CanonicalModels`, `DynamicVarSetAccess` |
| `src/Engine/InCombat/Simulation` | 36 | Le moteur de commandes : `CombatPredictionSimulator` en 30+ partials (Attack, Damage, Block, Card, CardPile, Draw, Energy, EndTurn, Potion, Orb, Heal, PowerAmounts…) + `SimulatedCombatState` | `CombatPredictionSimulator` (`CombatPredictionSimulator.cs:19`), `SimulatedCombatState` (`SimulatedCombatState.cs:30`) |
| `src/Engine/InCombat/Mirrors` | 10 + sous-dossiers `Hooks/*` | Ré-implémentations **synchrones** des hooks vanilla (async côté natif), classées par domaine : Attack, Block, Card, Damage, Death, Power, Relic, TurnStart/TurnEnd | `HookMirrors` (+ 8 partials), `MethodMirrorRegistry<T>`, `MirrorMethodSpec` |
| `src/Search` | 176 | Beam search, rétention, scoring, budgets, portefeuilles de largeur, continuations. Le plus gros bloc du dépôt. | `CombatBeamSolver` (~30 partials), `CombatSearchCoordinator` (7 partials), `BeamRetentionPolicy`, `StateEvaluation` |
| `src/Prediction` | 56 | Compensations de domaine, une par mécanique : pouvoirs, reliques, potions, IA de monstre, enchantements, orbes, choix | `IntentForecaster`, `BranchMonsterAi`, `MonsterMoveSemantics`, `PersistentPowerSupport`, `PlayerTurnEndLifecycle` |
| `src/Api` | 6 | Surface publique v6 + worker isolé | `PreCombatForecastApi`, `PreCombatForecastContracts`, `PreCombatForecastWorker` |
| `src/Runtime` | 64 | Capture de la racine sur le thread principal, orchestration, déploiement natif, GC/NoGC, notifications, statistiques | `Entry`, `SolverController` (3 714 lignes), `SolverDispatcher`, `CombatRootSnapshot`, `SearchGcPolicy` |
| `src/Strategy` | **1** | **Ce n'est pas une couche stratégie.** Un seul fichier de maths « value-only » partagées entre conseil de run et évaluation de branche | `CardMechanismFacts` (`CardMechanismFacts.cs:4`) — `AttackHits`, `ImmediateShivSupply`, `EstimatedShivPlays` |
| `src/UI` | 28 | Rendu du snapshot uniquement ; `SolverOverlaySnapshot.Capture` est l'unique frontière de projection (`docs/ARCHITECTURE.md:105`) | `SolverOverlay`, `SolverRouteRow`, `SolverActionPill` |
| `src/Diagnostics` | 3 | Enregistrement perf opt-in (P/Invoke Windows) | `PerformanceSession`, `PerformanceRecording` |
| `src/Testing` | ~39 | Hôte de test headless, protocole fichier, assertions | `UnattendedTestRunner`, `ProtocolHost` |

**Boucle d'exécution** (`docs/ARCHITECTURE.md:9`) : capture d'une racine stable sur le thread principal → gel de la politique → recherche en arrière-plan sur des branches ombre → retour sur le thread principal → déploiement via les entrées natives publiques → vérification du stamp de continuation ou nouvelle capture.

**Point notable pour ULTIMA** : `src/Strategy` ne contient aucune heuristique de décision. Les vraies heuristiques sont dans `src/Search` (`SolverWeights`, `StateEvaluation`, `BeamRetentionPolicy`), et les constantes y sont exprimées en unités de « 30 000 × PV » (`src/Search/SolverWeights.cs:10-12`).

---

## 2. Le cœur de simulation

### 2.1 Clonage / instanciation d'un état de combat

- Entrée de capture : `public static CombatRootSnapshot Capture(CombatState state, bool predictPotionReward = false)` — `src/Runtime/CombatRootSnapshot.cs:173`. Elle jette si `!NGame.IsMainThread()` (`:175-176`) et scelle les quatre tables de miroirs de phase de tour (`:177-180`).
- Appelants : `SolverController.cs:1210` et `PlayerTurnSetupPatches.cs:775`.
- Construction de l'ombre : `new SimulatedCombatState(state, liveCombatHookListeners)` puis `new CombatPredictionSimulator(simulatedCombat)` — `CombatRootSnapshot.cs:208-209`.
- `SimulatedCombatState` **est** un `ICombatState` (`SimulatedCombatState.cs:30`) : la branche n'est pas un wrapper, c'est l'objet d'état lui-même. Constructeur `SimulatedCombatState(CombatState inner, AbstractModel[]? capturedCombatHookListeners = null)` — `SimulatedCombatState.cs:251-253`, assertion main-thread `:254`.
- Matérialisation racine : `MaterializeRoot` (`SimulatedCombatState.cs:2077`) construit **eagerly** chaque `SimCreatureState`, `SimPlayerCombatState`, chaque clone de pouvoir et chaque état de prédiction, puis pose `_rootMaterialized = true` (`:2191`). Après cela, toute lecture manquante **jette** au lieu de retomber sur le live (`:1629-1633`).
- Fork de branche : `public CombatPredictionSimulator Fork()` — `CombatPredictionSimulator.cs:151`. Il ouvre **un seul** `using PredictionForkContext context = new();` (`:157`) et forke `State`, `StateStore`, `History`, `Rng` à l'intérieur (`:159-172`) : tout le graphe de la branche partage une seule table d'identité.
- `PredictionForkContext` = dictionnaire `source → fork` (`PredictionForking.cs:398`). `Register` jette « was forked twice » si une source mappe vers deux forks (`:430-441`) ; `RemapOrSelf` (`:464-469`) ; `RequireRemap` jette si aucun fork n'existe (`:484-492`).
- `PredictedCard` : copy-on-write à trois états — `Original`, `Preview`, jeton d'identité, `volatile bool Shared` (`PredictedCard.cs:13-18`). `Preview` retombe sur `Original` (`:99`) ; **toute écriture doit passer par `MutablePreview`** (`:105`), qui clone si aucun preview n'existe ou si `Shared` (`:113-126`).
- Piles de cartes : ordre copié en profondeur, état de carte COW — `SimCardPile.Fork` reconstruit une `List<PredictedCard>` et appelle `card.Fork(context)` par entrée (`SimCardPile.cs:89-94`).
- Conteneurs génériques : COW via `volatile bool Shared` sur `Storage` ; `Fork()` bascule le flag et partage, `EnsureWritable()` clone à la première mutation (`ForkableCollections.cs:42-46`, `:85-90`).
- Pouvoirs : **clonage profond systématique**, jamais COW — `ForkPower` clone le modèle et copie `_owner/_applier/_target/_amount` (`SimulatedCombatState.Fork.cs:313-324`). Identité par branche : `Dictionary<(Creature Owner, Type Type), PowerModel> _powers` (`SimulatedCombatState.cs:192`), forké entrée par entrée (`SimulatedCombatState.Fork.cs:106-113`).
- Side-table d'état : `PredictionStateStore` = `Dictionary<(AbstractModel Model, Type StateType), object>` + alias de modèles (`PredictionStateStore.cs:10-11`). `Fork` remappe la clé via `context.RemapOrSelf`, réutilise un état déjà forké via `TryRemap`, sinon appelle `IPredictionStateForkable.Fork` (`:192-231`) ; un état non forkable **jette** (`:208-212`).
- Frontière de fork : `IPredictionForkBoundary.AssertForkable` (`PredictionForking.cs:393-396`), implémentée en `SimulatedCombatState.Fork.cs:286-311` avec des gates en cascade : curseur de choix actif, profondeur de scope d'exécution de carte, fin de tour demandée non résolue, `_powerCardSources` non vide, choix de début de tour en attente, choix Knowledge Demon en attente, `_pendingPowerAmountChanges` non vide.

**Conséquence pratique** : le modèle de clonage est « deep copy au fork, COW sur les cartes, clone complet des pouvoirs ». C'est ce qui rend le fork suffisamment bon marché pour un beam search, mais impose que *toute* valeur mutable soit déclarée dans une des quatre catégories (racine, ombre, modèle cloné, `PredictionStateStore`) — sinon elle fuit entre branches (`AGENTS.md:68-76`).

### 2.2 Avancement des tours

Le tour est découpé en phases nommées, pas en une méthode unique.

- Fin de tour joueur, phase 1 : `PlayerTurnEndLifecycle.RunPhaseOne` (`PlayerTurnEndLifecycle.cs:32`) → `PlayerTurnPhase.End` → `EndTurnPowerSupport.TriggerVeryEarly` (`:39`) → `TurnStartRelicSupport.TriggerBeforeSideTurnEnd` (`:42`) → `SimulateEndPlayerTurnBeforeOrbPassives` (`:45`).
- Passifs d'orbes entre les deux phases : `OrbLifecycleSupport.TriggerBeforeTurnEnd` (`PlayerTurnEndLifecycle.cs:49-51`).
- Phase 2 : `RunPhaseTwo` = `CorePowerSupport.TriggerPlayerRegularSideTurnEndEffects` + `TurnStartRelicSupport.TriggerAfterSideTurnEnd` + `HookMirrors.AfterSideTurnEndLate`, puis `PlayerTurnPhase.None` (`PlayerTurnEndLifecycle.cs:11-30`).
- Côté ennemi : `CurrentSide = CombatSide.Enemy` (`CombatBeamSolver.Expansion.Replay.cs:1048`), `BeginSideTurn(enemy)` par ennemi (`:1050`), `HookMirrors.BeforeSideTurnStart` (`:1053`), clear de block par ennemi (`:1058-1067`), `TriggerSideTurnStart(side, Enemies, decrementEnemyPlating)` (`:1077`), poison ennemi (`:1084`).
- Exécution des moves : roster figé `actingEnemies` (`:1045`), boucle `:1115`, skip stun `ConsumeStunNextMove` (`:1120`), move forcé `TryConsumeForcedMonsterMove` (`:1125`), puis `MonsterMoveSemantics.ApplyForecastMove(...)` (`:1162`).
- Après les moves : `TriggerEnemySideTurnEndEffects` (`:1186`), poison joueur (`:1201`), `PrepareMonsterMovesForNextRound` (`:1222`).
- Début de tour joueur : `AdvanceRoundPlayerStart` (`CombatBeamSolver.RoundTransition.cs:11`) → `CurrentSide = Player` (`:25`), `RoundNumber++` (`:27`), `AdvancePlayerTurn` (`:28`), `BeginSideTurn` (`:29`), snapshot des montants de pouvoirs (`:30`), `BeforeSideTurnStart` (`:32`).
- Reset block joueur : `ShouldClearBlock` puis `DamageBlock(Block, ValueProp.Move)`, sinon `PersistentRelicSupport.TriggerAfterPreventingBlockClear` (`RoundTransition.cs:38-47`).
- Reset énergie : `ShouldPlayerResetEnergy(...)` → `LoseEnergy(Energy)` puis `GainEnergy(GetModifiedMaxEnergy(...) + ConsumeEnergyNextTurn(...))` (`RoundTransition.cs:56-59`).
- Pioche : `PrepareBeforeHandDraw` → `PersistentPowerSupport.ConsumeModifiedHandDraw(combat, player, CombatManager.baseHandDrawCount)` → `simulator.Draw(player, progress.DrawCount, fromHandDraw: true)` (`TurnExecutionContinuation.cs:82,94,113`).
- Compteurs par tour remis à zéro dans `BeginSideTurn` (attaques, shivs, cartes block/skill, exhausts, défausses, énergie/étoiles dépensées…) — `SimulatedCombatState.cs:1260-1293`.
- Durées Weak/Vulnerable/Frail/Intangible/NoBlock : `CorePowerSupport.TickDurations` (`CorePowerSupport.cs:703-715`).

### 2.3 Modélisation des intents ennemis

Deux mécanismes **distincts**, à ne pas confondre :

**(a) `IntentForecaster` — projection racine, lecture seule, multi-tours.** Utilisé pour le scoring et le diagnostic.
- `public static IntentForecast Build(CombatState state, int roundCount)` — `src/Prediction/IntentForecaster.cs:57`. Il clone le flux RNG depuis `state.RunState.Rng.MonsterAi.ToSerializable()` (`:61`) et marche `roundCount` tours (`:69-97`).
- Dégâts par coup : `int baseDamage = CaptureBaseDamage(attack, monster, move.Id)` puis `int single = attack.GetSingleDamage(state.PlayerCreatures, monster.Creature)` et **`for (int i = 0; i < Math.Max(attack.Repeats, 1); i++)`** → un `ForecastAttackHit(single, baseDamage)` par coup (`IntentForecaster.cs:124-127`). **C'est exactement la structure 16×3 recherchée pour F2.**
- `CaptureBaseDamage` exige `attack.DamageCalc` et jette sinon (`:31-44`) ; il audite au passage les patches Harmony étrangers sur `AttackIntent.GetSingleDamage` et `GenerateMoveStateMachine` (`:34-38`).
- Exactitude : un `DamageCalc` dynamique sur un move **non whitelisté** met `exact = false` (`:128-132`). La whitelist `IsKnownStableAttack` est une énorme expression de patterns `(type, moveId)` couvrant ~150 couples (`:155-264`).
- Branches : `RandomBranchState` → `MonsterRandomBranchResolver.Pick` (`:297-303`) ; `ConditionalBranchState` → `exact = false` + détail d'approximation (`:304-306`) ; tout autre état → `exact = false` (`:309-313`). Garde-fou de 32 étapes puis exception (`:290-317`).
- Intents non attaque : un Buff/Debuff modélisé par `MonsterMoveEffects.Supports` passe silencieusement (`:134-137`) ; sinon `unsupported = true` (`:138-145`). Sleep/Stun/Hidden sont tolérés (`:146`).
- Résultat : `IntentForecast { Rounds, HasUnsupportedIntent, IsExactForModeledDamage, UnsupportedDetails, ApproximationDetails, MonsterAiCountersByRound }` (`:19-27`), avec `IsExactForModeledDamage = exact && !unsupported` (`:103`).

**(b) `BranchMonsterAi` — machine à états mutable par branche.** C'est ce qui choisit réellement le prochain move pendant la recherche.
- État : monstre, machine, `MoveState` courant, `StateLog`, compteur de malédiction Knowledge Demon, snapshot statique, `NeedsInitialRoll` (`BranchMonsterAi.cs:13-20`).
- `BranchMonsterStaticSnapshot.Capture` parcourt **tous** les `MoveState` et capture `(BaseDamage, Repeats)` par intent d'attaque, les poids de chaque `RandomBranchState`, la sélection de chaque `ConditionalBranchState`, et les ints statiques du monstre (`:40-90`).
- `Advance` marche les `FollowUp` jusqu'à un `MoveState`, consomme `simulator.Rng.MonsterAi` pour les branches aléatoires, et jette si la machine ne converge pas en 32 étapes (`:151-205`, `:203-204`).
- Les conditionnelles en branche sont un `switch` codé en dur de **neuf** couples monstre/branche ; tout autre cas retombe sur la sélection capturée à la racine **ou jette** (`:267-310`). C'est là que la prédiction devient inexacte.

### 2.4 RNG

- Neuf flux, dans l'ordre : `Shuffle`, `CombatCardGeneration`, `CombatPotionGeneration`, `CombatCardSelection`, `CombatEnergyCosts`, `CombatTargets`, `CombatOrbGeneration`, `MonsterAi`, `Niche` (`CombatPredictionRngSet.cs:37-71`, capture depuis `RunRngSet` `:73-84`).
- État : `internal readonly record struct PredictionRngState(int Counter, ulong State0, ulong State1, ulong State2, ulong State3)` (`PredictionExtensions.cs:11-16`), capturé/restauré en poussant les champs privés natifs `_counter` et `_random._s0.._s3` (`:20-50`).
- Matérialisation paresseuse : `public Rng Mutable => _mutable ??= State.ToRng();` (`CombatPredictionRngSet.cs:25`) ; les lecteurs d'empreinte ne lisent que `State` (`:26-27`).
- Fork : jamais de `Rng` mutable partagé — `Stream.Fork()` fige l'état courant dans un `FrozenStream` (`:29-32`), le set forke les neuf (`:86-97`).
- `Advance(int count)` jette `NextUnsignedLong()` par valeur car « v0.109.0 a supprimé la reconstruction par compteur » (`PredictionExtensions.cs:52-62`) : **aucun fast-forward par seed**.
- Les neuf états entrent dans le stamp de continuation live (`ContinuationStamp.cs:105-117`) — c'est le contrat « neuf flux ».

### 2.5 Pouvoirs

- Stockage par branche : `_powers` clé `(Creature Owner, Type Type)` + `_powerListenerOrder` (`SimulatedCombatState.cs:192-194`).
- `GetMutablePowerInstance` renvoie une instance ajoutée, un clone racine multi-instance, le clone existant, ou clone à la demande (`:869-901`).
- `GetOrCreatePower` renvoie le clone capturé tant que `Amount != 0`, sinon clone une instance fraîche — commentaire : « A removed power is acquired as a fresh native instance, including its private counters » (`:989-1012`).
- `Apply<T>` : consommation d'artifact, branche instancié-vs-empilé, `Math.Clamp(amount, -999_999_999, 999_999_999)`, skip de durée à la première application, mise à jour de l'ordre de listeners, `RecordPowerAmountChange` (`:652-700`).
- Montants « consommés sans muter » : `PowerAmountPredictionState(int amount)` avec `Decrement/Decrease/Consume` (`PowerAmountPredictionState.cs:7-29`), réécrits **une seule fois** puis retirés du store — `SynchronizePowerAmountPredictionStates` (`CombatPredictionSimulator.PowerAmounts.cs:9-21`, remove `:20`).
- Ordre d'application des hooks : listeners de combat racine, puis pouvoirs du joueur, puis pouvoirs ennemis dans l'ordre du roster, puis pouvoirs des créatures nouvelles (`SimulatedCombatState.cs:1969-2062`).
- « Double settlement » = la même sémantique réglée par deux couches. Règle écrite comme invariant : « 同一战斗语义只能有一个权威实现 » (`AGENTS.md:99`). Garde-fou concret : `PowerLifecycleSupport.AfterCardPlayed` documente que `VitalSparkPower.AfterCardPlayed` est dispatché par `AfterCardPlayedMirrors`, donc « a second compensation here would apply TaintedPower twice » (`PowerLifecycleSupport.cs:86-87`). Second piège : sans le `StateStore.Remove` après réécriture, un `SynchronizePowerAmountPredictionStates` ultérieur rejouerait le même delta (`CombatPredictionSimulator.PowerAmounts.cs:16-21`).

### 2.6 Potions

- Usage en deux temps : `Prepare` consomme le slot et déclenche `BeforePotionUsed`, puis `PotionOnUseSupport.Use` dans un scope d'effet (`PotionExecutionSupport.cs:11-19`) ; `Complete` applique le choix, déclenche `AfterPotionUsed` seulement si le propriétaire est vivant, synchronise les pouvoirs, compense l'historique et applique les pouvoirs de mort (`:21-37`).
- Seules les potions whitelistées sont cherchables : `PotionOnUseSupport.CanSearch` = liste de types + `PotionOnUseMirrors.CanMirror` (`PotionOnUseSupport.cs:18-71`) ; la capture racine filtre les slots par ce prédicat (`CombatRootSnapshot.cs:229`).
- Inventaire par branche : `_potionSlots` `(Player, Slot) → PotionModel?`, `_freeEntropicPotions`, log ordonné `PredictedPotionUse(Slot, PotionId, StrategicHpCost, Automatic)` (`SimulatedCombatState.Potions.cs:10-19`).
- Choix requis : `PotionChoiceSupport.RequiresChoice` (registre de miroirs + potions génératrices + Ashwater/DropletOfPrecognition/GamblersBrew/LiquidMemories/TouchOfInsanity) (`PotionChoiceSupport.cs:12-18`).
- Le choix est un `CardChoiceSpec(Effect, SourcePile, MinCount, MaxCount, Options, SourceCards, ReplacementValue, ContextId, MaxBranches?, IsImplicitAllSelection, PreserveOrderedRoutingResidues)` (`CardChoiceSupport.cs:12-23`) ; le choix concret du plan est `PlanCardChoice(Effect, SourcePile, Cards, SourceId, ContextId, Timing)` (`CombatPlan.cs:110-116`).
- Continuation dédiée : fork d'un checkpoint au point de choix + rejeu du seul complément par frère (`CombatBeamSolver.PotionChoiceContinuation.cs:141`), avec assertion d'équivalence incrémentale contre un rejeu complet (`:44-47`).

### 2.7 Reliques

- Instances clonées à la racine via `PredictionUtils.CreateRelic(relic, player)` (`SimulatedCombatState.cs:314`), l'original conservé dans `_rootRelicSources` pour la capture one-shot des compteurs (`:316-322`).
- Compteurs par branche : `ForkableDictionary<RelicModel, StatefulRelicState>` avec `readonly record struct StatefulRelicState(int Current, int Previous)` (`SimulatedCombatState.Relics.cs:30-32`).
- `CaptureLiveState` lit une fois les champs privés par relique (ArtOfWar, BeltBuckle, BoneTea, HappyFlower, PenNib, Pocketwatch, RedSkull, UnsettlingLamp, VenerableTeaSet…) (`SimulatedCombatState.Relics.cs:325-356`).
- Reset par tour explicite : `ResetBeforeSideTurnStart` remet à zéro BrilliantScarf/Kunai/OrnamentalFan/Shuriken/VelvetChoker et efface DemonTongue/MusicBox/RainbowRing/Regalite/BeatingRemnant (`RelicPredictionStateSupport.cs:139-195`) ; `ResetAfterSideTurnStart` (LetterOpener, PaelsLegion) et `ResetAfterSideTurnEnd` (Kusarigama) complètent le modèle (`:196-223`).
- `RelicPredictionStateSupport.IsTracked` énumère **25** reliques suivies (`RelicPredictionStateSupport.cs:15-40`).
- `RelicCounterCatalog` mappe 11 reliques à période : HappyFlower 3, FakeHappyFlower 5, Pendulum 3, PollinousCore 4, PenNib 10, Nunchaku 10, TuningFork 10, JossPaper 5, IronClub 4, GalacticDust 10, MeatOnTheBone 2 (`RelicCounterCatalog.cs:10-23`).
- Reliques qui changent une **règle** et non un compteur : IceCream (skip reset énergie), RingingTriangle (skip hand flush), SturdyClamp (plafond de block retenu), SneckoSkull (montant de poison) — `PersistentRelicSupport.cs:13-78`.

---

## 3. Calcul de dégâts et de risque

### 3.1 `CombatPredictionRisk` — type interne, pas public

```csharp
// src/Engine/Common/CombatPredictionRisk.cs:6-9
internal sealed class CombatPredictionRisk(IReadOnlyList<CombatPredictionRiskEntry> entries)
    : PredictionRisk(entries.Count > 0)
{
    public IReadOnlyList<CombatPredictionRiskEntry> Entries { get; } = entries;
}
```

C'est un **transport de risque**, pas un calculateur. Base :

```csharp
// src/Engine/Common/PredictionRisk.cs:12-17
internal class PredictionRisk(bool hasRisk) {
    public static PredictionRisk None { get; } = new(false);
    public virtual bool HasRisk { get; } = hasRisk;
}
```

Motifs (`PredictionRiskReason`, `PredictionRisk.cs:3-10`) : `MethodNotMirrored`, `MethodMirrorIncomplete`, `UnresolvedPlayerChoice`, `CardDrawLimitExceeded`, `OrbChannelLimitExceeded`.

Entrée concrète : `internal sealed class CombatPredictionRiskEntry : CombatPredictionHistoryEntry { public required PredictionRiskReason Reason { get; init; } }` (`CombatPredictionHistoryEntry.cs:83-86`), avec `Index` et `Trace` hérités (`:76-81`).

Émission : `MethodMirrorRegistry` mappe `Inferred` → `RecordMethodMirrorIncompleteRisk()` et `Unsupported` → `RecordMethodNotMirroredRisk()` (`MethodMirrorRegistry.cs:240-272`), résolus en `History.RecordRisk(...)` (`CombatMirrorContext.cs:39-47`). Le scoring transforme cela en pénalité : `RiskPenalty = -2_000` quand un gap non compensé existe (`SolverWeights.cs:14`, `StateEvaluation.cs:504-505`).

**`PreCombatForecastRisk` n'existe pas** — recherche sur `src/` et `docs/` : ABSENT.

### 3.2 Pipeline de dégâts — ordre exact

Pour une attaque de carte : `ExecuteAttack(AttackCommand)` (`CombatPredictionSimulator.Attack.cs:83`) → `ModifyAttackHitCount` (`:120`) → par coup `Damage(targets, GetAttackDamageAmount(...), props, attacker, cardSource, cardPlay)` (`:147-153`). `GetAttackDamageAmount` ne fait **aucune** arithmétique de Strength/Weak/Vulnerable (`:221-232`).

Puis, par cible, dans `TryDamageTarget` :

| # | Étape | Référence |
|---|---|---|
| 1 | `HookMirrors.ModifyDamage(...)` | `CombatPredictionSimulator.Damage.cs:139` |
| 1a | enchantement : additif puis multiplicatif sur le montant brut | `HookMirrors.cs:785-790` |
| 1b | passe **additive** (Force, etc.) : `damage += additive` | `HookMirrors.cs:800-812` |
| 1c | passe **multiplicative** (Weak, Vulnerable, Surrounded…) : `damage *= multiplier` | `HookMirrors.cs:814-830` |
| 1d | `ModifyDamageCap` puis `Math.Max(0, Math.Min(damage, cap))` | `HookMirrors.cs:833-838` |
| 2 | `BeforeDamageReceived` (reçoit le montant déjà modifié, ne peut pas le changer) | `Damage.cs:149` ; facade `HookMirrors.cs:989-1014` |
| 3 | absorption par block : `blockTargetState.DamageBlock(modifiedAmount, props)` | `Damage.cs:159-161` |
| 4 | `ModifyHpLost` phase `BeforeOsty` | `Damage.cs:163-171` |
| 5 | redirection de cible (`ModifyUnblockedDamageTarget`, pets) | `Damage.cs:175-180` |
| 6 | `ModifyHpLost` phase `AfterOsty` + `AfterModifyingHpLostAfterOsty` | `Damage.cs:184-193` |
| 7 | soustraction de PV : `SimCreatureState.LoseHp` | `Damage.cs:199` ; impl `SimCreatureState.cs:59-72` |
| 8 | hooks post : `AfterBlockBroken` → `AfterCurrentHpChanged` → `AfterDamageGiven` → `AfterDamageReceived/Late` | `Damage.cs:280,287,292,304` |

Points durs :
- `LoseHp` est **le seul point de troncature decimal→int** du pipeline : `damage = (int)Math.Min(amount, 999999999m)` puis `CurrentHp = Math.Max(CurrentHp - damage, 0)` (`SimCreatureState.cs:63-64`).
- `DamageBlock` renvoie `props.HasFlag(ValueProp.Unblockable) ? 0 : Math.Min(Block, amount)` et fait `Block -= (int)blockedDamage` — le block est un `int`, donc troncature du bloqué (`SimCreatureState.cs:49-57`).
- Les moves ennemis passent par **exactement** le même pipeline : `DamagePlayer` pousse une source `MonsterMove` et appelle `simulator.Damage(player, baseDamage, ValueProp.Move, attacker)` (`MonsterMoveSemantics.cs:120-150`).
- Projection d'évaluation séparée (pas de block, pas de PV) : `CorePowerSupport.AdjustForecastAttack` appelle `HookMirrors.ModifyDamage` avec `ValueProp.Move` et renvoie `Math.Max(0, (int)Math.Floor(damage))` (`CorePowerSupport.cs:717-732`).

### 3.3 Weak et Vulnerable — sémantique exacte

Les deux sont enregistrés dans le **même** registre multiplicatif (`ModifyDamageMirrors.cs:102-103`).

**Weak** — `HandleWeakPower` (`ModifyDamageMirrors.cs:210-228`) :
- gate : `context.Dealer != power.Owner || !context.Props.IsPoweredAttack()` → renvoie `1` (`:212-213`) → **seul l'attaquant porteur de Weak est affecté, et seulement sur une attaque « powered »**.
- multiplicateur : `power.DynamicVars["DamageDecrease"].BaseValue` (`:215`) — le 25 % vient des **données du jeu**, pas d'une constante du dépôt.
- variantes : `PaperKrane.ModifyWeakMultiplier` (`:216-224`) et `DebilitatePower` qui double la réduction par `multiplier -= 1m - multiplier` (`:225-226`).

**Vulnerable** — `HandleVulnerablePower` (`:230-257`) :
- gate : `context.Target != power.Owner || !context.Props.IsPoweredAttack()` → `1` (`:234-235`) → **seule la cible porteuse de Vulnerable est affectée**.
- multiplicateur : `power.DynamicVars["DamageIncrease"].BaseValue` (`:237`).
- variantes : `PaperPhrog.ModifyVulnerableMultiplier` (`:238-246`), `CrueltyPower` ajoute `cruelty / 100m` (`:247-253`), `DebilitatePower` double l'augmentation (`:254-255`).

**Ordre Weak vs Vulnerable** : ABSENT. Aucun tri, aucune priorité, aucune doc — les deux passent dans la boucle des listeners dans l'ordre d'énumération (`HookMirrors.cs:814-830` sur la liste construite en `SimulatedCombatState.cs:1969-2062`). **Mais** chaque étape est une multiplication `decimal` sans arrondi, donc les deux commutent ; seuls le cap (`:833-836`) et le clamp final `Math.Max(0, …)` (`:838`) dépendent de l'ordre.

**Block vs Weak/Vulnerable** : block vient **strictement après** — les multiplicateurs s'appliquent à `modifiedAmount` avant `DamageBlock` (`Damage.cs:139` vs `:161`).

**Aucune constante `0.75` / `1.5` en dur** pour ces deux pouvoirs : le seul `1.5m` du dépôt est dans la gestion de `SurroundedPower` (`ModifyDamageMirrors.cs:188-189`).

### 3.4 `PreCombatForecastApi` — signatures exactes

```csharp
public static class PreCombatForecastApi            // src/Api/PreCombatForecastApi.cs:16
{
    public const int ApiVersion = 6;                                     // :18
    public const int DefaultWorkerIdleTimeoutMilliseconds = 120_000;     // :19
    public const int MinimumWorkerIdleTimeoutMilliseconds = 1_000;       // :20
    public const int MaximumWorkerIdleTimeoutMilliseconds = 86_400_000;  // :21

    public static bool IsAvailable { get; }                              // :26  (Windows && Enabled && !IsPreCombatWorker)
    public static string CaptureLiveStateToken(RunState run);            // :31
    public static Task StopWorkerAsync();                                // :36
    public static PreCombatWorkerStatus GetWorkerStatus();               // :39
    public static Task SetWorkerIdleTimeoutAsync(int? idleTimeoutMilliseconds);            // :45
    public static Task<PreCombatWorkerStatus> RestartWorkerAsync(RunState run, CancellationToken ct = default);            // :55
    public static Task<PreCombatWorkerStatus> RestartWorkerAsync(RunState run, int? idleTimeoutMilliseconds, CancellationToken ct = default); // :64
    public static Task<PreCombatForecastResult> SimulateAsync(RunState run, EncounterModel encounter,
        PreCombatRoomKind roomKind, PreCombatSimulationOptions? options = null, CancellationToken ct = default);           // :85
    public static Task<PreCombatForecastResult> SimulatePlanningAsync(RunState liveRun, SerializableRun plannedRun,
        EncounterModel encounter, int targetActFloor, int targetMapColumn, PreCombatRoomKind roomKind,
        PreCombatMapPointKind mapPointKind, PreCombatSimulationOptions? options = null, CancellationToken ct = default);   // :205
    public static Task<PreCombatForecastResult> ForecastAsync(RunState run, EncounterModel encounter,
        int targetActFloor, int targetMapColumn, PreCombatRoomKind roomKind, PreCombatMapPointKind mapPointKind,
        bool isSecondBoss = false, PreCombatForecastOptions? options = null, CancellationToken ct = default);              // :344
}
```

Contraintes codées en dur :
- `IsAvailable` = `OperatingSystem.IsWindows() && Entry.Enabled && !Entry.IsPreCombatWorker` (`:26-28`).
- `RestartWorkerAsync` **jette** `PlatformNotSupportedException` hors Windows (`:69-70`) et exige le thread principal (`:71-72`).
- `ForecastAsync` / `SimulateAsync` / `SimulatePlanningAsync` **ne jettent pas** : elles renvoient `PreCombatForecastStatus.Unsupported` avec le message « Windows only » (`:93-99`, `:217-223`, `:356-362`) et vérifient `NGame.IsMainThread()` (`:364-370`, `:100-106`, `:224-230`).
- `ValidateOptions` (`:616-652`) borne : `SearchBudgetMilliseconds` ∈ [1 000, 30 000] (`:627-628`), `OverallTimeoutMilliseconds` ≥ budget + 10 000 et ≤ 180 000 (`:629-633`), `MaxDegreeOfParallelism` ∈ [1, 16] (`:634-635`), cohérence `MapPointKind`/`RoomKind` (`:638-650`).

Sémantique : la prévision **ne construit jamais de combat dans le processus courant**. Elle sérialise le run complet, fabrique un token d'état opaque, puis lance un **processus de jeu Windows headless** possédant le même ensemble de mods ; le résultat n'est publié qu'après revalidation du run, du combat et du token (`README.md:44`, `:577-609`). Toute divergence renvoie `LiveStateChanged`.

### 3.5 `PreCombatForecastContracts` — champs exacts

| Type | Définition | Réf. |
|---|---|---|
| `PreCombatForecastStatus` | `Succeeded, Unsupported, LiveStateChanged, TimedOut, Cancelled, Failed` | `:7-15` |
| `PreCombatForecastConfidence` | `Complete, Bounded, DeathOnly` | `:18-23` |
| `PreCombatRoomKind` | `Normal, Elite, Boss` | `:26-31` |
| `PreCombatMapPointKind` | `Normal, Elite, Boss, Unknown, Event` | `:34-42` |
| `PreCombatMapStep` | `(MapCoord Coordinate, RoomType RoomType, MapPointType MapPointType)` | `:49-52` |
| `PreCombatForecastOptions` | `SearchBudgetMilliseconds=8_000`, `OverallTimeoutMilliseconds=60_000`, `MaxDegreeOfParallelism?`, `PlayerCurrentHpOverride?`, `InterveningMapPoints`, `CancelWorkerWhenCallerCancels`, `ForceRefresh`, `CloseWorkerAfterRequest`, `WorkerIdleTimeoutMilliseconds?=120_000`, `internal SimulationSeed?` | `:55-114` |
| `PreCombatSimulationOptions` | `SearchBudgetMilliseconds=8_000`, `OverallTimeoutMilliseconds=60_000`, `MaxDegreeOfParallelism?`, `ulong SampleSeed`, `CloseWorkerAfterRequest`, `WorkerIdleTimeoutMilliseconds?` | `:117-142` |
| `PreCombatWorkerStatus` | `required IsRunning`, `required IsBusy`, `ProcessId?`, `WorkingSetBytes?`, `PrivateMemoryBytes?`, `PeakWorkingSetBytes?`, `AudioMuted`, `IdleTimeoutMilliseconds?` | `:145-156` |
| `PreCombatPotionUse` | `(string Id, string Title, int Turn, int Slot)` | `:159-163` |
| `PreCombatForecastResult` | `required Status`, `required RequestId`, `ProjectedHpLoss?`, `PotionUses`, `SearchBoundary?`, `Confidence?`, `FinalHp?`, `CombatEndedTurn?`, `SearchElapsedMilliseconds?`, `TotalElapsedMilliseconds?`, `Error?`, `DiagnosticLogPath?`, `IsSuccess => Status == Succeeded` | `:166-182` |

Sémantique de `Confidence` : « quelle part du combat la route sélectionnée a **prouvée** » — `Complete`, `Bounded` (budget atteint), `DeathOnly` (`:17-23`). `ProjectedHpLoss` est la perte de PV **projetée sur tout le combat**, pas par tour (`README.md:42`).

### 3.6 Le seul évaluateur de risque réellement bon marché : `LiveEndTurnRiskEvaluator`

C'est la pièce la plus directement réutilisable pour F1.
```csharp
internal readonly record struct LiveEndTurnRiskProjection(int HpBefore, int HpAfter, int HpLost, bool PlayerDead, string MonsterMoves); // :14-19
public static LiveEndTurnRiskProjection Evaluate(CombatState state, IReadOnlyList<PlanCardChoice>? turnStartChoices);                    // :21-26
```
Il construit `new SimulatedCombatState(state)` (`:27`) puis rejoue **un seul** demi-tour : phase 1 de fin de tour, normalisations, pouvoirs de mort, flush de main, phase 2, puis bascule côté ennemi, `BeginSideTurn` par ennemi, clear de block, `TriggerSideTurnStart`, poison, puis l'exécution des moves (`:60-150`). Aucune recherche, aucune allocation de beam : coût O(nombre de moves × coups).

---

## 4. L'API publique v6 pour mods tiers

**Surface publique totale** : `namespace CombatSolver.Api` uniquement — `PreCombatForecastApi`, `CombatShowcaseApi` (+ 2 records), et les types de `PreCombatForecastContracts.cs`. Tout le reste de `src/Api/` est `internal` (`PreCombatLiveStateSnapshot.cs:13,18`, `PreCombatRunSerialization.cs:9`, `PreCombatForecastWorker.cs:12`). L'assembly du mod n'est **pas** publicisée, seule `sts2` l'est (`CombatSolver.csproj:53-56`).

**Contraintes d'appel** :

| Contrainte | Où |
|---|---|
| Thread principal du jeu obligatoire pour `ForecastAsync`/`SimulateAsync`/`SimulatePlanningAsync`/`RestartWorkerAsync` | `PreCombatForecastApi.cs:71-72, 100-106, 224-230, 364-370` |
| Windows obligatoire (sinon `Unsupported` ou `PlatformNotSupportedException`) | `:26-28, 69-70, 93-99` |
| Run solo, hors combat, pas déjà en train de charger une prévision | `README.md:65` ; revalidation `:577-586` |
| `EncounterModel` doit être enregistré dans le build courant (`ModelDb.GetByIdOrNull<EncounterModel>`) | `:107-113` |
| Les `InterveningMapPoints` doivent couvrir **chaque** rangée de carte jusqu'à la cible, et ne peuvent pas être Monster/Elite/Boss/Event | `:665-698` |
| `SimulatePlanningAsync` exige même acte, même seed, un seul joueur, même personnage, même `NetId` | `:270-282` |

**Ce qui est appelable depuis où** :
- Depuis un autre mod, sur le thread principal du jeu : tout ce qui précède. C'est la seule voie officielle.
- Depuis le worker lui-même : rien de la surface publique — `IsAvailable` est faux car `Entry.IsPreCombatWorker` est vrai (`:28`), et `Entry` refuse de démarrer l'UI dans ce cas (`SolverDispatcher.cs:49-59`).
- Depuis `Search`/`Engine` : rien — aucune API publique de recherche en combat, d'auto-play ou de réglages n'existe. `SolverController`, `SolverSettings`, `SolverOverlay` sont internes au mod. ABSENT.

**Mécanique du worker** (utile pour comprendre les limites) :
- `Process.Start` avec `UseShellExecute=false`, `CreateNoWindow=true` (`PreCombatForecastWorker.cs:782-800`) ; arguments `--headless --disable-vsync --max-fps 0 --force-steam=off --log-file …` (`:788-794`) ; variables `APPDATA`, `LOCALAPPDATA`, `COMBATSOLVER_HEADLESS=1`, `COMBATSOLVER_PRECOMBAT_WORKER=1` (`:795-798`).
- Communication **100 % fichiers JSON** : `combat_solver_test_request.json` / `_result.json` / `_ready.json` dans le user dir du worker (`:512-519`), polling 100 ms (`:803-829`). Pas de pipe, pas de socket, pas de stdout — ABSENT.
- Miroir du jeu par **hardlinks** `CreateHardLinkW` (`:159-164`, appel `:917`), avec `SlayTheSpire2.exe` codé en dur (`:504-506`).
- Worker réutilisé si la signature `gameRoot|userDataRoot|accountDataRoot|sha256(settings)|mods id@ver@path` correspond (`:471-483, 526-533`) ; timeout d'inactivité 120 s par défaut, `-1` = immortel (`:17, 543-586`).
- Cache de résultats terminés : clé = `StateToken|encounter.Id|floor|column|roomKind|mapPointKind|isSecondBoss|budget|timeout|dop|hpOverride|routeKey` (`PreCombatForecastApi.cs:427-440`), plafonné à 32 entrées puis `Clear()` (`:515-517`) — **éviction brutale, pas LRU**.

---

## 5. Modèles et structures de données réutilisables

| Structure | Forme exacte | Pourquoi c'est réutilisable |
|---|---|---|
| `CanonicalModels` | `static T Power<T>() / Card<T>() / Relic<T>() / Potion<T>() / Monster<T>() / Affliction<T>() / Enchantment<T>() / Orb<T>()` avec cache `static` par type générique fermé (`CanonicalModels.cs:19-84`) | `ModelDb.X<T>()` slugifie le nom de type par **trois** `Regex.Replace` à chaque appel ; ce cache supprime ce coût sur le chemin chaud (`:13-17`). Directement transposable. |
| `DynamicVarSetAccess` | `TryGetVars`, `RequireVars`, `Entries`, `Keys`, `Values`, + `EntryEnumerable`/`EntryEnumerator` struct (`DynamicVarSetAccess.cs:40-100`) | Accède au dictionnaire privé `_vars` via un délégué compilé une fois (`:29-38`), avec repli sur l'énumération publique si le champ disparaît. **Exactement ce qu'il faut pour lire `DamageDecrease`/`DamageIncrease` sans casser à la prochaine version.** |
| `StateFingerprint` | `readonly record struct StateFingerprint(ulong First, ulong Second)` + `StateFingerprintBuilder` à deux chaînes indépendantes (FNV-like + rotate-multiply) (`StateFingerprint.cs:5-57`) | Clé d'égalité d'état à 128 bits, sans collision pratique, sans allocation. Utilisable pour un cache de prévision par état. |
| `PredictedCard` | `Original` / `Preview` / jeton / `Shared` avec `MutablePreview` obligatoire à l'écriture (`PredictedCard.cs:13-18, 99-126`) | Modèle COW propre pour « je simule une carte sans toucher l'originale ». |
| `PredictionStateStore` | `Dictionary<(AbstractModel, Type), object>` + alias (`PredictionStateStore.cs:10-11, 44-60, 88-112, 159-231`) | Side-table qui garde les objets du jeu **intacts** tout en stockant l'état par branche. `Peek` n'insère pas, exprès (`:88-112`). |
| `PlanAction` | 22 champs : `Kind, Turn, CardId, CardOccurrence, TargetIndex, TargetCombatId?, CardTitle, TargetName, Choice?, NestedChoices?, NestedChoicesBeforePrimary, PotionSlot, PotionId, PotionTitle, TurnStartChoices?, RelicEffects?, ReplayCount, CardStateKey, CardStateOccurrence, EndsPlayerTurn, CardUpgradeLevel, CardEnchantmentId` + `IsExecutable`, `GetActionChoicesInExecutionOrder()` (`CombatPlan.cs:132-171`) | Format de plan **entièrement sérialisable et pur valeur** (aucune référence de nœud) — bon candidat pour un format d'export ULTIMA. |
| `PlanCardToken` | `(CardId, UpgradeLevel, StateKey, SourceOccurrence, OptionOccurrence, Title)` (`CombatPlan.cs:102-108`) | Identité de carte stable sans référence d'objet. |
| `RelicCounterCatalog` | 11 reliques → période + `Identify` + `UnavailableReason` (`RelicCounterCatalog.cs:10-47`) | Table de données directement copiable pour F5. |
| `MonsterValueReader` | Accesseurs compilés par `(type, name)` (`MonsterValueReader.cs:15-36`) | Lire des compteurs privés de monstre sans réflexion par appel. |
| `ForkableList/Dictionary/Set` | COW par `volatile bool Shared` (`ForkableCollections.cs:42-46, 85-90`) | Structure de collection COW générique et testée. |
| `SearchPolicySnapshot` | record à 18 paramètres positionnels + options `init` (`SearchPolicySnapshot.cs:3-50`) | Bon exemple de « politique gelée » immuable passée à la recherche, sans singleton. |

---

## 6. Techniques de performance

**Budgets** — `SolverSearchProfile.Default` : `BeamWidth 60`, `MaxExpandedNodes 120_000`, `MaxCardBranchesPerNode 32`, `MaxPileChoiceBranchesPerAction 18`, `MaxHandChoiceBranchesPerAction 24`, `SoftTimeBudgetMilliseconds 120_000` (`SolverSearchProfile.cs:49-55`). Paliers joueur (`SolverSettings.cs:180-205`) :

| Palier | Beam | Nœuds | Branches carte | Choix pile | Choix main | Temps |
|---|---:|---:|---:|---:|---:|---:|
| Bas | 45 | 60 000 | 24 | 12 | 16 | 60 s |
| Moyen (défaut) | 60 | 120 000 | 32 | 18 | 24 | 120 s |
| Haut | 90 | 250 000 | 48 | 28 | 36 | 180 s |
| Très haut | 135 | 500 000 | 72 | 42 | 54 | 300 s |

**Boucle principale** : `while (frontier.Count > 0 && … && _run.Expanded < _profile.MaxExpandedNodes && !timeBudgetReached && !memoryNoProgressTruncated)` (`CombatBeamSolver.Phases.cs:1479-1485`), avec boucle interne de profondeur de jeu (`:1525-1527`). Le compteur de nœuds est incrémenté **par parent admis**, dans `TryAdmitExpansionParent` (`CombatBeamSolver.ExpansionPlan.cs:34`).

**Cache / transpositions** :
- Deux `Dictionary<StateFingerprint, TranspositionFrontier>` (admission et expansion) (`CombatBeamSolver.Models.cs:112-113`). La valeur est une **frontière de labels non dominés** à 5 dimensions, pas un simple meilleur score (`Transpositions.cs:22-55`) : `<=` sur 5 dims et `Score >=`.
- Plafond `TranspositionEntryLimit = 1_000_000` (`SearchPolicySnapshot.cs:45-48`) ; au plafond les nouveaux états sont admis **sans être enregistrés** (`CombatBeamSolver.Expansion.Candidates.cs:869-877`) : **pas d'éviction LRU, un cutoff**.
- `StandPatCache` et `ThreatProjectionCache` (clé `(fingerprint, roundIndex)`) (`CombatBeamSolver.Models.cs:117,125`).
- `CanonicalModels` : cache par type générique fermé (§5).
- `SolvedRouteCache` : clé = `SHA256` sur un blob JSON d'identité (schéma, ids/versions de mods, seed, acte, étage, ascension, encounter, état de combat sérialisé, `ContinuationStamp.StateText`, snapshot de dégâts, ~30 champs de politique) (`SolvedRouteCache.cs:26-87`), stocké en `user://combat-solver-routes/<hex>.json` (`:88-89`), 64 entrées max (`:17, 159-168`). Invalidation **par construction** (tout changement change le hash) ; fichier corrompu renommé `.bad` (`:96-118`).

**Parallélisme** — le point le plus intéressant du dépôt :
- Partition en **vagues ordonnées par parent** : une vague admet ≤ `acceptedCapacity` parents, chacun produisant des jobs (Prepare/Action/Choice/Potion/Tail) dispatchés sur un pool de lanes fixe (`CombatBeamSolver.AdmittedExpansion.cs:104-181`).
- Lanes = `Thread` dédiés créés une fois par `Solve`, `ThreadPriority.BelowNormal`, handshake `AutoResetEvent` + `lock(_gate)` (`CombatBeamSolver.ParallelExpansion.cs:265-328`).
- Chaque lane possède son **solver worker** privé, clone complet avec callback de progression neutralisé (`:394-419`).
- État partagé (transpositions, dominance, admission, rangs, acceptation des nœuds) **uniquement sur le thread coordinateur** (`AdmittedExpansion.cs:158-171, 186-206`).
- Déterminisme par **commit dans l'ordre des parents** : `CommitReadyParents()` ne valide que le préfixe `parents[0..committed]` dont `TailCompleted` est vrai, et jette si le compte ne correspond pas (`AdmittedExpansion.cs:158-171, 205-206`) ; l'appelant vérifie en plus l'ordre de soumission (`Phases.cs:1898-1905`).
- Le fork est **sérialisé par parent** : « A parent simulator cannot be forked concurrently » — la lane prend un `ReplayForkSeed` sous `replayForkGate` puis ne consomme que son fork privé (`ParallelExpansion.cs:64-92, 421-511`).

**Mémoire / GC** :
- `SearchGcPolicy` : seuil de reclaim 256 MiB, délai de release 250 ms, limite de pression système 95 %, région NoGC minimale 512 MiB, part minimale région/config 50 % (`SearchGcPolicy.cs:10-23`). Budget NoGC par défaut **16 GiB** (`SolverSettings.cs:120, 171-172`).
- Capacité effective = `min(configuré, (limite_système − charge) + heap_réutilisable)`, avec `limite_système = HighMemoryLoadThreshold × 95/100` (`SearchGcPolicy.cs:2653-2671`).
- La pression mémoire gate le parallélisme à trois endroits : DoP plafonné à 2 en mode conservateur (`Phases.cs:168-175`), capacité de vague `min(2, dop)` (`:1721-1725`), et `MemorySafeParallelWaveCapacity` = `ParentWaveCapacity(desired, highWater, RemainingBytes)` (`:1730-1741`).
- Arrêt propre : `ConsecutiveNoProgressReclaims >= policy.MemoryNoProgressRecoveryLimit` → `SEARCH_MEMORY_NO_PROGRESS_STOP` et drain de la frontière vers EndTurn (`Phases.cs:1197-1212, 1530-1542`).

**FastLaneVerification** :
```csharp
internal static readonly bool Enabled =
    Environment.GetEnvironmentVariable("COMBATSOLVER_VERIFY_FAST_LANES") == "1"
    || Environment.GetEnvironmentVariable("COMBATSOLVER_VERIFY_HOOK_MASK") == "1";
```
(`FastLaneVerification.cs:18-20`). Principe documenté : une « fast lane » remplace un chemin coûteux par un chemin bon marché censé produire le même résultat ; chaque lane **rejoue le chemin original en parallèle et jette à la première divergence** (`:3-14`). Huit sites : `AfterBlockBroken`, `AfterCardPlayed`, `AfterCardPlayedLate`, `AfterModifyingHpLostAfterOsty`, `AfterAttack`, `AbortAttack`, `CaptureAfterPlayExecutionHookListeners`, et le tri d'insertion de `DeathLifecycle`. Les divergences jettent `InvalidOperationException` (`HookMirrors.cs:1520-1522`, `SimulatedCombatState.DeathLifecycle.cs:333-334`). Le flag est lu **une fois** au chargement de la classe.

---

## 7. Ce qui est portable tel quel sur Linux, ce qui ne l'est pas

### 7.1 Portable et vérifié

| Élément | Preuve |
|---|---|
| **Tout le moteur de recherche et de simulation** (`src/Engine`, `src/Search`, `src/Prediction`) | Aucun P/Invoke, aucune gate OS dans ces répertoires ; `grep DllImport` ne renvoie que `src/Api`, `src/Diagnostics`, `src/Runtime`. |
| Harness de recherche hors Godot : `tools/search/OfflineSearchHarness` | Console .NET 9 qui charge `sts2.dll` **sans démarrer Godot**, construit un combat et appelle `CombatRootSnapshot.Capture` + `CombatSearchCoordinator.Solve` (`docs/OFFLINE_SEARCH_HARNESS.md:3-8`) ; résolution Steam multi-plateforme `data_sts2_linuxbsd_x86_64` (`OfflineSearchHarness.csproj:9-30`). |
| Headless Linux du jeu complet | `tools/testing/run-unattended-test.sh:1240-1258` lance `SlayTheSpire2 --headless --disable-vsync --max-fps 0 --force-steam=off` avec `DISPLAY=` et `WAYLAND_DISPLAY=` **vidés**. Aucun X, aucun Xvfb. |
| Modèle de clonage, `PredictionStateStore`, `PredictedCard`, `ForkableCollections`, `StateFingerprint`, `CanonicalModels`, `DynamicVarSetAccess` | Pur C#, zéro dépendance OS. |
| Types de contrat `PreCombatForecast*` | Records purs (`PreCombatForecastContracts.cs:49-182`) — compilables et lisibles sur Linux, même si le service renvoie toujours `Unsupported`. |
| `PreCombatForecastApi.CaptureLiveStateToken`, `StopWorkerAsync`, `GetWorkerStatus`, `SetWorkerIdleTimeoutAsync` | Aucune gate OS ; fonctionnent sur Linux (`PreCombatForecastApi.cs:31, 36, 39, 45`). |
| `CombatShowcaseApi.GetCompatibility` / `EnterAsync` | Aucune gate plateforme ; seules des gates main-thread / mono-import / version de bundle (`CombatShowcaseApi.cs:18-22`, `CombatShowcaseRuntime.cs:42-58`). **C'est la seule fonctionnalité non triviale pleinement Linux-compatible.** |

### 7.2 Non portable — inventaire précis

**P/Invoke (tous des `DllImport`, aucun `LibraryImport` — ABSENT)**

| Symbole | DLL | Usage | Portabilité |
|---|---|---|---|
| `CreateHardLinkW` | `kernel32` | miroir du jeu pour le worker (`PreCombatForecastWorker.cs:159-164`, appel `:917`) | **Linux : impossible tel quel.** Sur Linux, hardlink via `link(2)` ou copie `cp -aL --reflink=auto` (ce que fait déjà `tools/testing/headless-runtime.sh:314`). |
| `GetCurrentThreadId` | `kernel32` | log perf, seulement si Windows, sinon `0` (`PerformanceRecording.cs:97, 316-317`) | Dégradation propre. |
| `GetProcessMemoryInfo` | `psapi` | échantillon mémoire natif (`PerformanceSession.cs:167-168`, appel `:144`) | Jamais atteint sur Linux (gate `:141`). |
| `GlobalMemoryStatusEx` | `kernel32` | mémoire physique (deux déclarations indépendantes : `PerformanceSession.cs:169-170` et `PhysicalMemoryUsage.cs:56-58`) | Repli `GCMemoryInfo` (`PhysicalMemoryUsage.cs:13-18`). |
| `GetProcessIoCounters` | `kernel32` | compteurs I/O (`PerformanceSession.cs:171-172`) | Gate `:141`. |
| `Shell_NotifyIconW` | `shell32` | balloon de fin de recherche (`SearchCompletionNotifier.cs:197-203`) | Désactivé (`:50-51`). |
| `LoadIconW`, `GetForegroundWindow`, `GetWindowThreadProcessId` | `user32` | icône + détection de premier plan (`:205-216`) | Désactivé (`:82-83, 144-145`). |
| `OpenProcessToken`/`LookupPrivilegeValue`/`AdjustTokenPrivileges`/`NtSetSystemInformation` | `advapi32`/`ntdll` | helper `CombatSolver.MemoryCleaner`, projet séparé `net48`/`WinExe`/`x64` (`tools/runtime/CombatSolver.MemoryCleaner/Program.cs:101-133`, `.csproj:1-8`) | **Windows uniquement, jamais chargé sur Linux.** |

**Gates `OperatingSystem.IsWindows()` et comportement Linux**

| Site | Effet sur Linux |
|---|---|
| `PreCombatForecastApi.cs:26` | `IsAvailable=false` → toute la prévision pré-combat est indisponible. |
| `PreCombatForecastWorker.cs:46` | pinning des sources de mods sauté ; plus tard `ResolvePinnedModSource` **jette** `InvalidOperationException` (`:115-116`). |
| `PerformanceRecording.cs:97` | `nativeMainThread = 0`. |
| `PerformanceSession.cs:141` | `NativeMemory(Available:false, 0…)`, pas d'exception. |
| `PhysicalMemoryUsage.cs:13-18` | repli `GCMemoryInfo`, `IsPhysicalSample=false`. |
| `SearchCompletionNotifier.cs:50, 82, 144` | notification silencieusement désactivée. |
| `SearchGcPolicy.cs:729-733, 763, 2139-2149, 2622-2626` | budget plus conservateur, `system_pressure_source=allocation_projection`. |
| `SystemMemoryReleaseService.cs:15-16` | **jette** `PlatformNotSupportedException` ; rattrapé par l'UI (`SolverOverlay.cs:2416-2421`) mais le bouton est créé inconditionnellement (`:2390-2394`). |
| `UnattendedTestRunner.ScenarioBuilder.cs:751-752` | self-test de hardlink sauté. |

Il n'existe **aucune** branche `OperatingSystem.IsLinux()` ni aucun `RuntimeInformation.IsOSPlatform` dans `src/` : Linux est toujours le `else` implicite (ABSENT). Le second gate, indépendant de l'OS, est `DisplayServer.GetName() == "headless"` (`OnlinePresence.cs:47-48`, `RunStatistics.cs:39`, `SearchCompletionNotifier.cs:52`, `RuntimeGcStartup.cs:16-17`).

**Chemins et exécutables codés en dur**
- `"SlayTheSpire2.exe"` (`PreCombatForecastWorker.cs:504`) — casserait sur une install Linux.
- `"data_sts2_windows_x86_64"` dans la liste de miroir (`:704`) ; Linux utilise `data_sts2_linuxbsd_x86_64` (`CombatSolver.csproj:34`).
- `APPDATA` / `LOCALAPPDATA` comme variables d'environnement (`:795-796`) — sans effet sous Linux (Godot utilise `XDG_*`).
- `Environment.SpecialFolder.ApplicationData` + `"SlayTheSpire2"` (`PreCombatLiveStateSnapshot.cs:61-63`) — résout `$XDG_CONFIG_HOME` sur Linux, **pas** le `user://` de Godot (`$XDG_DATA_HOME`, cf. `run-unattended-test.sh:512`).
- `ProcessStartInfo("powershell.exe")` (`PerformanceRecording.cs:105`) — opt-in et échec capturé (`:115-120`).
- `"CombatSolver.MemoryCleaner.exe"` + `Verb="runas"` (`SystemMemoryReleaseService.cs:9, 39-45`).
- `D:/Steam` en probe de build, conditionné `'$(OS)' == 'Windows_NT'` (`CombatSolver.csproj:29`, `local.props.example:3-5`).
- Aucun accès registre (ABSENT), aucune API console (ABSENT).

**Version** : `min_game_version: "0.111.0"` (`CombatSolver.json:10`), `RitsuLibReferenceTarget 0.111.0` (`CombatSolver.csproj:36-37`), `GameVersion: "0.111.0"` en dur dans le catalogue de couverture (`tools/inspection/CoverageCatalog/Program.cs:200,208,239,245,602`). Pour ULTIMA en **0.107.1**, le manifeste exclut l'installation et le chemin RitsuLib n'existe pas.

---

## 8. Idées réutilisables pour un calcul instantané (F1 / F2)

Tout ce qui suit s'obtient **sans** beam search, en lisant l'état vivant — c'est la partie que le dépôt appelle lui-même « projection » (`LiveEndTurnRiskEvaluator`, `ProjectHpAfterThreat`).

### F2 — index du coup létal (16×3)

La donnée existe déjà, par couple (coup, dégât) : `ForecastAttackHit(int Damage, int BaseDamage)` et la boucle `for (int i = 0; i < Math.Max(attack.Repeats, 1); i++)` (`IntentForecaster.cs:126-127`). Trois sources possibles pour le dégât par coup, par ordre de fidélité :

1. **`attack.GetSingleDamage(state.PlayerCreatures, monster.Creature)`** — utilisé par le forecaster (`:125`). Prend en compte les modificateurs natifs de l'intent.
2. **`CorePowerSupport.AdjustForecastAttack(...)`** — passe le montant dans `HookMirrors.ModifyDamage` avec `ValueProp.Move` et renvoie `(int)Math.Floor(...)` (`CorePowerSupport.cs:717-732`). **C'est le seul chemin qui applique Weak/Vulnerable/Force exactement comme le combat**, sans rejouer le combat.
3. `CaptureBaseDamage` (`IntentForecaster.cs:31-44`) pour le montant **brut**, utile pour afficher « 16 (base 16) ».

Algorithme de létalité par coup, aligné sur le pipeline réel :
```
reste = hpCible ; block = blockCible
pour chaque hit h dans [0 .. repeats-1] :
    d = AdjustForecastAttack(base = h.damage, attacker, cible)   // Weak/Vuln inclus
    absorbé = min(block, d) ; block -= absorbé                  // SimCreatureState.cs:49-57
    reste -= (d - absorbé)                                      // LoseHp, SimCreatureState.cs:63-64
    si reste <= 0 → létal au coup h, overkill = -reste
```
Points de vigilance mesurés dans le code : le block est un `int` donc l'absorbé est tronqué (`SimCreatureState.cs:55`) ; il n'y a **qu'une** troncature, dans `LoseHp` (`:63`), pas dans `ModifyDamage` ; `Vulnerable` s'applique avant le block (`Damage.cs:139` vs `:161`).

**Garde-fou d'exactitude** : le dépôt refuse de prétendre à l'exactitude quand `attack.DamageCalc` est dynamique sur un move non listé (`IntentForecaster.cs:128-132`) et expose `IsExactForModeledDamage` (`:103`). Reproduire ce booléen est plus utile que de forcer un chiffre : c'est ce qui distingue « 16×3, létal » de « 16×3 environ, létal peut-être ».

### F1 — dégâts hypothétiques sous Weak / Vulnerable

- Le multiplicateur n'est **pas** une constante : lire `power.DynamicVars["DamageDecrease"].BaseValue` (Weak) et `["DamageIncrease"].BaseValue` (Vulnerable) (`ModifyDamageMirrors.cs:215, 237`). Sur la version courante ce sont 0.25 / 0.5, mais les lire évite de casser sur une relique ou un patch.
- Les gates sont directionnels et valent d'être copiés tels quels : Weak ne s'applique que si `Dealer == owner` **et** `props.IsPoweredAttack()` (`:212-213`) ; Vulnerable ne s'applique que si `Target == owner` **et** `IsPoweredAttack()` (`:234-235`). Une attaque non « powered » (poison, orbe, HP loss) n'est affectée ni par l'un ni par l'autre.
- Ordre : peu importe entre Weak et Vulnerable (multiplications `decimal` commutatives, pas d'arrondi intermédiaire) ; mais **toujours avant le block**, et le clamp final est `Math.Max(0, Math.Min(damage, cap))` (`HookMirrors.cs:833-838`).
- Cas à ne pas oublier : `PaperKrane` (réduit l'effet de Weak sur le joueur), `PaperPhrog` (augmente Vulnerable), `Cruelty` (+`cruelty/100`), `Debilitate` (double la réduction/ augmentation) (`:216-255`).
- Pour un « et si j'appliquais Weak », il suffit d'appliquer le même calcul avec `Dealer = joueur` et de multiplier par `(1 − DamageDecrease)` — mais **le dépôt ne fournit pas cette fonction** : ABSENT. C'est une ligne à écrire côté ULTIMA.

### Ce qu'il ne faut PAS attendre d'un calcul instantané

- Aucune routine de « lethal check » dédiée n'existe : `IsCompleteVictory(actionCount, allEnemiesDead, playerDead, projectedPlayerHp)` est un simple prédicat (`SolverInterimResultOrdering.cs:5-13`), et le vrai `ProjectHpAfterThreat` est privé au solveur (`CombatBeamSolver.StateEvaluation.cs:1352`).
- La létalité « propre » (zéro dégât subi, victoire complète) est définie séparément : `IsProvenZeroDamageRoute` exige victoire complète + 0 potion explicite + `FutureSoldHp == 0` + `ProjectedBattleHpLost - BattleHpLostSoFar == 0` + PV au max (`CombatSearchCoordinator.cs:748-758`).

---

## 9. Pièges et dette technique visible

1. **Whitelist de monsters en dur, ~150 entrées, avec doublons.** `IsKnownStableAttack` est une expression `(type, moveId)` de 110 lignes (`IntentForecaster.cs:155-264`) où `Chomper/CLAMP_MOVE` apparaît deux fois (`:171` et `:201`), `TerrorEel` deux fois (`:222`, `:250`), `Fabricator` deux fois (`:178`, `:255`), `FrogKnight` deux fois (`:184`, `:256`). Chaque monstre ajouté au jeu exige une édition manuelle, et l'oubli dégrade silencieusement `IsExactForModeledDamage` en `false`.
2. **`MonsterMoveEffects.Apply` est un `switch` géant dont le résultat booléen est jeté.** `default: return false` (`MonsterMoveEffects.cs:949-950`), et le retour est ignoré à l'appel (`MonsterMoveSemantics.cs:92-98`) : la moitié non-attaque d'un move non listé **ne fait rien, sans erreur**.
3. **Conditionnelles de branche codées en dur pour neuf monstres** (`BranchMonsterAi.cs:267-306`) ; tout autre cas retombe sur la sélection capturée à la racine ou jette (`:307-310`).
4. **Garde-fous « 32 étapes » répétés** dans la marche de machine à états (`IntentForecaster.cs:290-317`, `BranchMonsterAi.cs:203-204`) : un cycle de machine à états produit une exception, pas une dégradation.
5. **`AdjustMonsterMoveDamage` ne compense que trois cas** (TheForgotten/DREAD, PRESSURE_GUN_MOVE, EXPLODE_MOVE) et renvoie sinon le dégât de base snapshotté (`SimulatedCombatState.cs:1369-1381`) — un buff de monstre non modélisé passe inaperçu.
6. **`catch (Exception)` dans la complétion du cache** qui log et continue (`PreCombatForecastApi.cs:520-523`), en contradiction directe avec la règle du dépôt (`AGENTS.md:103`).
7. **Cache de résultats à éviction brutale** : `if (Completed.Count >= 32) Completed.Clear();` (`PreCombatForecastApi.cs:515-517`).
8. **Duplication de P/Invoke** : `GlobalMemoryStatusEx` déclaré deux fois, dans deux fichiers, avec deux structures (`PerformanceSession.cs:169-170`, `PhysicalMemoryUsage.cs:56-58`).
9. **`Entry` n'a pas de Dispose.** `CombatManager.Instance.TurnStarted += OnTurnStarted` (`Entry.cs:65`) n'a aucun `-=` dans tout `src/` (recherche exhaustive) ; le teardown passe par `TreeExiting` (`:124-125`).
10. **Bouton UI créé sans condition, action qui jette sur Linux** (`SolverOverlay.cs:2390-2394` vs `SystemMemoryReleaseService.cs:15-16`).
11. **Version figée dans cinq endroits différents** : manifeste (`CombatSolver.json:10`), csproj (`:36-37`), `local.props.example:17`, csproj du catalogue (`CoverageCatalog.csproj:16-18`), code du catalogue (`Program.cs:200,208,239,245,602`). Cinq sources de vérité à synchroniser à la main.
12. **Le RNG ne peut pas être fast-forwardé** : `Advance` consomme `NextUnsignedLong()` par valeur parce que « v0.109.0 a supprimé la reconstruction par compteur » (`PredictionExtensions.cs:52-62`). Toute tentative de sauter N tirages est O(N).
13. **`Rng` non partageable entre branches** : le fork fige l'état (`CombatPredictionRngSet.cs:29-32`) ; oublier de forker un flux produit une corruption silencieuse inter-branches.
14. **Invariant « un seul règlement par sémantique » non vérifié automatiquement** — c'est une règle de revue (`AGENTS.md:99`), avec au moins deux incidents documentés (`PowerLifecycleSupport.cs:86-87`, `CombatPredictionSimulator.PowerAmounts.cs:16-21`).
15. **Documentation en chinois majoritaire** (README, AGENTS.md, ARCHITECTURE.md, commentaires de code) : coût de maintenance réel pour une équipe non sinophone, et les noms de mécaniques du jeu y sont en traduction officielle chinoise (`AGENTS.md:201`).
16. **Le code de `src/Search` est un partial de ~30 fichiers pour une seule classe** (`CombatBeamSolver`) : aucune frontière de compilateur, tout est accessible depuis n'importe quel partial.
17. **`SolverController.cs` fait 3 714 lignes** avec ~40 points d'entrée publics et un `AssertMainThread()` dupliqué en garde (`:3709-3713`) : un oubli de garde n'est pas détecté à la compilation.
18. **Le worker Windows ne peut pas être testé sur Linux** : le seul chemin qui l'exerce est `run-unattended-test.ps1`, et la variante `.sh` ne pose **pas** `COMBATSOLVER_PRECOMBAT_WORKER` (`run-unattended-test.sh:1243-1257`), donc elle n'emprunte pas le même chemin de code.

---

## 10. Licence et contraintes de réutilisation

**LICENSE** (texte intégral du fichier, 1 154 octets) :

```
MIT License

Copyright (c) 2026 Torch1230 and contributors
Copyright (c) 2026 hotwords123 (portions derived from Random Foreseer)
```

Suivi du texte MIT standard (permission d'usage/copie/modification/fusion/publication/distribution/sous-licence/vente, clause de copyright à inclure, fourniture « AS IS », exclusion de garantie).

**THIRD_PARTY_NOTICES.md** :
- Le cœur de simulation intégré **utilise et modifie des portions de Random Foreseer**, avec permission de l'auteur original (`THIRD_PARTY_NOTICES.md:5`).
- Projet : Random Foreseer, auteur **hotwords123**, <https://github.com/hotwords123/StS2.RandomForeseer>, Workshop id 3747531952 (`:7-11`).
- Licence amont : MIT (`:15-19`).
- Permission grant du 2026-08-28 : non exclusive, gratuite, perpétuelle, **sous quatre conditions** (`:23-28`) :
  1. conserver l'attribution à hotwords123 ;
  2. conserver un lien vers Random Foreseer ;
  3. décrire fidèlement la relation de source entre Combat Solver et Random Foreseer ;
  4. inclure un fichier LICENSE ou NOTICE contenant ces informations **avec chaque distribution binaire**.
- Périmètre : le code Random Foreseer et les portions dérivées restent sous copyright de hotwords123, distribués sous MIT ; le reste (recherche, déploiement, UI, diagnostics, tests, docs) est copyright Torch et contributeurs, aussi sous MIT (`:32-35`).
- Aucune dépendance d'exécution à l'assembly Random Foreseer (`:37`).
- Le fichier doit être conservé dans les distributions source et inclus avec chaque binaire (`:30`).

### Conséquences concrètes pour ULTIMA

| Action | Statut |
|---|---|
| Lire le code pour comprendre les méthodes, réimplémenter les idées | **Autorisé**, aucune restriction (MIT + analyse). |
| Copier des fichiers dans ULTIMA (projet interne) | **Autorisé** sous MIT, à condition d'inclure le copyright `Torch1230 and contributors` **et** `hotwords123 (portions derived from Random Foreseer)` + le texte MIT. |
| Copier des portions issues de Random Foreseer (le cœur de simulation, les forks, l'historique, les miroirs) | **Autorisé** mais déclenche les 4 conditions du grant : attribution hotwords123, **lien vers le dépôt Random Foreseer**, description de la relation de source, NOTICE joint à chaque binaire distribué. |
| Distribuer ULTIMA (binaire) avec du code copié | **Obligation** de joindre LICENSE + THIRD_PARTY_NOTICES (ou un NOTICE équivalent reprenant les deux copyrights et le lien). |
| Sous-licencier / vendre | **Autorisé** par MIT, mais les notices doivent suivre. |
| Utiliser le nom « Combat Solver » ou « Random Foreseer » pour promouvoir ULTIMA | **Non couvert** par MIT (pas de grant de marque). À éviter. |
| Extraire des tables de données (RelicCounterCatalog, whitelists de moves, périodes) | Couvert par MIT comme du code, même logique d'attribution. |

**Recommandation** : pour ULTIMA, la voie la moins coûteuse juridiquement est de **réimplémenter** les idées des §2–§3 et §8 (algorithmes, ordre du pipeline, gates Weak/Vulnerable) plutôt que de copier des fichiers entiers. Les idées ne sont pas protégeables ; le code l'est, mais sous MIT donc facilement réutilisable *avec* notice. Si un seul fichier est copié (`RelicCounterCatalog.cs`, `CanonicalModels.cs`, `DynamicVarSetAccess.cs` sont les meilleurs candidats), ajouter un `THIRD_PARTY_NOTICES.md` dans ULTIMA avec les deux copyrights et le lien Random Foreseer suffit à couvrir les quatre conditions.

---

## 10 idées concrètes à voler pour ULTIMA

Classées par **rapport gain / effort** décroissant. « Gain » = ce que ça apporte aux features F1/F2/F5 ; « Effort » = ce qu'il faut écrire côté ULTIMA.

| # | Idée | Gain | Effort | Pourquoi |
|---|---|---|---|---|
| 1 | **Table `(monster, moveId) → (baseDamage, repeats)` figée au chargement**, comme `BranchMonsterStaticSnapshot.Capture` (`BranchMonsterAi.cs:40-90`) | ★★★★★ F2 | ★ | Une seule passe sur les `MoveState` au démarrage, puis zéro réflexion en combat. Donne directement le 16×3. |
| 2 | **`AdjustForecastAttack` : passer le dégât d'intent dans `ModifyDamage` avec `ValueProp.Move`** (`CorePowerSupport.cs:717-732`) | ★★★★★ F1 | ★★ | Applique Weak/Vulnerable/Force exactement comme le jeu, sans rejouer un combat. C'est *la* brique qui rend F1 juste plutôt qu'approximatif. |
| 3 | **Lire les multiplicateurs depuis `DynamicVars["DamageDecrease"|"DamageIncrease"]` au lieu de coder 0.25/0.5** (`ModifyDamageMirrors.cs:215,237`) + `DynamicVarSetAccess` comme pont (`DynamicVarSetAccess.cs:29-48`) | ★★★★ F1 | ★ | Immunise contre les reliques (`PaperKrane`, `PaperPhrog`, `Cruelty`, `Debilitate`) et les changements de version. |
| 4 | **Index de létalité par coup avec absorption de block séquentielle** (§8), branché sur `SimCreatureState.DamageBlock`/`LoseHp` comme référence sémantique (`SimCreatureState.cs:49-72`) | ★★★★★ F2 | ★★ | Répond exactement à « 16×3 : lequel tue ? ». Une boucle de 3 itérations, aucun état. |
| 5 | **Booléen de confiance `IsExactForModeledDamage` + liste des raisons** (`IntentForecaster.cs:19-27, 103`) | ★★★★ F1/F2 | ★ | Afficher « exact » vs « estimé » vaut mieux qu'un chiffre faux. Le dépôt montre comment accumuler `UnsupportedDetails`/`ApproximationDetails` en `HashSet<string>` triés. |
| 6 | **`LiveEndTurnRiskEvaluator` : rejouer *un seul* demi-tour sur un `SimulatedCombatState` jetable** (`LiveEndTurnRiskEvaluator.cs:21-150`) | ★★★★ F1 | ★★★ | Donne la perte de PV réelle du tour ennemi, block et morts compris, sans beam. C'est le pont entre « instantané » et « simulation profonde » pour plus tard. |
| 7 | **`CanonicalModels` : cache `static` par type générique fermé** (`CanonicalModels.cs:37-83`) | ★★★ F5 | ★ | Supprime trois `Regex.Replace` par lookup de modèle sur chaque tick de recherche. Copiable tel quel. |
| 8 | **`RelicCounterCatalog` : table relique → période** (`RelicCounterCatalog.cs:10-23`) | ★★★ F5 | ★ | Données prêtes à l'emploi pour un scoring « prochain déclenchement ». 11 reliques, aucune logique. |
| 9 | **Clé de cache d'état à 128 bits (`StateFingerprint`)** (`StateFingerprint.cs:5-57`) | ★★★ F5 / cache | ★★ | Permet un cache de prévision invalidé par état exact plutôt que par un hash de texte. Deux chaînes indépendantes, zéro allocation. |
| 10 | **`FastLaneVerification` : variable d'environnement unique qui rejoue le chemin lent et jette à la première divergence** (`FastLaneVerification.cs:16-21`) | ★★★ qualité | ★ | Patron de test différentiel à coût nul en production, activable seulement en CI. À reprendre tel quel pour valider F1/F2 contre un calcul naïf. |
| 11 | **`PlanAction` comme format d'export valeur-pur** (22 champs, `CombatPlan.cs:132-171`) | ★★ F5 | ★★ | Si ULTIMA doit exporter/importer des routes, ce record est déjà sérialisable sans référence d'objet. |
| 12 | **`TranspositionFrontier` : frontière Pareto à 5 dimensions au lieu d'un meilleur score** (`Transpositions.cs:22-55`) | ★★ F5 futur | ★★★★ | Utile seulement si ULTIMA fait un jour de la recherche. À garder pour le mode « simulation profonde », pas avant. |

**Les trois à faire en premier** : #1 (table statique), #2 (`ModifyDamage` sur les intents), #4 (boucle de létalité). Ensemble ils couvrent F1 et F2 avec environ 150 lignes de C# et aucune dépendance au solveur.

**Ce qu'il ne faut pas voler** : le beam search (176 fichiers, 120 k nœuds par défaut, 16 GiB de NoGC), le worker headless Windows, et le système de miroirs (10 fichiers + sous-dossiers `Hooks/*`, conçu pour reproduire *tous* les hooks vanilla — surdimensionné pour un tracker qui n'a besoin que de dégâts d'intent).
