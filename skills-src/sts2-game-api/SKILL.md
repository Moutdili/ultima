---
name: sts2-game-api
description: Use when you need to find a class, hook or API inside Slay the Spire 2's source (combat state, intents, piles, map, rewards). Points to the extracted source tree and the verified entry points.
---

# STS2 game API — navigation

## Où lire le code

```
~/sts2/game/pck/src/Core        SOURCE ORIGINAL (priorité — code non obfusqué, shippé par MegaCrit)
~/sts2/game/csharp             ILSpy (types sans source, ex. GodotPlugins)
~/sts2/data/catalog/*.json     index généré (cartes, monstres, pouvoirs, priorités)
~/sts2/docs/11-api-catalog.md  résumé lisible
```

Recherche : `rg "class NCombatUi" ~/sts2/game/pck/src/Core`

## Namespaces utiles

| Namespace | Contenu |
|---|---|
| `Core.Nodes.Combat` | `NCombatUi`, `NCombatRoom`, `NIntent`, `NCardPlay` (UI) |
| `Core.Entities.Cards` | `CardModel`, `CardPlay`, `PileType`, `CardPreviewMode` |
| `Core.Entities.Creatures` | `Creature` (HP, block, powers) |
| `Core.MonsterMoves.Intents` | `AttackIntent`, `SingleAttackIntent`, `MultiAttackIntent`, `AbstractIntent` |
| `Core.Hooks` | `Hook` (tous les hooks), `ModifyDamageHookType` |
| `Core.Commands` | `CardPileCmd`, `DamageCmd`, `PowerCmd`, `CreatureCmd` (API d'action — **interdite** en read-only) |
| `Core.Combat.History` | `CombatHistory`, `CardPlaysFinished`, `HappenedThisTurn` |
| `Core.Multiplayer` | `NetMessageBus`, `INetMessage`, `RunManager.NetService` |
| `Core.Modding` | `ModInitializer`, `ModHelper.SubscribeFor*Hooks` |
| `Core.Map`, `Core.Odds` | graphe de map, probabilités |
| `Core.Models` | `Cards/`, `Monsters/`, `Powers/`, `Relics/`, `Potions/` |

## Points d'entrée vérifiés (v0.107.1)

- `NCombatUi._Ready` — création de l'UI de combat (cible d'injection overlay).
- `NCombatUi.OnCombatWon` — fin de combat.
- `CardPileCmd.Add` — carte ajoutée à une pile.
- `Hook.AfterCardPlayedLate(PlayerChoiceContext, CardPlay)` — carte résolue.
- `Hook.ModifyDamage(...)` — pipeline de dégâts (voir skill `sts2-combat-math`).
- `CombatManager.Instance.History` — historique de combat (source de vérité des events).
- `RunManager.Instance.NetService` — service réseau.

## Règle

Ne jamais inventer un nom de type ou de méthode. `rg` d'abord, puis citer fichier + ligne.
Le jeu est en Early Access : un nom peut changer entre deux patchs.
