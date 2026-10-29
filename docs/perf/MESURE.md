# Mesurer l'impact d'ULTIMA sur les FPS de Slay the Spire 2

Auteur : le Chronométreur (v2/chronometreur), 2026-10-29. Skill de référence : `sts2-performance`.

> Symptôme rapporté par Mio : « baisses de FPS quand les cartes sont piochées », extension ULTIMA
> active. **Rien n'est mesuré à ce jour.** Ce document dit comment mesurer, avec quels outils
> déjà présents sur la machine et dans quel ordre. Toutes les commandes qui modifient quelque chose
> (option de lancement Steam, dossier de sortie, bascules de l'extension) sont **à valider et à
> exécuter par Mio ou par le chef pendant la fenêtre de test**. Le Chronométreur n'a rien installé,
> rien lancé, rien modifié.

## 1. Faits vérifiés sur la machine (lecture seule, 2026-10-29 13:20-13:45)

| Fait | Preuve (commande en lecture) |
|---|---|
| Steam Flatpak `com.valvesoftware.Steam` 1.0.0.87, runtime `org.freedesktop.Platform/x86_64/26.08` | `flatpak info com.valvesoftware.Steam` |
| **MangoHud déjà installé** pour ce runtime : `org.freedesktop.Platform.VulkanLayer.MangoHud//26.08`, version **0.8.4** (aussi 25.08). Flathub ne propose rien de plus récent pour 26.08 | `flatpak list` · `flatpak remote-ls flathub` (21.08→0.6.8 … 26.08→0.8.4) |
| Le jeu (natif Linux, appid 2868840, v0.107.1) tourne dans **Steam Linux Runtime 2.0 (soldier)** via pressure-vessel, à l'intérieur du bac à sable Flatpak | ascendance du PID du jeu : `pv-adverb` → `bwrap` ; env `PRESSURE_VESSEL_RUNTIME=soldier_platform_2.0…` |
| La couche MangoHud est **visible dans le conteneur du jeu** : `implicit_layer.d/05-x86_64-linux-gnu.json` = `VK_LAYER_MANGOHUD_overlay_x86_64`, activée par `MANGOHUD=1` | lecture de `/proc/<pid du jeu>/root/usr/lib/pressure-vessel/overrides/share/vulkan/implicit_layer.d/` |
| Rendu : **Vulkan 1.4, Forward+, RADV PHOENIX** (Radeon 780M), `VSync: Adaptive` (FIFO relaxed) | `godot.log` (« Vulkan 1.4.354 - Forward+ - Using Device #0: … RADV PHOENIX », « VSync: Adaptive ») ; `libvulkan_radeon.so` dans `/proc/<pid>/maps` |
| Le jeu voit un écran de **3072×1920** (XWayland à l'échelle 2, `Xft.dpi: 192`) : en plein écran il rend 2,56× plus de pixels que la dalle (1920×1200), Mutter réduit ensuite l'image | `godot.log` « Screen info … Size: (3072, 1920) » ; `xrdb -query` |
| Dalle interne eDP-1 1920×1200 à **60,003 Hz**, échelle 1,25 | `org.gnome.Mutter.DisplayConfig.GetCurrentState` |
| **Le jeu se bride à 30 FPS dès qu'il perd le focus** (« Limiting background FPS to 30 », 12 fois dans le log du jour) | `godot.log` |
| Aucune option de lancement Steam définie aujourd'hui pour le jeu | `userdata/*/config/localconfig.vdf`, bloc 2868840 : pas de `LaunchOptions` |
| Dans le bac à sable, `/home/mio` = `~/.var/app/com.valvesoftware.Steam` de l'hôte, **et** ce dossier est aussi monté à son vrai chemin | `/proc/<pid du jeu>/mountinfo` |
| La couche `VK_LAYER_MESA_overlay` (repli) est aussi visible dans le conteneur (`explicit_layer.d/1.json` → GL.default 26.08, Mesa 26.2.5) | idem, `explicit_layer.d/` |
| Au moment du relevé : machine **sur batterie**, tuned `balanced-battery`, image **composée** par Mutter (plan principal = tampon 1920×1200 XR30) | `/sys/class/power_supply/AC/online` = 0 · `tools/perf/scanout-probe.py` |

## 2. Outil principal : MangoHud (journal CSV de temps par image)

### 2.1 Option de lancement Steam (exacte)
Steam → Slay the Spire 2 → Propriétés → Général → Options de lancement :
```
MANGOHUD=1 MANGOHUD_CONFIG=no_display,log_interval=0,toggle_logging=Control_L+Shift_L+L,output_folder=/home/mio/.var/app/com.valvesoftware.Steam/ultima-perf %command%
```
(`tools/perf/session.sh launch-option` imprime la même ligne.)

- `MANGOHUD=1` : active la couche implicite déjà présente dans le conteneur (pas d'installation).
- `no_display` : pas de HUD (le HUD consomme lui-même du GPU et fausserait la mesure).
- `log_interval=0` : **une ligne par image présentée** (MangoHud 0.8.4 : `try_log()` à chaque
  présentation ; colonnes `fps,frametime,cpu_load,cpu_power,gpu_load,…,process_rss,cpu_mhz,elapsed` ;
  `frametime` en ms, `elapsed` en ns depuis le début du journal ; source `src/logging.cpp`,
  `src/overlay.cpp` v0.8.4).
- `toggle_logging=Control_L+Shift_L+L` : **Ctrl gauche + Maj gauche + L** démarre / arrête le
  journal. Le défaut de MangoHud (`Shift_L+F2`) est inutilisable : la rangée F du portable de Mio
  envoie des touches média (skill `sts2-external-overlay`). À vérifier une fois au menu : la
  combinaison ne déclenche rien dans le jeu.
- `output_folder` : chemin **identique** dans le bac à sable et sur l'hôte (cf. § 1), hors du
  dossier du jeu et hors de ses données (l'audit no-write ne le surveille pas).
- Chaque démarrage/arrêt produit `SlayTheSpire2_AAAA-MM-JJ_HH-MM-SS.csv` (heure locale du
  démarrage) + `…_summary.csv` à l'arrêt.

### 2.2 Préparation à faire par Mio (une fois, avant la fenêtre)
```bash
mkdir -p ~/.var/app/com.valvesoftware.Steam/ultima-perf    # MangoHud ne crée pas le dossier
# puis coller l'option de lancement ci-dessus dans Steam
```
Retour arrière après la fenêtre : vider le champ « Options de lancement » (MangoHud n'est plus
chargé) ; `rm -r ~/.var/app/com.valvesoftware.Steam/ultima-perf` si on veut effacer les journaux.

> Remarque : MangoHud est une couche Vulkan chargée **dans le processus du jeu** par le chargeur
> Vulkan (mécanisme standard Flatpak/Steam), pas du code ULTIMA. Elle n'écrit que dans
> `ultima-perf/`. Son propre coût (écriture d'une ligne par image) est le même dans toutes les
> conditions, donc neutre pour la comparaison A/B. Elle n'est là que pendant la mesure.

## 3. Replis si MangoHud est impossible

| Méthode | Applicable ? | Pourquoi |
|---|---|---|
| `VK_LAYER_MESA_overlay` (Mesa 26.2.5, présente dans le conteneur) : `VK_INSTANCE_LAYERS=VK_LAYER_MESA_overlay VK_LAYER_MESA_OVERLAY_CONFIG=no_display,output_file=/home/mio/.var/app/com.valvesoftware.Steam/ultima-perf/mesa-overlay.txt,fps,frame_timing %command%` | **oui, repli n°1** | rien à installer. Mais elle écrit des **agrégats par période** (`fps_sampling_period`, 500 ms par défaut), pas une ligne par image : pas de 1 % low exact, seulement FPS moyen et temps d'image moyen/max par période. Pas de touche de démarrage : un fichier par lancement, découpage à l'horloge (marques du chef). `analyze-frametimes.py` ne lit pas ce format : comparaison manuelle des moyennes. |
| `GALLIUM_HUD` | **non** | réservé aux pilotes OpenGL Gallium (radeonsi). Le jeu rend en Vulkan/RADV. Lancer `launch_opengl.sh` changerait le moteur de rendu, donc la mesure n'aurait plus de sens. |
| `MESA_VK_TRACE=rgp` (traces RGP) | **non** pour l'A/B | capture des traces GPU image par image à analyser dans Radeon GPU Profiler : outil de diagnostic, lourd, pas un journal de temps par image. Utile seulement **après** un verdict, pour expliquer une image lente. |
| Statistiques de Mutter (Sysprof, marques du frame clock) | possible, **installation nécessaire** (`sysprof` absent) → décision de Mio | mesure côté compositeur (retards de composition), complémentaire de MangoHud qui mesure côté jeu. |
| `tools/perf/scanout-probe.py` + `tools/perf/cpu-budget.sh` | **oui, sans installation** | ne donnent pas de FPS, mais tranchent « composé ou scanout direct ? » et le coût CPU/réveils de gnome-shell, du daemon et du jeu. |

## 4. Scénario et conditions A/B

### 4.1 Scénario reproductible (un segment)
1. Le chef bascule la condition, puis `session.sh mark <condition>`. Il attend 2 s et dit « go ».
2. Mio, **jeu focalisé, sans alt-tab** : Ctrl+Maj+L (début du journal), puis il joue **5 tours**
   (fin de tour → tour ennemi → **pioche**), sans survoler le bord droit (sauf condition
   `panneau-ouvert`), puis Ctrl+Maj+L (fin du journal).
3. Même combat tant qu'il dure ; s'il se termine, on continue au combat suivant (le suivi de
   `godot.log` note la rencontre, `Creating NCombatRoom … encounter=…`).

Chaque condition est jouée **deux fois** (aller puis retour, ordre ABBA), soit **10 tours de pioche
par condition**. Cet ordre compense la dérive (chauffe du portable, combat qui change). La
référence `sans-ultima` ouvre et ferme la session : ses deux répétitions donnent le **bruit de
mesure**.

### 4.2 Conditions

| Condition | État ULTIMA | Bascule (chef, terminal ; sauf ⌨ = Mio) | Disponible |
|---|---|---|---|
| `sans-ultima` | extension désactivée (`enable_unredirect` rendu) | `gnome-extensions disable ultima@mio` | oui |
| `masque` | extension active, masquée (pas de sondage, unredirect rendu) | `gnome-extensions enable ultima@mio` puis ⌨ Super+Maj+O | oui |
| `rail-seul` | rail 14 px visible, verre flouté, `disable_unredirect` | ⌨ Super+Maj+O (réafficher), pointeur loin du bord droit | oui |
| `panneau-ouvert` | panneau 260 px épinglé, flouté | ⌨ Super+Maj+I (épingler) ; ⌨ Super+Maj+I à la fin | oui |
| `sans-flou` | rail + panneau sans `Shell.BlurEffect` (aplat) | `gsettings --schemadir ~/.local/share/gnome-shell/extensions/ultima@mio/schemas set org.gnome.shell.extensions.ultima blur 'off'` | **après le Verrier** (nom de clé à confirmer) |
| `sans-unredirect` | rail visible, **sans** `disable_unredirect` | `… set org.gnome.shell.extensions.ultima disable-unredirect false` | **après le Verrier** |
| `x11` | moteur X11 de repli (override-redirect, pas de flou) | `gnome-extensions disable ultima@mio && overlay/x11/ultima-x11 run &` ; fin : `overlay/x11/ultima-x11 quit` | oui (ouvre une fenêtre : accord de Mio) |

`sans-flou` et `sans-unredirect` se mesurent **panneau épinglé** (là où le flou coûte le plus) ;
pour `sans-unredirect`, si `scanout-probe.py` répond « SCANOUT DIRECT », l'overlay n'est plus
visible : la condition mesure alors exactement le coût de la composition forcée.

Ordre de la session (aller puis retour) :
`sans-ultima, masque, rail-seul, panneau-ouvert, sans-flou, sans-unredirect, x11,`
`x11, sans-unredirect, sans-flou, panneau-ouvert, rail-seul, masque, sans-ultima`.
Sans les réglages du Verrier : on saute `sans-flou` et `sans-unredirect` (10 segments).

### 4.3 Règles de validité
- **Sur secteur**, même profil d'énergie pour toute la session (l'en-tête de `info.txt` et de
  `cpu-budget.sh` le note) : sur batterie (`balanced-battery`), les fréquences GPU changent.
- **Aucun changement de focus** pendant un segment : le jeu se bride à 30 FPS hors focus.
  `session.sh godot-start` horodate `FocusOut`/`Limiting background FPS` ; l'analyse **exclut** tout
  segment touché, et détecte aussi un plateau à 30 FPS.
- Pas de capture d'écran pendant un segment (`capture-composed.py` et `capture-game-x11.sh`
  relisent l'image : à-coup garanti). Donc on n'utilise **pas** `tools/lab/test-window.sh` pour ce bloc.
- Attendre ≥ 2 s entre `mark` et Ctrl+Maj+L (l'heure du CSV est à la seconde).

## 5. Indicateurs, budget, verdict

Calculés par `tools/perf/analyze-frametimes.py` à partir du `frametime` de chaque image (et non de
la colonne `fps`, lissée sur 500 ms) :

| Indicateur | Définition |
|---|---|
| FPS moyen | 1000 × n / Σ frametime |
| **1 % low** | 1000 / moyenne des 1 % de frametimes les plus longs |
| 0,1 % low | idem sur 0,1 % |
| p50, **p99** | percentiles du temps par image (ms) |
| à-coups/min | images > 1,5 × médiane (à 60 Hz : ≥ 25 ms, une synchro verticale manquée) |
| pire seconde | FPS de la plus mauvaise tranche de 1 s |
| GPU % | moyenne de `gpu_load` (charge GPU **globale** : jeu + compositeur) |

Les pioches sont le moment le plus chargé de chaque tour : ce sont elles qui remplissent le pire
1 % et le p99 d'un segment de 5 tours. Les comparer revient donc à comparer les pioches.

**Budget (skill `sts2-performance`) : perte de 1 % low ≤ 3 % par rapport à `sans-ultima`.**
Verdict : `OK` (perte ≤ 3 %) · `DEPASSE` (perte > 3 % et > bruit) · `INDECIS` (perte > 3 % mais ≤
bruit : on refait des répétitions). Bruit = écart relatif max-min du 1 % low entre répétitions
d'une même condition. Une condition qui n'a qu'une répétition et dépasse le budget est marquée
« bruit inconnu ».

## 6. Analyse
```bash
tools/perf/session.sh collect            # copie les CSV de ~/.var/app/…/ultima-perf/ dans la session
tools/perf/session.sh analyze            # tableau + verdicts → ~/sts2-proofs/perf/current/analyse.md
tools/perf/session.sh analyze --json     # pour un traitement automatique
```
Banc d'essai du script : `bash tools/perf/test-analyze.sh` (journaux synthétiques de
`tools/perf/fixtures/session-demo/`, verdicts connus d'avance).

## 7. Étapes « perf » de la prochaine fenêtre de test (~30 min)

Qui fait quoi : **Mio** joue et appuie sur les touches marquées ⌨ ; **le chef** lance les commandes
depuis son shell, sans jamais prendre le focus ni faire de capture pendant un segment.

### 7.0 Avant (Mio, 3 min ; à valider)
1. Brancher le secteur.
2. `mkdir -p ~/.var/app/com.valvesoftware.Steam/ultima-perf`
3. Coller l'option de lancement (§ 2.1) dans Steam, lancer le jeu **plein écran**, continuer la run.
4. Au menu : ⌨ Ctrl+Maj+L, attendre 3 s, ⌨ Ctrl+Maj+L. Chef :
   `ls -l ~/.var/app/com.valvesoftware.Steam/ultima-perf/` → **un CSV doit exister**. Sinon :
   MangoHud ne s'est pas chargé ou la touche ne passe pas → repli § 3 (Mesa overlay), même protocole.

### 7.1 Ouverture (chef)
```bash
cd ~/sts2   # ou le worktree qui contient tools/perf
tools/perf/session.sh init                      # vérifier : alimentation = secteur, MangoHud 0.8.4
tools/perf/session.sh godot-start             # suivi détaché : focus / bridage 30 FPS / cartes jouées
tools/perf/scanout-probe.py                     # état KMS de départ
```

### 7.2 Question préalable « scanout direct sans ULTIMA ? » (2 min, en combat, jeu focalisé)
```bash
gnome-extensions disable ultima@mio
tools/perf/scanout-probe.py --watch 20          # Mio joue normalement pendant 20 s
gnome-extensions enable ultima@mio              # rail visible -> disable_unredirect
tools/perf/scanout-probe.py --watch 20
```
Attendu, écrit avant : extension désactivée → « SCANOUT DIRECT probable (tampon 3072x1920…) » **ou**
« COMPOSÉ » ; extension active → « COMPOSÉ ». Si les deux sont « COMPOSÉ », `disable_unredirect`
ne coûte rien sur cette machine (Mutter compose de toute façon) et le suspect n°1 tombe.

### 7.3 Segments (≈ 1 min chacun, 14 segments)
Pour chaque condition, dans l'ordre du § 4.2 :
```bash
<bascule de la condition (tableau § 4.2)>
tools/perf/session.sh mark <condition>
tools/perf/cpu-budget.sh --duration 30 --gpu --label <condition> --tsv ~/sts2-proofs/perf/current/cpu.tsv &
```
puis le chef dit « go » → ⌨ Ctrl+Maj+L → 5 tours → ⌨ Ctrl+Maj+L → Mio dit « fini ».

### 7.4 Clôture (chef)
```bash
tools/perf/session.sh godot-stop                # arrête le suivi de godot.log
gnome-extensions enable ultima@mio               # état normal ; Super+Maj+O si masqué
tools/perf/session.sh collect
tools/perf/session.sh analyze
column -t -s$'\t' ~/sts2-proofs/perf/current/cpu.tsv
```
Résultat à reporter dans `STATUS.md` : tableau d'`analyse.md`, verdicts, sortie de
`scanout-probe`, tableau CPU. Mio retire ensuite l'option de lancement (§ 2.2).

### 7.5 Mesure CPU au repos (bureau, sans jeu ; 3 min ; aucune fenêtre ouverte)
```bash
tools/perf/cpu-budget.sh --duration 60 --label repos-rail-visible --tsv ~/sts2-proofs/perf/cpu-repos.tsv
# ⌨ Super+Maj+O (masquer)
tools/perf/cpu-budget.sh --duration 60 --label repos-masque --tsv ~/sts2-proofs/perf/cpu-repos.tsv
```
Attendu : réveils/s du fil principal de gnome-shell plus élevés d'environ 12,5/s rail visible
(sondage du pointeur à 80 ms) ; CPU du daemon ≈ 0 % et ~0 réveil/s hors transitions.
