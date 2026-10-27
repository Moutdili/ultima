# Overlays, notes de cartes et templates — minage des dépôts de référence

Portée : `/home/mio/sts2/research/repos/{sts2-advisor, sts2-tierlist-overlay, STS2-ViewedCardsStatistics, spirescope, ModTemplate-StS2, Modding-Tutorial, BaseLib-StS2}`.
Lecture seule (aucun fichier des dépôts modifié). Toutes les affirmations sont citées `fichier:ligne` ; `ABSENT` quand l'info n'existe pas dans le dépôt. Jeu de référence : v0.107.1.

Résumé des trois architectures observées, parce qu'elles ne se valent pas pour ULTIMA :

| Dépôt | Mécanisme d'accroche | Overlay par carte | Dépendances |
|---|---|---|---|
| sts2-advisor | Harmony postfix sur méthodes de screens + panneau latéral CanvasLayer | badge = enfant du nœud carte | `sts2.dll`, `0Harmony`, Newtonsoft, SQLite |
| sts2-tierlist-overlay | **zéro Harmony** : `SceneTree.NodeAdded` + `ProcessFrame`, resync chaque frame | badge = enfant du nœud item | `sts2.dll`, `GodotSharp` seulement |
| STS2-ViewedCardsStatistics | RitsuLib `ModPatcher` (postfix déclaratifs) | non (tooltips compendium) | `sts2.dll`, `0Harmony`, **STS2-RitsuLib** |

---

## 1. sts2-advisor

### 1.1 Accroche exacte (hook) et affichage d'une note/grade à côté d'une carte

Deux mécanismes cohabitent : un **panneau latéral** (info riche) et des **badges posés sur les cartes** (grade seul).

Initialisation : `[ModInitializer("Init")]` (`QuestceSpire/Plugin.cs:15`), puis `_harmony.PatchAll(typeof(GamePatches).Assembly)` **et** `GamePatches.ApplyManualPatches(_harmony)` (`Plugin.cs:127-129`). Les patches réels sont posés à la main, pas par attributs (`GamePatches.cs:744-799`) :

```
PatchMethod(harmony, typeof(NCardRewardSelectionScreen), "_Ready",         nameof(OnCardRewardScreenReady));  // :748
PatchMethod(harmony, typeof(NCardRewardSelectionScreen), "ShowScreen",     nameof(OnCardRewardOpened));        // :749
PatchMethod(harmony, typeof(NCardRewardSelectionScreen), "RefreshOptions", nameof(OnCardRewardRefreshed));     // :750
PatchMethod(harmony, typeof(NCardRewardSelectionScreen), "SelectCard",     nameof(OnCardSelected));           // :746
```
`PatchMethod` ne pose qu'un **postfix** (`GamePatches.cs:801-824`, `harmony.Patch(target, postfix: ...)` en `:817`). Postfix signature `OnCardRewardOpened(NCardRewardSelectionScreen __result, IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> extraOptions)` (`GamePatches.cs:79`).

Chemin complet d'un badge :
1. `OnCardRewardOpened` filtre les faux positifs : >5 cartes = viewer de pile (`:86-90`), écrans réutilisant `NCardRewardSelectionScreen` (removal/upgrade/shop/event/rest) (`:92-99`), puis mémorise l'écran dans une `WeakReference` et un flag « genuine reward » (`:101-102`).
2. `TryShowCardRewardFromScreen` lit l'état et appelle `Plugin.SynergyScorer.ScoreOfferings(...)` puis `Plugin.Overlay.InjectCardGrades(screen, cards)` (`GamePatches.cs:224-228`).
3. `InjectCardGrades` (`QuestceSpire/UI/OverlayManager.cs:3747`) : garde `IsGenuineCardReward` (`:3753`), `scoredCards.Count > 5` rejeté (`:3761`), `ClearInGameBadges()`, `_badgeEpoch++`, puis `Callable.From(...InjectCardGradesDeferred...).CallDeferred()` (`:3766-3770`).
4. `InjectCardGradesDeferred` (`:3778`) retrouve les holders via `FindCardHolderNodes(screenNode, scoredCards.Count)` (`:3794`) et apparie **par index** : `AttachGradeBadge(cardHolders[i], scoredCards[i].FinalGrade, scoredCards[i].IsBestPick, scoredCards[i].FinalScore)` (`:3805-3808`).
5. `FindCardHolderNodes` cherche les nœuds dont le **nom de type** est `NGridCardHolder` (`:3921-3926`), puis prend l'enfant dont le type contient `Hitbox`, « it has the actual size » (`:3929-3938`) — arbre décrit en commentaire : `CardRow > NGridCardHolder > NCardHolderHitbox (300x422)` (`:3924`).

Contenu du badge (`AttachGradeBadge`, `OverlayManager.cs:4009-4054`) :
- sous-grade textuel : `TierEngine.ScoreToSubGrade(score)` → « S+ », « A- », « B+ »… seuils `TierEngine.cs:237-254` ;
- `PanelContainer` avec `CustomMinimumSize = (30|38, 30)` (`:4020`), `StyleBoxFlat` coloré par `TierBadge.GetGodotColor(grade)` (`TierBadge.cs:8-20`), coins 0/10/10/0 (`:4024-4027`), bordure et ombre renforcées si `isBestPick` (`:4028-4034`) ;
- `ZIndex = 10` et `MouseFilter = Ignore` (`:4046-4047`) ;
- ajouté **comme enfant du nœud cible** : `targetNode.AddChild(badge, ...)` (`:4050`), suivi dans `_inGameBadges` avec `WeakReference<Control>` (`:52`, `:4051`).

Positionnement : `PositionBadgeInParent` — centré horizontalement, **4 px au-dessus du bas du parent**, en coordonnées locales (`OverlayManager.cs:4060-4070`) :
```
badge.Position = new Vector2((parentW - badgeW) / 2f, parentH - badgeH - 4f);   // :4069
```

Le détail textuel (synergies, notes) n'est **pas** dans le badge : il est dans le panneau latéral, une ligne par carte avec `CreateBadge(card.FinalGrade, card.FinalScore)` (`:2086-2090`), un one-liner (`:2182`) et un tooltip qui inclut `card.Notes` (`:2219`).

Panneau : `CanvasLayer` propre, `_layer.Layer = 100` (`:441-442`), panneau ancré à droite (`AnchorLeft/Right = 1`, `:444-447`), offsets persistés (`:449-454`), ajouté différé à la racine de la scène : `sceneTree.Root.CallDeferred("add_child", _layer)` (`:574`).

### 1.2 Cycle de vie des badges

- Nettoyage **avant chaque** injection : `Plugin.Overlay?.CleanupAllBadges()` à l'ouverture d'un reward (`GamePatches.cs:84`), `ClearInGameBadges()` en tête de `InjectCardGrades` (`OverlayManager.cs:3766`) et de la version différée (`:3790`).
- Invalidation des appels différés obsolètes par époque : `_badgeEpoch++` / `if (epoch != _badgeEpoch) return;` (`:3767-3768`, `:3783`).
- Vérification de contexte ~1×/s : `OverlayInputHandler._Process` appelle `CheckForStaleScreen()` toutes les 1,0 s (`UI/OverlayInputHandler.cs:48-53`), qui appelle `UpdateInGameBadgePositions()` (`OverlayManager.cs:632-649`).
- `UpdateInGameBadgePositions` (`:4106-4170`) : si le nœud écran n'est plus valide/visible → tout nettoyer (`:4113-4119`) ; si le nombre de `NGridCardHolder` dépasse `_badgeExpectedHolderCount + 1` (une pile s'est ouverte) → tout nettoyer (`:4121-4128`) ; sinon repositionne (`:4148-4149`) et purge les entrées mortes (`:4152-4169`).
- Fin de vie sur sélection : `OnCardSelected` / `OnRelicSelected` remettent les flags à zéro et appellent `Plugin.Overlay?.Clear()` (`GamePatches.cs:867-873`, `:903-909`).
- Les badges de reliques ont été **retirés** : « Relic badge injection removed — overlay panel handles relic screens » (`GamePatches.cs:287-288`).

### 1.3 Stockage des données de tiers (format)

Répertoire `Data/` à côté de la DLL (`Plugin.cs:85-86`), chargé par `TierEngine` (`Core/TierEngine.cs:20-25`).

Cartes — `Data/CardTiers/<personnage>.json` (ironclad, silent, regent, necrobinder, defect, colorless) :
```json
{ "character": "ironclad",
  "source": "https://www.youtube.com/watch?v=GFc8Bh2RYvI",
  "cards": [ { "id": "Strike Ironclad", "baseTier": "D",
               "synergies": ["damage"], "antiSynergies": [],
               "notes": "Starter strike, replace when possible" } ] }
```
(`Data/CardTiers/ironclad.json:1-10`). Modèle : `CardTierEntry{id, baseTier, synergies, antiSynergies, notes}` (`Core/CardTierEntry.cs:8-21`), `CharacterCardTiers{character, cards}` (`Core/CharacterCardTiers.cs:8-12`). Index par personnage puis id normalisé, insensible à la casse (`Core/TierEngine.cs:42-55`).

Reliques — `Data/RelicTiers/<catégorie>.json` : `{ "category": "common", "relics": [ { "id": "Anchor", "baseTier": "A", "synergies": ["block"], "antiSynergies": [], "notes": "Common. +10 block first turn" } ] }` (`Data/RelicTiers/common.json:1-19`, modèle `Core/RelicTierFile.cs:8-12`).

Potions — `Data/PotionTiers/<catégorie>.json`, même forme avec `"potions"` et sans `antiSynergies` (`Data/PotionTiers/common.json:1-13`).

Autres jeux de données : `Data/CardProperties/*.tsv` (`all_cards_full.tsv`, `card_dynamic_vars.tsv`), `Data/EnemyTips/enemies.json`, `Data/EventAdvice/events.json` (arborescence du dépôt).

### 1.4 « Local learning » : fichiers et format

Trois étages, tous locaux :

1. **Journal de runs (écriture)** — SQLite `questcespire.db` dans le dossier du mod : `Path.Combine(_pluginFolder, "questcespire.db")` (`Tracking/RunDatabase.cs:47`). Tables créées en `:53` : `runs(run_id, player_id, character, seed, start_time, end_time, outcome, final_floor, final_act, ascension_level, synced)`, `decisions(id, run_id, floor, act, event_type, offered_ids, chosen_id, deck_snapshot, relic_snapshot, current_hp, max_hp, gold, timestamp)`, `community_card_stats`, `community_relic_stats` (clé `(card_id|relic_id, character)`), index `idx_decisions_run_id`, `idx_runs_synced`. `offered_ids`/`deck_snapshot`/`relic_snapshot` sont des **tableaux JSON sérialisés** stockés en TEXT (`:93-96`). Identité : `player_id.txt` (GUID) à côté de la DLL (`Tracking/RunTracker.cs:41-51`). Écriture d'un run à `EndRun` (`:206-219`).
2. **Recalcul des stats** — `LocalStatsComputer.RecomputeAll()` (`Plugin.cs:92-93`) fait `DELETE` puis `INSERT ... SELECT` avec `JOIN json_each(d.offered_ids)`, `HAVING COUNT(*) >= 3` (`Tracking/LocalStatsComputer.cs:51-79` cartes, `:99-127` reliques), puis une passe « contexte d'archétype » (`:139`).
3. **Import de l'historique du jeu (lecture)** — `GameDataImporter.ImportAll()` (`Plugin.cs:94`, `:111`) lit `%APPDATA%/SlayTheSpire2/steam/<steamid>/profile*/saves/history/*.run` (`Tracking/GameDataImporter.cs:32-39`, résolution `:263-286`) et parse `win`, `was_abandoned`, `players[0].character`, `map_point_history[].player_stats[].card_choices[].{card.id, was_picked}` et `relic_choices[].{choice, was_picked}` (`:58-107`), puis agrège en `CommunityCardStats`/`CommunityRelicStats` (`:122-140`).

`AdaptiveScorer` consomme ces tables pour ajuster les scores (`Core/AdaptiveScorer.cs:19`, `:24`, `:52`).

**Piège Linux (important pour ULTIMA)** : `FindSaveDirectory()` part de `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` (`GameDataImporter.cs:266`). Sous Linux, ce chemin résout vers `~/.config`, alors que le jeu écrit sous `~/.local/share/SlayTheSpire2` (cf. `spirescope/sts2/config.py:171-174`). L'import de l'historique est donc très probablement inopérant sur Fedora — INFÉRENCE sur la valeur exacte retournée par .NET, mais le code ne teste aucun chemin Linux et aucun `XDG_DATA_HOME`.

Config du mod : `overlay_settings.json` à côté de la DLL, Newtonsoft, avec `SettingsVersion` et migrations (`Core/OverlaySettings.cs:29-35`, `:37-76`) ; sauvegarde à chaque changement (`:78-90`). Champs : offsets du panneau, visibilité, tooltips, badges, opacité, collapsed, sections, `CloudSyncEnabled` (`:9-30`).

### 1.5 Points faibles constatés (à ne pas copier)

- Manifeste au **format hérité** : `Data/mod_manifest.json` avec `hasPck`/`hasDll`/`affectsGameplay` en camelCase (`Data/mod_manifest.json:1-11`), alors que le contrat loader actuel est `<id>.json` avec `has_pck`/`has_dll` (`Modding-Tutorial/docs/03-mod-loading-and-manifests.md:65-97`).
- `hasPck: true` mais **aucun** `project.godot`, `export_presets.cfg` ni `.pck` dans le dépôt (recherche `find` : 0 résultat) → la chaîne de packaging est incomplète.
- `QuestceSpire.csproj` référence des DLL via `..\lib\` en chemins **Windows** et ne cible pas le SDK Godot (`QuestceSpire.csproj:14-29`) ; `SETUP_GUIDE.md:8` parle d'injection par `DOTNET_STARTUP_HOOKS`, ce qui ne correspond plus au loader v0.107.x.
- Écrit des `.json` de config **dans** `mods/<Mod>/` (voir §9 : tout `.json` sous `mods/` est désérialisé comme manifeste).

---

## 2. sts2-tierlist-overlay

### 2.1 Accroche : pas de Harmony du tout

Commentaire d'en-tête : « Harmony does not work on macOS arm64, so instead of patching game methods this watches the scene tree for item nodes and keeps their badges in sync with what they show » (`TierListOverlayMod.cs:28-33`). Aucune occurrence de `[HarmonyPatch]`/`Harmony` dans le dépôt (grep global : 0).

À la place, `[ModInitializer("Initialize")]` (`:34`) et deux signaux :
```
tree.NodeAdded    += OnNodeAdded;      // :62
tree.ProcessFrame += OnProcessFrame;   // :63
```
`OnNodeAdded` ne fait qu'**enregistrer** les nœuds d'intérêt dans un `HashSet<Control> Tracked` : `NCard or NRelic or NPotion or NRewardButton or NEventOptionButton or NCardRewardAlternativeButton or NMerchantCardRemoval` (`:67-74`), et passe la main aux modules `Tips`, `SettingsUi`, `Hint` (`:71-73`).

`OnProcessFrame` (`:76-111`) : purge les nœuds sortis de l'arbre (`:86-87`), résout le personnage courant (`:88`), puis pour chaque nœud : `Describe(node)` → `Content(...)` → `Badges.Sync(node, content, bounds, placement)` (`:96-100`). Les nœuds étant **poolés et réutilisés**, la synchro est refaite à chaque frame avec une clé de contenu pour éviter les reconstructions (`Badges.cs:113-127`).

### 2.2 Superposition d'une info sur cartes / reliques / potions

`Describe` (`TierListOverlayMod.cs:292-327`) est la table de correspondance nœud → (itemId, mode, placement, rect) :

| Nœud | Placement | Source |
|---|---|---|
| `NCard` (écran de choix) | `InsideBottom`, `CardBounds = (-defaultSize/2, defaultSize)` car `NCard` est centré sans taille propre | `:287-304` |
| `NCardRewardAlternativeButton` (Skip) | `AboveTop` | `:307-308` |
| `NMerchantCardRemoval` | `AcrossBottom`, rect = `Hitbox` | `:310-312` |
| `NRelic` sous `NMerchantInventory` / `NTreasureRoomRelicCollection` / `NChooseARelicSelection` | `AcrossBottom` | `:316-317` |
| `NPotion` sous `NMerchantInventory` | `AcrossBottom` | `:318` |
| `NRewardButton { Reward: RelicReward }` / `PotionReward` | `RightEdge` | `:320-321` |
| `NEventOptionButton { Option.Relic }` (anciens) | `TopCentre` | `:323` |
| `NEventOptionButton` (autres events) | `RightEdge` ou `EventTop` selon la largeur du texte | `:324`, `:333-344` |

Enum `Placement` : `InsideBottom`, `AcrossBottom`, `RightEdge`, `TopCentre`, `EventTop`, `AboveTop` (`Badges.cs:7-22`). Géométrie dans `Place` (`Badges.cs:260-287`) — constantes **mesurées sur les textures** : bord intérieur de carte à `28.6` unités sur 422 (`:271-274`), bord de boîte d'ancien à `8.5%` (`:278`), boîte d'event à `11.7%` (`:281`), bouton Skip à `14%` (`:284`), badge à droite à `-16 px` (`:290`). Les icônes de relique/potion sont dessinées à des échelles très différentes → `badge.Scale = HoverZoom(item) / item.GetGlobalTransform().Scale` (`:265-266`), avec `ShopSlotScale = 0.65f` (`:248`).

Rendu : `PanelContainer` + `Draw` callback dessinant un hexagone (rim + dégradé) derrière le texte (`Badges.cs:184-223`), **pas de ZIndex** : « the badge must stay in the item's own draw order so hover tips cover it » (`:136`), `MouseFilter = Ignore` (`:190`). Police = celle du titre de la carte, récupérée via `FindChild("TitleLabel")` (`:161-163`). Métadonnées `item_id`, `tier`, `kind`, `label`, `key` posées sur le badge pour l'outillage (`:192-196`).

Place à la marchandise : `MakeRoomForBadge` décale le nœud `Cost` du slot marchand vers le bas quand un badge existe (`TierListOverlayMod.cs:379-400`).

Tooltips : `Tips` s'abonne aussi à `NHoverTipSet` via `NodeAdded` (`Tips.cs:44-47`), lit en réflexion `_owner` et `_textHoverTipContainer` (`:32-33`), et ajoute des tips maison dans le container, puis force `CorrectVerticalOverflow`/`CorrectHorizontalOverflow` (`:88-91`). Commentaire clé : le tip set est ajouté avant que le jeu ne lui donne son owner → traitement à la frame suivante (`:48-50`).

### 2.3 Données de tiers (format + cache)

Source réseau : API publique Untapped.gg, `https://api.sts2.untapped.gg/api/v1/tier-lists/{0}/resolve/?game_version={1}` (`TierLists.cs:47`), 12 UUID codés en dur (2 auteurs × 6 listes) (`:29-43`). `TierEntry` = `record (Tier, Color, Commentary, Author, GoodUpgrade, MaxCopies, Kind)` + `RatedFor` (`:16-20`), parsé depuis le JSON de l'API (`:220-239` : `author.display_name`, groupes `cards`/`relics`/`potions`, `good_upgrade`, `max_copies`).

Cache disque : `cache/<author>-<name>.tierlist` (`:126-127`), stamp `cache/fetched.cfg` (`:150`), refresh si > 6 h ou version de jeu différente (`:51`, `:155`). Chargement cache immédiat puis fetch en tâche de fond (`:133-160`). `Generation` sert de compteur pour ne vérifier les IDs qu'une fois (`:59-62`, `TierListOverlayMod.cs:84-85`).

### 2.4 Config et « local learning »

Config : **`settings.cfg`** à côté de la DLL (`TierListOverlayMod.cs:50`), via `Godot.ConfigFile`, section `tierlist` (`Settings.cs:33`), version `3` avec migrations `FromVersion1/2` (`:35`, `:85-99`), clés `badges`, `cards`, `relics`, `potions`, `events`, `deck`, `author`, `commentary`, `stats_tips`, `stats_samples`, `event_advice` (`:38-56`), défauts en dictionnaire (`:57-70`), `Get`/`Set`+`Save` (`:136-152`). Login Untapped : `account.cfg` (tokens access/refresh + paire « compte ») (`Account.cs:21-22`, `:113-131`).

Stats personnelles : lecture **pure** de l'historique du jeu — `profile{N}/saves/history` **et** `modded/profile{N}/saves/history` (`History.cs:163-164`), fichiers `*.run` (`:172`), re-scan toutes les 3 s et seuls les nouveaux fichiers sont lus (`:48`, `:40-43`), runs comptés à partir de l'Ascension 7 (`:47`). Agrégat par personnage `Dictionary<string, Tally>` (`:60`, `:191`). Aucune écriture : « Only reads: never writes, moves or renames anything there » (`:38`). Dossier `test-history/` à côté de la DLL pour les tests (`:87`).

Stats globales : `Stats.cs` — `StatEntry(ActionPct, ActionTier, ActDelta, ActTier, RunDelta, RunTier, TimesOffered, AllCharacters)` (`Stats.cs:30-37`), réponse par groupes `cards/relics/potions/skip/remove` (`:46-53`), cache 30 min (`:58-60`).

Config UI : le mod **ajoute un onglet** dans l'écran de réglages du jeu en dupliquant le panneau « General » et les lignes existantes, puis en insérant l'onglet après `Input` (`SettingsUi.cs:176-245`), avec réflexion sur `NSettingsTabManager._tabs`, `SwitchTabTo`, `NPaginator._options/_currentIndex/_label` (`:112-116`).

---

## 3. STS2-ViewedCardsStatistics

### 3.1 Comment il compte les cartes « vues »

Ce n'est **pas** un hook de consultation en direct : tout vient de l'**import de l'historique de runs** via l'API du jeu.
- `ImportFromRunHistory()` : `SaveManager.Instance.GetAllRunHistoryNames()` (`Data/StatisticsManager.cs:50`), puis `SaveManager.Instance.LoadRunHistory(fileName)` (`:57`), puis `ProcessRunHistory(...)` (`:59`), enfin `Save()` (`:67`).
- Au fil des `MapPointHistoryEntry` : `ancientChoices` → `RecordSeen` + `RecordPicked` si `WasChosen` (`:208-218`) ; `CardChoices` → `RecordSeen(cardId)` systématique et `RecordPicked` si `wasPicked` (`:220-235`) ; `CardsGained` → comptés vus+pris sauf s'ils correspondent déjà à un choix (`:237-254`) ; `RelicChoices` (`:256-269`) ; `PotionChoices` (`:271-284`) ; `CardsTransformed` (`:286-301`).
- Déduplication par run : `runId = seed_startTime_character` puis `Data.ProcessedRuns.Add(runId)` (`:106`, `:154`), persisté dans le JSON (`Data/Models/ViewedStatisticsData.cs:24`).
- Rafraîchissement à chaud : postfix sur `SaveManager.SaveRunHistory(RunHistory)` → `ProcessAndSaveRunHistory` (`Patches/SaveDataPatch.cs:20-29`). Patch via `ModPatcher` RitsuLib (`Main.cs:27-30`, `:53-63`).

Stockage : RitsuLib `ModDataStore` — `settings` en portée **Global** (`settings.json`) et `statistics` en portée **Profile** (`viewed_statistics.json`), avec `data_version`/migration (`Data/ModDataStore.cs:22-45`, `Const.cs:7-8`). Schéma : `card_stats`/`relic_stats`/`potion_stats`/`monster_stats` = `{ <characterId>: { seen:{id:count}, picked:{id:count}, total_seen, total_picked } }` + `processed_runs` (`Data/Models/ViewedStatisticsData.cs:13-24`, `:76-100`).

### 3.2 Affichage dans les tooltips du compendium

| Écran | Cible du patch | Rendu |
|---|---|---|
| Bibliothèque de cartes, tooltip | `NCardHolder.CreateHoverTips` (postfix) | `HoverTipHelper.AddTipToOwner(__instance, "Viewed", statsText)` — seulement si le parent est `NCardLibraryGrid { ShowStats: true }` et si la carte est `ModelVisibility.Visible` (`Patches/CardHoverTipStatsPatch.cs:23`, `:34-46`) |
| Bibliothèque de cartes, panneau de stats | `NCardLibraryStats.UpdateStats(CardModel)` (postfix) | concatène du BBCode à `__instance._label.Text` : `Picked: a/b (x%)` + `Seen: c/d (y%)` (`Patches/CardLibraryStatsPatch.cs:20`, `:42-46`) |
| Collection de reliques | `NRelicCollectionEntry.OnFocus` (postfix) | tip « Viewed » (`Patches/RelicCollectionStatsPatch.cs:21`, `:36-40`) |
| Potion lab | `NLabPotionHolder.OnFocus` (postfix) | tip « Viewed » (`Patches/PotionLabStatsPatch.cs:21`, `:34-38`) |
| Menu principal | `NMainMenu._Ready` (postfix) | popup de premier lancement (`Patches/MainMenuShowPopupPatch.cs:28`, `:45`) |

Le texte détaillé est construit par `GetItemDetailedStatsText` (pourcentages par personnage, `StatisticsManager.cs:499-528`).

### 3.3 Commandes console

`vcs <reimport|verbose|clear>` — classe `ViewedStatsConsoleCmd : AbstractConsoleCmd`, `CmdName => "vcs"` (`Console/ViewedStatsConsoleCmd.cs:8-14`) :
- `reimport` → `StatisticsManager.ClearAndReimportFromRunHistory()` (`:34-38`, cible `StatisticsManager.cs:71`) ;
- `verbose [on|off]` → bascule `VerboseImportLogging` (`:40-53`) ;
- `clear` → `CreateEmptyData()` (`:55-59`).
Complétion des sous-commandes et de `on|off` (`:61-68`). `IsNetworked = false`, `DebugOnly = false` (`:15-16`).

Enregistrement de la commande : **ABSENT** du dépôt (aucun appel d'enregistrement, `grep` ne trouve que la déclaration) — présumément découverte automatique par RitsuLib (INFÉRENCE).

---

## 4. spirescope

### 4.1 Sauvegardes du jeu lues : chemins, format, champs

Résolution des répertoires (`sts2/config.py:145-223`) :
- override `STS2_SAVE_DIR`, liste séparée par `os.pathsep` (`:162-164`) ;
- Windows `%APPDATA%\SlayTheSpire2` (`:167-168`) ; macOS `~/Library/Application Support/SlayTheSpire2` (`:169-170`) ; Linux `$XDG_DATA_HOME` sinon `~/.local/share` + `SlayTheSpire2` (`:171-174`) ;
- Proton (Steam Deck) : chemin codé en dur `~/.local/share/Steam/steamapps/compatdata/2868840/pfx/drive_c/users/steamuser/AppData/Local/SlayTheSpire2`, utilisé **seulement s'il existe** (`:176-184`) ;
- puis `steam/<steamid>/profile*/saves` et `steam/<steamid>/modded/profile*/saves`, regroupés par (id, profil), arbre le plus frais d'abord, un seul profil retenu (`:189-223`, exposé `:302-303`).

Fichiers ouverts (aucun autre) :

| Fichier | Rôle | Ligne |
|---|---|---|
| `current_run.save`, `current_run_mp.save` | run en cours, le plus récent gagne | `sts2/saves.py:157` |
| `current_run.save.backup`, `current_run_mp.save.backup` | secours si pas de run vivant et run non terminé | `:179-193` |
| `history/*.run` | runs terminés | `:436-439` |
| `progress.save` | progression | `:295` |
| `.../SlayTheSpire2/logs/godot.log` | télémétrie live (hors save) | `sts2/logparser.py:10-27` |

Format : **JSON texte UTF-8**, objet racine obligatoire, `json.load`, taille plafonnée à 20 Mo (`sts2/saves.py:13`, `:31-53`). Aucun gzip/msgpack/`.tres` (grep : 0).

Champs extraits de `current_run*.save` : `players[]` (`:213`) avec `id`, `character_id`/`character`, `current_hp`/`max_hp`/`gold`, `deck[].id`, `deck[].current_upgrade_level`→`upgrade_count`, `deck[].enchantment.id`, `deck[].props`, `relics[].id`, `potions[].id` (`:218-230`, `:271-280`, `:104-107`) ; au niveau run : `ascension`, `current_act_index`, `run_time`, `events_seen`, `rng.seed`→`seed`, `start_time`, `save_time` (`:261-289`, `:133-138`, `:187`) ; `map_point_history[][]` → `map_point_type`, `rooms[0].{room_type, model_id, monster_ids[], turns_taken}`, `player_stats[].{player_id, damage_taken, hp_healed, current_hp, max_hp, current_gold, card_choices[].{was_picked, card.id}}` (`:235-255`, `:92-101`).

`history/*.run` : `win`, `ascension`, `seed`, `acts`, `killed_by_encounter`, `run_time`, `build_id`, `start_time` (sinon nom de fichier), `players[].{character, deck, relics}`, `map_point_history` + `potion_used`, `potion_choices[].{choice, was_picked}` (`sts2/saves.py:520-552`, `:471-525`).

`progress.save` : `total_playtime`, `character_stats[]`, `badges[]`, `card_stats[]`, `encounter_stats[]`, `enemy_stats[]`, `discovered_*`, `epochs[]` (`sts2/saves.py:315-395`).

Modèles pydantic correspondants : `RunFloor` (`sts2/models.py:167`), `RunHistory` (`:210`), `CurrentRun` (`:238-273`, champs `active, character, ascension, current_hp, max_hp, gold, act, floor, run_time, deck, deck_upgrades, deck_properties, deck_enchantments, relics, potions, events_seen, encounters_won, floors, player_index, total_players, player_id, seed, start_time, telemetry_status, cards_played, extra_turns, elites_defeated`), `PlayerProgress` (`:276`).

Aucune sauvegarde réelle n'est versionnée ; la seule forme d'exemple est la fixture synthétique `tests/test_saves.py:7-47`.

### 4.2 Service de l'UI : SSE sur FastAPI

FastAPI (`sts2/app.py:97`), uvicorn (`sts2/__main__.py`), **pas de WebSocket** (grep `websocket` : 0).
- `GET /api/live/stream?player=N` (`sts2/routes.py:1986-1987`) → `StreamingResponse(media_type="text/event-stream")` avec `Cache-Control: no-cache` et `X-Accel-Buffering: no` (`:2041-2043`).
- Messages : `data: <json>\n\n` (`:2023`) ; événements `event: error` + `{"reason":"too_many"}` (`:2004`) et `event: timeout` (`:2025`). Pas de `id:` ni `retry:`.
- Payload = `CurrentRun.model_dump()` + `revision` + `danger{level,hp_pct}` + `ghost{info,splits}` (`:320-339`).
- Émission seulement si le hash SHA-1 de l'état change (`:2009-2023`) ; réveil sur `_live_data_event` avec **timeout 3 s** = polling de repli (`:2027-2032`).
- Limites : 10 connexions, 3 par IP, inactivité 300 s (`:1979-1981`), 429 sinon (`:1994-1996`).
- Front : `new EventSource('/api/live/stream?player=' + player)` (`sts2/static/live.js:96`, `sts2/static/overlay.js:23`) ; reconnexion backoff et gestion de `timeout` (`live.js:26-29`, `:145-154`).
- Variante non stream : `GET /api/live` (`:1971-1974`).

### 4.3 Réutilisable pour un export de run

Découplé de l'UI (importable tel quel) :
- `sts2/saves.py` : `get_run_history()` (`:424-559`, fusion vanilla/modded, dédoublonnage SHA-256, tri newest-first), `_read_history_files()` (`:400-421`, 4 threads), `get_current_run()` (`:141-208`), `get_progress()` (`:293-312`), helpers `local_player_index` (`:56`), `_get_player` (`:73`), `_read_json` (`:31`), `_current_run_seed` (`:124`).
- `sts2/config.py` : `_find_save_dirs()` (`:145-223`), `local_steam_id()` (`:248-262`).
- `sts2/models.py` : les modèles ci-dessus.
- `sts2/watcher.py` : `SaveFileHandler` — debounce **trailing** 0,5 s, filtre `.save`/`.run` (`:25-60`), `start_observer()` récursif (`:79-98`) ; couplé à `asyncio.Event` mais sans FastAPI.
- `sts2/logparser.py` : `LogTailer` incrémental par offset avec détection de rotation/troncature (`:210-270`), override `STS2_LOG_FILE` (`:33-40`).
- `sts2/persist.py` (écriture atomique temp+fsync+rename), `sts2/state_lock.py` (`flock`), `sts2/integrity.py` (`compute_run_digest`).
- Déjà un export : `GET /runs/{run_id}/export` → `{spirescope_version, format_version: 1, digest_version, integrity_digest, run}` (`sts2/routes.py:1052-1070`) ; CSV `GET /api/export/runs` (`:2174-2202`).

Couplé à l'UI : tout passe par `_app()` (`routes.py:342-351`), caches TTL 30/60 s (`app.py:302-379`), `_refresh_data()`/`_watch_saves()` (`app.py:798-900`), `_build_live_payload` (`routes.py:320-339`).

Verdict pour ULTIMA : `saves.py` + `config.py` + `models.py` sont la brique « lire un run » à reprendre ; l'export JSON existant est un bon format de référence (`format_version` + digest).

---

## 5. ModTemplate-StS2 + Modding-Tutorial : squelette, manifest, packaging, test

### 5.1 csproj

`content/ModTemplate/ModTemplate.csproj` :
- `Sdk="Godot.NET.Sdk/4.5.1"`, `InitialTargets="CheckDependencyPaths"`, import de `Sts2PathDiscovery.props` (`:1-2`) ;
- `net9.0`, `ImplicitUsings`, `Nullable`, `Publicize`, `AllowUnsafeBlocks` (`:4-8`), `GodotDisabledSourceGenerators=GodotPluginsInitializer` (`:12`) ;
- références `0Harmony` et `sts2` en `HintPath $(Sts2DataDir)/...` + `<Private>false</Private>`, sous `Condition="Exists('$(Sts2DataDir)')"` (`:21-30`) ; `GodotSharp` **non** référencé explicitement (il vient du SDK Godot) ;
- `Krafs.Publicizer` conditionné par `$(Publicize)` (`:32-35`) ; `PackageReference Alchyr.Sts2.BaseLib Version="*"` (`:38`) ; `BSchneppe.StS2.PckPacker` présent mais **commenté** (`:40-41`) ;
- exclusions d'assets (`:46-54`), `None` pour `.gitignore`, `<ModId>.json`, `project.godot` et le dossier d'assets (`:58-61`) ;
- cibles `CheckDependencyPaths` (`:64-68`), `UpdateDependencyVersions` qui réécrit la version BaseLib dans le manifest (`:70-92`), `CopyToModsFolderOnBuild` (`:94-99`), `NeedGodotForPublish` (`:107-110`), `GodotPublish` (`:112-117`).

Le tutoriel, lui, référence `GodotSharp` à la main (`Modding-Tutorial/docs/03-mod-loading-and-manifests.md:55-58`) et justifie `Private=false` : « prevents copies of game-owned assemblies from being staged beside the mod DLL » (`:68`).

### 5.2 Manifest

Nom : **`<ModId>.json` à la racine du projet**, à côté des payloads (`ModTemplate.json`). Champs :
`id`, `name`, `author`, `description`, `version`, `min_game_version` (`0.107.0`), `has_pck`, `has_dll`, `dependencies` (`[{"id":"BaseLib","min_version":"3.3.0"}]`), `affects_gameplay` (`content/ModTemplate/ModTemplate.json:1-13`).

Contrat loader : `<manifest dir>/<id>.dll` et `<manifest dir>/<id>.pck` (`Modding-Tutorial/docs/03-mod-loading-and-manifests.md:47-52`) ; plus de champ `pck_name`, et un `mod_manifest.json` embarqué n'est **pas** le contrat actif (`:97`) ; `dependencies` = IDs de manifeste, ordre de chargement garanti (`docs/02-vanilla-sts2-layout.md:66-67`).

### 5.3 Dossiers

Source : `.csproj`, `<ModId>.json`, `project.godot`, `export_presets.cfg`, `Sts2PathDiscovery.props`, `Directory.Build.props` à la racine ; code dans `ModTemplateCode/` ; assets + localisation dans `ModTemplate/` (`ModTemplate.csproj:42`, `:46`, `:59-61`). Contrainte : « Put solution and project in same directory » (`README.md:9`).

Installé : un sous-dossier par mod (`docs/09-packaging-installing-and-verifying.md:8-13`) :
```
<STS2>/mods/<ModId>/<ModId>.json
<STS2>/mods/<ModId>/<ModId>.dll
<STS2>/mods/<ModId>/<ModId>.pck
```
Build → copie de `dll`, `pdb`, `<AssemblyName>.json` vers `$(ModsPath)$(MSBuildProjectName)/` (`ModTemplate.csproj:94-99`), avec `ModsPath = $(Sts2Path)/mods/` (`Sts2PathDiscovery.props:26`, `:35`, `:44`).

### 5.4 Packaging PCK sans éditeur Godot

Commande réelle, dans le template :
```
"$(GodotPath)" --headless --export-pack "BasicExport" "$(ModsPath)$(MSBuildProjectName)/$(MSBuildProjectName).pck"
```
(`ModTemplate.csproj:114`, cible `GodotPublish` `AfterTargets="Publish"`, `:112` ; `EnvironmentVariables="IsInnerGodotExport=true;MSBUILDDISABLENODEREUSE=1"` `:115` ; `ContinueOnError="WarnAndContinue"` `:116`).

Contraintes : `GodotPath` doit exister sinon build en erreur (`:107-110`), défini dans `Directory.Build.props:5` (« MegaDot / Godot 4.5.1 mono executable used for export-pack when publishing »), avec un commentaire décisif : « Megadot current version is 4.5.1, and the game won't load your .pck if the Godot version used is newer » (`Directory.Build.props:3-4`). Preset `BasicExport`, plateforme `Windows Desktop`, `export_filter="all_resources"`, exclusion `ModTemplate.json` (`content/ModTemplate/export_presets.cfg:3-11`).

Déclencheur : `dotnet publish` (cible accrochée à `Publish`) — INFÉRENCE de sémantique MSBuild, aucun script du dépôt ne l'écrit.

Alternative sans Godot : `BSchneppe.StS2.PckPacker` 0.1.1, **commentée**, « Generates a .pck file when building if only using simple assets. May not work on all setups » (`ModTemplate.csproj:40-41`, cible `CopyQuickPck` `:101-105`).

**Dans Modding-Tutorial : commande ABSENTE.** La section « Building The PCK » ne liste que le contenu (`docs/06-assets-pck-and-localization.md:110-122`) et `docs/09:41` dit seulement « create `<ModId>.pck` ». L'outil cité, `godotpcktool.exe` sous `<STS2>/tools`, sert à **lister/extraire** le PCK vanilla (`docs/01-environment-setup.md:59-61`, `docs/04-reverse-engineering-the-game.md:58-64`), pas à créer un PCK de mod. Prérequis déclaré : « a Godot 4.5.1 .NET editor or a compatible PCK packer » (`docs/01:58-61`).

### 5.5 Procédure de test

1. Build → copie automatique (`ModTemplate.csproj:95-98`) ; ou copie du dossier complet vers `<STS2>/mods/<ModId>/` (`docs/08-debugging-and-fast-iteration.md:18-23`).
2. **Jeu fermé** avant de remplacer DLL/PCK (`docs/08:18`), staging recréé depuis les sorties connues, pas de copie fichier par fichier (`docs/08:21-26`).
3. Logs : `<STS2>/sts2_stdout.log`, `<STS2>/sts2_stderr.log`, `%APPDATA%/SlayTheSpire2/logs/...`, plus l'état du loader en jeu (`docs/08:5-12`).
4. Séquence loader attendue : scan récursif des `.json` sous `mods/`, tri par dépendances, chargement `<id>.dll`/`<id>.pck`, appel des `[ModInitializer]` (`docs/02:12-19`).
5. Test d'installation propre : archive exacte, game root propre, nouvelle run, save/quit/relaunch, logs (`docs/09:67-79`).
6. Preuve de release : version de jeu, hash DLL, hash PCK, chemins testés (`docs/09:93-104`).

Steam launch options / variables d'environnement runtime : **ABSENT** des deux dépôts (seule occurrence d'`EnvironmentVariables` : `ModTemplate.csproj:115`, c'est du build).

---

## 6. BaseLib-StS2 : apport, dépendance, helpers

Version `v3.4.7`, `min_game_version 0.107.1`, `affects_gameplay: false`, `has_pck`/`has_dll` true, `dependencies: []` (`BaseLib.json:1-11`).

### 6.1 API apportée (échantillon vérifié)

| Namespace | surface |
|---|---|
| `BaseLib.Config` | `ModConfig` (abstrait), `SimpleModConfig`, `ModConfigRegistry.Register/Get/GetAll`, attributs `[ConfigSection]`, `[ConfigSlider]`, `[ConfigButton]`, `[ConfigVisibleIf]`, `[ConfigHideInUI]`, `[ConfigColorPicker]` (`Config/ModConfig.cs:43`, `Config/SimpleModConfig.cs:16`, `Config/ModConfigRegistry.cs:7-30`, `Config/ConfigAttributes.cs`) |
| `BaseLib.Hooks` | `BaseLibHooks.AfterScryed/ModifyScryAmount/...` + interfaces `IAfterScryed`, `IModifyScryAmount`, `IMaxHandSizeModifier`, `IHealAmountModifier`, `ICardTypeTextModifier`, `IPlayCustomPowerSfx`, `IHealthBarForecastSource` (`Hooks/*.cs`) |
| `BaseLib.Abstracts` | `CustomCardModel`, `CustomRelicModel`, `CustomPowerModel`, `CustomPotionModel`, `CustomEventModel`, `CustomActModel`, `CustomCharacterModel`, `CustomBadge`, `CustomResource` |
| `BaseLib.Extensions` | `CardExtensions.GetModifiers/AddModifier`, `ControlExtensions.AddThemeFontSizeOverrideAll`, `HarmonyExtensions.TryPatchAll`, `DynamicVarExtensions.WithTooltip` |
| `BaseLib.Utils` | `HookUtils.Dispatch/Modify/Aggregate`, `GodotUtils`, `NodeFactory.CreateFromScene/RegisterSceneType`, `CustomLocTableManager`, `ModCredits` |
| `BaseLib.BaseLibScenes` | `NLogWindow`, `NHorizontalScrollContainer`, `NAdditionalCostDisplay`, `NRewardHighlight`, `NDynamicCombatBackground` |
| `BaseLib.Commands` / `ConsoleCommands` | `ScryCmd`, `MultiPileCardSelect`, `OpenLogWindow` |
| `BaseLib.Patches.*` | dictionnaires de contenu custom (`CustomContentDictionary.AddBadge/AddEvent/AddAct/RegisterType`) — volume : 106 fichiers `[HarmonyPatch]`, 362 attributs (grep) |

### 6.2 Faut-il en dépendre pour un mod read-only ?

**Non, pas nécessaire** — et pour ULTIMA je recommande de ne pas en dépendre.

Preuves :
- Le template traite BaseLib comme optionnel : « If you don't need it, remove it from the csproj and mod manifest json » (`ModTemplate-StS2/README.md:3`).
- Coût global : BaseLib patche le **processus entier** — `ExtendedSavePatches.Patch`, `TheBigPatchToCardPileCmdAdd.Patch`, `CustomBadgesPatch.Patch` puis `MainHarmony.TryPatchAll(assembly)` (`BaseLibMain.cs:55-66`) — soit 362 attributs `[HarmonyPatch]`, très au-delà du besoin d'un overlay.
- Coût de déploiement : déclarer `dependencies` rend le chargement obligatoire et ordonné (`docs/02:66-67`) et l'utilisateur doit installer dll+pck+json séparément (`BaseLib-StS2/README.md:9`).
- BaseLib écrit sa **propre** config au premier lancement (`ModConfigRegistry.Register(ModId, new BaseLibConfig())`, `BaseLibMain.cs:53`).
- L'API « badge » de BaseLib ne répond pas au besoin : `CustomBadge` est un badge de **fin de run** (`Abstracts/CustomBadge.cs:22-35`, `Rarity`/`IsObtained`), injecté dans le pool de badges du game-over, pas un overlay sur carte. Aucune API de badge-sur-carte dans le dépôt (grep `Badge` : uniquement `CustomBadge*`, `CustomContentDictionary.CustomBadgeTypes`, `NBadge`).

Ce qui est prouvé vs inféré : le coût (patches globaux, dépendance dure, config propre) est prouvé par le code ; le fait que BaseLib ne soit « pas indispensable » est une **INFÉRENCE** d'architecture, corroborée par le README du template et par le fait que les deux overlays qui fonctionnent (tierlist, advisor) ne l'utilisent pas.

Un overlay read-only n'a besoin que de `sts2.dll` + `0Harmony.dll` (+ `GodotSharp`), déjà référencés par le template (`ModTemplate.csproj:21-30`).

### 6.3 Helpers utiles si on dépendait de BaseLib

- **Config** : fichier `Path.Combine(OS.GetUserDataDir(), "mod_configs", filename)`, extension `.cfg` par défaut (`Config/ModConfig.cs:118-120`), format **JSON `Dictionary<string,string>`** (`SaveInternal`, `:306-320`), API `Load()` (`:365`), `Save()` (`:294`), `SaveDebounced()` (`:235-246`), statiques `ModConfig.Load<T>()` (`:219`), events `ConfigChanged`/`OnConfigReloaded` (`:51-52`), `SetupConfigUI(Control)` abstrait (`:193`). Contrainte forte : **seules les propriétés statiques** sont acceptées, les autres sont ignorées avec un warning (`:147-151`).
- **Hooks** : ex. `Task AfterScryed(PlayerChoiceContext, Player, int scryAmount, int discardAmount, List<CardModel> seen, List<CardModel> discarded)` (`Hooks/IAfterScryed.cs:53`) ; `int ModifyScryAmount(Player, int)` ; `int ModifyMaxHandSize(Player, int)` ; `decimal ModifyHealAdditive/Multiplicative(Creature, decimal)` ; `void AfterCardDowngraded(CardModel)` ; `IEnumerable<LocString> GetTypeModifiers(CardModel)` ; `IEnumerable<HealthBarForecastSegment> GetHealthBarForecastSegments(HealthBarForecastContext)` (`Hooks/IHealthBarForecastSource.cs:242-248`).
- **Logging** : `BaseLibMain.Logger` (`BaseLibMain.cs:23`), `ModConfigLogger.Warn/Error(msg, showInGui)` (`Config/ModConfig.cs:78-85`), fenêtre `NLogWindow` + commande `OpenLogWindow`.
- **UI/Godot** : `NodeFactory.CreateFromScene/CreateFromResource`, `GodotUtils.TransferAllNodes<T>`, `NHorizontalScrollContainer.Create`, `ControlExtensions.AddThemeFontSizeOverrideAll`.

Aucun de ces helpers n'est nécessaire à un overlay : `ConfigFile` Godot (tierlist, §2.4) couvre la persistance sans dépendance.

---

## 7. Pour ULTIMA : 5 patterns UI réutilisables (et leur adaptation read-only)

**P1 — Badge = enfant du nœud item, positionné en coordonnées locales, re-synchronisé chaque frame.**
Preuve : `Badges.Sync` + `Place` (`sts2-tierlist-overlay/Badges.cs:113-140`, `:260-287`), `MakeRoomForBadge` (`TierListOverlayMod.cs:379-400`).
Pourquoi : c'est le seul mécanisme qui survit au pooling des nœuds de cartes ; il suit automatiquement l'animation/le layout du jeu.
Adaptation ULTIMA : badge de létalité en `PanelContainer` + `MouseFilter = Ignore`, `AddChild` sur le nœud carte **sans** `ZIndex` (pour que les tips du jeu passent au-dessus), clé de contenu (itemId + valeur) pour ne pas reconstruire à chaque frame, `QueueFree()` + `RemoveChild` symétriques. Aucune écriture dans le modèle de carte.

**P2 — Observer l'arbre de scène plutôt que patcher (zéro Harmony).**
Preuve : `tree.NodeAdded` + `tree.ProcessFrame` et le commentaire « Harmony does not work on macOS arm64 » (`TierListOverlayMod.cs:28-33`, `:61-63`), `Tracked` + purge `RemoveWhere` (`:39`, `:86-87`).
Pourquoi : read-only par construction, aucune signature de méthode du jeu à maintenir, résistant aux mises à jour tant que les types de nœuds existent.
Adaptation ULTIMA : enregistrer sur `NodeAdded` uniquement les types utiles (`NCard`, `NRelic`, `NPotion`, `NRewardButton`, `NCombatRoom`…), et faire le calcul de létalité **hors** du callback (une fois par frame au plus, avec cache par `floor`/état de combat).

**P3 — Postfix Harmony ciblé quand l'arbre ne donne pas la donnée (liste des cartes offertes, état de run).**
Preuve : advisor `PatchMethod(..., "ShowScreen", ...)` en postfix seul (`GamePatches.cs:749`, `:801-824`) ; garde de contexte (`:92-99`, `:3753`), `WeakReference` sur l'écran (`:101`).
Pourquoi : la liste des options n'est pas lisible de façon fiable depuis l'arbre (les nœuds sont créés après) ; le postfix donne les arguments exacts.
Adaptation ULTIMA : postfix **sans** toucher aux arguments ni au `__result`, tout le corps dans un `try/catch` qui log et retourne (comme `:115-118`), ne jamais `PatchAll` en aveugle sur l'assembly du jeu, préférer `AccessTools.TypeByName` pour les types qui peuvent disparaître (`:756-780`).

**P4 — Tips enrichis dans le tooltip natif du jeu (pas une bulle maison).**
Preuve : tierlist `Tips` s'accroche à `NHoverTipSet` (`Tips.cs:44-47`), lit `_owner`/`_textHoverTipContainer` en réflexion (`:32-33`), ajoute dans le container du jeu puis corrige les overflows (`:88-91`) ; alternative RitsuLib `HoverTipHelper.AddTipToOwner` (`STS2-ViewedCardsStatistics/Patches/CardHoverTipStatsPatch.cs:46`).
Pourquoi : le tip garde le style, la position et le comportement du jeu (fermeture au mouvement, scroll) ; c'est ce que l'utilisateur attend d'un « tracker augmenté ».
Adaptation ULTIMA : **append-only** (jamais remplacer `_label.Text` ni retirer un tip existant), texte court (≤ ~28 caractères par ligne, cf. `docs/UI_TEXT.md:20-22`), et un fallback silencieux si la réflexion échoue (les noms privés changent : `docs/UPDATING.md:15-27`).

**P5 — Panneau latéral sur son propre `CanvasLayer`, config persistée avec version + migration.**
Preuve : advisor `CanvasLayer` `Layer = 100`, panneau ancré à droite, offsets en config (`OverlayManager.cs:441-454`, `:574`), `OverlaySettings` versionné avec migrations (`Core/OverlaySettings.cs:29-76`).
Pourquoi : c'est là que vivent les notes longues, l'historique et les réglages ; un overlay qui n'a que des badges ne peut rien expliquer.
Adaptation ULTIMA : `MouseFilter` explicite (le panneau capture, les badges non), opacité réglable, position mémorisée, et **fichier de config hors de `mods/`** (voir §9) — ou extension non-`.json` dans le dossier du mod.

Complément non-UI mais critique : la config. Le pattern le plus propre observé est `ConfigFile` Godot + section + `settings_version` + migrations (`sts2-tierlist-overlay/Settings.cs:33-99`) ; il évite d'écrire du JSON dans `mods/` et reste lisible/éditable à la main sous Linux (`~/.config` ou dossier du mod).

---

## 8. Licensing

| Dépôt | Licence | Preuve |
|---|---|---|
| sts2-advisor | MIT **déclarée dans le README**, aucun fichier `LICENSE` | `README.md:108-110` (« ## License » / « MIT ») ; `ls` racine : pas de LICENSE |
| sts2-tierlist-overlay | MIT, © 2026 Frank Blendinger | `LICENSE:1-3`, `README.md` fin |
| STS2-ViewedCardsStatistics | MIT, © 2026 OLC | `LICENSE:1-3`, `README.md:19-21` |
| spirescope | MIT, © 2026 The Quantum Falcon | `LICENSE:1-3`, `pyproject.toml` (`license = "MIT"`) ; tiers : FastAPI MIT, Starlette BSD-3, watchdog Apache-2.0, PyInstaller GPL-2.0-or-later with bootloader exception (`THIRD_PARTY_NOTICES.md`) |
| ModTemplate-StS2 | Pas de fichier LICENSE ; licence du **package** MIT | `Alchyr.Sts2.Templates.csproj:21` ; licence des templates elle-même ABSENTE |
| Modding-Tutorial | MIT, © 2026 fresh-milkshake | `LICENSE:1-3`, `README.md:67` |
| BaseLib-StS2 | MIT, sans ligne de copyright | `LICENSE.txt:1`, `BaseLib.csproj:36` (`PackageLicenseExpression`) |

Conséquence pratique : tout est MIT, donc réutilisation du code possible avec attribution ; l'absence de fichier LICENSE côté advisor et ModTemplate signifie qu'il faut citer le README/le package comme source de licence, pas un fichier.

---

## 9. Pièges de packaging Linux / Flatpak

1. **Le loader avale tous les `.json` de `mods/`.** « every `.json` below `mods/` is treated as a possible manifest, so do not store settings or arbitrary JSON beside installed mods unless they are outside the scanned tree or use a different extension » (`Modding-Tutorial/docs/03-mod-loading-and-manifests.md:54`, mécanique `docs/02:12-13`). → Config ULTIMA en `.cfg`/`.toml`/`.ini`, ou hors de `mods/`. L'advisor viole cette règle (`overlay_settings.json` + `Data/**/*.json` dans le dossier du mod, `Plugin.cs:77`, `QuestceSpire.csproj:38-40`).
2. **Nom et voisinage des payloads.** Manifeste `<id>.json` **à côté** de `<id>.dll` et `<id>.pck` (`docs/03:47-52`) ; `has_pck`/`has_dll` doivent refléter les fichiers réellement présents, sinon le loader laisse le mod en erreur (`docs/09:46-52`). Le `mod_manifest.json` camelCase de l'advisor (`Data/mod_manifest.json:7-10`) est le format hérité, non le contrat actuel (`docs/03:97`).
3. **Le `.pck` doit être produit par Godot/MegaDot 4.5.1.** « the game won't load your .pck if the Godot version used is newer » (`ModTemplate-StS2/content/ModTemplate/Directory.Build.props:3-4`). Sur Fedora il faut donc un binaire MegaDot 4.5.1 mono Linux (le template ne fournit qu'un exemple Windows, `:5-8`).
4. **Chemins Steam Linux codés en dur, casse fréquente.** `Sts2PathDiscovery.props` suppose `$(HOME)/.local/share/Steam/steamapps` (`:32`) → faux pour **Steam Flatpak** (`~/.var/app/com.valvesoftware.Steam/.local/share/Steam/...`), pour une bibliothèque secondaire, ou pour `~/.steam/steam`. Remède prévu : override `Sts2Path`/`SteamLibraryPath` (`local.props` ou `-p:Sts2Path=...`, `Sts2PathDiscovery.props:2-4`). Aucun dépôt ne documente Flatpak : **ABSENT** partout (grep `flatpak`/`.var/app` sur les 7 dépôts : 0 hors spirescope qui ne gère pas non plus).
5. **DLL de référence = même dossier de données que le runtime.** Le tutoriel impose « confirm all three referenced DLLs come from the same data directory » (`docs/01:18-21`) : `data_sts2_linuxbsd_x86_64` pour un jeu natif Linux, `data_sts2_windows_x86_64` si le jeu tourne via Proton. Un mod compilé contre les DLL Windows ne se charge pas dans un jeu natif Linux (et inversement) — INFÉRENCE directe de la résolution d'assemblies .NET, cohérente avec `Directory.Build.props:21-24` (tierlist) qui distingue les deux dossiers.
6. **`Private=false` obligatoire** sur `sts2`, `0Harmony`, `GodotSharp` : sinon des copies d'assemblies du jeu sont déposées à côté du mod (`docs/03:68`, `ModTemplate.csproj:21-30`).
7. **Le dossier `mods/` doit être inscriptible par l'utilisateur qui lance le jeu.** Pour une install Steam native c'est le cas ; pour Flatpak, l'arborescence vit sous `~/.var/app/com.valvesoftware.Steam/...` et un outil externe (script de déploiement, installeur) doit écrire **là**, pas dans `~/.local/share/Steam` (INFÉRENCE : aucun dépôt n'aborde le sandbox Flatpak). Corollaire : la config écrite « à côté de la DLL » (tierlist `settings.cfg`, advisor `overlay_settings.json`, ViewedCards via `OS.GetUserDataDir()`) atterrit dans le sandbox et n'est pas visible d'un outil hôte sans passer par `~/.var/app/.../`.
8. **Le toolkit de dev des grosses stacks est macOS-only.** `tools/reload.sh` de tierlist utilise `pgrep "SlayTheSpire2.app/Contents/MacOS"` et `osascript` (`tools/reload.sh:11`, `:34`), et le README assume « Developed and tested on macOS (Apple Silicon) only » — Windows/Linux « nobody has tried yet » (`README.md`, section Platforms).
9. **Chaîne de build incomplète côté advisor.** Pas de `project.godot`/`export_presets.cfg`/`.pck`, références `..\lib\*.dll` en chemins Windows, et `SETUP_GUIDE.md:8` parle d'injection `DOTNET_STARTUP_HOOKS` : ne pas partir de ce dépôt pour le packaging, seulement pour les idées d'UI.
10. **Le `.pck` n'est pas nécessaire pour un overlay.** Les deux overlays qui fonctionnent en production ont `has_pck: false` (`sts2-tierlist-overlay/TierListOverlay.json:7-8`) ou aucun `.pck` du tout ; un mod 100 % code + données embarquées (`EmbeddedResource`) évite complètement Godot et donc le piège n°3 — voie recommandée pour ULTIMA (`STS2-ViewedCardsStatistics.csproj:33-36` montre l'embarquement de la localisation en `EmbeddedResource`).
11. **Après une mise à jour du jeu, rien ne casse à la compilation mais tout peut casser à l'exécution.** Les points fragiles sont listés et outillés chez tierlist : réflexion sur membres privés (`NHoverTipSet._owner`, `_textHoverTipContainer`, `NSettingsTabManager._tabs`, `NPaginator._options`…), noms de nœuds (`TitleLabel`, `Cost`, `Hitbox`), constantes mesurées (`28.6`, `8.5%`, `11.7%`, `14%`, `0.65`) (`docs/UPDATING.md:15-31`, `TierListOverlayMod.cs:44`). Prévoir un `TestedGameVersion` + log d'avertissement comme `TierListOverlayMod.cs:53-57`.

---

## Annexe — ce qui manque (ABSENT), sans extrapolation

- Advisor : fichier LICENSE ; `.pck`/`project.godot`/`export_presets.cfg` ; toute gestion des chemins de sauvegarde Linux ; badges sur reliques/potions (retirés, `GamePatches.cs:288`).
- Tierlist : usage de Harmony (inexistant, choix documenté) ; support Linux testé ; `.pck`.
- ViewedCardsStatistics : enregistrement explicite de la commande console ; `.pck` (`has_pck: false`).
- spirescope : support Flatpak ; `.run`/`.save` d'exemple versionnés (seulement une fixture synthétique).
- Modding-Tutorial : commande de création de PCK ; toute mention Linux/Proton/Flatpak ; Steam launch options.
- BaseLib : API de badge/overlay sur carte ; licence des templates ModTemplate-StS2.
