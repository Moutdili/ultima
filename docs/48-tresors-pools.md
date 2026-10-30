# 48 — Trésors potentiels : pools réels et classement par utilité

> Demande du chef : « récupérer tous les trésors potentiels qui pourraient être pratiques ».
> Chaque relique déclare sa rareté dans son propre code (`RelicRarity Rarity => ...`) : c'est
> cette déclaration qui définit les pools, pas une supposition sur les coffres.

## Pools mesurés (298 reliques avec code)

    Common     30      Uncommon   40      Rare       50
    Shop       30      Event      35      Ancient   100      Starter 10

    hors pool : 2 reliques « None » (mécaniques spéciales) + 1 sans déclaration
    (VAKUU_CARD_SELECTOR), publiées sans rareté — jamais classées d'office.

Extraction : `tools/treasures/index_relics.py` → `data/catalog/relic_rarity.json` (29 Ko,
id, rareté, fichier, ligne).

## Ce que le moteur sait répondre

`TreasureIndex.Rank(contexte, pool, n)` — les reliques les plus utiles d'un pool pour **ce** deck,
notées par le scorer habituel (méta communautaire, tags, archétype), triées et stables.

`TreasureIndex.RankAll(contexte, n)` — toutes provenances confondues, **hors set de départ** :
une relique de départ n'est pas un trésor à trouver.

## Ce que le moteur ne prétend pas savoir

- **Les probabilités de coffre.** Le code indexé déclare la rareté, pas les tirages
  (petit/moyen/grand coffre, élite, boss). Aucune cote n'est inventée : on répond « ce que ce pool
  contient », pas « avec quelle chance ».
- **La disponibilité conditionnelle** (relique d'événement précise, offre d'ancien déjà passée).
  `PoolOf(id)` dit le pool ; savoir si ce pool est encore atteignable demande l'état de la run.
- Les reliques sans rareté déclarée n'entrent dans aucun classement.

## Exemple réel (outil `tools/treasure_report`)

    dotnet run --project tools/treasure_report -- SILENT 1

    Ancient   Bois bénis 81 · Pierre philosophale 81 · Gantelets à pointes 80
    Rare      Grû en papier 75 · Bandages résistants 75 · Tuile en ivoire 73
    Shop      L'Abaque 75 · Pain 73 · Bâtonnets enflammés 73
    Uncommon  Royalithe 75 …
    Event     Champi aromatique 73 · Manuel d'Histoire 73 · Épée de jade 73

Le classement dépend du deck passé en contexte : c'est le même scorer que les récompenses, donc
une relique situationnelle peut grimper si le deck l'appelle (plafond S inchangé).

## Fichiers

    tools/treasures/index_relics.py       index du code (machine du jeu)
    tools/treasure_report/                rapport en console par pool
    data/catalog/relic_rarity.json        29 Ko
    Ultima.Engine/Scoring/Treasures/TreasureIndex.cs
    Tests : TreasureIndexTests (5)
