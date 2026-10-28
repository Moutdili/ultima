# tools/proof, tools/lab, tools/audit : le banc de preuves d'ULTIMA v2

Ces outils servent à rendre la **vérification en conditions réelles** bon marché (docs/20 §0 leçon 6,
docs/21 fiche G). Aucun ne lance le jeu. Aucun n'écrit dans le dossier du jeu ni dans ses données :
toutes les sorties vont dans `~/sts2-proofs/`, hors dépôt.

| Outil | Ce qu'il prouve | Voit l'overlay ? |
|---|---|---|
| `proof/capture-composed.py` | ce que Mio voit (image finale de Mutter) | **oui** |
| `proof/capture-game-x11.sh` | le contenu de la seule fenêtre X11 du jeu | non (par construction) |
| `audit/no-write-audit.sh` | non-négociables §1 : pas de `mods/`, `modded/` intact, aucune écriture d'ULTIMA | n/a |
| `lab/test-window.sh` | déroule une fenêtre de test : étapes, captures, audit, rapport Markdown | n/a |
| `lab/fake-fullscreen.py` | substitut du jeu : X11 plein écran, mire, compteur de clics/touches/focus | n/a |

La machine de Mio : GNOME Shell 50.6 sous Wayland, écran interne 1920x1200 à l'échelle **1,25**,
XWayland en `DISPLAY=:0` avec une racine X de **3072x1920** (`Xft.dpi: 192`, donc les clients X11 rendent
à 2x puis Mutter réduit l'image). Conséquences :
- la capture X11 d'une fenêtre plein écran fait **3072x1920** ;
- la capture composée fait **1920x1200** (pixels physiques) ;
- facteur entre les deux : **1,6**. Les coordonnées de clic X11 sont en pixels X (3072 de large).

---

## 1. `proof/capture-composed.py` : capture composée par le portail

```bash
tools/proof/capture-composed.py [--label txt] [--out-dir DIR] [--timeout 30] [--keep-original]
# stdout : chemin du PNG (~/sts2-proofs/composed-AAAAMMJJ-HHMMSS-mmm[-label].png)
# codes : 0 ok · 1 refusé/annulé · 2 erreur backend · 3 timeout (dialogue en attente ?) · 4 D-Bus
```
Le script appelle `org.freedesktop.portal.Screenshot.Screenshot("", {interactive: false})` via Gio,
s'abonne au signal `Response` **avant** l'appel, puis déplace le fichier écrit par le portail
(`~/Pictures/Screenshot.png`) vers `~/sts2-proofs/`. Il ne laisse donc rien dans `~/Pictures`.

**Autorisation, une seule fois.** Au premier appel non interactif, GNOME (xdg-desktop-portal-gnome 50)
demande s'il faut autoriser la capture. Le choix est mémorisé dans le permission store, table
`screenshot`, pour l'application hôte `""` (un script lancé hors Flatpak n'a pas d'app-id) :
```bash
flatpak permissions screenshot          # attendu : screenshot  screenshot    yes
# révoquer :  flatpak permission-remove screenshot screenshot ""
# ré-accorder sans dialogue :  flatpak permission-set screenshot screenshot "" yes
```
Observé le 2026-10-28 : le premier appel a mis 4,7 s, et le fichier `~/.local/share/flatpak/db/screenshot`
a été écrit à 18:53:41.88, pendant l'appel (envoyé à 18:53:37, réponse à 18:53:42). L'appel suivant a
pris 0,5 s, sans dialogue. Comme l'autorisation vaut pour **tout** processus hôte, n'importe quel
programme de l'utilisateur peut désormais faire une capture silencieuse. Pour revenir en arrière,
utiliser la commande `permission-remove` ci-dessus.

## 2. `proof/capture-game-x11.sh` : la fenêtre du jeu seule

```bash
tools/proof/capture-game-x11.sh --list                       # fenêtres X11 : id, pid, WM_CLASS, titre
tools/proof/capture-game-x11.sh [--class RE] [--name RE] [--id 0x…] [--label txt]
# codes : 0 ok · 2 aucune fenêtre · 3 plusieurs candidates · 4 import échoue · 5 image unie (noire ?)
```
L'outil parcourt `_NET_CLIENT_LIST`, compare `WM_CLASS` et le titre (regex insensibles à la casse), puis
lance `import -window <id>` (ImageMagick). Il calcule ensuite l'écart-type de l'image : en dessous de
0,005, l'image est jugée **unie** (une fenêtre GL/Vulkan qui ne se laisse pas lire donne souvent du
noir) et l'outil sort avec le code 5.

Classe par défaut : `slay the spire 2|slaythespire2|godot_engine`. Godot 4 met
`res_name=Godot_Engine` et `res_class=<nom du projet>`. **Ce n'est pas vérifié sur le vrai jeu** : à la
première fenêtre de test, lancer `--list` et noter la classe réelle (`lab/examples/gate-g-real-game.steps`
le fait). `_NET_WM_PID` est un PID du namespace Flatpak, il ne faut donc pas s'en servir pour retrouver
le PID hôte.

## 3. `audit/no-write-audit.sh` : les non-négociables

```bash
tools/audit/no-write-audit.sh check [--since '2026-10-28 00:00'] [--write-baseline F] [--baseline F]
tools/audit/no-write-audit.sh watch [--duration S] [--log F] [--paths 'D1:D2']
tools/audit/no-write-audit.sh selftest        # dans un dossier temporaire, sans toucher au jeu
```
- `check` vérifie que `mods/` est absent ou vide, qu'aucun fichier `*ultima*` n'existe dans le jeu ni
  dans les données, que `modded/` n'a pas changé depuis `--since` et que son manifeste sha256 est
  identique à `--baseline`. Code 0 si tout passe.
- `watch` lance `inotifywait -m -r` sur le dossier du jeu et sur les données. Il journalise **toutes**
  les écritures (create/modify/close_write/moved/delete/attrib) avec un horodatage à la microseconde,
  et note pour chaque événement les processus qui tiennent encore le fichier ouvert (`pid:comm:mode`).
  Un échantillonneur relève aussi, chaque seconde, les fds **en écriture** ouverts par un processus
  ULTIMA (cmdline correspondant à `ULTIMA_RE`, défaut `ultima`) dans les arbres surveillés.
  La ligne `# PRET` du journal indique que toutes les surveillances sont posées.
  - `EVENT` : une écriture, quel que soit son auteur. Le jeu écrit ses saves et ses logs, c'est normal.
  - `ALERTE` : une écriture dans le dossier d'installation du jeu ou dans un profil `modded/`.
  - `VIOLATION` : un processus ULTIMA tient un fd en écriture, ou un fichier porte « ultima » dans son nom.

**Limites, à ne pas sur-vendre :**
- inotify ne donne pas le PID de l'écrivain. L'attribution reste heuristique. Une écriture d'ULTIMA
  très brève (moins d'une seconde) serait journalisée en `EVENT` sans être attribuée, mais elle
  apparaîtrait quand même dans le journal. Pour une attribution exacte, il faut fanotify ou auditd, donc
  root. Commande à faire lancer par Mio si on veut cette preuve forte :
  `sudo auditctl -w "<dossier>" -p wa -k sts2` puis `sudo ausearch -k sts2 -i`.
- `ULTIMA_RE` attrape aussi les shells d'agents dont la ligne de commande contient « ultima ». Ce n'est
  une VIOLATION que s'ils tiennent un fd en écriture dans le jeu, ce qui serait de toute façon une faute.
- Un événement qui survient avant la ligne `# PRET` peut échapper à la surveillance.

## 4. `lab/test-window.sh` : la fenêtre de test

```bash
tools/lab/test-window.sh ETAPES.steps [--auto [S]] [--no-game] [--game-proc RE] [--class RE] [--no-composed]
```
Le script vérifie que le jeu tourne (`pgrep -x SlayTheSpire2`, PID hôte) et s'arrête sinon. Il lance
ensuite `check --write-baseline`, démarre `watch` en fond et attend la ligne `# PRET`. Pour chaque
étape, il prend une capture composée et une capture X11, et vérifie que le jeu est toujours vivant.
Il finit par `check --baseline`, puis écrit `~/sts2-proofs/<date>/<heure>-<nom>/rapport.md`, avec
`captures/`, `audit.log` et `run.log`.

Directives du fichier `.steps` : `manuel:` (attend Entrée, puis demande le résultat observé),
`cmd:` (avec `$ULTIMA_TOOLS` et `$STEP_DIR` dans l'environnement), `attendre:`, `capture:`, et
`attendu:` (rattachée à l'étape précédente et écrite **avant** la session). Exemples :
`lab/examples/smoke-fake.steps` (répétition à blanc) et `lab/examples/gate-g-real-game.steps` (porte G).

## 5. `lab/fake-fullscreen.py` : le faux jeu

```bash
python3 tools/lab/fake-fullscreen.py [--windowed 800x500] [--timeout S] [--state F.json] [--mimic]
```
Fenêtre GTK3 forcée en X11 (`GDK_BACKEND=x11`), comme le jeu. Elle affiche des barres SMPTE, un dégradé
de gris (repère de transparence), une grille à 10 %, des coins magenta et un panneau « CLICS REÇUS : n ».
Chaque événement (CLICK, KEY, FOCUS) est écrit sur stdout et dans `--state` (JSON). Pour la porte S1 :
un clic à travers la zone click-through de l'overlay doit incrémenter `clicks`, et `focus` doit rester
`true`. `--mimic` reprend la `WM_CLASS` supposée du jeu (`Godot_Engine` / `Slay the Spire 2`). Maj+Échap
ferme la fenêtre. Ce banc n'envoie aucun clic synthétique (pas de `xdotool`) : le clic vient d'une
main humaine ou d'un outil XTEST de l'agent qui teste.
