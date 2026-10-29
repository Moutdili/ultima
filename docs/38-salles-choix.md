# 38 — Salles « ? » : classement des choix d'événement

> Les salles `unknown` (le point d'interrogation) ne sont pas des combats : ce sont des
> **événements à options**, souvent des choix difficiles (une relique contre des PV, de l'or
> contre une malédiction, un retrait contre un max de vie). Ce document décrit le classement.
>
> Générateur : `tools/events/measure.py` → `data/calibration/events.json` (commité, 50 Ko).
> Librairie : `Ultima.Engine/Scoring/Events/{EventCatalog,EventScorer}.cs` (6 tests).

## Mesure

80 000 runs **solo** dédupliqués (seed, personnage, ascension) → **321 076 choix** →
**199 options** avec profil. Pour chaque option, on agrège les deltas observés dans la salle :

- `avg_hp_delta` (soins − dégâts)
- `avg_gold_delta`
- `relic_rate`, `card_gain_rate`, `card_remove_rate`, `curse_rate`

Le **win rate par option est enregistré mais n'entre pas dans le score** : les joueurs qui
prennent les options risquées sont déjà plus forts (biais de sélection). Un joueur qui prend
`LINGER` ( −23 PV) a un meilleur win rate que celui qui s'abstient — ce n'est pas l'option qui le
rend fort, c'est son niveau.

## Score

```
score = 50
      + or × 0,06                      (prudent : 100 or ≈ 6 points)
      + relique × 8                    (une relique vaut ~8 points)
      + cartes × 3 (plafond 3 cartes)
      + retrait × 6                    (≈ 75 or)
      + PV gagnés × 0,2
      − PV perdus × 0,35 (× 2 si vie < 50 %)
      − malédiction × 20
```

Le **coût des PV double sous 50 % de vie** : perdre 14 PV à 70/70 n'est pas le même choix qu'à
10/70. C'est ce qui rend le classement contextuel.

## Exemples mesurés

| Événement / option | Profil | Score |
|---|---|---:|
| `SUNKEN_TREASURY` / `SECOND_CHEST` | +334 or, 1 carte | **73** |
| `THIS_OR_THAT` / `ORNATE` | relique 100 % | **58** |
| `ROOM_FULL_OF_CHEESE` / `SEARCH` | relique 100 %, −13,9 PV | 53 (pleine vie) / **50** (blessé) |
| `SLIPPERY_BRIDGE` / `OVERCOME` | retrait 100 %, −5,4 PV | 56 |
| `SLIPPERY_BRIDGE` / `HOLD_ON_1` | retrait 99 %, −11,5 PV | 53 |
| `ABYSSAL_BATHS` / `ABSTAIN` | +9,8 PV | 52 |
| `ABYSSAL_BATHS` / `LINGER` | −23,5 PV | 46 |

## Comportement quand la donnée manque

Une option sans profil (événement récent, mode custom) n'est **jamais devinée** : score neutre
**50**, `Measured = false`, et le texte dit « aucune mesure — à toi de juger ». Elle est classée
après les options mesurées à score égal.

## Limites

- Les événements à plusieurs pages enregistrent **plusieurs options** pour une même salle ; le
  profil est la moyenne de ces choix, pas une option isolée.
- Les valeurs (`{Damage}`, `{MaxHp}`) dépendent de l'ascension et ne sont pas résolues : c'est la
  **moyenne mesurée** qui sert, pas le texte.
- `max_hp_gained/lost` n'est pas encore agrégé (une option qui donne +5 PV max vaut plus que son
  seul soin) — ajout prévu.
- Le profil est une moyenne sur tous les contextes ; le score corrige les PV, pas le reste du deck.

## Note sur les types de salle

`data/calibration/rooms.json` mesure aussi les salles par type (monster, elite, rest_site, shop…).
Le win rate par type y est **fortement biaisé** (élite 25 % > monstre 18 % : les joueurs qui
prennent les élites sont meilleurs). Seuls les **dégâts subis** et les **deltas d'or** y sont
utilisables ; ce fichier ne sert pas encore de score de salle.
