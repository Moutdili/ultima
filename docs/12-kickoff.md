# 12 — Kickoff Claude Code (Fable, effort max)

## Lancement

Sur **fedora** (machine de jeu) :

```bash
cd ~/sts2
claude          # puis approuver le MCP "rea" au premier lancement
```

Au premier démarrage, Claude Code demande d'approuver le serveur MCP `rea` (portée projet).
Répondre **oui**. Vérifier ensuite : `claude mcp list` → `rea ... ✔ Connected`.

Les 6 skills sont déjà dans `~/.claude/skills/` et se chargent automatiquement :
`sts2-mod-dev`, `sts2-game-api`, `sts2-combat-math`, `sts2-multiplayer-net`,
`sts2-overlay-ui`, `sts2-decompile-rea`.

## Prompt à coller (une seule fois)

```
Tu travailles dans ~/sts2 sur ULTIMA, un deck tracker augmenté pour Slay the Spire 2.

Lis dans cet ordre :
1. AGENT-MANUAL.md
2. docs/00-brief.md
3. docs/05-feature-spec.md
4. docs/06-architecture.md
5. docs/10-source-map.md
6. docs/11-api-catalog.md

Contexte vérifié : jeu v0.107.1, Godot 4.5.1, C# net9.0, source original dans
game/pck/src/Core. Le scaffold Ultima/ compile déjà (dotnet build OK) et est déployé
dans le dossier mods du jeu. La toolchain est user-space (~/.dotnet, ~/tools/gdre).

Objectif : livrer les features F1 (DamageForecast) et F2 (LethalIndex) d'abord,
en read-only strict, avec UI compacte. Puis F3/F4 (multi + broadcast), puis F5/F6.

Méthode imposée :
- Avant d'écrire une API, la vérifier par `rg` dans game/pck/src/Core et citer
  fichier:ligne en commentaire.
- Logique métier dans Ultima/Core/ SANS using Godot ni MegaCrit (testable).
- Accès au jeu uniquement dans Ultima/GameApi/.
- Chaque patch Harmony dans un try/catch avec log fichier.
- Après chaque étape : `bash tools/dev.sh`, puis me dire quoi tester en jeu.

Commence par l'étape 1 (VersionProbe + lecture d'état loggée), montre-moi le diff,
et attends mon feu vert avant l'étape 2.
```

## Découpage des étapes (à garder dans cet ordre)

| # | Livrable | Preuve attendue |
|---|---|---|
| 1 | `GameApi/VersionProbe` + `CombatReader` (log de l'état) | log `ULTIMA state: ...` en combat |
| 2 | `UltimaOverlay` V1 (panneau + hotkey F8) | overlay visible, click-through |
| 3 | `Core/DamageForecast` + tests console | valeurs identiques à l'affichage du jeu |
| 4 | `Core/LethalIndex` + tests console | `16x3` sur 35 PV → 3e coup |
| 5 | Badges in-game F1/F2 | capture d'écran en combat |
| 6 | `TurnPriority` (local) | badge sur carte glow gold |
| 7 | `INetMessage` + handshake | 2 clients, alerte reçue |
| 8 | `RunScore` + CSV | fichier CSV relisible |
| 9 | `PathRisk` | notes par embranchement sur la map |

## Garde-fous à répéter à Claude

- **Ne pas** modifier le gameplay, même « pour tester ».
- **Ne pas** committer `game/`, `src/Core`, ni les assets du jeu (IP MegaCrit).
- **Ne pas** patcher une méthode dont la signature n'a pas été vérifiée dans le source.
- **Ne pas** ajouter de dépendance NuGet sans accord (le mod doit rester autonome).
- En cas de doute sur une API : `rg`, puis lire l'appelant vanilla.

## Ce qui est déjà prouvé (ne pas refaire)

- `dotnet build` du scaffold : **0 warning, 0 erreur**.
- Manifest + DLL déployés dans `<jeu>/mods/Ultima/`.
- Décompilation : `game/pck/src/Core` (3 400 fichiers .cs) + `game/csharp`.
- Catalogue : 578 cartes, 123 monstres (HP + moves résolus), 30 pouvoirs,
  34 cartes à payoff conditionnel.
- MCP REA : 229 outils, `mcp doctor` vert.
- `tools/doctor.sh` : 14/14.
