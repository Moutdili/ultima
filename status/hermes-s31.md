# Hermes — S31 : percentiles de deck (point 5) + suite de la file

> Branche : `main` directement (`d2549f5`). **257 tests**, `hermes-verify` PASS **avec
> `data/meta/local/` présent** sur fedora. Précédent : `status/hermes-s30.md` (revue du chef).

## Point 5 — note du deck : FAIT

`DeckEvaluator` ne rend plus une moyenne de notes de cartes (qui donnait « F 17 » à tout deck de
départ). Il rend le **percentile de G** parmi les decks arrêtés au même acte.

    fichier   data/calibration/deck_percentiles.json   (2,9 Ko)
    source    120 000 runs solo, decks finaux, index dédupliqué
    buckets   18 (personnage × acte + « * » par acte), seuil n ≥ 200
    exemples  *|1 p10=43 p50=48 p90=53   *|2 40/46/52   *|3 33/40/47

Mécanique : les termes habituels (moyenne des cartes, malédictions, boss, reliques, archétype)
restent affichés — ils expliquent **de quoi** le deck est fait — et un dernier terme porte l'écart
vers le percentile, si bien que `Total = percentile`. Si aucun bucket n'existe pour l'acte :
terme « Percentile indisponible » à 0 point, total absolu conservé.

Générateur : `tools/deck_percentiles/` (`extract.py` + outil C#). Doc : `docs/43-percentiles-deck.md`.

### Deux bugs corrigés en route

1. **Les PV entraient dans la note.** G utilise les PV courants : un joueur blessé aurait vu sa
   note de deck chuter, exactement ce que docs/27 §2 interdit. Calcul à `Hp = MaxHp`.
2. **L'index ne publie pas les PV de fin de run** (`players[].current_hp` absent de l'export
   v0.107.1). Convention explicite : **PV pleins 70 pour tous les decks**, écrite dans le fichier.
   Sans ça, tous les decks étaient évalués à 1 PV.

### Réserves (dans le JSON, pas seulement ici)

- deck en cours comparé à des decks **finaux** au même acte → référence légèrement haute ;
- G est normalisé par acte → comparaison valable **à acte égal** seulement (c'est le bucket) ;
- 27 % des runs de l'index n'ont aucune décision → acte inconnu, écartés, jamais devinés.

## Point 2 — reliques muettes : FAIT (`559a3ba`)

Barème de repli borné par tags (`RelicTagValues`, docs/44) : plafond **B (55)**, plancher 0,
confiance `low`, chaque terme portant le tag + le début de la description FR.
Mesure : **303 / 304 reliques notées** (avant : 164). Seule exception `DEPRECATED_RELIC`
(artefact de développement sans effet). Circlet reste à 73 — c'est la méta communautaire qui note
la blague, pas au moteur de la corriger.
Tests : `RelicTagTests` (6) + `RelicCoverageTests` (2 invariants). **265 tests** au total.

## Point 3 — IReviewBuilder : FAIT

`Ultima.Engine/Scoring/Review/ReviewBuilder.cs` (docs/45). Le daemon peut brancher
`new ReviewService(..., new ReviewBuilder(), ...)` — aucune autre modification.

v1 chiffrée : les **choix de cartes** (ΔG de chaque option, regret = meilleur − pris, passer = 0,
bandes `RegretBands`). Les autres décisions sont présentes mais **déclarées non chiffrées**, avec
raison. Réserves dans `ReviewCoverage` : deck reconstitué depuis le deck final (cartes retirées non
reconstituables), reliques = set final, potions vides par étage.
Tests : `ReviewBuilderTests` (4). **269 tests** au total.

## Point 4 — rencontres sans stats : FAIT

`tools/encounters/measure.py` mesure le corpus (785 121 runs lus, 475 845 solo) →
`data/calibration/encounter_measure.json` (63 Ko, 157 rencontres). **35 des 37 manquantes sont
couvertes** ; 2 restent sous le seuil de 200 runs (`THE_ARCHITECT_EVENT_ENCOUNTER`,
`TUNNELER_NORMAL`).
`ThreatModel` complète les stats absentes sans jamais écraser le catalogue, provenance
`Source = "measured"`, et ne publie une stat par personnage qu'au-delà de 200 runs.
Doc : `docs/46-rencontres-mesure.md`. Tests : `EncounterMeasureTests` (3) + `ThreatTests` mis à
jour (AEONGLASS_BOSS passe de « aucune stat » à 43,97 dégâts sur 40 286 runs).

## Statuts — trois familles indexées (docs/47)

`tools/status/index_sources.py` (lancé sur la machine du jeu) : **238 cartes** appliquent un pouvoir
(192 sur soi, 75 sur l'ennemi), **96 monstres** (131 buffs sur soi, 50 debuffs au joueur), 458
applications, 228 pouvoirs distincts, **0 inconnu**.
`tools/status/build_status_rules.py` génère les règles depuis les hooks de `powers.json` : 36
pouvoirs touchent aux dégâts, **12 modélisés**, 24 publiés non modélisés avec leur raison
(direction non déductible du hook).
`StatusRules.Incoming` (plat → multiplicatifs → plafond) et `BlockGained` sont branchés sur
`ThreatModel.PredictedMaxHit`. `StatusSources` répond « qui applique quoi », avec la ligne.
**284 tests**.

## Trésors potentiels (docs/48)

`tools/treasures/index_relics.py` extrait la rareté **déclarée dans le code de chaque relique** :
Common 30 · Uncommon 40 · Rare 50 · Shop 30 · Event 35 · Ancient 100 · Starter 10 (298 reliques).
`TreasureIndex.Rank(ctx, pool, n)` classe les reliques d'un pool pour le deck courant (même scorer
que les récompenses) ; `RankAll` exclut le set de départ. Outil `tools/treasure_report` pour la
liste en console. Aucune probabilité de coffre inventée : le code indexé déclare la rareté, pas
les tirages. **289 tests**.

## Méga-vérification (tools/verify_data.py)

Script unique, **440 contrôles** sur toutes les données produites : schémas, bornes, couvertures,
cohérence entre fichiers, déterminisme des générateurs, chemins absolus, budget de clone.
Trois anomalies trouvées et corrigées : 2 reliques `RelicRarity.None` rangées à tort dans un pool
trésor, 222 decks d'actes sous le seuil non documentés, et un contrôle de poids qui lisait la
mauvaise clé. Résultat : **PASS, 0 échec**, budget 1 999 681 / 2 097 152 octets (95,4 %).

## File restante

1. 204 pouvoirs comportementaux (d'un bloc, plus tard) — l'index statuts est la première brique.
2. Rotation des monstres : non modélisée (docs/22 interdit de l'inventer).
3. A2 pioche — attendre le miroir (piles de combat).
3. 37 rencontres sans statistiques.
4. 204 pouvoirs comportementaux — d'un bloc, plus tard.
5. A2 (probabilité de pioche, docs/25) — **ne pas commencer** avant que le miroir publie les piles.

## Arbitrage reçu (point 7)

On garde **uniquement** `RemovalAdvisor` : `ScoreRemoval` passe par lui, le prix devient un terme
informatif à 0 point, la formule « +25 fixe − coût » disparaît. Test attendu : retirer une
malédiction note plus haut que retirer une Frappe, au même prix. À faire après les tags.
