# Hermes s35 — T4 : HUD GNOME `ultima@mio` v1.4 (thèmes, polices, disposition, modules perso)

Branche `hermes/t4-hud-v14`, partie de `origin/main` (60bb99a) + `git merge origin/main` (« Déjà à jour »),
puis `git fetch origin` + `git rebase origin/main` (« à jour ») avant push.
Commit poussé : **4e9bd1d629d0afc0c38a6e899e9c782c0ea09b8a** (`feat(hud): thèmes, polices, disposition et
modules perso (v1.4)`), 21 fichiers, +2228/-323.
Périmètre respecté : `overlay/gnome/**` seulement (aucun fichier de `Ultima.Engine/`, `schemas/`, `docs/`,
`themes/`, `tools/`, ni `overlay/companion/`). Headless : aucune session, aucun `gsettings`, aucune capture.

## Fait

1. **Résolveur de thème** — nouveau module pur `ultima@mio/theme.js` :
   `appearance.hud.theme` → `extends` (récursif, 8 niveaux max, cycles refusés) → `appearance.hud.tones`
   (surcharges validées une à une) ; `sts` est **toujours** la base de la chaîne, donc un thème partiel
   n'efface jamais ce qu'il ne redéfinit pas. Teinte du verre depuis `glass.tint` + `glass.tintOpacity`
   (`eco` = même couleur, +0,13 d'opacité), liseré depuis le ton `border`, ombre depuis `background`.
   `auto-perso` = thème du personnage de la run (`run.characterId` → `ironclad|silent|defect|regent|necrobinder`),
   repli `sts` hors run ou si le thème du personnage manque. **Thème cassé** (JSON invalide, `api ≠ 1`,
   id invalide, parent introuvable, cycle) ⇒ repli `sts` + `problems[]` journalisés `[ULTIMA] thème : …`,
   **jamais d'exception** (résolution et chargement entourés de `try/catch` dans l'extension).
   Chargement : `~/.config/ultima/themes/` > `~/.local/share/ultima/themes/` > `themes/` livré avec
   l'extension (premier fichier gagnant par id ; `sts` est **toujours** relu dans l'extension pour que le
   repli ne puisse pas être cassé par un thème utilisateur).
2. **Aucun hexadécimal hors des fichiers de thème** : `palette.js` supprimé ; nouvelle source
   `ultima@mio/themes/sts.json` (copie **identique** de `themes/sts.json`, un test l'exige) ; les seules
   valeurs hexadécimales du HUD vivent dans les `*.json` de thème. Pont de vocabulaire v1.3 → v1.4 dans
   `theme.js` (`cream→text`, `gold→accent`, `red→danger`, `green→ok`, `blue→info`, `orange→warn`,
   `purple→curse`, `aqua→enchant`, `gray/lightGray→muted`), donc la vue (`render.js`/`grammar.js`) et le
   compagnon gardent leurs noms de tons et le thème décide des couleurs. Le test statique « palette » est
   remplacé par : **0 hex, 0 `rgb()` littéral, 0 couleur CSS nommée dans les 10 modules JS**.
3. **Polices** — `appearance.hud.font.family` / `.size` (12–24 px, hors bornes ramené), appliqués au CSS
   de St (`font-size`, `font-family`), repli sur la police du thème puis sur la police système ; famille
   validée par `FONT_FAMILY` (lettres/chiffres/espaces `._-` seulement) ⇒ **aucune injection CSS**
   possible ; hauteur de ligne remesurée et colonnes de repli recalculées (`wrapCols(largeur, taille)`)
   à chaque changement de police.
4. **Disposition** — `geometry.js` réécrit : `side` (left/right), `anchor` (top/center/bottom),
   `offsetX`/`offsetY` (bornés à 40 % de l'écran puis ramenés dans l'écran), `width` 280–520 px,
   `heightFraction` 0,2–0,85, `railEdge`. Le panneau est placé dans une **zone sûre** qui exclut la barre
   du haut, **« Fin du tour » (bas droite)** et la pioche (bas gauche) ; le modèle « tiroir » est conservé
   (plaque de la taille du panneau, seule `translation_x` anime l'ouverture, rail toujours collé au bord
   quel que soit `offsetX`), la zone de survol suit le panneau ouvert et le rail replié.
5. **Modules perso et plugins** — `render.js` dessine `EngineState.Custom` à la place de `modules[]` :
   en-tête (libellé + nombre de lignes + santé), lignes avec tons du contrat (`DATA_TONES`) et note S-F en
   tête, `screens` respecté (module natif **et** perso, `ModuleSetting.Screens`), santé ≠ ok **atténuée et
   nommée** (`retard`, `erreur`, `désactivé`), texte tronqué proprement (`…`, 60 car.), ordre et
   activation lus dans `settings.modules` (ids `x.`, doublons et ids inconnus ignorés). **Aucun code tiers
   exécuté** : seules des chaînes sont lues et échappées (test statique : pas d'`eval`, `Function`,
   `import()`, `GLib.spawn`, `Gio.Subprocess`).
6. **Performance** — thème et police ne sont relus qu'à l'ouverture et au changement de `settings.json`
   (jamais à chaque publication d'état, jamais par image) ; un changement de thème/police repose sur un
   re-style ponctuel (`styleKey` = thème + police + largeur) puis **plus rien** : 0 minuterie, 0 écriture
   d'acteur au repos (test sur horloge virtuelle, 10 min). `eco` inchangé (aucun flou, aucun reflet,
   aucune loupe ; seule l'opacité de l'aplat change).
7. **Documentation et outils** : `TEST-WINDOW.md` réécrit pour la fenêtre n°4 (20 gestes bureau + 7 gestes
   en jeu : thèmes, thème cassé, `auto-perso`, polices, largeur, ancrages, décalages, côté, `railEdge`,
   modules perso injectés dans un état, critères) ; `PERF.md` complété (thème/police sans coût au repos) ;
   `metadata.json` 0.4.0 ; `install-dev.sh` empaquette `appearance.js`, `theme.js` et `themes/sts.json` et
   lance la nouvelle suite ; `tools/hud-set.py` sait écrire `appearance.hud` (`theme=`, `fontFamily=`,
   `fontSize=`, `anchor=`, `positionSide=`, `width=`, `offsetX=`, `offsetY=`, `railEdge=`,
   `--reset-appearance`).
8. **Tests** : nouvelle suite `tests/test-theme.js` (gjs **et** node) ; `test-pure.js` réécrit (géométrie
   sur 5 résolutions × 3 ancrages × 5 décalages × 3 largeurs × 3 hauteurs, contrôles statiques) ;
   `test-hud.js` §9 (`appearance.hud`) ; `test-render.js` §13 (modules perso, `screens`, budget de lignes) ;
   `test-extension.mjs` §11bis (thème, police, disposition, `railEdge`, thème cassé, `auto-perso`, tons,
   modules perso, zéro minuterie) ; bouchons complétés (`Gio.File.load_contents/enumerate_children`,
   `GLib.get_user_data_dir`).

## Prouvé

Commandes et sorties brutes (courtes).

```
$ cd overlay/gnome && npm test        # avant : 458 OK (main) ; après : 729 OK
OK   aucune erreur journalisée
tous les tests passent
```
Détail par suite (0 échec partout) :
```
gjs -m tests/test-pure.js                                                137 OK  0 FAIL
gjs -m tests/test-theme.js                                                57 OK  0 FAIL
gjs -m tests/test-hud.js                                                  90 OK  0 FAIL
gjs -m tests/test-render.js                                               94 OK  0 FAIL
node tests/test-theme.js                                                  57 OK  0 FAIL
node tests/test-hud.js                                                    90 OK  0 FAIL
node tests/test-render.js                                                 94 OK  0 FAIL
node --import ./tests/shell-harness/register.mjs tests/test-extension.mjs 110 OK  0 FAIL
```
Porte, deux fois de suite, sur le commit poussé, `XDG_RUNTIME_DIR` isolé + `unset TMPDIR`, aucune autre
porte en cours (vérifié avec `pgrep -af '^bash tools/verify-all.sh$'` → « aucune porte concurrente ») :
```
$ export XDG_RUNTIME_DIR=$(mktemp -d /tmp/xdg-t4.XXXXXX); unset TMPDIR; bash tools/verify-all.sh
.NET       vert
compagnon  vert
hud        vert
console    vert
PORTE : VERTE
PORTE 1 EXIT=0

$ … même préparation …; bash tools/verify-all.sh
.NET       vert
compagnon  vert
hud        vert
console    vert
PORTE : VERTE
PORTE 2 EXIT=0
```
Rejouée deux fois **sur le commit poussé** (`4e9bd1d`, arbre de travail propre, même préparation
`XDG_RUNTIME_DIR` + `unset TMPDIR`, aucune porte concurrente) : sortie identique
(`.NET vert / compagnon vert / hud vert / console vert / PORTE : VERTE`), `EXIT=0` les deux fois.
Preuves ciblées (extraits des sorties ci-dessus) :
- repli `sts` : `OK thème cassé : repli sts, journal [ULTIMA] (une ligne), aucune exception — WARN [ULTIMA] thème : themes/casse.json : thème « casse » : JSON invalide : …`
- `auto-perso` : `OK auto-perso : thème du personnage de la run (ironclad)` et `OK auto-perso hors run (état sans run en cours) : repli sts journalisé`
- géométrie : `OK géométrie 675 combinaisons (ancrage, décalage, largeur, hauteur) : jamais hors écran — 0` et
  `OK géométrie 675 combinaisons : jamais sur « Fin du tour » — 0`
- polices : `OK police : appliquée au CSS de St (corps 20 px, en-tête de carte 22 px) — font-size: 22px; font-family: "Fira Sans";`
- modules perso : `OK modules perso dessinés à la place de modules[] : en-tête + première ligne — Or / étage 2 | 134 or · étage 5 | … +1`
- repos : `OK changement de thème : AUCUNE minuterie, AUCUN travail par image au repos (10 min) — sources=0`
- couleurs : `OK aucun hexadécimal dans les styles St (les couleurs viennent du thème, en rgba)`

## NON prouvé

- **Rien n'a tourné dans une vraie session GNOME Shell 50 ni sur le vrai jeu** : rendu St/Clutter réel,
  compilation des shaders GLSL, hauteur de ligne réelle, comportement du survol XWayland, FPS.
- `install-dev.sh --check` n'a pas pu tourner ici : la machine n'a pas la commande `gnome-extensions`
  (`which gnome-extensions` → absent), donc l'empaquetage réel de `themes/sts.json` n'est pas vérifié.
- Le **verre change d'aspect** : la teinte vient maintenant du fichier de thème, donc `sts` est appliqué à
  `glass.tintOpacity` = **0,55** (v1.3 codé en dur : 0,72) et le liseré du panneau à `border` = `#EFC85166`
  (v1.3 : or @ 0,18). C'est la lecture littérale de `docs/44 §4`, mais **ce n'est pas validé sur capture**.
- `Gio.File.enumerate_children` sur un dossier de thèmes absent (lève puis est attrapé) : comportement
  attendu de Gio, non observé en session.
- Aucun test de contraste (rôle de `ultima theme check`, côté T2/TUI) : un thème peut être peu lisible
  sans que le HUD le signale.

## Reste

- **Fenêtre de test n°4** (`overlay/gnome/TEST-WINDOW.md`) avec Mio : gestes 1-20 sur le bureau, 21-27 en
  jeu, plus les mesures FPS (§4) et la vérification du rendu du verre (0,55 vs 0,72).
- Les **10 thèmes de T2** ne sont pas dans cette branche : le HUD les chargera depuis
  `~/.local/share/ultima/themes/` ou `~/.config/ultima/themes/` sans changement de code (testé avec des
  thèmes de fixture dans un dossier temporaire, jamais dans `themes/`).
- `status/verrier-design.md` cite encore `palette.js` (« aucune couleur hex (seul palette.js en définit) ») :
  fichier hors de mon périmètre, à réaligner par le chef/T3.
- `PERF.md` §4 et `docs/perf/MESURE.md` : les nouvelles conditions (thème/police) restent à mesurer.

## Demandes de contrat

1. **Ton `shadow`** dans `schemas/theme.schema.json` (+ `Tones` ?) : l'ombre du panneau n'a pas de ton
   dédié ; j'utilise `background` @ 0,45. Un thème clair donnera une ombre claire.
2. **Priorité `appearance.hud.position.side` vs `HudSettings.Side`** : `docs/44` ne tranche pas. J'ai fait
   primer `position.side` (v1.4) et gardé `HudSettings.Side` (fichier/état/GSettings) en repli. À confirmer.
3. **`glass.tintOpacity`** : est-ce l'opacité du verre **ouvert** (ma lecture, 0,55 pour `sts`) ou une base
   à moduler par la surface (v1.3 : 0,72) ? Si Mio veut l'ancien rendu, `themes/sts.json` doit passer à
   0,72 (fichier du chef).
4. **`ModuleSetting.Screens` sur les modules NATIFS** : le HUD l'applique (en plus des règles par écran) ;
   le daemon filtre-t-il déjà ? À documenter pour éviter deux vérités.
5. **`CustomModuleView.Screens` vs `ModuleSetting.Screens`** (module perso) : quand les deux existent, j'ai
   donné la priorité au premier (ce que le daemon publie). À écrire dans `docs/44` §3.
6. **Bornes du contrat** : `position.width` 280-520 et `font.size` 12-24 sont des bornes dures côté schéma ;
   côté HUD j'ai choisi de **ramener** (pas de rejeter) une valeur utilisateur hors bornes, comme les
   autres bornes v1.3. Confirmer que c'est la règle voulue pour un réglage écrit à la main.
