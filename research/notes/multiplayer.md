# Multi-joueur dans les mods StS2 clonés — audit pour ULTIMA (F3/F4)

Atelier : `fedora-hp`, jeu ciblé v0.107.1. Périmètre lu : `/home/mio/sts2/research/repos` (16 dépôts).
Lecture seule — aucun fichier des dépôts n'a été modifié.

## Légende de provenance

| Marqueur | Sens |
|---|---|
| **[CODE]** | Vérifié en lisant le source du dépôt (citation `fichier:ligne`). |
| **[DOC]** | Affirmation provenant de la documentation interne du projet (`/home/mio/sts2/docs`, `skills-src`). **Non vérifiable dans cet atelier** : aucun `sts2.dll` ni source décompilée du jeu n'est présent sur la machine (`find` sur `/home/mio` : aucun `sts2.dll`, aucun dossier `Decompiled/`). |
| **[ABSENT]** | Zéro occurrence après grep. |
| **[UNVERIFIED]** | Déduit ou supposé, non prouvé par le code lu. |

## TL;DR pour le décideur

1. **Aucun des deux mods multi ne fait de réseau.** Zéro `INetMessage`, zéro `SendMessage`, zéro `RegisterMessageHandler`, zéro `NetService` dans `STS2-DamageTracker` et `sts2-multiplayer-damage-stat`. L'hypothèse de départ est **confirmée** : les deux lisent de l'état déjà synchronisé par le jeu.
2. **`HoveredModelTracker` n'existe dans aucun dépôt** — seulement dans la doc interne. C'est une piste **[DOC]**, pas un fait vérifié.
3. **Correction du brief : `STS2-DamageTracker` a une LICENSE — MIT** (`LICENSE:1-3`). Seul `sts2-multiplayer-damage-stat` est sans licence. La recommandation « réimplémenter les deux » n'est donc justifiée que pour freude916.
4. **Voie la plus sûre pour F3** : `HoveredModelTracker` si (et seulement si) la sonde de vérification ci-dessous confirme l'API ; sinon repli sur les hooks synchronisés (alerte *a posteriori*, pas *a priori*).
5. **Voie F4** : ne pas écrire d'`INetMessage` brut. Passer par `ICustomMessage` de BaseLib, qui fait déjà l'enregistrement, l'allocation d'ID et le dispatch — au prix d'une dépendance dure et d'un hack sur `MessageTypes._cache`.

---

## 1. Inventaire exhaustif des symboles réseau

### 1.1 Matrice (occurrences en `.cs` uniquement, `obj/` et `bin/` exclus)

| Dépôt | INetMessage | SendMessage | RegisterMessageHandler | NetService | NetMessageBus | RunManager | PeerInput | HoveredModelTracker |
|---|---|---|---|---|---|---|---|---|
| BaseLib-StS2 | 4 | 2 | 2 | 5 | ABSENT | 25 | ABSENT | ABSENT |
| CombatSolver | ABSENT | ABSENT | ABSENT | 10 | ABSENT | 284 | ABSENT | ABSENT |
| ModTemplate-StS2 | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT |
| Modding-Tutorial | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT |
| **STS2-DamageTracker** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** |
| STS2-ViewedCardsStatistics | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT |
| STS2FirstMod | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT |
| STS2MCP | ABSENT | ABSENT | ABSENT | 14 | ABSENT | 21 | ABSENT | ABSENT |
| Slay-the-Spire-2-mod-StS2-Damage-Logger | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | 1 | ABSENT | ABSENT |
| Spire-Forge | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT |
| spirescope | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT |
| sts2-advisor | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | 13 | ABSENT | ABSENT |
| sts2-deck-tracker | ABSENT | ABSENT | ABSENT | 3 | ABSENT | 8 | ABSENT | ABSENT |
| **sts2-multiplayer-damage-stat** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** | **ABSENT** |
| sts2-tierlist-overlay | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | 4 | ABSENT | ABSENT |
| sts2decktracker | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT | ABSENT |

Deux symboles sont **totalement absents de l'écosystème cloné** : `NetMessageBus` et `PeerInput`, ainsi que `HoveredModelTracker`. Les 3 n'apparaissent que dans `/home/mio/sts2/docs` et `skills-src`.

### 1.2 Détail des occurrences non nulles

| Dépôt | Occurrence | Ce que ça fait |
|---|---|---|
| BaseLib-StS2 | `Abstracts/CustomMessage.cs:11` | `CustomMessageWrapper : INetMessage` — enveloppe unique qui transporte tous les messages custom des mods. |
| BaseLib-StS2 | `Abstracts/CustomMessage.cs:56` | `messageBuffer.RegisterMessageHandler<CustomMessageWrapper>(HandleCustomMessage)` — enregistrement sur le service réseau. |
| BaseLib-StS2 | `Abstracts/CustomMessage.cs:95` | `(netService ?? RunManager.Instance.NetService).SendMessage(new CustomMessageWrapper{...})` — envoi. |
| BaseLib-StS2 | `Abstracts/CustomTargetedMessage.cs:14` | Variante `IRunLocationTargetedMessage, INetMessage` — message livré à un point précis du run (buffer ciblé). |
| BaseLib-StS2 | `Abstracts/CustomTargetedMessage.cs:106` | `SendMessage` sur le même `NetService`. |
| BaseLib-StS2 | `Patches/Networking/CustomMessagePatches.cs:16,24` | `Register(__instance.NetService)` sur postfix de `RunManager.InitializeShared` (`:13`), `Unregister` sur `RunManager.CleanUp` (`:23`). |
| BaseLib-StS2 | `Patches/Networking/CustomMessagePatches.cs:41-77` | Chirurgie directe du cache privé `MessageTypes._cache` : retire les wrappers, recalcule `_typeToId`, puis réserve des IDs **entre 128 et 254** pour les 2 wrappers. |
| BaseLib-StS2 | `Patches/Networking/CustomMessagePatches.cs:79-95` | Prefix sur `MessageTypes.TryGetMessageType(int id, …)` pour résoudre les IDs 128+ vers les wrappers BaseLib. |
| BaseLib-StS2 | `Patches/UI/CustomCharacterSelectEntryPatch.cs:489` | `if (!screen._lobby.NetService.Type.IsMultiplayer())` — garde UI, pas du réseau. |
| sts2-deck-tracker | `src/CardRegistry/CardRegistryPlayerManagement.cs:48-51` | Lit `RunManager.Instance?.NetService` uniquement pour récupérer `Platform`, puis `PlatformUtil.GetPlayerNameRaw(...)` (`:55`). Aucun envoi. |
| sts2-deck-tracker | `CardRegistryPlayerManagement.cs:44-46` | Commentaire clé : « This runs inside synchronized game hooks during co-op, so any throw here would desync the run ». Preuve directe que les hooks s'exécutent de façon synchronisée chez tous les clients. |
| STS2MCP | `McpMod.MultiplayerState.cs:57,66` | Garde `NetService.Type.IsMultiplayer()` + expose `net_type` en JSON. Lecture seule. |
| STS2MCP | `McpMod.MultiplayerState.cs:296,334,379` | Lecture des synchroniseurs natifs : `MapSelectionSynchronizer`, `EventSynchronizer`, `TreasureRoomRelicSynchronizer`. |
| STS2MCP | `McpMod.MultiplayerState.cs:68` | `LocalContext.GetMe(runState)` — identification du joueur local (idiome natif). |
| STS2MCP | `McpMod.MultiplayerActions.cs:95,123` / `McpMod.Actions.cs:140` | `RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(...))` — passe par la file d'actions synchronisée du jeu au lieu d'un message custom. |
| STS2MCP | `McpMod.StateBuilder.cs:803` | `ulong hostId = lobby.NetService.Type == NetGameType.Host ? localId : 0;` — sémantique host/client. |
| CombatSolver | `src/Runtime/OnlinePresence.cs:61-62` | Garde `RunManager.Instance.NetService is null` (fenêtre de chargement de sauvegarde) avant toute lecture multi. |
| CombatSolver | `src/Runtime/RunStatistics.cs:95` | `manager.NetService.Type != NetGameType.Singleplayer` — détection multi. |
| CombatSolver | `src/Search/SimulatedCombatState.cs:347` | `PlatformUtil.GetPlayerName(RunManager.Instance.NetService.Platform, player.NetId)` — nom d'affichage via la plateforme du service réseau. |
| CombatSolver | `src/Testing/.../UnattendedTestRunner.ProcessDiagnostics.cs:16-24` | Injection d'un `NetService` null par réflexion pour simuler la fenêtre « State existe, NetService pas encore installé ». |
| Slay-the-Spire-2-mod-StS2-Damage-Logger | `DamageLoggerModV2.cs:226` | `RunManager.Instance?.RunLobby is null` — idiome alternatif de détection solo. |

Les 284 occurrences de `RunManager` dans CombatSolver sont massivement des tests, docs et `RequestEnqueue` d'actions ; aucune n'est un envoi de message custom.

### 1.3 Dépôts totalement vierges de réseau

`ModTemplate-StS2`, `Modding-Tutorial`, `Spire-Forge`, `spirescope`, `STS2FirstMod`, `sts2decktracker`, `STS2-ViewedCardsStatistics`, `sts2-tierlist-overlay`, et **les deux cibles**.

---

## 2. Comment BAIGUANGMEI lit les dégâts des autres joueurs

**Réponse : il ne « lit » pas les autres joueurs. Il lit un hook que le jeu exécute identiquement sur tous les clients, et il accumule localement.**

### 2.1 API et point d'accroche

| Élément | Citation | Détail |
|---|---|---|
| Classe patchée | `src/ModEntry.cs:69` `AccessTools.Method(typeof(Hook), hookName)` | Patch Harmony sur les méthodes statiques de `MegaCrit.Sts2.Core.Hooks.Hook`. |
| Hook principal | `src/ModEntry.cs:34` + `:132-151` | `Hook.AfterDamageGiven` en **postfix**. Signature attendue : `(PlayerChoiceContext, CombatState, Creature? dealer, DamageResult, ValueProp, Creature target, CardModel? cardSource)`. |
| Filtre | `src/ModEntry.cs:142-143` | `if (dealer != null && !ReflectionHelpers.IsPlayerCreature(dealer)) return;` — ignore les dégâts ennemis. |
| Attribution | `src/RunDamageTrackerService.cs:167-173` | Résout le joueur depuis `dealer`, sinon depuis `cardSource` (cartes générées). |
| Montant | `src/ReflectionHelpers.cs:75-83` (`DamageMembers`) puis `TryResolveDamageAmount` | Ordre : `DamageDealt`, `FinalDamage`, `ActualDamage`, `UnblockedDamage`, `Amount`, `Damage`. |
| Cycle de combat | `src/ModEntry.cs:31-32` + `:100-118` | `Hook.BeforeCombatStart` → `BeginRun` + `BeginCombat` (+ création de l'overlay) ; `Hook.AfterCombatEnd` → `EndCombat`. |
| Joueur actif | `src/ModEntry.cs:33` + `:122-126` | `Hook.AfterPlayerTurnStart` → `EnsurePlayersRegistered` + `NotePlayer(player)`. |
| Cas spéciaux | `src/ModEntry.cs:36-37`, `:155-180` | `PoisonPower.AfterSideTurnStart` (prefix/postfix + `AsyncLocal`), `DoomPower.DoomKill` (prefix), `Hook.AfterDiedToDoom`. |

### 2.2 Pourquoi ça marche en multi sans réseau

Les actions de jeu passent par `RunManager.ActionQueueSynchronizer.RequestEnqueue(...)` (preuve de l'idiome : `STS2MCP/McpMod.Actions.cs:140`, `CombatSolver/src/Runtime/SolverController.cs:3136`). Chaque client rejoue donc la même séquence d'actions, ce qui fait tirer `Hook.AfterDamageGiven` **sur chaque client, pour chaque joueur**. BAIGUANGMEI n'a besoin d'aucune donnée distante : il observe un événement déjà répliqué.

Corroboration indépendante : `sts2-deck-tracker/src/CardRegistry/CardRegistryPlayerManagement.cs:44-46` documente explicitement que ces hooks tournent « inside synchronized game hooks during co-op » et qu'une exception y provoquerait un **desync**.

### 2.3 Moment exact

- Enregistrement des joueurs : au début de combat (`BeforeCombatStart`) et à chaque début de tour joueur (`AfterPlayerTurnStart`), plus à chaque coup (`EnsurePlayersRegistered` dans le postfix de dégâts, `src/ModEntry.cs:145`).
- Incrément : **immédiatement après chaque instance de dégâts**, pas en fin de tour.
- Publication UI : `RunDamageTrackerService.RecordDamage` → `Publish()` → événement `Changed` (`src/RunDamageTrackerService.cs:191`, `:466-469`) → overlay sur le thread Godot via `CallDeferred` (`src/DamageTrackerOverlay.cs:599-603`).
- Persistance : tous les 10 coups (`src/RunDamageTrackerService.cs:188-189`) et en début/fin de combat (`:84`, `:134`).

**Aucune trace de `NetService`, `SendMessage`, `INetMessage` dans ce dépôt** (matrice §1.1). Le `Steamworks.NET` référencé dans `DamageTracker.csproj:26-29` n'est utilisé nulle part dans `src/` — référence morte.

---

## 3. Comment freude916 s'accroche à `NMultiplayerPlayerState`

### 3.1 Le patch exact

```csharp
[HarmonyPatch(typeof(NMultiplayerPlayerState))]      // NMultiplayerPlayerStatePatch.cs:10
public static class NMultiplayerPlayerStatePatch
{
    [HarmonyPostfix]                                  // :38
    [HarmonyPatch("UpdateHighlightedState")]          // :39
    public static void UpdateHighlightedStatePostfix(NMultiplayerPlayerState __instance, bool ____isHighlighted)
```
`NMultiplayerPlayerStatePatch.cs:10,38-40`.

- Patch **par nom de méthode en chaîne** (`"UpdateHighlightedState"`), pas par `nameof`.
- Déclencheur = survol de la vignette d'un joueur. Sortie immédiate si `!____isHighlighted` (`:42`).
- Garde anti-collision avec l'UI native : si `_networkProblemIndicator.IsShown`, on n'affiche rien (`:44-45`).

### 3.2 Champs privés atteints

| Champ | Accès | Citation |
|---|---|---|
| `_isHighlighted` | injecté par Harmony sous le nom `____isHighlighted` dans la signature | `:40` |
| `_networkProblemIndicator` | `typeof(NMultiplayerPlayerState).GetField("_networkProblemIndicator", BindingFlags.NonPublic \| BindingFlags.Instance)` | `:13-15` |
| `Player` (propriété) | `__instance.Player` | `:47` |
| `player.NetId` | publique, sert de clé de dictionnaire | `:54` |
| `HoverTip.<Title>k__BackingField` … ×9 | réflexion sur les backing fields du **struct** | `:19-36` |

### 3.3 Technique de réflexion

Deux techniques distinctes, toutes deux parce que le projet **n'active pas le publicizer sur `sts2`** :

1. `damageStat.csproj` référence `sts2` avec `<Publicize>False</Publicize>` (**ligne 42**) — commentaire : « Allows private/protected field references but may cause errors ». Le paquet `BepInEx.AssemblyPublicizer.MSBuild` est bien référencé (`:75`) mais neutralisé sur `sts2`. Résultat : accès aux membres privés **uniquement à l'exécution**, via `GetField(...)` + `?.` (donc silencieux si renommé).
2. **Écriture dans un struct via `TypedReference`** — le point le plus fragile :
   ```csharp
   HoverTip hoverTip = default;
   TypedReference tr = __makeref(hoverTip);      // :74
   TitleField?.SetValueDirect(tr, title);        // :76
   ...
   ```
   `NMultiplayerPlayerStatePatch.cs:71-87`. Le struct `HoverTip` est construit champ par champ ; `Id` est forcé à `"damageStat.tooltip"` (`:78`), `Icon`/`CanonicalModel` à `null`.

Affichage : `NHoverTipSet.CreateAndShow(instance, hoverTip)` puis repositionnement `GlobalPosition + Vector2.Down * 80f` (`:67-68`).

### 3.4 D'où viennent les chiffres

`DamageTracker.cs` — **aucune donnée réseau** :

| Étape | Citation |
|---|---|
| Abonnement | `CombatManager.Instance.CombatSetUp += OnCombatSetUp` / `CombatEnded += OnCombatEnded` (`:25-26`) |
| Historique | `CombatManager.Instance.History.Changed += OnHistoryChanged` en début de combat (`:33`), `-=` en fin (`:40`) |
| Lecture | `foreach (var entry in CombatManager.Instance.History.Entries)` filtré sur `DamageReceivedEntry` (`:45-48`) |
| Dédoublonnage | `entry.GetHashCode()` dans un `HashSet<int>` (`:51-52`) |
| Attribution | `dealer.IsPlayer ? dealer.Player : dealer.PetOwner` (`:67`) — gère les pets (Osteo/Osty) |
| Montant | `entry.Result.UnblockedDamage` (`:71`) |
| Clé | `player.NetId` (`:74`) |

`CombatManager.History` est donc lui aussi un état **déjà synchronisé** : freude916 exploite exactement la même propriété que BAIGUANGMEI, par une porte différente (historique vs hook).

---

## 4. Gestion de l'affichage multi dans les deux mods

### 4.1 BAIGUANGMEI — overlay autonome (tous joueurs, solo et multi)

| Aspect | Implémentation | Citation |
|---|---|---|
| Nature | `CanvasLayer` Godot privé au mod, colonnes `PLAYER / PCT / TOTAL / COMBAT / LAST / MAX` | `DamageTrackerOverlay.cs:340-348` |
| Lignes | une par `PlayerDamageSnapshot`, triées par `TotalDamage` décroissant puis nom | `RunDamageTrackerService.cs:366-368` |
| Nom | `snap.DisplayName` (label 13 px) | `DamageTrackerOverlay.cs:391` |
| Nom de personnage | `snap.CharacterName` (label 10 px) | `DamageTrackerOverlay.cs:395` |
| Portrait | `LoadIcon(snap.CharacterName) ?? snap.PortraitTexture` ; sinon carré coloré + initiales | `DamageTrackerOverlay.cs:466`, `:485-497` |
| Couleur de repli | `Color.FromHsv((snap.PlayerKey % 360)/360f, …)` — dérivée du NetId | `DamageTrackerOverlay.cs:489` |
| Joueur actif | `snap.IsActive` → fond/bordure/texte accentués | `DamageTrackerOverlay.cs:355`, `:363-370` |
| Calcul de l'actif | `NotePlayer` met `IsActive` pour la clé courante et remet les autres à `false` | `RunDamageTrackerService.cs:149-154` |
| Origine du nom | `PlatformUtil.PrimaryPlatform` puis `PlatformUtil.GetPlayerName(platformType, playerKey)` | `ReflectionHelpers.cs:501-507` |
| Repli nom | `player.Creature.Name` → `Character.Title` → `"Player {netId}"` | `ReflectionHelpers.cs:195-198` |

**Fragilité notable** : BAIGUANGMEI utilise `PlatformUtil.PrimaryPlatform` (`ReflectionHelpers.cs:501`) alors que `sts2-deck-tracker` (`CardRegistryPlayerManagement.cs:48-56`) et `CombatSolver` (`SimulatedCombatState.cs:347`) utilisent `NetService.Platform`. En crossplay, `PrimaryPlatform` n'est pas forcément la plateforme du pair → repli sur « Player {id} ». C'est le seul endroit où BAIGUANGMEI s'écarte de l'idiome dominant, et c'est un bug latent, pas un choix documenté.

**`IsActive` est une inférence locale**, pas une donnée synchronisée : elle dépend de la diffusion de `Hook.AfterPlayerTurnStart` pour *tous* les joueurs sur chaque client. Non vérifié en runtime — à tester (§7.13).

### 4.2 freude916 — zéro UI propre, greffe sur l'UI native

| Aspect | Implémentation | Citation |
|---|---|---|
| Nature | Aucun nœud créé ; tooltip éphémère au survol de la vignette native | `NMultiplayerPlayerStatePatch.cs:52-69` |
| Portrait | **ABSENT** — réutilise celui de `NMultiplayerPlayerState` | — |
| Nom | **ABSENT** — le panneau natif l'affiche déjà | — |
| Id joueur | `player.NetId` comme clé de lecture | `:54` |
| Joueur actif | **ABSENT** — aucune notion de tour | — |
| Portée | **Multi uniquement** : `NMultiplayerPlayerState` n'existe pas en solo → le mod est invisible en solo | `:10` |

C'est l'approche la plus économe : le panneau natif fournit déjà portrait/nom/reliques, le mod n'ajoute que deux lignes de texte. Contrepartie : aucune vue d'ensemble, et dépendance totale à la stabilité de `NMultiplayerPlayerState.UpdateHighlightedState` et du struct `HoverTip`.

---

## 5. Ce que le jeu synchronise — et ce qu'il ne synchronise pas

Déduit **du code lu**, pas de la doc.

### 5.1 Synchronisé (prouvé par l'usage qu'en font les mods)

| Donnée | Preuve |
|---|---|
| File d'actions de combat (`PlayCardAction`, `EndPlayerTurnAction`) | `STS2MCP/McpMod.Actions.cs:140`, `CombatSolver/src/Runtime/SolverController.cs:3136` passent par `ActionQueueSynchronizer.RequestEnqueue` |
| Résolution des dégâts et hooks associés | `STS2-DamageTracker/src/ModEntry.cs:34,132-151` + `sts2-deck-tracker/.../CardRegistryPlayerManagement.cs:44-46` (« synchronized game hooks ») |
| Historique de combat (`CombatManager.History.Entries`) | `sts2-multiplayer-damage-stat/DamageTracker.cs:33,45` — lu sur chaque client |
| État des joueurs (`RunState.Players`, `CombatState.Players`, PV, block, powers) | `STS2-DamageTracker/src/ReflectionHelpers.cs:347-360` énumère `runState.Players` / `combatState.Players` |
| Sélection de carte sur la map | `STS2MCP/McpMod.MultiplayerState.cs:296` (`MapSelectionSynchronizer`) |
| Choix d'événement | `:334` (`EventSynchronizer`) |
| Relique de salle au trésor | `:379` (`TreasureRoomRelicSynchronizer`) |
| Récompenses | `CombatSolver/src/Runtime/CombatReplayRecording.cs:65` (`RewardsSetSynchronizer`) |
| Choix de cartes (player choice) | `CombatSolver/src/Runtime/CombatReplayRecording.cs:61` (`PlayerChoiceSynchronizer`) |
| Identité locale | `STS2MCP/McpMod.MultiplayerState.cs:68` (`LocalContext.GetMe`) |

### 5.2 Non synchronisé / local par client

| Donnée | Preuve |
|---|---|
| L'overlay et sa mise en page | Créé localement (`STS2-DamageTracker/src/DamageTrackerOverlay.cs:897-912`) ; aucun échange réseau dans le dépôt |
| `IsActive` (joueur actif) | Calculé localement (`RunDamageTrackerService.cs:149-154`) — inférence, pas une donnée reçue |
| Noms d'affichage | Résolus localement via la plateforme (`ReflectionHelpers.cs:497-520`, `CardRegistryPlayerManagement.cs:48-56`) → peuvent différer ou échouer par client |
| Tooltip de dégâts freude916 | Construit localement (`NMultiplayerPlayerStatePatch.cs:52-69`) |
| Survol / sélection de carte des autres joueurs | **[DOC]** `RunManager.Instance.HoveredModelTracker` + `GetHoveredModel(ulong)` + event `HoverChanged` (`docs/08-multiplayer.md:31-44`, `skills-src/sts2-multiplayer-net/SKILL.md:18-22`). **Aucune occurrence dans les 16 dépôts.** |

### 5.3 Conséquence pour la conception

Tout ce qui touche à la **résolution** (dégâts, PV, historique, actions) est déjà répliqué : inutile — et dangereux — de le retransmettre. Tout ce qui touche à l'**intention non encore validée** (survol, « je vais jouer X ») n'est couvert par aucun code de mod vérifié : c'est précisément le trou que F3/F4 doivent combler.

---

## 6. Stratégie recommandée pour ULTIMA (F3/F4)

### 6.0 Sonde de vérification à faire AVANT toute conception

Aucun `sts2.dll` n'est présent ici ; les noms ci-dessous viennent de la doc interne. À valider sur la machine de jeu, en une commande :

```bash
# installer un décompilateur IL puis lister les membres
dotnet tool install -g ilspycmd
ilspycmd -t MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput.HoveredModelTracker \
  "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/sts2.dll"
```
Critères d'acceptation : la classe existe, `GetHoveredModel(ulong)` et l'event `HoverChanged` existent, et `RunManager.HoveredModelTracker` est une propriété publique. **Si l'un des trois manque → appliquer 6.1-bis.**

### 6.1 Niveau (a) — sans message custom

**a-1. Source : `HoveredModelTracker` [DOC, à vérifier]**

```
RunManager.Instance.HoveredModelTracker.HoverChanged += id => { var m = GetHoveredModel(id); ... }
```
1. S'abonner à `HoverChanged`.
2. Filtrer sur le joueur distant (`id != localNetId`).
3. `GetHoveredModel(id)` renvoie un `AbstractModel?` → **type-test** `is CardModel`, puis lookup `card.Id.Entry` dans `data/catalog/priority_cards.json`.
4. Si match → bannière locale « <nom> tient <carte> — à jouer en premier ».

Avantages : zéro sérialisation, zéro ID de message, zéro risque de desync (lecture pure), zéro dépendance BaseLib.

Risques :
- **Existence non prouvée** (§6.0).
- `HoverChanged` peut être très bavard → debounce obligatoire (≥100 ms) et un seul affichage par (joueur, carte, tour).
- Le survol n'est **pas** un engagement : ce n'est pas « s'apprête à jouer ». Ne pas formuler l'alerte comme une certitude.
- Duplication avec l'UI native d'intentions distantes (`NMultiplayerPlayerIntentHandler` cité par `docs/08-multiplayer.md:42-44` **[DOC]**) → se positionner en *surcouche de priorité*, pas en second affichage d'intention.
- `GetHoveredModel` peut renvoyer `null` hors tour du joueur → traiter comme « pas d'alerte ».

**a-1-bis. Repli si l'API n'existe pas**

Il n'existe alors **aucun moyen vérifié** de connaître l'intention d'un joueur distant. Le repli honnête pour F3 est *a posteriori* : détecter la carte **au moment où elle est jouée**, via un hook synchronisé (modèle §2), et n'alerter que si elle est prioritaire et jouée tardivement. C'est moins bon fonctionnellement, mais 100 % sûr et déjà démontré par BAIGUANGMEI.

**a-2. Pour F4 sans message** : F4 (« diffuser ces alertes ») est **satisfait gratuitement** en mode a-1, puisque chaque client calcule la même alerte à partir du même état synchronisé. Aucune diffusion explicite n'est nécessaire — c'est le point le plus important de ce rapport : *F4 n'exige un message custom que si F3 s'appuie sur une donnée locale*.

### 6.2 Niveau (b) — avec `INetMessage` custom, seulement si (a) insuffisant

**Ne pas implémenter `INetMessage` à la main.** Utiliser `ICustomMessage` de BaseLib, qui fournit l'enregistrement, l'ID et le dispatch :

```csharp
public sealed class UltimaPriorityMessage : ICustomMessage        // BaseLib.Abstracts
{
    public byte ProtocolVersion;      // 1
    public int  RunSeedHash;          // filtre de run
    public int  Turn;                 // filtre de péremption
    public ulong PlayerNetId;
    public string CardId;             // = CardModel.Id.Entry
    public void Serialize(PacketWriter w)   { w.WriteInt(ProtocolVersion); /* … */ }
    public void Deserialize(PacketReader r) { ProtocolVersion = (byte)r.ReadInt(); /* … */ }
    public void HandleMessage(ulong senderId) { /* read-only UI */ }
    public bool ShouldBroadcast => true;   // le host relaie
    public NetTransferMode Mode  => NetTransferMode.Reliable;
}
// envoi
CustomMessageWrapper.Send(new UltimaPriorityMessage{...});   // CustomMessage.cs:93-96
```

Contrat vérifié dans BaseLib :
- `ICustomMessage : IPacketSerializable` avec `HandleMessage(ulong senderId)`, `ShouldBroadcast`, `ShouldBuffer` (défaut `true`), `Mode` (défaut `Reliable`), `LogLevel` (`CustomMessage.cs:103-133`).
- `CustomMessageWrapper.Send` → `RunManager.Instance.NetService.SendMessage(...)` (`CustomMessage.cs:95`).
- Enregistrement automatique au `RunManager.InitializeShared`, désenregistrement au `CleanUp` (`CustomMessagePatches.cs:10-27`).
- `ShouldBroadcast = true` = « envoyé au host, qui relaie aux autres » (`CustomMessage.cs:114-117`).

Risques, par ordre de gravité :
1. **Dépendance dure à BaseLib.** freude916 la référence déjà (`damageStat.csproj:44-46`) ; BAIGUANGMEI non (`DamageTracker.csproj:16-29`, qui ne référence que `sts2`, `0Harmony` et `Steamworks.NET`).
2. **Espace d'IDs 128-254 réservé par BaseLib** (`CustomMessagePatches.cs:60-77`). Un autre mod faisant la même chirurgie entre en collision → message routé vers le mauvais type → désérialisation arbitraire.
3. **IDs internes dérivés du `FullName`** (`CustomMessage.cs:42`) et résolus par `++key` en cas de collision après tri par `FullName` (`:28-29`, `:43-47`). Donc **l'ID dépend de l'ensemble des mods installés** : deux clients avec des mods différents peuvent attribuer des IDs différents. C'est une bombe à retardement pour un déploiement hétérogène.
4. **Modification de champs privés du jeu** (`MessageTypes._cache`, `_idToType`, `_typeToId`, `CustomMessagePatches.cs:41-55`) : un renommage au patch suivant casse l'init réseau de **tous** les clients.
5. **Client vanilla recevant un ID inconnu** : comportement **non vérifié** (la doc elle-même le note, `docs/08-multiplayer.md:100-102`). À traiter comme « peut déconnecter » → handshake obligatoire.
6. Une exception dans `Deserialize` se produit dans la boucle de réception réseau → risque de déconvion/desync, pas juste de log.

**Atténuations obligatoires** : `ProtocolVersion` en premier champ ; handshake `UltimaHello{version, capabilities}` en début de combat et broadcast restreint aux pairs ULTIMA ; `Prepare()`/feature-detect sur BaseLib ; try/catch total dans `Deserialize` et `HandleMessage` ; `Turn` + `RunSeedHash` pour jeter le périmé ; aucun effet de bord sur l'état de jeu.

### 6.3 Verdict

| Besoin | Voie | Condition |
|---|---|---|
| F3 « voir la carte survolée d'un pair » | (a) `HoveredModelTracker` | sonde §6.0 positive |
| F3 si sonde négative | repli a-1-bis (alerte a posteriori via hooks) | aucune |
| F4 « diffuser » | **aucun message** si F3 = (a) | — |
| F4 si F3 doit transmettre une intention non synchronisée | (b) `ICustomMessage` + handshake | BaseLib acceptée comme dépendance |

---

## 7. Pièges de désérialisation et de version à éviter

1. **Ne jamais laisser une exception sortir d'un hook synchronisé.** Preuve : `sts2-deck-tracker/.../CardRegistryPlayerManagement.cs:44-46` — « any throw here would desync the run ». Le `try/catch` de `ReflectionHelpers.TryGetPlatformDisplayName` (`:497-520`) est le bon modèle.
2. **L'ID de message n'est pas stable.** `ComputeBasicHash()` du `FullName` + bump sur collision après tri (`CustomMessage.cs:28-29,40-50`). Renommer la classe, changer son namespace, ou installer un mod concurrent change l'ID. → version de protocole obligatoire, et refus explicite si mismatch.
3. **Ne pas compter sur `MessageTypes.Initialize`.** BaseLib doit gérer deux formes selon la version du jeu : `MessageTypes.Initialize` (v0.107.1+) **ou** le constructeur statique (`CustomMessagePatches.cs:30-49`), avec un `Prepare()` qui renvoie `false` proprement. À répliquer : toujours feature-detect, jamais `throw` à l'init.
4. **`Activator.CreateInstance(msgType)`** (`CustomMessage.cs:81`) impose un constructeur sans paramètre. Pas de `required`, pas de dépendance injectée.
5. **Ordre des champs = contrat.** Aucun champ optionnel, aucune hiérarchie, aucune collection sans compteur explicite. Un champ ajouté au milieu décale tout le reste silencieusement.
6. **Pas de `ShouldBuffer` magique.** Le commentaire dit que le jeu ne bufferise qu'au démarrage du run (`CustomMessage.cs:119-123`) → ne pas compter sur le buffering pour l'ordre ; porter `Turn`.
7. **`Reliable` par défaut, `Unreliable` non prouvé.** `NetTransferMode.Reliable` est utilisé (`CustomMessage.cs:130`) ; `Unreliable` n'apparaît que dans un commentaire (`:128`, « for purely visual effects, such as communicating what a player is hovering »). **Ne pas coder `Unreliable` avant de l'avoir vu dans l'énumération.**
8. **Harmony par nom de chaîne = bombe à retardement.** `[HarmonyPatch("UpdateHighlightedState")]` (`NMultiplayerPlayerStatePatch.cs:39`) lève « Undefined target method » à `PatchAll` si la méthode est renommée → **le mod entier ne charge plus**. Utiliser `TargetMethod()` + `Prepare() => false`.
9. **`SetValueDirect` sur `TypedReference`** (`NMultiplayerPlayerStatePatch.cs:73-84`) dépend des backing fields générés `<X>k__BackingField` : ils changent si le jeu est recompilé autrement. Préférer un constructeur public de `HoverTip` s'il en existe un (à vérifier §6.0) ; sinon isoler dans une méthode `try/catch` avec repli « pas de tooltip ».
10. **Champs privés renommés = silence total.** freude916 utilise `?.` partout (`:13-15`, `:44`), donc un renommage produit un mod qui ne fait *rien* sans erreur. Defensif, mais **ajouter un log unique** au premier échec, sinon le bug est invisible.
11. **Publicizer vs réflexion.** freude916 désactive explicitement le publicizer sur `sts2` (`damageStat.csproj:37`) ; BaseLib l'active (`BaseLib-StS2/*.csproj:60-64`). Les deux approches ne se mélangent pas : si ULTIMA publicise `sts2`, les noms privés sont figés à la compilation et **toute** différence de version casse au chargement (`MissingFieldException`), pas à l'exécution.
12. **JSON de persistance sans version de schéma.** `SavedState` (`RunDamageTrackerService.cs:629-643`) n'a pas de champ `SchemaVersion`, et `TryLoadState` avale toute exception (`:536-539`) → une évolution de schéma perd silencieusement l'historique. Ajouter `SchemaVersion` et migrer explicitement.
13. **Ne pas copier `entry.GetHashCode()` comme clé de dédoublonnage** (`DamageTracker.cs:51-52`) : collisions de hash → dégâts perdus ; et `Reset()` (`:97-102`) n'est **jamais appelé** → les totaux s'accumulent d'un run à l'autre dans la même session. Bug réel à ne pas reproduire.
14. **`NetId` comme clé** : utilisé par les deux mods (`DamageTracker.cs:74`, `ReflectionHelpers.cs:194`). Comportement en solo (0 ? stable ?) **non vérifié**. Prévoir un repli sur l'index de slot (`RunState.Players`), comme le fait `STS2MCP/McpMod.MultiplayerState.cs:70-80`.
15. **`IsActive` est une inférence, pas une donnée.** Si `Hook.AfterPlayerTurnStart` ne tire que pour le joueur local sur chaque client, BAIGUANGMEI n'highlighte jamais que soi-même. À vérifier en runtime à 2 clients avant de réutiliser ce pattern.
16. **Fenêtre de chargement.** `RunManager.NetService` peut être `null` alors que `State` existe (sauvegarde en cours de chargement) : `CombatSolver/src/Runtime/OnlinePresence.cs:61-62` et `UnattendedTestRunner.ProcessDiagnostics.cs:16-24` documentent explicitement ce cas. Toute lecture multi doit être gardée.

---

## 8. Licences

### 8.1 Les deux cibles

| Dépôt | Licence | Preuve | Conséquence pour ULTIMA |
|---|---|---|---|
| **STS2-DamageTracker** (BAIGUANGMEI) | **MIT**, © 2026 BAIGUANGMEI | `LICENSE:1-3` — « MIT License / Copyright (c) 2026 BAIGUANGMEI / Permission is hereby granted, free of charge… » | **Réutilisation autorisée** avec conservation du copyright et de la notice. Le brief (« aucun LICENSE ») est **inexact**. |
| **sts2-multiplayer-damage-stat** (freude916) | **AUCUNE** | Pas de `LICENSE`/`COPYING` ; rien dans `README.md`, `README_zh.md`, `damageStat.csproj`, `mod_manifest.json` (grep insensible à la casse : 0 hit) | **Tous droits réservés** par défaut. Aucune réutilisation de code → **réimplémenter** (le pattern `NMultiplayerPlayerState` + `HoverTip` s'inspire, le code ne se copie pas). |

### 8.2 Contexte complet (16 dépôts)

| Dépôt | Licence |
|---|---|
| BaseLib-StS2 | MIT (`LICENSE.txt:1`) |
| CombatSolver | MIT, © 2026 Torch1230 and contributors |
| Modding-Tutorial | MIT, © 2026 fresh-milkshake |
| ModTemplate-StS2 | ABSENT |
| Slay-the-Spire-2-mod-StS2-Damage-Logger | **GPL** (texte GNU GPL en tête de `LICENSE`) — copyleft, à éviter dans un binaire lié |
| Spire-Forge | ABSENT |
| spirescope | MIT, © 2026 The Quantum Falcon |
| sts2-advisor | ABSENT |
| **STS2-DamageTracker** | **MIT** |
| sts2-deck-tracker | ABSENT |
| sts2decktracker | ABSENT |
| STS2FirstMod | ABSENT |
| STS2MCP | MIT, © 2026 Yikun Ji (Kunologist) |
| **sts2-multiplayer-damage-stat** | **ABSENT** |
| sts2-tierlist-overlay | MIT, © 2026 Frank Blendinger |
| STS2-ViewedCardsStatistics | MIT, © 2026 OLC |

⚠️ Le GPL de `Slay-the-Spire-2-mod-StS2-Damage-Logger` est un piège : son approche d'attribution de dégâts est intéressante (`RunManager.Instance?.RunLobby is null` pour détecter le solo, `DamageLoggerModV2.cs:226`), mais toute copie de code contaminerait ULTIMA.

---

## 9. Ce qui manque (ABSENT) — à ne pas combler par invention

| Élément attendu | Statut |
|---|---|
| `INetMessage` dans un mod de tracker multi | **ABSENT** des 16 dépôts, hors BaseLib (qui l'implémente pour autrui) |
| `NetMessageBus` | **ABSENT** de tous les dépôts ; seule la doc le nomme (`docs/08-multiplayer.md:73`), avec un `RegisterMessageHandler<T>` que BaseLib appelle en réalité sur `INetGameService` |
| `PeerInput` / `PeerInputSynchronizer` | **ABSENT** de tous les dépôts |
| `HoveredModelTracker` | **ABSENT** de tous les dépôts ; uniquement `docs/04-trackers.md:81`, `docs/08-multiplayer.md:31-44`, `docs/06-architecture.md:50`, `skills-src/sts2-multiplayer-net/SKILL.md:18-22` |
| Source du jeu (`sts2.dll`, décompilation) | **ABSENTE** de la machine → toute signature `MegaCrit.*` non couverte par un `using` d'un dépôt reste **[DOC]** |
| Comportement d'un client vanilla face à un ID inconnu | **NON VÉRIFIÉ** — la doc le signale elle-même (`docs/08-multiplayer.md:100-102`) |
| Valeur `NetTransferMode.Unreliable` | **NON VÉRIFIÉE** (commentaire seulement) |
| Méthodes `PacketWriter`/`PacketReader` autres que `WriteInt`/`ReadInt` | **NON VÉRIFIÉES** — `WriteInt`/`ReadInt` sont les seules prouvées (`CustomMessage.cs:73,79`) |
| Licence de freude916 | **ABSENTE** → réimplémentation obligatoire |
