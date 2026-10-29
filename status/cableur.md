# Câbleur — mission 2 : contrat v1.2/v1.3 dans le daemon — rendu du 2026-10-29 (18:10)

Branche `v2/cableur`, fusionnée avec `origin/main` (a62893f). Auteur Mio, aucune mention d'outil.
Aucune lecture mémoire du vrai jeu (le jeu était fermé pendant les essais ; vérifié avant chacun).

## Commits
| sha | objet |
|---|---|
| `78e23f6` | feat(daemon): accept event and piles sections in the mirror probe CLI |
| `68f3d5a` | feat(daemon): fill contract v1.2/v1.3 map, route, party, event, review, HUD |
| `2d48966` | feat(daemon): document v1.3 IPC and add a real daemon state sample |

## Livré (par point de la mission)
1. **Map / Route / Party / Allies** : `EngineState.Map` depuis la save (acte courant visible, types bruts =
   `RoomTypes`, liens, cases visitées, position, boss ; acte futur jamais publié) ; `EngineState.Route` via
   `RoutePlanner` (Hermes) ; `RunView.Party` = tous les joueurs de la save (`IsLocal`), PV repris de la mémoire
   si la dernière lecture VÉRIFIÉE est plus récente que la save ; `ScoringContext.Allies` = alliés sans le local.
2. **Event** : besoin miroir `event` à l'entrée d'une salle d'événement → `EventView` + `EventAdvisor`
   (ids passés nus : `ABYSSAL_BATHS`, `IMMERSE`). Tant que le miroir ne sait pas lire la section : `null`,
   aucune dégradation, UNE tentative par lancement du jeu. CLI du probe : `--sections …,event,piles`, appel de
   `GameReader.ReadEvent()/ReadPiles()` par réflexion s'ils existent, sinon champ `"unsupported"`.
3. **Revue** : op socket `{"op":"review.open","runId"}` → `review/<hash>.json` via `IReviewBuilder`, hors
   boucle, 30 s max, cache, refus pendant une partie. Hermes n'a pas d'implémentation →
   `{"op":"review.open","runId":…,"ok":false,"error":"review indisponible"}`.
4. **HUD v1.3** : `settings.json` → `state.settings.hud`, validé champ par champ (valeur invalide → défaut de
   ce champ ; `hud` absent/null → `HudSettings.Default`).
5. **Journal** : `~/.local/state/ultima/daemon.log` (`$XDG_STATE_HOME`, option `--log`), lignes
   `2026-10-29 18:07:55.628 INFO  message` (INFO/WARN/ERROR/DEBUG alignés sur 5), copie stderr, rotation > 5 Mio.
6. **Échantillon** : `docs/ipc/state.v13.sample.json` = `ultima-daemon dump` sur la save réelle (jeu fermé),
   sans SteamID ni chemin personnel. Le chef le reprend pour `docs/22-state.sample.json`.
Aussi : `ultima-mirror` cherché dans le dossier du daemon (installation `~/.local/share/ultima/bin`).

## Preuves brutes
```
dotnet build Ultima.sln   → 0 erreur ; 1 avertissement préexistant (Ultima.Saves.Tests/RunSaveReaderTests.cs:228, xUnit1031)
dotnet test Ultima.sln
Passed!  - Failed: 0, Passed:  64 - Ultima.Saves.Tests.dll
Passed!  - Failed: 0, Passed:  63 - Ultima.Knowledge.Tests.dll
Passed!  - Failed: 0, Passed:  39 - Ultima.Daemon.Tests.dll
Passed!  - Failed: 0, Passed: 252 - Ultima.Engine.Tests.dll
```
(l'erreur Debug de `Ultima.sln` signalée en mission 1 est corrigée sur main.)

Tests ajoutés (`V12Tests`, 11) : carte (types ⊂ RoomTypes, graphe fermé, visitées = save, acte futur null) ;
route du planificateur, panne → null ; équipe solo (1 entrée locale, 0 allié) et co-op (2 joueurs, alliés sans
le local, PV mémoire) ; événement (ids nus, option non mesurée → null) ; lecture `event` à l'entrée puis effacée
sur vote ; section non prise en charge → 1 tentative par session (rejeu du log réel : 4 lectures au lieu de 8) ;
revue sans implémentation, avec implémentation factice (fichier, cache, `runId` hostile `../../etc/passwd`
refusé, run introuvable, op inconnue, JSON illisible, refus en partie), op sur la vraie socket ; HUD ; format du
journal. Le rejeu complet du log solo réel donne maintenant `[Event, Reward, Event, Event, Event, Event, Reward, Shop]`
(5 salles d'événement du log), toujours 0 lecture en combat.

`dump` sur la vraie save (jeu fermé) :
```
contrat 1 3 ; map acte 0 7 x 16, 57 nœuds, position (3,5), boss (3,16), 6 visités,
types [ancient, boss, elite, monster, rest_site, shop, treasure, unknown]
route : 12 pas, 28 PV attendus au boss, mayBeLethal false, « PathPlanner v1 · rooms.json (80 000 runs solo) · PV cumulés »
équipe : [La Tisse-Mort, local, 70/73, 14 cartes] ; hud : verre 0.55 0.62 right ; event : null
```
Bout en bout (vrai `ultima-mirror` enfant, cible `Sts2Shape.TestTarget`, jamais le jeu) :
```
probe --json --sections run,event,piles → exit 0, sections [run], unsupported [event, piles]
daemon : 2 salles d'événement → 1 seule lecture : « mirror Event exit=0 mirror ms=122 »
         puis « miroir : section « event » non prise en charge (Miroir 2 à venir) … »
socket : → {"op":"review.open","runId":"abandon_solo"}
         ← {"op":"review.open","runId":"abandon_solo","ok":false,"error":"review indisponible"}
journal : $XDG_STATE_HOME/ultima/daemon.log, lignes « 2026-10-29 18:08:02.036 INFO  mirror Event exit=0 mirror ms=122 »
```
Sobriété (binaire final, jeu fermé, 60 s) : `utime=31 stime=3` à t0 et t60 (0 tick), 6 réveils, top 0,0 %.

## Ce qui n'est PAS prouvé
- Rien sur le vrai jeu en mémoire (aucune fenêtre de test) : PV d'équipe repris de la mémoire, `event` réel.
- `event` : la section n'existe pas encore (Miroir 2) ; forme attendue proposée dans `docs/ipc/README.md` §4.2,
  non validée par Miroir 2. Les ids d'options réels du jeu (clé de texte ?) ne sont pas confirmés.
- Revue : aucun `IReviewBuilder` réel ; seul le chemin « indisponible » tourne en vrai, le chemin « ok » est
  prouvé avec une implémentation factice.
- Co-op : construit et testé sur une save synthétique (2 joueurs) ; `AllyView.Name` = nom du PERSONNAGE
  (la save ne contient pas le pseudo Steam) ; un client co-op SANS `current_run_mp.save` n'a pas d'état run.
- Carte : seule la save est source ; mise à jour à chaque réécriture de la save (pas au vote).
- Un événement à plusieurs pages : une seule lecture à l'entrée (pas de signal de log pour la page suivante).

## Besoins pour la prochaine fenêtre de test
Ceux de la mission 1 (parcours combat → récompense → marchand + achat → événement → feu → trésor → menu,
MangoHud A/B), plus : vérifier `map`/`route` contre l'écran de carte ; une salle d'événement une fois la
section `event` de Miroir 2 livrée (ids d'options, plusieurs pages) ; co-op hôte pour `party`.

## Demandes (contrat et autres parties)
1. **Miroir 2** : section `event` à la forme de `docs/ipc/README.md` §4.2 (ou me dire la sienne) ; `piles` sans
   l'ordre de la pioche.
2. **Hermes** : implémentation d'`IReviewBuilder` (le daemon l'appelle dès qu'elle existe : un seul endroit,
   `DaemonHost.BuildEngine`).
3. **Chef** : reprendre `docs/ipc/state.v13.sample.json` dans `docs/22-state.sample.json` ; nullabilité de
   `RewardView.SkipScore`, `DeckEvaluation.Score`, `CardView.Cost` (toujours ouverte, voir mission 1) ;
   `AllyView.Name` : pseudo non disponible dans la save, préciser si le nom du personnage convient.
4. **Ultima.Saves** : signaux de log menu/abandon/achat (toujours ouverts, mission 1).

---

# Mission 1 — Ultima.Daemon (orchestration temps réel et IPC) — rendu du 2026-10-29

Branche `v2/cableur` (worktree `/home/mio/v2-cableur`), à jour avec `origin/main` (1dd1654, contrat v1.2).
Auteur des commits : Mio ; aucune mention d'outil dans les messages (consigne du chef).

## Commits

| sha | objet |
|---|---|
| `ba39d87` | feat(daemon): add --json to ultima-mirror probe/layout/self-check |
| `56eb64a` | feat(daemon): add event-driven daemon publishing EngineState over IPC |
| `201739d` | feat(daemon): add replay tests on real test-window godot logs |
| `642192d` | feat(daemon): fix the ULTIMA_DATA data-root convention for the engine |
| `b66a9e7` | fix(daemon): cross-check every shop read and never re-read on reread save |
| `0bad23b` | feat(daemon): document IPC transport and add user systemd unit |
| `efb6196` | feat(daemon): use the Encyclopedist knowledge base for names and texts |
| `2bcfdb5` | feat(daemon): document knowledge base location in the IPC README |
| merges | `50811f6`, `cec7af0`, `883538c`, `43c39a5` (origin/main → v2/cableur ; seul conflit : `Ultima.sln`, version de main + mes 2 projets) |

## API / CLI exposées

- `ultima-mirror probe|layout|self-check --json` : **une** ligne JSON, code de sortie inchangé ; `--timeout-ms`.
  Schéma : `{v, cmd, exit, ms, error, health{gameRunning, attached, ptraceScope, pid, attachMs, runtime},
  sections{run|reward|shop|combat:{stable, attempts, ms, error, value}}, compare}`.
- `ultima-daemon run|status|dump` ; options `--root --runtime-dir --settings --mirror --no-mirror
  --no-scoring --data --game-comm -v`.
- IPC : `$XDG_RUNTIME_DIR/ultima/state.json` (tmp + rename, seulement si changé) + `state.sock`
  (dernier état à la connexion puis 1 ligne/état, multi-clients, EOF détecté) + `daemon.lock`.
- Réglages : `~/.config/ultima/settings.json` lu et surveillé (absent → `OverlaySettings.Default`).
- Convention **`ULTIMA_DATA` = dossier racine des données** (`meta/`, `catalog/`, `calibration/` ;
  `<repo>/data`) : `--data` → variable → remontée ; réécrite dans l'environnement avant le moteur.
- Unité `docs/ipc/ultima-daemon.service` (NON installée), doc `docs/ipc/README.md`.

## Preuves brutes

### Tests
```
dotnet test Ultima.sln
Passed!  - Failed: 0, Passed:  63, Total:  63 - Ultima.Knowledge.Tests.dll
Passed!  - Failed: 0, Passed:  64, Total:  64 - Ultima.Saves.Tests.dll
Passed!  - Failed: 0, Passed:  27, Total:  27 - Ultima.Daemon.Tests.dll
error CS0234: … 'Core' does not exist in the namespace 'Ultima'  (Ultima.Engine.Tests/OfflinePackagingTests.cs:225)
dotnet test Ultima.Engine.Tests          # seul
Passed!  - Failed: 0, Passed: 247, Total: 247 - Ultima.Engine.Tests.dll
```
Build de mes projets : 0 avertissement, 0 erreur (`TreatWarningsAsErrors`). Les 2 défauts de la
solution sont **préexistants sur origin/main** (reproduits sur un clone propre), hors de mon périmètre,
non corrigés :
1. `Ultima.sln` : `Ultima.Core` n'a pas `Debug|Any CPU.Build.0` → dans une build Debug de la
   solution, Ultima.Core n'est pas construit et `Ultima.Engine.Tests` ne compile pas (il compile seul : 247/247). → **chef**.
2. `Ultima.Saves.Tests/RunSaveReaderTests.cs:228` : avertissement xUnit1031. → propriétaire d'Ultima.Saves.

Tests du daemon (27) : écrans rejoués sur logs RÉELS anonymisés (`Fixtures/make_fixtures.py` :
session solo 13:07 complète, 719 lignes ; session co-op 12:47, 1384 événements, 4 joueurs) ; rejeu
complet à travers `DaemonCore` avec faux miroir et horloge virtuelle → **exactement 3 lectures**
(2 récompenses + 1 marchand), **0 en combat**, 0 au repos (1 h virtuelle, 0 minuteur armé) ;
aller-retour de `docs/22-state.sample.json` identique ; forme d'un état assemblé = forme de
l'échantillon ; publication atomique (lecteur concurrent, 300 écritures, 0 fichier partiel) ; socket
multi-clients ; 50 sondes éphémères sans fuite ; calcul qui lève / stub vide → `null` ; divergence ;
réglages ; ligne `--json` ; hôte réel (inotify) sur copie de fixture.

### `ultima-daemon run` jeu FERMÉ — 0 % CPU sur 60 s (pidstat absent : /proc + ps + top)
```
jeu fermé            (pgrep -x SlayTheSpire2 : rien)
pid=680598
== t0 17:54:10   utime=27 stime=3 (ticks 10 ms)   réveils volontaires (tous fils)=45
== t60 17:55:10  utime=27 stime=4 (ticks 10 ms)   réveils volontaires (tous fils)=51
    PID  NI %CPU     TIME   RSS     ELAPSED COMMAND
 680598  10  0.4 00:00:00 75740       01:06 ultima-daemon      (%CPU de ps = moyenne depuis le lancement)
 680598 mio 30 10 259.1g 75740 47988 S 0.0 0.3 0:00.31 ultima-+   (top, 5 s)
santé : jeu=fermé save=ok miroir=— ; écran Unknown ; run : La Tisse-Mort A0 acte 1 étage 5 PV 70/73 or 134
```
→ 1 tick (10 ms) en 60 s ≈ 0,02 %, 6 réveils en 60 s (attribués : fil Finalizer du runtime +3,
fils FileSystemWatcher). Mesures identiques à 13:47 et 17:46 (0 tick). Tous les fils à `NI 10`.

### strace — aucune écriture dans le jeu, 0 `process_vm_writev`
A. Jeu fermé, vraie racine, miroir activé, 20 s (`strace -f -e trace=openat,process_vm_writev`) :
```
openat total : 912 ; dans les données du jeu : 261, dont en écriture (O_WRONLY|O_RDWR|O_CREAT|O_TRUNC|O_APPEND) : 0
process_vm_writev : 0
écritures (toutes) : $SP/rtA/daemon.lock, $SP/rtA/.state.json.tmp ×2, /proc/self/task/*/comm (noms de fils du runtime)
```
B. De bout en bout avec le **vrai** processus enfant `ultima-mirror` contre la cible de test
`Sts2Shape.TestTarget` (jamais le jeu), copie de fixture, lignes de log réelles écrites dans le
`godot.log` de la COPIE (`strace -f -e trace=openat,process_vm_writev,process_vm_readv,execve,ptrace`) :
```
1) combat                 écran=Combat  miroir non appelé
2) 3 tirages              écran=Reward  récompense=[BACKFLIP_TEST, DAGGER_THROW_TEST, PREPARED_TEST]  mirror ms=458
3) choix                  écran=Map
4) vote+tirage+marchand   écran=Shop    dégradé=divergence save↔mémoire : PV 61≠70, or 656≠99, deck 10≠14 cartes,
                                          reliques différentes ; shop=None (attendu : la cible ≠ la save)  mirror ms=498
5) vote                   écran=Map
execve : ultima-daemon ; ultima-mirror layout --json ; probe --json --sections reward ; probe --json --sections run,shop
process_vm_writev : 0   ptrace : 0   process_vm_readv : 12603 (enfants → cible de test)
openat dossiers du jeu : 2 (sts2.dll, O_RDONLY, par `layout`) ; en écriture : 0
arrêt SIGTERM : < 500 ms
```
Cette preuve a trouvé un **défaut corrigé** (`b66a9e7`) : la relecture de la save pendant la
vérification croisée relançait une lecture du marchand publiée sans vérification. Test de non-régression ajouté.

### Observation de terrain (non instrumentée par moi)
Mio a lancé lui-même `ultima-daemon run --no-mirror` (13:57, terminal Ptyxis) pendant sa session :
état publié à 14:26:59 `gameRunning=false`, Nécrolieuse étage 5, or 134, 17 chemins → fin du jeu
détectée (pidfd) ; 3 h 44 de fonctionnement pour **1 s de CPU**. Processus laissé en place.

## Citations des skills (étape 0)
- sts2-engine-contract : « Le contrat est la frontière entre les parties. Personne d'autre que le chef ne le modifie. »
- sts2-daemon : « Le miroir n'est appelé QUE sur ces transitions, avec anti-rebond (≥ 250 ms) — jamais en boucle. »
- sts2-save-format : « Les cartes de récompense sont tirées APRÈS l'écriture […] → l'offre n'est jamais dans la save au moment du choix : elle vient du miroir mémoire. »
- sts2-memory-mirror : « Le daemon lance le miroir dans un processus ENFANT avec délai maximum (ClrMD peut planter côté lecteur). »
- sts2-performance : « Daemon / miroir : lectures mémoire (doivent être absentes en combat). »

## Ce qui n'est PAS prouvé
- **Rien n'a tourné avec mon daemon ET le miroir sur le vrai jeu.** Le jeu tournait (session de Mio)
  pendant une partie de mon travail : je n'ai fait AUCUNE lecture mémoire du jeu ; mes essais en direct
  ont simulé « jeu fermé » (`--game-comm` inexistant, miroir désactivé), puis ont eu lieu jeu réellement fermé.
- Coût réel d'une lecture sur le jeu (≈ 450-500 ms mesurés sur la cible de test, dont ~200 ms de
  démarrage .NET de l'enfant) et impact FPS (MangoHud) : non mesurés.
- Écrans sur le vrai jeu : Treasure, RestSite, boss, mort/victoire → Menu ; **retour au menu en cours
  de run non détecté** (le parseur d'Ultima.Saves n'expose pas « main menu » / « Abandoning run » / « NGame.Quit »).
- Récompense de cartes d'un **événement** sans tirage de rareté (log solo l. 581-583) : non détectée comme `Reward`.
- Relecture du marchand **après achat** : suppose que le jeu réécrit la save après un achat (non vérifié).
- Vérification croisée à l'entrée du marchand sur le vrai jeu (risque de course save/mémoire : relecture de la save prévue).
- Co-op réel avec `current_run_mp.save` (joueur local) ; plusieurs récompenses de cartes simultanées.
- Unité systemd : syntaxe vérifiée (`systemd-analyze --user verify`), jamais démarrée.

## Besoins pour la prochaine fenêtre de test
1. Dans `~/sts2` après fusion : `dotnet publish Ultima.Mirror/src/Ultima.Mirror.Probe -c Release -o build/mirror`
   et `dotnet publish Ultima.Daemon -c Release -o build/daemon` ; `ptrace_scope` = 0.
2. Lancer `build/daemon/ultima-daemon run -v` (ou le service) AVANT le jeu ; garder le journal (`mirror ms=`).
3. Parcours : combat → récompense (offre visible avant le clic ?) → carte → marchand (15 prix, puis UN achat :
   la save est-elle réécrite ? l'inventaire se met-il à jour ?) → événement avec récompense de cartes →
   feu → trésor → retour au menu → reprendre → abandon/mort.
4. MangoHud A/B : jeu seul / jeu + daemon (miroir actif), pioches en combat (le daemon ne doit rien lire en combat).
5. Si possible une partie co-op (hôte) pour `current_run_mp.save` et le filtrage du joueur local.

## Demandes de changement de contrat (chef) et aux autres parties
1. `RewardView.SkipScore` et `DeckEvaluation.Score` → `Score?` : le daemon y publie `null` quand le calcul
   échoue (sémantique « — »), ce que le type non nullable ne dit pas (consommateurs C#). Consommateur : overlay/compagnon.
2. `CardView.Cost` → `int?` : coût inconnu publié `-1`, ambigu avec « injouable ». Consommateur : overlay.
3. `RewardView` : une seule offre ; l'écran peut contenir plusieurs récompenses de cartes (le daemon publie
   la première non prise). Proposer `IReadOnlyList<RewardView>` ou un index.
4. `docs/22-state.sample.json` : ne montre pas `card.description`, `potion.score` (v1.1) ni `contractMinor`,
   `map`, `route`, `event`, `run.party`, `score.deltaG`, `term.evidence` (v1.2) — tolérés explicitement par mes tests.
5. v1.2 non remplie par le daemon (consigne : ne pas l'anticiper) : `map`, `route`, `event`, `party` = `null`,
   alors que `contractMinor` vaut 2 par défaut. À planifier (Map depuis la save, Event via le miroir).
6. Ultima.Saves (`GodotLogParser`) : exposer « retour au menu » (`[Startup] Time to main menu`), « Abandoning run »,
   « NGame.Quit », « Player N obtained … » (achat/récompense) → détection du menu et des achats sans sondage.
7. Miroir (bibliothèque gelée) : `CardView` du miroir sans enchantement → enchantements des cartes proposées
   (récompense, marchand) inconnus ; à ajouter à `ObjectPaths` si le champ est autorisé.
8. `Ultima.sln` : ajouter `{1111…}.Debug|Any CPU.Build.0` pour Ultima.Core (casse la build Debug de la solution).
