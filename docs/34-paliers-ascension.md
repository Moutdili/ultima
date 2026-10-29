# 34 — Les dix paliers d'ascension, vérifiés dans le code

> Source : `~/sts2/game/pck/src/Core` (fedora), patch v0.107.1. `AscensionManager.HasLevel(level)`
> renvoie `_level >= (int)level` : **l'ordre de l'enum est le numéro du palier**. Aucune table
> parallèle, aucune supposition.

## Table vérifiée

| Palier | Nom | Effet réel | Preuve |
|---:|---|---|---|
| 1 | `SwarmingElites` | élites **×1,6** (5 → 8 par acte) | `Map/MapPointTypeCounts.cs:14` |
| 2 | `WearyTraveler` | soin des Anciens **×0,8** (Neow ne rend que 80 % des PV manquants) | `Models/AncientEventModel.cs:180` |
| 3 | `Poverty` | or gagné **×0,75** | `Helpers/AscensionHelper.cs:14` (`PovertyAscensionGoldMultiplier = 0.75`) |
| 4 | `TightBelt` | **−1 emplacement de potion** | `Entities/Ascension/AscensionManager.cs:56` |
| 5 | `AscendersBane` | ajoute la malédiction **Ascender's Bane** au deck (étage 1) | `AscensionManager.cs:60` |
| 6 | `Inflation` | retrait de carte au marchand **100** (au lieu de 75), hausse de prix **50** (au lieu de 25) | `Entities/Merchant/MerchantCardRemovalEntry.cs:20,22` |
| 7 | `Scarcity` | rareté des cartes : rares **0,0149** (au lieu de 0,03), communes 0,615 (0,6), croissance 0,005 (0,01) | `Odds/CardRarityOdds.cs:13,31,33` |
| 8 | `ToughEnemies` | **PV** des ennemis → variante `ascension` | 101 fichiers, `AscensionHelper.GetValueIfAscension(..., MaxInitialHp)` |
| 9 | `DeadlyEnemies` | **dégâts** des ennemis → variante `ascension` | 96 fichiers |
| 10 | `DoubleBoss` | **deux boss** dans l'acte final | `Runs/RunManager.cs:686` |

Mesures corpus concordantes : à A8, les PV observés valent déjà la variante `ascension`
(Bygone Effigy 132, Ceremonial Beast 262, Crusher 219) ; les dégâts restent `normal` à A8 et
basculent à A9 (76 % de concordance sur 389 moves de replays). À A10, **70 % des runs ont deux
salles de boss en acte 3** (7 509 / 10 685).

## Conséquences pour le scoring (à brancher)

| Palier | Ce que ça doit changer dans la note |
|---:|---|
| 1 | Un chemin à plus d'élites : la valeur des élites (reliques, or) monte, le risque de chemin aussi |
| 2 | Le soin des Anciens rapporte moins → les cartes de soin et le repos prennent de la valeur |
| 3 | 25 % d'or en moins → les prix du shop doivent être confrontés à un or plus rare |
| 4 | Un slot de potion en moins → la valeur d'une potion baisse (le 3ᵉ slot n'existe pas) |
| 5 | Une malédiction dès l'étage 1 → dilution du deck, la pioche et le retrait valent plus |
| 6 | Retrait plus cher → enlever une carte coûte 100, l'arbitrage or/retrait change |
| 7 | Moins de rares → la qualité moyenne des récompenses baisse ; les cartes rares déjà obtenues valent plus |
| 8 | PV ennemis plus hauts → les seuils de létalité (`16x3`) changent, la valeur des dégâts aussi |
| 9 | Dégâts ennemis plus hauts → la défense et les PV effectifs valent plus |
| 10 | Deux boss consécutifs → le sustain (soin, bloc persistant, régénération) vaut plus que le burst seul |

## Ce qui reste à faire

1. Exposer ces paliers au moteur : `AscensionModifiers` (élites, or, potions, rareté, soin) lus depuis
   les constantes ci-dessus, jamais devinés.
2. Brancher `BossCount(act, ascension)` : deux rencontres de boss en acte 3 à A10.
3. Vérifier l'effet de `Scarcity` sur nos probabilités de récompense (0,0149 vs 0,03 : les rares sont
   **deux fois plus rares**, pas « un peu »).
4. Ne pas confondre : `ToughEnemies` (8) ne touche que les PV, `DeadlyEnemies` (9) que les dégâts —
   deux constantes, deux tests aux bornes.
