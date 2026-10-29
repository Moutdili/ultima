---
name: sts2-performance
description: Use when measuring or optimizing the frame-rate impact of ULTIMA on Slay the Spire 2 (overlay compositing, blur, unredirect, pointer polling, daemon and memory-mirror reads) — MangoHud frametime logging, A/B protocol and budgets. Mio felt FPS drops when cards are drawn from the draw pile.
---

# ULTIMA — performance (exigence Mio)

## Symptôme rapporté (2026-10-29)
Baisses de FPS ressenties « quand les cartes sont tirées de la pioche », extension GNOME active
(rail + verre, `disable_unredirect`). Non mesuré.

## Suspects (à départager par MESURE, pas par intuition)
1. `global.compositor.disable_unredirect()` : le jeu plein écran perd le scanout direct → composition
   plein écran à chaque image (iGPU AMD 780M, 1920×1200).
2. `Shell.BlurEffect` : flou recalculé à chaque image où le décor derrière bouge (animations de pioche).
3. Sondage du pointeur 12,5 Hz (faible, à vérifier).
4. Daemon / miroir : lectures mémoire (doivent être absentes en combat).

## Mesure
- MangoHud (journal de temps par image) : Flatpak `org.freedesktop.Platform.VulkanLayer.MangoHud`
  (installation par Mio), option de lancement Steam `MANGOHUD=1 MANGOHUD_CONFIG=no_display,output_folder=…
  ,log_interval=0 %command%` ; démarrer/arrêter l'enregistrement (Maj_D+F2 par défaut, configurable).
- Scénario reproductible : même combat, 10 tours de pioche, conditions A/B : sans ULTIMA ; rail seul ;
  panneau ouvert ; sans flou ; sans `disable_unredirect` (si le rail reste visible) ; moteur X11.
- Indicateurs : FPS moyen, 1 % low, temps par image p99 pendant les pioches. Budget : ≤ 3 % de perte
  de 1 % low par rapport à « sans ULTIMA ».

## Leviers connus
Flou seulement panneau ouvert (rail = aplat) ; flou statique (capture figée à l'ouverture) ;
`disable_unredirect` seulement si nécessaire ; sondage suspendu quand masqué ; daemon `nice 10`.
