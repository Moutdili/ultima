# 36 — Registre exhaustif des entités et couverture mesurée

> Générateur : `tools/build_registry.py` → `data/catalog/registry.json` (164 Ko, commité).
> Tags des reliques : `tools/index_relic_tags.py` → `data/catalog/relics.json` + `relic_tags.json`.
> Test d'invariant : `Ultima.Engine.Tests/RegistryCoverageTests.cs` (5 tests) et
> `RelicTagCoverageTests.cs` (5 tests).
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
| Pouvoirs avec au moins un hook évaluable | 49 | 260 |
| Monstres avec contrainte spéciale | 3 | 115 |
| Rencontres avec statistiques observées | 50 | 87 |
| Reliques avec tags | **304** | 304 (dont 4 `no_effect` justifié) |

## Les trous, nommés

1. **1 malédiction sans mesure** : `BAD_LUCK` (jamais acquise avant l'acte 3 dans le corpus) →
   pénalité prudente explicite, jamais 0.
2. **14 statuts sans mesure** : `DAZED`, `DEBRIS`, `DISINTEGRATION`, `FRANTIC_ESCAPE`, `INFECTION`,
   `MIND_ROT`, `SLIMED`, `SOOT`, `TOXIC`, `VOID`, `WASTE_AWAY`, `WITHER`, `WOUND` (+ le placeholder).
   Ce sont des cartes **temporaires** : elles ne restent pas dans le deck final, donc le corpus ne
   peut pas les classer. Il faut les traiter par leur **effet décrit** (injouable, dégâts à la
   pioche, perte d'énergie…) et l'écrire comme tel.
3. **211 pouvoirs sans hook évaluable** : leurs valeurs passent par `DynamicVars` ou des branches
   conditionnelles. À traiter en priorité : les **25 multiplicateurs de dégâts**, les 16 énergies et
   les 17 pioches — ce sont eux qui changent un classement.
4. ~~**140 reliques sans tags**~~ : **résolu** — `tools/index_relic_tags.py` dérive les tags des
   descriptions FR (40 règles, 41 tags, gardes contre les déclencheurs) et écrit
   `data/catalog/relic_tags.json` (vocabulaire + règle + preuve par relique). 304/304 reliques ont
   un tag, dont **4 `no_effect` justifiés** (CIRCLET, DEPRECATED_RELIC, FAKE_MERCHANTS_RUG,
   WONGO_CUSTOMER_APPRECIATION_BADGE). Limite mesurée : le scoring ne convertit que 8 tags en ΔG
   (`block`, `draw`, `energy`, `heal`, `strength`, `debuff`, `curse` + `maxhp`/`revive` pour
   `Needs`), donc **141/304** reliques restent invisibles du scorer ; étendre la conversion est un
   travail de `PlayerStrength`/`RelicScorer`, hors de cette passe (cf. `status/hermes-s26.md`).
5. **37 rencontres sans statistiques** : pas de `avg_damage`/`avg_turns`. Le moteur doit dire
   « aucune mesure » et ne pas inventer de moyenne.

## Invariant garanti par les tests

- Toute carte du registre a un type connu (`attack`, `skill`, `power`, `curse`, `status`, `quest`).
- Aucune carte n'est sans effets (hors placeholder).
- **Toute malédiction et tout statut a un statut d'impact** : `measured` ou `no_data`, jamais absent.
- **Toute relique a au moins un tag** (jamais de liste vide) : `Ultima.Engine.Tests/RelicTagCoverageTests.cs`
  vérifie aussi qu'aucune relique n'est inconnue du registre de dérivation, que chaque tag est
  déclaré au vocabulaire, que chaque règle a un témoin, et que le nombre de tags reste ≤ 8.
- Les identifiants sont uniques par catégorie.
- Les comptes du registre ne peuvent pas dériver sans faire échouer la suite.

## Ce que ça change

Un score ne peut plus être « silencieusement faux » : si une entité n'est pas modélisée, elle est
listée, et le moteur applique une valeur par défaut **explicite** avec sa source. C'est la base
solide demandée : à jour, exhaustive, et vérifiable par une commande.
