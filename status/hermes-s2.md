# S2 « Historien » — statistiques personnelles (historique des runs)

Branche : `hermes/s2-personal` (worktree `~/wt-s2`, base `main` @ 30f4b50)
Périmètre modifié : `Ultima.Engine/Scoring/Personal/**`, `Ultima.Engine.Tests/Scoring/PersonalStatsTests.cs`, ce fichier.

## Fait

### `PersonalStats` (agrégation de `history/*.run`)

- **Filtrage build** : `Load(dir, buildId = "v0.107.1")`, `null` = tous les builds. Build inconnu de la
  cible → `SkippedOtherBuild++` (compté, pas juste sauté).
- **Une offre par carte et par étage** : dédoublonnage de `card_choices` par id dans chaque étage.
  `Offered` / `Picked` comptent des *occasions-étages*, `WinsWhenPicked` / `LossesWhenPicked` /
  `WinsWhenSkipped` / `LossesWhenSkipped` comptent des **runs** (une run = au plus 1 unité même si la
  carte y a été prise plusieurs fois) → `RunsWithCard`, `RunsWithoutCard` et `WinRateDelta` restent
  interprétables.
- **Co-op sans double comptage** : un seul `PlayerFloorStats` est agrégé par étage, celui du joueur
  local — `Players[0]` en solo, `Players[i].Id == SteamID64` en co-op (paramètre `steamId64`), repli
  sur `Players[0]` (hôte) si le SteamID est absent/inconnu. Aucun repli sur un coéquipier : un étage
  sans stats du joueur local est compté dans `FloorsWithoutPlayerStats`.
- **`RoomRisk(actIndex, roomType)`** : moyenne des `DamageTaken` par (acte, type de salle) via un
  accumulateur exact (somme + n ; moyenne calculée à la lecture). `Basis` contient toujours
  `n=<N>`, l'acte, le type normalisé, le périmètre (`5 run(s), build v0.107.1`) et le détail des
  runs ignorées (`; ignorés : 2 d'un autre build`). Absence = `RoomRisk(0, 0, "aucune donnée …")`.
  Normalisation : casse/espaces/tirets + synonymes (`rest`/`restsite` → `rest_site`,
  `elite_monster` → `elite`, `monster_fight` → `monster`, `merchant` → `shop`).
- **Accesseurs** : `RunCount`, `Wins`, `Losses`, `AbandonedRuns`, `RunsForCharacter`, `Card(id)`
  (préfixe `CARD.` optionnel, casse indifférente), `Cards` (toutes les stats, triées par offres),
  `BuildFilter`.
- **Audit « failure signal » (exigence ajoutée en cours de route)** — plus aucun `catch {}` muet :
  - `UnreadableRuns` : fichiers illisibles (JSON cassé, ni fichier ni `.backup`, IO) ;
  - `LoadErrors` : une ligne `fichier : TypeException: message` par incident (y compris dossier
    d'historique introuvable et énumération impossible) ;
  - `DuplicateRuns` : même `start_time` déjà vu (copie d'un run) ;
  - `RunsWithoutPlayers`, `UnknownCharacterRuns`, `FloorsWithoutPlayerStats`,
    `FloorsWithoutRoomType`, `CardChoicesWithoutId` : tout ce qui est écarté est compté ;
  - `Summary` (jamais vide) et `RoomRisk.Basis` disent combien de runs ont été ignorées et pourquoi.
  - Les seules exceptions non capturées (OOM, etc.) remontent : elles ne sont pas déguisées en succès.
- **IC de Wilson** dans `PersonalCardStats` (signature du constructeur positionnel et membres
  existants inchangés, `CardScorer` compile tel quel) : `Interval` (`Low`/`High`/`Width`/`Contains`),
  `WinRateWithCi`, `WinRateWithoutCi`, `PickRateCi`, `WinRateDeltaCi` (Newcombe = combinaison des
  deux Wilson), seuil 5 runs/côté identique à `WinRateDelta`, `Wilson(successes, n, z = 1.96)`
  (n ≤ 0 → [0 ; 1], jamais d'exception).

### Tests — `Ultima.Engine.Tests/Scoring/PersonalStatsTests.cs` (11 tests)

1. `Load_filters_runs_on_build_id` — 7 sources, `RunCount == 5`, `SkippedOtherBuild == 2`,
   `Load(null).RunCount == 7` ; vérifie les vrais `build_id` des fixtures (5 × v0.107.1, 2 × v0.103.3).
2. `Known_card_reports_offers_and_picks` — `CARD.DEADLY_POISON` : `Offered > 0`, `0 ≤ Picked ≤ Offered`,
   `RunsWithCard > 0`, IC cohérents ; `CARD.UNTOUCHABLE` proposée 11 fois jamais prise → `Picked == 0`,
   `WinRateWithCi == null`.
3. `Room_risk_by_act_and_room_has_samples_and_basis` — `Samples > 0`, `AverageDamage ≥ 0`, `Basis`
   avec `n=`/`acte 0`, normalisation `" MONSTER "`, absence explicite pour l'acte 99, et moyenne
   recalculée en brute force sur les fixtures.
4. `Empty_exposes_explicit_absences` — `RunCount == 0`, `Card("X") == null`, `Card("") == null`,
   `RoomRisk` vide mais `Basis` non vide, `LoadErrors` vide.
5. `Old_build_run_does_not_contribute` — `Load(null)` vs `Load("v0.107.1")` : `CARD.DISMANTLE`
   (uniquement dans `old_build.run`) absent du filtré, tous les compteurs filtrés ≤ tous-builds,
   `CHARACTER.IRONCLAD` à 0 dans le filtré (≥ 1 sans filtre), SILENT 4 / DEFECT 1.
6. `Coop_run_counts_only_the_local_player` — `coop4.run` seul : 4 joueurs, `RunsForCharacter(SILENT) == 1`
   et `(IRONCLAD) == 0` avec SteamID64 ; `RoomRisk(0,"monster").Samples` == nombre d'étages (pas ×4) et
   moyenne == dégâts du seul joueur local ; repli hôte sans SteamID.
7. `Duplicate_offer_on_same_floor_counts_once` — JSON muté (même carte proposée 2× dans un étage) :
   `Offered` identique à la baseline, toutes les cartes identiques.
8. `Unreadable_run_is_counted_not_silently_skipped` — `broken.run` + 1 run valide : `UnreadableRuns == 1`,
   `RunCount == 1`, 1 ligne `LoadErrors` nommant le fichier, `"1 illisible"` dans `Summary` et `Basis` ;
   dossier absent → `LoadErrors` non vide, pas d'exception.
9. `Duplicate_start_time_is_counted_once_and_traced` — deux copies d'une même run : `RunCount == 1`,
   `DuplicateRuns == 1`, trace dans `LoadErrors`/`Summary`.
10. `All_card_counters_are_internally_consistent` — pour les 84 cartes : `Offered > 0`,
    `0 ≤ Picked ≤ Offered`, runs avec+sans ≤ `RunCount`, taux ∈ [0,1], IC contient le taux (ou null).
11. `Wilson_intervals_are_sane_and_thresholded` — Wilson(5,10) ∋ 0,5 ; Wilson(0,0) == [0,1] ;
    seuil 5/5 (pas d'IC delta sous le seuil) ; écart 0,6 contenu dans son IC sur un cas construit.

## Sortie de test (collée)

```
$ cd ~/wt-s2 && export PATH="$HOME/.dotnet:$PATH" && dotnet test Ultima.Engine.Tests/Ultima.Engine.Tests.csproj -v q
Série de tests pour /home/mio/wt-s2/Ultima.Engine.Tests/bin/Debug/net9.0/Ultima.Engine.Tests.dll (.NETCoreApp,Version=v9.0)
Au total, 1 fichiers de test ont correspondu au modèle spécifié.

Réussi!  - échec :     0, réussite :    16, ignorée(s) :     0, total :    16, durée : 378 ms - Ultima.Engine.Tests.dll (net9.0)
```

Détail (`-v n`) : 11 `PersonalStatsTests` + 4 `SmokeTests` + 1 `ContractTests`, tous « Réussi ».
`dotnet build Ultima.sln` : 0 erreur (1 warning xUnit1031 préexistant dans `Ultima.Saves.Tests`).

## Validation indépendante (hors tests)

Recalcul en Python sur le JSON brut des fixtures, comparé aux valeurs réelles de `PersonalStats`
(petit harnais console dans `~/.hermes/cache/scratch/s2check`, hors dépôt) — identique :

| mesure | Python (JSON brut) | `PersonalStats` |
|---|---|---|
| runs retenues (v0.107.1) | 5 | 5 |
| SILENT / DEFECT / IRONCLAD | 4 / 1 / 0 | 4 / 1 / 0 |
| `CARD.DEADLY_POISON` offres / prises étages / runs avec | 11 / 5 / 3 | 11 / 5 / 3 |
| `CARD.UNTOUCHABLE` offres / prises | 11 / 0 | 11 / 0 |
| acte 0 « monster » n / somme dégâts / moyenne | 24 / 136 / 5,666667 | 24 / 5,666667 |
| offres totales filtré / tous builds (joueur local) | 249 (84 cartes) / 329 (124 cartes) | 249 / 329 |
| `CARD.DISMANTLE` filtré / tous builds | 0 / 1 | absent / 1 |

## Non prouvé

- **Co-op réel avec un SteamID absent des fixtures** : le repli « hôte » (`Players[0]`) est testé, mais
  pas le cas « SteamID fourni et présent dans `Players` mais absent d'un étage donné » — couvert par le
  code (`FloorsWithoutPlayerStats`) sans fixture dédiée.
- **Comptage des runs abandonnées / daily** : elles sont agrégées comme les autres (aucun filtre
  `WasAbandoned`/`Modifiers`), choix assumé mais pas validé avec toi.
- **Semantique victoire/défaite** : `run.Win` uniquement ; une run abandonnée est comptée comme défaite
  (comportement du jeu). Pas de pondération par ascension ni par mode jeu.
- **`progress.save`** (mentionné dans le TODO d'origine) : non agrégé — hors API de lecture fournie pour
  cette tâche (`Toolkit`/`ProgressReader` non utilisés). `PersonalStats` ne lit que `history/*.run`.
- **Performance** : non mesurée sur un historique réel complet (250 runs). Le chargement est linéaire
  et sans allocation majeure, mais aucun benchmark n'a été exécuté.
- **Build par défaut `v0.107.1` codé en dur** (`DefaultBuildId`) : pas de lecture automatique du build
  courant du jeu (le save ne l'expose pas) ; à câbler par l'appelant si le patch change.

## Demandes / points à trancher

1. Faut-il exclure les runs `WasAbandoned` (ou les runs `daily`/`Modifiers`) des stats par carte ?
   Aujourd'hui elles comptent, ce qui biaise `WinRateDelta` (2 abandons sur 5 runs des fixtures).
2. Faut-il exposer `PersonalStats.Load` derrière `IPersonalStats` (fabrique) pour que `Scorer` charge
   l'historique réel ? Aujourd'hui `Scorer` utilise `PersonalStats.Empty()` par défaut.
3. Le seuil de 5 runs/côté pour `WinRateDelta` est conservé ; les IC sont exposés en plus mais aucun
   affichage n'a été ajouté (hors périmètre S2).
