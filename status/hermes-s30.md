# Hermes — S30 : réponse à la revue du chef (2026-10-29 19:10)

> Branche `hermes/s30-scoring-fixes`, commit `8bb4f95`. **252 tests**, `hermes-verify` PASS.
> Vérifié **sur la machine de jeu** (fedora) avec `data/meta/local/` copié dans le worktree :
> 252/252 — la porte n'est plus en HOLD.

## Point par point

| # | Point du chef | État | Ce qui a changé |
|---|---|---|---|
| 1 | [P0] F 0 silencieux sans données | **corrigé** | `MetaTable.IsLoaded`, `DataPath`, `Problem` ; `Scorer.DataStatus` (+ `Degraded`) |
| 2 | [P1] Shop ≠ récompense | **corrigé** | `ScoreShopItem(card)` = `ScoreCard` complet + terme économie à 0 point |
| 3 | [P1] « trop cher » ≠ mauvais | **corrigé** | plus de −20 ; `Non abordable` devient un terme à 0 point avec preuve |
| 4 | [P1] potions neutres au-dessus | **corrigé** | plafond `PotionCap = 45`, `Confidence.Unknown`, terme « non comparable » |
| 5 | [P2] note du deck peu informative | **ouvert** | demande une table de percentiles par acte/étage (à produire depuis le corpus) |
| 6 | [P2] `DeltaG` jamais rempli | **corrigé** | vient du fit, signé et non arrondi ; malédiction toujours négative |
| 7 | [P2] retrait : deux vérités | **partiel** | la carte à retirer vient du `RemovalAdvisor` (nom, gain, seuil) ; le total reste la formule marchand |
| 8 | [info] AUC 0,66-0,69 | inchangé | fit borné ±20, la méta ne peut pas être écrasée |
| 9 | [info] corpus Spire Codex | inchangé | brut hors dépôt, agrégats seuls (déjà le cas) |

## Preuves (tests)

- `DataStatusTests.Empty_dir_is_reported_instead_of_scored_zero` : répertoire vide → `IsLoaded == false`,
  `Problem` = « … introuvable », `DataStatus.Degraded` non nul.
- `ShopTests.Shop_card_letter_is_the_reward_letter` : `ScoreShopItem(card).Total == ScoreCard.Total`
  et même lettre.
- `ShopTests.Unaffordable_item_keeps_the_letter_and_says_why` : 250 or sur 99 → note identique,
  terme `Non abordable` à **0** point.
- `ShopTests.Sale_price_is_scored_as_displayed_and_never_double_discounted` : 60 → 30 or, note
  identique, seule la ligne économie change.
- `ShopTests.Potion_is_capped_and_marked_not_comparable` : `Total ≤ 45`, `Confidence.Unknown`.
- `DataStatusTests.Signed_DeltaG_comes_from_the_fit_calculation` : ΔG non nul sur un deck saturé.
- `RegistryCoverageTests` : `relics_with_code` (297) + `relics_localization_only` (7) = `relics` (304).

## Tests indépendants de la machine

`TestEnvironment` (déjà chargé de neutraliser `ULTIMA_DATA`) pose maintenant
**`ULTIMA_META_NO_LOCAL=1`** : toute la suite lit la variante commitée. Le test qui vérifie
justement le repli fichier par fichier passe `includeLocal: true` explicitement.
`MetaTable.Load(..., includeLocal: false)` reste disponible pour un appelant qui veut la variante
publique.

## Registre

`data/catalog/registry.json` (106 Ko) : `relics` 304, `relics_with_code` **297**,
`relics_localization_only` **7**, `tagged_relics_with_code` 297, et `relics_without_tags` vide.
Les sept reliques de localisation seule (CURSED_KETTLE, ENDLESS_APPETITE, GLOWING_ORB, INK_BOTTLE,
MYSTERIOUS_COCOON, RED_VINE_TEA, VINE_BRACELET) restent listées avec leur drapeau.

## Ce qui reste à trancher

- **Point 5** : je peux produire `data/calibration/deck_percentiles.json` (G du deck de départ et
  du deck final par acte/étage, percentiles 10-90) depuis le corpus dédupliqué. C'est un calcul
  hors dépôt puis un fichier compact — dis-moi si je le lance.
- **Point 7** : le total du retrait reste la formule marchand (base 25 + bonus malédiction − coût).
  Si tu veux une seule échelle, il faut décider laquelle : `ΔG` du conseiller (petits nombres) ou
  score 0..100 de marché. Je ne mélange pas les deux sans arbitrage.
