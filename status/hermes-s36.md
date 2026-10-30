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

### §5 — Deux bugs trouvés en test réel (situation 1/10 de Claude)

- **Courbe de coût** : la densité se calculait sur les seules cartes dont la MÉTA connaît le coût.
  Terme affiché « 2/4 cartes à 1 énergie → ce coût sature (−4) » sur un deck de 14 cartes.
  Corrigé : le coût vient du catalogue (578 cartes), source « N cartes comptées sur M ».
  Deux tests : deck 2/14 → aucun malus ; deck 8/14 → malus réel.
- **« passer » à F 23** : `ScoreSkip` = taux de passage × 60 − 0,35 × meilleure carte. Le `−0,35`
  est un chiffre magique et la note de « passer » dépend des cartes proposées. **Non corrigé** :
  proposition envoyée à Mio, en attente de son go.

### §6 — Doom, le point aveugle de Pestilence

`DoomPower.cs:65` — `IsOwnerDoomed() => Owner.CurrentHp <= Amount`, tué en fin de tour adverse
(`:68`). Ce n'est pas un dégât, c'est un **seuil d'exécution** : 13 Doom = mort à ≤ 13 PV.
Primitive `Scoring/Powers/Doom.cs` + 4 tests (`IsLethal`, `EffectiveHp`, `StacksToCondemn`).
`DoomPower` ne surcharge **aucun** hook indexé — c'est pourquoi la note de Pestilence ne repose que
sur la méta 44/100.

### §7 — Porte instable : cause trouvée, corrigée à la racine

Deux tests mesuraient du **temps mural** (`DeckTests.Deck_de_60_cartes_reste_sous_5ms`,
`PathAnalyzerTests.Two_hundred_calls_stay_under_one_second`). La solution lance cinq projets de
tests en parallèle : le premier tombait à 7,9 ms au lieu de 0,65 ms sans une instruction de plus.
Remplacés par un instrument déterministe — **octets alloués par thread** — plus un plafond de
catastrophe large. Mesure réelle : 0,65 ms et 190 624 o par évaluation de 60 cartes.
**4 passes consécutives de `dotnet test Ultima.sln` vertes**, puis `tools/verify-all.sh` → PORTE VERTE.

## Prouvé

    dotnet test Ultima.Engine.Tests  → Réussi 303, échec 0 (30 oct., après tous les changements)
    dotnet test Ultima.sln           → 60 + 57 + 38 + 292 + 39 tests, échec 0
    build Ultima.Engine              → 0 erreur, 0 avertissement
    tests ajoutés : Price_never_changes_the_letter_only_the_notes,
                    EventAdvisor_note_une_relique_proposee_par_un_ancien,
                    EventAdvisor_rend_null_pour_une_option_qui_n_est_ni_texte_ni_relique
    registry.json                    → gaps.declared_absent = DEPRECATED_RELIC + raison

    dotnet test Ultima.sln           → 4 passes consécutives exit 0
    tools/verify-all.sh              → .NET vert · compagnon vert · hud vert · console vert
                                       PORTE : VERTE, sortie 0
    perf mesuré                      → 0,65 ms et 190 624 o par évaluation de 60 cartes

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
