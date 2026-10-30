# 45 — Revue de run : `IReviewBuilder` v1

> Contrat v1.2 : le daemon expose déjà l'op `review.open` (`ReviewService`), mais
> `IReviewBuilder` n'avait **aucune implémentation** — la revue restait vide. C'est fait :
> `Ultima.Engine/Scoring/Review/ReviewBuilder.cs`.

## Ce que la v1 chiffre

Les **choix de cartes** (récompense, et tout étage où des cartes sont proposées) :

- chaque carte offerte reçoit son `ΔG` (`CardScorer.ScoreCard(...).DeltaG`) et ses termes ;
- le **regret** = `ΔG(meilleure option) − ΔG(prise)` ; passer vaut **0** (on ne prend rien) ;
- bande de regret via `RegretBands` (`best` / `info` / `inaccuracy` / `error` / `blunder`) ;
- la décision est marquée `Scored` ; sans ΔG disponible, `NotEvaluable` **avec une raison**,
  jamais un regret inventé.

Les autres décisions (relique, boutique, repos, événement, ancien, retrait, transformation,
potion) apparaissent dans la timeline avec leur type et l'identifiant choisi, mais sont
**déclarées non chiffrées en v1** : `notEvaluable` + raison « décision X non chiffrée en v1 ».

## Ce que la timeline contient

    ReviewRun      runId, buildId, mode, ascension, personnage, dates, win/abandon, tué par
    ReviewLane     deck final + reliques finales par joueur
    ReviewState    par étage et par joueur : PV, or, deck à l'étage
    ReviewPoint    par étage et par joueur : type de salle, rencontre, tours, delta PV/or,
                   options (ΔG, termes, pris ou non), regret, note
    ReviewSummary  points, chiffrés, non chiffrés, regret total/moyen, pires points,
                   concentration (part du top 3), regret par acte, phrase de synthèse
    ReviewCoverage chiffrés, non chiffrés, raisons, base de données

## Réserves, écrites dans la couverture

- **Deck à l'étage** reconstitué depuis le deck final : cartes dont `FloorAdded <= étage`. Les
  cartes **retirées** n'existent plus dans le deck final → la reconstitution est une borne.
  Si `FloorAdded` manque partout, on garde le deck final (jamais un deck vide).
- **Reliques** : le set final est utilisé pour tous les étages (pas d'historique par étage).
- **Potions** : aucun historique par étage → liste vide explicite.
- `ΔG` dépend du deck reconstitué : une carte retirée plus tard n'apparaît pas dans le contexte.

## Branchement (côté daemon, chef)

`ReviewService` reçoit un `IReviewBuilder?` (null par défaut). Il suffit de passer
`new ReviewBuilder()` à la composition :

    var review = new ReviewService(reviewDir, historyDir, new ReviewBuilder(), () => gameRunning);

Aucun autre changement : l'op `review.open` écrit déjà le fichier `review/<hash>.json`.

## Tests

`ReviewBuilderTests` (4) : timeline reconstruite + choix chiffré, passer = 0, run sans décision
(« aucune décision chiffrable »), deck filtré par étage (6 puis 7 cartes).
