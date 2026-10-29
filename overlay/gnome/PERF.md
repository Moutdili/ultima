# ULTIMA overlay GNOME : coûts et leviers A/B

Symptôme (Mio, 2026-10-29) : baisses de FPS ressenties **quand les cartes sont piochées**, extension
active (rail + verre, `disable_unredirect`). **Non mesuré.** Skill `sts2-performance` : « Suspects
(à départager par MESURE, pas par intuition) ». Ce document liste les leviers exposés pour que le
Chronométreur les départage. Il ne dit pas lequel est en cause.

## 1. Leviers réglables à chaud (GSettings `org.gnome.shell.extensions.ultima`)

Chaque changement est appliqué **sans recharger l'extension** et journalisé avec un horodatage :
`[ULTIMA] réglages perf : …` puis `[ULTIMA] plan : visible=… flou=… unredirect-inhibé=… sondage=…`.

| Clé | Valeurs (défaut en gras) | Effet | Risque |
|---|---|---|---|
| `blur-mode` | **`panel-only`**, `always`, `never` | `Shell.BlurEffect` (BACKGROUND) actif seulement panneau ouvert. Sur le rail, `enabled=false` : aplat teinté, plus aucun calcul de flou. `always` = comportement du spike S1 (flou aussi sur le rail). | `never` : texte sur fond teinté non flouté (lisibilité à vérifier) |
| `unredirect-mode` | **`while-shown`**, `while-expanded`, `never` | `global.compositor.disable_unredirect()` tant que l'overlay est visible (S1), seulement panneau ouvert, ou jamais. Hors inhibition, Mutter peut afficher le jeu plein écran en scanout direct (sans composition). | `while-expanded`/`never` : le **rail peut disparaître** au-dessus du jeu plein écran. Le survol de la zone reste actif, puisqu'il passe par le pointeur et non par l'acteur. |
| `poll-interval-ms` | 0 à 1000, **80** | Intervalle du suivi du pointeur (survol). `0` désactive le survol : le panneau ne s'ouvre plus qu'avec Super+Maj+I. Valeurs 1-19 ramenées à 20. | > 150 ms : ouverture au survol moins réactive |

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

- **Zéro sondage quand masqué.** Masqué (Super+Maj+O) ou vue d'ensemble : acteur invisible, flou
  coupé, unredirect rendu, minuterie du pointeur **retirée** (`GLib.source_remove`) et minuteur de
  repli annulé. Il ne reste aucune source GLib d'ULTIMA dans la boucle du compositeur.
  Preuve par le code : un seul point de décision `computePlan()` (`ultima@mio/plan.js`), appliqué par
  `_applyPlan()` (`extension.js`), où `poll = shown && !pinned && ms > 0`.
  Preuve par les tests : `tests/test-render.js` §12 (36 combinaisons masquées : rien d'actif ;
  `Poller` sur horloge virtuelle : 0 tick en 60 s) et `tests/test-extension.mjs` §5 (vraie
  extension, 10 min masquée : 0 lecture de `global.get_pointer()`, 0 source GLib).
- **Épinglé = pas de sondage** (Super+Maj+I) : le panneau ne se replie pas, donc le survol est inutile.
- **État relu seulement sur changement** : `Gio.FileMonitor` sur `$XDG_RUNTIME_DIR/ultima/state.json`,
  avec un anti-rebond de 100 ms (une rafale d'écritures donne une seule lecture asynchrone). Aucune
  relecture périodique.
- **Rendu différé** : panneau replié, seul le rail (3 pastilles) est mis à jour. Le texte est
  appliqué à l'ouverture. Panneau ouvert, un état republié sans changement visible (par ex. seul
  `at` change) ne touche aucun acteur : pas de relayout, pas de redessin.
- **Peu d'acteurs** : un `St.Label` par bloc (≈ 10), mis à jour seulement si le texte change.

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

Lecture : B contre C isole le flou du rail, C contre D isole l'unredirect, E contre F isole le flou
du panneau, C contre H isole le sondage, G contre A vérifie qu'ULTIMA masqué ne coûte rien.

## 4. Non prouvé (à mesurer en fenêtre de test)

- **Aucun chiffre de FPS.** Tous les leviers sont câblés et testés sans session, aucun n'est mesuré.
- Que Mutter repasse vraiment en scanout direct avec `unredirect-mode=while-expanded|never` alors que
  le rail est affiché, et si le rail reste visible dans ce cas.
- Que `BlurEffect.enabled=false` supprime tout le coût du flou (attendu : l'effet n'est plus exécuté).
- La hauteur de ligne réelle (mesurée en session par une étiquette sonde, journalisée
  `[ULTIMA] hauteur de ligne mesurée`) et l'estimation du retour à la ligne (32 caractères pour 232 px).
- **Surveillance d'un dossier absent** : tant que `$XDG_RUNTIME_DIR/ultima/` n'existe pas (daemon
  jamais lancé), le backend inotify de GLib vérifie son apparition toutes les 4 s dans son propre
  thread (`inotify-missing.c`), hors de la boucle du compositeur. Ce comportement est connu du code
  GLib mais n'a pas été observé ici. Le coût est négligeable et disparaît dès que le daemon crée le dossier.

## 5. Leviers non implémentés (si les mesures les réclament)

- Flou statique (capture figée à l'ouverture du panneau, au lieu d'un flou recalculé à chaque image).
- `nice 10` du daemon (propriété du Câbleur, hors `overlay/gnome`).
