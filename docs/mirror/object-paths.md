# Miroir mémoire — carte des chemins d'objets (S2, statique)

> Agent B « Le Miroir ». Établi **sans le jeu** le 2026-10-28, à partir du source livré
> (`~/sts2/game/pck/src/Core`, v0.107.1, commit 59260271) et de la **métadonnée** de `sts2.dll`
> (sha256 `d13f3f15…bd03`, MVID `64622899-e36c-49f2-b463-1b26ad68cbd5`).
> Source unique de vérité dans le code : `Ultima.Mirror/src/Ultima.Mirror/ObjectPaths.cs`.

## Comment c'est prouvé

| Preuve | Commande | Résultat |
|---|---|---|
| Chaque champ existe dans `sts2.dll` avec le bon nom réel et le bon genre (statique/instance) | `ultima-mirror layout` (System.Reflection.Metadata : la DLL n'est ni chargée ni exécutée) | **56/56 OK** |
| Chaque `fichier:ligne` ci-dessous déclare bien ce membre | script python de contrôle (ligne lue == nom du membre) | **56 vérifiés, 0 écart** |
| MCP `rea` `inspect_managed_members` | sur `sts2.dll` | **inutilisable** : « Managed metadata exceeds the 67108864 byte representation budget », 0 champ décodé → remplacé par `layout` |

Conventions : `K(X)` = `<X>k__BackingField` (propriété auto). `A+B` = type imbriqué. Tous les
fichiers sont relatifs à `~/sts2/game/pck/src/Core/`. **Aucun chemin ne passe par un nœud Godot**
(les liens de l'arbre de scène sont natifs, pas des champs managés) ni par un parcours du tas.

## 1. Run (RunManager → RunState → Player)

```
static RunManager.K(Instance)                         Runs/RunManager.cs:64
  └ K(State) : RunState  (null hors run)              Runs/RunManager.cs:209
      ├ _players : List<Player>                       Runs/RunState.cs:25
      ├ _currentRooms : List<AbstractRoom>            Runs/RunState.cs:46   (dernier = CurrentRoom, l.175)
      ├ _currentActIndex : int                        Runs/RunState.cs:27
      └ K(AscensionLevel) : int                       Runs/RunState.cs:210
static LocalContext.K(NetId) : ulong?                 Context/LocalContext.cs:19   (joueur local)
Player
  ├ K(NetId) : ulong                                  Entities/Players/Player.cs:48
  ├ K(Character) : CharacterModel                     Entities/Players/Player.cs:44
  ├ K(Creature) : Creature                            Entities/Players/Player.cs:46
  │    ├ _currentHp / _maxHp / _block : int           Entities/Creatures/Creature.cs:30 / :32 / :28
  ├ _gold : int                                       Entities/Players/Player.cs:38
  ├ _relics : List<RelicModel>                        Entities/Players/Player.cs:32
  ├ _potionSlots : List<PotionModel?>                 Entities/Players/Player.cs:34   (null = slot vide)
  ├ K(Deck) : CardPile → _cards : List<CardModel>     Entities/Players/Player.cs:198 → Entities/Cards/CardPile.cs:16
  └ K(MaxEnergy) : int                                Entities/Players/Player.cs:200
AbstractModel.K(Id) : ModelId                         Models/AbstractModel.cs:31
  ModelId.K(Category) / K(Entry) : string             Models/ModelId.cs:9 / :11  → "CARD.X" (même forme que la save)
CardModel._currentUpgradeLevel : int                  Models/CardModel.cs:91
```

## 2. Écran de récompense de cartes (PRIORITÉ) — racine gérée, pas de scan du tas

Trouvé en suivant `RewardsCmd.OfferForRoomEnd` (Commands/RewardsCmd.cs:20-36) →
`RewardsSet.Offer` (Rewards/RewardsSet.cs:153-196) qui appelle
`_synchronizer.BeginRewardsSet(this)` (l.161) **avant** `NRewardsScreen.ShowScreen` (l.192).
`BeginRewardsSet` empile le set dans un état tenu par le singleton
(Multiplayer/Game/RewardsSetSynchronizer.cs:154-166) et ne le dépile qu'à la fin (l.368, l.413).

```
static RunManager.K(Instance)
  └ K(RewardsSetSynchronizer)                                     Runs/RunManager.cs:143
      ├ _localPlayerId : ulong                                    Multiplayer/Game/RewardsSetSynchronizer.cs:107
      └ _rewardStates : List<RewardsSetSynchronizer+PlayerRewardState>   …:115  (même ordre que RunState._players)
          └ [slot local].rewardsStack : List<+RewardsSetState>    …:66   (pile : une relique prise peut ouvrir un 2e écran)
              └ [i].set : RewardsSet                              …:93
                  ├ K(Player)                                     Rewards/RewardsSet.cs:47
                  └ K(Rewards) : List<Reward>                     Rewards/RewardsSet.cs:49
                      └ (type exact == CardReward)
                          ├ K(SuccessfullySelected) : bool        Rewards/Reward.cs:51
                          └ _cards : List<CardCreationResult>     Rewards/CardReward.cs:37   ← LES CARTES PROPOSÉES
                              ├ _modifiedCard : CardModel?        Entities/Cards/CardCreationResult.cs:10 (prioritaire, l.14)
                              └ originalCard : CardModel          Entities/Cards/CardCreationResult.cs:8
```

Conséquence : l'offre est lisible **dès que l'écran des récompenses apparaît**, avant même que le
joueur clique sur « ajouter une carte » (le tirage a lieu dans `GenerateWithoutOffering`, l.125-146,
appelé l.159 juste avant `BeginRewardsSet`).

Hors périmètre (non couvert, à cartographier si besoin) : choix de cartes issus d'événements, de
potions ou de reliques qui passent par `CardSelection` et pas par `CardReward`.

## 3. Shop (PRIORITÉ)

`MerchantRoom.EnterInternal` (Rooms/MerchantRoom.cs:52-70) crée un `MerchantInventory` par joueur ;
la salle est sur la pile `RunState._currentRooms` (`PushRoom`, Runs/RunState.cs:491-497).

```
RunState._currentRooms[^1]  (type exact MegaCrit.Sts2.Core.Rooms.MerchantRoom)
  └ K(Inventories) : List<MerchantInventory>               Rooms/MerchantRoom.cs:27  (index = slot joueur, l.46-50)
      ├ K(Player)                                          Entities/Merchant/MerchantInventory.cs:48  (on filtre sur NetId local)
      ├ _characterCardEntries : List<MerchantCardEntry>    …/MerchantInventory.cs:30
      ├ _colorlessCardEntries : List<MerchantCardEntry>    …/MerchantInventory.cs:32
      ├ _relicEntries : List<MerchantRelicEntry>           …/MerchantInventory.cs:34
      ├ _potionEntries : List<MerchantPotionEntry>         …/MerchantInventory.cs:36
      └ K(CardRemovalEntry) : MerchantCardRemovalEntry?    …/MerchantInventory.cs:46
MerchantEntry._cost : int                                  Entities/Merchant/MerchantEntry.cs:17
MerchantCardEntry.K(CreationResult) : CardCreationResult?  Entities/Merchant/MerchantCardEntry.cs:32  (null = vendu)
MerchantCardEntry.K(IsOnSale) : bool                       Entities/Merchant/MerchantCardEntry.cs:34
MerchantRelicEntry.K(Model) : RelicModel?                  Entities/Merchant/MerchantRelicEntry.cs:21 (null = vendu)
MerchantPotionEntry.K(Model) : PotionModel?                Entities/Merchant/MerchantPotionEntry.cs:24 (null = vendu)
MerchantCardRemovalEntry.K(Used) : bool                    Entities/Merchant/MerchantCardRemovalEntry.cs:16
```

**Piège prix** : `_cost` est le prix **avant hooks**. Le prix affiché est
`(int)Hook.ModifyMerchantPrice(_cost)` (Entities/Merchant/MerchantEntry.cs:19-30). Les seuls
modificateurs dans le source v0.107.1 sont `MembershipCard` ×0.5 (Models/Relics/MembershipCard.cs:18)
et `TheCourier` ×0.8 (Models/Relics/TheCourier.cs:19). Le miroir ne peut pas appeler un hook (lecture
seule) : il rapporte `BaseCost` (lu) et `EstimatedDisplayedCost` (calculé, relique par relique, à
vérifier à l'écran). Le soldé est déjà inclus dans `_cost` (MerchantCardEntry.cs:125-128).

## 4. Combat

```
static CombatManager.K(Instance)                    Combat/CombatManager.cs:80
  ├ K(IsInProgress) : bool                          Combat/CombatManager.cs:153
  └ _state : CombatState?                           Combat/CombatManager.cs:67
      ├ _enemies : List<Creature>                   Combat/CombatState.cs:29
      │    └ K(Monster) : MonsterModel → K(Id)      Entities/Creatures/Creature.cs:101
      └ K(RoundNumber) : int                        Combat/CombatState.cs:102
Player.K(PlayerCombatState) : PlayerCombatState?    Entities/Players/Player.cs:114
  ├ _energy : int                                   Entities/Players/PlayerCombatState.cs:21
  └ K(Hand) : CardPile                              Entities/Players/PlayerCombatState.cs:60
```

## 5. Champs INTERDITS (refusés à l'exécution par `ReadGuard`)

Décision Mio (docs/20 §8.2, « probabiliste seulement ») + ordre caché : tout nom de champ contenant
`rng`, `odds`, `grabbag`, `encounterid`, `eventid`, `seed`, `drawpile`, `_piles`, `_runpiles`
lève `ForbiddenReadException`, de même que tout champ absent de `ObjectPaths.All`. Exemples réels
écartés : `Player.K(PlayerRng)` (Player.cs:55), `K(PlayerOdds)` (:62), `K(RelicGrabBag)` (:69),
`RunState.K(Rng)` (RunState.cs:215), `K(Odds)` (:220), `K(SharedRelicGrabBag)` (:225),
`PlayerCombatState.K(DrawPile)` (PlayerCombatState.cs:62 : l'ORDRE de la pioche est caché),
`Player._runPiles` (Player.cs:30). Les prix des reliques/cartes sont tirés via `PlayerRng.Shops`
(MerchantRelicEntry.cs:44) : on lit le **résultat affiché** `_cost`, jamais le RNG.

## 6. Ce qui reste à prouver en jeu (fenêtre de test)

- Que ces chemins portent les valeurs attendues dans le processus réel (cf. `test-window.md`).
- Que `LocalContext.NetId` est renseigné en solo (sinon repli : 1 seul joueur ⇒ c'est lui).
- Que l'entrée `CardReward` apparaît bien dans `rewardsStack` avant le clic (déduit du source, l.159-192).
