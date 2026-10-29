# 24 — Hermes : architecture du moteur de calcul (vague 1)

> Propriétaire : Hermes (moi) + mes sous-agents. Périmètre : `Ultima.Engine/Scoring/**`,
> `Ultima.Engine/Paths/**`, `Ultima.Engine.Tests/Scoring/**`, `Ultima.Engine.Tests/Paths/**`.
> Interdits : `Ultima.Engine/Contracts/**`, `Ultima.Saves/**`, `Ultima.Mirror/**`,
> `Ultima.Daemon/**`, `overlay/**` (propriété du chef — demande écrite dans `status/hermes.md`).

## 1. Ce qui est déjà en place (commit « socle »)

| Fichier | Rôle |
|---|---|
| `Scoring/Model/MetaModels.cs` | `SourceRating`, `MetaEntry`, `MetaArchetype`, `IMetaTable`, `Ids` (normalisation `CARD.X` → `X`) |
| `Scoring/Model/ScoreBuilder.cs` | `ScoreBuilder` (termes nommés, total borné), `ScoreMath` (Clamp/Median/Round), `Confidence` |
| `Scoring/ScoringData.cs` | résolution du dossier `data/meta` (`ULTIMA_DATA`, sinon remontée depuis l'assembly) |
| `Scoring/Meta/MetaTable.cs` | **implémenté** : charge `data/meta` (variante `local/` prioritaire), cartes 503, reliques 263, archétypes, `skip_reference` |
| `Scoring/Personal/PersonalStats.cs` | agrégation history (base fonctionnelle, à durcir par S2) |
| `Scoring/Cards/CardScorer.cs` | base : méta + enchantement + malédiction + perso (à enrichir par S3) |
| `Scoring/Cards/SynergyEngine.cs` | **stub** : `Terms()` et `Tags()` vides (S3) |
| `Scoring/Relics/RelicScorer.cs` | base méta (S4) |
| `Scoring/Shop/ShopScorer.cs` | base valeur/économie + retrait (S4) |
| `Scoring/Deck/DeckEvaluator.cs` | base moyenne + malédictions (S5) |
| `Paths/PathAnalyzer.cs` | **stub** : retourne un `PathsView` vide (S6) |
| `Scoring/Scorer.cs` | façade `IScorer` qui assemble tout (propriété Hermes) |

Tests de fumée : `Ultima.Engine.Tests/Scoring/SmokeTests.cs` — 4/4 verts (503 cartes, 263
reliques, archétypes indexés, score explicable).

## 2. Formule de score v1 (figée — ne pas inventer d'autres nombres)

`Score = clamp(0..100, Σ termes)`. Chaque terme est un `ScoreTerm(label, points, source)`.
Un terme à 0 point n'est pas ajouté. `Confidence` = `Confidence.Of(sourcesMeta, runsPerso)`.

### ScoreCard (récompense)
| Terme | Points | Source |
|---|---|---|
| Méta communautaire | `round(consensus × 0.55)` (≤ 55) | `CardScorer.SourceLabel(meta)` |
| Enchantement | `+3 + min(2, amount)` (≤ 5) | `enchantement` |
| Malédiction | `−30` | nom de la carte |
| Historique perso | `clamp(round(delta × 40), −8, +8)`, seulement si ≥ 5 runs des deux côtés | `tes N runs` |
| Synergie deck | `+3` par tag satisfait, **cap 15** | règle nommée |
| Synergie reliques | `0..+5` | relique citée |
| Adéquation boss | `−10..+10` | boss de l'acte |
| Courbe de coût | `−5..+5` | courbe du deck |

### ScoreSkip
`skip = pick_rate communautaire` → `+round(skipRate × 60)` puis `− round(meilleureCarte × 0.35)`.
Source « Spire Codex (toutes versions) ».

### ScoreShopItem (card | relic | potion)
`valeur × 0.7` + économie `clamp(1 − prix/or, −1, 1) × 15` + `−20` si non abordable.

### ScoreRemoval
`25` + `+30` si le deck contient une malédiction + `−clamp(prix/or, 0, 1.5) × 20`.

### EvaluateDeck
`moyenne des ScoreCard × 0.8` + `−8` par malédiction + adéquation boss (`±10`) + reliques (`0..5`)
+ correspondance d'archétype (`0..8`). `Archetype` = meilleur cluster ; `PotentialArchetypes` = top 3.

## 3. Chemins (`IPathAnalyzer`)

Entrées : `ActMap` (DAG 7×16, `Start`, `Boss`, `Points`, `Children`), position = dernière
`visited_map_coords`, `actIndex`, `characterId`, `hp`.

1. **P1** : énumération exacte des chemins de la position au boss (DFS + mémoïsation du nombre
   de chemins par nœud). `TotalPaths` = nombre de chemins depuis la position.
2. **P2** : pour chaque enfant immédiat → `PathChoice{Col,Row,RoomType,PathCount,Min,Median,Max}`.
3. **P3** : `NodeFrequency` = chemins traversant la case / `TotalPaths` (préfixe × suffixe en DP).
4. **P4** : `ExpectedDamage` = Σ (nombre de salles du type × `PersonalStats.RoomRisk(act,type)`),
   `RiskBasis` explicite (`tes N runs (n salles)` sinon `aucune donnée`). Les `?` utilisent les
   cotes publiques du source du jeu, **jamais** `odds`/`*_encounter_ids` de la save.
5. **P5** : tests sur la map réelle de fixture (7×16, 60 points, position (4,1), boss
   `ENCOUNTER.WATERFALL_GIANT_BOSS`).

Garde-fous : profondeur ≤ 16, cap d'énumération 200 000 chemins (au-delà : DP + échantillonnage
documenté), déterminisme strict (mêmes entrées → mêmes sorties), < 5 ms par appel sur la map réelle.

## 4. Données et fixtures

- Méta : `data/meta/{card_tiers,relic_tiers,archetypes}.json` (+ `data/meta/local/` si présent,
  non commité). Le loader préfère `local/`.
- Catalogue : `data/catalog/*.json`.
- Fixtures saves : `fixtures/saves/root/steam/70000000000000001/profile1/saves/current_run.save`
  (map réelle Silent, deck 14, position (4,1)) et
  `fixtures/saves/root/steam/70000000000000001/profile1/saves/history/*.run` (7 runs, dont
  `win_solo.run`, `death_solo.run`, `abandon_solo.run`, `old_build.run`).
- Lecture en test : `new RunSaveReader().Read(path)` → `RunState`
  (`Acts[i].Map`, `VisitedMapCoords`, `Players[0].Deck`).
- **Interdits** : champs cachés (`*_encounter_ids`, `event_ids`, `rng`, `odds`, `relic_grab_bag`,
  `*_visited`). Aucun test ne doit les lire.

## 5. Tests exigés (tous en xUnit, hors réseau, hors horloge)

| Agent | Fichier de test | Preuves minimales |
|---|---|---|
| S2 | `Tests/Scoring/PersonalStatsTests.cs` | `RunCount == 7` sur les fixtures ; une carte connue a `Offered > 0` ; `RoomRisk(0,"monster").Samples > 0` ; un build inconnu est ignoré |
| S3 | `Tests/Scoring/CardScorerTests.cs` | 3 cartes Defect réelles classées avec termes explicables ; une synergie poison ajoute un terme nommé ; `Total` borné 0..100 sur 503 cartes |
| S4 | `Tests/Scoring/ShopTests.cs` | prix > or ⇒ terme négatif « Non abordable » ; retrait avec malédiction > sans ; relique notée 0..100 |
| S5 | `Tests/Scoring/DeckTests.cs` | deck vide ⇒ score bas + `Confidence.low` ; deck avec malédiction ⇒ `Curses` non vide et terme négatif ; archétype choisi parmi `data/meta` |
| S6 | `Tests/Paths/PathAnalyzerTests.cs` | `Σ PathCount(children) == TotalPaths` ; `Min ≤ Median ≤ Max` ; fréquence ∈ [0,1] ; somme des parts cohérente ; 2 appels identiques ⇒ mêmes résultats ; < 5 ms |

Chaque agent ajoute aussi **un** test de non-régression sur `Scorer` (façade) pour prouver
l'intégration.

## 6. Commandes (machine de dev = fedora-hp)

```bash
export PATH="$HOME/.dotnet:$PATH"
cd ~/wt-<agent>                       # worktree dédié (voir §7)
dotnet build Ultima.Engine.Tests/Ultima.Engine.Tests.csproj -v q
dotnet test  Ultima.Engine.Tests/Ultima.Engine.Tests.csproj --filter "FullyQualifiedName~<Area>" -v q
```

## 7. Protocole swarm

1. Un **worktree par agent** : `git worktree add ~/wt-<id> -b hermes/<id> main`
   (jamais deux agents dans le même dossier : `bin/obj` partagés = courses).
2. Fichiers **disjoints** par agent (§8). Aucun agent ne touche aux fichiers d'un autre.
3. Avant de rendre : `build` **et** `test` verts, puis `git add <ses fichiers>` +
   `git commit` + `git push -u origin hermes/<id>`.
4. Rendu : `status/hermes-<id>.md` — fait / preuves (sortie de test) / non prouvé / demandes.
5. Merge : Hermes relit, merge dans `main`, relance **tous** les tests, puis commit final.

## 8. Propriété des fichiers

| Agent | Possède (exclusivement) |
|---|---|
| S2 « Historien » | `Scoring/Personal/**`, `Tests/Scoring/PersonalStatsTests.cs` |
| S3 « Stratège cartes » | `Scoring/Cards/**`, `Tests/Scoring/CardScorerTests.cs` |
| S4 « Boutiquier » | `Scoring/Shop/**`, `Scoring/Relics/**`, `Tests/Scoring/ShopTests.cs` |
| S5 « Architecte de deck » | `Scoring/Deck/**`, `Tests/Scoring/DeckTests.cs` |
| S6 « Cartographe » | `Paths/**`, `Tests/Paths/**` |

Hermes : `Scoring/Model/**`, `Scoring/Meta/**`, `Scoring/ScoringData.cs`, `Scoring/Scorer.cs`,
`docs/24*`, `status/hermes.md`.

## 9. Règles dures

1. **Probabiliste seulement** : jamais de séquence pré-tirée, de `rng`, d'`odds`, de sac de reliques.
2. **Explicable** : aucun total sans termes ; chaque terme a une source lisible par un humain.
3. **Pur** : pas de réseau, pas d'horloge, pas d'accès jeu ; données en entrée ou depuis `data/`.
4. **Lecture seule** partout ; aucune écriture hors de son worktree.
5. **Zéro warning** au build (le repo est propre).
6. **< 5 ms** par écran (mesurer dans un test pour S6).

## 10. Definition of Done (par agent)

- [ ] Le stub de sa zone est remplacé par une implémentation réelle.
- [ ] Ses tests passent **et** les tests existants restent verts (`dotnet test` complet).
- [ ] Aucun fichier hors de sa propriété modifié (`git status` le prouve).
- [ ] `status/hermes-<id>.md` écrit avec la sortie de test collée.
- [ ] Branche poussée : `hermes/<id>`.

## 11. Review Focus — ce que les tests prévus ne couvrent PAS (5 max, par ordre de risque)

| # | Entrée / mode de défaillance | Comportement attendu | Qui ajoute le test |
|---|---|---|---|
| 1 | `data/meta/local/` absent (variante publique, 1 seule source) | scores bornés, `Confidence` jamais `high`, aucun `NaN` | S3/S5 |
| 2 | Historique vide (0 run lisible) | `PersonalStats.Empty()`, aucun terme perso, pas d'exception, `Confidence.low` | S2 |
| 3 | Map incohérente (cycle, enfant hors bornes, nœud manquant) | DFS borné, `TotalPaths` cohérent, aucune récursion infinie | S6 |
| 4 | Enchantement inconnu ou `Amount` négatif | terme borné (0..5), jamais de total hors 0..100 | S3 |
| 5 | `ctx.Gold == 0` au shop / retrait | aucun division par zéro, terme économie borné | S4 |

Non-objectifs de cette vague : pas de fine-tuning de formule sur les 247 runs (S7 viendra après
validation humaine), pas d'UI, pas d'IPC, pas de modification des contrats.

## 12. Pièges connus

- Les ids méta sont **sans préfixe** (`ALCHEMIZE`) alors que les instances sont `CARD.ALCHEMIZE` :
  passer par `Ids.Bare()`.
- `consensus.score` peut être `null` (variante publique à 1 source) : ne jamais diviser par zéro.
- `Data/meta/local/` est gitignoré : les tests doivent passer avec la variante **publique**.
- Les fixtures ne contiennent qu'un `current_run.save` : ne pas coder en dur le nom du profil.
- `PersonalStats` ignore les runs d'un autre `build_id` (`v0.107.1`) — c'est voulu.
