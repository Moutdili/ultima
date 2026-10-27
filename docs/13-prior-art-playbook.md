# 13 — Playbook prior art (ce que tu réutilises, et comment)

Synthèse des 5 audits de dépôts (2 548 lignes de notes citées fichier:ligne dans
`research/notes/`). **Lis ce document avant de coder : tout ce qui suit est vérifié dans
le code des dépôts, pas supposé.** Les détails complets sont dans les notes.

Sources :
- `research/notes/sts2mcp.md` (768 l.) — lecture d'état
- `research/notes/trackers-ui.md` (393 l.) — UI, piles, attribution
- `research/notes/multiplayer.md` (408 l.) — réseau, ce qui est synchronisé
- `research/notes/overlays-templates.md` (433 l.) — badges, saves, packaging
- `research/notes/combatsolver.md` (546 l.) — solveur, risque

---

## 0. CORRECTIONS CRITIQUES (issues des audits — lis-les en premier)

C1. **Aucun `.json` dans `mods/` autre que le manifest.** Le loader désérialise *tout* `.json`
    sous `mods/` comme un manifeste. La config d'ULTIMA doit être en `.cfg`/`.ini`, ou dans un
    sous-dossier non scanné. (`research/notes/overlays-templates.md`)

C2. **Les IDs de `INetMessage` dépendent de l'ensemble des types chargés** (dérivés du FullName,
    ordre stable seulement à mod-set identique). BaseLib contourne ça en réécrivant
    `MessageTypes._cache` avec des IDs fixes 128-254 (`CustomMessageWrapper`).
    → Conséquence : handshake `UltimaHello` obligatoire, et **même mod-set des deux côtés**
    pour le broadcast. Ne jamais supposer qu'un pair sans ULTIMA comprendra l'ID.

C3. **STS2MCP n'est PAS read-only côté GET** : `OpenInventory()` (`StateBuilder.cs:552`),
    `chestButton.ForceClick()` (`:2306`), `merchantButton.ForceClick()` (`:1475`) mutent l'état
    pendant la construction du JSON. Ne jamais reprendre ces lignes.

C4. **STS2MCP n'utilise pas `AttackIntent`** : `DamageCalc`, `Repeats`, `GetSingleDamage` sont
    ABSENTS de son code (il ne lit que le label). Notre lecture typée vient du **source du jeu**
    (docs/05), pas de STS2MCP. Le « 60 % du travail » ne concerne que HP/block/pouvoirs/piles.

C5. **Versions de jeu différentes selon les dépôts** :
    - 999dulgi : écrit pour **≥ 0.110** (`CardPileWatcher.cs:175`) → plus récent que notre 0.107.1.
    - ronishae : déclare `min_game_version: 0.107.1` → aligné sur nous.
    - CombatSolver : `min_game_version 0.111.0` → ses APIs ne sont pas garanties chez nous.
    → Tout pattern repris de 999dulgi ou CombatSolver doit être revalidé sur 0.107.1.

C6. **Pipeline réel confirmé** (CombatSolver, `CorePowerSupport.cs:717-732`) :
    enchant → additif → multiplicatif → cap → `BeforeDamageReceived` → block → `ModifyHpLost`
    → `LoseHp`. Weak et Vulnerable commutent (decimal), le block vient **strictement après**.
    `IntentForecaster.Build` produit déjà un `ForecastAttackHit(Damage, BaseDamage)` par coup :
    c'est exactement la structure de F2.

C7. **Licences corrigées** : `STS2-DamageTracker` = MIT ; `Slay-the-Spire-2-Damage-Logger` = GPL-3.0
    (à éviter) ; freude916/999dulgi/ronishae/advisor = aucune licence → réimplémenter.

---

## 0bis. État du repo (ne pas refaire)

- `Ultima.Core` : `LethalIndex` + `DamageMath` **écrits et testés (12 tests verts)**.
- `Ultima/Net` : `UltimaPingMessage` + `NetBridge` — **compilent**, premier test live à faire.
- `Ultima/Overlay` : V0.1 (F7 ping, F8 hide).
- Container + CI + Makefile + `tools/doctor.sh` : opérationnels.
- Catalogue : `data/catalog/*.json` (578 cartes, 123 monstres, 34 cartes prioritaires).

---

## 1. F1/F2 — lire les intents et les dégâts

### Ce que fait STS2MCP (à reprendre, ~60 % du travail de lecture)

| Méthode | Fichier:ligne | Ce qu'elle donne |
|---|---|---|
| `BuildEnemyState(Creature, Dictionary<string,int>)` | `StateBuilder.cs:1320` | nom, HP/max, block, pouvoirs, intents |
| `BuildPlayerState(Player)` | `StateBuilder.cs:1092` | HP, block, énergie, main, reliques, potions |
| `BuildBattleState(RunState, CombatRoom)` | `StateBuilder.cs:1062` | état de combat agrégé |
| `BuildPowersState(Creature)` | `StateBuilder.cs:2355` | pouvoirs avec stacks |
| `BuildPileCardList(...)` | `StateBuilder.cs:1303` | cartes d'une pile |

Extrait exact de la lecture d'intent (`StateBuilder.cs:1342-1371`) :

```csharp
foreach (var intent in moveState.Intents)
{
    ["type"] = intent.IntentType.ToString();
    string label = intent.GetIntentLabel(targets, creature).GetFormattedText();
    // ... texte localisé, dégâts inclus
}
```

**Conséquence pour ULTIMA** : `GetIntentLabel(...)` donne le texte que le joueur voit.
C'est **l'oracle de test** de F1 : si ton calcul ne reproduit pas ce label pour l'état
courant, ton pipeline est faux. Utilise-le comme test de non-régression, jamais comme donnée.

### Chemins de dégâts (rappel vérifié dans le jeu)

- `AttackIntent.GetSingleDamage(targets, owner)` applique **déjà** tout le pipeline
  (`Hook.ModifyDamage(...)`) du point de vue du joueur local.
- Weak = ×0.75 multiplicatif (`WeakPower.cs`), modulé par `PaperKrane` et `DebilitatePower`.
- Ne pas re-multiplier si le dealer a déjà `WeakPower`.

### Pièges de lecture (payés par STS2MCP)

1. **`draw_pile` n'est pas l'ordre de pioche** : tri par rareté puis id (`StateBuilder.cs:1133`).
2. **Les cartes de pile n'ont pas d'`id`** dans `BuildPileCardList` (`:1309`) → passer par `BuildCardInfo`.
3. **Pas de deck hors combat** : tout le bloc piles est sous `CombatManager.Instance.IsInProgress` (`:1106`).
4. **`entity_id` instable** (`{baseId}_{n}`, compteur des vivants, `:1326`) → utiliser **`creature.CombatId`** (`:1334`).
5. **`Energy`/`MaxEnergy` null-ref hors combat** (`:1103`).
6. **`GetDescriptionForPile(pile)`** change les nombres affichés selon la pile (`Helpers.cs:48`).
7. **`FindAllSortedByPosition` obligatoire** : `NGridCardHolder.OnFocus()` appelle `MoveToFront()`
   et casse l'ordre des enfants (`Helpers.cs:211-216`).
8. **`IsLiveNode`** avant de toucher un node : `IsInstanceValid && !IsQueuedForDeletion` (`Helpers.cs:308`).
9. **`RunOnMainThread`** obligatoire si tu lis depuis un autre thread (`McpMod.cs:127-158`).
10. **`card.CanPlay(out reason, out _)`** = seul test fiable de jouabilité (`StateBuilder.cs:1259`).
11. **Exceptions avalées partout** dans STS2MCP (`catch {}`) : un champ absent peut être une exception. Logge.

### Helpers réutilisables (à réimplémenter, 15-30 lignes chacun)

- `SafeGetText` / `StripRichTextTags` / `SafeGetCardDescription` (`Helpers.cs:84,98,48`).
- `GetInstanceFieldValue` (`Helpers.cs:163-179`) : réflexion sur champs privés Godot
  (`DeclaredOnly` + remontée de `BaseType`).
- `FindAll` / `FindFirst` / `IsNodeVisible` (`Helpers.cs:203,284,320`).

---

## 2. F3 — cartes à payoff conditionnel

Deux signaux, tous deux vérifiés dans le **source du jeu** :

1. `CardModel.ShouldGlowGold` (`CardModel.cs:830-864`) : vrai = le bonus conditionnel est
   actif **maintenant**. 31 cartes overrident `ShouldGlowGoldInternal` / `ShouldGlowRedInternal`.
2. Historique : `CombatManager.Instance.History.CardPlaysFinished.Any(e => e.CardPlay.Card == this && e.HappenedThisTurn(combatState))`
   (pattern utilisé par `Models/Cards/Fetch.cs`).

Catalogue généré : `data/catalog/priority_cards.json` (34 entrées, champ `priority_candidate`).

⚠️ Aucun dépôt public n'utilise ces deux signaux pour une alerte : c'est du neuf.
Teste les deux et garde celui qui est stable.

---

## 3. F3/F4 — multi : ce qui est synchronisé, ce qui ne l'est pas

### Prouvé par l'usage des mods existants

- `RunManager.Instance.NetService.Type` (`NetGameType` : None/Singleplayer/Host/Client/Replay).
- Les dégâts et l'état des autres joueurs sont lisibles **sans réseau custom** :
  `BAIGUANGMEI/STS2-DamageTracker` et `freude916/sts2-multiplayer-damage-stat` n'ont
  **aucun** `INetMessage` / `SendMessage` / `RegisterMessageHandler` (grep exhaustif sur 16 dépôts).
- `freude916` s'accroche à `NMultiplayerPlayerState` + champs privés via réflexion
  (`NMultiplayerPlayerStatePatch.cs:8-40`).

### Vérifié dans le source du jeu (mais utilisé par aucun mod public → à valider en jeu)

```csharp
// Runs/RunManager.cs:155 (propriété publique)
public HoveredModelTracker HoveredModelTracker { get; private set; }

// Multiplayer/Game/PeerInput/HoveredModelTracker.cs
public AbstractModel? GetHoveredModel(ulong playerId);
public event Action<ulong>? HoverChanged;
```

`NPlayerHand` alimente ce tracker à chaque survol (`NPlayerHand.cs:382,389`), et
`NMultiplayerPlayerIntentHandler` le consomme pour afficher l'intent distant (`:163`).
→ **Voie A de F3** : s'abonner à `HoverChanged`, filtrer sur le catalogue prioritaire.

### Voie B (message custom) — notre POC

`UltimaPingMessage` + `NetBridge` compilent. Contrat vérifié :

```csharp
public interface INetMessage : IPacketSerializable
{
    bool ShouldBroadcast { get; }   // le host relaie
    NetTransferMode Mode { get; }   // Reliable / Unreliable / None
    LogLevel LogLevel { get; }
    bool ShouldBuffer { get; }
    void Serialize(PacketWriter writer);
    void Deserialize(PacketReader reader);
}
// PacketWriter/Reader : WriteInt/ReadInt, WriteULong/ReadULong, WriteString/ReadString,
// WriteBool/ReadBool, WriteEnum<T>/ReadEnum<T>, WriteList<T>/ReadList<T>.
```

Envoi : `RunManager.Instance.NetService.SendMessage(msg)`.
Réception : `net.RegisterMessageHandler<T>(handler)` (délégué `(T message, ulong senderId)`).

**Risques** : aucun précédent public ; `MessageTypes.Initialize()` construit le cache d'IDs
au démarrage → le mod doit être chargé **avant** (c'est notre cas). Un client vanilla ne
connaît pas l'ID → handshake obligatoire (`UltimaHello`) avant tout broadcast.

### Pièges de désérialisation

- Toujours `ShouldBuffer = true` sauf raison forte.
- `Mode = Reliable` pour les alertes (jamais `Unreliable` pour un état).
- Inclure une **version** dans chaque message et jeter les versions majeures différentes.
- `TurnNumber` obligatoire : jeter les messages périmés.

---

## 4. F5 — attribution de dégâts (ronishae)

Le plus complet. Techniques par difficulté (extrait de `trackers-ui.md` §10) :

| Technique | Fichier | Difficulté |
|---|---|---|
| Identité de carte stable (tracking id) | `CardRegistryCardIdentity.cs:381` | moyen |
| Capture `ModifyDamage` → snapshot des modificateurs | `HookPatches.Cards.cs:92-140` | dur |
| Peeling marginal add/mult (Vulnerable composite) | `CardRegistryDamageCalc.cs:11-153` | dur |
| Ledger FIFO Vulnerable/Debilitate | `CardRegistryBuffs.cs:125-194` | moyen |
| Poison proportionnel décimal | `CardRegistryPoison.cs:16-152` | moyen |
| Forge FIFO + base 10 | `CardRegistryForge.cs:13`, `CardRegistrySovereignBlade.cs:113` | dur |
| Doom FIFO | `CardRegistryDoom.cs:134-191` | dur |
| Orbes (canaliseur, focus) | `CardRegistryOrbs.cs:52-346` | très dur |
| Export CSV par combat | `RunExporter.cs:14-58` | moyen |

**Conseil V1** : ne pas viser la parité. Faire « dégâts directs + Vulnerable FIFO + poison
proportionnel » et **lister explicitement** ce qui n'est pas attribué (forge, orbes, doom).
Un chiffre honnête vaut mieux qu'un chiffre complet et faux.

---

## 5. F6 — map

`BuildMapState` / `BuildMapNode` (`StateBuilder.cs:1673`, `:1807`) donnent le DAG complet :
nœuds avec `children` en `[col,row]`, position courante, nœuds `Travelable`, identité du boss.
→ F6 = un calcul sur ce graphe, pas une reconstruction.

---

## 6. UI — patterns exacts

| Besoin | Pattern vérifié | Source |
|---|---|---|
| Injecter un panneau | Postfix `NCombatUi._Ready` + `AddChild` | `DeckTrackerInjectionPatch.cs:8-50` |
| Overlay global (hors combat) | `CanvasLayer` + `SceneTree.Root.AddChild` différé | `DeckTrackerOverlay.cs:77-97` |
| Click-through | `MouseFilter=Ignore` récursif, puis réactiver les boutons | `CardListPanel.cs:178-184,243-250` |
| Drag | `_Input` + offset + clamp viewport | `CardListPanel.cs:301-319` |
| Fade au repos | `Modulate` + timer, avec détection de chevauchement des intents | `CardListPanel.cs:394-416`, `IntentOverlapHelper.cs:8-32` |
| Scroll sans scrollbar | `GetVScrollBar().Modulate = Transparent` en `CallDeferred` | `CardListPanel.cs` |
| Resync piles | s'abonner à `ContentsChanged` (les déplacements silencieux existent) | `CardPileWatcher.cs:175-184` |
| Top card | Prefix `CardPileCmd.Add` + `Transform` | `TopCardTrackingPatches.cs:19-89` (difficile) |
| Config | JSON à côté de la DLL (jamais `%APPDATA%`) | `ModSettings.cs:110-169` |

**Anti-patterns vus** : polling clavier 20 Hz au lieu de `_Input` (`InGameDamageHud.cs:89`) ;
aucun try/catch dans un Postfix (`InGameDamageTracker.cs`) ; agrégation par nom d'affichage
(homonymes fusionnés, `:43`) ; UI WPF Windows-only (`Sts2DamageOverlay.csproj:4`).

---

## 7. Packaging sans Godot éditeur

- `godot/` pour les ressources, `build/mods/<Id>/` pour le staging, manifest `<Id>.json`.
- `.pck` fabricable avec `gdre_tools --headless --pck-create=<dir> --pck-version=2 --pck-engine-version=4.5.1`.
- `Microsoft.NET.Sdk` + références `Private=false` suffit (pas besoin de Godot.NET.Sdk).
  Vérifié : notre scaffold compile en 0 warning.

---

## 8. Licences (corrigé par l'audit)

| Dépôt | Licence | Réutilisation |
|---|---|---|
| STS2MCP | MIT (© Yikun Ji) | autorisée avec notice |
| **STS2-DamageTracker** (BAIGUANGMEI) | **MIT** | autorisée avec notice |
| CombatSolver | MIT (© Torch1230) | autorisée avec notice |
| spirescope | MIT | autorisée |
| BaseLib-StS2 | MIT | autorisée |
| Modding-Tutorial | MIT | autorisée |
| sts2-tierlist-overlay | MIT | autorisée |
| STS2-ViewedCardsStatistics | MIT | autorisée |
| **Slay-the-Spire-2-Damage-Logger** | **GPL** | ⚠️ copyleft : ne pas lier/copier |
| sts2-multiplayer-damage-stat | aucune | réimplémenter |
| sts2decktracker, sts2-deck-tracker, sts2-advisor, ModTemplate | aucune | réimplémenter |

**Règle projet** : même sous MIT, on réimplémente à partir de la compréhension. On ne copie
pas de bloc de code d'un dépôt tiers dans `Ultima/` sans décision explicite + notice.

---

## 9. Oracles de test (le plus important)

1. **Label d'intent du jeu** = vérité pour F1. Compare, ne devine pas.
2. **`ShouldGlowGold`** = vérité pour F3 (le jeu sait déjà).
3. **Log** = vérité pour le réseau (chaque étape loggée).
4. **CSV** = vérité pour F5 (réconciliation des totaux).
5. **`doctor.sh`** = vérité pour l'environnement (14 checks).

Si un de ces oracles contredit ton calcul, **c'est ton calcul qui est faux**.
