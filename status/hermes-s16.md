# S16 — Couche L2 « besoin immédiat » des cartes

Branche `hermes/s16-need`, worktree `~/wt-s16` (depuis `main` 446af2a). Livre la couche **L2** de
docs/27 §3 pour les **cartes** : le score d'une carte dépend maintenant de l'état de la run (PV,
menace du boss), pas seulement de sa note intrinsèque (L0) et de son fit au deck (L1).
`CardScorer.MaxFit` (20), `FitScale` (2,0) et le terme L0 sont **inchangés** : L2 est un terme
séparé qui s'ajoute, total toujours borné 0..100.

## Formule

```
besoin(carte, run) = min(15, besoin_PV + besoin_boss)          // 0..+15, aucun terme à 0 conservé

besoin_PV   = 0                                            si PV = max ou carte sans bloc/soin
            = clamp(round(10 × (0,50 − PV/MaxPV) / 0,30), 1, 10)   si PV < 50 % du max
besoin_boss = 5   si pire coup de l'acte > bloc/tour du deck ET carte de bloc
            = 0   sinon
```

| Constante | Valeur | Origine |
|---|---|---|
| `CardScorer.MaxNeed` | **15** | docs/27 §3 couche L2 (« 0..15 ») |
| `CardScorer.MaxHpNeed` | **10** | part des PV ; 10 + 5 = 15, aucun rognage caché |
| `CardScorer.BossPressureNeed` | **5** | « le coup va passer » (docs/27 §3, boss connu) |
| `CardScorer.HpComfortRatio` | **0,50** | au-dessus, le pool encaisse : aucun besoin |
| `CardScorer.HpCriticalRatio` | **0,20** | à ≤ 20 % des PV, besoin maximal (linéaire entre 50 % et 20 %) |

**Carte de bloc** = valeur de bloc chiffrée dans `data/catalog/cards_effects.json` **ou** tag `block`
de `CardTags` (Barricade n'a pas de bloc chiffré mais reste une carte de bloc).
**Carte de soin** = `vars["Heal"] > 0` au catalogue (jamais deviné au nom) : 2 cartes seulement
(Spur, Not Yet), c'est une limite de la donnée, pas de la règle.

Le **bloc/tour du deck** vient de `PlayerStrengthCalculator.Compute(ctx).Measures["blockPerTurn"]` :
une seule définition de cette mesure dans tout le dépôt. La **menace** vient de
`ResolveThreat(ctx, acte)` : boss exact si `ctx.BossId` se résout, sinon **médiane des boss de
l'acte** — la source du terme le dit explicitement dans les deux cas.

**Règle « rencontre multi-cibles » (cartes de zone +5) : non implémentée.**
`ScoringContext` ne porte aucune rencontre en cours (seulement `ActIndex`, `BossId`, `Ascension`) ;
la consigne était de ne pas inventer de champ. Aucune donnée ⇒ aucun terme. À reprendre quand le
contexte exposera la rencontre (ou `PartyView`).

## Exemples chiffrés (données réelles, `data/catalog`)

| Cas | Deck / PV | Sans L2 | Avec L2 | Termes L2 |
|---|---|---|---|---|
| **Taunt** (L0 **41**, C) | 12 × Big Bang, **70/70** | 64 B | **69 B** | coup du boss +5 (15 > 0) |
| **Taunt** (L0 **41**, C) | 12 × Big Bang, **20/70** | 64 B | **76 A** | PV bas +7, coup du boss +5 = **12** |
| **Stone Armor** (L0 **45**, C) | 12 × Big Bang, 70/70 | 63 B | **68 B** | coup du boss +5 |
| **Stone Armor** (L0 **45**, C) | 12 × Big Bang, **20/70** | 61 B | **73 A** | PV bas +7, coup du boss +5 |
| Impervious (L0 62) | 10 Defend (15 bloc/tour = pire coup 15), 20/70 | 64 B | **71 A** | PV bas +7 (pas de terme boss : le deck couvre) |
| Impervious (L0 62) | 10 Defend, 70/70 | 64 B | **64 B** | aucun |
| Impervious (L0 62) | 10 Strike (0 bloc), 70/70 | 82 A | **87 S** | coup du boss +5 (15 > 0) |
| Armaments (bloc) | deck sans bloc, **5/70** | — | **+15** | PV bas +10 (plafond), boss +5 (plafond L2) |
| Spur (soin) | deck sans bloc, 10/70 | — | **+10** | PV bas +10 (14 % des PV) |
| Molten Fist (attaque) | deck sans bloc, 5/70 | — | **0** | aucun (pas de bloc, pas de soin) |

Le test d'acceptation de Mio est `Low_meta_block_card_reaches_A_with_max_fit_and_low_hp` :
**Taunt, C-tier L0 41, fit maximal +20, besoin +12 → 76 (A)**. Sans L2 la même carte vaut 64 (B) :
c'est bien la couche d'état qui fait changer la lettre, pas le fit (dont l'amplitude n'a pas bougé).

## Note de deck : L2 exclue (docs/27 §2)

`DeckEvaluator` appelle `CardScorer.ScoreCard(card, ctx, includeNeed: false)` : la note de deck reste
**indépendante des PV**, sinon elle chuterait quand le joueur prend un coup. Les notes d'items
(récompense, shop, skip) gardent L2. Test : `Deck_note_ignores_the_need_layer` + l'ancien
`Deck_note_stays_stable_when_hp_changes` (vert, inchangé).

## Tests

`bash tools/hermes-verify.sh` → **PASS** (build `-warnaserror` propre, **143/143**, aucun champ
interdit, aucun stub, contrats intacts).

`Ultima.Engine.Tests/Scoring/NeedTests.cs` — 9 tests :

1. `Same_block_card_scores_higher_at_low_hp_than_at_full_hp` — Impervious 20/70 (**71 A**) vs 70/70
   (**64 B**), écart **7** (borné 5..10), terme nommé + source « PV 20/70 (29 %) → besoin de bloc » ;
2. `Hp_need_grows_as_hp_drops` — monotonie 34 → 21 → 14 → 7 PV, maximum 10 atteint à 20 % ;
3. `Low_meta_block_card_reaches_A_with_max_fit_and_low_hp` — **acceptation** : Taunt 41 + fit 20 +
   besoin 12 = **76 A**, `total − besoin < 70`, les deux termes L2 séparés (7 + 5) ;
4. `A_45_point_card_with_a_big_fit_plus_need_reaches_A` — le cas chiffré de docs/27 (45 + fit +
   besoin → **73 A**) ;
5. `Need_is_bounded_to_15_on_every_ironclad_card` — scan des 82 cartes Ironclad à 5/70 : chaque
   terme ∈ [1, 15], total ∈ [0, 15], sources non vides, plafond 15 réellement atteint (Armaments) ;
6. `Attack_card_without_block_has_no_need_even_at_low_hp` — Molten Fist à 50/20/5 PV : **aucun**
   terme ; Spur (soin chiffré) prend +10 ;
7. `No_need_term_at_full_hp` — deck couvert à 70/70 : aucun terme ; deck sans bloc : aucun terme
   « PV bas » (la règle boss est indépendante des PV) ;
8. `Boss_pressure_needs_a_hit_above_the_deck_block_per_turn` — 15 bloc/tour vs coup 15 : rien ;
   15 > 0 : +5 avec source « pire coup 15 > bloc/tour 0.0 — Waterfall Giant » ;
9. `Deck_note_ignores_the_need_layer` — note de deck identique à 70/70 et 15/70.

**Une assertion existante mise à jour** (`FitTests.Same_card_scores_higher_when_the_need_is_not_covered`) :
Impervious dans 10 × Strike passe de **82 A à 87 S** parce que L2 ajoute +5 (pire coup 15 > bloc/tour 0).
Justification en commentaire dans le test ; l'écart avec le deck couvert, lui, reste porté par le fit
(69 B vs 87 S), donc l'idempotence testée est intacte.

## Non prouvé

- **Seuils non calibrés** : 50 % / 20 % (barème linéaire), plafond 10 pour les PV et +5 pour le boss
  sont des choix de conception argumentés, pas des mesures. Le backtest docs/27 §5 (~785 k runs,
  19 M décisions) doit régler l'échelle — comme pour `FitScale` (S14) et les poids de G (S15).
- **« Proportionnel » est une hypothèse** : rien ne prouve qu'un besoin de bloc croît linéairement
  entre 50 % et 20 % des PV plutôt que par paliers (le barème des reliques, lui, est par bandes).
- **Le fit contient déjà la survie** : `G` inclut les PV effectifs, donc ΔG d'une même carte bouge
  légèrement avec les PV (comportement S14, non modifié). L2 est le seul terme *explicitement* d'état,
  mais la séparation L1/L2 n'est donc pas parfaitement orthogonale — à trancher au backtest.
- **Menace = médiane quand le boss est inconnu** : le +5 s'émet alors sur une référence d'acte, pas
  sur le boss réel. La source le dit (« médiane des N boss de l'acte 1 »), mais ce n'est pas le boss
  que le joueur va affronter.
- **Rencontre multi-cibles absente** : impossible sans champ dans `ScoringContext` (interdit d'en
  inventer un) ; les cartes de zone ne reçoivent donc aucun besoin contextuel de rencontre.
- **Soins quasi inexistants au catalogue** : `vars["Heal"]` n'est chiffré que pour 2 cartes ; la
  règle « carte de soin » est correcte mais couvre très peu de cartes réelles.
- **Le +5 « coup du boss » est presque toujours actif** : le bloc/tour du deck dépasse rarement le
  pire coup mesuré (même 25 cartes dont 15 Defend donnent 9,0 bloc/tour pour 15 de menace). Le terme
  discrimine donc peu et remonte collectivement les cartes de bloc ; c'est la règle demandée, mais
  son utilité réelle (et son ampleur) reste à valider au backtest.
- **PV ≥ 50 %** : aucune aide pour les cartes de bloc, même à 49 % avec un boss qui frappe fort hors
  du modèle (le terme boss reste, lui, actif).
