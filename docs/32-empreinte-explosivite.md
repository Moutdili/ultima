# 32 — Empreinte de run, courbe de croissance et explosivité

> Mesuré le 2026-10-29 sur 50 000 runs gagnées + 50 000 perdues (1,06 M + 0,56 M combats).

## 1. Correction majeure : les combats sont datés

Chaque étage porte un champ `rooms[]` que l'indexeur initial ignorait :

```json
{"model_id": "ENCOUNTER.NIBBITS_WEAK",
 "monster_ids": ["MONSTER.NIBBIT", "MONSTER.NIBBIT", "MONSTER.NIBBIT"],
 "room_type": "monster",
 "turns_taken": 5}
```

Conséquences :
- **`turns_taken`** = durée réelle du combat, pour toutes les runs (pas seulement les 8 % avec replay) ;
- **`model_id`** = rencontre exacte → jointure directe avec `encounter_stats` (`avg_turns`, `avg_damage`) ;
- **`monster_ids` avec doublons** → corrige la limite « MaxHit ignore les copies » (3 exosquelettes = 3 fois les dégâts).

## 2. Ce que la data dit : les gagnants encaissent, ils ne rushent pas

Agrégats par acte (tous combats confondus) :

| Acte | Gagnants : tours/combat | Perdants | Gagnants : dégâts/combat | Perdants | Écart dégâts |
|---|---|---|---|---|---|
| 1 | 4,8 | 5,0 | **8,3** | 11,6 | −28 % |
| 2 | 4,7 | 5,1 | **11,8** | 18,0 | −34 % |
| 3 | 4,7 | 5,1 | **12,5** | 21,8 | −43 % |

- La **vitesse** ne sépare presque pas (4,7-5,0 tours vs 5,0-5,1) : l'écart est de 4 à 8 %.
- Les **dégâts subis** séparent fortement, et l'écart grandit avec l'acte.
- Corrélation vitesse ↔ dégâts à l'acte 3 : **+0,59** (les combats rapides sont aussi ceux où l'on encaisse peu), mais la puissance finale du deck ne corrèle ni à l'un ni à l'autre (0,03 et −0,08).

**Conséquence pour l'algo** : la récompense doit porter d'abord sur les **dégâts évités** (survie), la vitesse en second. Cela converge avec la calibration (S15 : Survie 80-92 % du poids) et contredit l'hypothèse « explosivité = tuer vite ».

## 3. Le corpus contient 38 % de doublons exacts

- 10 000 runs gagnées → **6 212 empreintes distinctes** (62,1 %), plus gros groupe : **12 copies**.
- Le taux ne bouge pas si l'on ne garde que 10 % des cartes (59,9 %) : les doublons sont des **copies intégrales** (même deck, mêmes reliques, mêmes PV, mêmes tours par acte), pas des builds convergents.
- Les hash sont pourtant tous différents → doublons de contenu, pas de hash.

**Conséquence** : dédupliquer avant tout calcul statistique, y compris la calibration des poids (S15 a travaillé sur 71 634 runs potentiellement gonflés). Un doublon ne crée pas de signal, il double le poids d'une observation.

## 4. Contraintes d'ennemis : confirmées dans le code source

Recherche dans `~/sts2/game/pck/src/Core` (fedora) :

| Mécanique | Fichier | Effet |
|---|---|---|
| `IntangiblePower.ModifyDamageCap` | `Models/Powers/IntangiblePower.cs` | plafonne les dégâts subis à **1** |
| `HardToKillPower.ModifyDamageCap` | `Models/Powers/HardToKillPower.cs` | plafonne à `Amount` |
| `TestSubject.RespawnMove` | `Models/Monsters/TestSubject.cs` | **respawn 2 fois** (`Respawns >= 2` → disparaît) |
| `SoulFysh`, `Exoskeleton` | `Models/Monsters/` | appliquent Intangible / HardToKill |

C'est exactement l'argument de Mio : une stratégie « empiler du mana et frapper N fois » ne marche pas contre un plafond par tour. Ces contraintes doivent être **par rencontre**, pas globales.

## 5. Ce qui reste à construire

1. **Déduplication** du corpus par signature de contenu (deck + reliques + trajectoire) → index propre.
2. **Expression par run** : courbe de puissance cumulée par étage (pente, courbure, paliers d'acte) + profil d'explosivité (tours et dégâts par combat, comparés à la moyenne de la rencontre).
3. **Clustering** des expressions → archétypes (explosif, tanky, scaling, hybride) et poids par acte.
4. **Catalogue de contraintes** par rencontre (plafond de dégâts, respawn, représailles) issu du code source.
5. **Test d'identification** : après dédup, vérifier que l'empreinte (deck + trajectoire) est unique, puis mesurer la reconnaissance à partir d'un état partiel.

## 6. Limites

- Biais de survie : les moyennes des gagnants sont conditionnelles à la victoire ; l'acte 1 (avant sélection) est le point de comparaison le plus propre.
- `encounter_stats.avg_turns` couvre toutes les ascensions et tous les joueurs : le ratio par run `avg_turns / turns_taken` a une médiane de 0,88 à l'acte 1, ce qui suggère un décalage de périmètre (salles multiples, ascensions). Les agrégats bruts (tours/combat) sont plus fiables que ce ratio.
- Les tours par combat sont connus, mais pas la séquence des cartes jouées (sauf 8 % de replays).
