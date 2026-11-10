# ULTIMA — tracker externe pour Slay the Spire 2

Compagnon de jeu pour Slay the Spire 2 (Early Access, `v0.107.1`) sur Fedora GNOME, dans l'esprit de
Hearthstone Deck Tracker : il **observe** le jeu de l'extérieur et affiche des notes et des conseils par-dessus.
Ce n'est pas un mod : rien n'est injecté dans le jeu, rien n'y est écrit.

## Ce que fait ULTIMA
- **Notes S à F explicables** pour les cartes d'une récompense, les articles du marchand, le retrait, les
  options d'événement et le deck, avec les termes chiffrés et leur source.
- **Deck, reliques, potions, malédictions, cartes enchantées** de la run en cours, et ceux des alliés en co-op.
- **Chemins et route conseillée** sur la carte de l'acte, avec le risque estimé.
- **Revue de run** après coup : chaque choix comparé à la meilleure option.
- **Personnalisation** : thèmes, polices, disposition, modules déclaratifs et plugins.

Règle de fond : **probabiliste seulement**. ULTIMA n'utilise que ce que le joueur voit à l'écran, jamais les
tirages cachés (ordre de la pioche, rencontres à venir, graine).

## Architecture
```
saves + godot.log + mémoire du jeu (lecture seule)
        │
   Ultima.Saves   Ultima.Mirror   Ultima.Knowledge
        └──────────────┬──────────────┘
                 Ultima.Daemon ──► Ultima.Engine (notes, chemins, revue)
                       │
        state.json + state.sock  ($XDG_RUNTIME_DIR/ultima)
        ┌──────────────┼──────────────┐
   overlay/gnome   tools/console   overlay/companion
   (HUD au-dessus  (commande et    (fenêtre GTK4)
    du jeu)         TUI `ultima`)
```
Le contrat entre le daemon et les surfaces est dans `Ultima.Engine/Contracts/` et décrit par
`docs/22-contrat-moteur.md`, `docs/44-personnalisation.md`, `docs/46-contrat-v15.md` et `docs/51-contrat-v16.md`.

## Installer (Fedora, sans sudo)
```bash
tools/install.sh --check     # vérifie les prérequis, n'écrit rien
tools/install.sh             # installe sous ~/.local (daemon, miroir, console, extension, données)
ultima doctor                # diagnostic
ultima                       # tableau de bord dans le terminal
tools/install.sh --uninstall # retire exactement ce qui a été installé
```
La lecture mémoire demande `kernel.yama.ptrace_scope = 0`. Sans elle, ULTIMA fonctionne en mode dégradé à
partir des sauvegardes seules et l'annonce.

## Développer
```bash
tools/verify-all.sh          # porte : .NET, compagnon, HUD, console ; doit sortir 0 avant tout push
bash tools/check-offline.sh  # un clone seul suffit à compiler et tester le moteur, hors réseau
```
- Données lues par le moteur : `data/` (commitées, budget 2 Mo, voir `data/README.md`). Les gros corpus de
  backtest ne sont pas versionnés.
- Plan de travail en cours : `docs/45-plan-hermes/` (commencer par `00-LISEZMOI.md`). État : `STATUS.md`.
- La décompilation du jeu (`game/`) n'est jamais commitée ; `tools/decompile.sh` la reproduit localement.

## Héritage
Le projet a commencé comme un mod en jeu (dossiers `Ultima/`, `Ultima.Tests/`, `Makefile`, `containers/`,
`TEST-NOW.md`, `SPEC.md`, `AGENT-MANUAL.md`, docs 00 à 19). Cette approche est abandonnée depuis
`docs/20-pivot-externe.md` ; ces fichiers ne décrivent plus le produit.

## Avertissement
Recherche et interopérabilité sur un jeu possédé. ULTIMA lit, il ne modifie rien. Slay the Spire 2 appartient à
Mega Crit.
