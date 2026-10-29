---
name: sts2-console-tui
description: Use when building the `ultima` terminal command — full-screen TUI dashboard when run without arguments (live state, deck, S-F grades, sources, logs, HUD settings), colored scriptable subcommands, customizable colored logs, and the clean install/uninstall of ULTIMA on Fedora.
---

# ULTIMA — console `ultima` (TUI + CLI + installation)

## Exigences de Mio (2026-10-29)
- `ultima` sans argument = **TUI plein écran, propre, en couleur** : tableau de bord vivant.
- Configuration du HUD et paramétrage depuis le TUI ; **logs en couleur personnalisables**.
- Sous-commandes conservées pour les scripts (`start|kill|restart|status|logs|ps|hud on|off|status|dump`),
  sortie colorée si TTY, `NO_COLOR` respecté, `--json` pour les scripts.
- **Installation propre** : rien ne pointe vers un dossier de travail d'agent.

## Données
État : `$XDG_RUNTIME_DIR/ultima/state.json` + `state.sock` (contrat : skill `sts2-engine-contract`).
Réglages : `~/.config/ultima/settings.json` (`OverlaySettings` + `Hud` v1.3, écriture atomique).
Préférences du TUI (thème, couleurs des niveaux de log) : `~/.config/ultima/console.json`.
Journal du daemon : `~/.local/state/ultima/daemon.log` (plus jamais à la racine de `~`).

## Écrans du TUI
1. Tableau de bord : jeu/daemon/miroir (santé, latence), écran courant, run (perso, acte, PV, or), décision en
   cours (récompense/shop avec lettres S-F colorées), malédictions, enchantées.
2. Deck / reliques / potions (avec descriptions FR et notes).
3. HUD : modules (ordre/visibilité), qualité du verre (eco/verre/loupe), hauteur, opacité, côté, aperçu texte.
4. Journal en direct, filtrable par niveau, couleurs personnalisables (thème + par niveau).
5. Contrôles : démarrer/arrêter le daemon, HUD on/off, installer/vérifier (doctor).
Couleurs des notes : S gold, A green, B aqua, C blue, D orange, F red, — gray (palette du jeu).

## Installation
`tools/install.sh` (idempotent) : build Release → `~/.local/share/ultima/{bin,lib}` ; lien `~/.local/bin/ultima` ;
génération de la base de connaissance (`build-kb`) ; extension GNOME copiée (activation laissée à Mio) ;
unité systemd utilisateur optionnelle. `ultima uninstall` retire tout (sauf réglages, sur option).
`ultima doctor` vérifie l'installation. Aucune dépendance système sans accord (pas de sudo) ; dépendances
Python éventuelles dans un venv sous `~/.local/share/ultima/venv`.
