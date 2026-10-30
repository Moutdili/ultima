# ULTIMA overlay GNOME : coûts et leviers A/B

Symptôme (Mio, 2026-10-29) : baisses de FPS ressenties **quand les cartes sont piochées**, extension
active (rail + verre, `disable_unredirect`). **Non mesuré.** Skill `sts2-performance` : « Suspects
(à départager par MESURE, pas par intuition) ». Ce document liste les leviers exposés pour que le
Chronométreur les départage. Il ne dit pas lequel est en cause.

## 1. Leviers réglables à chaud (GSettings `org.gnome.shell.extensions.ultima`)

Chaque changement est appliqué **sans recharger l'extension** et journalisé avec un horodatage :
`[ULTIMA] réglages perf : …` puis `[ULTIMA] plan : visible=… verre=… flou=… reflet=… loupe=… unredirect-inhibé=… survol=…`.

| Clé | Valeurs (défaut en gras) | Effet | Risque |
|---|---|---|---|
| `blur-mode` | **`panel-only`**, `always`, `never` | `Shell.BlurEffect` (BACKGROUND) actif seulement panneau ouvert. Sur le rail, `enabled=false` : aplat teinté, plus aucun calcul de flou. `always` = comportement du spike S1 (flou aussi sur le rail). | `never` : texte sur fond teinté non flouté (lisibilité à vérifier) |
| `unredirect-mode` | **`while-shown`**, `while-expanded`, `never` | `global.compositor.disable_unredirect()` tant que l'overlay est visible (S1), seulement panneau ouvert, ou jamais. Hors inhibition, Mutter peut afficher le jeu plein écran en scanout direct (sans composition). | `while-expanded`/`never` : le **rail peut disparaître** au-dessus du jeu plein écran. Le survol de la zone reste actif, puisqu'il passe par le pointeur et non par l'acteur. |
| `poll-interval-ms` | 0 à 1000, **80** | Suivi du pointeur : période (`poll`) ou délai de regroupement des mouvements (`event`). `0` désactive le survol : le panneau ne s'ouvre plus qu'avec Super+Maj+I. Valeurs 1-19 ramenées à 20. | > 150 ms : ouverture au survol moins réactive |
| `hover-source` | **`event`**, `poll` | `event` (recommandation R4 de `docs/perf/HYPOTHESES.md`) : écoute du signal `position-invalidated` du curseur (`global.backend.get_cursor_tracker()`, comme `ui/magnifier.js`) ; au premier mouvement on se déconnecte et on lit le pointeur une fois 80 ms plus tard. **Pointeur immobile : zéro réveil.** En mouvement : ≤ 12,5 lectures/s. `poll` : minuterie 12,5 Hz permanente (spike S1, prouvé sur le vrai jeu). | `event` non prouvé au-dessus du jeu plein écran XWayland : si le survol n'ouvre plus rien, `poll` |

Le **niveau de verre** suit `HudSettings.Quality` (contrat v1.3), lu dans `~/.config/ultima/settings.json`
(outil : `tools/hud-set.py quality=eco|verre|loupe`), sinon dans `state.settings.hud`, sinon la clé
GSettings `quality`. Depuis la v1.4, **apparence** (thème, police, tons, position, railEdge) vit dans
`appearance.hud` du même fichier (`tools/hud-set.py theme=… fontFamily=… fontSize=… anchor=… width=…
offsetX=… offsetY=… positionSide=… railEdge=…`) : ce sont des réglages de rendu, aucun n'ajoute de
coût par image, et ils ne sont relus qu'au changement du fichier :

| Niveau | Rail (replié) | Panneau ouvert | Coût attendu (hypothèse) |
|---|---|---|---|
| `eco` | aplat teinté #192930 @ 0.85 + liseré or | idem, **aucun effet** (ni flou, ni GLSL, ni clone) | ~0 : styles St seulement |
| `verre` (défaut) | aplat (flou coupé) | `Shell.BlurEffect` BACKGROUND rayon 24 + reflet GLSL (`Shell.GLSLEffect`, uniformes fixes) | flou recalculé à chaque image où le jeu bouge, borné à la largeur du panneau (280-520 px) × H logiques |
| `loupe` | aplat | flou rayon 10 + reflet + `Clutter.Clone` de la fenêtre du jeu ×1,06 réfracté sur les bords | + une peinture de la texture du jeu dans un tampon (largeur × H) ; clone créé à l'ouverture (plaque immobile), **détruit** au début du repli ; absent si l'unredirect n'est pas inhibé |

Commandes (installé dans `~/.local/share/gnome-shell/extensions/ultima@mio`) :

```bash
S="gsettings --schemadir $HOME/.local/share/gnome-shell/extensions/ultima@mio/schemas"
K=org.gnome.shell.extensions.ultima
$S set $K blur-mode never            # ou panel-only / always
$S set $K unredirect-mode never      # ou while-shown / while-expanded
$S set $K poll-interval-ms 0         # ou 80, 200…
$S reset $K blur-mode; $S reset $K unredirect-mode; $S reset $K poll-interval-ms   # retour aux défauts
journalctl --user -b -o short-precise | grep -E '\[ULTIMA\] (plan|réglages perf)'  # horodatage des bascules
```

## 2. Toujours actif (pas de réglage, aucun coût ajouté)

- **Zéro suivi quand masqué.** Masqué (Super+Maj+O) ou vue d'ensemble : acteur invisible, flou,
  reflet et loupe coupés, unredirect rendu, signal du curseur **déconnecté** et minuterie du pointeur
  **retirée** (`GLib.source_remove`), minuteur de repli annulé. Il ne reste aucune source GLib d'ULTIMA
  dans la boucle du compositeur.
  Preuve par le code : un seul point de décision `computePlan()` (`ultima@mio/plan.js`), appliqué par
  `_applyPlan()` (`extension.js`), où `poll = shown && !pinned && ms > 0`.
  Preuve par les tests : `tests/test-hud.js` §6-8 (108 combinaisons masquées : rien d'actif ;
  `HoverWatch` sur horloge virtuelle : 0 tick en 60 s) et `tests/test-extension.mjs` §8 (vraie
  extension, 10 min masquée, pointeur sur le rail : 0 lecture de `global.get_pointer()`, 0 source GLib,
  0 écriture d'acteur).
- **Épinglé = pas de suivi du pointeur** (Super+Maj+I) : le panneau ne se replie pas, donc le survol est inutile.
- **État et réglages relus seulement sur changement** : `Gio.FileMonitor` sur `$XDG_RUNTIME_DIR/ultima/state.json`
  et `~/.config/ultima/settings.json` (lu, jamais écrit),
  avec un anti-rebond de 100 ms (une rafale d'écritures donne une seule lecture asynchrone). Aucune
  relecture périodique.
- **Thème et police : rien au repos.** Les fichiers `themes/*.json` ne sont lus qu'à l'ouverture de
  l'extension et à chaque changement de `settings.json` (jamais à chaque publication d'état, jamais par
  image). Changer de thème ou de police ne crée **aucune** minuterie : les tests le prouvent sur
  l'horloge virtuelle (`tests/test-extension.mjs` §11bis, 10 min de repos après un changement de thème
  et de police : 0 source GLib, 0 écriture d'acteur). Un thème cassé est résolu une fois puis remplacé
  par `sts` : aucun coût récurrent.
- **Rendu différé** : panneau replié, le rail est une simple bande de verre (skill design §12) :
  un état publié ne touche **aucun acteur** (prouvé : `tests/test-extension.mjs` §3, compteur
  d'activité des bouchons). Le texte est appliqué à l'ouverture. Panneau ouvert, un état republié
  sans changement visible (par ex. seul `at` change) ne touche aucun acteur.
- **Au repos (rail, pointeur immobile) : rien.** Aucune minuterie, aucune lecture du pointeur, aucune
  écriture d'acteur, aucun effet actif, aucune transition (prouvé sans session : `test-extension.mjs`
  §2, 10 min ; `test-hud.js` §7). Pointeur qui bouge ailleurs : ≤ 12,5 lectures/s, zéro écriture.
- **Ouverture sans relayout** (R6) : plaque de taille fixe, seule `translation_x` est animée (220 ms
  ease-out), contenu préparé avant l'animation puis fondu ligne par ligne ; ombre jamais recalculée.
  Fermeture 150 ms : le flou reste pendant le glissement (pas de saut), puis est coupé.
- **Peu d'acteurs** : une ligne par module (§11), mise à jour seulement si le texte change.

## 3. Protocole A/B proposé au Chronométreur (skill `sts2-performance`)

Même combat, 10 tours de pioche, MangoHud (`no_display,log_interval=0`), FPS moyen, 1 % low et p99
du temps par image pendant les pioches. Budget : perte de 1 % low ≤ 3 % par rapport à A.

| # | Condition | Réglages |
|---|---|---|
| A | sans ULTIMA | `gnome-extensions disable ultima@mio` |
| B | rail, configuration S1 | `blur-mode=always unredirect-mode=while-shown` |
| C | rail, **défaut v1** | `blur-mode=panel-only unredirect-mode=while-shown` |
| D | rail sans inhibition | `blur-mode=panel-only unredirect-mode=while-expanded` (noter si le rail reste visible) |
| E | panneau épinglé, flou | Super+Maj+I, `blur-mode=panel-only` |
| F | panneau épinglé, sans flou | Super+Maj+I, `blur-mode=never` |
| G | ULTIMA masqué | Super+Maj+O (doit être identique à A) |
| H | sans survol | `poll-interval-ms=0`, rail visible |
| I | survol S1 | `hover-source=poll`, rail visible (comparer à C, `hover-source=event`) |
| J | panneau épinglé, **eco** | Super+Maj+I, `tools/hud-set.py quality=eco` |
| K | panneau épinglé, **loupe** | Super+Maj+I, `tools/hud-set.py quality=loupe` |

Lecture : B contre C isole le flou du rail, C contre D isole l'unredirect, E contre F isole le flou
du panneau, C contre H isole le suivi du pointeur, C contre I compare `event` et `poll`, J/E/K
comparent les trois niveaux de verre panneau ouvert, G contre A vérifie qu'ULTIMA masqué ne coûte rien.
Si K dépasse le budget, le défaut reste `verre` (skill design §8).

## 4. Non prouvé (à mesurer en fenêtre de test)

- **Aucun chiffre de FPS.** Tous les leviers sont câblés et testés sans session, aucun n'est mesuré.
- Que Mutter repasse vraiment en scanout direct avec `unredirect-mode=while-expanded|never` alors que
  le rail est affiché, et si le rail reste visible dans ce cas.
- Que `BlurEffect.enabled=false` supprime tout le coût du flou (attendu : l'effet n'est plus exécuté).
- Que le signal `position-invalidated` arrive bien quand le pointeur bouge au-dessus du jeu plein écran
  (XWayland) : sinon `hover-source=poll`.
- Que les extraits GLSL (reflet, loupe) compilent sur le pilote radeonsi (aucun test hors session ne
  compile un shader) ; journal à surveiller : `JS ERROR` / `Cogl` dans `journalctl --user -b`.
- Le coût réel de la loupe (peinture du clone + effet hors écran) et du reflet (OffscreenEffect).
- La hauteur de ligne réelle (mesurée en session par une étiquette sonde, journalisée
  `[ULTIMA] hauteur de ligne mesurée`) et l'estimation du retour à la ligne (32 caractères pour 232 px).
- **Surveillance d'un dossier absent** : tant que `$XDG_RUNTIME_DIR/ultima/` n'existe pas (daemon
  jamais lancé), le backend inotify de GLib vérifie son apparition toutes les 4 s dans son propre
  thread (`inotify-missing.c`), hors de la boucle du compositeur. Ce comportement est connu du code
  GLib mais n'a pas été observé ici. Le coût est négligeable et disparaît dès que le daemon crée le dossier.

## 5. Leviers non implémentés (si les mesures les réclament)

- Flou statique (R7 : capture figée à l'ouverture du panneau, au lieu d'un flou recalculé à chaque image).
- Masquage automatique en combat (R3), si la mesure accuse `disable_unredirect`.
- `nice 10` du daemon (propriété du Câbleur, hors `overlay/gnome`).
