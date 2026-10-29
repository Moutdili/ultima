# S21 — Catalogue déclaratif des pouvoirs (260)

> Source : `mio@100.123.83.40:~/sts2/game/pck/src/Core/Models/Powers/*.cs`, lue en **ssh
> lecture seule** par `tools/index_powers.py` (tar sur stdout, rien n'est écrit à distance).
> Sortie commitée : `data/catalog/powers.json`, **45 782 octets** (budget données : 1,73 Mio / 2 Mio).

## Ce qui est livré

| Livrable | Rôle |
|---|---|
| `tools/index_powers.py` | extraction + normalisation + rapport ; `--src-dir` pour rejouer hors ligne |
| `data/catalog/powers.json` | 260 pouvoirs, hooks, expressions, `source` fichier:ligne |
| `Ultima.Engine/Scoring/Powers/PowerCatalog.cs` | chargeur (`Load`, `Empty`, `Get`, `Expression`), repli `Empty` |
| `Ultima.Engine/Scoring/Powers/ExpressionEval.cs` | évaluateur decimal sans `Eval`/réflexion : `Amount`, `In`, `+ - * /`, parenthèses, `1.5m` |
| `Ultima.Engine.Tests/Scoring/PowerCatalogTests.cs` | 13 tests (les 6 cas demandés + invariants) |

**Conflit de nom réglé** : `tools/index_source.py` écrivait lui aussi `data/catalog/powers.json`
(index historique des 30 pouvoirs modifiant les dégâts, 4,3 Ko). Il écrit maintenant
`data/catalog/powers_damage.json` — fichier conservé tel quel, ajouté à
`OfflinePackagingTests.RequiredCatalogFiles`. Sinon `make index` aurait écrasé le catalogue.

## Taux d'extraction

| Mesure | Valeur |
|---|---:|
| Pouvoirs extraits | **260 / 260** |
| Pouvoirs qui surchargent au moins un des 12 hooks | **53** |
| Hooks surchargés | **54** |
| Hooks dont l'expression est évaluable | **50 (92,6 %)** |
| Pouvoirs avec au moins un hook évaluable | **49** |
| Pouvoirs sans aucun hook numérique (207) | agissent par `AfterCardPlayed`, `AfterTurnEnd`, `ShouldDie`… hors des 12 hooks |

Par hook (surcharges / évaluables) :

| Hook | Surcharges | Évaluables |
|---|---:|---:|
| `ModifyDamageMultiplicative` | 21 | 19 |
| `ModifyHandDraw` | 8 | 7 |
| `ModifyDamageAdditive` | 7 | 7 |
| `ModifyCardPlayCount` | 6 | 6 |
| `ModifyBlockMultiplicative` | 4 | 3 |
| `ModifyMaxEnergy` | 4 | 4 |
| `ModifyDamageCap` | 2 | 2 |
| `ModifyBlockAdditive` | 2 | 2 |
| `ModifyXValue`, `ModifyMerchantPrice`, `ShouldDie`, `PreventDeath` | 0 | — |

Écart avec `docs/33` : le registre annonce **25** multiplicateurs de dégâts ; c'est le total
**pouvoirs + reliques**. Côté pouvoirs : **21**.

## Expressions non évaluables (4, toutes sourcées)

| Pouvoir | Hook | Raison | Code source |
|---|---|---|---|
| `intercept` | `ModifyDamageMultiplicative` | `state` — `GetInternalData<Data>().coveredCreatures.Count + 1` | `InterceptPower.cs:58` |
| `mind_rot` | `ModifyHandDraw` | `call` — `Math.Max(0m, count - Amount)` | `MindRotPower.cs:14` |
| `shadowmeld` | `ModifyBlockMultiplicative` | `call` — `(decimal)Math.Pow(2.0, base.Amount)` | `ShadowmeldPower.cs:24` |
| `slow` | `ModifyDamageMultiplicative` | `dynamicvar_runtime` — `SlowAmount` réécrite en combat (`BaseValue++`) | `SlowPower.cs:33` |

Aucune de ces quatre n'a de valeur de repli : `Expression()` rend `null` et `Problem()` rend la
raison. C'est la règle du `null` explicite plutôt qu'un `1.0` silencieux.

## Ce que le moteur peut réellement appliquer

- `vulnerable` ×1,5 · `weak` ×0,75 · `frail` ×0,75 · `covered`/`no_block` ×0 · `tank`/`conqueror` ×2
  · `gigantification` ×3 · `surrounded` ×1,5 · `colossus`/`guarded`/`diamond_diadem` ×0,5
- plafonds : `intangible` → 1, `hard_to_kill` → `Amount`
- additifs : `strength`, `dexterity`, `leadership`, `vigor`, `tainted`, `phantom_blades`, `calcify`, `accuracy` → `Amount`
- pioche/énergie/jets : `In + Amount` (`machine_learning`, `pyre`, `tag_team`…) ; `waste_away` = `In - Amount`
- `lethality` = `1 + Amount / 100` · `slow` refusé

## Limites (mesurées, pas supposées)

1. **Les gardes ne sont pas capturées.** `VulnerablePower` rend `1m` si `target != Owner` ou si
   l'attaque n'est pas « powered » ; le catalogue garde l'**effet** (1,5), pas la condition
   d'application. C'est à l'appelant de décider *quand* appliquer le hook. Le nombre de `if`
   et de `switch` est conservé dans `shape` pour que ce soit visible.
2. **Valeurs `DynamicVars` figées au littéral.** Une `DynamicVar` n'est reprise que si
   `CanonicalVars` la déclare comme nombre (`new DynamicVar("DamageDecrease", 0.5m)`). Si elle
   est réécrite en combat, elle est refusée (`dynamicvar_runtime`) au lieu d'être figée à sa
   valeur initiale.
3. **Modificateurs externes nommés, pas absorbés.** `vulnerable` vaut 1,5 **avant**
   `PaperPhrog` / `CrueltyPower` / `DebilitatePower`, listés dans `dynamic_inputs`. Le moteur
   qui veut la valeur exacte doit les appliquer.
4. **Sélection de l'expression.** Parmi les `return` d'un hook, les valeurs neutres sont
   écartées (`1`, `0`, `count`, `MaxValue`). S'il reste une seule valeur non neutre, c'est
   elle ; s'il en reste plusieurs (aucun cas dans les 260), la dernière est gardée et le hook
   est marqué `conditional`.
5. **Pas de sémantique.** Le catalogue dit *quelle* valeur le hook rend, pas à quoi il
   s'applique : `ModifyBlockAdditive` sur `dexterity` et `ModifyDamageAdditive` sur `strength`
   ont la même expression `Amount`.
6. **`In` vs `Amount`.** `Amount` = piles du pouvoir ; `In` = valeur reçue par le hook
   (cartes à piocher, énergie, nombre de jets). Confondre les deux donnerait `2 × Amount`.

## Vérification

```
env -u ULTIMA_DATA bash tools/hermes-verify.sh   # PASS, 159 tests (146 + 13)
```

⚠️ Le shell de cette session exporte `ULTIMA_DATA=/home/mio/wt-s23` (autre worktree). Sans
`env -u ULTIMA_DATA`, `ScoringData.ResolveCatalogDir()` lit le catalogue de wt-s23 et les
tests S21 échouent avec `Count = 0`. À vérifier avant de conclure à une régression.
