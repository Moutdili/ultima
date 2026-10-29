# Fenêtre de test n°2 : overlay v1 (`ultima@mio` branché sur l'état du moteur)

Durée estimée : 25 min, déconnexion/reconnexion comprise, plus le temps de mesure FPS du Chronométreur.
**Ne rien lancer hors de cette fenêtre.** Le jeu est lancé par Mio. L'extension ne lit ni n'écrit
rien du jeu : elle lit seulement `$XDG_RUNTIME_DIR/ultima/state.json`, publié par le daemon.

Ce qui change depuis S1 : panneau en **texte brut** (design en dernier), données de l'EngineState
(contrat v1, notes S à F), modules dans l'ordre des réglages, leviers de performance réglables à chaud
(voir `PERF.md`). Défaut : **flou seulement panneau ouvert** (le rail est un aplat).

## 0. Avant (sans effet sur la session)

```bash
cd ~/v2-overlay-v1                       # ou le dépôt après merge
overlay/gnome/install-dev.sh --check     # metadata, schéma strict, pack, 4 suites de tests ; n'installe rien
```
Attendu : `schéma OK`, `pack OK`, quatre fois `tous les tests passent`, `shell local : GNOME Shell 50.6`.

## 1. Installer, puis se reconnecter

```bash
overlay/gnome/install-dev.sh             # copie + schéma compilé, NE recharge PAS l'extension
```
Puis se déconnecter et se reconnecter. GNOME Shell garde en cache les modules JS d'une extension
déjà chargée : `disable/enable` ne garantit pas que le nouveau code soit pris. La reconnexion est le
chemin sûr.

```bash
gnome-extensions enable ultima@mio       # si elle n'est pas déjà active
journalctl --user -b -o cat /usr/bin/gnome-shell | grep '\[ULTIMA\]' | tail
```
Attendu :
- `[ULTIMA] réglages perf : blur-mode=panel-only unredirect-mode=while-shown poll-interval-ms=80`
- `[ULTIMA] plan : visible=1 flou=0 unredirect-inhibé=1 sondage=80ms (réglage, rail)`
- `[ULTIMA] actif : … état=/run/user/1000/ultima/state.json`
- aucune ligne `JS ERROR`

## 2. Sans le jeu : lecture de l'état (2 min)

À faire **seulement si le daemon ne tourne pas** (sinon il réécrit le fichier).

| # | Geste | Attendu |
|---|---|---|
| 1 | Survoler le rail | Panneau : `ULTIMA · en attente du jeu` / `aucun état publié (daemon arrêté ?)` ; pastilles grises |
| 2 | `mkdir -p $XDG_RUNTIME_DIR/ultima && cp docs/22-state.sample.json $XDG_RUNTIME_DIR/ultima/s.tmp && mv $XDG_RUNTIME_DIR/ultima/s.tmp $XDG_RUNTIME_DIR/ultima/state.json` | Pastilles aqua / rouge / or. Panneau : `Nécrolieuse A9 · acte 1 · étage 4`, `MALÉDICTIONS · 1` en rouge, `SHOP · 116 or`, lignes tronquées annoncées (`DECK · 11 cartes … +5`) |
| 3 | `gsettings --schemadir ~/.local/share/gnome-shell/extensions/ultima@mio/schemas set org.gnome.shell.extensions.ultima height-fraction 0.70` | Panneau plus haut, **rien de tronqué** : 4× Frappe, `carte Scourge · 24 or soldé · A`, `relique Pendule · 181 or TROP CHER · —`, `NOTE DU DECK · C` |
| 4 | `printf '{' > $XDG_RUNTIME_DIR/ultima/s.tmp && mv $XDG_RUNTIME_DIR/ultima/s.tmp $XDG_RUNTIME_DIR/ultima/state.json` | Panneau **inchangé** (dernier état valide) ; journal : `état rejeté (dernier état valide conservé)` |
| 5 | `rm $XDG_RUNTIME_DIR/ultima/state.json` | Retour à `en attente du jeu` |

Mio choisit la hauteur à garder (0.36 par défaut ; à 1536×960, cela donne environ 15 lignes de texte).
Le choix sera noté dans STATUS.md.

## 3. Avec le daemon et le jeu PLEIN ÉCRAN (vraies données)

| # | Écran du jeu | Attendu dans le panneau |
|---|---|---|
| 6 | Carte | Titre (perso, ascension, acte, étage, PV, or) == écran ; `CHEMINS · N jusqu'au boss` + choix par colonne ; deck == deck du jeu (quantités, `+` des améliorations) |
| 7 | Combat | Pas de module contextuel ni de note du deck ; malédictions toujours visibles |
| 8 | Récompense de cartes | `RÉCOMPENSE` : les 3 cartes proposées dans l'ordre, une lettre S à F (ou `—`), `passer · …` |
| 9 | Shop | Articles et prix == écran, `soldé`, `TROP CHER` si or insuffisant, `retrait · 75 or` |
| 10 | Quitter le jeu | `ULTIMA · en attente du jeu` (si le daemon publie `gameRunning=false`) |
| 11 | Couper le miroir (daemon dégradé) | Ligne orange `DÉGRADÉ : …` **en tête** |

Clics et touches doivent toujours aller au jeu, panneau ouvert ou non (non-régression S1).

## 4. Mesures FPS (Chronométreur, protocole `PERF.md` §3)

Conditions A à H : sans ULTIMA, S1 (`blur-mode=always`), défaut v1, sans inhibition d'unredirect,
panneau épinglé avec et sans flou, masqué, sans survol. Les bascules se font à chaud par `gsettings`
(commandes dans `PERF.md` §1), sans recharger. Les horodatages des bascules se lisent avec
`journalctl --user -b -o short-precise | grep -E '\[ULTIMA\] (plan|réglages perf)'`.
Pour D (`unredirect-mode=while-expanded`), **noter si le rail reste visible** au-dessus du jeu plein écran.

## 5. Preuves à recueillir

1. Captures des gestes 2, 3, 8 et 9 (`Impr. écran` → écran entier).
2. Journal : `journalctl --user -b -o short-precise /usr/bin/gnome-shell | grep -E '\[ULTIMA\]|JS ERROR' > /tmp/ultima-tw2.log`
3. Traces MangoHud par condition (Chronométreur).

## 6. Critères de réussite

- [ ] gestes 1 à 5 conformes (lecture sur changement, dernier état valide, en attente)
- [ ] vraies données == écran sur carte, récompense, shop ; malédictions visibles ; `—` pour un score absent
- [ ] `[ULTIMA] plan : visible=0 … sondage=non` après Super+Maj+O
- [ ] FPS : perte de 1 % low ≤ 3 % avec le défaut v1 (C) par rapport à A, sinon levier identifié
- [ ] aucune ligne `JS ERROR` liée à `ultima@mio`

## 7. En cas de problème

```bash
gnome-extensions disable ultima@mio      # retire tout immédiatement (acteurs, raccourcis, minuteries, unredirect)
S="gsettings --schemadir $HOME/.local/share/gnome-shell/extensions/ultima@mio/schemas"
$S reset-recursively org.gnome.shell.extensions.ultima    # réglages par défaut
```
Si l'extension pose encore problème : `overlay/gnome/install-dev.sh --uninstall`, puis se déconnecter
et se reconnecter. Si la session refuse de démarrer, depuis un TTY (Ctrl+Alt+F3) :
`gsettings set org.gnome.shell disable-user-extensions true`.
