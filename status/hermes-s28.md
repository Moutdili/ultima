# S28 — Couverture évaluable des pouvoirs (priorité multiplicateurs, énergie, pioche)

> Source : `mio@100.123.83.40:~/sts2/game/pck/src/Core/Models/Powers/*.cs`, lue en **ssh
> lecture seule** par `tools/index_powers.py` (tar sur stdout, rien n'est écrit à distance).
> Sorties commitées : `data/catalog/powers.json` (68 281 octets, schéma 2) et
> `data/catalog/power_constants.json` (table manuelle sourcée).

## Ce qui est livré

| Livrable | Rôle |
|---|---|
| `tools/index_powers.py` | extraction v2 : constantes, `DynamicVar` de constructeur/`SetVar`, pliage, hooks sans `override`, statut par hook ; `--self-test` |
| `data/catalog/powers.json` | 260 pouvoirs, `evaluable` + `resolution` (`code`/`manual`/`unresolved`) par hook, `manual_sources`, top-20 non résolus |
| `data/catalog/power_constants.json` | 4 lignes manuelles (multiplicateurs/pioche/bloc) + les 4 reliques des 25 multiplicateurs, chacune avec `fichier.cs:ligne` |
| `Ultima.Engine/Scoring/Powers/PowerCatalog.cs` | lit `resolution` / `manual_sources` ; `ResolutionOf`, `ManualSourceOf`, `IsEvaluable`, `Catalog.Resolution(id, hook)` |
| `Ultima.Engine.Tests/Scoring/PowerCoverageTests.cs` | 6 tests (cohérence des deux fichiers, résolutions, 25 multiplicateurs, inconnu jamais à 1) |

## Couverture avant / après

| Mesure | Avant (HEAD) | Après |
|---|---:|---:|
| Pouvoirs | 260 | 260 |
| Pouvoirs avec ≥ 1 hook surchargé | 53 | **56** |
| Hooks surchargés (15 hooks) | 54 | **58** |
| Hooks `code` | 50 | **54** |
| Hooks `manual` | 0 | **4** |
| Hooks `unresolved` | 4 | **0** |
| Pouvoirs `code` (≥ 1 hook évaluable) | 49 | **52** |
| Pouvoirs `manual` (documentés, hors grammaire) | 0 | **4** |
| Pouvoirs couverts (code + manual) | 49 | **56** |
| Pouvoirs `unresolved` | 211 | **204** |

Par hook (surcharges / code / manual) :

| Hook | Avant | Après |
|---|---:|---:|
| `ModifyDamageMultiplicative` | 21 / 19 | 21 / **19 / 2** |
| `ModifyDamageAdditive` | 7 / 7 | 7 / 7 / 0 |
| `ModifyDamageCap` | 2 / 2 | 2 / 2 / 0 |
| `ModifyBlockMultiplicative` | 4 / 3 | 4 / **3 / 1** |
| `ModifyBlockAdditive` | 2 / 2 | 2 / 2 / 0 |
| `ModifyHandDraw` | 8 / 7 | 8 / **7 / 1** |
| `ModifyMaxEnergy` | 4 / 4 | 4 / 4 / 0 |
| `ModifyCardPlayCount` | 6 / 6 | 6 / 6 / 0 |
| `ModifyVulnerableMultiplier` | absent | **2 / 2 / 0** |
| `ModifyWeakMultiplier` | absent | **1 / 1 / 0** |
| `ModifyEnergyGain` | absent | **1 / 1 / 0** |

## Ce que l'extraction résout maintenant (et pourquoi ça ne bouge pas les 207)

Le goulot n'était pas la normalisation : sur les 12 hooks du moteur, **50 des 54 surcharges
étaient déjà évaluables**. Les 4 restantes sont hors grammaire par nature (`Math.Pow`,
`Math.Max`, `GetInternalData`, `DynamicVar` réécrite en combat) : aucune constante ne les
débloque. Ce qui a réellement augmenté la couverture :

1. **3 hooks ajoutés** (présents dans `PowerModel`, même forme entrée décimale → décimale) :
   - `ModifyVulnerableMultiplier` — `CrueltyPower.cs:17` (`In + Amount / 100`), `DebilitatePower.cs:26` (`In + (In - 1)`) ;
   - `ModifyWeakMultiplier` — `DebilitatePower.cs:39` (`In - (1 - In)`) ;
   - `ModifyEnergyGain` — `NoEnergyGainPower.cs:19` (`0`).
   `Cruelty` et `Debilitate` sont deux multiplicateurs de dégâts qui étaient invisibles.
2. **Constantes littérales** : `public const decimal damageDecrease = 30m` (ShrinkPower.cs:18)
   substituée avant normalisation — vérifié par `--self-test` (cas synthétique `0.7`).
3. **`DynamicVar` de constructeur / `SetVar`** : lus seulement dans un constructeur sans
   paramètre (les 260 pouvoirs n'en ont pas ; Slow/Panache/Withering écrivent ces vars dans
   des hooks de tour, donc restent `dynamicvar_runtime`). Capacité couverte par `--self-test`.
4. **Pliage des constantes** : `50 / 100` → `0.5` (Flutter, Soar), `(100 - 30) / 100` → `0.7`
   (Shrink). Même valeur, forme canonique.
5. **Hooks déclarés sans `override`** (`public decimal ModifyVulnerableMultiplier(...)`) :
   sans ça, Cruelty, Debilitate et PaperPhrog étaient ignorés.

Les 204 pouvoirs restants n'ont **aucun** hook numérique parmi les 15 : ils agissent par
`AfterTurnEnd` (Poison), `AfterCardPlayed` (Afterimage), `AfterDamageReceived`… Le catalogue
le dit (`no_hook`) au lieu de fabriquer un 1.

## Table manuelle — 4 lignes, 4 lignes de code citées

| Pouvoir | Hook | `value_expression` | Source | Pourquoi hors grammaire |
|---|---|---|---|---|
| `slow` | `ModifyDamageMultiplicative` | `1 + 0.1 * SlowAmount` | `SlowPower.cs:43` | compteur de combat : +1 par carte jouée (l.28), remis à 0 au tour suivant (l.58) |
| `intercept` | `ModifyDamageMultiplicative` | `CoveredCount + 1` | `InterceptPower.cs:68` | `GetInternalData<Data>().coveredCreatures.Count + 1` (état) |
| `mind_rot` | `ModifyHandDraw` | `max(0, In - Amount)` | `MindRotPower.cs:20` | `Math.Max(0m, count - Amount)` — pas de `max` dans le langage |
| `shadowmeld` | `ModifyBlockMultiplicative` | `2 ^ Amount` | `ShadowmeldPower.cs:30` | `Math.Pow(2.0, Amount)` — pas d'exposant |

Ces 4 lignes sont `evaluable: false` : documentées, jamais injectées comme valeur. Le
catalogue garde `hooks: null` + `resolution: "manual"` + `manual_sources`.

## Les « 25 multiplicateurs » : total vérifié

`docs/33 §3.1` et `docs/36` annoncent 25 multiplicateurs de dégâts. Mesuré dans le code :

| Composant | Nombre | Preuve |
|---|---:|---|
| Pouvoirs surchargeant `ModifyDamageMultiplicative` | 21 | `grep -l "ModifyDamageMultiplicative" Powers/*.cs` |
| dont `code` | 19 | `powers.json` |
| dont `manual` (`slow`, `intercept`) | 2 | `power_constants.json` |
| Reliques surchargeant `ModifyDamageMultiplicative` | 3 | `PenNib.cs:125`, `UndyingSigil.cs:47`, `VitruvianMinion.cs:31` |
| Relique modifiant `ModifyVulnerableMultiplier` | 1 | `PaperPhrog.cs:26` |
| **Total** | **25** | `sets.damage_multipliers.documented_total` |

Les 4 reliques sont documentées dans `power_constants.json` (hors `powers.json`, qui ne
contient que les 260 modèles de pouvoirs) pour que le total de `docs/33` soit vérifiable.

## Énergie / pioche : les chiffres de `docs/33` ne se reproduisent pas

`docs/33 §3.2` annonce « 16 énergies et 17 pioches » (33 hooks de reliques). Mesure :

| Périmètre | Énergie | Pioche |
|---|---:|---:|
| Pouvoirs (`ModifyMaxEnergy` / `ModifyHandDraw`) | 4 `code` + 1 `ModifyEnergyGain` `code` | 8 (7 `code` + 1 `manual`) |
| Reliques (`decimal ModifyMaxEnergy` / `decimal ModifyHandDraw`) | 12 | 10 |

Les deux nombres de `docs/33` sont une estimation antérieure, pas une mesure : je ne les
reprends pas comme un dû. Ce qui compte est couvert côté pouvoirs — 5/5 hooks d'énergie et
8/8 hooks de pioche sont `code` ou `manual`.

## Les 20 pouvoirs non résolus les plus utilisés

Usage = cartes de `cards_effects.json` qui appliquent le pouvoir + monstres dont le source
appelle `Apply<XPower>` (`models/Monsters/*.cs`, scanné en ssh).

| Pouvoir | Cartes | Monstres | Total |
|---|---:|---:|---:|
| `artifact` | 0 | 8 | 8 |
| `plating` | 3 | 5 | 8 |
| `poison` | 6 | 0 | 6 |
| `doom` | 5 | 0 | 5 |
| `focus` | 5 | 0 | 5 |
| `minion` | 0 | 5 | 5 |
| `thorns` | 2 | 2 | 4 |
| `battleworn_dummy_time_limit` | 0 | 3 | 3 |
| `ritual` | 0 | 3 | 3 |
| `crab_rage` | 0 | 2 | 2 |
| `illusion` | 0 | 2 | 2 |
| `slippery` | 0 | 2 | 2 |
| `adaptable` | 0 | 1 | 1 |
| `afterimage` | 1 | 0 | 1 |
| `arsenal` | 1 | 0 | 1 |
| `asleep` | 0 | 1 | 1 |
| `back_attack_left` | 0 | 1 | 1 |
| `back_attack_right` | 0 | 1 | 1 |
| `biased_cognition` | 1 | 0 | 1 |
| `black_hole` | 1 | 0 | 1 |

204 non résolus au total (61 applications par des cartes, 72 par des monstres). Aucun n'a de
valeur numérique dans les 15 hooks : ce sont des effets différés (`Poison` = dégâts au tour
suivant, `Artifact` = annulation de statut, `Minion` = invocation). Les résoudre demanderait
un hook de type `AfterTurnEnd`/`AfterApplied`, pas une expression.

## Limites (mesurées, pas supposées)

1. **`unresolved: 0` sur les hooks surchargés** ne veut pas dire « tout est évaluable » : 4
   hooks sont `manual` (documentés, non machine-évaluables). Le statut par pouvoir le dit.
2. **Les gardes ne sont toujours pas capturées** : `VulnerablePower` rend `1m` si la cible
   n'est pas le propriétaire ; le catalogue garde l'effet (1.5), pas la condition.
3. **Les `DynamicVar` de constructeur n'existent pas dans ces 260 fichiers** : la capacité
   est testée par `--self-test`, pas par une ligne du jeu. Elle ne prétend pas couvrir un cas
   réel absent.
4. **`modifyEnergyCost` (6 pouvoirs) n'est pas extrait** : signature `bool ... (out int cost)`,
   incompatible avec le mini-langage. Non compté comme couvert.

## Vérification

```
python3 tools/index_powers.py --self-test     # PASS, 5 cas
env -u ULTIMA_DATA bash tools/hermes-verify.sh  # PASS, 210 tests
```

`data/catalog/registry.json` a été régénéré : `coverage.evaluable_powers` 49 → 52,
`gaps.powers_without_evaluable_hook` 211 → 208.
