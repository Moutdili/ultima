# 36 — Registre exhaustif des entités et couverture mesurée

> Générateur : `tools/build_registry.py` → `data/catalog/registry.json` (164 Ko, commité).
> Test d'invariant : `Ultima.Engine.Tests/RegistryCoverageTests.cs` (5 tests).
> Principe : **aucune entité inconnue**. Chaque carte, relique, potion, pouvoir, monstre et
> rencontre a une entrée, avec un statut explicite quand la donnée manque.

## Comptes (source : décompilation + catalogues)

| Catégorie | Total |
|---|---:|
| Cartes | **578** |
| — Attaques | 195 |
| — Compétences | 234 |
| — Pouvoirs (cartes) | 111 |
| — Malédictions | 18 |
| — Statuts | 17 (dont `DEPRECATED_CARD`, placeholder) |
| — Quêtes | 3 |
| Reliques | 304 |
| Potions | 64 |
| Pouvoirs (modèles) | 260 |
| Monstres | 115 |
| Rencontres | 87 |

## Couverture

| Mesure | Couvert | Sur |
|---|---:|---:|
| Cartes avec effets dans le catalogue | **577** | 578 (`DEPRECATED_CARD` exclu) |
| Cartes avec valeurs numériques | 348 | 578 |
| Malédictions avec impact **mesuré** | 17 | 18 |
| Statuts avec impact **mesuré** | 3 | 17 |
| Pouvoirs avec au moins un hook évaluable | **52** | 260 |
| Pouvoirs documentés (code + table manuelle sourcée) | **56** | 260 |
| Monstres avec contrainte spéciale | 3 | 115 |
| Rencontres avec statistiques observées | 50 | 87 |
| Reliques avec tags | 164 | 304 |

## Les trous, nommés

1. **1 malédiction sans mesure** : `BAD_LUCK` (jamais acquise avant l'acte 3 dans le corpus) →
   pénalité prudente explicite, jamais 0.
2. **14 statuts sans mesure** : `DAZED`, `DEBRIS`, `DISINTEGRATION`, `FRANTIC_ESCAPE`, `INFECTION`,
   `MIND_ROT`, `SLIMED`, `SOOT`, `TOXIC`, `VOID`, `WASTE_AWAY`, `WITHER`, `WOUND` (+ le placeholder).
   Ce sont des cartes **temporaires** : elles ne restent pas dans le deck final, donc le corpus ne
   peut pas les classer. Il faut les traiter par leur **effet décrit** (injouable, dégâts à la
   pioche, perte d'énergie…) et l'écrire comme tel.
3. **208 pouvoirs sans hook évaluable** (52 `code`, 4 `manual`, 204 sans aucun hook numérique
   parmi les 15 du catalogue) : leurs valeurs passent par `AfterTurnEnd`, `AfterCardPlayed` ou
   une `DynamicVar` réécrite en combat, hors du mini-langage. Les **25 multiplicateurs de
   dégâts** sont, eux, couverts : 21 pouvoirs (19 `code` + 2 `manual` : `Slow`, `Intercept`)
   + 4 reliques (`PenNib`, `UndyingSigil`, `VitruvianMinion`, `PaperPhrog`) documentées dans
   `data/catalog/power_constants.json`. Les « 16 énergies et 17 pioches » de `docs/33` ne se
   retrouvent pas tels quels dans le code : mesuré côté pouvoirs, **4 `ModifyMaxEnergy` +
   1 `ModifyEnergyGain`** et **8 `ModifyHandDraw`** (11 `code` + 1 `manual` : `MindRot`) ;
   côté reliques, 12 `ModifyMaxEnergy` et 10 `ModifyHandDraw`. Les deux chiffres de `docs/33`
   sont une estimation antérieure, pas une mesure.
4. **140 reliques sans tags** : le scoring des reliques repose sur les tags ; ces 140 sont
   sous-évaluées. À dériver des descriptions FR, comme les potions.
5. **37 rencontres sans statistiques** : pas de `avg_damage`/`avg_turns`. Le moteur doit dire
   « aucune mesure » et ne pas inventer de moyenne.

## Invariant garanti par les tests

- Toute carte du registre a un type connu (`attack`, `skill`, `power`, `curse`, `status`, `quest`).
- Aucune carte n'est sans effets (hors placeholder).
- **Toute malédiction et tout statut a un statut d'impact** : `measured` ou `no_data`, jamais absent.
- Les identifiants sont uniques par catégorie.
- Les comptes du registre ne peuvent pas dériver sans faire échouer la suite.

## Ce que ça change

Un score ne peut plus être « silencieusement faux » : si une entité n'est pas modélisée, elle est
listée, et le moteur applique une valeur par défaut **explicite** avec sa source. C'est la base
solide demandée : à jour, exhaustive, et vérifiable par une commande.
