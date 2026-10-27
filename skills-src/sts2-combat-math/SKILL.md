---
name: sts2-combat-math
description: Use when computing enemy damage, Weak/Vulnerable variants, or exact lethality (which hit in an NxM sequence kills). Contains the verified damage pipeline and the lethal algorithm.
---

# STS2 combat math

## API vérifiées

```csharp
// Core/MonsterMoves/Intents/AttackIntent.cs
public Func<decimal>? DamageCalc { get; protected set; }
public virtual int Repeats => 0;                       // 1 pour Single, N pour Multi
public override int GetTotalDamage(targets, owner);     // single * repeats
public int GetSingleDamage(IEnumerable<Creature> targets, Creature owner)
{
    decimal num = DamageCalc();
    Player me = LocalContext.GetMe(owner.CombatState);
    if (me != null)
        num = Hook.ModifyDamage(me.RunState, me.Creature.CombatState, me.Creature, owner,
                                DamageCalc(), ValueProp.Move, null,
                                ModifyDamageHookType.All, CardPreviewMode.None, out _);
    return Math.Max(0, (int)num);
}
```

→ **`GetSingleDamage` applique déjà tout le pipeline** (force, vulnérable, faiblesse,
reliques) du point de vue du joueur local. Ne pas re-multiplier par-dessus.

## Multiplicateurs

| Pouvoir | Effet | Fichier |
|---|---|---|
| `WeakPower` | ×0.75 (multiplicatif), modulé par `PaperKrane` (relique de la cible) et `DebilitatePower` (du dealer) | `Models/Powers/WeakPower.cs` |
| `VulnerablePower` | ×? — **lire la valeur** dans le fichier, ne pas supposer 1.5 | `Models/Powers/VulnerablePower.cs` |
| `IntangiblePower` | cap (hook `ModifyDamageCap`) | `Models/Powers/IntangiblePower.cs` |

## Variantes hypothétiques

```
current = intent.GetSingleDamage(targets, owner)      // état réel
weak    = current * 0.75m                             // SEULEMENT si owner n'a pas déjà Weak
```
Si le monstre a déjà `WeakPower`, `current` l'inclut : afficher « déjà affaibli », pas de
double comptage. Idem pour Vulnerable côté joueur.

Toujours arrondir comme le jeu : `(int)decimal` puis `Math.Max(0, ...)`.

## Ordre du pipeline (confirmé)

`enchant → additif → multiplicatif → cap → BeforeDamageReceived → block → ModifyHpLost → LoseHp`.
Weak et Vulnerable commutent (decimal). Le block vient **strictement après** tous les
multiplicateurs : ne jamais appliquer un multiplicateur après le block.

⚠️ CombatSolver (qui documente cet ordre) cible la version **0.111.0** ; nous sommes en
**0.107.1**. Revalider toute signature reprise.

## Létalité (NxM)

```
remainingBlock = player.Block
remainingHp    = player.CurrentHp
lethalAt = null
for i in 1..intent.Repeats:
    dmg = perHit
    absorbed = min(remainingBlock, dmg); remainingBlock -= absorbed; dmg -= absorbed
    remainingHp -= dmg
    if remainingHp <= 0 and lethalAt is null: lethalAt = i
```

Sortie : `16x3 → 3e coup LÉTAL (−2 PV)`, ou `16x3 → 14 PV restants (block 20)`.

Cas à gérer : `perHit == 0`, mort au 1er coup, overkill, cap `IntangiblePower`,
block persistant (vérifier les pouvoirs de rétention avant d'affirmer que le block s'épuise).

## Priorité de tour (cartes conditionnelles)

```csharp
// déjà jouée ce tour ?
CombatManager.Instance.History.CardPlaysFinished
    .Any(e => e.CardPlay.Card == this && e.HappenedThisTurn(combatState));
```

Le jeu expose aussi `CardModel.ShouldGlowGold` / `ShouldGlowRed` : si `ShouldGlowGold`
est vrai, le bonus conditionnel de la carte est **actif maintenant**. C'est le meilleur
signal pour l'alerte multi — pas besoin de re-deviner la condition.

Catalogue : `~/sts2/data/catalog/priority_cards.json` (34 cartes, généré par l'indexeur).
