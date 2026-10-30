# Fenêtre de test n°4 : HUD thèmes v1.4 (`ultima@mio` 0.4.0)

Durée estimée : 35 min, déconnexion/reconnexion comprise, plus les mesures FPS du Chronométreur.
**Ne rien lancer hors de cette fenêtre.** Le jeu est lancé par Mio. L'extension ne lit ni n'écrit
rien du jeu : elle lit `$XDG_RUNTIME_DIR/ultima/state.json` (daemon) et `~/.config/ultima/settings.json`
(réglages, jamais écrits par l'extension), plus les fichiers de thème qu'elle **lit** seulement
(`~/.config/ultima/themes/`, `~/.local/share/ultima/themes/`, et ceux livrés dans son propre dossier).

Ce qui change depuis la fenêtre n°3 (verre v3) — **le verre, l'ouverture, les raccourcis et le
click-through ne changent pas** :

- **Thèmes** (contrat v1.4, `docs/44`) : `appearance.hud.theme` → `extends` → `appearance.hud.tones`.
  Teinte du verre depuis `glass`, police du thème, `auto-perso` (thème du personnage de la run).
  Un thème cassé retombe sur `sts` avec une ligne `[ULTIMA] thème : …` dans le journal — jamais une
  exception dans GNOME Shell. **Aucune couleur n'est codée en dur dans l'extension** : tout vient des
  fichiers `themes/*.json` (celui livré avec l'extension est `ultima@mio/themes/sts.json`).
- **Polices** : `appearance.hud.font.family` et `.size` (12-24 px, bornée), appliquées au CSS de St ;
  repli sur la police du thème, puis sur la police système.
- **Disposition** : `appearance.hud.position` = `side` (left/right), `anchor` (top/center/bottom),
  `offsetX`/`offsetY` (bornés à l'écran), `width` 280-520 px ; `railEdge` (liseré or du rail).
  La hauteur reste `heightFraction` (0,2-0,85) et l'opacité au repos `restOpacity` (0,35-1).
  Le panneau est toujours placé dans une **zone sûre** : jamais sur « Fin du tour » (bas droite),
  ni sur la pioche (bas gauche), ni sur la barre du haut.
- **Modules perso et plugins** : `EngineState.Custom` est dessiné à la place de `modules[]`
  (libellé, tons, note S-F, `screens`, santé ≠ ok atténuée + le mot, texte tronqué avec « … »).
  L'extension **n'exécute aucun code tiers** : elle ne lit que des chaînes de l'état publié.

Raccourcis : **Super+Maj+I** épingler (détail), **Super+Maj+O** masquer. Jamais de touche F.

## 0. Avant (sans effet sur la session)

```bash
cd ~/v2-verrier-design                   # ou le dépôt après merge
overlay/gnome/install-dev.sh --check     # metadata, schéma strict, pack, 8 suites de tests ; n'installe rien
cp -a ~/.config/ultima/settings.json /tmp/ultima-settings.bak 2>/dev/null || echo "pas de settings.json"
H=overlay/gnome/tools/hud-set.py         # règle les blocs « hud » (v1.3) et « appearance.hud » (v1.4)
S="gsettings --schemadir $HOME/.local/share/gnome-shell/extensions/ultima@mio/schemas"
K=org.gnome.shell.extensions.ultima
# Thèmes : les fichiers livrés dans themes/ s'installent dans le dossier de données d'ULTIMA.
mkdir -p ~/.local/share/ultima/themes
cp themes/*.json ~/.local/share/ultima/themes/            # sur main : sts.json seul (les 10 autres avec T2)
$H --reset-appearance                                     # repart des défauts du contrat
```
Attendu : `schéma OK`, `pack OK` (le paquet contient `themes/sts.json`), huit fois « tous les tests
passent », `shell local : GNOME Shell 50.6`.

## 1. Installer, puis se reconnecter

```bash
overlay/gnome/install-dev.sh             # copie + schéma compilé, NE recharge PAS l'extension
```
Se déconnecter / se reconnecter (le cache des modules JS rend `disable/enable` peu sûr), puis :
```bash
gnome-extensions enable ultima@mio       # si elle n'est pas déjà active
journalctl --user -b -o cat /usr/bin/gnome-shell | grep -E '\[ULTIMA\]|JS ERROR' | tail -n 30
```
Attendu :
- `[ULTIMA] actif : couche=addTopChrome, API flou=1 glsl=1 clone=1, …`
- `[ULTIMA] thèmes : N disponibles (sts …)` — N ≥ 1 ; un thème invalide donne
  `[ULTIMA] thème : <fichier> : …` (et n'empêche rien)
- `[ULTIMA] apparence (démarrage) : thème=sts police=14px (système) côté=right ancrage=center largeur=360 offset=(0,0) railEdge=oui tons=0`
- `[ULTIMA] géométrie : moniteur=1536x960 côté=right ancrage=center … panneau=(1172,…,360x…) police=14px …`
- aucune ligne `JS ERROR` ; si `API … =0` : noter laquelle (le niveau descend tout seul).

## 2. Sur le bureau, sans le jeu (12 min)

| # | Geste | Attendu |
|---|---|---|
| 1 | Regarder le bord droit | Bande de verre de 14 px collée au bord, liseré or, **aucune pastille ni texte** |
| 2 | Pointeur sur la bande, **ne plus bouger** | Ouverture ~0,2 s : le verre glisse, puis les lignes en fondu. Sans daemon : `En attente du daemon` |
| 3 | Sortir le pointeur, attendre 2,5 s | Contenu effacé, verre rentré plus vite, flou conservé jusqu'au bout du glissement |
| 4 | *(daemon arrêté)* `mkdir -p $XDG_RUNTIME_DIR/ultima && cp docs/22-state.sample.json $XDG_RUNTIME_DIR/ultima/s.tmp && mv $XDG_RUNTIME_DIR/ultima/s.tmp $XDG_RUNTIME_DIR/ultima/state.json`, puis survol | Une seule carte : grande lettre **A** verte, `Scourge · 24 or soldé`, dessous `Méta (médiane 3 sources) +65` |
| 5 | Pointeur sur la carte | Détail : les 5 options du shop, termes, puis `Deck 11`, `Enchantées 1`, `Malédiction 1`, `Reliques 1`, `Potions 1`, `Note du deck C` |
| 6 | Super+Maj+I, puis Super+Maj+I | Épinglé ouvert avec détail ; libéré : repli |
| 7 | Super+Maj+O, puis Super+Maj+O | Tout disparaît (`plan : visible=0 … survol=non`), puis revient |
| 8 | `$H theme=rubis` (ou tout thème de `themes/`) puis survol | Journal `apparence (settings.json) : thème=rubis (sts → rubis) …`. Les couleurs changent (accent, texte, liseré), **le verre garde la teinte du thème** ; aucun hex dans le code (seulement dans les fichiers de thème) |
| 9 | `$H theme=sts` puis `$H theme=auto-perso` (hors run) | Retour aux couleurs du jeu ; `auto-perso` hors run : repli `sts` + `[ULTIMA] thème : auto-perso hors run : repli « sts »` |
| 10 | Écrire un thème cassé : `printf '{"id":"casse","tones":' > ~/.config/ultima/themes/casse.json` puis `$H theme=casse` | Repli immédiat sur `sts`, **aucune exception**, journal `[ULTIMA] thème : thème « casse » : JSON invalide…` ; l'extension continue |
| 11 | `$H theme=sts fontSize=20` puis `$H fontSize=30` | Texte plus gros (20 px) ; 30 est **ramené à 24 px** (journal `police=24px`) |
| 12 | `$H fontFamily=Cantarell` puis `$H --reset-appearance` | Police changée ; retour à la police du thème puis système |
| 13 | `$H width=520` puis `width=280` | Panneau plus large, puis plus étroit (280-520 px) ; le texte se replie correctement, jamais de débordement |
| 14 | `$H anchor=top` puis `anchor=bottom` puis `anchor=center` | Le panneau se place en haut / en bas / au centre de la zone sûre ; **jamais sur « Fin du tour »** |
| 15 | `$H offsetY=-40 offsetX=60` puis `$H --reset-appearance` | Le panneau se décale (borné à l'écran) ; le rail reste collé au bord ; retour aux défauts |
| 16 | `$H positionSide=left` (puis `--reset-appearance`) | Le HUD passe au bord gauche (mêmes gestes), sans couvrir la pioche |
| 17 | `$H railEdge=false` puis `railEdge=true` | Liseré or du rail éteint (bande de verre seule), puis rallumé |
| 18 | `$H quality=eco` / `verre` / `loupe` (comme fenêtre n°3) | eco : aplat sans flou ; verre : flou + reflet ; loupe : jeu grossi ×1,06 réfracté |
| 19 | `$H restOpacity=0.4`, puis `0.9` ; `$H heightFraction=0.7`, puis `--reset` | Bande plus transparente puis opaque ; panneau plus haut ; retour aux défauts |
| 20 | Modules perso sans daemon : `cp docs/22-state.sample.json /tmp/s.json && python3 - <<'PY'` … (voir ci-dessous) puis survol | En-tête `Or / étage 2`, première ligne `134 or · étage 5`, `… +1` ; épinglé (Super+Maj+I) : les deux lignes, la note `A` en vert devant `Shop rentable` |

```bash
# Geste 20 : injecter deux modules perso dans un état publié (le daemon fera pareil en v1.4).
python3 - <<'PY'
import json, os, pathlib
src = json.load(open('docs/22-state.sample.json'))
src['custom'] = [
  {"id": "x.or-par-etage", "label": "Or / étage", "source": "module", "screens": None, "health": "ok",
   "problem": None, "rows": [{"text": "134 or · étage 5", "tone": "accent", "grade": None},
                              {"text": "Shop rentable", "tone": "ok", "grade": "A"}]},
  {"id": "x.plugin-casse", "label": "Plugin", "source": "plugin", "screens": ["combat"],
   "health": "error", "problem": "délai dépassé",
   "rows": [{"text": "ne doit pas apparaître en carte", "tone": "warn", "grade": None}]},
]
d = pathlib.Path(os.environ['XDG_RUNTIME_DIR']) / 'ultima'
d.mkdir(parents=True, exist_ok=True)
(d / 'state.tmp').write_text(json.dumps(src, ensure_ascii=False))
os.replace(d / 'state.tmp', d / 'state.json')
PY
```
Attendu : le module `x.or-par-etage` apparaît (ligne + première ligne), `x.plugin-casse` **non**
(écran `combat` seulement) ; au combat, l'inverse, avec `Plugin 1 · erreur` en gris atténué.
Aucun de ces textes n'est exécuté : ce sont des chaînes.

Captures (gestes 1, 4, 5, 8, 13, 14, 20) : `tools/proof/capture-composed.py` (portail, image composée).

## 3. Avec le daemon et le jeu PLEIN ÉCRAN

| # | Écran du jeu | Attendu |
|---|---|---|
| 21 | Carte | Bande visible au-dessus du jeu ; survol : `Deck N`, `Reliques N`, `Potions N`, `Chemins N · K choix`, `Note du deck X` (valeurs == jeu) |
| 22 | Combat | Bande seule ; ouvert : `Deck`, `Reliques`, `Potions` (pas de note du deck ni de chemins) ; **« Fin du tour » entièrement visible et cliquable**, panneau ouvert |
| 23 | Combat, avec un module perso/plugin `screens: ["combat"]` | Le module apparaît **au combat seulement** ; santé ≠ ok : ligne atténuée + le mot (`retard`, `erreur`, `désactivé`) |
| 24 | Récompense de cartes | Carte de décision : lettre S-F (ou `—`), nom, une raison ; survol de la carte : les 3 cartes + `Passer` |
| 25 | Shop | Carte de décision (meilleur article abordable) avec son prix ; survol : articles == écran, `trop cher` si or insuffisant |
| 26 | Loupe au-dessus du jeu (`$H quality=loupe`, épinglé) | Le jeu derrière le panneau est grossi et réfracté **dans le bon alignement** |
| 27 | Quitter le jeu | `En attente du jeu` |

## 4. Mesures FPS (Chronométreur, `PERF.md` §3, `docs/perf/MESURE.md`)

Conditions A (sans ULTIMA), C (défaut : rail, `event`), I (`$S set $K hover-source poll`), E (épinglé
verre), J (épinglé eco), K (épinglé loupe), G (masqué). Aucune capture et aucun changement de focus
pendant un segment. Horodatages des bascules :
`journalctl --user -b -o short-precise | grep -E '\[ULTIMA\] (plan|réglages perf|HUD|apparence)'`.

Nouveau en v1.4 : un **changement de thème ou de police** ne doit rien coûter ensuite. Vérifier :
`$H theme=nuit` puis laisser le panneau replié 10 min → le journal ne montre **aucune** ligne
périodique, et les FPS de la condition C ne bougent pas (le thème n'est relu qu'au changement de
`settings.json`, jamais par image).

## 5. Critères de réussite

- [ ] gestes 1-20 conformes ; rail sans pastille ni texte ; ouverture et fermeture sans saut
- [ ] thèmes : couleurs du thème, `extends` respecté, thème cassé → repli `sts` + journal, 0 `JS ERROR`
- [ ] polices : 12-24 px appliqués, repli thème puis système, texte replié sans débordement
- [ ] disposition : 280-520 px, 3 ancrages, 2 côtés, décalages bornés ; **jamais « Fin du tour »**
- [ ] modules perso : libellé + lignes + note S-F, `screens` respecté, santé ≠ ok atténuée, texte tronqué
- [ ] vraies données == écran (carte, récompense, shop) ; `—` pour un score absent
- [ ] FPS : perte de 1 % low ≤ 3 % en C par rapport à A ; K mesuré (si hors budget, défaut `verre`)

## 6. En cas de problème

```bash
gnome-extensions disable ultima@mio      # retire tout immédiatement (acteurs, effets, clone, raccourcis, minuteries, unredirect)
$S reset-recursively $K                  # réglages GSettings par défaut
$H --reset; $H --reset-appearance        # ou : cp /tmp/ultima-settings.bak ~/.config/ultima/settings.json
mv ~/.config/ultima/themes /tmp/themes.bak   # isoler un thème utilisateur fautif
```
Si l'extension pose encore problème : `overlay/gnome/install-dev.sh --uninstall`, puis se déconnecter
et se reconnecter. Si la session refuse de démarrer, depuis un TTY (Ctrl+Alt+F3) :
`gsettings set org.gnome.shell disable-user-extensions true`.
