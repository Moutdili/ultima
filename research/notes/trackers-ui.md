# Trackers UI — analyse des 3 dépôts (pour ULTIMA)

Analyse en lecture seule des trois dépôts de `/home/mio/sts2/research/repos/`. Aucun fichier des dépôts n'a été modifié. Jeu de référence d'ULTIMA : Slay the Spire 2 v0.107.1. Toutes les affirmations techniques sont suivies de `fichier:ligne` ; `ABSENT` quand l'information n'existe pas dans le dépôt.

**Correction d'une hypothèse de départ** : les dépôts 1 et 2 n'ont effectivement **aucun** fichier de licence ; le dépôt 3 (Hsyuan54670) en a un : **GPL-3.0** (`LICENSE:1-2`). Le GPL étant copyleft, la conclusion opérationnelle ne change pas : **réimplémenter, ne pas copier**. Les deux autres dépôts, sans licence, sont en « all rights reserved » par défaut : toute copie de code est juridiquement non autorisée.

---

## 1. Les trois dépôts

### 1.1 `sts2decktracker` — 999dulgi

- **But** : afficher la pioche (draw) et la défausse (discard) pendant le combat, plus un panneau « ▲ NEXT » pour la carte du dessus de la pioche (`README.md:3`, `CardListPanel/TopCardPanel.cs:81`).
- **Taille** : 14 fichiers `.cs`, 3 575 lignes. Manifeste version `1.4.4`, `has_pck: true`, `has_dll: true`, `affects_gameplay: false` (`sts2decktracker_manifest.json:5-10`).
- **Structure** :
  - `DeckTrackerInjectionPatch.cs` (207 l.) — patches Harmony d'injection/retrait + persistance de position.
  - `CardListPanel/` : `CardListPanel.cs` (1 250 l., UI), `CardPileWatcher.cs` (422 l., état), `TopCardPanel.cs` (468 l.), `TopCardTracker.cs` (25 l.), `TopCardTrackingPatches.cs` (90 l.), `CardStateBridge.cs` (128 l., 14 patches CardModel), `AppliedKeywordTrackerPatch.cs` (53 l.), `IntentOverlapHelper.cs` (34 l.), `CardColorHelper.cs` (46 l.).
  - `SettingsUI/` : `ModSettings.cs` (193 l.), `ModSettingsPanelNode.cs` (348 l.), `ArrowInputRow.cs` (180 l.), `ModdingScreenSettingsPatch.cs` (131 l.).
- **Version de jeu visée** : **non déclarée** (aucun champ `min_game_version` dans le manifeste). Le code cite explicitement un changement de comportement « 0.110.0 » (`CardListPanel/CardPileWatcher.cs:175`) → le code est écrit pour **≥ 0.110**, donc plus récent que la v0.107.1 d'ULTIMA. À considérer comme non compatible sans vérification des signatures.
- **Licence** : **ABSENTE** — aucun `LICENSE`/`COPYING`, aucune occurrence de « licen »/« copyright » dans `README.md` (158 l.), aucun en-tête SPDX dans les `.cs`.
- Cible de build : Godot.NET.Sdk 4.5.1, net9.0, Harmony 2.3.3 (`sts2decktracker.csproj:2-3,12`) ; chemins Steam Windows codés en dur (`sts2decktracker.csproj:14-15,24-27`).

### 1.2 `sts2-deck-tracker` — ronishae (« DeckTracker »)

- **But** : quantifier les dégâts par carte/relic/potion, avec overlay in-game (petit + plein écran) et export CSV par combat (`README.md:5-7`). Mod read-only revendiqué : « does not modify gameplay » (`README.md:6`), `affects_gameplay: false` (`DeckTracker.json:9`).
- **Taille** : 73 fichiers `.cs`, 9 122 lignes.
- **Structure** (dossiers) : `src/CardRegistry/` (25 fichiers, toute la comptabilité : `CardRegistry.cs`, `...DamageCalc.cs`, `...Buffs.cs`, `...Poison.cs`, `...Forge.cs`, `...Doom.cs`, `...Orbs.cs`, `...Persistence.cs`, `...Tracking.cs`, `...Lifecycle.cs`, `...SovereignBlade.cs`, etc.), `src/HookPatches/` (6 fichiers, 1 400 l. de hooks), `src/DeckTrackerOverlay/` (8 fichiers partiels, ~1 400 l. d'UI), `src/TrackerState/` (7 fichiers, stratégies d'attribution), `src/Contributions/` (5 fichiers), `src/RunExport/` (5 fichiers), `src/Statistics/` (5 fichiers), `src/Managers/` (2), `src/Logging/` (2), plus `ModEntry.cs`, `MainFile.cs`, `GameRelease.cs`.
- **Version de jeu visée** : **`min_game_version: "0.107.1"`** (`DeckTracker.json:5`) — exactement la version d'ULTIMA. Version du mod `v0.0.0` (`DeckTracker.json:4`).
- **Licence** : **ABSENTE** — aucun `LICENSE`/`COPYING` (`.gitignore` et README ne mentionnent aucune licence).
- Build Linux supporté : `Sts2PathDiscovery.props:31-36` résout `~/.local/share/Steam/steamapps/...` et `data_sts2_linuxbsd_x86_64` ; Harmony et `sts2.dll` viennent du dossier de données du jeu (`DeckTracker.csproj:21-28`).

### 1.3 `Slay-the-Spire-2-mod-StS2-Damage-Logger` — Hsyuan54670

- **But** : HUD de dégâts in-game (par joueur) + journal structuré des événements de combat dans un fichier, avec overlay desktop WPF optionnel (`README.md:3`).
- **Taille** : 9 fichiers `.cs` livrés, 1 794 lignes (+ 1 prototype non compilé de 189 l. sous `mod-prototype/`). Le reste du dépôt est du bruit (`obj/`, `bin/`, `dist/`, `.dotnet/`, `.appdata/`).
- **Structure** : `DamageLoggerModV2.cs` (446 l., point d'entrée + 6 patches + écriture du log), `InGameDamageTracker.cs` (362 l.), `InGameDamageHud.cs` (288 l.), `DamageLogModels.cs` (136 l.), `DamageLogTailService.cs` (96 l.), `OverlayViewModel.cs` (365 l.), `MainWindow.xaml(.cs)` (94 l. de code), `App.xaml.cs` (7 l.), `mod_manifest.json`, `project.godot`, `pack_mod.gd`, `publish-mod.ps1`, `LICENSE`.
- **Version de jeu visée** : **ABSENTE**. Aucun numéro de version de jeu dans `mod_manifest.json:1-7` ni dans le README ; seule la version du moteur Godot 4.5.1 est citée (`README.md:157`).
- **Licence** : **GPL-3.0** (`LICENSE:1-2`), sans en-tête SPDX dans les sources.
- Build : mod `net9.0` Library (`Sts2DamageLoggerV2.csproj:3-9`) ; overlay `net9.0-windows` + `UseWPF` (`Sts2DamageOverlay.csproj:3-5`).

### 1.4 Récapitulatif

| | repo1 999dulgi | repo2 ronishae | repo3 Hsyuan |
|---|---|---|---|
| `.cs` / lignes | 14 / 3 575 | 73 / 9 122 | 9 / 1 794 |
| Version jeu | ABSENTE (code ≥0.110) | **0.107.1** | ABSENTE |
| Licence | **ABSENTE** | **ABSENTE** | GPL-3.0 |
| Overlay in-game | oui (2 panneaux + NEXT) | oui (petit + plein écran) | oui (HUD 1 panneau) |
| Attribution dégâts | non | oui (très complète) | non (totaux par joueur) |
| Export | non | CSV + JSON | log pipe |
| Config utilisateur | JSON à côté de la DLL | ABSENTE (toggles en mémoire) | ABSENTE |

---

## 2. INJECTION UI — cibles Harmony exactes

### 2.1 repo1 (999dulgi)

| Cible | Méthode exacte | Type | Rôle | Réf. |
|---|---|---|---|---|
| `NCombatUi` | `_Ready` | Postfix | crée et ajoute les 3 panneaux | `DeckTrackerInjectionPatch.cs:8,18` |
| `NCombatUi` | `OnCombatWon` (nom en **string**, pas `nameof`) | Postfix | masque + `QueueFree()` les panneaux | `DeckTrackerInjectionPatch.cs:164-183` |
| `NGame` (via `AccessTools.TypeByName`) | `ReturnToMainMenu` | Prefix | sauve la position custom puis remet `_isReturningToMainMenu` | `DeckTrackerInjectionPatch.cs:192-204` |
| `NModInfoContainer` (via `TypeByName`) | `Fill(Mod)` | Postfix | injecte l'écran de réglages du mod | `SettingsUI/ModdingScreenSettingsPatch.cs:7-28` |

Ajout dans l'arbre Godot : les panneaux sont ajoutés **directement comme enfants de `NCombatUi`**, pas dans un `CanvasLayer` dédié : `__instance.AddChild(_drawPilePanel)` (`DeckTrackerInjectionPatch.cs:33`), `AddChild(_topCardPanel)` (:40), `AddChild(_discardPilePanel)` (:50). `NCombatUi` n'est pas un singleton et est recréé à chaque combat (note explicite `CLAUDE.md:17`), donc le Postfix `_Ready` s'exécute une fois par combat.

Nettoyage / destruction :
- À la victoire : parcours des enfants de `NCombatUi`, `Visible = false` puis `QueueFree()` pour `CardListPanel` et `TopCardPanel` (`DeckTrackerInjectionPatch.cs:171-183`).
- **Aucun patch sur la défaite** : la destruction repose alors sur la destruction de `NCombatUi` par le jeu (inférence, non vérifiée en exécution).
- `_ExitTree()` du panneau sauve la position custom dans un champ statique si on ne retourne pas au menu (`CardListPanel.cs:79-85`).
- Les champs statiques `_drawPilePanel/_topCardPanel/_discardPilePanel` ne sont **jamais remis à `null`** après `QueueFree` (:11-13) ; les usages ultérieurs se protègent par `IsInsideTree()` / `IsInstanceValid` (:68,86,149-161).

Autres hooks d'état (non-UI) du même dépôt : `CardModel.GiveSingleTurnRetain`, `GiveSingleTurnSly`, `EndOfTurnCleanup`, `InvokeEnergyCostChanged`, `BaseStarCost` setter, `AddTemporaryStarCost`, `BaseReplayCount` setter, `AddKeyword`, `RemoveKeyword`, `EnchantInternal`, `ClearEnchantmentInternal`, `AfflictInternal`, `ClearAfflictionInternal`, `UpgradeInternal`, `DowngradeInternal` (`CardListPanel/CardStateBridge.cs:17-127`) ; `CardCmd.ApplyKeyword` (`AppliedKeywordTrackerPatch.cs:14`) ; `AbstractModel.MutableClone` (`AppliedKeywordTrackerPatch.cs:44`) ; `CardPileCmd.Add`, `CardModel.RemoveFromCurrentPile`, `AfterTransformedFrom`, `AfterTransformedTo` (`TopCardTrackingPatches.cs:19,51,67,79`).

### 2.2 repo2 (ronishae)

Pas d'injection dans `NCombatUi`. L'overlay est un **`CanvasLayer` autonome** ajouté à la racine de la `SceneTree` :

- `EnsureCreated()` : `_instance = new CanvasLayer { Layer = 100, Name = "DeckTrackerOverlay" }`, puis `tree.Root.CallDeferred(Node.MethodName.AddChild, _instance)` (`DeckTrackerOverlay.cs:77-97`, citation :90 et :95).
- Déclencheur unique : le hook `Hook.BeforeCombatStart` → `DeckTrackerOverlay.EnsureCreated()` protégé par `_overlayScheduled` (`HookPatches.cs:50-68`, citation :63-67). D'où la note du README : l'UI n'apparaît qu'au premier combat (`README.md:17`).
- Abonnement au frame : `tree.ProcessFrame += OnProcessFrame` et `tree.Root.TreeExiting += OnGameExiting` (`DeckTrackerOverlay.cs:83-87`).
- Cibles Harmony d'état (extrait ; liste complète dans `ModEntry.cs`) : `Hook.BeforeCombatStart`, `Hook.AfterCombatEnd`, `Hook.AfterRoomEntered`, `Hook.AfterSideTurnStart`, `Hook.AfterPlayerTurnStart`, `Hook.AfterDamageGiven`, `Hook.ModifyDamage`, `Hook.AfterForge`, `Hook.AfterDiedToDoom`, `Hook.BeforePowerAmountChanged`, `Hook.BeforeCardPlayed/AfterCardPlayed/AfterCardDrawn/AfterCardChangedPiles/BeforeCardRemoved`, `Hook.BeforePotionUsed/AfterPotionUsed/...` (`ModEntry.cs:38-85`) — toutes posées par réflexion via `PatchHook(nameof(Hook.X), nameof(HookPatches.X))` (`ModEntry.cs:253-258`).
- Patches de types concrets (pouvoirs/orbes/relics), posés un par un ou par boucles : `PoisonPower.AfterSideTurnStart`, `DoomPower.DoomKill`, `CountdownPower.AfterSideTurnStart`, `ReaperFormPower.AfterDamageGiven`, `NecroMasteryPower.AfterCurrentHpChanged`, `ReflectPower.AfterDamageReceived`, `PowerCmd.Remove`, `CreatureCmd.LoseBlock`, `LoopPower.AfterPlayerTurnStart`, `RollingBoulderPower.AfterPlayerTurnStart`, `TheBombPower.BeforeSideTurnEnd`, `PanachePower.AfterCardPlayed`, `PrepTimePower.AfterSideTurnStart`, `RitualPower.AfterSideTurnEnd`, `HandDrill.AfterDamageGiven`, `GoldPlatedCables.ModifyOrbPassiveTriggerCounts`, `TheBoot.ModifyHpLostAfterOstyLate`, `OrbCmd.Channel`, `TemporaryFocusPower.BeforeApplied/AfterPowerAmountChanged/AfterSideTurnEnd`, `LightningOrb/GlassOrb/DarkOrb.Passive/Evoke` (`ModEntry.cs:88-250`).
- Nettoyage : uniquement `OnGameExiting()` → `_instance.Free()` (`DeckTrackerOverlay.cs:99-107`). Aucune destruction par combat ; le panneau est simplement masqué par `SyncVisibility()` (`:169-176`) et `SetFullScreenVisible(false)` (`:159-167`).

### 2.3 repo3 (Hsyuan)

- `[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]`, Postfix → `InGameDamageTracker.AttachToControl(__instance)` (`DamageLoggerModV2.cs:352-359`).
- Le HUD est un `CanvasLayer` (`Layer = 2048`, `ProcessMode = Always`) qui devient enfant de l'instance `NCombatUi` : `parent.AddChild(rootNode)` (`InGameDamageTracker.cs:70-76`, citation :75 ; layer construit `InGameDamageHud.cs:66-71`).
- Re-parentage si le parent change : `currentParent.RemoveChild(rootNode); parent.AddChild(rootNode);` (`InGameDamageTracker.cs:77-82`).
- `AttachToGame()` (appelé au `Hook.BeforeCombatStart`, `DamageLoggerModV2.cs:342-349`) ne fait qu'appeler `EnsureHudExists()` qui est **vide** → l'attache réelle ne peut passer que par `NCombatUi.Activate` (`InGameDamageTracker.cs:51-54`).
- Nettoyage : `Dispose()` existe (`InGameDamageHud.cs:152-159`) et `Detach()` l'appelle (`InGameDamageTracker.cs:87-97`), mais **`Detach()` n'a aucun appelant** → pas de cycle de destruction.

---

## 3. CONSTRUCTION UI

### 3.1 repo1 — `CardListPanel : Panel`

Arbre : `Panel` → `VBoxContainer(mainContainer)` → `MarginContainer` (marges 10) → `VBoxContainer(innerContainer)` → `ScrollContainer` → `VBoxContainer(_cardList)` (`CardListPanel.cs:199-234`). Chaque ligne : `HBoxContainer` → `Control` (clip, `ClipContents=true`) → `TextureRect` (portrait) + `HBoxContainer` de labels ; puis conteneurs de coût énergie/étoile (`:745-772`, `:774-823`, `:926-932`). Le panneau « NEXT » est un `Panel` → `MarginContainer` → `VBoxContainer` → `Label` + `HBoxContainer` (`TopCardPanel.cs:67-95`).

- **Click-through** : `MouseFilter = Ignore` sur le panneau (`CardListPanel.cs:195`), puis `SetMouseIgnoreRecursive(this)` qui force `Ignore` sur **tous** les descendants (`:178-184`, appel `:238`). La barre de boutons est ajoutée **après** la récursion pour conserver son `Stop`/`Pass` par défaut (commentaire explicite `:243-250`). `_scrollContainer.MouseFilter = Ignore` réaffirmé et scrollbar rendue transparente par `Modulate` en `CallDeferred` (`:240-241`).
- **Drag** : dans `_Input`, sur `InputEventMouseButton` gauche, test `new Rect2(GlobalPosition, Size).HasPoint(mb.GlobalPosition)` puis `_dragOffset` (`:301-319`) ; déplacement dans `_Process` (`:333-336`). Le drag n'appelle **pas** `SetInputAsHandled()` → le clic traverse aussi vers le jeu.
- **Position** : `_defaultPosition` (settings) vs `_customPosition` ; `UpdatePosition()` choisit l'un ou l'autre et écrit `GlobalPosition` (`:122-127`) ; reset via bouton ↺ (`:279-286`). Persistance inter-combats par champs statiques (`DeckTrackerInjectionPatch.cs:14-16,99-113`), et sur disque seulement si `RememberCustomPosition` (`:122-138`).
- **Opacité** : `Modulate = new Color(1,1,1,_currentOpacity)` sur `_contentContainer` uniquement — les boutons restent opaques (`CardListPanel.cs:94,396-399`). Lissage `Mathf.Lerp(..., 5.0f * delta)` (`:25,394-399`). Bascule idle/active : par minuterie (`_idleDelaySeconds`) ou par chevauchement des icônes d'intention via `IntentOverlapHelper.IsOverlappingEnemyCreature` (`:404-416`, helper `IntentOverlapHelper.cs:8-32` qui lit `creature.IntentContainer` et la propriété dynamique `_intentHolder`).
- **Redimensionnement** : pas de poignée ; la taille vient de `PanelWidth/PanelHeight` dérivés de `CardWidth/CardHeight` (`ModSettings.cs:100-108`) et se règle par les boutons `−/+` du mini-panneau (`CardListPanel.cs:1189-1211`). Hauteur dynamique recalculée depuis `_cardList.GetMinimumSize().Y`, plafonnée à `790 - Y` si `ScrollableAutoHeight` (`:148-176`).
- **Thème / police** : `res://fonts/kreon_regular.ttf` chargée en `ResourceLoader.Load<Font>` (`:29`) ; `AddThemeFontOverride("font", KreonRegular)` + `ApplyLocaleFontSubstitution(FontType.Bold, "font")` (`:797-798,821-822`) ; couleurs du jeu `StsColors.gold/cream/green/...` (`:790,1024-1029`) ; `StyleBoxEmpty` pour supprimer le fond du `Panel` (`:197`). `CardColorHelper.GetTitleColors` mappe la rareté (`CardColorHelper.cs:10-44`).
- **Hotkeys** : **ABSENT** (grep `InputMap|IsActionPressed|InputEventKey` = 0 ; seuls des événements souris `CardListPanel.cs:301,321`).

Détails utiles : tri initial Random/Alphabetical/Cost (`CardListPanel.cs:539-565`), regroupement par clé de carte (`CardPileWatcher.cs:399-420`), tooltips via `NHoverTipSet.CreateAndShow/Remove` (`CardListPanel.cs:383-391`), icônes d'enchantement/affliction/temp keywords (`:829-914`), scène d'effet d'affliction chargée depuis `res://sts2decktracker/effects/<id>.tscn` (`:683-693`).

### 3.2 repo2 — overlay `PanelContainer` dans `CanvasLayer`

- **Petit panneau** : `PanelContainer` à `Position(20,160)`, `CustomMinimumSize(280,50)`, fond `StyleBoxFlat` noir 0.8 alpha, coins 4 (`DeckTrackerOverlay.Build.cs:9-10`) → `MarginContainer` 10 px → `VBoxContainer` → `HBoxContainer` d'en-tête (`Label` titre + bouton `+Forge` + bouton `[+]`) + `ColorRect` séparateur + `ScrollContainer` (hauteur min 250) → `VBoxContainer` de lignes (`DeckTrackerOverlay.Build.cs:15-53`). Placement initial contre le bord droit sous la barre de reliques, une seule fois (`:58-68`).
- **Drag** : uniquement par le `Label` de titre, avec `MouseFilter = Stop`, curseur `Move`, `GuiInput += OnSmallPanelDragInput` (`DeckTrackerOverlay.Build.cs:25-28`) ; offset conservé et position **clampée** au viewport (`:72-95`).
- **Plein écran** : `PanelContainer` 1800×900 centré par ancres (`Anchor 0.5`, offsets ±900/±450), fond 0.98 alpha, bordure 2 px, coins 8 (`DeckTrackerOverlay.Build.cs:100-106`) ; en-tête avec cases Act 1/2/3, boutons de toggles, `X` (`:114-158`) ; onglets Cards/Relics/Potions (`:160-173`) ; `ScrollContainer` + `VBoxContainer` de lignes (`:184-188`). Fermeture = `Visible=false` (`DeckTrackerOverlay.cs:159-167, OnClosePressed Toggles.cs:63`).
- **Click-through** : **ABSENT** au niveau des panneaux. Seuls les lignes/contrôles internes passent en `Pass` (`DeckTrackerOverlay.Helpers.cs:180`) ou `Ignore` (`DeckTrackerOverlay.RenderCards.cs:280`) ; aucun `MouseFilter` n'est posé sur `_smallPanel`/`_fullScreenPanel`, qui gardent donc le défaut `Stop` de `Control` → la surface du panneau consomme les clics. Conséquence pour ULTIMA : ne pas copier ce choix.
- **Position persistée** : **ABSENTE** — aucune sauvegarde de la position du panneau ; `_smallPanelPositioned` n'empêche qu'un repositionnement ultérieur (`DeckTrackerOverlay.Build.cs:17-19,58-68`).
- **Opacité** : **ABSENTE** (pas de fade ; alpha fixe dans les `StyleBoxFlat`).
- **Redimensionnement** : **ABSENT** (tailles fixes ; le petit panneau grandit avec son contenu).
- **Thème / police** : pas de police custom ; `AddThemeFontSizeOverride` (12/24 px) et couleurs littérales `new Color("FACC15")`, `"4ADE80"`, `"38BDF8"` (`DeckTrackerOverlay.Build.cs:22-35,116-118` ; `DeckTrackerOverlay.RenderCards.cs:44`).
- **Hotkeys** : polling clavier dans `HandleInputs()` appelé chaque frame : `[` = `Key.Bracketleft` (petit panneau), `]` = `Key.Bracketright` (plein écran), `\` = `Key.Backslash` (log VeryDebug), avec détection de front montant par champs `_bracketLeftWasPressed`, etc. (`DeckTrackerOverlay.cs:132-157`).
- **Lignes / colonnes** : en-têtes triables `CARD NAME | RUN DMG | COMBAT DMG | % PLAYED | RUN AVG (#) | HALLWAY/ELITE/BOSS AVG (#) | ADDED | REMOVED | EVOLVED`, largeurs 80-300 px (`DeckTrackerOverlay.RenderCards.cs:87-99`) ; tri par `_currentSort.Column` (`:184-208`) ; lignes `HBoxContainer` de `Label` à largeur minimale fixe (`:294-362`). Rebuild complet (détache + `QueueFree` de tous les enfants) à chaque publish (`DeckTrackerOverlay.cs:188-189`).

### 3.3 repo3 — HUD `CanvasLayer` + WPF

- Arbre : `CanvasLayer` (Layer 2048) → `Control` hôte ancré à droite (`AnchorLeft/Right=1`, `OffsetLeft=-508`, `OffsetRight=-108`, `OffsetTop=84`, `OffsetBottom=320`) → `PanelContainer` → `VBoxContainer` (titre 18 px, contexte 13 px, totaux 13 px, ligne d'en-tête, `VBoxContainer` de lignes, hint 11 px) (`InGameDamageHud.cs:66-84,161-225`).
- **Click-through** : `MouseFilter = Ignore` sur l'hôte, le `PanelContainer`, le `VBoxContainer` racine et le conteneur de lignes (`InGameDamageHud.cs:75,181,190,207`) → oui, totalement traversable. Aucun drag (`_Input`/`GuiInput` absents).
- **Position / taille** : ancrée, offsets fixes ; pas de persistance, pas de resize (`:80-83`).
- **Thème / police** : pas de police custom ; `StyleBoxFlat` fond (0.08,0.09,0.12,0.86), bord orange, coins 10, marges 14/12 (`:163-186`) ; tailles/couleurs par overrides.
- **Hotkeys** : `F8` par **polling** dans un `Godot.Timer` de 0.05 s (`WaitTime=0.05`, `Autostart`) (`InGameDamageHud.cs:89-96,228-239`).
- **Partie WPF** (processus séparé, pas in-game) : fenêtre 560×340 `Topmost`, sans bordure, fond transparent, drag par `DragMove()` (`MainWindow.xaml:9-22`, `MainWindow.xaml.cs:77-83`) ; elle *tail* le même fichier de log et réimplémente l'agrégation (`OverlayViewModel.cs:148-174`). Aucun click-through (`WS_EX_TRANSPARENT` absent).

---

## 4. LECTURE D'ÉTAT

### 4.1 repo1 — piles et cartes

Point d'entrée par frame : `_Process` du panneau appelle `_pileWatcher.Tick()` (`CardListPanel.cs:401`). Dans `Tick()` :

- Garde combat : `CombatManager.Instance == null || !CombatManager.Instance.IsInProgress` (`CardPileWatcher.cs:89`).
- État de combat : `CombatManager.Instance.DebugOnlyGetState()` (`:109`).
- Joueur local : `LocalContext.GetMe(combatState)` (`:113`), puis `_currentPlayer.PlayerCombatState` (`:114`).
- Piles : `_currentPlayer.PlayerCombatState.DrawPile` (`:121`) et `.DiscardPile` (`:132`). **La main (Hand) n'est jamais lue** (grep `PileType.` : seuls `Draw` et `Discard`, p. ex. `CardPileWatcher.cs:132`).
- Contenu : `pile.Cards` (`:69,530`), `pile.IsEmpty` (`CardListPanel.cs:414`), `pile.Cards.Count` (`CardPileWatcher.cs:182`).
- Événements de pile : `CardAdded`, `CardRemoved`, `ContentsChanged` (`:149-151,180-184`), avec le commentaire important : depuis 0.110, les déplacements en lot (shuffle) utilisent `RemoveInternal(silent:true)` → seul `ContentsChanged` est émis (`:175-179`), d'où le resync par comparaison de comptes (`:182`).
- Ressources : `PlayerCombatState.EnergyChanged`, `.StarsChanged` (`:344-345`), uniquement pour invalider le cache de clé de groupe (`:356`).
- Pouvoirs du joueur : `player.Creature` (`:117`) puis `Creature.PowerApplied`, `.PowerIncreased`, `.PowerDecreased`, `.PowerRemoved` (`:285-288`) ; ces événements ne servent qu'à invalider le cache si le pouvoir override `AbstractModel.TryModifyEnergyCostInCombat`, `...Late`, `TryModifyStarCost`, `TryModifyKeywordsInCombat` (détection par réflexion `:307-326`).
- **HP / block : ABSENT** — aucune lecture de `CurrentHp`, `MaxHp` ou `Block` dans tout le dépôt (grep).
- **Pouvoirs affichés : ABSENT** — aucun rendu de powers.

Membres `CardModel` utilisés : `Title`, `IsUpgraded`, `CurrentUpgradeLevel`, `Portrait`, `Rarity`, `Pool.EnergyOutlineColor`, `VisualCardPool?.EnergyIconPath`, `EnergyIcon`, `EnergyCost` → `.CostsX`, `.GetWithModifiers(CostModifiers.All|None)`, `.WasJustUpgraded`, `.HasLocalModifiers`, `GetStarCostWithModifiers()`, `HasStarCostX`, `TemporaryStarCost`, `Enchantment` → `.IntendedIconPath`, `.GetType().Name`, `Affliction` → `.Id.Entry`, `Keywords`, `GetKeywordsWithSources(KeywordSources.Local)`, `ShouldRetainThisTurn`, `IsSlyThisTurn`, `BaseReplayCount`, `CombatState`, `HoverTips`, `Id.Entry` (`CardListPanel.cs:678,683-723,730-833,1068` ; `CardPileWatcher.cs:365-413`).
Membres d'infrastructure : `CardPileCmd.Add(IEnumerable<CardModel>, CardPile, CardPilePosition, ...)` (`TopCardTrackingPatches.cs:26-43`), `CardModel.RemoveFromCurrentPile()`, `AfterTransformedFrom/To` (`:51,67,79`).

Le suivi de la carte du dessus est un `HashSet<CardModel>` : `TopCardTracker.MarkAsIntendedTop / IsIntendedTop / Untrack / PruneCards / Clear` (`TopCardTracker.cs:10-24`). `TopCardPanel` rend la première carte de `drawPile.Cards` marquée (`TopCardPanel.cs:120-131`), se positionne à `drawPilePanel.GlobalPosition.X + width + 4` (`:407-408`) et renvoie `false` depuis `_HasPoint` pour être totalement transparent aux clics (`:31-34`).

### 4.2 repo2 — deck, HP, block, powers

- Deck (hors combat et à chaque salle) : `runState.Players[i].Deck.Cards`, plus `p.NetId`, `p.NetId.ToString()` (`HookPatches.cs:163-183`). Rescan hors combat throttlé à 10 frames (`DeckTrackerOverlay.cs:62,116-120`) avec signature `HashCode.Combine(c.Id.Entry, c.CurrentUpgradeLevel, c.Enchantment?.Id.Entry, p.NetId)` (`HookPatches.cs:148-161`).
- Piles : **pas d'affichage draw/discard/main** ; seule lecture `card.Pile?.Type == PileType.Hand` pour distinguer les pioches (`HookPatches.Cards.cs:33,82`).
- HP : `creature.CurrentHp` (`CardRegistryDoom.cs:140-142`), `creature.IsAlive` (`HookPatches.Cards.cs:234` ; `CardRegistryLifecycle.cs:197`).
- Block : `target.Block` lu **avant** réduction dans le prefix `CreatureCmd.LoseBlock` (`CardRegistryForcedBlockLoss.cs:14-23`).
- Pouvoirs : `creature.GetPower<T>()` — `VulnerablePower`, `CrueltyPower`, `DebilitatePower` (`CardRegistryDamageCalc.cs:43-46`), `FocusPower` (`CardRegistryOrbs.cs:296`), `player.GetRelic<PaperPhrog>()` (`CardRegistryDamageCalc.cs:44`).
- Événements de pouvoir : `Hook.BeforePowerAmountChanged` (postfix) reçoit `(ICombatState, PowerModel power, decimal amount, Creature target, Creature? applier, CardModel? cardSource)` et route selon `power.Id.Entry` (`HookPatches.Powers.cs:11-215`).
- Cartes : `CardModel.Id.Entry`, `FloorAddedToDeck`, `CurrentUpgradeLevel`, `Enchantment`, `CurrentUpgradeLevel`, `Pile`, `Owner` ; identité stable `GetTrackingId()` = `"{baseId}_F{floorAdded}_C{copyIndex}_U{upgradeLevel}_{enchant}_P{owner}"` (`CardRegistryCardIdentity.cs:381-398`).
- Résultat de dégâts : `DamageResult.TotalDamage`, `.UnblockedDamage`, `.BlockedDamage`, `.OverkillDamage`, `.WasTargetKilled`, `.WasBlockBroken` (`HookPatches.Cards.cs:209-216`).
- Run : `runState.TotalFloor`, `runState.CurrentActIndex`, `runState.BaseRoom?.RoomType`, `runState.Players.Count`, `runState.AscensionLevel`, `ReleaseInfoManager.Instance.ReleaseInfo?.Version` (`HookPatches.cs:90-107,163-183` ; `GameRelease.cs:11-17`).

### 4.3 repo3 — lecture minimale

`Hook.AfterDamageGiven(PlayerChoiceContext, CombatState, Creature? dealer, DamageResult results, ValueProp props, Creature target, CardModel? cardSource)` (`DamageLoggerModV2.cs:406-416`) ; run via `combatState.RunState` (`:418`) ; `runState.CurrentActIndex`, `.TotalFloor`, `.CurrentLocation`, `combatState.RoundNumber` (`:330-340`) ; `dealer.Player.NetId` (`:432`) ; `results.TotalDamage/UnblockedDamage/BlockedDamage/OverkillDamage/WasTargetKilled/WasBlockBroken` (`:434-439`). Piles, HP, block, powers : **ABSENT**.

---

## 5. ATTRIBUTION DE DÉGÂTS (repo2 / ronishae)

### 5.1 Modèle de contributions

Tout est en `decimal` et stocké dans `CardRegistry.EntityLedger : Dictionary<string, EntityStats>` indexé par *tracking id* (`CardRegistry.cs:9`). Les « contributions » sont des paires `{ TrackingId, Amount }` (`Contributions/Contribution.cs:3-7`) ; `DamageHistoryItem` porte le contexte d'un coup (combat, dealer, `DamageResult`, cible, `CardModel`, `BaseDamage`, `ActiveConquerorId`) (`Contributions/DamageHistoryItem.cs:7-24`).

Deux niveaux :
1. **Ledgers de buffs** : `PersistentLedgers`, `ConsumableLedgers`, `DurationLedgers[creature][powerId]` (`CardRegistryBuffs.cs:42-47,125`).
2. **Trackers par pouvoir** (5 stratégies) : `GenericDamageTracker` (FIFO simple, `TrackerState/GenericDamageTracker.cs:22-74`), `TargetedDamageTracker` (FIFO par cible, `:23-91`), `ProportionalShareTracker` (parts proportionnelles, `:21-116`), `QueueBuilderTracker` (file de charges, `:30-148`), `BuffHandoffTracker` (transfert exact/proportionnel vers un autre ledger, `:23-108`), plus `InstancedPowerTracker` (mapping instance de pouvoir → source, `:30-75`).

### 5.2 Hooks utilisés

- **Capture des modificateurs** : `Hook.ModifyDamage` en Postfix (`HookPatches.Cards.cs:92-140`). Le postfix **recalcule** chaque modificateur en appelant `mod.ModifyDamageAdditive(...)` et `mod.ModifyDamageMultiplicative(...)` sur la liste `modifiers` (`:119-133`), puis stocke un `DamageSnapshot { BaseDamage, CardSource, Target, Dealer, Props, AdditiveModifiers, MultiplicativeModifiers }` dans `CardRegistry.CurrentAttackSnapshot` (`:110-134`). Il ignore `previewMode != None` et `cardSource == null` (`:96-108`).
- **Paiement** : `Hook.AfterDamageGiven` en Postfix (`HookPatches.Cards.cs:209-306`). Ordre de routage : joueur cible → return (`:211-214`) ; correction `TheBoot` (`:218-223`) ; relique en cours (`:225-229`) ; poison (`:231-239`) ; orbe (`:241-245`) ; tracker ciblé (`:247-252`) ; tracker simple (`:254-259`) ; tracker proportionnel (`:261-266`) ; NecroMastery (`:268-272`) ; Reflect (`:274-278`) ; source sans carte (`:280-294`) ; sinon `ProcessDamageSnapshot` puis `TryHandleCustomCardDamage` ou `AddDamage` (`:296-305`).
- **Décomposition des multiplicateurs** : `CardRegistry.ProcessDamageSnapshot(snapshot, actualDealtDamage)` (`CardRegistryDamageCalc.cs:11-153`) : calcule `basePlusAdditives`, `totalMultipliers`, `currentCalculatedDamage = floor(basePlusAdditives * mult)`, `overkill = max(0, calc - actual)`, puis « pèle » les multiplicateurs **en ordre inverse** (`:34`) puis les additifs en ordre inverse (`:121`), en payant chaque différence marginale à son auteur. Retourne le montant restant attribuable à la carte (`:152`).

### 5.3 Vulnerable

- Ledger FIFO par cible : `DurationDebuffPowerIds = { VULNERABLE_POWER, DEBILITATE_POWER, GIGANTIFICATION_POWER, FLANKING_POWER, KNOCKDOWN_POWER }` (`CardRegistryBuffs.cs:36-39`), alimenté dans `BeforePowerAmountChangedPostfix` (`HookPatches.Powers.cs:78-83`) via `AddDurationBuff(target, powerId, amount, GetCurrentSourceId(cardSource))` (`CardRegistryBuffs.cs:127-147`), décrémenté par `RemoveDurationBuff` → `DrainLedger(ledger, amount, lifo:false)` (`:149-168`).
- Paiement : dans `ProcessDamageSnapshot`, `VULNERABLE_POWER` est traité comme multiplicateur composite : base `vulnPower.DynamicVars["DamageIncrease"].BaseValue` (défaut 1.5), puis `PaperPhrog.ModifyVulnerableMultiplier`, puis `CrueltyPower.ModifyVulnerableMultiplier`, puis `DebilitatePower` (`CardRegistryDamageCalc.cs:41-54`). Les sous-couches sont pelées en ordre inverse Debilitate → Cruelty → Phrog → base (`:80-84`).
- `PayoutMultiplierDamage` route vers `DurationLedgers[target][powerId]` en premier (`:180-194`), puis vers `DurationLedgers[dealer][powerId]` (`:196-210`), puis `ConsumableLedgers` (Pen Nib) (`:212-225`), puis `PersistentLedgers` (Strength/Accuracy/PhantomBlades) avec arrondi `Ceiling` et « remainder » sur le dernier (`:227-256`), enfin les reliques via `ResolveRelicLedgerKey` (`:160-166`).
- **Écart code/README à noter** : dans la branche « Target Duration », la boucle paie `payout = remainingToPay` **sans** `Math.Min(remainingToPay, contribution.Amount)` (`:188-190`) — contrairement à la branche `Consumable` qui plafonne (`:219`). Le ledger n'est donc jamais drainé couche par couche : tout le bonus Vulnerable va au **premier** contributeur tant qu'il existe. Le README décrit pourtant un crédit « first in, first out » avec rotation (`README.md:66-68`). À traiter comme divergence à vérifier en jeu, pas comme vérité.

### 5.4 Poison

- Parts par cible : `PoisonShares : Dictionary<Creature, List<Contribution>>` (`CardRegistryPoison.cs:9`).
- Application : `RoutePoisonApplication(target, amount, cardSource)` (`:16-34`) — si un tracker proportionnel est en cours, distribution proportionnelle (`:20-24`), sinon `AddPoisonSharesById(..., GetCurrentSourceId(cardSource))` ; les valeurs négatives décrémentent proportionnellement (`:30-33,80-124`).
- Tick : `PoisonPower.AfterSideTurnStart` Prefix fixe `CurrentPoisonTarget` si la cible n'est pas un joueur, Postfix enveloppe la tâche (`HookPatches.Powers.cs:364-385`), puis `AfterDamageGivenPostfix` route vers `DistributePoisonDamage(target, damageAmount)` (`HookPatches.Cards.cs:231-239`).
- Distribution : `part = totalDamage * (share.Amount / totalShares)` par contributeur (`CardRegistryPoison.cs:126-152`), donc **décimales** assumées (documenté `README.md:70-80`).
- Cas limites : `Accelerant` ne reçoit rien pour le double proc (`README.md:113`).

### 5.5 Forge

- Historique par carte-source : `BladeForgeHistories : Dictionary<CardModel, List<Contribution>>` (`CardRegistryForge.cs:13-31`).
- Application : `Hook.AfterForge(combatState, amount, forger, source)` Postfix → `AddForge` si `source is CardModel`, `HandleFurnaceForge` si `FurnacePower`, `HandleHammerTimeForge` si `HammerTimePower`, `AddForgeById` si `RelicModel`, `AddPotionForge` si `PotionModel` (`HookPatches.Cards.cs:172-202`).
- Distribution : `SplitForgeDamage(...)` (`CardRegistrySovereignBlade.cs:113-158`) — Conqueror est prélevé en premier (`:125-138`), puis un coup « replay » va à la carte qui ajoute le replay (`:142-147`), un coup « spillover » à Seeking Edge (`:148-152`), sinon `AttributeMaxHitDamage` : **10 dégâts de base à Sovereign Blade** (`:167-169`), le reste est distribué FIFO sur l'historique de forge (`:170-176`).
- Cas limites documentés : overkill payé aux premiers forgeurs ; plusieurs Sovereign Blades possibles mais « may be some bugs » (`README.md:83-87`).

### 5.6 Doom

- `DoomHistory : Dictionary<Creature, List<Contribution>>`, `PendingDoomHp : Dictionary<Creature, decimal>` (`CardRegistryDoom.cs:12-15`).
- Application : `RouteDoomApplication` (`:17-63`) ; l'Oblivion passe par `CurrentOblivionContributions` et une répartition proportionnelle (`:57,77-96`).
- Capture HP : `DoomPower.DoomKill` Prefix → `CapturePendingDoomHp` (`HookPatches.Powers.cs:388-392`, `CardRegistryDoom.cs:134-147`).
- Paiement : `Hook.AfterDiedToDoom` → `DistributeDoomDamage` : FIFO sur l'HP capturée, plafonné par `contribution.Amount` (`HookPatches.Powers.cs:394-399`, `CardRegistryDoom.cs:150-191`). Le README précise que le doom ne compte **que** si l'animation tue (`README.md:88-91`).

### 5.7 Orbes

- Channel : `OrbCmd.Channel` Postfix → `RegisterChanneledOrb(orb, CurrentPlayingCard)` (`HookPatches.Orbs.cs:12-16`), stocké dans `OrbChannelers : Dictionary<OrbModel, string>` (`CardRegistryOrbs.cs:14,52-91`) ; DarkOrb a en plus un ledger de vagues (`:84-89,123-172`).
- Passive/Evoke : Prefix des méthodes `LightningOrb/GlassOrb/DarkOrb.Passive|Evoke` (`ModEntry.cs:227-239`) construit un `OrbExecutionContext(orb, isEvoke, expectedValue, forcedActorId)` (`HookPatches.Orbs.cs:57-58,74-77`, type `Contributions/OrbExecutionContext.cs:5-18`).
- Distribution : `DistributeOrbDamage(context, totalDamage, player)` (`CardRegistryOrbs.cs:226-346`) avec répartition en cascade (canaliseur, focus, modificateurs de relique), et log des montants non attribués (`:271,346`).
- Focus : `LogFocusChange/LogFocusChangeById` (`:174-224`), focus temporaire via patches dédiés (`ModEntry.cs:244-250`).
- Cas limites : orbes non attribués si aucun canaliseur (`CardRegistryOrbs.cs:271`).

### 5.8 Cartes générées

`_cardCreatedBy`, `_cardOrigin` (roll-up O(1)), `_cardGeneratedBy`, `_cardGeneratedByImmediate` (`CardRegistry.cs:44-57`) ; tag au moment de l'insertion en pile (`HookPatches.Cards.cs:29-32` → `TagGeneratedCardOnCreation`). Le générateur reçoit les dégâts de ses cartes dans un bucket « generated », et l'UI affiche l'arbre générateur → cartes (`README.md:95-108`).

### 5.9 Export CSV

- Chemin : `user://deck_tracker_exports/card_fights.csv` (`RunExporter.cs:11-12`).
- En-tête (34 colonnes) : `runSeed,runStartedAt,character,ascension,gameVersion,act,actName,floor,combatIndex,encounterId,combatType,turns,damageTaken,combatResult,entityType,name,playerIndex,ownerNetId,floorAdded,copyIndex,upgradeLevel,enchantment,generatedBy,rarity,floorObtained,floorUsed,floorDiscarded,timesDrawn,timesPlayed,playRate,damage,generatedDamage,damageContribPct,rawForge,connectedForge,receivedForge` (`RunExporter.cs:14-18`).
- Écriture : append au **combat end** uniquement, avec un « high-water mark » `log.LastExportedCombatIndex` persisté dans la sauvegarde pour éviter les doublons (`RunExporter.cs:20-58`) ; échappement CSV correct (guillemets doublés si virgule/quote/newline) (`:85-93`). Lignes source : `EntityFightStat` (`RunExport/EntityFightStat.cs:6-43`).

### 5.10 Cas limites documentés (README)

Slow attribué à la carte qui frappe (`README.md:112`) ; Accelerant sans crédit (`:113`) ; replay/forced play au crédit de la carte de base (`:114`) ; upgrade en combat non crédité à l'entité qui upgrade (`:115-116`) ; pertes de focus/force non tracées (`:117-118`) ; génération de potions non tracée (`:119`) ; Chemical X non crédité (`:120`). En plus, côté code : les contributions `PersistentLedgers` utilisent `Ceiling` par part puis « remainder » au dernier (`CardRegistryDamageCalc.cs:246-252`), ce qui peut sur-attribuer de quelques unités.

---

## 6. DAMAGE LOGGER (repo3 / Hsyuan)

- **Capture** : un seul point, `[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]` Postfix avec la signature `(PlayerChoiceContext, CombatState, Creature? dealer, DamageResult results, ValueProp props, Creature target, CardModel? cardSource)` (`DamageLoggerModV2.cs:406-416`). Aucun `Prefix`, aucun `Transpiler`, aucun patch dynamique : `Harmony.PatchAll(...)` une fois (`:34`), instance nommée `codex.sts2.damage.logger.v2` (`:27`).
- **Hooks annexes** (cycle de vie) : `Hook.BeforeCombatStart` (`:342-349`), `NCombatUi.Activate` (`:352-359`), `Hook.AfterCombatEnd` (`:361-368`), `Hook.AfterRoomEntered` (`:370-377`), `Hook.AfterPlayerTurnStart` (`:379-404`).
- **Données écrites** : une ligne `DMG|time=...|runSeed=...|act=...|floor=...|location=...|encounter=...|round=...|player=...|dealerNetId=...|target=...|amount=...|unblocked=...|blocked=...|overkill=...|kill=...|blockBroken=...|source=...|card=...` (`DamageLoggerModV2.cs:426-441`), plus une ligne `ROUND|...` dédupliquée par `runSeed|location|round` (`:379-401`). Échappement : `|`→`/`, CR/LF→espace, vide→`-` (`:72-77`).
- **Affichage** : HUD Godot (`InGameDamageHud.cs`) attaché à `NCombatUi` (§2.3/§3.3) ; agrégation round/combat/run par nom de joueur (`InGameDamageTracker.cs:21-25,143-191`) ; total retenu = `TotalDamage` sinon `UnblockedDamage + BlockedDamage` (`:289-298`).
- **Agrégation** : uniquement les dégâts dont le dealer est un joueur (`InGameDamageTracker.cs:270-277`), repli par nom déjà connu (`:279-286`), état indexé par **nom d'affichage** en `OrdinalIgnoreCase` → deux joueurs homonymes fusionnent (`:43,258-268`).
- **Overlay WPF** : tail du même log (`DamageLogTailService.cs:56-70`), `Dispatcher.InvokeAsync` par ligne (`MainWindow.xaml.cs:53`), logique dupliquée (`OverlayViewModel.cs:148-174`).

---

## 7. Persistance de config

### 7.1 repo1 — `DeckTracker.cfg` à côté de la DLL

- Chemin : `Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "DeckTracker.cfg")` (`SettingsUI/ModSettings.cs:110-113`) → dans le dossier du mod (`mods/<...>/DeckTracker.cfg`), documenté `README.md:13-17`.
- Format : **JSON** malgré l'extension `.cfg`, sérialisé depuis un objet anonyme avec `WriteIndented = true` (`ModSettings.cs:139-163`) ; chargement `JsonSerializer.Deserialize<ModSettings>` (`:115-132`). Tous les champs ont un `[JsonPropertyName]` camelCase (`:26-98`).
- Erreurs sous Linux :
  - `Assembly.GetExecutingAssembly().Location` peut être **vide** selon le mode de chargement ; le `?? ""` produit alors un chemin **relatif** (`DeckTracker.cfg` dans le CWD du jeu). Au `Save()`, `Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath))` reçoit `""` → `ArgumentException` en .NET, avalée par le `catch` → **échec silencieux de sauvegarde** (inférence à partir de `ModSettings.cs:110-113,138` et de la sémantique .NET ; non testé en exécution).
  - Écrire dans le dossier du mod signifie que la config est **écrasée/perdue à chaque mise à jour du mod** (le zip est réextrait).
  - Le build lui-même ne passe pas sous Linux sans édition : `HintPath` et `ModsFolder` sont des chemins Windows absolus (`sts2decktracker.csproj:14-15,24-27`).
- Incohérence documentaire : `CLAUDE.md:71` annonce `%APPDATA%\SlaytheSpire2\DeckTracker.config.json`, ce qui **contredit** le code et le README (`ModSettings.cs:110-113`). Ne pas s'y fier.

### 7.2 repo2 — pas de config ; deux artefacts de données

- **Aucune configuration persistée** : les toggles UI (`_mergeCardVersions`, `_hideZeroDamageCards`, `_thisCombatOnly`, `_showRawForge`, `_includeConnectedForge`, actes, tri) sont des champs statiques initialisés en dur (`DeckTrackerOverlay.cs:44-57,55-57`), modifiés en mémoire (`DeckTrackerOverlay.Toggles.cs:14-60`) et **perdus au redémarrage**.
- **Sauvegarde de run** : `user://deck_tracker_saves/` + `{stem}.json`, max 5 runs (LRU par date d'écriture) (`CardRegistryPersistence.cs:10-11,123-165`) ; JSON via `JsonSerializer` + `SavedStateCtx` source-generated (`:106-109,186-187`).
- **Export** : `user://deck_tracker_exports/card_fights.csv` (`RunExporter.cs:11-12`).
- Sous Linux : `ProjectSettings.GlobalizePath("user://...")` résout le dossier utilisateur du **projet Godot hôte** (le jeu), pas celui du mod ; l'emplacement exact dépend du `config/name`/`use_custom_user_dir` du jeu et **n'est pas dans le dépôt** → `ABSENT`. Ordre de grandeur Godot par défaut : `~/.local/share/godot/app_userdata/<nom du jeu>/`.
- Le build Linux est prévu (`Sts2PathDiscovery.props:31-36`), donc pas d'erreur de compilation spécifique.

### 7.3 repo3 — un seul fichier, chemin Windows

- `Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "SlayTheSpire2", "logs")` + `damage-events.log` (`DamageLoggerModV2.cs:24-26`), créé au démarrage (`:33`).
- Format : texte ligne-à-ligne `TYPE|clé=valeur|...`, UTF-8, `File.AppendAllText` sous `lock` (`:40-46,59-65`) ; parseur tolérant (`DamageLogModels.cs:35-67`).
- Sous Linux : `SpecialFolder.ApplicationData` → `$XDG_CONFIG_HOME`/`~/.config`, donc `~/.config/SlayTheSpire2/logs/damage-events.log` — fonctionne, mais aucun nettoyage/rotation (fichier cumulatif, `:63`). L'overlay desktop, lui, **ne compile pas** sous Linux (`net9.0-windows`, `UseWPF`, `WinExe` : `Sts2DamageOverlay.csproj:3-5`). Chemins Steam `E:\` en dur dans le csproj du mod (`Sts2DamageLoggerV2.csproj:21-29`) et `D:\` dans `publish-mod.ps1:2`.

---

## 8. Réutilisable pour ULTIMA (méthode exacte + adaptation read-only)

### 8.1 Overlay compact

1. **Injection par `NCombatUi._Ready` Postfix + `AddChild`** (repo1, `DeckTrackerInjectionPatch.cs:8-50`) : le plus simple, et le panneau est libéré avec le nœud de combat. Adaptation ULTIMA : ajouter un `CanvasLayer` propre en enfant de `NCombatUi` (`Layer` ~100) plutôt que des `Panel` nus, pour maîtriser le z-order face aux FX.
2. **Alternative sans toucher `NCombatUi`** (repo2, `DeckTrackerOverlay.cs:77-97`) : `new CanvasLayer { Layer = 100 }` + `tree.Root.CallDeferred(Node.MethodName.AddChild, ...)`, création paresseuse au premier `Hook.BeforeCombatStart` (`HookPatches.cs:63-67`). Plus robuste aux refontes de `NCombatUi`, mais il faut gérer soi-même la visibilité hors combat.
3. **Click-through** : `MouseFilter = Ignore` sur la racine + `SetMouseIgnoreRecursive` (repo1, `CardListPanel.cs:178-184`) puis réactivation ciblée des seuls boutons **après** la récursion (`:243-250`). C'est la bonne recette ; repo2 ne l'applique pas.
4. **Drag sans casser le jeu** : `_Input` + `Rect2.HasPoint` + `_dragOffset` (repo1, `:301-319`), ou `GuiInput` sur un `Label` poignée (repo2, `DeckTrackerOverlay.Build.cs:25-28,72-95`). Pour ULTIMA : ajouter `GetViewport().SetInputAsHandled()` quand le drag est actif (les deux dépôts l'omettent) et clamper au viewport comme repo2.
5. **Opacité idle/active** : `_targetOpacity`/`_currentOpacity` + `Mathf.Lerp(..., 5f * delta)` sur `Modulate` (repo1, `:394-399`), avec fade sur chevauchement des intents (`IntentOverlapHelper.cs:8-32`) — directement réutilisable pour ne pas masquer les intentions ennemies.
6. **Hotkeys** : `Input.IsKeyPressed(Key.Bracketleft)` avec front montant (repo2, `:132-157`) est le seul modèle présent ; pour ULTIMA préférer `_UnhandledInput`/`InputMap` pour ne pas poller 3 touches par frame.

### 8.2 Pioche / défausse / main

7. **Chemin d'accès** : `CombatManager.Instance.IsInProgress` → `CombatManager.Instance.DebugOnlyGetState()` → `LocalContext.GetMe(combatState)` → `player.PlayerCombatState.DrawPile` / `.DiscardPile` (repo1, `CardPileWatcher.cs:89-132`). La **main** n'est lu nulle part dans les trois dépôts : `ABSENT` — à trouver dans `PlayerCombatState` (nom exact inconnu ici, ne pas inventer).
8. **Fraîcheur** : événements `CardAdded`, `CardRemoved`, `ContentsChanged` sur `CardPile` (`CardPileWatcher.cs:149-151`) **plus** un resync périodique par comparaison `CurrentPile.Cards.Count != _pileCardSet.Count`, indispensable depuis 0.110 où les déplacements en lot n'émettent que `ContentsChanged` (`:175-184`). Pour la v0.107.1, vérifier si `RemoveInternal(silent:true)` existe : sinon le resync reste utile mais le commentaire ne s'applique pas.
9. **Regroupement** : clé `"{Title}|{IsUpgraded}|{enchant}|{affliction}|{hasTempKeyword}|{energy}|{star}"` (`CardPileWatcher.cs:399-420`) et ordre stable par « zombie » de compteur 0 pour éviter les sauts de ligne (`:236-252`). Réutilisable tel quel pour une liste compacte.
10. **Carte du dessus** : `CardPileCmd.Add` Prefix quand `newPile.Type == PileType.Draw && position == CardPilePosition.Top` (`TopCardTrackingPatches.cs:37-43`), avec report d'identité sur `Transform` (`:67-89`). Optionnel pour ULTIMA (score de run), mais c'est la seule technique « next card » solide des trois dépôts.

### 8.3 Attribution de dégâts par carte

11. **Squelette minimal** : `Hook.ModifyDamage` Postfix pour figer `BaseDamage` + liste des modificateurs (`HookPatches.Cards.cs:92-140`), puis `Hook.AfterDamageGiven` Postfix pour router (`:209-306`). Pour ULTIMA, la version « facile » est de ne garder que `cardSource != null` et d'appeler `CardRegistry.AddDamage(cardSource, results.TotalDamage)` — c'est ce que fait repo3 en practice via `source`/`card` dans le log.
12. **Répartition des buffs** : `ProcessDamageSnapshot` (`CardRegistryDamageCalc.cs:11-153`) est la pièce la plus réutilisable conceptuellement (peeling marginal), mais c'est **du code GPL-free ? non** : repo2 n'a **pas** de licence → **ne pas copier**, réimplémenter l'algorithme : (a) `calc = floor((base + Σadd) * Πmult)`, (b) `overkill = max(0, calc - actual)`, (c) peler chaque mult puis chaque add en ordre inverse et payer la différence, (d) rendre le reste à la carte.
13. **Ledgers** : `DurationLedgers[creature][powerId]` pour Vulnerable/Debilitate (`CardRegistryBuffs.cs:125-168`), `PoisonShares[creature]` avec parts décimales (`CardRegistryPoison.cs:9,126-152`), `BladeForgeHistories[card]` + base 10 pour Sovereign Blade (`CardRegistryForge.cs:13-31`, `CardRegistrySovereignBlade.cs:167-169`), `DoomHistory`/`PendingDoomHp` (`CardRegistryDoom.cs:12-15,134-191`), `OrbChannelers` (`CardRegistryOrbs.cs:14,52-91`).
14. **Identité de carte stable** : `"{baseId}_F{floorAdded}_C{copyIndex}_U{upgradeLevel}_{enchant}_P{owner}"` (`CardRegistryCardIdentity.cs:381-398`) — indispensable pour agréger un score de run sans mélanger deux copies.
15. **Export** : le schéma `card_fights.csv` (`RunExporter.cs:14-18`) est un bon contrat de données pour le futur score de run ; écriture append + high-water mark (`:22-58`) évite les doublons après reprise.

### 8.4 Ce qu'ULTIMA doit faire autrement (read-only strict)

- Ne jamais écrire dans les objets du jeu : repo1 écrit uniquement dans ses propres champs et dans son JSON ; repo2 ne modifie que ses ledgers (`CardRegistry.cs:7-141`) et journalise via `GD.Print` (`Logging/Log.cs:24-41`). Aucun des trois ne modifie `DamageResult` ni les piles.
- Envelopper **chaque** hook dans un `try/catch` : modèle repo2 `Guard(nameof(X), () => ...)` (`HookPatches.cs:17-27`), motif à reprendre tel quel (c'est une idée, pas du code copié).
- Ne pas dépendre de `damage-events.log` ou d'un fichier externe pour l'UI : repo3 duplique l'agrégation dans WPF (`OverlayViewModel.cs:148-174`) → source de divergence.

---

## 9. Anti-patterns et bugs visibles

**repo1**
- Nettoyage uniquement sur `OnCombatWon` (`DeckTrackerInjectionPatch.cs:164-183`) ; aucun patch sur la défaite, et références statiques non nullifiées après `QueueFree` (`:11-13`).
- `OnCombatWon` ciblé par **chaîne** (`"OnCombatWon"`, `:164`) : casse silencieuse si la méthode est renommée, contrairement aux autres patches en `nameof`.
- Le drag ne consomme pas l'événement : le clic peut atteindre l'UI du jeu sous le panneau (`CardListPanel.cs:301-319`, aucun `SetInputAsHandled`).
- Le retour à `ActiveOpacity` n'existe que dans `OnPileNeedsFullRebuild` (`:421-429`) ; les mises à jour incrémentales (`OnGroupCountChanged`, `:432-461`) ne réarment pas `_timeSinceLastChange` → le panneau peut rester à l'opacité idle malgré des changements.
- `_targetOpacity`/`_currentOpacity` réinitialisés à chaque `SetSettings` (`:87-95`) : un `ApplySettings` en combat fait clignoter l'opacité.
- Config : JSON dans un `.cfg` (`ModSettings.cs:110-113,162-163`), écrite dans le dossier du mod (perdue à la mise à jour) ; `ModSettings.ResetToDefaults` remet `DiscardPileX = 1670` (`:175`) alors que le défaut est `1660` (`:33`).
- Docs contradictoires sur le chemin de config (`CLAUDE.md:71` vs `ModSettings.cs:110-113`).
- Détection de pouvoir par réflexion à chaque événement, avec cache par `Type` (`CardPileWatcher.cs:307-326`) : coûteux et fragile.
- `GroupKey` peut throw sur une carte partiellement initialisée ; corrigé par `try/catch` + clé de secours (`:399-420`), mais le `catch` masque la cause.
- `new System.Random()` à chaque rebuild pour le tri aléatoire (`CardListPanel.cs:556`) → ordre non reproductible entre deux ouvertures.
- `SetMouseIgnoreRecursiveExceptHover` reparcours tout l'arbre à chaque `UpdateCardList` (`:658`, définition `:1121-1127`) — coût O(n) par mise à jour.

**repo2**
- Aucun click-through : `PanelContainer` sans `MouseFilter` (`DeckTrackerOverlay.Build.cs:9,100`) → le petit panneau bloque les clics sur sa surface ; seul le drag par le titre est prévu.
- Rebuild complet de l'UI à chaque publish : détache + `QueueFree` de tous les enfants (`DeckTrackerOverlay.cs:188-189`, `RenderCards.cs`), allocations par frame ; atténué par `DrainPendingSnapshot` une fois par frame (`CardRegistryPersistence.cs:209-237`).
- Toggles non persistés (`DeckTrackerOverlay.Toggles.cs:14-60`) → état perdu à chaque lancement.
- `EnemyDebuffLedgers` déclaré et `Reset` mais **jamais alimenté** (`CardRegistryBuffs.cs:42,102`) : code mort qui suggère une refonte inachevée (le ledger réel est `DurationLedgers`, `:125`).
- Branche « Target Duration » de `PayoutMultiplierDamage` sans `Math.Min` (`CardRegistryDamageCalc.cs:188-190`) → pas de rotation FIFO réelle, contrairement au README (`README.md:66-68`).
- `Log.cs:10` dit « press J in-game » alors que la touche réelle est `\` (`DeckTrackerOverlay.cs:150`) — doc interne fausse.
- Chemins `user://` : le dossier exact dépend du projet hôte, non documenté (`ABSENT`).
- `GameRelease.Version` avale toute exception et renvoie `"unknown"` (`GameRelease.cs:13-23`) : perte d'information silencieuse dans le CSV.
- Beaucoup de patches par type concret (`ModEntry.cs:88-250`) : très sensible aux renommages de classes du jeu ; une seule méthode introuvable fait échouer le patch individuel (pas tout `PatchAll`, ici c'est manuel — bon point).
- `decimal` partout avec arrondis `Ceiling` par part (`:246`) : sur-attribution possible de quelques points par coup.

**repo3**
- I/O disque bloquante sur le thread de jeu à chaque dégât, sous `lock` (`DamageLoggerModV2.cs:40-46,59-65`) ; fichier cumulatif sans rotation.
- `EnsureHudExists()` vide, appelée 7 fois (`InGameDamageTracker.cs:324-327`) ; `Detach()` jamais appelé (`:87-97`) → pas de destruction propre du HUD.
- Polling clavier 20 Hz (`InGameDamageHud.cs:89-96,228-239`) au lieu de `_Input`.
- Aucun `try/catch` dans `InGameDamageTracker.cs`/`InGameDamageHud.cs` → une exception dans un Postfix remonte dans le code du jeu.
- Agrégation par **nom d'affichage** (`OrdinalIgnoreCase`) : homonymes fusionnés (`:43,258-268`).
- Overlay WPF Windows-only et logique dupliquée (`Sts2DamageOverlay.csproj:4-5`, `OverlayViewModel.cs:148-174`) ; libellés chinois codés en dur.
- Pas de gestion de ligne partielle en fin de fichier dans le tail (`DamageLogTailService.cs:65-70`).
- `NCombatUi.Activate` : paramètre `state` inutilisé (`DamageLoggerModV2.cs:355-357`).

---

## 10. Tableau final — technique → dépôt → difficulté de réimplémentation

| Technique | Dépôt d'origine | Difficulté |
|---|---|---|
| Panneau ajouté en enfant de `NCombatUi` via `_Ready` Postfix | repo1 (`DeckTrackerInjectionPatch.cs:8-50`) | facile |
| `CanvasLayer` racine + `SceneTree.Root.AddChild` différé | repo2 (`DeckTrackerOverlay.cs:77-97`) | facile |
| HUD attaché à `NCombatUi.Activate` + re-parentage | repo3 (`DamageLoggerModV2.cs:352-359`, `InGameDamageTracker.cs:70-82`) | facile |
| Masquage/`QueueFree` des panneaux à la fin du combat | repo1 (`:164-183`) | facile |
| Click-through par `MouseFilter=Ignore` récursif + boutons réactivés | repo1 (`CardListPanel.cs:178-184,243-250`) | facile |
| Drag par `_Input` + offset + clamp viewport | repo1 + repo2 (`:301-319`, `DeckTrackerOverlay.Build.cs:72-95`) | facile |
| Fade idle/active sur `Modulate` + détection de chevauchement des intents | repo1 (`:394-416`, `IntentOverlapHelper.cs:8-32`) | moyen |
| Panneau redimensionnable par réglages (pas de poignée) | repo1 (`ModSettings.cs:100-108`, `CardListPanel.cs:1189-1211`) | facile |
| Colonnes triables + lignes `HBoxContainer` | repo2 (`DeckTrackerOverlay.RenderCards.cs:87-99,184-208,294-362`) | moyen |
| Lecture pioche/défausse (`PlayerCombatState.DrawPile/DiscardPile`) | repo1 (`CardPileWatcher.cs:89-132`) | facile |
| Resync de pile après déplacements silencieux (`ContentsChanged`) | repo1 (`:175-184`) | moyen |
| Suivi de la carte du dessus (`CardPileCmd.Add` prefix + `Transform`) | repo1 (`TopCardTrackingPatches.cs:19-89`) | dur |
| HP / block / pouvoirs (`CurrentHp`, `Block`, `GetPower<T>`) | repo2 (`CardRegistryDoom.cs:140`, `CardRegistryForcedBlockLoss.cs:23`, `CardRegistryDamageCalc.cs:43-46`) | facile |
| Identité de carte stable (tracking id) | repo2 (`CardRegistryCardIdentity.cs:381-398`) | moyen |
| Capture `ModifyDamage` → snapshot des modificateurs | repo2 (`HookPatches.Cards.cs:92-140`) | dur |
| Peeling marginal add/mult (dont Vulnerable composite) | repo2 (`CardRegistryDamageCalc.cs:11-153`) | dur |
| Ledger FIFO Vulnerable/Debilitate + paiement | repo2 (`CardRegistryBuffs.cs:125-168`, `:180-194`) | moyen |
| Poison proportionnel décimal + décroissance | repo2 (`CardRegistryPoison.cs:16-152`) | moyen |
| Forge FIFO + base 10 Sovereign Blade | repo2 (`CardRegistryForge.cs:13-31`, `CardRegistrySovereignBlade.cs:113-176`) | dur |
| Doom FIFO sur HP capturée | repo2 (`CardRegistryDoom.cs:134-191`) | dur |
| Orbes : canaliseur, focus, évocations multiples | repo2 (`CardRegistryOrbs.cs:52-346`) | très dur |
| Cartes générées + roll-up vers le générateur | repo2 (`CardRegistry.cs:44-57`, `HookPatches.Cards.cs:29-32`) | dur |
| Export CSV par combat (append + high-water mark) | repo2 (`RunExporter.cs:14-58`) | moyen |
| Journal pipe clé=valeur + parseur tolérant | repo3 (`DamageLoggerModV2.cs:426-441`, `DamageLogModels.cs:35-67`) | facile |
| HUD à lignes fixes + `Timer` de poll clavier | repo3 (`InGameDamageHud.cs:161-239`) | facile |
| Config JSON à côté de la DLL | repo1 (`ModSettings.cs:110-169`) | facile (mais chemin à revoir) |
| Sauvegarde de run JSON + LRU | repo2 (`CardRegistryPersistence.cs:76-165`) | moyen |

### Notes de fiabilité
- Tout ce qui précède vient de lecture statique ; **rien n'a été exécuté** (aucun build, aucun lancement du jeu).
- Les points marqués « inférence » (nettoyage à la défaite repo1, échec de `Save()` si `Assembly.Location` est vide) sont des déductions de code, pas des observations.
- `ABSENT` est utilisé au sens strict : l'information n'existe pas dans le dépôt, elle n'a pas été devinée.
