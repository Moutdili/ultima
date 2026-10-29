# IPC d'ULTIMA — daemon `ultima-daemon` (Câbleur)

Le daemon lit le jeu **sans jamais y toucher** (saves + `godot.log` + miroir mémoire en processus
enfant), assemble un `EngineState` (contrat : `Ultima.Engine/Contracts/*.cs`, skill
`sts2-engine-contract`) et le publie à l'overlay GNOME et au compagnon.

```
saves/ (inotify) ──┐
godot.log (inotify)┼─► DaemonCore ──► StateAssembler ──► state.json (tmp + rename) ─► overlay, compagnon
pidfd (fin du jeu) ┤   (écran, transitions)  │ IKnowledge / IScorer / IPathAnalyzer └► state.sock (1 ligne/état)
ultima-mirror ─────┘   (enfant, sur transition seulement)
settings.json (inotify, compagnon → daemon)
```

## 1. Transport

| Élément | Chemin | Détail |
|---|---|---|
| État courant | `$XDG_RUNTIME_DIR/ultima/state.json` | un objet JSON (UTF-8, une ligne + `\n`), réécrit **atomiquement** : fichier temporaire `.state.json.tmp` dans le même dossier, `fsync`, puis `rename(2)`. Un lecteur voit l'ancien OU le nouveau fichier entier, jamais un fichier partiel. Droits `0600`, dossier `0700`. |
| Flux | `$XDG_RUNTIME_DIR/ultima/state.sock` | socket Unix **flux**. À la connexion, le client reçoit le **dernier** état, puis **une ligne JSON par nouvel état** (terminée par `\n`). Plusieurs clients. Requêtes montantes : §4.1 (`review.open`). |
| Revues | `$XDG_RUNTIME_DIR/ultima/review/<hash>.json` | `ReviewTimeline` (v1.2) écrite à la demande (§4.1), tmp + rename. |
| Journal | `~/.local/state/ultima/daemon.log` (`$XDG_STATE_HOME`) | `AAAA-MM-JJ HH:MM:SS.mmm NIVEAU message`, niveau ∈ `INFO` `WARN` `ERROR` `DEBUG` (aligné sur 5) ; copie sur stderr ; rotation > 5 Mio → `daemon.log.1`. Jamais à la racine de `~`. |
| Verrou | `$XDG_RUNTIME_DIR/ultima/daemon.lock` | une seule instance par utilisateur (verrou exclusif, contient le PID). |
| Réglages | `~/.config/ultima/settings.json` (`$XDG_CONFIG_HOME`) | écrits par le compagnon, **lus** par le daemon (jamais écrits). |

Sans `$XDG_RUNTIME_DIR` : repli `/tmp/ultima-<utilisateur>`. Option `--runtime-dir` pour tout autre dossier.

**Publication seulement si l'état change** : empreinte SHA-256 de l'état sans son horodatage `at`.
Aucun message « je suis vivant » : un état identique n'est jamais republié.

**Sérialisation** : System.Text.Json, camelCase, enums en chaîne (`"Shop"`), `null` explicite —
même forme que `docs/22-state.sample.json` (prouvé par `ContractShapeTests`). Contrat publié : **v1.3**
(`contractVersion` 1, `contractMinor` 3). État réel généré par le daemon (save réelle, jeu fermé) :
[`state.v13.sample.json`](state.v13.sample.json).

| Champ (version) | Source | Rempli quand |
|---|---|---|
| `run.party[]` (v1.2, `AllyView`) | save (tous les joueurs, `isLocal`) ; PV pris en mémoire si la dernière lecture vérifiée est plus récente que la save | run en cours ; solo = 1 entrée |
| `map` (v1.2, `MapView`) | save : carte de l'acte COURANT (types de case = `RoomTypes`, liens, cases visitées, position, boss) | run en cours ; actes futurs jamais publiés |
| `route` (v1.2, `RouteView`) | `IRoutePlanner` (Hermes `RoutePlanner`) sur `map` + contexte | à chaque save ; panne → `null` |
| `event` (v1.2, `EventView`) | miroir section `event` (Miroir 2) + `IEventAdvisor` (Hermes) | écran `Event` ; **`null` tant que le miroir ne lit pas cette section** |
| `settings.hud` (v1.3, `HudSettings`) | `settings.json` | toujours (défaut `verre, 0.55, 0.62, right`) |
| `card.description`, `potion.score` (v1.1), `score.grade`, `score.deltaG`, `term.evidence` | connaissance / calcul | selon le calcul (`null` sinon) |

`ScoringContext.Allies` (v1.2) : alliés de la save, joueur local exclu (vide en solo), passé à tout le calcul.

### Sémantique (rappel du contrat)
- `score == null` : calcul indisponible → l'UI affiche « — », jamais 0. Le daemon met `null` quand le
  calcul **lève** une exception ou renvoie un **stub vide** (aucun terme), jamais un crash.
- `health.degraded != null` : l'UI l'affiche en tête (voir §3).
- `screen` pilote les modules contextuels : `reward` n'est publié qu'en `Reward`, `shop` qu'en `Shop`.
- **Probabiliste seulement** : aucune donnée cachée (rng, odds, sacs, ids pré-tirés, valeurs des
  tirages de rareté du log — seul le *signal* « tirage » est utilisé).

## 2. Cycle de vie

| Moment | Ce que fait le daemon |
|---|---|
| Démarrage | verrou → profil vanilla actif (`ProfileLocator`, jamais `modded/`) → connaissance (`~/.local/share/ultima/kb/kb.json`, sinon noms = identifiants) → données (`ULTIMA_DATA`, §6) → calcul (Hermes) → réglages → jeu lancé ? → save (`SaveWatcher`) → `godot.log` : **rejoué depuis le début si le jeu tourne** (écran courant retrouvé sans lecture mémoire), sinon suivi depuis la fin → publication → `nice 10` + E/S idle sur tous les fils. |
| Jeu lancé | détecté sur activité inotify du dossier des logs (le jeu fait tourner `godot.log` au démarrage) puis `/proc/PID/comm == SlayTheSpire2` ; écran `Menu` ; **un** `ultima-mirror layout --json` (lecture du fichier `sts2.dll`, pas de la mémoire). |
| Jeu arrêté | `pidfd_open` + `poll` bloquant : notification immédiate, 0 % CPU ; écran `Unknown`, `gameRunning=false`, vues mémoire effacées, état fichiers seulement. |
| Save réécrite | (le jeu écrit `X.tmp` puis renomme) relecture → deck, reliques, potions, scores, chemins recalculés **une fois par save**. Plus de run → écran `Menu`. |
| Réglages modifiés | rechargés (minuteur ponctuel 150 ms pour regrouper tmp + rename) ; JSON invalide → réglage précédent conservé. `hud` validé champ par champ (valeur invalide → défaut de CE champ). |
| SIGTERM / SIGINT | arrêt propre (< 500 ms mesuré), socket retirée, `state.json` conservé (dernier état). |

### Écrans (machine à états `Screens/ScreenTracker.cs`, rejouée sur les logs réels du 2026-10-29)

| Signal (`godot.log`) | Écran | Lecture mémoire |
|---|---|---|
| `Preloading 'Combat Room'` / `Creating NCombatRoom` | `Combat` | **aucune** (en attente annulée) |
| `Preloading 'Event Room'` | `Event` | `event` (une fois par salle ; plus du tout si non prise en charge) |
| `Preloading 'RestSite Room'` / `'Treasure Room'` | `RestSite` / `Treasure` | — |
| `Preloading 'Merchant Room'` | `Shop` | `run,shop` + vérification croisée |
| `Card rarity: Rolled` après combat/événement/feu/trésor | `Reward` | `reward` (1 lecture par rafale) |
| `Card rarity: Rolled` depuis la carte | inchangé (génération du marchand) | — |
| `Player N selected card reward` (joueur **local**) | `Map` (ou l'écran d'origine si ce n'était pas un combat) | — |
| `Player vote changed for N` (joueur **local**) / `Continuing run` | `Map` | — |

En co-op, le joueur local est celui de la save dont `net_id == SteamID64` (solo : `1`) ; les choix et
votes des autres joueurs ne changent pas l'écran.

### Miroir mémoire (processus enfant)
- `ultima-mirror probe --json --sections … --timeout-ms 2700` : **une** ligne JSON
  (`{v, cmd, exit, ms, error, health{gameRunning, attached, ptraceScope, pid, attachMs, runtime},
  sections{name:{stable, attempts, ms, error, value}}, compare}`), code de sortie inchangé.
- Appelé **uniquement** sur les transitions ci-dessus ; anti-rebond 250 ms ; **un seul en vol** ;
  garde au départ (jeu lancé, écran toujours pertinent, jamais `Combat`, pas de patch) ; tué (arbre
  entier) après 3 s. Chaque lecture est journalisée : `mirror Shop exit=0 mirror ms=498`.
- Exécutable : `--mirror`, sinon `$ULTIMA_MIRROR`, sinon `ultima-mirror` dans le dossier du daemon (installation
  `~/.local/share/ultima/bin/`), sinon `../mirror/ultima-mirror`,
  sinon `build/mirror/ultima-mirror` du dépôt. `DOTNET_ROOT` transmis s'il manque.

## 3. Santé et modes dégradés (`health`)

| Situation | `gameRunning` | `saveOk` | `mirrorOk` | `degraded` |
|---|---|---|---|---|
| Jeu fermé | false | true | false | `null` — état des fichiers seulement |
| Tout va bien | true | true | true (dernière lecture) | `null` |
| Save illisible (JSON invalide, `.backup` aussi) | — | false | — | `save illisible : dernier état valide conservé` |
| Profil introuvable | — | false | — | `profil du jeu introuvable (…)` |
| Patch du jeu (`layout` code 4) | true | — | false | `lecture mémoire indisponible (patch du jeu)` — miroir désactivé jusqu'au prochain lancement |
| `ptrace_scope` ≠ 0 (attache refusée) | true | — | false | `lecture mémoire refusée : ptrace_scope=N (doit valoir 0)` |
| Attache impossible (autre cause) | true | — | false | `lecture mémoire indisponible (attache impossible)` |
| Lecture > 3 s | true | — | false | `lecture mémoire trop lente (abandonnée)` |
| Lecture instable (code 7) | true | — | false | `lecture mémoire instable (réessai à la prochaine transition)` |
| `ultima-mirror` absent | true | — | false | `lecture mémoire indisponible (ultima-mirror introuvable)` |
| Save ≠ mémoire au marchand | true | — | false | `divergence save↔mémoire : or 120≠116, …` — **l'inventaire n'est pas publié** |
| Calcul en panne (Hermes) | — | — | — | aucun : scores/chemins `null` (« — ») |

Plusieurs causes : messages joints par ` ; `. `mirrorLatencyMs` = durée de la dernière lecture.

**Vérification croisée** : à chaque lecture du marchand, PV, PV max, or, deck (multiensemble
id+amélioration) et reliques (ordre) de la mémoire sont comparés à la save (relue si besoin).
Divergence → message, `mirrorOk=false`, `shop=null`. Jamais de faux chiffre.

## 4. Brancher un client

Lire l'état courant (overlay qui démarre, script) :
```bash
jq .screen "$XDG_RUNTIME_DIR/ultima/state.json"
```
Suivre les changements (une ligne par état) :
```bash
socat -u UNIX-CONNECT:"$XDG_RUNTIME_DIR/ultima/state.sock" - | jq -c '{screen, gold: .run.gold, degraded: .health.degraded}'
```
Python (stdlib) :
```python
import json, os, socket
path = os.path.join(os.environ["XDG_RUNTIME_DIR"], "ultima", "state.sock")
s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM); s.connect(path)
for line in s.makefile("r", encoding="utf-8"):
    state = json.loads(line)          # EngineState (camelCase)
    if state["contractVersion"] > 1: raise SystemExit("mettre à jour ULTIMA")
    print(state["screen"], state["health"]["degraded"])
```
Règles client : ignorer les champs inconnus ; refuser une version **majeure** supérieure en
l'annonçant ; afficher `degraded` en tête ; `null` → « — ». Une connexion puis fermeture immédiate
(sonde de vie du compagnon toutes les ~4 s) est acceptée sans bruit ni fuite (EOF détecté).
Un client lent ne bloque jamais le daemon (file bornée à 8 états par client, les plus récents gardés).

### 4.1 Requêtes : revue de run (`review.open`, v1.2)
Envoyer UNE ligne JSON sur la socket ; la réponse (une ligne, reconnaissable à son champ `op`) n'est
envoyée qu'à ce client, entre deux lignes d'état :
```
→ {"op":"review.open","runId":"1780696537"}          (runId = nom du fichier history/<runId>.run, « .run » facultatif)
← {"op":"review.open","runId":"1780696537","ok":true,"path":"/run/user/1000/ultima/review/3fa2…e1.json"}
← {"op":"review.open","runId":"1780696537","ok":false,"error":"review indisponible"}
```
Calcul hors boucle principale, 30 s max, une revue à la fois, résultat mis en cache (hash du run + date du
fichier + implémentation). Refusé pendant une partie (« calculée hors ligne »). **Aujourd'hui Hermes ne
fournit aucune implémentation d'`IReviewBuilder` : la réponse est toujours `"review indisponible"`.**
Autres erreurs : `runId invalide` (seulement `[A-Za-z0-9_-]`), `run introuvable`, `op inconnue`,
`requête illisible`. Requête > 4 Kio : client déconnecté.

### 4.2 Section `event` attendue du miroir (demande à Miroir 2)
Le daemon appelle `ultima-mirror probe --json --sections event` à l'entrée d'une salle d'événement. La CLI
appelle `GameReader.ReadEvent()` si la bibliothèque l'expose (idem `ReadPiles()` pour `piles`), sinon la section
est listée dans `"unsupported"` (code de sortie inchangé, pas de dégradation) et le daemon ne retente qu'au
prochain lancement du jeu. Forme attendue de `sections.event.value` (champs absents tolérés) :
```json
{"eventId":"EVENT.ABYSSAL_BATHS","name":"Bains abyssaux",
 "options":[{"id":"ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE","label":"Plonger","description":"…","available":true}]}
```
Le daemon passe au calcul les identifiants NUS (`ABYSSAL_BATHS`, `IMMERSE` = dernier segment après « . »),
format de `data/calibration/events.json`. `piles` : uniquement des COMPTES/contenus visibles, jamais l'ordre
de la pioche (champ interdit par `ReadGuard`).

Alternative sans socket : surveiller `state.json` (inotify `IN_MOVED_TO` sur le dossier), mais sa
seule présence ne prouve pas que le daemon tourne (le fichier survit à l'arrêt).

## 5. CLI

```
ultima-daemon run    [options]   premier plan (systemd : §7)
ultima-daemon status [options]   état publié + daemon joignable ? (code 3 si injoignable)
ultima-daemon dump   [options]   calcule UNE fois l'état depuis les fichiers, l'écrit sur stdout (rien n'est publié, aucune lecture mémoire)
  --root DIR  --runtime-dir DIR  --settings FILE  --mirror FILE  --no-mirror  --no-scoring
  --data DIR  --game-comm NAME   --log FILE   -v|--verbose
```
Journal : `~/.local/state/ultima/daemon.log` (+ stderr → journald sous systemd), une ligne par événement
utile, jamais par sondage.

## 6. Données du calcul : `ULTIMA_DATA`

Convention fixée par le daemon (demande de Hermes, docs/26 §6.3) : **`ULTIMA_DATA` = dossier racine
des données**, celui qui contient `meta/`, `catalog/`, `calibration/`… (dans le dépôt : `<repo>/data`).
Compatibilité : un dossier qui contient `data/meta` (racine du dépôt) est ramené à `<dossier>/data`.

Ordre : `--data DIR` → variable `ULTIMA_DATA` → `data/` trouvé en remontant depuis l'exécutable.
La valeur retenue est **réécrite dans l'environnement du daemon** avant de construire le moteur, donc
tous ses résolveurs (`ScoringData`, `DataPaths`, `CurseIndex`…) lisent la même racine. Une valeur qui
ne mène à aucun `meta/` est ignorée et signalée. Sans données : scores et chemins `null`. Journal :
`données : ULTIMA_DATA=/home/mio/sts2/data (à côté de l'exécutable)`.

Noms et textes FR : base de l'Encyclopédiste `$XDG_DATA_HOME/ultima/kb/kb.json`
(`~/.local/share/ultima/kb/kb.json`, construite par `tools/kb/build-kb`). Absente : noms = identifiants,
malédictions reconnues quand même (liste des classes `Curse` du source v0.107.1).

## 7. Installation (Mio — rien n'est installé automatiquement)

```bash
cd ~/sts2
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet publish Ultima.Mirror/src/Ultima.Mirror.Probe -c Release -o build/mirror
dotnet publish Ultima.Daemon -c Release -o build/daemon
build/daemon/ultima-daemon dump | jq '.health, .screen, .run.floor'   # essai sans rien publier

# service utilisateur (facultatif)
mkdir -p ~/.config/systemd/user
cp docs/ipc/ultima-daemon.service ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now ultima-daemon.service
journalctl --user -u ultima-daemon -f
build/daemon/ultima-daemon status
```
Pour un lancement manuel : `build/daemon/ultima-daemon run` (le daemon se met lui-même en nice 10 +
E/S idle). Pré-requis du miroir : `cat /proc/sys/kernel/yama/ptrace_scope` doit afficher `0`.

## 8. Sobriété (exigence Mio : baisses de FPS à la pioche)

- Aucun minuteur périodique : seuls existent l'anti-rebond du miroir (ponctuel, armé par une
  transition), le regroupement des écritures de `godot.log` (ponctuel, 100 ms) et des réglages (150 ms).
  `GodotLogTail.Start()` (sondage 250 ms) n'est **pas** utilisé : le log est lu sur événement inotify.
- **Aucune lecture mémoire en combat** (garde au départ + annulation à l'entrée en combat).
- Scores recalculés une fois par save ; vues récompense/marchand mises en cache.
- GC poste de travail non concurrent ; nice 10 + classe d'E/S idle sur tous les fils.
- Mesuré jeu fermé : 0 tick CPU et 6 réveils de fils en 60 s (voir `status/cableur.md`).
