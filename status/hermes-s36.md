# hermes-s36 — T5 Calcul (en cours)

> Plan : `docs/45-plan-hermes/T5-calcul.md`. Périmètre : `Ultima.Engine/Scoring/**`,
> `Ultima.Engine/Paths/**`, leurs tests, `data/**`, `tools/` de calcul. T6 non touché.
> Rapport en deux états séparés : ce qui est **prouvé** (commande + sortie réelle) et ce qui ne
> l'est **pas**.

## Fait

### §1 — Retrait : une seule vérité

- `ShopScorer.ScoreRemoval` ne calcule plus `25 + 30 (malédiction) − coût`. Il appelle
  `RemovalAdvisor.Recommend(ctx, prix, or)` et note le **gain réel de G** du meilleur retrait.
- Le **prix est un terme informatif à 0 point** (« Prix du retrait », plus « Retrait non
  abordable » et « Sous le seuil de rentabilité » quand ils s'appliquent).
- `Scorer.ScoreRemoval` ne ré-ajoute plus un second terme de classement : il délègue au shop
  (auparavant deux classements cohabitaient, dont un à 0 point).
- **Piège rencontré** : `ScoreBuilder.Add` ignore silencieusement les termes à 0 point. Les termes
  informatifs sont donc construits explicitement dans une liste de `ScoreTerm`, sinon l'information
  disparaît sans erreur (le Total restant juste).

### §3 — Derniers trous, déclarés au lieu d'être silencieux

- `tools/encounters/measure.py` publie désormais `not_measured` : toute rencontre vue sous le seuil
  (`n < 200`), avec son volume, **et** toute rencontre du catalogue absente du corpus (`n = 0`,
  « absente du corpus »). Le silence devient une donnée lisible.
- `data/catalog/registry.json` porte un bloc `gaps.declared_absent` nommant la seule relique hors
  barème, avec sa raison : `DEPRECATED_RELIC`, artefact de développement (`RelicRarity.None`),
  sans effet en jeu — 303/304 reliques notées, l'exception est écrite, pas déduite.

### §4 — Anciens : options réelles, notées

- `EventAdvisor.Rank` reconnaît une option qui est une **relique** du catalogue (offre d'Ancien) et
  la note avec le `RelicScorer` (même barème que le butin), avec un terme « Offre d'Ancien » à
  0 point. Une option qui n'est ni un texte profilé ni une relique reste `null` — jamais devinée.
- L'advisor se dote d'un scorer de reliques complet quand il est construit sans (le daemon fait
  `new EventAdvisor()`), donc aucun câblage supplémentaire n'est nécessaire côté daemon.

## Prouvé

    dotnet test Ultima.Engine.Tests  → Réussi 292, échec 0 (30 oct., après les trois changements)
    dotnet test Ultima.sln           → 60 + 57 + 38 + 292 + 39 tests, échec 0
    build Ultima.Engine              → 0 erreur, 0 avertissement
    tests ajoutés : Price_never_changes_the_letter_only_the_notes,
                    EventAdvisor_note_une_relique_proposee_par_un_ancien,
                    EventAdvisor_rend_null_pour_une_option_qui_n_est_ni_texte_ni_relique
    registry.json                    → gaps.declared_absent = DEPRECATED_RELIC + raison

## NON prouvé

- **Aucun test sur le vrai jeu** : rien n'a été lancé dans une vraie session, aucun écran vu.
- **`not_measured`** : le passage sur le corpus (2,5 Go) tournait encore au moment d'écrire ; la
  sortie n'est donc pas encore vérifiée champ par champ.
- **Anciens** : la forme exacte des identifiants d'options publiée par le miroir 2 n'est pas
  confirmée par une session réelle ; le code accepte un id de relique (`RELIC.X` ou `X`) et rend
  `null` sinon. À confirmer avec le daemon de T1.
- **Porte `tools/verify-all.sh`** : verte à vide, mais **instable sous charge** (le parseur 2,5 Go
  et les builds de T1 en parallèle font tomber `.NET` et `console` par intermittence). Aucun push
  n'a été fait depuis ces changements.

## Reste

- §2 — couche de simulation d'événements (`AfterTurnEnd`, `AfterCardPlayed`…) pour les pouvoirs
  comportementaux, sans valeur inventée (effet non modélisé ⇒ `null` + source).
- Relancer `measure.py` au calme, vérifier `not_measured`, puis la porte, puis commit.

## Demandes de contrat

Aucune à ce stade. Le contrat v1.4 ne bloque pas T5.
