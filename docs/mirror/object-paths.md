# Miroir mémoire — carte des chemins d'objets (S2, statique)

> Agent B « Le Miroir ». Établi **sans le jeu** le 2026-10-28, à partir du source livré
> (`~/sts2/game/pck/src/Core`, v0.107.1, commit 59260271) et de la **métadonnée** de `sts2.dll`
> (sha256 `d13f3f15…bd03`, MVID `64622899-e36c-49f2-b463-1b26ad68cbd5`).
> Source unique de vérité dans le code : `Ultima.Mirror/src/Ultima.Mirror/ObjectPaths.cs`.

## Comment c'est prouvé

| Preuve | Commande | Résultat |
|---|---|---|
| Chaque champ existe dans `sts2.dll` avec le bon nom réel et le bon genre (statique/instance) | `ultima-mirror layout` (System.Reflection.Metadata : la DLL n'est ni chargée ni exécutée) | **56/56 OK** (S2) → **79/79 OK** avec le miroir 2 (evidence/miroir2/21) |
| Chaque `fichier:ligne` ci-dessous déclare bien ce membre | `evidence/miroir2/check-source-lines.py` (ligne lue contient le nom du membre) | **79 vérifiés, 0 écart** (evidence/miroir2/31) |
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
`PlayerCombatState.K(DrawPile)` (PlayerCombatState.cs:62 : l'ORDRE de la pioche est caché — depuis le
miroir 2 son CONTENU est lu, compté, par le seul chemin `ReadUnorderedPile`, cf. §8),
`Player._runPiles` (Player.cs:30). Les prix des reliques/cartes sont tirés via `PlayerRng.Shops`
(MerchantRelicEntry.cs:44) : on lit le **résultat affiché** `_cost`, jamais le RNG.

## 6. Ce qui reste à prouver en jeu (fenêtre de test S2)

- Que ces chemins portent les valeurs attendues dans le processus réel (cf. `test-window.md`).
- Que `LocalContext.NetId` est renseigné en solo (sinon repli : 1 seul joueur ⇒ c'est lui).
- Que l'entrée `CardReward` apparaît bien dans `rewardsStack` avant le clic (déduit du source, l.159-192).

---

# Miroir 2 (2026-10-29) — événement, piles de combat, enchantements

Même méthode : source `~/sts2/game/pck/src/Core` v0.107.1, noms réels vérifiés dans la métadonnée de
`sts2.dll` (sha256 `d13f3f15…bd03`, inchangé) par `layout` = **79/79** (56 + 23 nouveaux), chaque
`fichier:ligne` vérifié par script (79/0 écart). Code : `ObjectPaths.cs` (bloc « miroir 2 »),
`Readers.cs` (`ReadEvent`, `ReadPiles`, `Card`), `MirrorSession.ReadUnorderedPile`.
Preuves sur la cible de test : `evidence/miroir2/` (rien n'a été lu dans le vrai jeu).

## 7. Événement courant — section `event`

Trouvé en suivant `EventRoom.EnterInternal` (Rooms/EventRoom.cs:56-83) →
`RunManager.Instance.EventSynchronizer.BeginEvent(CanonicalEvent, …)` (l.59), qui crée UNE copie
mutable par joueur (Multiplayer/Game/EventSynchronizer.cs:96-103) ; l'écran affiche celle du joueur
local, `GetLocalEvent()` = `_events[slot local]` (l.306-315), et ses `CurrentOptions`
(Nodes/Rooms/NEventRoom.cs:192).

```
RunState._currentRooms[^1]  (type exact MegaCrit.Sts2.Core.Rooms.EventRoom)
  └ K(CanonicalEvent) : EventModel → K(Id)                    Rooms/EventRoom.cs:29          (id de la salle)
static RunManager.K(Instance)
  └ K(EventSynchronizer)                                      Runs/RunManager.cs:139         (créé l.408)
      ├ _localPlayerId : ulong                                Multiplayer/Game/EventSynchronizer.cs:26
      └ _events : List<EventModel>                            …/EventSynchronizer.cs:28      (index = slot joueur, l.311-315)
          └ [slot local] : EventModel (mutable)
              ├ K(Id)                                         Models/AbstractModel.cs:31     → "EVENT.X"
              ├ K(Owner) : Player → K(NetId)                  Models/EventModel.cs:77
              ├ K(Description) : LocString?                   Models/EventModel.cs:87        (page courante)
              ├ _isFinished : bool                            Models/EventModel.cs:44
              └ _currentOptions : List<EventOption>?          Models/EventModel.cs:42        (null = aucune)
                  ├ K(TextKey) : string                       Events/EventOption.cs:14       → id de l'option
                  ├ K(Title) : LocString                      Events/EventOption.cs:16
                  ├ K(Description) : LocString                Events/EventOption.cs:18
                  ├ K(IsLocked) : bool                        Events/EventOption.cs:24       (clic ignoré : NEventRoom.cs:211)
                  ├ K(IsProceed) : bool                       Events/EventOption.cs:26
                  ├ K(WasChosen) : bool                       Events/EventOption.cs:28
                  └ K(Relic) : RelicModel?                    Events/EventOption.cs:30       (options d'ancien)
LocString.<locTable>P / <locEntryKey>P : string               Localization/LocString.cs:13   (constructeur primaire)
```

**Contrôles « je ne sais pas »** : la salle du dessus doit être un `EventRoom` (sinon `InEvent=false`) ;
`_events[slot].Owner.NetId == joueur local` ET `_events[slot].Id == CanonicalEvent.Id`. Sinon
(événement pas encore commencé — `BeginEvent` est appelé après un `await` de préchargement, l.58-59 —
ou événement précédent encore dans la liste : elle n'est vidée qu'au `BeginEvent` suivant, l.91)
→ `optionsKnown=false`, `options=null` (« inconnu ») : jamais les options d'un autre événement (preuve :
evidence/miroir2/25 et 32-probe-json-event-stale.jsonl, scène `event-stale`).

**Forme JSON = celle que lit le daemon** (`Ultima.Daemon/Mirror/MirrorModels.cs` : `MEvent(EventId, Name,
Options)`, `MEventOption(Id, Label, Description, Available)` ; camelCase, champs inconnus ignorés). Le miroir
ne rend AUCUN texte (il faudrait appeler `LocString.GetFormattedText`, une méthode du jeu) : les chaînes
d'affichage sont toujours `null` et les clés de localisation voyagent à côté. Vérifié par
`Ultima.Mirror.Tests` (la ligne brute de la CLI passe par `MirrorResult.Parse` du daemon, evidence/miroir2/32-33).

| JSON (`--sections event`) | Source | Lu par le daemon | Remarque |
|---|---|---|---|
| `eventId` | `CanonicalEvent.Id` (`"EVENT.X"`) | oui | recoupé avec l'événement local |
| `name` | — | oui | toujours `null` → le daemon affiche l'id nu |
| `options` | `_currentOptions` | oui | `null` si `optionsKnown=false` → le daemon n'affiche rien |
| `options[].id` | `TextKey` | oui | ex. `X.pages.INITIAL.options.Y` ; le daemon garde le dernier segment |
| `options[].label`, `.description` | — | oui | toujours `null` → le daemon affiche l'id nu de l'option |
| `options[].available` | `!IsLocked` | oui | |
| `nameKey` | (`table`, `"X.title"`) | non | déduite d'`EventModel.Title` (EventModel.cs:66) ; table = celle de `Description` (`events`, ou `ancients` pour `AncientEventModel`, l.34, et `TheArchitect`) ; `null` si inconnue |
| `descriptionKey` | `Description` (table, clé) | non | page courante |
| `options[].titleKey`, `.descriptionKey` | `Title`, `Description` | non | les options de relique ont `Title` = titre de la relique (EventOption.cs:126-131) |
| `isFinished` | `_isFinished` | non | fini ⇒ l'écran remplace les options par UN bouton « Continuer » synthétique, absent de `_currentOptions` (NEventRoom.cs:193-196) |
| `inEvent`, `optionsKnown`, `isAncient`, `options[].index/locked/isProceed/wasChosen/relicId` | | non | `isAncient` = le type dérive de `AncientEventModel` (métadonnée de type, pas un champ) |

**Non lu, volontairement** : les variables des `LocString` (`_variables`, ex. « Perdre {Damage} PV ») —
leur rendu passe par SmartFormat et des `DynamicVar` virtuels ; plutôt que risquer une valeur fausse, le
libellé est rendu par la KB avec la clé (variables manquantes = « ? »). Aussi : `Rng` (EventModel.cs:142),
`_multiplayerOptionSelectionRng` (EventSynchronizer.cs:38), `_playerVotes`, `OnChosen`, `WillKillPlayer`,
`_dynamicVars`, `_mutableEncounter`, `AncientEventModel._generatedOptions` (refusés : evidence/miroir2/22).

## 8. Piles de combat — section `piles` (comptées, jamais ordonnées)

```
Player.K(PlayerCombatState) : PlayerCombatState?            Entities/Players/Player.cs:114   (null hors combat)
  ├ K(Hand)        : CardPile                               Entities/Players/PlayerCombatState.cs:60
  ├ K(DrawPile)    : CardPile   ← UnorderedOnly             Entities/Players/PlayerCombatState.cs:62
  ├ K(DiscardPile) : CardPile                               Entities/Players/PlayerCombatState.cs:64
  ├ K(ExhaustPile) : CardPile                               Entities/Players/PlayerCombatState.cs:66
  └ K(PlayPile)    : CardPile                               Entities/Players/PlayerCombatState.cs:68   (carte en cours de jeu)
CardPile._cards : List<CardModel>                           Entities/Cards/CardPile.cs:16
```

**Pourquoi le contenu de la pioche est lisible et pas son ordre** : le jeu lui-même montre la pioche
**triée** (rareté puis id), jamais dans l'ordre de tirage (Nodes/Screens/NCardPileScreen.cs:122-125).
Le miroir fait pareil, en plus strict :

- `ReadGuard.Check("<DrawPile>k__BackingField")` **refuse toujours** (fragment `drawpile`) ; le champ n'est
  pas dans `ObjectPaths.All` mais dans `ObjectPaths.UnorderedOnly` (vérifié par `layout`).
- Seul `MirrorSession.ReadUnorderedPile` passe par `ReadGuard.CheckUnordered`, qui n'accepte QUE ce champ ;
  la liste ordonnée n'existe que dans ses variables locales ; il rend un `CountedPile` (multiensemble
  trié par id, amélioration, enchantement) — aucune API ne rend la pioche dans l'ordre.
- Toutes les piles sont rendues comptées (main comprise ; l'ordre visible de la main reste dans `combat`).
- Forme JSON (`--sections piles`) : `{inCombat, hand, draw, discard, exhaust, play, cardsBeforeReshuffle}`,
  chaque pile `{total, cards:[{card:{id, upgrade, enchantment}, count}]}` ; piles `null` hors combat.
  Le daemon de main ne demande pas encore cette section (aucun modèle `MPiles`).
- `CardsBeforeReshuffle` = taille de la pioche (la défausse n'est remélangée que quand une pioche la trouve vide).
- Preuve d'invariance à l'ordre : la cible rebat sa pioche toutes les 5 ms ; 40 sondes ⇒ **1 seule
  sortie** (evidence/miroir2/28). `_piles` (cache `AllPiles`, l.19) et `Player._runPiles` restent interdits.

## 9. Enchantements des cartes — champ `Enchantment` de toute `CardView`

```
CardModel.K(Enchantment) : EnchantmentModel?                Models/CardModel.cs:631    (null = aucun)
  ├ K(Id)                                                   → "ENCHANTMENT.X" (forme de la save, ToSerializable, EnchantmentModel.cs:370-378)
  └ _amount : int                                           Models/EnchantmentModel.cs:29
```

`Amount` affiché = `DisplayAmount` = `Amount` (EnchantmentModel.cs:116 ; **aucune** surcharge de
`DisplayAmount` dans `Models/Enchantments/*`) ; certains enchantements n'affichent pas le nombre
(`ShowAmount`, l.114) — le miroir rend quand même la valeur. Ajouté à `CardView` comme 3e paramètre
optionnel (`Enchantment = null`, JSON `"enchantment": {"id", "amount"}` ou `null`) : les sections `run`
(deck), `reward`, `shop`, `combat` et `piles` le portent toutes. Le daemon de main l'IGNORE encore
(`MCard(Id, Upgrade)`) : compatible, mais l'enchantement n'atteint pas l'overlay sans changement côté daemon.
Pour la récompense et le shop, l'enchantement lu est celui de la carte **effective**
(`_modifiedCard` si une relique l'a modifiée, sinon `originalCard`), comme pour l'id.

Non couvert : `CardModel.K(Affliction)` (l.633, malédictions temporaires de combat) — même forme, à
ajouter si l'overlay le demande.

## 10. Ce que le miroir 2 ne prouve PAS encore

- Aucune valeur du **vrai jeu** : tout est prouvé sur la cible de test (même forme, autres données).
  Protocole à suivre lors d'une fenêtre de test : `status/miroir2.md` (« reste à faire »), sur le modèle de `test-window.md`.
- Que `_events[slot]` suit bien le slot de `RunState._players` en co-op (déduit de
  `GetPlayerSlotIndex`, EventSynchronizer.cs:313 ; testé en solo seulement sur la cible).
- Les événements à mise en page `Custom`/`Combat` (EventModel.cs:176-183) : options lues pareil, mais
  l'écran peut afficher autre chose (ex. Crystal Sphere) — à vérifier en jeu.
- La fréquence de lectures INCONNU sur les piles pendant une animation de pioche dans le vrai jeu.
