# S14 — Fit au deck (ΔG) dans le score des cartes et des reliques

Branche `hermes/s14-fit`, worktree `~/wt-s14` (depuis `main` 6ea9c90). Livre la couche **L1** de
docs/27 §3 : un objet médiocre en général mais parfait pour CE deck remonte. L0 (méta/rang) est
**inchangé** et reste la base ; le fit est un terme séparé qui s'ajoute, total toujours borné 0..100.

## Formule

```
carte   : fit = clamp(round(2,0 × ΔG), −20, +20)      ΔG = G(deck + carte) − G(deck)
relique : fit = clamp(round(2,5 × ΔG), −15, +15)      ΔG = G(reliques + relique) − G(reliques)
terme   : « Fit au deck (ΔG) », source = dimensions et mesures qui bougent
```

| Constante | Valeur | Origine |
|---|---|---|
| `CardScorer.MaxFit` | **20** | docs/27 §3 couche L1 (« ±20 ») |
| `CardScorer.FitScale` | **2,0** | docs/27 §7bis écrit `note ≈ 2·ΔG + prior` |
| `RelicScorer.MaxFit` | **15** | demande S14 (plus étroit que les cartes : une relique ne gagne pas un combat seule) |
| `RelicScorer.FitScale` | **2,5** | ΔG des reliques plus petit (tags, pas de valeurs) : max mesuré 6 → 15 |
| `MinDeckForFit` | **5** | sous ce seuil, G est dominé par la dilution (un deck d'1 carte la joue chaque tour) |

G vient de S11 (`PlayerStrengthCalculator.Compute`) : `Survie/Offense/Consistance/Couverture`
pondérées par acte, dimensions **saturantes** — d'où l'idempotence (2ᵉ source de bloc ≈ 0).
Nouvelles API : `DeltaForCardDetailed` / `DeltaForRelicDetailed` (retournent `StrengthDelta`
avant/après et son `Describe()`), `DeltaForCard` et `DeltaForRelic` restent des `int`.
Une relique **déjà possédée** ⇒ ΔG 0 (aucun double comptage) ; deck vide ou < 5 cartes ⇒ aucun terme.

## Exemples chiffrés (données réelles)

| Cas | Deck | Sans fit (L0+autres) | Avec fit | Terme |
|---|---|---|---|---|
| Impervious (30 bloc) | 10 × Strike Ironclad | **62 B** | **82 A** | +20 (ΔG +11 : survie 23→53, bloc/tour 0,0→7,5) |
| Impervious | 10 Strike + **15 Defend** | 62 B | **62 B** | **aucun** (bloc déjà couvert : idempotence) |
| Impervious (30 bloc) | 4 Molten Fist + 4 Bully + 4 Tremble | 65 B | **85 S** | +20 (ΔG +13 : survie 23→56, bloc/tour 0,0→9,0) |
| Crash Landing (21 en zone, L0 50 = C) | 12 × Big Bang (0 dégât) | 53 C | **73 A** | +20 (ΔG +11 : offense 0→24, couverture 8→49) |
| Pact's End (L0 55) | 10 × Defend Ironclad | 55 B | **73 A** | +18 (ΔG +9 : offense 0→12, couverture 5→49) |
| Ancre (relique, +2 bloc/tour) | 10 × Strike Ironclad | 55 B | **68 B** | +13 (ΔG +5 : survie 23→37) |
| Foreuse à main (relique) | 10 × Strike Ironclad | 20 F | **35 D** | +15 (ΔG +6, plafond) |

Amplitudes mesurées (avant plafond, 82 cartes Ironclad × 3 decks, 304 reliques) :

- ΔG cartes : **−2 … +11** (moyenne +0,3 à +1,4 selon le deck) — +11 = Impervious dans un deck sans bloc ;
- ΔG reliques : **−1 … +6** (moyenne +0,5 ; 86/304 reliques seulement bougent) — ×2,5 = plafond 15 ;
- le plafond ±20 est donc **atteignable** (Impervious, Crash Landing, Grand Finale, Bulwark…) sans être banal :
  la plupart des cartes restent à ±4.

## S atteignable ?

Oui, mais **pas par le fit seul** pour une carte de bas de tableau : L0 descend à 20 et le plafond
est +20, donc une carte de L0 < 50 ne peut pas dépasser ~69 (B). Le S exige fit **+ un socle réel** :
Impervious L0 62 + fit 20 + courbe de coût 3 = **85 S**. C'est la lecture de docs/27 §3
(« 45 + 20 + besoin 10 = 75 A ») : le fit est un des termes, pas le total.
Le cas « C-tier → A » de Mio est réalisé : **Crash Landing, L0 50 (C), 73 (A)**.

## Tests

`bash tools/hermes-verify.sh` → **PASS** (build `-warnaserror` propre, **134/134**, aucun champ
interdit, aucun stub, contrats intacts). 125 tests existants verts, aucun seuil déplacé.

`Ultima.Engine.Tests/Scoring/FitTests.cs` — 9 tests :

1. `Same_card_scores_higher_when_the_need_is_not_covered` — Impervious 82 A (deck sans bloc) vs
   62 B + aucun terme de fit (deck couvert) : idempotence ;
2. `Low_meta_card_reaches_A_through_fit` — Crash Landing L0 50 (C) → 73 (A), et
   `total − fit < 70` : c'est bien le fit qui fait changer de lettre ;
3. `S_cap_is_reachable_when_a_perfect_fit_adds_to_a_real_base` — Impervious → 85 S, fit au
   plafond, `total − fit < 85` ;
4. `Card_fit_is_bounded_to_20_points` — scan des 82 cartes Ironclad × 3 decks : tout terme dans
   [−20, +20], source non vide, > 20 termes observés ;
5. `Relic_fit_is_bounded_to_15_points_and_lifts_a_hole_filler` — borne ±15, Ancre > 0 et source
   citant `bloc/tour`, relique déjà possédée ⇒ aucun fit ;
6. `Empty_deck_has_no_fit_term` — deck vide (et deck < 5 cartes) ⇒ aucun terme, cartes et reliques ;
7. `Meta_term_does_not_depend_on_the_deck` — L0 identique quel que soit le deck (L0 reste la base) ;
8. `Total_stays_bounded_even_with_a_maxed_fit` — 0..100 sur les 82 cartes ;
9. `Delta_is_computable_on_an_empty_deck_but_not_scored` — ΔG calculable, terme non émis.

Performance : le test `Deck_de_60_cartes_reste_sous_5ms` reste vert. ΔG coûtant deux G par carte,
`CardScorer` mémoïse les ΔG **par instance de contexte** (`ConditionalWeakTable`, clé = référence,
`DeckEvaluator` passe le même contexte pour les 60 cartes). Sans ce cache : 8,8 ms/appel mesurés ;
avec : sous les 5 ms.

## Non prouvé

- **Poids non calibrés** : `FitScale` 2,0 / 2,5 et `MinDeckForFit` 5 sont des choix de conception
  (docs/27 §7bis pour le 2), pas des mesures. Le corpus S9 (~785 k runs, 19 M décisions) n'est pas
  encore utilisé : c'est lui qui doit régler l'échelle et le plafond.
- **Reliques en tags** : `RelicBlockPerTurn`, `RelicDrawPerTurn`… (S11) sont des ordres de grandeur,
  pas les valeurs du jeu. Le fit des reliques hérite de cette imprécision — il peut sous-évaluer une
  relique dont l'effet réel est plus fort que son tag.
- **Modèle de débit** : G suppose des cartes jouées au hasard (pas de priorisation), donc ΔG
  sous-estime les decks qui jouent bien leurs meilleures cartes. Le biais est commun, la
  comparaison reste utilisable, la valeur absolue non.
- **Petits decks exclus** : décision assumée (dilution), mais elle crée un angle mort : à 4 cartes,
  l'outil ne dit rien du fit — cohérent avec « pas de courbe sur un deck naissant ».
- **L2 (or, besoin) et L3 (co-op)** non traités : `PartyView` / `TeamAdvisor` restent à faire.
- Le raffinement « la carte est déjà dans le deck » (ΔG d'un doublon) n'est pas distingué du cas
  « carte offerte » : pour une note de deck, c'est la valeur marginale d'un doublon qui est mesurée.
