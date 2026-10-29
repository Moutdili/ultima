---
name: sts2-companion-app
description: Use when building the ULTIMA companion window (the Hearthstone Deck Tracker-style main window) on Fedora GNOME — GTK4/libadwaita in Python, reading EngineState over IPC, editing overlay settings (modules, order, visibility), source health and run info. Functional UI only until Mio orders the design phase.
---

# ULTIMA — fenêtre compagnon (modèle HDT : overlay + fenêtre principale)

## Pile
Python 3 + PyGObject, **GTK4 + libadwaita 1.9** (présents sur la machine, natif GNOME). Lancement :
`overlay/companion/ultima-companion` (script) ; fichier `.desktop` utilisateur optionnel.

## Données
- Lit `$XDG_RUNTIME_DIR/ultima/state.json` (Gio.FileMonitor) ou `state.sock` ; contrat : skill
  `sts2-engine-contract`, exemple `docs/22-state.sample.json`.
- Écrit UNIQUEMENT `~/.config/ultima/settings.json` (écriture atomique) : modules (id, activé, ordre).
- Ne lit jamais le jeu, ses saves ou sa mémoire directement.

## Pages (fonctionnelles, texte brut — design en dernier, décision Mio)
1. **État** : jeu lancé ?, sources (save, miroir, latence, mode dégradé), écran courant.
2. **Overlay** : liste des modules (deck, enchantées, malédictions, reliques, potions, récompense, shop,
   chemins, note du deck) — activer/désactiver, réordonner ; raccourcis rappelés (Super+Maj+I / O).
3. **Run** : deck complet, reliques, potions avec descriptions FR et scores (« — » si absent).
4. **Historique** (si le daemon l'expose plus tard) : vide propre sinon.

## Règles
Ne jamais voler le focus du jeu ; aucune fenêtre ouverte sans action de Mio ; aucune capture d'écran.
