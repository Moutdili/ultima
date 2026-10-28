# Fenêtre de test S1 : moteur B (extension GNOME Shell `ultima@mio`)

Durée estimée : 15 min, déconnexion/reconnexion comprise. **Ne rien lancer hors de cette fenêtre.**
Le jeu est lancé par Mio. L'extension ne lit ni n'écrit rien dans le dossier du jeu ni dans ses saves.

## 0. Avant de commencer (30 s, sans effet sur la session)

```bash
cd ~/v2-verrier-gnome            # ou le dépôt après merge
overlay/gnome/install-dev.sh --check     # valide metadata, schéma et pack ; n'installe rien
gsettings get org.gnome.shell disable-user-extensions    # attendu : false
```

## 1. Installer (copie seulement, sans activation)

```bash
overlay/gnome/install-dev.sh
# -> ~/.local/share/gnome-shell/extensions/ultima@mio  (schéma compilé, NON activé)
```

## 2. Se déconnecter puis se reconnecter

Sous Wayland, GNOME Shell ne découvre une nouvelle extension qu'au démarrage de la session.
Enregistrer son travail, puis menu système → Se déconnecter, et se reconnecter (session GNOME Wayland).

## 3. Activer et vérifier

```bash
gnome-extensions info ultima@mio          # État : INACTIVE (ou DISABLED)
gnome-extensions enable ultima@mio
gnome-extensions info ultima@mio          # attendu : État : ACTIVE (ENABLED)
journalctl --user -b -o cat /usr/bin/gnome-shell | grep '\[ULTIMA\]' | tail
#   attendu : "[ULTIMA] géométrie : moniteur=1536x960 ..." et "[ULTIMA] actif : couche=addTopChrome ..."
#   (1536x960 = 1920x1200 à l'échelle 1,25, en pixels logiques)
```

Attendu immédiatement, hors jeu : un rail vertical de 14 px (≈ 17 px physiques), ancré à droite,
de 22 % à 58 % de la hauteur, avec 3 pastilles (rouge, or, aqua) et un liseré or.

## 4. Lancer le jeu en PLEIN ÉCRAN, entrer en combat

## 5. Gestes à faire (dans cet ordre), avec le résultat attendu écrit à l'avance

| # | Geste | Attendu |
|---|---|---|
| 1 | Rien, en combat | Le rail est visible au-dessus du jeu plein écran |
| 2 | Amener la souris sur le rail (≈ 0,2 s) | Le panneau s'ouvre en 260 px (180 ms, sans rebond) : verre flouté, titre « Deck · 10 cartes · EXEMPLE », 5 lignes |
| 3 | Cliquer une carte/un ennemi du jeu situé **sous** le panneau ouvert | Le jeu reçoit le clic (le panneau ne prend aucun clic) |
| 4 | Sortir la souris du panneau | Retour au rail après 2,5 s |
| 5 | `Super+F9` | Panneau épinglé ouvert, il ne se replie pas |
| 6 | `Super+F9` | Panneau libéré, retour au rail après 2,5 s hors survol |
| 7 | `Super+F8` | ULTIMA entièrement masqué ; `Super+F8` de nouveau : réaffiché |
| 8 | Taper au clavier dans le jeu | Le jeu garde le focus ; F8/F9 seuls ne sont pas pris par ULTIMA |
| 9 | Super seul (sans autre touche) | Comportement GNOME normal (vue d'ensemble) ; ULTIMA s'y masque |

Si le rail recouvre des intents ou le bouton de fin de tour, on peut le déplacer sans recompiler.
Le réglage est appliqué à chaud :

```bash
SCHEMADIR=~/.local/share/gnome-shell/extensions/ultima@mio/schemas
gsettings --schemadir $SCHEMADIR set org.gnome.shell.extensions.ultima top-fraction 0.30
gsettings --schemadir $SCHEMADIR set org.gnome.shell.extensions.ultima height-fraction 0.30
```

## 6. Preuves à recueillir

1. **Capture** pendant les gestes 1 et 2 (`Impr. écran` → capture d'écran entier). Les deux doivent montrer le jeu
   plein écran avec le rail, puis avec le panneau ouvert.
2. **FPS** : compteur FPS de Steam (Paramètres → En jeu → compteur d'images) relevé pendant
   30 s de combat avec ULTIMA visible, puis 30 s après `Super+F8` (masqué). Seuil : ≥ 55 FPS avec ULTIMA visible
   (docs/17 §7.7). Noter les deux valeurs.
3. **Log** : `journalctl --user -b -o cat /usr/bin/gnome-shell | grep -E '\[ULTIMA\]|JS ERROR' > /tmp/ultima-s1.log`

## 7. Critères de réussite (porte S1)

- [ ] rail visible au-dessus du jeu **plein écran** (capture 1)
- [ ] panneau 260 px avec un **verre flouté visible** derrière le texte (capture 2)
- [ ] clic sur le jeu à travers le panneau ouvert : reçu par le jeu
- [ ] `Super+F9` / `Super+F8` fonctionnent pendant que le jeu a le focus ; le jeu ne perd jamais le focus
- [ ] FPS avec ULTIMA visible ≥ 55 et écart avec ULTIMA masqué noté
- [ ] aucune ligne `JS ERROR` liée à `ultima@mio` dans le log

## 8. En cas de problème (dans l'ordre)

```bash
gnome-extensions disable ultima@mio        # retire tout immédiatement (acteurs, raccourcis, timers)
```
Si l'extension pose encore problème : `overlay/gnome/install-dev.sh --uninstall`, puis se déconnecter et se reconnecter.
Si la session refuse de démarrer, depuis un TTY (Ctrl+Alt+F3) : `gsettings set org.gnome.shell disable-user-extensions true`.
