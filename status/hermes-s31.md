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

## File restante

1. **IReviewBuilder** (contrat v1.2, `review.open`) — à faire avant rencontres et pouvoirs.
3. 37 rencontres sans statistiques.
4. 204 pouvoirs comportementaux — d'un bloc, plus tard.
5. A2 (probabilité de pioche, docs/25) — **ne pas commencer** avant que le miroir publie les piles.

## Arbitrage reçu (point 7)

On garde **uniquement** `RemovalAdvisor` : `ScoreRemoval` passe par lui, le prix devient un terme
informatif à 0 point, la formule « +25 fixe − coût » disparaît. Test attendu : retirer une
malédiction note plus haut que retirer une Frappe, au même prix. À faire après les tags.
