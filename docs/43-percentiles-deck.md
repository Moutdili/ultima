# 43 — Note du deck : percentile sur le corpus

> Demande du chef (docs/42, revue 18:10, point 5) : une note de deck qui vaut « F 17 » pour tout
> deck de départ ne dit rien. La note devient le **percentile de G** parmi les decks arrêtés au
> même acte. Générateur : `tools/deck_percentiles/` → `data/calibration/deck_percentiles.json`.

## Ce que contient le fichier

    schema      ultima.deck-percentiles/1
    source      runs solo, decks finaux, G = PlayerStrengthCalculator.Compute
    buckets     un par (personnage, acte) + un par acte tous personnages (« * »)
                n, p10, p25, p50, p75, p90, median_deck

Publié : **400 000 runs solo** (exports complets via `extract.py`), 20 buckets, seuil **n ≥ 200**.
Contrôle croisé : les 120 000 runs de l'index dédupliqué donnent **exactement les mêmes**
percentiles (43 / 48 / 53 en acte 1) — la mesure est stable, le corpus large ajoute la couverture
par personnage.

Couverture : les buckets publiés totalisent **399 778 decks sur 400 000** ; les 222 restants sont
des actes sous le seuil de publication (n < 200), volontairement écartés plutôt que bruités.

## Comment la note est calculée

1. `DeckEvaluator` garde ses termes habituels (moyenne des cartes, malédictions, boss, reliques,
   archétype) — ils expliquent **de quoi** le deck est fait.
2. Il calcule G à **PV pleins** (`Hp = MaxHp`) : prendre un coup ne doit pas changer la note
   (docs/27 §2).
3. Il lit le percentile de G dans le bucket (personnage, acte), avec repli sur « * ».
4. Un dernier terme recale le total : **Total = 75 % percentile + 25 % note absolue**
   (`DeckEvaluator.PercentileWeight`). Le percentile domine — c'est lui qui répond à « où en est ce
   deck par rapport aux autres » — et la note absolue tranche les ex æquo : deux decks au 0e
   percentile ne se valent pas si l'un porte une malédiction. Sans ce correctif, la note-percentile
   pure écrasait l'ordre en bas d'échelle (test `Malediction_connue_listee_et_penalisee`).
5. Aucune valeur inventée : si aucun bucket n'existe pour l'acte, un terme « Percentile
   indisponible » à 0 point le dit et le total reste absolu.

## Réserves, écrites dans le fichier

- Un deck **en cours** est comparé aux decks **finaux** des runs arrêtés au même acte : les cartes
  retirées ne sont pas reconstituables, donc la référence est légèrement haute.
- L'index ne publie pas les PV de fin de run (champs absents de l'export v0.107.1) : tous les decks
  sont comparés à **PV pleins 70**, seule convention comparable.
- G est normalisé par acte (poids et menace) : la comparaison n'a de sens **qu'à acte égal**, ce
  que fait le bucket.
- 27 % des runs de l'index n'ont aucune décision : leur acte est inconnu, ils sont écartés
  (jamais devinés).

## Reproduction

    python3 tools/deck_percentiles/extract.py        # optionnel : decks avec niveaux d'amélioration
    dotnet run --project tools/deck_percentiles -- \
        data/index/deduped-v0.107.1.jsonl.gz data/calibration/deck_percentiles.json 120000 --index
