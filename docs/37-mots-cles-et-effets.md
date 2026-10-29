# 37 — Mots-clés de carte et effets des malédictions

> Deux couches ajoutées le 2026-10-29 : les **7 mots-clés** du jeu et la **sévérité d'effet** des
> malédictions/statuts, qui prime sur le win rate observé quand celui-ci est faussé par un bundle.
> Générateurs : `tools/index_curse_effects.py`, `tools/build_registry.py`.

## 1. Les 7 mots-clés (source : `Entities/Cards/CardKeyword.cs`)

| Mot-clé | FR | Effet réel | Effet sur le score |
|---|---|---|---|
| `Exhaust` | Épuisement | quitte le combat après avoir été jouée | transitoire → pénalité ×0,6 |
| `Ethereal` | Éthéré | épuisée en fin de tour si encore en main | transitoire → ×0,6 |
| `Innate` | Inné | commence dans la main de départ | encombre le tour 1 |
| `Unplayable` | Injouable | ne peut pas être jouée | main bloquée |
| `Retain` | Retenue | reste en main en fin de tour | **avec Unplayable : ×1,4** (carte morte qui ne part jamais) |
| `Sly` | Rusée | si défaussée, revient au-dessus de la pioche | moins grave qu'une carte perdue |
| `Eternal` | Éternel | **ne peut être retirée du deck ni transformée** (`CardModel.IsRemovable`) | +20 % de pénalité, et **retrait impossible** |

Couverture : 156 cartes portent au moins un mot-clé ; **0 mot-clé inconnu** (vérifié par le
registre). Les 17 statuts et 18 malédictions sont couverts.

## 2. La modulation par mots-clés

```
pénalité finale = pénalité de base × (Retain+Unplayable ? 1,4 : Ethereal/Exhaust ? 0,6 : 1)
                              × (Eternal ? 1,2 : 1)
```

Exemples mesurés dans les tests :

| Carte | Mots-clés | Base | Finale |
|---|---|---:|---:|
| Poor Sleep | Retain + Unplayable | −25 | **−30** (plafond) |
| Normality | Unplayable | −25 | −25 |
| Regret | Unplayable | −20 | −20 |
| Injury | Unplayable | −17 | −17 |
| Clumsy | Ethereal + Unplayable | −14 | **−8** |
| Ascender's Bane | Ethereal + Unplayable + Eternal | −12 | −6 |

## 3. La couche « effet » (le cas Normality)

Le delta de win rate ne peut pas capturer un effet structurel : Normality (« vous ne pouvez pas
jouer plus de **3 cartes** ce tour ») mesure **+1,9 pp** parce que l'événement qui la donne offre une
contrepartie forte. Le delta mesure le **bundle**, pas la carte.

`data/catalog/curse_effects.json` classe chaque malédiction/statut par famille, avec une sévérité :

| Famille | Sévérité | Cartes |
|---|---:|---|
| `play_limit` | **95** | Normality (3 cartes), Sloth |
| `draw_reduction` | 85 | Mind Rot |
| `energy_loss` | 80 | Void, Waste Away |
| `end_turn_damage` | 75 | Burn, Decay, Infection, Toxic, Wither, Disintegration |
| `hp_loss` | 75 | Bad Luck, Regret |
| `debuff` | 55 | Doubt (Affaibli), Shame (Fragilisé) |
| `forced_order` | 50 | Enthralled |
| `gold_loss` | 45 | Debt |
| `self_removing` | 10 | Guilty |
| `unplayable_only` | 35 | Clumsy, Wound, Dazed, Soot… |

**Toxicité retenue = max(mesure, effet).** Normality passe donc de 41/100 à **95/100**, et sa
pénalité de +1,9 pp (bundle) à **−25 points** dans le score.

Classement corrigé (tête) :

```
BECKON      100   (statut, jeu limité)
SLOTH       100   (play_limit)
POOR_SLEEP   97   (Retain+Unplayable)
NORMALITY    95   (play_limit)
MIND_ROT     85   (draw_reduction)
VOID         80   (energy_loss)
DECAY/REGRET/BAD_LUCK 75
CLUMSY       53
```

## 4. Eternal : le retrait impossible

`CALLING_BELL → CURSE_OF_THE_BELL` est prouvé par co-occurrence (1 623/1 623). Mais `Curse of the
Bell` porte `Eternal` : **le marchand ne peut pas la retirer**. Le conseiller de retrait marque donc
la carte « Éternel : ne peut pas être retirée du deck » et ne la recommande jamais, même si elle est
la plus toxique. `Ascender's Bane` (A5+) est aussi Eternal.

## 5. Limites

- Les textes FR contiennent des placeholders (`{Damage}`, `{HpLoss}`) : la **valeur** exacte dépend
  de l'ascension/du personnage et n'est pas résolue. La **famille** et la sévérité, elles, sont
  fiables.
- 20 textes sur 33 sont disponibles ; les autres (Clumsy, Folly, Greed…) n'ont que des mots-clés,
  ce qui suffit pour les classer en `unplayable_only` mais pas pour deviner un effet chiffré.
- Les statuts restent traités à part (transitoires) ; la sévérité d'effet les concerne aussi, S27
  aligne leur barème.
