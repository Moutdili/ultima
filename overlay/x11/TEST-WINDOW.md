# Fenêtre de test — moteur A (X11 override-redirect) au-dessus du VRAI jeu

But : prouver la porte S1 sur le vrai Slay the Spire 2 **plein écran** : panneau ULTIMA visible,
clic traversant, raccourci qui bascule, focus du jeu intact, FPS inchangé.
Durée : ~10 min. Aucun fichier du jeu ni save n'est touché ; l'overlay ne lit rien du jeu.

Chemins ci-dessous : worktree `/home/mio/v2-verrier-x11` (à remplacer par `/home/mio/sts2` après merge).

## 0. Préparation (une fois, jeu fermé)

```bash
O=/home/mio/v2-verrier-x11/overlay/x11
$O/ultima-x11 help                 # compile si besoin (dotnet 9), affiche l'usage
```

Raccourci GNOME personnalisé (le jeu ne lit JAMAIS cette touche, c'est Mutter qui l'intercepte) :
Paramètres → Clavier → Raccourcis personnalisés → « + »
- Nom : `ULTIMA toggle` — Commande : `/home/mio/v2-verrier-x11/overlay/x11/ultima-x11 toggle` — Raccourci : `Super+Maj+U`

(Optionnel, second raccourci `Super+Maj+H` → `.../ultima-x11 visible` = masquage total, ex-F8.)

FPS (optionnel, pour le critère « FPS inchangé ») : compteur FPS de Steam
(Steam → Paramètres → En jeu → Compteur d'images/s), ou MangoHud (déjà installé en couche Flatpak)
via les options de lancement Steam `MANGOHUD=1 %command%` — à retirer après le test.

## 1. Déroulé (commande unique par étape, résultat attendu écrit à l'avance)

| # | Action de Mio | Résultat attendu |
|---|---|---|
| 1 | Lancer STS2 depuis Steam, **plein écran**, entrer dans un combat. Noter le FPS. | FPS de référence F0. |
| 2 | Dans un terminal (Alt+Tab possible, ou lancer AVANT le jeu) : `$O/ultima-x11 run` | Log : `override_redirect=1`, `scale=2 (Xft.dpi=192)`, `input_region_rects=0`. Un **rail de 14 px logiques (~17 px physiques) à liseré or, 3 pastilles** apparaît au bord droit, ~120 px sous le haut, **par-dessus le jeu**. |
| 3 | Revenir au jeu (clic dedans). Appuyer `Super+Maj+U`. | Le panneau « ULTIMA » (260 px logiques, verre `#192930` ~78 %, 5 lignes de deck factices) se déploie en ~180 ms. Le jeu garde le focus. |
| 4 | Survoler puis cliquer un élément du jeu **sous le panneau** (ennemi, intention, relique en haut à droite…). | Le jeu réagit (survol/infobulle/clic) exactement comme sans overlay. Aucun clic avalé. |
| 5 | Juste après, appuyer une touche du jeu (ex. `E` fin de tour, ou `Échap`). | Le jeu réagit : le focus clavier n'a jamais quitté le jeu. |
| 6 | `Super+Maj+U` à nouveau. | Retour au rail. |
| 7 | Capture COMPOSÉE (ce que voit l'œil) depuis un terminal pendant que le jeu est au premier plan : `python3 -I $O/tools/portal-screenshot.py ~/ultima-s1-jeu.png` (ou `Impr. écran` GNOME). | Le PNG montre le jeu plein écran ET le panneau/rail ULTIMA. |
| 8 | Comparer le FPS en combat, panneau déployé. | F1 ≥ F0 − 2 (et ≥ 55). |
| 9 | `$O/ultima-x11 status` puis `$O/ultima-x11 stack` | `input_rects=0` ; dans `stack`, la ligne `name="ULTIMA"` est **au-dessus** (plus bas dans la liste) que la fenêtre du jeu. |
| 10 | `$O/ultima-x11 quit` | L'overlay disparaît, le jeu continue. |

Envoyer au chef : `~/ultima-s1-jeu.png`, la sortie des étapes 2/9, et
`~/.local/state/ultima/ultima-x11.log` (chaque `toggle` y est horodaté avec la durée de transition).

## 2. Critère de réussite (porte S1, moteur A)

- [ ] Capture composée : jeu **plein écran** + ULTIMA visible (rail ET panneau).
- [ ] Clic et survol traversent le panneau (étape 4), touche du jeu OK juste après (étape 5).
- [ ] `Super+Maj+U` bascule en jeu, sans que le jeu reçoive la touche.
- [ ] FPS en combat inchangé à ±2 (≥ 55).
- [ ] Le log montre `transition 14->260px en 150-220 ms`.

## 3. Si ça échoue — quoi noter

- **Panneau invisible au-dessus du jeu** : noter si le jeu est en « plein écran exclusif » ou
  « fenêtré sans bordure » (réglage du jeu) ; refaire l'étape 7 et l'étape 9. Le jeu Flatpak Steam
  parle bien au même Xwayland `:0` (socket X11 partagé), mais un passage en Wayland natif du jeu
  (`--display-driver wayland`) casserait le moteur A.
- **Raccourci sans effet en jeu** : GNOME a peut-être affiché « Autoriser l'application à inhiber
  les raccourcis ? » → répondre Non ; sinon tester `ultima-x11 toggle` depuis un terminal.
- **Panneau trop petit / trop gros** : `ULTIMA_X11_SCALE=1.6 $O/ultima-x11 run` (1 px logique =
  1 px physique sur l'écran 1920×1200 à 125 %) ; position verticale : `ULTIMA_X11_TOP=200`.
