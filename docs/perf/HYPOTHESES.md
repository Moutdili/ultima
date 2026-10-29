# Pourquoi ULTIMA pourrait faire baisser les FPS : suspects classés et recommandations au Verrier

Auteur : le Chronométreur, 2026-10-29. **Ce document contient des hypothèses, pas des résultats.**
Chaque suspect est accompagné de la mesure qui le confirme ou l'écarte (protocole :
`docs/perf/MESURE.md`). Le Verrier implémente, le Chronométreur mesure.

Sources lues :
- code ULTIMA : `overlay/gnome/ultima@mio/extension.js` et `geometry.js` (main `257d71c`) ;
- GNOME Shell 50.6 installé (`/usr/lib64/gnome-shell/libshell-18.so`, ressources JS extraites avec
  `gresource extract`) ;
- sources amont de la branche `gnome-50` : `gnome-shell/src/shell-blur-effect.c`,
  `mutter/src/compositor/meta-compositor-view-native.c`, `mutter/src/wayland/meta-wayland-surface.c` ;
- état réel de la machine, en lecture seule (`MESURE.md` § 1).

## 0. Ce que fait l'extension, image par image

Géométrie (écran logique 1536×960, échelle 1,25) : rail **14×346** logique (≈ 18×433 px réels),
panneau **260×346** logique (≈ 325×433 px réels), soit 0,33 % et 6,1 % de l'écran.

| Élément du code | Effet par image (jeu à 60 FPS) |
|---|---|
| `global.compositor.disable_unredirect()` dès que le rail est visible (`_updateShown`) | Mutter ne peut plus afficher le tampon du jeu en scanout direct : il **compose tout l'écran à chaque image du jeu**, pendant toute la partie |
| `Shell.BlurEffect({mode: BACKGROUND, radius: 24})` sur `ultima-glass`, **aussi en mode rail** | à chaque image où la zone est repeinte : copie du fond, deux passes de flou, composition |
| Cadre St : `background-color`, `border`, `border-radius`, `box-shadow` | peint à chaque repeinte de la zone ; l'ombre est **recalculée** pendant les 180 ms d'expansion (largeur animée) |
| `GLib.timeout_add(PRIORITY_DEFAULT, 80)` → `global.get_pointer()` | 12,5 réveils/s du fil principal de gnome-shell tant que le rail est visible |
| `_render()` à chaque changement de `overlay-state.json` | détruit puis recrée tous les `St.Label` (mise en page Pango, ramasse-miettes GJS) |

## 1. Suspects classés

Classement par **(probabilité d'être LA cause ressentie à la pioche) × (coût plausible)**. Un
suspect ne passe « confirmé » ou « écarté » qu'après mesure.

### n°1 — Composition plein écran forcée par `disable_unredirect` (probabilité : moyenne à forte, SI le scanout direct existe sans ULTIMA)

**Mécanisme.** Mutter 50 cherche un candidat au scanout direct à chaque image
(`find_candidate()` dans `meta-compositor-view-native.c`). Le premier refus possible est
`if (meta_compositor_is_unredirect_inhibited (compositor))` → « No … candidate: unredirect
inhibited ». ULTIMA déclenche ce refus **en permanence** tant que le rail est visible. GNOME Shell
fait la même chose pour l'OSD de volume (`osdWindow.js`, lignes 95 et 130), mais seulement pendant
quelques secondes.

**Pourquoi c'est cher ici, et justement à la pioche.**
- Le jeu rend en **3072×1920** (XWayland à l'échelle 2 sous une échelle d'écran de 1,25 ;
  `godot.log` : « Screen info … (3072, 1920) »). Composer = relire ce tampon (≈ 23,6 Mo en XRGB8888)
  et écrire l'image de l'écran (1920×1200 XR30, ≈ 9,2 Mo) **à chaque image**, soit ≈ 33 Mo/image,
  ≈ 2 Go/s à 60 Hz, sur une mémoire partagée avec le CPU et avec le jeu. Ordre de grandeur estimé :
  **0,2 à 0,6 ms de GPU par image** (hypothèse, à mesurer).
- La moyenne bouge à peine, mais **la queue bouge** : les animations de pioche sont les images les
  plus lourdes du jeu. Si le jeu frôle déjà 16,7 ms à ce moment-là, le travail de composition
  (même faible) fait rater la synchro verticale → image à 33 ms → à-coup ressenti. C'est
  exactement le symptôme décrit (baisse à la pioche, pas en continu).
- La composition ajoute aussi une étape de planification (le frame clock de Mutter) entre la
  présentation du jeu et l'écran : un retard du fil principal de gnome-shell (JS, GC) se voit alors
  à l'écran, alors qu'en scanout direct il ne se voyait pas.

**Ce qui peut faire tomber ce suspect.** Si Mutter **compose de toute façon** le jeu sans ULTIMA,
`disable_unredirect` ne coûte rien. Raisons possibles, non vérifiées : tampon 3072×1920 ≠ mode
1920×1200 (Mutter passe un `src_rect`/`crtc_dst_rect` au plan KMS ; la mise à l'échelle par le plan
doit être acceptée par le pilote amdgpu), format/espace colorimétrique (« surface color state needs
mapping to the output's ») — l'écran est en **XR30** (10 bits) aujourd'hui.

**Mesure décisive.** `tools/perf/scanout-probe.py --watch 20`, extension désactivée puis active,
en combat (MESURE.md § 7.2) ; puis 1 % low `sans-ultima` vs `masque` vs `rail-seul` vs
`sans-unredirect`, et la colonne GPU %.

### n°2 — `Shell.BlurEffect` en mode BACKGROUND recalculé à chaque image (probabilité : faible à moyenne)

**Mécanisme, prouvé par le code amont** (`shell-blur-effect.c`, branche gnome-50) :
```c
case SHELL_BLUR_MODE_BACKGROUND:
  return TRUE;          /* needs_repaint() : jamais de cache en mode BACKGROUND */
```
À chaque peinture, `paint_background()` copie la zone sous l'acteur depuis le framebuffer en cours
(`clutter_blit_node_new (src)`), puis applique le flou en deux passes sur des tampons hors écran,
puis compose le résultat. Le jeu repeignant tout l'écran à chaque image, **ULTIMA refait ce travail
60 fois par seconde**, y compris en mode rail.

**Coût estimé.** `calculate_downscale_factor()` divise la taille par 2 tant que le rayon dépasse
`MAX_RADIUS` (12) et que la largeur et la hauteur restent au-dessus de 256 px :
- panneau ≈ 325×433 px réels → réduit à ≈ 162×216, rayon 12 : quelques centaines de milliers
  d'accès texture par image ;
- rail ≈ 18×433 px → **pas de réduction** (largeur < 256), rayon 24 sur une bande étroite.
C'est probablement **< 0,1 ms de GPU par image**, mais c'est 4 passes de rendu supplémentaires
(copie, flou vertical, flou horizontal, composition) dans chaque image du compositeur, et une
lecture du framebuffer qui impose que tout ce qui est dessous soit déjà rendu. À noter : GNOME
Shell 50 lui-même n'utilise `Shell.BlurEffect` qu'en mode ACTOR sur un fond statique (écran de
verrouillage, `unlockDialog.js` l. 712), jamais en BACKGROUND au-dessus d'un contenu animé.

**Mesure.** `panneau-ouvert` vs `sans-flou` (même géométrie), et `rail-seul` vs `masque`.

### n°3 — Travail sur le fil principal de gnome-shell au mauvais moment (probabilité : faible aujourd'hui, à surveiller avec le daemon)

- `_render()` recrée tous les labels à chaque écriture de `overlay-state.json`. Quelques ms de JS +
  Pango + GC sur le fil qui prépare aussi chaque image composée : si ça tombe pendant une pioche,
  le compositeur rate une image. **Aujourd'hui aucun daemon ne publie** (aucun processus trouvé
  le 2026-10-29) : ce suspect ne peut pas expliquer ce que Mio a ressenti, mais il deviendra réel
  si le daemon publie en combat.
- L'expansion anime `width` (relayout et ombre recalculés à chaque image pendant 180 ms).

**Mesure.** `cpu-budget.sh` (CPU et réveils du fil `gnome-shell`) pendant les segments ; plus tard,
segment « daemon actif en combat » vs « daemon arrêté ».

### n°4 — Sondage du pointeur à 12,5 Hz (probabilité : très faible pour les FPS)

`global.get_pointer()` + quelques comparaisons : de l'ordre de la microseconde. Pendant le jeu, le
fil principal se réveille déjà à chaque image (mesuré : ≈ 230-260 réveils/s pour le fil
`gnome-shell`, jeu en arrière-plan à 30 FPS, sur batterie). Les 12,5 réveils/s d'ULTIMA s'y
fondent. Ils coûtent surtout **au repos** (batterie). Mesure : MESURE.md § 7.5.

### n°5 — Daemon et miroir mémoire (probabilité : nulle pour l'épisode ressenti, budget à tenir)

Aucun processus `Ultima.Daemon` / `ultima-mirror` ne tournait (relevé `cpu-budget.sh`). La règle
de la skill `sts2-daemon` : « Aucune lecture mémoire pendant un combat (v1) » ; « `nice 10` + classe
E/S idle ; 0 % CPU au repos (aucun timer actif hors transitions) ». À vérifier avec `cpu-budget.sh`
dès que le daemon existe.

### Hors ULTIMA (à séparer grâce à la référence `sans-ultima`)
1. **Le jeu rend en 3072×1920** sur un iGPU : il est probablement lui-même proche de la limite
   pendant les animations. Si `sans-ultima` montre déjà les mêmes chutes, ULTIMA n'est pas en cause.
2. **Bridage à 30 FPS hors focus** (« Limiting background FPS to 30 ») : un alt-tab, une
   notification qui prend le focus ou une fenêtre de test suffisent à diviser les FPS par deux.
   Ce n'est pas lié à la pioche, mais un ressenti peut mélanger les deux.
3. **Batterie** : relevé fait en `balanced-battery` ; les fréquences GPU y sont plus basses.

## 2. Recommandations chiffrables pour le Verrier

Toutes sont des **réglages A/B** (GSettings, lus à chaud via `changed::<clé>`), pour que la mesure
décide du réglage par défaut. Les noms de clés sont des propositions, à confirmer par le Verrier ;
`MESURE.md` § 4.2 les utilise.

| # | Changement | Clé proposée | Gain attendu (hypothèse) | Critère de succès mesuré |
|---|---|---|---|---|
| R1 | Flou **seulement panneau ouvert** : rail = aplat (`effect.enabled = false`, ou effet retiré, quand replié) | `blur` = `'panel'` (défaut) / `'always'` / `'off'` | supprime 4 passes de rendu par image en mode rail | `rail-seul` ≤ `masque` + 1 % de 1 % low ; GPU % identique à ± 1 point |
| R2 | `disable_unredirect` **réglable** | `disable-unredirect` (booléen, défaut `true`) | si le scanout direct existe : supprime la composition plein écran | dépend de `scanout-probe` ; `sans-unredirect` vs `rail-seul` |
| R3 | Si R2 montre un coût : **masquer automatiquement le rail en combat** (état `Screen=Combat` publié par le daemon) et rendre l'unredirect pendant le combat ; réafficher au survol/raccourci | `hide-in-combat` (booléen) | coût nul pendant les pioches, seul moment où il compte | 1 % low en combat = `sans-ultima` à ± bruit |
| R4 | Survol **piloté par événement** au lieu du minuteur : `global.backend.get_cursor_tracker()` + signal `'position-invalidated'`, traitement regroupé avant la prochaine image (`global.compositor.get_laters()`), comme `magnifier.js` (l. 138, 273, 309) | aucune (remplacement) | 0 réveil/s quand le pointeur ne bouge pas (contre 12,5/s) | `cpu-budget.sh` au repos : réveils/s du fil `gnome-shell` rail visible = masqué à ± 2/s |
| R5 | `_render()` : ne rien faire si masqué ou si le contenu est identique (hachage) ; mettre à jour le texte des `St.Label` existants au lieu de tout recréer | aucune | supprime les pics de quelques ms sur le fil du compositeur | aucun à-coup corrélé aux écritures de l'état (horodatages du daemon vs CSV) |
| R6 | Expansion : animer `translation_x` d'un acteur de taille fixe (et l'opacité) au lieu de `x`+`width` ; pas de `box-shadow` animée | aucune | supprime relayout et recalcul de l'ombre pendant 180 ms | pas d'image > 25 ms pendant l'expansion (segment dédié, facultatif) |
| R7 | Option « flou figé » : capture du fond à l'ouverture du panneau, puis aplat texturé (pas de recalcul) | `blur` = `'static'` | flou à coût quasi nul panneau ouvert | `panneau-ouvert` (static) ≈ `sans-flou` |

Ordre conseillé : R1 et R2 tout de suite (ils rendent possibles les conditions `sans-flou` et
`sans-unredirect` du protocole), R4 et R5 ensuite (gains sûrs, faible risque), R3, R6 et R7
seulement si la mesure les justifie.

## 3. Budgets proposés (à valider par Mio)

| Grandeur | Budget | Outil |
|---|---|---|
| Perte de 1 % low en combat, ULTIMA visible vs `sans-ultima` | **≤ 3 %** (skill `sts2-performance`) | `analyze-frametimes.py` |
| Perte de FPS moyen | ≤ 1 % | idem |
| Écart de p99 du temps par image | ≤ + 1 ms | idem |
| GPU % global, ULTIMA visible vs masqué, même scène | ≤ + 2 points | colonne `gpu_load` ; `cpu-budget.sh --gpu` |
| CPU gnome-shell ajouté au repos, rail visible | ≤ 0,5 % d'un cœur et ≤ 13 réveils/s (≤ 1/s après R4) | `cpu-budget.sh` |
| Daemon au repos | ≤ 0,1 % d'un cœur, ≤ 1 réveil/s | `cpu-budget.sh` |

## 4. Ce qui n'est PAS établi ici
- Aucun chiffre de FPS réel : tous les coûts ci-dessus sont des **estimations** à partir du code et
  d'ordres de grandeur, pas des mesures.
- Mutter 50 sait-il afficher en scanout direct un tampon XWayland 3072×1920 sur un mode 1920×1200
  (mise à l'échelle par le plan amdgpu, écran en XR30) ? Je ne l'ai pas lu dans
  `meta_wayland_buffer_try_acquire_scanout()` ni dans le pilote. `scanout-probe.py` tranchera.
- Je n'ai pas vérifié si le rayon du flou est multiplié par l'échelle 1,25 (rayon réel 24 ou 30 px) :
  ça ne change pas le classement.
- La touche `Control_L+Shift_L+L` n'a pas été essayée dans le jeu (étape 7.0 du protocole).
