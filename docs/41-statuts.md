# 41 — Statuts : classement par effet et barème

> Les 17 statuts (cartes temporaires de combat) sont classés par **effet décrit**, pas par win
> rate : ils ne restent jamais dans le deck final, donc le corpus ne peut pas les mesurer.
> Générateur : `tools/index_status_effects.py` → `data/catalog/status_effects.json`.
> Moteur : `CurseScorer.Status` (bandes) + `StatusCatalog`. Tests : `StatusTests.cs` (4).

## Familles et sévérité

| Famille | Statuts | Sévérité | Pénalité |
|---|---|---:|---:|
| `play_limit` | Sloth (3 cartes/tour) | 95 | **−12** |
| `damage_when_drawn` | Burn 2, Infection 3, Wither 3, Toxic 5, Disintegration 6 | 75 | **−12** |
| `hp_loss` | Beckon (6 PV/tour) | 75 | **−12** |
| `energy_loss` | Void (perd 1 énergie), Waste Away (−1/tour) | 80 | **−12** |
| `draw_block` | Mind Rot (−1 carte/tour) | 85 | **−12** |
| `unplayable` | Dazed, Soot, Wound | 35 | **−6** |
| `escape` | Frantic Escape | 40 | **−6** |
| `self_removing` | Debris, Slimed (partent au tour suivant) | 10 | **−3** |
| `other` / non classé | `DEPRECATED_CARD` | — | **−6 explicite** |

Barème : **sévère −12**, **modéré −6**, **bénin −3**. Un statut non classable prend **−6
explicite** (« forfait explicite, jamais 0 »), et la modulation par mots-clés s'applique ensuite
(par exemple `Toxic` = `Exhaust` → ×0,6).

## Pourquoi −12 et pas −25

Un statut est **transitoire** : il disparaît au tour suivant ou ne revient pas au prochain combat.
Une malédiction permanente (Poor Sleep −30) pèse donc plus lourd, même à effet comparable. La
hiérarchie est vérifiée par test : `Burn` (statut, −12) < `Injury` (malédiction, −17).

## Limites

- Textes FR absents pour Beckon, Dazed, Soot, Wound, Debris et `DEPRECATED_CARD` :
  classement par `vars` et mots-clés, avec `description_fr: null`.
- **Écart assumé** : `curse_effects.json` laisse Beckon `unclassified` ; `status_effects.json` la
  classe `hp_loss` via `vars.HpLoss = 6`. Le score des statuts lit `status_effects.json`.
- Valeurs = base du jeu, **non résolues par palier d'ascension**.
- Aucun statut ne s'épuise à la pioche (`exhaust_on_draw` vide).
