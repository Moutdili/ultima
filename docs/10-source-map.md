# 10 — Carte du code source (v0.107.1)

Source original : `~/sts2/game/pck/src/Core` (extrait du `.pck`, shippé par MegaCrit).
Comptage réel des fichiers `.cs` par répertoire.

## Volumétrie par module

| Module | Fichiers .cs | Rôle |
|---|---:|---|
| `Models/` | 1688 | tout le contenu : cartes, monstres, pouvoirs, reliques, potions, events, acts |
| `Nodes/` | 689 | UI Godot (combat, cartes, map, menus, VFX) |
| `Multiplayer/` | 147 | messages, sérialisation, transport Steam/ENet, sync |
| `Entities/` | 125 | données runtime : `Creature`, `CardModel`, `CardPlay`, `Player` |
| `Saves/` | 106 | sauvegardes, profils, migration |
| `Timeline/` | 74 | progression / epochs |
| `Localization/` | 58 | chargement des tables de langue |
| `DevConsole/` | 49 | console de dev (commandes) |
| `Runs/` | 39 | `RunManager`, `RunState` (point d'accès `NetService`) |
| `GameActions/` | 38 | `PlayerChoiceContext`, actions de jeu, multi |
| `AutoSlay/` | 31 | mode auto (bot interne !) |
| `Platform/` | 28 | Steam, plateformes, null-impl |
| `Combat/` | 27 | état de combat, tours, **`History/`** |
| `Helpers/` | 26 | utilitaires, `ReflectionHelper` |
| `MonsterMoves/` | 24 | intents + state machine des moves |
| `Commands/` | 22 | API d'action (`CardPileCmd`, `DamageCmd`, `PowerCmd`…) |
| `Map/` | 16 | graphe de map, génération, nodes |
| `Modding/` | 15 | `ModInitializer`, `ModHelper`, manifest |
| `Odds/` | 6 | probabilités (raretés, récompenses) |
| `ValueProps/` | 4 | `DamageProps` (ValueProp prédéfinis) |

## Où trouver quoi (chemins vérifiés)

### Combat / dégâts
```
Core/MonsterMoves/Intents/AbstractIntent.cs
Core/MonsterMoves/Intents/AttackIntent.cs        DamageCalc, Repeats, GetSingleDamage
Core/MonsterMoves/Intents/SingleAttackIntent.cs
Core/MonsterMoves/Intents/MultiAttackIntent.cs   GetTotalDamage = single * repeats
Core/Hooks/Hook.cs:1486                          ModifyDamage(...)
Core/Hooks/ModifyDamageHookType.cs               Additive / Multiplicative / Cap
Core/Models/Powers/WeakPower.cs                  ×0.75 (+ PaperKrane, Debilitate)
Core/Models/Creature/… (via Entities/Creatures)  HP, block, powers
Core/Combat/History/CombatHistory.cs             CardPlaysStarted/Finished, Changed
```

### Cartes
```
Core/Models/Cards/*.cs                 578 cartes (1 fichier = 1 carte)
Core/Models/CardModel.cs               ShouldGlowGold/Red, GetDescriptionForPile
Core/Entities/Cards/CardPlay.cs        PlayIndex, IsAutoPlay, ResultPile, Resources
Core/Entities/Cards/CardPreviewMode.cs None / Normal / Upgrade / MultiCreatureTargeting
Core/Commands/CardPileCmd.cs           Add/Draw/Discard (API d'ACTION, interdite en read-only)
```

### Monstres
```
Core/Models/Monsters/**/*.cs           123 monstres, MinInitialHp/MaxInitialHp
                                       (souvent AscensionHelper.GetValueIfAscension(...))
Core/MonsterMoves/MonsterMoveStateMachine/   MoveState, FollowUpState
```
Les dégâts des moves sont souvent des propriétés (`KillshotDamage`) résolues par
`AscensionHelper.GetValueIfAscension(level, valeurAscension, valeurNormale)`.
→ `data/catalog/monsters.json` contient déjà les valeurs résolues.

### Multiplayer
```
Core/Multiplayer/Serialization/INetMessage.cs      contrat des messages
Core/Multiplayer/Serialization/MessageTypes.cs     IDs, + sous-types des MODS
Core/Multiplayer/NetMessageBus.cs                  Serialize/Register/Deserialize
Core/Multiplayer/NetHostGameService.cs             SendMessage (broadcast)
Core/Multiplayer/NetClientGameService.cs           SendMessage (→ host)
Core/Multiplayer/Game/PeerInput/                   intentions des autres joueurs
Core/Runs/RunManager.cs:121                        NetService (accès global)
```

### UI Godot
```
Core/Nodes/Combat/NCombatUi.cs         cible d'injection (_Ready, OnCombatWon)
Core/Nodes/Combat/NCombatRoom.cs       %CombatUi, création par combat
Core/Nodes/Combat/NIntent.cs           rendu des intents
Core/Nodes/Combat/NCardPlay.cs         preview de carte jouée
Core/Nodes/Cards/…                     cartes, piles, tooltips
Core/Nodes/Screens/Map/…               écran de map (pour F6)
```

### Map / progression / probabilités
```
Core/Map/…            graphe, nodes, types de salles
Core/Odds/…           probabilités de rareté / récompenses
Core/Rewards/…        récompenses proposées
Core/Timeline/…       epochs / unlocks
```

### Modding
```
Core/Modding/ModInitializerAttribute.cs
Core/Modding/ModHelper.cs        SubscribeForRunStateHooks / SubscribeForCombatStateHooks
```
Le loader scanne `<jeu>/mods/**/*.json`, charge `<id>.dll` et `<id>.pck`.

## Astuce de recherche

```bash
cd ~/sts2/game/pck/src/Core
rg "class NCombatUi" .
rg "ModifyDamageMultiplicative" -n
rg "new MultiAttackIntent" Models/Monsters | head
```

## Ce qui est surprenant (et utile)

- **`AutoSlay/` (31 fichiers)** : le jeu embarque un mode de jeu automatique. Excellente
  source d'exemples d'API « jouer une carte » — mais **interdit** pour ULTIMA (read-only).
- **`DevConsole/` (49 fichiers)** : commandes de debug, utile pour tester des scénarios.
- **`TestSupport/`** : helpers de test du jeu, potentiellement réutilisables pour mocker
  un combat sans lancer le jeu.
- Le jeu ship `sts2.csproj`, `sts2.sln` et `project.godot` : on peut ouvrir le projet
  dans Rider/Godot pour naviguer, mais **ne pas committer** ces fichiers (IP MegaCrit).
