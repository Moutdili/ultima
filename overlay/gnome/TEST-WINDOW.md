# Fenêtre de test n°3 : HUD verre v3 (`ultima@mio` 0.3.0)

Durée estimée : 30 min, déconnexion/reconnexion comprise, plus les mesures FPS du Chronométreur.
**Ne rien lancer hors de cette fenêtre.** Le jeu est lancé par Mio. L'extension ne lit ni n'écrit
rien du jeu : elle lit `$XDG_RUNTIME_DIR/ultima/state.json` (daemon) et `~/.config/ultima/settings.json`
(réglages, jamais écrits par l'extension).

Ce qui change depuis la fenêtre n°2 : rail = **simple bande de verre** (plus de pastilles, §12) ;
ouverture fluide (le verre glisse en 220 ms, puis le contenu apparaît ligne par ligne) ; contenu
minimaliste (§11 : au shop/récompense/événement, la carte de décision seule ; ailleurs une ligne
par module) ; trois niveaux de verre `eco` / `verre` / `loupe` (HudSettings v1.3) ; survol piloté
par le signal du curseur (`hover-source=event`, aucun réveil quand le pointeur ne bouge pas).

Raccourcis : **Super+Maj+I** épingler (détail), **Super+Maj+O** masquer. Jamais de touche F.

## 0. Avant (sans effet sur la session)

```bash
cd ~/v2-verrier-design                   # ou le dépôt après merge
overlay/gnome/install-dev.sh --check     # metadata, schéma strict, pack, 6 suites de tests ; n'installe rien
cp -a ~/.config/ultima/settings.json /tmp/ultima-settings.bak 2>/dev/null || echo "pas de settings.json"
H=overlay/gnome/tools/hud-set.py         # règle le bloc « hud » de settings.json (écriture atomique)
S="gsettings --schemadir $HOME/.local/share/gnome-shell/extensions/ultima@mio/schemas"
K=org.gnome.shell.extensions.ultima
```
Attendu : `schéma OK`, `pack OK`, six fois `tous les tests passent`, `shell local : GNOME Shell 50.6`.

## 1. Installer, puis se reconnecter

```bash
overlay/gnome/install-dev.sh             # copie + schéma compilé, NE recharge PAS l'extension
```
Se déconnecter / se reconnecter (le cache des modules JS rend `disable/enable` peu sûr), puis :
```bash
gnome-extensions enable ultima@mio       # si elle n'est pas déjà active
journalctl --user -b -o cat /usr/bin/gnome-shell | grep -E '\[ULTIMA\]|JS ERROR' | tail -n 20
```
Attendu :
- `[ULTIMA] actif : couche=addTopChrome, API flou=1 glsl=1 clone=1, état=… réglages=…/.config/ultima/settings.json`
- `[ULTIMA] HUD (démarrage) : quality=verre heightFraction=0.55 restOpacity=0.62 side=right … ; verre effectif=verre`
- `[ULTIMA] plan : visible=1 verre=verre flou=0 reflet=0 loupe=0 unredirect-inhibé=1 survol=event/80ms (réglage, rail)`
- aucune ligne `JS ERROR` ; si `API … =0` : noter laquelle (le niveau descend tout seul, journal `repli : API absente`).

## 2. Sur le bureau, sans le jeu (8 min)

| # | Geste | Attendu |
|---|---|---|
| 1 | Regarder le bord droit | Une bande de verre de 14 px collée au bord, à 22 % du haut, liseré or ; **aucune pastille, aucun texte, aucune icône** |
| 2 | Amener le pointeur sur la bande et **ne plus bouger** | Ouverture en ~0,2 s : le verre glisse et s'élargit sans à-coup, puis les lignes apparaissent en fondu, l'une après l'autre. Sans daemon : `En attente du daemon`. **Si rien ne s'ouvre** : `$S set $K hover-source poll`, recommencer, et le noter (signal du curseur absent) |
| 3 | Sortir le pointeur, attendre 2,5 s | Le contenu s'efface, le verre rentre (plus vite qu'à l'ouverture), le flou reste jusqu'au bout du glissement (pas de « saut ») |
| 4 | (daemon arrêté seulement) `mkdir -p $XDG_RUNTIME_DIR/ultima && cp docs/22-state.sample.json $XDG_RUNTIME_DIR/ultima/s.tmp && mv $XDG_RUNTIME_DIR/ultima/s.tmp $XDG_RUNTIME_DIR/ultima/state.json`, puis survol | Une seule carte : grande lettre **A** verte, `Scourge · 24 or soldé`, dessous `Méta (médiane 3 sources) +65` en crème atténuée. Rien d'autre |
| 5 | Pointeur sur la carte | Détail : les 5 options du shop dans l'ordre (`— · Graveblast · 50 or` … `B · Retrait · 75 or`), termes, puis `Deck 11`, `Enchantées 1` (aqua), `Malédiction 1` (violet), `Reliques 1`, `Potions 1`, `Note du deck C` |
| 6 | Super+Maj+I, puis Super+Maj+I | Épinglé ouvert avec le détail ; libéré : repli |
| 7 | Super+Maj+O, puis Super+Maj+O | Tout disparaît (journal `plan : visible=0 … survol=non`), puis revient |
| 8 | `$H quality=eco` puis survol | Journal `HUD (settings.json) : quality=eco … verre effectif=eco`. Panneau = aplat sombre opaque (0.85), **pas de flou** |
| 9 | `$H quality=verre` puis survol | Flou du fond + reflet diagonal discret + grain fin ; texte lisible (contraste) |
| 10 | `$H quality=loupe` puis survol, **sur une fenêtre au contenu net** (texte) | Après l'ouverture : le fond est légèrement grossi (×1,06), courbé sur les bords (lentille), fine aberration colorée au bord ; le centre reste aligné sur ce qui est derrière. Journal `plan : … loupe=1` |
| 11 | `$H restOpacity=0.4`, puis `0.9` | Bande plus transparente puis plus opaque au repos |
| 12 | `$H side=left`, puis `$H side=right` | Le HUD passe au bord gauche (mêmes gestes), puis revient |
| 13 | `$H heightFraction=0.7`, puis `$H --reset` | Panneau plus haut ; retour aux défauts du contrat |
| 14 | `rm $XDG_RUNTIME_DIR/ultima/state.json` (daemon arrêté seulement) | Retour à `En attente du daemon` |

Captures (gestes 1, 4, 5, 8, 9, 10) : `tools/proof/capture-composed.py` (portail, image composée).

## 3. Avec le daemon et le jeu PLEIN ÉCRAN

| # | Écran du jeu | Attendu |
|---|---|---|
| 15 | Carte | Bande visible au-dessus du jeu ; survol : `Deck N`, `Reliques N`, `Potions N`, `Chemins N · K choix`, `Note du deck X` (une ligne par module, valeurs == jeu) |
| 16 | Combat, pioches | Bande seule ; ouvert : `Deck`, `Reliques`, `Potions` (pas de note du deck ni de chemins). Clics et touches vont au jeu, bande et panneau ouverts compris (non-régression S1) |
| 17 | Récompense de cartes | Carte de décision : lettre S-F (ou `—`) de la meilleure option, son nom, une raison ; survol de la carte : les 3 cartes + `Passer` |
| 18 | Shop | Carte de décision (article le mieux noté abordable) avec son prix ; survol : articles == écran, `trop cher` si or insuffisant |
| 19 | Loupe au-dessus du jeu (`$H quality=loupe`, panneau épinglé) | Le jeu derrière le panneau est grossi et réfracté **dans le bon alignement** (pas de décalage, pas de bande noire) |
| 20 | Quitter le jeu | `En attente du jeu` |

## 4. Mesures FPS (Chronométreur, `PERF.md` §3, `docs/perf/MESURE.md`)

Conditions A (sans ULTIMA), C (défaut : rail, `event`), I (`$S set $K hover-source poll`), E (épinglé
verre), J (épinglé eco), K (épinglé loupe), G (masqué). Aucune capture et aucun changement de focus
pendant un segment. Horodatages des bascules :
`journalctl --user -b -o short-precise | grep -E '\[ULTIMA\] (plan|réglages perf|HUD)'`.

## 5. Critères de réussite

- [ ] gestes 1-14 conformes ; rail sans pastille ni texte ; ouverture et fermeture sans saut
- [ ] survol en mode `event` fonctionnel au-dessus du jeu plein écran (sinon : `poll`, à noter)
- [ ] eco sans flou, verre flouté, loupe grossie et alignée ; aucun `JS ERROR` (shaders compilés)
- [ ] vraies données == écran (carte, récompense, shop) ; `—` pour un score absent
- [ ] FPS : perte de 1 % low ≤ 3 % en C par rapport à A ; K mesuré (si hors budget, défaut `verre`)

## 6. En cas de problème

```bash
gnome-extensions disable ultima@mio      # retire tout immédiatement (acteurs, effets, clone, raccourcis, minuteries, unredirect)
$S reset-recursively $K                  # réglages GSettings par défaut
$H --reset                               # ou : cp /tmp/ultima-settings.bak ~/.config/ultima/settings.json
```
Si l'extension pose encore problème : `overlay/gnome/install-dev.sh --uninstall`, puis se déconnecter
et se reconnecter. Si la session refuse de démarrer, depuis un TTY (Ctrl+Alt+F3) :
`gsettings set org.gnome.shell disable-user-extensions true`.
