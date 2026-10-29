---
name: sts2-memory-mirror
description: Use when reading live Slay the Spire 2 state (card reward offers, shop inventory and prices, run state, combat) from the running game process with the read-only ClrMD mirror (Ultima.Mirror), like HearthMirror for Hearthstone Deck Tracker.
---

# ULTIMA — miroir mémoire en lecture seule (prouvé sur le vrai jeu, 2026-10-29)

## Ce qui est prouvé
- Attache ClrMD sur le processus `SlayTheSpire2` (Flatpak + Steam Linux Runtime) via la DAC du jeu
  lue par `/proc/<pid>/root/...libmscordaccore.so` ; runtime CoreCLR 9.0.725 == DAC (version + commit).
- Lecture par `process_vm_readv` uniquement : strace = 0 `ptrace`, 0 `process_vm_writev`. Jamais de suspension.
- Carte : PV/or/deck/reliques mémoire == save == écran (8/8).
- **Récompense : cartes proposées lues AVANT le clic** (3/3, ordre exact).
- **Shop : 15/15 prix** (cartes dont soldes, reliques, potions, retrait) ; achat suivi en direct.

## Usage
`build/mirror/ultima-mirror probe --sections run|reward|shop|combat [--compare-save]`
(env : `DOTNET_ROOT=$HOME/.dotnet`). Chemins d'objets : `docs/mirror/object-paths.md`.
`ultima-mirror layout` : vérifie les 56 champs après chaque patch (code 4 = dérive → mode dégradé fichiers).

## Règles absolues
- Lecture seule ; `ReadGuard` refuse les champs cachés interdits (rng, odds, sacs de reliques, ids pré-tirés,
  graine, ordre de la pioche) — décision Mio « probabiliste seulement ».
- Le daemon lance le miroir dans un processus ENFANT avec délai maximum (ClrMD peut planter côté lecteur).
- Vérification croisée avec la save à chaque salle ; divergence → mode dégradé annoncé à l'écran.
- `ptrace_scope` doit rester 0 (sinon refus) : le daemon le teste et l'explique.
