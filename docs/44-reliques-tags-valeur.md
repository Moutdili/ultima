# 44 — Reliques : note de repli par tags

> Revue du chef (docs/42, point 2) : 304 reliques taguées, `RelicScorer` n'en lisait que 7 —
> **163 reliques taguées sortaient muettes**. Elles reçoivent maintenant une note de repli
> **bornée**, calculée sur leurs tags, uniquement quand aucune mesure communautaire n'existe.

## Barème (`Ultima.Engine/Scoring/Relics/RelicTagValues.cs`)

Points par tag, ordonnés par impact réel observé :

    revive 8 · maxhp 6 · damage 5 · strength/energy/draw/mitigation/debuff 4
    block/heal/gold/upgrade/remove/transform/aoe/cost 3
    reward/potion/relic/rest/path/elite/orb/copy/dexterity/poison/thorns/summon/curse/enchant 2
    exhaust/retain/discard/add_card/shop/star/focus/forge 1
    ethereal 0 · no_effect 0 · penalty −4

Règles :

- **Plafond 55 (B)** : une règle de tag ne bat jamais une relique réellement notée (`MaxTagScore`).
  Au-delà, un terme « Plafond des tags » porte l'écart exact, donc la somme des termes reste égale
  au total.
- **Plancher 0** : un tag négatif (`penalty`) ne rend pas le score négatif — le contrat borne 0..100.
- **Chaque terme porte le tag et le début de la description FR du jeu** : pas de chiffre sans source.
- **Confiance `low`** : c'est une règle, pas une mesure.
- Le barème ne s'applique **que** si la relique n'a pas de consensus communautaire, ou si elle est
  absente de `data/meta/relic_tiers.json`.

## Résultat mesuré

    avant   163 reliques taguées sans note
    après   303 / 304 reliques notées
            seule exception : DEPRECATED_RELIC (artefact de développement, sans effet)

Circlet (« C'est un diadème. ») reste notée **73 par la méta communautaire** : la communauté note
la blague, ce n'est pas au moteur de la corriger.

Tests : `RelicTagTests` (6) + `RelicCoverageTests` (2, invariant « toute relique avec un effet a une
note » et « une note de tags ne dépasse jamais B »).
