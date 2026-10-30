# Console `ultima` — état (agent Console, 2026-10-30)

Branche `v2/console`, rebasée sur `main` (`d55d9e8`, qui contient `0bc0da1`). Auteur des commits : Mio.

## Fait

- **TUI** (`ultima` sans argument, Textual 8.2.8) : bandeau ULTIMA, tableau de bord (la décision d'abord : lettre S-F
  plus raison), Deck, Reliques/Potions, HUD, Journal, Contrôles.
- **Contrat v1.3** : tout champ ou section absent ou `null` s'affiche « — » (jamais `None`, jamais 0). Cela couvre
  `skipScore`, `cost`, `deck` (y compris quand il vaut null), `event` non lu, ainsi que `paths`/`route`/`map`.
  L'équipe co-op s'affiche dans les couleurs des personnages. Validé sur `docs/ipc/state.v13.sample.json`.
- **Réglages du HUD, tous éditables dans le TUI** : les 6 champs de `HudSettings`, plus chaque module (affiché et
  ordre, dans l'ordre du contrat).
  - Bornes du contrat : hauteur 0,30–0,85, opacité 0,35–1. Une valeur hors bornes est ramenée à la borne.
  - Un module listé sans `enabled` est actif (`true`).
  - L'écriture est atomique, octet pour octet comme le compagnon (vérifié par un test).
  - Les libellés sont identiques à `grammar.js` (`MODULE_LABEL`) et au compagnon (`MODULE_LABELS`), vérifié par un test.
- **Journal** : format du daemon de main (`AAAA-MM-JJ HH:MM:SS.mmm NIVEAU`) ; l'ancien format reste lu.
  - Filtre par niveau, recherche, suivi qui reprend après une rotation.
  - Couleurs personnalisables (`~/.config/ultima/console.json`, palette du jeu).
  - `ultima start` ne duplique plus les lignes : stdout et stderr vont dans `daemon.stderr`, et le daemon reçoit `--log`.
- **Sous-commandes** : `start`, `kill`, `restart`, `status` (code 3 si daemon injoignable), `logs`, `ps`, `hud`, `dump`,
  `doctor`, `install`, `uninstall`, `version`, `banner`.
  - Toutes sont en couleur ; `--json`, `--color auto|always|never`, `NO_COLOR` sont respectés.
  - `kill` n'arrête que des PID dont `/proc/PID/cmdline` est un `ultima-daemon run` du dossier IPC courant.
- **Bandeau** (dessin de Mio recopié tel quel, 48×8) :
  - dégradé par paliers de cream, gold et orange (palette du jeu, aucune couleur inventée) ;
  - repli sur une seule ligne sous 52×30 ou avec une locale non UTF-8. Le lanceur transmet la locale d'origine
    dans `ULTIMA_LOCALE`, parce que Python transforme une locale C en C.UTF-8.
- **`tools/install.sh`** (et `tools/uninstall.sh` = `install.sh --uninstall` = `ultima uninstall`) :
  - **Prérequis** vérifiés ; build Release du daemon et de `ultima-mirror`, avec un HOME jetable pour dotnet et NuGet
    et sans serveur de build laissé en mémoire. Base FR en `--kb auto` ; extension copiée, jamais activée.
  - **Venv** Textual : versions figées dans `tools/console/requirements.txt`, pip lancé avec `--no-cache-dir`.
  - **Manifeste** `~/.local/share/ultima/manifest.json` : chaque fichier (avec son sha256), chaque lien, le venv, les
    dossiers créés et les sauvegardes.
  - **Existant non géré** (ancien `~/.local/bin/ultima`, extension posée à la main) : déplacé dans `backup/`, remis à
    la désinstallation.
  - **Réinstallation** idempotente : 0 écriture si rien n'a changé.
  - **`--purge`** : retire aussi les réglages, le journal et le dossier IPC, après confirmation (`-y` obligatoire hors
    terminal). Refusé tant qu'un daemon répond sur la socket.
  - **Options** : `--prefix`, `--no-build`/`--build-dir`, `--no-venv`, `--wheels`, `--systemd` (l'unité est écrite,
    jamais activée), `--check`.
  - Aucun `sudo`, aucun `systemctl` à l'installation, aucun `gnome-extensions`.

## Prouvé

Tests (venv de test du scratchpad, `pip install -r tools/console/requirements-dev.txt`) :
```
$ python -m pytest tools/console
124 passed in 30.82s
  (banner 23 · cli 15 · installer 10 · logs 9 · procs 8 · settings 12 · state 7 · textview 24 · tui 16)
```
Chaque test tourne avec `HOME`, `XDG_*` et `ULTIMA_*` dans `tmp_path`. Un garde-fou fait échouer tout chemin résolu
hors du dossier de test, et un contrôle de fin de test détecte une isolation défaite. Le daemon est un faux
(`ultima-daemon run` → python3), `gnome-extensions` est un faux, `systemctl` est désactivé.
La TUI est testée en headless (`App.run_test`).

Installation réelle (build dotnet compris) dans un HOME temporaire, avec un faux ancien `~/.local/bin/ultima` :
```
== install 1
sauvegardé : $S/ihome/.local/bin/ultima → $S/ihome/.local/share/ultima/backup/HOME/.local/bin/ultima
116 élément(s) écrit(s), 116 entrée(s) au manifeste
== install 2 (idempotence)
venv inchangé : …/venv
0 élément(s) écrit(s), 116 entrée(s) au manifeste
install1 == install2 (3029 entrées)
fichiers hors manifeste : aucun
== uninstall
  remis en place : $S/ihome/.local/bin/ultima
  116 élément(s) retiré(s), 1 sauvegarde(s) remise(s)
ARBRE IDENTIQUE avant/après (6 entrées)
```
Le diff compare le type, le mode, le chemin, la cible des liens et le sha256 des fichiers (les mtimes des dossiers ne
sont pas comparés). D'autres scénarios sont prouvés :
- `--kb auto` génère la base FR (721 Kio).
- `ultima uninstall --purge` sans `-y` hors terminal renvoie le code 2 ; avec `-y`, `ARBRE IDENTIQUE après --purge`.
- `--prefix` donne `HOME inchangé par l'install --prefix`, puis `préfixe retiré entièrement`.
- Le daemon installé répond à `ultima dump` (`contrat 1 3`), et le venv installé fait tourner la TUI en headless.

## NON prouvé

- **Rien n'a été installé pour de vrai** : `~/.local/bin/ultima` (le script du 29/10 13:55), `~/.config/ultima`,
  `~/.local/state/ultima` et `$XDG_RUNTIME_DIR/ultima` n'ont pas été touchés.
- **Aucune commande lancée contre le vrai daemon** (PID 663552, toujours actif) : ni `start`/`kill`/`restart`, ni
  `hud on|off`.
- **L'extension n'a été ni activée ni installée** dans la vraie session.
- **Pas de vrai terminal** : la TUI n'a jamais tourné dans un terminal réel (headless seulement). Les couleurs réelles
  du thème et le confort à 1920×1200 n'ont pas été vus.
- **Unité systemd jamais démarrée** : `--systemd` écrit l'unité (`ExecStart=…/bin/ultima daemon-exec`), mais elle n'a
  jamais tourné.
- **Venv hors ligne non essayé** : `--wheels` n'a pas été testé.

## Reste à faire / points ouverts

- **Bornes du HUD encore anciennes chez les autres** : sur main, le compagnon applique 0,30–0,90 et 0,20–1, et le
  daemon (`SettingsStore.NormalizeHud`) garde toute valeur de ]0,1] sans la ramener à la borne. Le TUI applique déjà
  le contrat (0,30–0,85 et 0,35–1). Tant que ce n'est pas aligné, le daemon peut publier 0,9 alors que le TUI affiche
  85 %.
- **Modules inconnus** : un module d'id inconnu (version future) est conservé dans `settings.json`, alors que le daemon
  l'ignore. Le choix est assumé, et c'est aussi ce que fait le compagnon.
- **Extension en cours d'activation** : `ultima uninstall` n'appelle pas `gnome-extensions disable` (interdit pendant
  les tests). Il faut la désactiver avant de désinstaller si elle est active.

## À lancer par Mio pour l'installation réelle

```bash
cd ~/sts2                              # après merge de v2/console
tools/install.sh --check               # prérequis, n'écrit rien
tools/install.sh                       # build + ~/.local/share/ultima + ~/.local/bin/ultima
                                       # (l'ancien script ~/.local/bin/ultima est sauvegardé dans
                                       #  ~/.local/share/ultima/backup/HOME/.local/bin/ultima)
ultima doctor
ultima kill && ultima start            # remplace le daemon lancé depuis le worktree v2-cableur (PID 663552)
# HUD : se reconnecter, puis  ultima hud on
# Service (facultatif) : tools/install.sh --systemd && systemctl --user daemon-reload \
#                        && systemctl --user enable --now ultima-daemon.service
# Retrait : tools/uninstall.sh   (--purge pour les réglages et le journal)
```
