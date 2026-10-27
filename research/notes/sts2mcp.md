# STS2MCP — analyse exhaustive (lecture seule)

Dépôt analysé : `/home/mio/sts2/research/repos/STS2MCP`
HEAD : `55e0648` — « Fix game API compatibility with STS2 v0.107 (#123) », 2026-07-29.
Objet : mod Slay the Spire 2 qui expose l'état du jeu en **JSON** sur un serveur HTTP local, plus un pont MCP Python optionnel.
But de ce document : fournir à l'agent qui code ULTIMA tout ce qui est **réutilisable en lecture seule** (F1–F6), et tout ce qu'il ne faut **pas** reprendre.

Convention de citation : `fichier.cs:ligne` (numéros de ligne du dépôt au HEAD ci-dessus). Tout ce qui n'existe pas dans le code est marqué **ABSENT**.

---

## 1. Vue d'ensemble — architecture du mod

### 1.1 Fichiers

| Fichier | Lignes | Rôle |
|---|---:|---|
| `McpMod.cs` | 482 | Point d'entrée (`[ModInitializer("Initialize")]` l.19-20), démarrage du serveur HTTP, table de routage, file « main thread », détection solo/multi. |
| `McpMod.StateBuilder.cs` | 2424 | **Le cœur** : `BuildGameState()` (l.61) et tous les sous-builders solo (bataille, joueur, ennemi, piles, carte, event, shop, map, rewards, sélections, orbes, pets). |
| `McpMod.MultiplayerState.cs` | 447 | `BuildMultiplayerGameState()` (l.27) + variantes multi (bataille, map, event, trésor) + `BuildAllPlayersState()` (l.413). |
| `McpMod.Actions.cs` | 1893 | **Toute la couche ACTION** (POST) : 27 handlers + `ExecuteMenuSelect()` (l.1080) + `ResolveTarget()` (l.1054). À ne pas reprendre. |
| `McpMod.MultiplayerActions.cs` | 132 | Actions multi : `end_turn` / `undo_end_turn` via file d'actions synchronisée. |
| `McpMod.Helpers.cs` | 822 | Helpers : `IsPlayPhase`, `SafeGetText`, `StripRichTextTags`, `SendJson`, **`GetInstanceFieldValue` (réflexion)**, traversée de nodes Godot (`FindAll`/`FindFirst`/`IsLiveNode`), détection FTUE/popups. |
| `McpMod.Formatting.cs` | 1116 | Rendu Markdown de l'état (alternative `?format=markdown`). ~30 formatters. |
| `McpMod.Compendium.cs` | 669 | `/api/v1/compendium` : stats de profil (`SaveManager.Progress`) + lecture des fichiers `saves/history/*.run` en `JsonDocument`. |
| `McpMod.Profile.cs` | 370 | `/api/v1/profile`, `/profiles`, switch/delete de profil (via UI). |
| `McpMod.Wiki.cs` | 389 | `/api/v1/wiki` : recherche floue (Levenshtein l.355) sur cartes/reliques découvertes. |
| `McpMod.SettingsUI.cs` | 166 | **Seul usage de Harmony** : injecte une checkbox « Instant Mode » dans les réglages (patches l.83, 93, 104, 119, 139). |
| `STS2_MCP.csproj` | 30 | `net9.0`, `LangVersion 12`, refs `sts2.dll` / `GodotSharp.dll` / `0Harmony.dll` en `Private=false`. |
| `mod_manifest.json` | 10 | `id: STS2_MCP`, `version: 0.4.0`, `has_dll: true`, `affects_gameplay: false`. |
| `build.ps1` | 80 | Build PowerShell **Windows only** (hardcode `data_sts2_windows_x86_64` l.45). |
| `mcp/server.py` | 1151 | FastMCP + httpx : ~30 tools qui *wrappent* les endpoints HTTP. Aucune logique d'état. |
| `docs/raw-full.md` / `docs/raw-simplified.md` | 1540 / 213 | Référence API (schémas JSON) destinée aux agents. |

### 1.2 Flux de données

```
HTTP thread (ThreadPool)
  └─ HandleRequest (McpMod.cs:175)
       └─ RunOnMainThread(lambda)            McpMod.cs:138-158
            └─ ConcurrentQueue<Action>       McpMod.cs:28
                 └─ SceneTree.ProcessFrame   McpMod.cs:89-90
                      └─ ProcessMainThreadQueue (max 10 actions/frame) McpMod.cs:127-136
                           └─ BuildGameState()  StateBuilder.cs:61
                                └─ Dictionary<string, object?>
                                     └─ JsonSerializer (snake_case, indenté, ignore null) McpMod.cs:29-35
```

Deux formats de sortie pour un même objet : JSON (défaut) et Markdown (`?format=markdown`, `McpMod.cs:293`, `:387`) — le Markdown est produit par `FormatAsMarkdown` (`Formatting.cs:10`).

### 1.3 Découpage par responsabilité (utile pour piller)

- **Lire l'état** → `StateBuilder.cs` + `MultiplayerState.cs` + `Helpers.cs` (partie `SafeGetText`/`FindAll`).
- **Agir** → `Actions.cs` + `MultiplayerActions.cs` (+ `SettingsUI.cs` pour les prefs).
- **Lire le profil/hors-run** → `Compendium.cs`, `Profile.cs`, `Wiki.cs`.
- **Présenter** → `Formatting.cs`.

---

## 2. Serveur HTTP local — port, lib, thread, cycle de vie

| Élément | Valeur | Référence |
|---|---|---|
| Bibliothèque | `System.Net.HttpListener` (BCL, pas de dépendance externe) | `McpMod.cs:26`, `:94` |
| Port par défaut | `15526` | `McpMod.cs:23` |
| Préfixes | `http://localhost:{port}/` **et** `http://127.0.0.1:{port}/` (pas de wildcard) | `McpMod.cs:95-96` |
| Config | fichier `STS2_MCP.conf` **à côté du DLL**, clé `port` ; créé avec la valeur par défaut s'il manque | `McpMod.cs:24`, `:37-79` (création l.50-52, parse l.62-68) |
| Thread serveur | `new Thread(ServerLoop){ IsBackground = true, Name = "STS2_MCP_Server" }` | `McpMod.cs:99-104` |
| Boucle | `GetContext()` bloquant, puis `ThreadPool.QueueUserWorkItem(HandleRequest)` → 1 requête = 1 thread du pool | `McpMod.cs:160-173` |
| CORS | `Access-Control-Allow-Origin: *`, méthodes `GET, POST, OPTIONS`, `OPTIONS` → 204 | `McpMod.cs:181-190` |
| Auth | **aucune** | `McpMod.cs:175-266` |
| Arrêt | **ABSENT** : jamais de `_listener.Stop()`, aucun hook `ApplicationQuit` ; le serveur meurt avec le process | grep `_listener.` → seules occurrences l.95-97, 166 |
| Log | `GD.Print` / `GD.PrintErr` uniquement | `McpMod.cs:106`, `:110` |
| Marshalling main thread | `tree.Connect(SceneTree.SignalName.ProcessFrame, ...)` ; file `ConcurrentQueue<Action>` ; `TaskCompletionSource` ; le thread HTTP **bloque** sur `GetAwaiter().GetResult()` | `McpMod.cs:89-90`, `:28`, `:127-158`, `:297-298`, `:391-392` |
| Débit | max **10** actions de file traitées par frame | `McpMod.cs:130` |

### 2.1 Table de routage (`McpMod.cs:194-266`)

| Route | Méthodes | Handler | Garde |
|---|---|---|---|
| `/` | GET | `{message, status}` | — |
| `/api/v1/singleplayer` | GET / POST | `HandleGetState` / `HandlePostAction` | **409** si run multi en cours (`:202-207`) |
| `/api/v1/multiplayer` | GET / POST | `HandleGetMultiplayerState` / `HandlePostMultiplayerAction` | **409** si pas multi (`:219-224`) |
| `/api/v1/profiles` | GET / POST | `HandleGetProfiles` / `HandlePostProfiles` | — |
| `/api/v1/profile` | GET | `HandleGetProfile` | — |
| `/api/v1/compendium` | GET | `HandleGetCompendium` | — |
| `/api/v1/wiki` | GET | `HandleGetWiki` (`query` requis) | 400 si query vide |
| autre | — | 404 | `:263-266` |

Paramètre `format=json|markdown` sur les deux GET d'état (`McpMod.cs:293`, `:387`).

### 2.2 Détection solo/multi

```csharp
// McpMod.cs:281-289
return RunManager.Instance.IsInProgress
    && RunManager.Instance.NetService.Type.IsMultiplayer();
```
Commentaire explicite l.278-280 : l'appel se fait **depuis le thread HTTP**, c'est un garde « best effort » ; les vérifications autoritatives sont refaites **dans** les lambdas `RunOnMainThread`.

### 2.3 Initialisation (`McpMod.cs:81-112`)

1. `TryApplyHarmonyPatches()` (l.86) → `new Harmony("com.sts2mcp").PatchAll()` en try/catch (l.114-125) : **l'échec du patch UI ne bloque pas le serveur**.
2. Connexion `ProcessFrame` (l.89-90).
3. `LoadPort()` (l.92).
4. `HttpListener` + `Start()` (l.94-97).
5. Thread serveur (l.99-104).

---

## 3. Inventaire exhaustif des méthodes de lecture d'état

### 3.1 Carte des `state_type` produits

| `state_type` | Builder | Clé racine |
|---|---|---|
| `menu` (+ `menu_screen`) | `BuildGameState` (l.75-380), `AddCharacterSelectMenuState` (l.623), `AddMultiplayerJoinMenuState` (l.846), `AddMultiplayerLoadLobbyMenuState` (l.935), `BuildVisibleFtueState` (Helpers l.438) | `options`, `lobby`, `characters`, `epochs` |
| `monster` / `elite` / `boss` | `BuildBattleState` (l.1062) | `battle` |
| `hand_select` | `BuildHandSelectState` (l.2089) + `battle` | `hand_select` |
| `map` | `BuildMapState` (l.1673) | `map` |
| `event` | `BuildEventState` (l.1376) | `event` |
| `fake_merchant` | `BuildFakeMerchantState` (l.1436) | `fake_merchant` |
| `shop` | `BuildShopState` (l.1563) | `shop` |
| `rest_site` | `BuildRestSiteState` (l.1537) | `rest_site` |
| `treasure` | `BuildTreasureState` (l.2289) | `treasure` |
| `rewards` | `BuildRewardsState` (l.1821) | `rewards` |
| `card_reward` | `BuildCardRewardState` (l.1863) | `card_reward` |
| `card_select` | `BuildCardSelectState` (l.1888) / `BuildChooseCardState` (l.1997) | `card_select` |
| `bundle_select` | `BuildBundleSelectState` (l.2028) | `bundle_select` |
| `relic_select` | `BuildRelicSelectState` (l.2157) | `relic_select` |
| `crystal_sphere` | `BuildCrystalSphereState` (l.2190) | `crystal_sphere` |
| `game_over` | inline (l.452-460) | `game_over` |
| `overlay` | catch-all (l.461-476) | `overlay` |
| `unknown` | l.586-603 | `room_type` |

Champs **toujours** ajoutés hors menu : `run{act, floor, ascension}` (l.606-611) et `player{...}` (l.614-618, via `LocalContext.GetMe(runState)`).

### 3.2 `BuildGameState()` — `StateBuilder.cs:61`

**Lit / appelle :**
- `RunManager.Instance.IsInProgress` (l.73) — aiguillage menu vs run.
- `SceneTree` root + `FindFirst<T>()` sur des types UI concrets : `NSingleplayerSubmenu` (l.83), `NMultiplayerHostSubmenu` (l.114), `NMultiplayerSubmenu` (l.145), `NJoinFriendScreen` (l.179), `NMultiplayerLoadGameScreen` (l.190), `NCharacterSelectScreen` (l.200), `NTimelineScreen` (l.208), `NCompendiumSubmenu` (l.209), `NSettingsScreen` (l.210), `NProfileScreen` (l.290), `NMainMenu` (l.336).
- `RunManager.Instance.DebugOnlyGetState()` (l.389) → `RunState`.
- `NOverlayStack.Instance?.Peek()` (l.409) puis `is`-pattern sur `NCardGridSelectionScreen`, `NChooseACardSelectionScreen`, `NChooseABundleSelectionScreen`, `NChooseARelicSelection`, `NCrystalSphereScreen`, `NCardRewardSelectionScreen`, `NRewardsScreen`, `NGameOverScreen`, `IOverlayScreen` (l.412-476).
- `runState.CurrentRoom` (l.410), `runState.Map` (via BuildMapState), `runState.VisitedMapCoords`, `runState.CurrentActIndex`, `runState.TotalFloor`, `runState.AscensionLevel` (l.608-610).
- `CombatManager.Instance.IsInProgress` (l.484), `NPlayerHand.Instance.IsInCardSelection` (l.487-488), `combatRoom.RoomType` (l.496).
- `LocalContext.GetMe(runState)` (l.614).
- `SaveManager.Instance.Progress.Epochs` + `EpochState` (l.225-273), `SaveManager.Instance.CurrentProfileId` (l.295).

**JSON produit :**
```jsonc
{
  "state_type": "monster|elite|boss|map|event|shop|rest_site|treasure|rewards|card_reward|card_select|bundle_select|relic_select|crystal_sphere|hand_select|fake_merchant|game_over|overlay|menu|unknown",
  "run": { "act": 1, "floor": 3, "ascension": 0 },
  "player": { /* §3.3 */ },
  "battle" | "map" | "event" | "shop" | "rest_site" | "treasure" | "rewards" | "card_reward" | "card_select" | "bundle_select" | "relic_select" | "crystal_sphere" | "hand_select" | "fake_merchant" | "game_over" | "overlay": { ... },
  "message": "…"        // optionnel
}
```

**⚠ Mutations cachées dans le chemin GET** (voir §7.3) : `merchUI.OpenInventory()` (l.552-555), `merchantButton.ForceClick()` (l.1475), `chestButton.ForceClick()` (l.2306).

### 3.3 `BuildPlayerState(Player player)` — `StateBuilder.cs:1092`

| Champ JSON | Source exacte | Ligne |
|---|---|---|
| `character` | `player.Character.Title` (LocString) | 1098 |
| `hp` / `max_hp` / `block` | `creature.CurrentHp` / `.MaxHp` / `.Block` où `creature = player.Creature` | 1095, 1099-1101 |
| `energy` / `max_energy` | `player.PlayerCombatState.Energy` / `.MaxEnergy` | 1108-1109 |
| `stars` | `combatState.Stars`, si `player.Character.ShouldAlwaysShowStarCounter \|\| Stars > 0` | 1112-1114 |
| `hand[]` | `combatState.Hand.Cards` → `BuildCardState(card, i)` | 1120-1125 |
| `draw_pile_count` / `discard_pile_count` / `exhaust_pile_count` | `.Cards.Count` sur `DrawPile` / `DiscardPile` / `ExhaustPile` | 1128-1130 |
| `draw_pile[]` | `DrawPile.Cards.ToList()` **trié par `Rarity` puis `Id.Entry`** | 1133-1137 |
| `discard_pile[]` / `exhaust_pile[]` | `BuildPileCardList(pile.Cards, PileType.*)` | 1138-1139 |
| `orbs[]`, `orb_slots`, `orb_empty_slots` | `combatState.OrbQueue` : `.Capacity`, `.Orbs`, `orb.Id.Entry`, `orb.Title`, `orb.SmartDescription` (+ `Add("energyPrefix"/"Passive"/"Evoke")`), `orb.PassiveVal`, `orb.EvokeVal`, `orb.HoverTips` | 1142-1171 |
| `pets[]` | `BuildPetsState(player)` | 1174-1178 |
| `gold` | `player.Gold` | 1181 |
| `status[]` | `BuildPowersState(creature)` (toujours, même hors combat) | 1184 |
| `relics[]` | `player.Relics` : `relic.Id.Entry`, `.Title`, `.DynamicDescription`, `.ShowCounter ? .DisplayAmount : null`, `.HoverTipsExcludingRelic` | 1187-1199 |
| `potions[]` | `player.PotionSlots` (trous `null` conservés via index) : `potion.Id.Entry`, `.Title`, `.DynamicDescription`, `slot`, `can_use_in_combat` (`Usage == CombatOnly \|\| AnyTime`), `target_type = potion.TargetType`, `potion.ExtraHoverTips` | 1204-1221 |
| `max_potion_slots` | `player.MaxPotionCount` | 1222 |

**Garde importante** : tout le bloc combat est conditionné par `combatState != null && CombatManager.Instance.IsInProgress` (l.1106) — commentaire l.1103-1105 : `Energy`/`MaxEnergy` déclenchent `Hook.ModifyMaxEnergy` et **null-ref hors combat**.

**JSON produit :** voir `docs/raw-full.md:49-107` (schéma documenté) + l.111-165 pour `Card/Object`, `Pile Card`, `Orb`, `Power`, `Keyword`.

### 3.4 `BuildBattleState(RunState, CombatRoom)` — `StateBuilder.cs:1062`

```csharp
var combatState = CombatManager.Instance.DebugOnlyGetState();   // 1064
battle["round"]         = combatState.RoundNumber;               // 1073
battle["turn"]          = combatState.CurrentSide.ToString().ToLower(); // 1074
battle["is_play_phase"] = IsPlayPhase(combatState);              // 1075
foreach (var creature in combatState.Enemies)                    // 1080
    if (creature.IsAlive) enemies.Add(BuildEnemyState(creature, entityCounts)); // 1082-1085
```
JSON : `{ round:int, turn:"player"|"enemy", is_play_phase:bool, enemies:[Enemy] }`. Erreur si `combatState == null` → `{ "error": "Combat state unavailable" }` (l.1067-1071).

Variante multi `BuildMultiplayerBattleState` (`MultiplayerState.cs:259`) ajoute `all_players_ready = CombatManager.Instance.AllPlayersReadyToEndTurn()` (l.273).

### 3.5 `BuildEnemyState(Creature, Dictionary<string,int>)` — `StateBuilder.cs:1320`

| Champ | Source | Ligne |
|---|---|---|
| `entity_id` | `$"{monster.Id.Entry}_{count}"`, compteur incrémental **par id de monstre**, remis à zéro à chaque appel | 1322-1329 |
| `combat_id` | `creature.CombatId` | 1334 |
| `name` | `monster?.Title` | 1335 |
| `hp` / `max_hp` / `block` | `creature.CurrentHp` / `.MaxHp` / `.Block` | 1336-1338 |
| `status[]` | `BuildPowersState(creature)` | 1339 |
| `intents[]` | `monster?.NextMove is MoveState` → `moveState.Intents` (voir §4) | 1343-1370 |

### 3.6 `BuildCardInfo(CardModel, PileType)` — `StateBuilder.cs:1241` ; `BuildCardState` — `:1257`

`BuildCardInfo` (base commune) :
```csharp
["id"]          = card.Id.Entry;
["name"]        = SafeGetText(() => card.Title);
["type"]        = card.Type.ToString();
["cost"]        = GetCostDisplay(card);        // 1227-1228
["star_cost"]   = GetStarCostDisplay(card);    // 1230-1235
["description"] = SafeGetCardDescription(card, pile);
["rarity"]      = card.Rarity.ToString();
["is_upgraded"] = card.IsUpgraded;
["keywords"]    = BuildHoverTips(card.HoverTips);
```
- `GetCostDisplay` : `card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString()` (l.1227-1228).
- `GetStarCostDisplay` : `card.HasStarCostX` → `"X"` ; sinon `card.CurrentStarCost >= 0` → `card.GetStarCostWithModifiers()` ; sinon `null` (l.1230-1235).

`BuildCardState` ajoute (l.1259-1266) :
```csharp
card.CanPlay(out var unplayableReason, out _);
["index"] = index;
["description"] = SafeGetCardDescription(card);   // pile par défaut = Hand
["target_type"] = card.TargetType.ToString();
["can_play"] = unplayableReason == UnplayableReason.None;
["unplayable_reason"] = unplayableReason != UnplayableReason.None ? unplayableReason.ToString() : null;
```

### 3.7 `BuildPileCardList(IEnumerable<CardModel>, PileType)` — `StateBuilder.cs:1303`

Volontairement **pauvre** (commentaire l.1308 « keep it lightweight ») :
```jsonc
{ "name": "...", "cost": "1|X", "star_cost": null|"N"|"X", "description": "..." }
```
→ **pas d'`id`, pas de `type`, pas de `rarity`, pas de `is_upgraded`** (l.1309-1315). Impossible de recouper une carte de pile avec son `id` sans passer par `BuildCardInfo`.

### 3.8 `BuildPowersState(Creature)` — `StateBuilder.cs:2355`

- Itère `creature.Powers`, ignore `!power.IsVisible` (l.2358-2360).
- `power.HoverTips` (l.2368) : prend le tip dont `tip.Id == power.Id.ToString()` comme description résolue, les autres deviennent `keywords` (l.2371-2382).
- Fallback `power.SmartDescription` (l.2383).
- Champs : `id = power.Id.Entry`, `name = power.Title`, `amount = power.DisplayAmount`, `type = power.Type.ToString()` (`Buff`/`Debuff`), `description`, `keywords` (l.2385-2393).
- **try/catch par pouvoir** (l.2366, 2395) : un pouvoir qui throw est silencieusement omis.
- Utilisé pour joueur (l.1184), ennemis (l.1339), pets (l.2418), Osty.

### 3.9 `BuildEventState(EventRoom, RunState)` — `StateBuilder.cs:1376`

- `eventRoom.CanonicalEvent` (l.1380) ; `isAncient = eventModel is AncientEventModel` (l.1381).
- `eventModel.Id.Entry`, `.Title`, `.Description` (l.1382-1401).
- `NEventRoom.Instance` (l.1388) ; `FindFirst<NAncientEventLayout>` + `GetNodeOrNull<NClickableControl>("%DialogueHitbox")` pour `in_dialogue` (l.1391-1396).
- `FindAll<NEventOptionButton>(uiRoom)` (l.1407) → `button.Option` : `Title`, `Description`, `IsLocked`, `IsProceed`, `WasChosen`, `Relic` (+`Title`/`DynamicDescription`), `HoverTips` (l.1412-1426).

JSON : `{event_id, event_name, is_ancient, in_dialogue, body, options[{index,title,description,is_locked,is_proceed,was_chosen,relic_name?,relic_description?,keywords}]}`.

### 3.10 `BuildShopState(MerchantRoom, RunState)` — `StateBuilder.cs:1563`

- `merchantRoom.GetLocalInventory()` (l.1567) — **v0.107 a splitté l'inventaire par joueur** (commentaire `Actions.cs:365`).
- `inventory.CardEntries` : `entry.Cost`, `.IsStocked`, `.EnoughGold`, `.IsOnSale`, `.CreationResult?.Card` → `BuildCardInfo` (l.1581-1606).
- `inventory.RelicEntries` : `.Cost`, `.IsStocked`, `.EnoughGold`, `.Model` (l.1609-1628).
- `inventory.PotionEntries` : idem avec `.Model` (l.1631-1650).
- `inventory.CardRemovalEntry` (l.1653-1663).
- `NMerchantRoom.Instance?.ProceedButton?.IsEnabled` → `can_proceed` (l.1667-1668).
- Erreur douce si inventaire `null` (l.1568-1575).

`BuildFakeMerchantState` (l.1436) : `eventRoom.LocalMutableEvent ?? eventRoom.CanonicalEvent` casté `FakeMerchant` (l.1441), `.StartedFight`, `NFakeMerchant`, `NMerchantInventory`, `fakeMerchant.Inventory` → `BuildFakeMerchantShopItems` (l.1497, reliques uniquement).

### 3.11 `BuildMapState(RunState)` — `StateBuilder.cs:1673` (détail F6 en §9)

- `runState.Map`, `runState.VisitedMapCoords` (l.1677-1678) ; `map.GetPoint(coord)?.PointType` (l.1687).
- `NMapScreen.Instance` (l.1705) puis `FindAll<NMapPoint>().Where(mp => mp.State == MapPointState.Travelable && mp.Point != null)` triés par `col` (l.1708-1711). `leads_to` = `pt.Children` (l.1726-1734).
- `map.StartingMapPoint` (l.1746), `map.GetAllMapPoints()` (l.1750), `map.BossMapPoint` (l.1761), `map.SecondBossMapPoint` (l.1766).
- `runState.Act.BossEncounter` en try/catch (l.1755-1756, « throws if the act hasn't finished setup »), `runState.Act.SecondBossEncounter` (l.1757).
- `BuildMapNode` (l.1807-1819) : `{col, row, type, children:[[col,row],…]}`.

JSON complet documenté : `docs/raw-full.md:468-513`.

### 3.12 Autres builders (résumé factuel)

| Méthode | Lit | Ligne |
|---|---|---|
| `BuildRewardsState` | `FindAll<NRewardButton>(NRewardsScreen)`, `button.Reward`, `.IsEnabled`, `reward.Description`, `GoldReward.Amount`, `PotionReward.Potion`, `FindFirst<NProceedButton>` | 1821-1861 |
| `BuildCardRewardState` | `FindAllSortedByPosition<NCardHolder>`, `holder.CardModel`, `NCardRewardAlternativeButton` → `can_skip` | 1863-1886 |
| `BuildCardSelectState` | type de screen (`NDeckTransformSelectScreen`/`NDeckUpgradeSelectScreen`/`NDeckCardSelectScreen`/`NSimpleCardSelectScreen`), `%BottomLabel`, `NGridCardHolder`, `%UpgradeSinglePreviewContainer`/`%UpgradeMultiPreviewContainer`/`%PreviewContainer`, `NBackButton`/`NConfirmButton` | 1888-1995 |
| `AddPreviewCardsFromContainer` | `NCardHolder` puis fallback `NPreviewCardHolder` | 1270-1301 |
| `BuildChooseCardState` | `NGridCardHolder`, `"SkipButton"` | 1997-2026 |
| `BuildBundleSelectState` | `FindAll<NCardBundle>`, `bundle.Bundle`, `%BundlePreviewContainer`, `%Cards`, `NPreviewCardHolder`, `%Cancel`/`%Confirm` | 2028-2087 |
| `BuildHandSelectState` | `hand.CurrentMode`, `%SelectionHeader`, `hand.ActiveHolders`, `%SelectedHandCardContainer` + `NSelectedHandCardHolder`, `%SelectModeConfirmButton` | 2089-2155 |
| `BuildRelicSelectState` | `FindAll<NRelicBasicHolder>`, `holder.Relic?.Model`, `"SkipButton"` | 2157-2188 |
| `BuildCrystalSphereState` | `FindAll<NCrystalSphereCell>`, `cell.Entity.X/Y/IsHidden/IsHighlighted/IsHovered/Item`, `%BigDivinationButton`/`%SmallDivinationButton`/`%Outline`, `%DivinationsLeft` | 2190-2287 |
| `BuildTreasureState` | `FindFirst<NTreasureRoom>`, `"Chest"`, `%RelicCollection`, `NTreasureRoomRelicHolder`, `ProceedButton` | 2289-2342 |
| `BuildPetsState` | `player.PlayerCombatState.GetPet<Osty>()` puis `Monster?.Id.Entry`, `Title`, `IsAlive`, `CurrentHp`, `MaxHp`, `Block`, `BuildPowersState` | 2400-2422 |
| `BuildStartRunLobbyState` | `StartRunLobby.NetService.Type`, `.GameMode`, `.MaxPlayers`, `.Ascension`, `.MaxAscension`, `.Players[].id/.slotId/.character/.isReady`, `.LocalPlayer`, `.Seed`, `.IsAboutToBeginGame()` | 771-832 |
| `AddMultiplayerLoadLobbyMenuState` | `LoadRunLobby` récupéré **par réflexion** (`"_runLobby"`), `.Run.Players`, `.ConnectedPlayerIds`, `.IsPlayerReady(NetId)`, `.NetService.NetId` | 935-1060 |

### 3.13 `BuildMultiplayerGameState()` — `MultiplayerState.cs:27`

- Même logique « overlay d'abord » que le solo (l.82-131), puis les builders multi.
- Métadonnées : `game_mode = "multiplayer"` (l.65), `net_type = RunManager.Instance.NetService.Type.ToString()` (l.66), `player_count = runState.Players.Count` (l.67), `local_player_slot` (index dans `runState.Players`, l.71-79).
- `players = BuildAllPlayersState(runState)` (l.247) + `player = BuildPlayerState(localPlayer)` (l.253).
- Refuse de servir si `!NetService.Type.IsMultiplayer()` (l.57-62).

---

## 4. Lecture des intents ennemis et des dégâts affichés

### 4.1 Code exact — `StateBuilder.cs:1342-1371`

```csharp
if (monster?.NextMove is MoveState moveState)          // 1343
{
    var intents = new List<Dictionary<string, object?>>();
    foreach (var intent in moveState.Intents)          // 1346
    {
        var intentData = new Dictionary<string, object?> {
            ["type"] = intent.IntentType.ToString()    // 1350
        };
        try {
            var targets = creature.CombatState?.PlayerCreatures;   // 1354
            if (targets != null) {
                string label = intent.GetIntentLabel(targets, creature).GetFormattedText(); // 1357
                intentData["label"] = StripRichTextTags(label);      // 1358
                var hoverTip = intent.GetHoverTip(targets, creature); // 1360
                if (hoverTip.Title != null)       intentData["title"] = StripRichTextTags(hoverTip.Title);       // 1362
                if (hoverTip.Description != null) intentData["description"] = StripRichTextTags(hoverTip.Description); // 1364
            }
        } catch { /* intent label may fail for some types */ }      // 1367
        intents.Add(intentData);
    }
    state["intents"] = intents;                        // 1370
}
```

### 4.2 Ce que ça donne, et ce que ça ne donne **pas**

- `intent.IntentType` → enum stringifié (`Attack`, `Defend`, `Buff`, `Debuff`, `Sleep`, …). C'est le **seul champ typé**.
- `label` = le texte **affiché par le jeu** via `GetIntentLabel(...).GetFormattedText()` puis nettoyé du BBCode. **C'est une `string`, pas un nombre** : le nombre de dégâts n'est jamais exposé en entier, ni `Repeats`, ni le détail par coup.
- `title` / `description` = contenu du hover tip (texte localisé, ex. « Deals 11 damage. »).
- **ABSENT** dans tout le dépôt : `AttackIntent`, `DamageCalc`, `GetSingleDamage`, `GetTotalDamage`, `Repeats`, toute variante Weak/Vulnerable, tout calcul de dégâts. Vérifié par grep sur `*.cs` : les seules occurrences de « Intent » sont `StateBuilder.cs:13,1342-1370` et `Formatting.cs:410-421`.
- Seul le **prochain** move est lu (`NextMove`) : la file de moves suivante n'est pas exposée.

### 4.3 Rendu Markdown — `Formatting.cs:410-421`

```
**Intent:** {title ?? type} ({type}) {label} - {description}
```
(ex. `Attack (Attack) 11 - Deals 11 damage.`)

### 4.4 Conséquence pour ULTIMA F1/F2

STS2MCP ne fournit **aucune** brique de prévision : ni dégâts par coup, ni multiplicateurs, ni répétitions. Le seul point de départ réutilisable est la **structure d'itération** (`monster.NextMove as MoveState` → `moveState.Intents` → `intent.GetIntentLabel(targets, owner)` avec `targets = creature.CombatState?.PlayerCreatures`) et le fait que `GetIntentLabel` rend déjà exactement ce que le joueur voit (donc la valeur « courante » post-modificateurs). Pour les variantes Weak/Vulnerable et le NxM, il faut descendre sur `AttackIntent` (`DamageCalc`, `Repeats`, `GetSingleDamage`) — hors du périmètre de ce dépôt.

---

## 5. Piles, reliques, potions, pouvoirs, HP/block

### 5.1 HP / block / or / énergie

| Donnée | Accès exact | Ligne |
|---|---|---|
| HP joueur | `player.Creature.CurrentHp` | 1099 |
| Max HP | `player.Creature.MaxHp` | 1100 |
| Block joueur | `player.Creature.Block` | 1101 |
| Or | `player.Gold` | 1181 |
| Énergie | `player.PlayerCombatState.Energy` / `.MaxEnergy` | 1108-1109 |
| Étoiles | `combatState.Stars` (+ `Character.ShouldAlwaysShowStarCounter`) | 1112-1114 |
| HP/block ennemi | `creature.CurrentHp` / `.MaxHp` / `.Block` | 1336-1338 |
| HP/block pet (Osty) | `osty.CurrentHp` / `.MaxHp` / `.Block`, `osty.IsAlive` | 2414-2417 |
| Block équipiers (multi) | `player.Creature.Block` dans `BuildAllPlayersState` | MState 430 |

### 5.2 Piles (pioche / défausse / épuisées / main)

- **Main** : `combatState.Hand.Cards` → `BuildCardState` (index = position) — `StateBuilder.cs:1120-1125`.
- **Compteurs** : `DrawPile.Cards.Count`, `DiscardPile.Cards.Count`, `ExhaustPile.Cards.Count` — l.1128-1130.
- **Contenu** :
  - pioche : `.ToList()` puis `Sort` par `Rarity` croissant, puis `Id.Entry` ordinal (l.1133-1136) → **ce n'est PAS l'ordre de pioche** ;
  - défausse / épuisées : ordre natif de la collection (l.1138-1139).
- **Chargement par carte** : `BuildPileCardList` → `{name, cost, star_cost, description}` uniquement (l.1303-1318).
- **Sélection en combat** : `NPlayerHand.Instance.IsInCardSelection` (l.487-488) → `BuildHandSelectState` (l.2089) qui lit `hand.ActiveHolders` et `%SelectedHandCardContainer`.

**ABSENT** : aucune lecture du deck complet hors combat. `BuildPlayerState` n'expose les piles que dans le bloc `if (combatState != null && CombatManager.Instance.IsInProgress)` (l.1106-1179). Sur la map / au repos / en boutique, il n'y a **ni deck ni piles** dans le JSON. Le seul endroit où un deck apparaît est le deck de départ en sélection de personnage (`cm.StartingDeck`, l.672-675) et le `deck_count` des fichiers de run (Compendium l.340-341).

### 5.3 Reliques — `StateBuilder.cs:1187-1199`

```csharp
foreach (var relic in player.Relics)
    ["id"] = relic.Id.Entry;
    ["name"] = relic.Title;
    ["description"] = relic.DynamicDescription;
    ["counter"] = relic.ShowCounter ? relic.DisplayAmount : null;
    ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic);
```
Même schéma en sélection de relique (l.2171-2179) et en trésor (l.2325-2333), avec en plus `rarity`.

### 5.4 Potions — `StateBuilder.cs:1202-1222`

`player.PotionSlots` (itération avec `slotIndex++` même sur les slots vides), champs : `id`, `name`, `description`, `slot`, `can_use_in_combat` (`potion.Usage == PotionUsage.CombatOnly || PotionUsage.AnyTime`), `target_type` (`potion.TargetType.ToString()`), `keywords` (`potion.ExtraHoverTips`). `max_potion_slots = player.MaxPotionCount`.

### 5.5 Pouvoirs (statuts)

`BuildPowersState(creature)` — §3.8. Présent pour joueur, ennemis, pets. Champ JSON nommé **`status`** (pas `powers`), avec `amount = power.DisplayAmount`.

### 5.6 Orb queue (Defect)

`combatState.OrbQueue.Capacity`, `.Orbs` ; par orbe : `Id.Entry`, `Title`, `SmartDescription` enrichi des placeholders `energyPrefix` / `Passive` / `Evoke`, `PassiveVal`, `EvokeVal`, `HoverTips` (l.1142-1171). `orb_slots`, `orb_empty_slots` dérivés.

---

## 6. Multi — `NetService.Type`, ids joueurs, lobbies

### 6.1 Détection et type de réseau

| Besoin | Code | Ligne |
|---|---|---|
| Suis-je en multi ? | `RunManager.Instance.IsInProgress && RunManager.Instance.NetService.Type.IsMultiplayer()` | `McpMod.cs:285-286` |
| Type affiché | `RunManager.Instance.NetService.Type.ToString()` → `net_type` | MState 66 |
| Mapping lobby | `NetGameType.Host` → `"host"`, `Client` → `"client"`, `Singleplayer` → `"singleplayer"` | StateBuilder 775-781 |
| Id local | `lobby.LocalPlayer.id` (l.794-796) ; `lobby.NetService.NetId` (l.961, 1006) | — |

### 6.2 Identité des joueurs

- `runState.Players` (`IReadOnlyList<Player>`) — MState 67, 299, 387, 417.
- `LocalContext.GetMe(runState)` (MState 68), `LocalContext.IsMe(player)` (MState 305, 348, 393, 422, 434).
- `player.NetId` (ulong) pour les saves multi (StateBuilder 961, 1006).
- `lobby.Players[].id` (ulong) / `.slotId` / `.character` / `.isReady` (StateBuilder 805-824).
- Nom de plateforme : `PlatformUtil.GetPlayerName(platform, id)` en try/catch (`StateBuilder.cs:840-844`, utilisé l.821 et 1010) ; `PlatformUtil.PrimaryPlatform` (l.881).
- **Pas d'index de slot dans le JSON de run** : `local_player_slot` est l'index dans `runState.Players` (MState 71-79), calculé par comparaison de références.

### 6.3 État par joueur — `BuildAllPlayersState` (`MultiplayerState.cs:413`)

```csharp
["character"] = player.Character.Title;   // 421
["is_local"]  = LocalContext.IsMe(player);// 422
["hp"], ["max_hp"] = player.Creature.CurrentHp / MaxHp;  // 423-424
["gold"]      = player.Gold;              // 425
["is_alive"]  = player.Creature.IsAlive;  // 426
// si CombatManager.Instance.IsInProgress (l.415, 428) :
["block"]     = player.Creature.Block;                                   // 430
["is_ready_to_end_turn"] = CombatManager.Instance.IsPlayerReadyToEndTurn(player); // 431
["pets"]      = BuildPetsState(player)   // seulement si !IsMe (l.434-441)
```

### 6.4 Synchroniseurs multi lus (jamais écrits)

| Donnée | API | Ligne |
|---|---|---|
| Votes de map | `RunManager.Instance.MapSelectionSynchronizer.GetVote(player)` → `vote?.coord.col/row` | MState 296-309 |
| Event partagé | `RunManager.Instance.EventSynchronizer.IsShared`, `.GetPlayerVote(player)` | MState 334-350 |
| Bids trésor | `RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics`, `.GetPlayerVote(player)` | MState 379-399 |
| Tous prêts | `CombatManager.Instance.AllPlayersReadyToEndTurn()` | MState 273 |
| Prêt à finir le tour | `CombatManager.Instance.IsPlayerReadyToEndTurn(player)` | MState 431 |
| Lobby (pré-run) | `StartRunLobby` : `Players`, `LocalPlayer`, `MaxPlayers`, `Ascension`, `MaxAscension`, `GameMode`, `Seed`, `IsAboutToBeginGame()` | StateBuilder 771-832 |
| Lobby (reprise de save) | `LoadRunLobby` (champ privé `_runLobby` par réflexion) : `Run`, `ConnectedPlayerIds`, `IsPlayerReady(NetId)` | StateBuilder 935-1060 |

### 6.5 Ce qui est **ABSENT** (critique pour F3/F4)

- **Aucun `INetMessage`**, aucun `SendMessage`, aucun `RegisterMessageHandler` dans le dépôt (grep négatif sur `*.cs`).
- **Aucun `HoveredModelTracker`**, aucun `HoverChanged`, aucune lecture de la carte survolée/sélectionnée par un autre joueur (grep négatif).
- Aucune notification *push* : le seul canal est le **polling** `GET`.
- Aucun état « qui a sélectionné quelle carte » ni « qui est en train de jouer » au-delà de `is_ready_to_end_turn`.
- Le mod n'annonce pas sa présence aux autres joueurs (pas de handshake de version).

Donc F3 (« alerte multi quand un joueur a/sélectionne une carte à jouer en premier ») et F4 (« broadcast d'alertes ») n'ont **aucun précédent** dans STS2MCP ; il faudra les construire (piste réseau : `HoveredModelTracker` / `INetMessage`, à vérifier dans `src/Core` avant usage).

### 6.6 Détection « FastMP » / join localhost

`bool fastMp = !SteamInitializer.Initialized || CommandLineHelper.HasArg("fastmp")` (StateBuilder 856) ; message d'état l.913-918 : auto-join `localhost:33771`. Utile pour tester le multi sans Steam.

---

## 7. Ce qu'il fait d'INSCRIPTION / ACTION — à ne PAS reprendre

### 7.1 Commandes de jeu et files d'actions (mutations d'état)

| API | Usage | Ligne |
|---|---|---|
| `RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, target))` | jouer une carte (chemin UI/sync) | `Actions.cs:140` |
| `PlayerCmd.EndTurn(player, canBackOut: false)` | fin de tour solo | `Actions.cs:163` |
| `PotionCmd.Discard(potion)` | jeter une potion | `Actions.cs:261` |
| `ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, roundNumber))` | vote de fin de tour multi | `MultiplayerActions.cs:95-96` |
| `ActionQueueSynchronizer.RequestEnqueue(new UndoEndPlayerTurnAction(player, roundNumber))` | retrait du vote | `MultiplayerActions.cs:123-124` |
| `entry.OnTryPurchaseWrapper(inventory)` | achat boutique | `Actions.cs:417` |
| `mapScreen.OnMapPointSelectedLocally(target)` | choix de nœud de map | `Actions.cs:449` |
| `charSelect.Lobby.SetSeed(seed)` | seed de run | `Actions.cs:1631` |
| `btn.Select()` (`NCharacterSelectButton`) | sélection de personnage | `Actions.cs:1658` |
| `SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant` | **écrit les préférences** | `SettingsUI.cs:111` et `:126` |

### 7.2 Actions simulées par clics UI (ForceClick / EmitSignal)

Toutes les autres actions passent par la **simulation d'UI**, ce qui est le plus fragile et le plus éloigné du read-only :

- `NClickableControl.ForceClick()` : ~45 occurrences (`Actions.cs:292, 315, 346, 386, 486, 531, 549, 557, 568, 572, 588, 593, 604, 679, 694, 710, 732, 755, 769, 798, 816, 834, 879, 905, 924, 958, 989, 1036, 1140, 1160, 1193, 1202, 1210, 1223, 1256, 1425, 1448, 1482, 1514, 1530, 1555, 1566, 1595, 1601, 1612, 1643, 1887`).
- `holder.EmitSignal(NCardHolder.SignalName.Pressed, holder)` (`Actions.cs:512`, `:648`, `:860`).
- `grid.EmitSignal(NCardGrid.SignalName.HolderPressed, holder)` (`Actions.cs:632`).
- `cell.EmitSignal(NClickableControl.SignalName.Released, cell)` (`Actions.cs:1018`).
- `bundles[index].Hitbox.ForceClick()` (`Actions.cs:798`).
- `merchUI.OpenInventory()` (`Actions.cs:364`, `StateBuilder.cs:554`).
- `chestButton.ForceClick()` (`StateBuilder.cs:2306`), `merchantButton.ForceClick()` (`StateBuilder.cs:1475`).
- `timelineScreen._stack.Pop()` par réflexion (`Actions.cs:1261-1265`).
- `gameOver.ReturnToMainMenu()` par réflexion (`Actions.cs:1107-1114`).
- `SwitchToThisProfile()` par réflexion (`Profile.cs:203-208`).
- Suppression/switch de profil (`Profile.cs:121-185`) — passe par l'UI du jeu.

### 7.3 Le point le plus important : le GET **n'est pas** read-only

Trois mutations se produisent pendant la construction de l'état (donc sur un simple `GET`) :

1. `merchUI.OpenInventory()` si l'inventaire du marchand est fermé — `StateBuilder.cs:552-555` (commentaire assumé l.548-550).
2. `chestButton.ForceClick()` pour ouvrir le coffre au trésor — `StateBuilder.cs:2303-2309`.
3. `merchantButton.ForceClick()` pour le faux marchand — `StateBuilder.cs:1466-1477`.

**À supprimer impérativement** dans ULTIMA : remplacer par « signaler que l'inventaire/coffre est fermé » et laisser le joueur cliquer. Sinon un simple polling ouvre des coffres et des boutiques.

### 7.4 Catalogue complet des actions exposées (pour mémoire, tout est à exclure)

`ExecuteAction` (`Actions.cs:62-91`) : `play_card`, `use_potion`, `discard_potion`, `end_turn`, `choose_map_node`, `choose_event_option`, `advance_dialogue`, `choose_rest_option`, `shop_purchase`, `claim_reward`, `select_card_reward`, `skip_card_reward`, `proceed`, `select_card`, `confirm_selection`, `cancel_selection`, `select_bundle`, `confirm_bundle_selection`, `cancel_bundle_selection`, `combat_select_card`, `combat_confirm_selection`, `select_relic`, `skip_relic_selection`, `claim_treasure_relic`, `crystal_sphere_set_tool`, `crystal_sphere_click_cell`, `crystal_sphere_proceed`.
`ExecuteMultiplayerAction` (`MultiplayerActions.cs:35-68`) : les mêmes + `end_turn` / `undo_end_turn` (variantes synchronisées).
`ExecuteMenuSelect` (`Actions.cs:1080-1663`) : navigation complète des menus, profils, timeline, FTUE, popups, character select, join, load lobby.

### 7.5 Frontière « read-only » utilisable

Ce qui, dans le chemin de lecture, **ne mute pas** et peut être conservé :
- `RunManager.Instance.DebugOnlyGetState()`, `CombatManager.Instance.DebugOnlyGetState()`, `RunManager.Instance.IsInProgress`, `RunManager.Instance.NetService.Type`.
- Tous les getters de modèles (`CardModel.Id/Title/Type/Rarity/IsUpgraded/TargetType/EnergyCost`, `Creature.CurrentHp/MaxHp/Block/Powers`, `Player.Relics/PotionSlots/Gold`, `Monster.NextMove`, `Power.DisplayAmount`, `Relic.ShowCounter/DisplayAmount`, `Orb.PassiveVal/EvokeVal`).
- `card.CanPlay(out reason, out _)` — requête avec `out`, déjà utilisée dans le chemin de lecture (`StateBuilder.cs:1259`) ; à garder mais entourer de try/catch.
- Les getters de description (`GetDescriptionForPile`, `DynamicDescription`, `HoverTips`) — attention : ce sont des getters « lourds » qui résolvent des `LocString` et des `DynamicVars`.
- La lecture des nodes UI **en lecture** (`GetNodeOrNull`, `Visible`, `IsEnabled`, `GlobalPosition`).

---

## 8. Helpers de réflexion et hacks de champs privés

### 8.1 Le helper central — `Helpers.cs:163-179`

```csharp
private static object? GetInstanceFieldValue(object source, string fieldName)
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public
                             | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    for (var type = source.GetType(); type != null; type = type.BaseType)
    {
        var field = type.GetField(fieldName, Flags);
        if (field != null) return field.GetValue(source);
    }
    return null;
}
```
Points clés : `DeclaredOnly` **plus** remontée manuelle de `BaseType` → permet de trouver un champ privé déclaré dans une classe parente sans se faire masquer par un champ homonyme. Renvoie `null` silencieusement si absent (pas d'exception).

### 8.2 Champs privés accédés par nom (littéraux)

| Champ | Type attendu | Occurrences |
|---|---|---|
| `_backButton` | `NBackButton`/`NClickableControl` | StateBuilder 316, 728 ; Actions 1251, 1574 |
| `_unreadyButton` | `NClickableControl` | StateBuilder 756, 1048 ; Actions 1563, 1609 |
| `_confirmButton` | `NClickableControl` | StateBuilder 1023 ; Helpers 429, 778 |
| `_embarkButton` | `NClickableControl` | StateBuilder 709 ; Actions 1639 |
| `_standardButton` / `_dailyButton` / `_customButton` | boutons de mode | StateBuilder 90, 121 |
| `_hostButton` / `_joinButton` / `_loadButton` / `_abandonButton` | boutons multi | StateBuilder 152 |
| `_profileButtons` | `IEnumerable` (puis `_profileId` sur chaque) | StateBuilder 298, 303 |
| `_runLobby` | `LoadRunLobby` | StateBuilder 942 |
| `_loadingFriendsIndicator` / `_loadingOverlay` / `_noFriendsLabel` / `_buttonContainer` / `_refreshButton` | nodes UI join | StateBuilder 859-902 |
| `_label` | label de bouton popup | Helpers 635 |
| `_verticalPopup` | popup du FTUE tutoriels | Actions 1131 |
| `_acknowledgeButton` | bouton FTUE | Actions 1188 |
| `_closeButton` | écran d'inspection | Actions 1218 |
| `_stack` | pile d'écrans timeline (`.Pop()`) | Actions 1261-1265 |
| `_hoverTip` | `IHoverTip` sur `NFastModeHoverTip` | SettingsUI 162 |
| `_isUiVisible`, `_inputBlocker`, `_unlockScreens`, `_unlockedEpochs`, `_erasToUnlock`, `_epoch` | divers (timeline/unlocks) | grep `GetInstanceFieldValue` |

Plus un cas de **backing field explicite** : `"<YesButton>k__BackingField"` / `"<NoButton>k__BackingField"` (`Actions.cs:1135-1136`).

### 8.3 Accès par propriété (nom en chaîne)

- `btn.GetType().GetProperty("IsEnabled")?.GetValue(btn) as bool?` — pattern répété (`Helpers.cs:192`, `StateBuilder.cs:98, 129, 160, 306`).
- `popup.GetType().GetProperty(propertyName)` avec `"TitleLabel"` / `"BodyLabel"` (`Helpers.cs:627-631`, appelé l.504-505).
- Sondage de label : `GetClickableControlLabel` essaie `"_label"`, `"_textLabel"`, `"_title"`, `"_buttonLabel"`, puis les propriétés `Text`/`Label`/`Title`, puis le texte récursif des enfants, puis le nom du node (`Helpers.cs:639-666`).
- `GetNodeText` : propriétés `Text` puis `BbcodeText` (`Helpers.cs:697-710`).

### 8.4 Le hack le plus profond : `HoverTip` (record struct) — `SettingsUI.cs:146-163`

```csharp
object boxed = (object)default(HoverTip);
var ht = typeof(HoverTip);
ht.GetProperty("Id")!.SetValue(boxed, "sts2mcp_instant_mode");
ht.GetProperty("Title")!.GetSetMethod(true)!.Invoke(boxed, ["Instant Mode"]);
ht.GetProperty("Description")!.GetSetMethod(true)!.Invoke(boxed, ["..."]);
typeof(NFastModeHoverTip).GetField("_hoverTip", BindingFlags.NonPublic | BindingFlags.Instance)
    ?.SetValue(__instance, (IHoverTip)boxed);
```
Technique : **boxer** un struct, appeler les setters privés (`GetSetMethod(true)`) sur la copie boxée, puis réinjecter la boîte dans le champ privé. C'est le seul moyen propre de fabriquer un `HoverTip` custom sans constructeur public.

### 8.5 Réflexion « en aveugle » et `dynamic`

- `FindFtueAdvanceButton` (`Helpers.cs:774-822`) : essaie 8 noms de champs, puis **énumère tous les champs** de l'instance et prend le premier `NClickableControl` actionnable, puis fallback `FindAll<NClickableControl>`. C'est du contournement de type inconnu.
- `Compendium.cs` utilise `dynamic` pour les stats (`BuildEncounterStats` l.185, `BuildEnemyStats` l.193, `BuildFightStatsEntry` l.201) et `type.GetMembers(BindingFlags.Instance|Public|NonPublic)` (l.247-262) pour sérialiser des membres non typés de `ProgressState`.
- `SteamUtil.GetSteamID64` par réflexion statique (`Compendium.cs:547-566`).
- `Actions.cs:1767-1771` (`Count`/`Peek` sur une file inconnue), `:1830-1831` (propriétés `Model`/`Id` d'un objet anonyme).
- `Profile.cs:203-208` (`SwitchToThisProfile`).

### 8.6 Chemins de nodes « unique name » (`%Name`)

Utilisés partout à la place des champs privés quand c'est possible : `%DialogueHitbox` (StateBuilder 1394), `%BottomLabel` (1904), `%UpgradeSinglePreviewContainer` / `%UpgradeMultiPreviewContainer` / `%PreviewContainer` (1930-1932), `%Close` (1964), `%PreviewCancel` (1957), `%PreviewConfirm` (1976), `%Confirm` (1983), `%BundlePreviewContainer` (2059), `%Cards` (2064), `%Cancel` (2081), `%SelectionHeader` (2102), `%SelectedHandCardContainer` (2129), `%SelectModeConfirmButton` (2151), `%RelicCollection` (2312), `%InstructionsTitle` (2194), `%InstructionsDescription` (2202), `%BigDivinationButton` (2265), `%SmallDivinationButton` (2266), `%Outline` (2269), `%DivinationsLeft` (2276), `%ProceedButton` (1046 Actions), `%GeneralSettings` (SettingsUI 49), `"Chest"` (2303), `"SkipButton"` (2019, 2184), `"Confirm"` (1975), `"Cancel"` (1956).

**Recommandation** : privilégier `%Name` (chemin stable exposé par la scène) sur les champs privés `_xxx`, qui changent à chaque refactor du jeu.

---

## 9. Réutilisable pour ULTIMA F1–F6 (méthode exacte + adaptation read-only)

Règle générale d'adaptation : **copier la structure de lecture, jamais l'action**. Concrètement :
1. remplacer tout `ForceClick()` / `EmitSignal()` / `OpenInventory()` / `Cmd.*` / `RequestEnqueue()` par du néant ou par un flag « non disponible » ;
2. garder `RunOnMainThread` (obligatoire : les propriétés du jeu ne sont pas thread-safe) ;
3. garder les `try/catch` par élément (`SafeGetText`, `BuildPowersState`) mais **logger** au lieu d'avaler ;
4. ne JAMAIS publier l'API sur un port accessible sans contrôle (pas d'auth dans STS2MCP, CORS `*`).

### F1 — Prévision des dégâts d'intent + variantes Weak/Vulnerable

| À reprendre | Où | Comment |
|---|---|---|
| Boucle d'intents | `BuildEnemyState` `StateBuilder.cs:1343-1371` | garder `monster?.NextMove is MoveState` + `moveState.Intents`, `targets = creature.CombatState?.PlayerCreatures` |
| Cible(s) du calcul | même code l.1354 | `creature.CombatState?.PlayerCreatures` (les coéquipiers en multi : calculer par cible) |
| Valeur « affichée » de référence | l.1357 `intent.GetIntentLabel(targets, creature).GetFormattedText()` | **sert de test de non-régression** : ton calcul doit reproduire ce nombre, sinon ton pipeline est faux |
| États qui modulent | `BuildPowersState` `StateBuilder.cs:2355-2398` | lire `Weak`/`Vulnerable`/`Strength` via `power.Id.Entry` + `power.DisplayAmount` (attention : `DisplayAmount`, pas `Amount`) |
| HP/block de la cible | `BuildPlayerState` l.1099-1101 | `creature.CurrentHp` / `.Block` |

À ajouter (ABSENT du dépôt) : `AttackIntent.DamageCalc`, `.Repeats`, `.GetSingleDamage(targets, owner)` — noms d'API issus de la baseline **v0.107.1 du code source du jeu** (`src/Core/MonsterMoves/Intents/AttackIntent.cs`), à re-vérifier avant usage ; ne pas re-multiplier par Weak/Vulnerable si `GetSingleDamage` les a déjà appliqués (double comptage). Ne **jamais** parser `label` : c'est une chaîne localisée.

### F2 — Index du coup létal sur une séquence NxM (block/HP)

| À reprendre | Où | Comment |
|---|---|---|
| Ennemis vivants + `entity_id` stable | `BuildBattleState` l.1080-1087 + `BuildEnemyState` l.1326-1334 | utiliser `creature.CombatId` comme clé stable ; `entity_id` se décale quand un ennemi meurt (compteur recalculé à chaque appel) |
| HP/block joueur | `BuildPlayerState` l.1099-1101 | recalculer à chaque frame, ne pas mémoriser |
| HP/block ennemis | `BuildEnemyState` l.1336-1338 | — |
| Cible de l'attaque | `creature.CombatState?.PlayerCreatures` | en multi, simuler par joueur ciblé |
| Cap de dégâts (Intangible) | `BuildPowersState` l.2355 | chercher `Intangible` dans `status` avant d'annoncer une létalité |

Logique à écrire (aucune trace dans le dépôt) : pour `i in 1..Repeats`, `absorbed = min(block, dmg)`, etc. ; afficher « létal au 3e coup » ou « reste X PV ». Cas à couvrir : `perHit == 0`, mort au 1er coup, overkill, cap Intangible, block qui persiste entre les coups.

### F3 — Alerte multi : un joueur a/sélectionne une carte à jouer en premier

**ABSENT** dans STS2MCP : pas de `HoveredModelTracker`, pas de `HoverChanged`, pas de carte survolée d'un autre joueur, pas de « selected card ». Voir §6.5.

Ce qui est réutilisable :
| À reprendre | Où | Comment |
|---|---|---|
| Identification des joueurs | `BuildAllPlayersState` `MultiplayerState.cs:413-446` | `player.NetId`, `LocalContext.IsMe(player)`, `Character.Title` |
| Détection multi | `McpMod.cs:285-286` | `NetService.Type.IsMultiplayer()` |
| Noms de plateforme | `SafeGetPlayerName` `StateBuilder.cs:840-844` | `PlatformUtil.GetPlayerName(platform, id)` en try/catch |
| Convention de nommage JSON | tout le dépôt | snake_case, `is_local`, `player`, `entity_id` |

À construire : lecture du survol/sélection (`RunManager.Instance.HoveredModelTracker` — à vérifier dans `src/Core`), et surtout un **canal de diffusion** (F4). Le polling HTTP de STS2MCP ne suffit pas (latence + un seul client local).

### F4 — Broadcast d'alertes (multi)

**ABSENT** intégralement : aucun `INetMessage`, aucun `NetService.SendMessage`, aucun `RegisterMessageHandler`, aucun handshake de version, aucun code de sérialisation réseau.

Réutilisable seulement : le pattern d'accès `RunManager.Instance.NetService` (`MultiplayerState.cs:57`, `:66`) et la détection host/client (`StateBuilder.cs:775-781`). Pour le reste, partir de zéro (interface `INetMessage` + `ShouldBroadcast` + `Mode` + `LogLevel` + `ShouldBuffer`, découverte par réflexion côté jeu).

### F5 — Scoring de run par carte / relique / potion

| À reprendre | Où | Comment (read-only) |
|---|---|---|
| Stats par carte | `Compendium.cs:52-59` (`SaveManager.Instance.Progress.CardStats`) | `TimesPicked`, `TimesSkipped`, `TimesWon`, `TimesLost` par `Id.Entry` — agrégat historique, pas par run |
| Stats par personnage | `Compendium.cs:61-72` | `MaxAscension`, `TotalWins/Losses`, `BestWinStreak`, `Playtime` |
| Découvertes | `Compendium.cs:81-83` | `DiscoveredCards` / `DiscoveredRelics` / `DiscoveredPotions` (`Id.Entry`) |
| Totaux globaux | `Compendium.cs:89-101` | `CurrentScore`, `FloorsClimbed`, `ArchitectDamage`, `Wins`, `Losses`, `NumberOfRuns`, `TotalPlaytime` |
| Historique de runs | `Compendium.cs:264-358` | lecture disque des `saves/history/*.run` en `JsonDocument` : `start_time`, `run_time`, `ascension`, `win`, `killed_by_encounter`, `killed_by_event`, `seed`, `acts`, `players[].deck_count/relic_count/potion_count`, `map_point_count` (l.413-425) |
| Run courante | `Compendium.cs:360-411` | `current_run.save` : `start_time`, `save_time`, `run_time`, `game_mode`, `ascension`, `current_act_index`, `rng.seed` |

**ABSENT** : aucune attribution par run en cours (dégâts par carte, PV gagnés par relique, valeur d'une potion). Le dépôt ne fournit que des **agrégats de profil** et un **comptage** dans les fichiers de run. Pour F5 il faut instrumenter soi-même (hooks `AfterCardPlayedLate`, `CombatManager.Instance.History.CardPlaysFinished`, dégâts par source) — hors périmètre de ce dépôt.

### F6 — Risque des chemins de map

C'est la feature **la plus directement réutilisable** :

| À reprendre | Où | Comment |
|---|---|---|
| DAG complet | `BuildMapState` `StateBuilder.cs:1743-1776` + `BuildMapNode` `:1807-1819` | `map.StartingMapPoint`, `map.GetAllMapPoints()`, `pt.coord.col/row`, `pt.PointType`, `pt.Children` → exactement la structure d'un calcul de risque |
| Position courante | l.1681-1689 | `runState.VisitedMapCoords.Last()`, `map.GetPoint(coord)?.PointType` |
| Nœuds accessibles maintenant | l.1705-1739 | `NMapScreen.Instance` + `NMapPoint.State == MapPointState.Travelable` (⚠ dépend de l'UI : `NMapScreen` doit exister) |
| Lookahead 1 niveau | l.1726-1734 | `pt.Children` triés par `col` |
| Boss de l'acte | l.1753-1781 | `runState.Act.BossEncounter` (en try/catch), `runState.Act.SecondBossEncounter`, `map.BossMapPoint`, `map.SecondBossMapPoint` |
| Contexte run | l.606-611 | `act`, `floor`, `ascension` |

**ABSENT** : le contenu des rencontres par nœud (pools de monstres/élites par acte), les dégâts attendus par combat, toute notion de risque. `BuildMapState` ne donne que `PointType`. Pour scorer le risque il faut croiser avec les `EncounterStats` (`Compendium.cs:185-200`) ou le catalogue d'ennemis — à construire.

**Recommandation** : pour F6, utiliser `map.GetAllMapPoints()` (données) et **pas** `next_options` (UI) : le premier fonctionne même quand l'écran de map est fermé, le second exige `NMapScreen`.

---

## 10. Pièges, dépendances, version testée, licence

### 10.1 Licence

- **MIT**, titulaire « Copyright 2026 Yikun Ji (Kunologist) » — `LICENSE:1-5`. Confirmé par `README.md:181-183`.
- Conséquence : copie, modification, redistribution autorisées, **avec conservation du copyright et de la notice**. Attribution obligatoire dans ULTIMA (fichier `NOTICE`/`THIRD-PARTY`).

### 10.2 Version du jeu

| Source | Version |
|---|---|
| `README.md:9` | « Tested against STS2 `v0.103.2` » — **obsolète** |
| HEAD commit `55e0648` (2026-07-29) | « Fix game API compatibility with STS2 **v0.107** » |
| `Helpers.cs:19-21` | « STS2 **v0.107** removed `CombatManager.IsPlayPhase` » → phase par joueur dans `PlayerCombatState.Phase` |
| `Actions.cs:365` | « **v0.107** split the merchant into per-player inventories » |
| `McpMod.cs:22` + `mod_manifest.json:6` | version du mod : `0.4.0` |

→ Le code cible **v0.107.x** ; la doc README est en retard d'une version. Le jeu installé ici est en v0.107.1 : cohérent avec le HEAD.

### 10.3 Dépendances et build

- `net9.0`, `LangVersion 12`, `Nullable enable` (`STS2_MCP.csproj:3-6`).
- Références : `sts2.dll`, `GodotSharp.dll`, `0Harmony.dll` avec `<Private>false</Private>` (`csproj:17-28`) → le DLL du mod ne embarque pas le jeu.
- `STS2GameDir` par défaut = chemin **Windows** `D:\SteamLibrary\...` (`csproj:8`) → sur Fedora, passer `-p:STS2GameDir=...`.
- `build.ps1` est **Windows only** (hardcode `data_sts2_windows_x86_64` l.45) ; utiliser `dotnet build` directement (`README.md:162-164`).
- Manifest : `id` doit correspondre aux noms de fichiers (`STS2_MCP.dll` + `STS2_MCP.json`), sinon rien ne charge (`README.md:178-179`).
- Côté MCP : Python 3.11+, `uv`, `httpx`, `mcp.server.fastmcp` (`mcp/pyproject.toml`, `mcp/server.py:13`).

### 10.4 Pièges de lecture (spécifiques au code)

1. **`draw_pile` n'est pas l'ordre de pioche** : tri par `Rarity` puis `Id.Entry` (`StateBuilder.cs:1133-1136`). Inutilisable pour prédire la prochaine carte.
2. **Les cartes de pile n'ont pas d'`id`** (`BuildPileCardList` l.1309-1315) : impossible de dédupliquer ou d'indexer par id sans passer par `BuildCardInfo`.
3. **Pas de deck hors combat** : tout le bloc piles/main est sous `CombatManager.Instance.IsInProgress` (l.1106). Sur la map, `player` ne contient que HP/or/reliques/potions/statuts.
4. **`entity_id` instable** : `{baseId}_{n}` avec un compteur local qui ne compte que les vivants (l.1326-1329). Dès qu'un ennemi meurt, les ids se décalent. `ResolveTarget` reconstruit la même convention (Actions l.1063-1075) — donc les deux se décalent ensemble, mais un id mémorisé devient faux.
5. **`combat_id` (`creature.CombatId`) est la clé stable** (l.1334) — préférer celle-ci.
6. **`Energy`/`MaxEnergy` null-ref hors combat** (commentaire l.1103-1105) : ne jamais les lire sans `CombatManager.Instance.IsInProgress`.
7. **La description dépend de la pile** : `SafeGetCardDescription(card, pile)` utilise `card.GetDescriptionForPile(pile)` (`Helpers.cs:48-52`). `BuildCardInfo` passe `PileType.None` par défaut (l.1241), `BuildCardState` repasse en `Hand` (l.1263), les piles passent `Draw`/`Discard`/`Exhaust`. Les nombres affichés peuvent donc différer d'un contexte à l'autre.
8. **Exceptions avalées partout** : `catch { }` (intent l.1367, pouvoirs l.2395, boss l.1756, epoch l.276, `SafeGetText` Helpers l.95). Un champ manquant peut vouloir dire « pas de donnée » **ou** « exception ». Pour ULTIMA : logger.
9. **`FindAllSortedByPosition` est obligatoire** pour les listes de cartes : `NGridCardHolder.OnFocus()` appelle `MoveToFront()`, ce qui casse l'ordre des enfants (commentaire `Helpers.cs:211-216`). Trier par `GlobalPosition.Y` puis `.X`.
10. **Toujours vérifier `IsLiveNode`** avant de toucher un node : `GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion()` (`Helpers.cs:308-318`), sinon `ObjectDisposedException` pendant les transitions de salle.
11. **Marshalling obligatoire** : toute lecture de modèle doit passer par `RunOnMainThread` (`McpMod.cs:138-158`). Appeler `BuildGameState()` depuis le main thread **deadlock** (la lambda serait mise en file et attendue de façon bloquante sur ce même thread).
12. **Cap de 10 actions/frame** (`McpMod.cs:130`) : si plusieurs requêtes arrivent, la latence monte ; le polling agressif sature.
13. **`GetAwaiter().GetResult()` sur le thread HTTP** (`McpMod.cs:297-298`, `:391-392`) : pas de timeout, une requête peut bloquer indéfiniment si le main thread est occupé (chargement de scène).
14. **Pas d'arrêt propre** : aucun `Stop()`, aucun hook de quit (grep). Le port reste occupé tant que le process vit ; un redémarrage rapide du jeu peut laisser un `TIME_WAIT`.
15. **Sécurité** : pas d'auth, CORS `*` (`McpMod.cs:181-183`), écoute sur `localhost` **et** `127.0.0.1`. N'importe quel processus local — et n'importe quelle page web ouverte dans un navigateur (GET simple, CORS permissif) — peut lire l'état, et **POST** des actions. Pour ULTIMA : soit ne pas exposer de HTTP, soit ajouter un token + restreindre les origines.
16. **`affects_gameplay: false` est discutable** : le manifest l'affirme (`mod_manifest.json:9`) alors que le mod envoie `PlayCardAction`, `EndPlayerTurnAction`, achète, change de seed et écrit `PrefsSave.FastMode`. Utile à savoir si ULTIMA veut un manifest honnête.
17. **Noms de champs privés fragiles** : `_backButton`, `_unreadyButton`, `_runLobby`, `_verticalPopup`… changent à chaque refactor du jeu ; `%UniqueName` est plus stable. Le HEAD a d'ailleurs un commit entier consacré à la compatibilité v0.107.
18. **`LoadRunLobby` n'expose plus `IsAboutToBeginGame`** : le mod recalcule la readiness à la main (`StateBuilder.cs:975-990`) — signe que l'API bouge vite.
19. **Markdown = code mort pour ULTIMA** : 1116 lignes de `Formatting.cs` à ignorer (sauf si tu veux un rendu texte pour l'overlay).
20. **Le pont MCP (`mcp/server.py`) n'apporte rien** à ULTIMA : ce sont des wrappers HTTP 1:1, sans logique.

---

## Top 10 des choses à reprendre (classées par gain de temps)

1. **`BuildEnemyState` + `BuildPlayerState` + `BuildPowersState`** (`StateBuilder.cs:1320`, `:1092`, `:2355`) — le modèle de combat complet (HP, block, statuts, intents, orbes, pets) est déjà écrit et testé en jeu. C'est ~60 % du travail de lecture de F1/F2. À copier en retirant les trois mutations cachées.
2. **`BuildMapState` + `BuildMapNode`** (`StateBuilder.cs:1673`, `:1807`) — le DAG de la map avec `children` en `[col,row]`, la position courante, les nœuds `Travelable` et l'identité du boss : F6 devient un simple calcul sur ce graphe.
3. **`RunOnMainThread` + `ProcessMainThreadQueue`** (`McpMod.cs:127-158`, connexion `ProcessFrame` l.89-90) — le seul moyen correct de lire l'état depuis un thread non-Godot. Sans ça, crashs aléatoires ou deadlock.
4. **`SafeGetText` + `StripRichTextTags` + `SafeGetCardDescription`** (`Helpers.cs:84`, `:98`, `:48`) — gestion des `LocString` (`GetFormattedText()`), du BBCode `[color=…]`/`[img]…[/img]` et des retours à la ligne. À reprendre tel quel.
5. **`GetInstanceFieldValue`** (`Helpers.cs:163-179`) — 15 lignes qui remplacent des heures de lutte avec les champs privés Godot (pattern `DeclaredOnly` + remontée de `BaseType`).
6. **`FindAll` / `FindFirst` / `IsLiveNode` / `IsNodeVisible` / `FindAllSortedByPosition`** (`Helpers.cs:203`, `:284`, `:308`, `:320`, `:217`) — traversée de l'arbre Godot qui survit aux transitions de salle et au `MoveToFront()`.
7. **`card.CanPlay(out reason, out _)` + `UnplayableReason`** (`StateBuilder.cs:1259-1266`) — le seul moyen fiable de savoir si une carte est jouable *maintenant* (coût, cible, conditions). Pur read, aucune mutation.
8. **La lecture multi** : `NetService.Type.IsMultiplayer()` (`McpMod.cs:285-286`), `BuildAllPlayersState` (`MultiplayerState.cs:413`), `IsPlayerReadyToEndTurn` / `AllPlayersReadyToEndTurn` (l.431, 273) — tout ce qu'il faut pour F3 côté « qui est là / qui est prêt », sans jamais voter.
9. **Les stats de profil + run history** (`Compendium.cs:41-103`, `:302-358`) — base directe pour F5 : `CardStats` (picked/skipped/won/lost), `CharacterStats`, totaux globaux, et lecture disque des `.run` (deck/relic/potion counts, cause de mort, seed).
10. **Le label d'intent + hover tip comme oracle de test** (`StateBuilder.cs:1357-1364`) — pas comme donnée : comme **vérification**. Si ton calcul F1 ne reproduit pas le `label` du jeu pour l'état courant, ton pipeline de dégâts est faux. C'est le test de non-régression gratuit.

**À ne surtout pas reprendre** : `McpMod.Actions.cs` (1893 lignes), `McpMod.MultiplayerActions.cs`, `ExecuteMenuSelect` (580 lignes de navigation de menus), `Formatting.cs` (1116 lignes de Markdown), `Profile.cs`/`Wiki.cs`, et les 5 patches Harmony de `SettingsUI.cs` (qui écrivent dans `PrefsSave`).
