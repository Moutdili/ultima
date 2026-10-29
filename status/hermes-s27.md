# S27 — Statuts classés par effet décrit

> Branche `hermes/s27-status` (depuis main `0f27c6b`). Générateur :
> `tools/index_status_effects.py` → `data/catalog/status_effects.json` (commité, 17 entrées).
> Barème appliqué par `CurseScorer.Status` : −12 sévère, −6 modéré, −3 bénin,
> −6 **explicite** quand la famille est `unclassified`.

## Pourquoi l'effet décrit, pas la mesure

Les 17 statuts sont des cartes **temporaires** : ils ne restent pas dans le deck final. Le corpus
de 63 634 runs entrés en acte 3 ne les voit donc presque jamais (n = 0 à 3), et leurs rares
toxicités mesurées sont du bruit (Sloth : 1 run). Le seul classement possible est celui de leur
**effet décrit**. Le moteur appliquait un −6 forfaitaire à tous ; il distingue désormais :

- un statut qui inflige des dégâts chiffrés (Burn, Toxic…) ;
- une carte simplement injouable (Wound, Soot…) ;
- une carte qui s'épuise d'elle-même (Slimed, Debris) ;
- un placeholder sans aucune donnée (Deprecated Card).

## Familles (les 17 statuts)

| Statut | Famille | Famille fine (`effect_family`) | Valeur | Sévérité | Bande | Base |
|---|---|---|---:|---:|---|---:|
| BURN | damage_when_drawn | `end_turn_damage` | 2 dégâts | 75 | severe | −12 |
| INFECTION | damage_when_drawn | `end_turn_damage` | 3 dégâts | 75 | severe | −12 |
| WITHER | damage_when_drawn | `end_turn_damage` | 3 dégâts | 75 | severe | −12 |
| TOXIC | damage_when_drawn | `end_turn_damage` | 5 dégâts | 75 | severe | −12 |
| DISINTEGRATION | damage_when_drawn | `end_turn_damage` | 6 dégâts | 75 | severe | −12 |
| BECKON | hp_loss | `hp_loss` | 6 PV | 75 | severe | −12 |
| VOID | energy_loss | `energy_loss` | 1 énergie | 80 | severe | −12 |
| WASTE_AWAY | energy_loss | `energy_loss` | 1 énergie/tour | 80 | severe | −12 |
| MIND_ROT | draw_block | `draw_reduction` | 1 carte/tour | 85 | severe | −12 |
| SLOTH | other (limite de cartes) | `play_limit` | 3 cartes/tour | 95 | severe | −12 |
| FRANTIC_ESCAPE | other (fuite) | `escape` | — | 40 | moderate | −6 |
| DAZED | unplayable | `unplayable_only` | — | 35 | moderate | −6 |
| SOOT | unplayable | `unplayable_only` | — | 35 | moderate | −6 |
| WOUND | unplayable | `unplayable_only` | — | 35 | moderate | −6 |
| DEBRIS | other (s'épuise) | `self_removing` | — | 10 | benign | −3 |
| SLIMED | other (s'épuise) | `self_removing` | 1 carte piochée | 10 | benign | −3 |
| DEPRECATED_CARD | other | `unclassified` | — | — | unclassified | −6 explicite |

Aucune famille `exhaust_on_draw` n'est peuplée : **aucun statut du jeu ne s'épuise à la pioche**.
`Exhaust` sur Debris/Slimed s'applique quand la carte est **jouée** — elles se retirent d'elles-mêmes,
d'où `self_removing` (bénin) et non « encombrement ».

## Barème

```
sévérité ≥ 70   → severe        → −12
35 ≤ sev < 70   → moderate      → −6
1 ≤ sev < 35    → benign        → −3
sévérité nulle / famille `unclassified` → −6 explicite, jamais 0
```

Les seuils sont dans `tools/index_status_effects.py` (`BANDS`, `SEVERE_MIN`, `MODERATE_MIN`) et
dans `CurseScorer` (`StatusSeverePenalty`, `StatusPenalty`, `StatusBenignPenalty`) ; les tests
`StatusTests.Severe_and_benign_families_use_their_own_bands` les verrouillent.

La modulation par mots-clés de S26 (docs/37) s'applique **après** la bande : `Ethereal`/`Exhaust`
×0,6, `Retain`+`Unplayable` ×1,4, `Eternal` ×1,2. Exemples :

| Statut | Base | Modulation | Finale |
|---|---:|---|---:|
| BURN (Unplayable) | −12 | aucune | **−12** |
| TOXIC (Exhaust) | −12 | ×0,6 | **−7** |
| DAZED (Ethereal + Unplayable) | −6 | ×0,6 | **−4** |
| WOUND (Unplayable) | −6 | aucune | **−6** |
| SLIMED (Exhaust) | −3 | ×0,6 | **−2** |
| SLOTH (aucun mot-clé) | −12 | aucune | **−12** |

## D'où viennent les valeurs

- **Texte FR** : `data/catalog/curse_texts.json` (extrait de `pck/localization/fra/cards.json`,
  même source que la couche effet de docs/37). `cards_effects.json` ne contient **pas** de
  description aujourd'hui : le générateur lit donc le texte dans `curse_texts.json` et les
  données numériques dans `cards_effects.json`. Si une version ultérieure de
  `cards_effects.json` ajoute `description_fr`, ce champ prime automatiquement.
- **Valeur** : les textes FR portent des placeholders (`{Damage:diff()}`, `{HpLoss:diff()}`).
  La valeur est résolue depuis les `vars` de la carte (`Damage`, `DisintegrationPower`,
  `HpLoss`, `Energy`, `WasteAwayPower`, `MindRotPower`, `SlothPower`, `Cards`), jamais estimée.
  Si aucune donnée chiffrée n'existe → `value: null`.
- **Liste des 17** : `registry.json` (`entities.card` de type `status`), y compris le placeholder
  `DEPRECATED_CARD` absent de `cards_effects.json` et de `curse_impact.json`. Le test
  `All_17_statuses_have_a_classification_and_no_unknown` compare les deux ensembles.

## Exemples chiffrés (vérifiés par les tests)

- `BURN` : « subissez 2 dégâts » → `damage_when_drawn`, valeur 2, sévérité 75 → **−12**.
- `TOXIC` : « subissez 5 dégâts » → valeur 5, sévérité 75, puis ×0,6 (Exhaust) → **−7**.
- `WOUND` : injouable sans autre effet → `unplayable`, sévérité 35 → **−6**.
- `SLOTH` : « pas plus de 3 cartes par tour » → `play_limit`, sévérité 95 → **−12**.
- `SLIMED` : « Piochez 1 carte », puis s'épuise → `self_removing`, sévérité 10 → base −3,
  finale **−2**.
- `DEPRECATED_CARD` : aucune description ni donnée → bande `unclassified` → **−6 explicite**.

## Non prouvé / limites assumées

1. **Textes FR manquants** : `curse_texts.json` ne fournit pas le texte de BECKON, DAZED, SOOT,
   WOUND, DEBRIS ni DEPRECATED_CARD. BECKON est classé par ses données (`vars.HpLoss = 6`), les
   autres par leur mot-clé (`Unplayable`, `Exhaust`) ou restent `unclassified` (placeholder).
   Aucune phrase n'est inventée : `description_fr` vaut `null` dans ce cas.
2. **Valeur de base, pas par ascension** : `vars` donne la valeur de base du jeu. Les paliers
   d'ascension peuvent modifier un chiffre (le placeholder `diff()` existe pour cela) ; la
   famille et la sévérité ne changent pas, la valeur affichée est donc la valeur de base.
3. **`SLIMED` / `DEBRIS` ne sont peut-être pas des handicaps** : Slimed pioche 1 carte pour 1
   énergie puis s'épuise. La bande bénigne (−3, finale −2) est un plancher prudent, pas une
   mesure d'infériorité.
4. **`FRANTIC_ESCAPE`** : « Éloignez-vous, augmentez Sables mouvants de 1, augmentez le coût de
   cette carte de 1 ». La sémantique de `Sandpit` n'est pas modélisée ; la famille `escape`
   (sévérité 40) est une classification prudente, pas une quantification.
5. **Aucune mesure de corpus** : n = 0 à 3 pour tous les statuts. Le barème est **normatif**
   (effet décrit), pas observationnel. Il ne prétend pas prédire un delta de win rate.
6. **La couche effet de docs/37 et ce classement divergent sur BECKON** : `curse_effects.json`
   le laisse `unclassified` (texte FR absent) ; `status_effects.json` le classe `hp_loss` par
   `vars.HpLoss = 6`. Le score des statuts lit `status_effects.json`, donc BECKON vaut −12 et
   non −6. Écart documenté, pas silencieux.

## Fichiers

| Fichier | Rôle |
|---|---|
| `tools/index_status_effects.py` | classement reproductible des 17 statuts |
| `data/catalog/status_effects.json` | catalogue commité (famille, valeur, sévérité, bande) |
| `Ultima.Engine/Scoring/Curses/StatusCatalog.cs` | chargement tolérant du catalogue |
| `Ultima.Engine/Scoring/Curses/CurseScorer.cs` | branche `CurseKind.Status` : bande + libellé citant le texte |
| `Ultima.Engine.Tests/Scoring/StatusTests.cs` | 4 tests (exhaustivité, dégâts > injouable, non classé −6, bandes) |

`CurseKind.Curse`, `CardScorer.cs`, `ThreatModel.cs`, `Contracts/**` et `Saves/**` ne sont pas
modifiés par cette tâche.
