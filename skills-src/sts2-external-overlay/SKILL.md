---
name: sts2-external-overlay
description: Use when building or changing the ULTIMA external overlay drawn above Slay the Spire 2 on Fedora GNOME Wayland — the GNOME Shell 50 extension engine (proven over the fullscreen game), shortcuts, click-through, hover, glass, and the X11 fallback engine.
---

# ULTIMA — overlay externe (prouvé sur le vrai jeu, 2026-10-29)

## Machine de référence
GNOME Shell 50.6 Wayland, écran 1920×1200 à l'échelle 1,25 (logique 1536×960 ; XWayland 3072×1920,
facteur X11→écran 1,6). Le jeu : Godot X11 via XWayland, PLEIN ÉCRAN obligatoire, rendu 16:9 avec bandes.

## Moteur retenu : extension GNOME Shell `ultima@mio` (`overlay/gnome/`)
- Couche : `Main.layoutManager.addTopChrome(actor)` **sans** `trackFullscreen` (layout.js 880-888) +
  `global.compositor.disable_unredirect()` tant que visible → affiché au-dessus du jeu plein écran (prouvé).
- Click-through : AUCUN acteur réactif. Survol = lecture de `global.get_pointer()` à 12,5 Hz seulement
  quand ULTIMA est visible. Prouvé : clic + touche « 1 » reçus par le jeu, panneau ouvert.
- Verre : `Shell.BlurEffect` (mode BACKGROUND) + teinte #192930, liseré or, radius 10 (prouvé sur capture).
- Raccourcis (`Main.wm.addKeybinding`, schéma GSettings) : **Super+Maj+I** épingler, **Super+Maj+O** masquer.
  ⚠️ JAMAIS de touches F : sur le portable de Mio la rangée F envoie des touches média.
- Installer : `overlay/gnome/install-dev.sh` puis déconnexion/reconnexion (Wayland), `gnome-extensions enable ultima@mio`.
  Recharger sans déconnexion (extension déjà connue) : `gnome-extensions disable/enable ultima@mio`.
- Log : `journalctl --user -b -o cat | grep '\[ULTIMA\]'`.

## Repli : moteur X11 (`overlay/x11/`)
Fenêtre override-redirect ARGB, région d'entrée vide (XFixes), au-dessus du jeu plein écran (prouvé),
pas de vrai flou. Bascule : `ultima-x11 toggle` (raccourci GNOME personnalisé Super+Maj+U).

## Règles
- Le panneau ne lit jamais le jeu : il affiche l'état publié par le daemon.
- Tout réglage visuel se valide sur capture réelle (`tools/proof/capture-composed.py`) PENDANT une
  fenêtre de test convenue avec Mio (jamais sur son bureau sans accord).
- FPS ≥ 55 : non encore mesuré (compteur Steam absent) → à instrumenter.
