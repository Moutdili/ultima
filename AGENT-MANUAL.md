# AGENT MANUAL — ULTIMA (Slay the Spire 2)

> Ce fichier est le manuel d'opération pour l'agent (Claude Code) qui développe ULTIMA.
> Si tu veux qu'il soit chargé automatiquement au démarrage dans le repo :
> `ln -s AGENT-MANUAL.md CLAUDE.md` (à faire par Mio, pas par l'agent).

Tu construis **ULTIMA**, un deck tracker augmenté pour Slay the Spire 2, en C#/Godot/Harmony,
sur **Fedora**, jeu installé en Flatpak (build Linux natif).

## Règles dures

1. **Read-only gameplay.** Le mod observe et affiche. Il ne modifie jamais l'état, ne joue
   aucune carte, ne change aucun dégât. Conséquence : utilisable en multi même si un seul
   joueur l'a installé.
2. **Pas de patch large.** Ordre : override de modèle > hook sémantique (`Hook.*`,
   `ModHelper.SubscribeFor*Hooks`) > Harmony ciblé avec signature exacte.
3. **Vérifie avant d'affirmer.** Toute API doit exister dans `~/sts2/game/pck/src/Core`
   (source original) ou `~/sts2/game/csharp` (ILSpy). Cite fichier + ligne.
4. **Zéro hardcode de données de jeu.** Dégâts, multiplicateurs, coûts : lus dans les
   modèles (`DamageCalc`, `DynamicVars`, pouvoirs). Une valeur en dur = bug.
5. **Version.** Early Access `v0.107.1` au moment du dossier. Tout patch Harmony note la
   version testée.
6. **UI discrète.** Ne masque jamais la main ni les intents. Opacité repos 0.25-0.35,
   réveil au survol, hotkey de toggle.

## Environnement vérifié

```
Jeu (Flatpak) : ~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2
  exe         : SlayTheSpire2 (Linux natif, pas Proton)
  code compilé: data_sts2_linuxbsd_x86_64/sts2.dll (net9.0)
  source+assets: SlayTheSpire2.pck (1.9 Go) → extrait dans ~/sts2/game/pck
  deps        : 0Harmony.dll, GodotSharp.dll dans data_sts2_linuxbsd_x86_64/
  mods        : <racine jeu>/mods/<ModId>/{json,dll,pck}
Toolchain (user-space, PAS de sudo sur cette machine) :
  dotnet      ~/.dotnet/dotnet (SDK 9.0.320 + 10.0.403)
  ilspycmd    ~/.dotnet/tools/ilspycmd (11.2)
  GDRE Tools  ~/tools/gdre/gdre_tools (2.7.4)
  node/npm    /usr/bin/node v22.23.1
```

## Boucle de travail

```bash
bash tools/doctor.sh     # toolchain + jeu + mod déployé
bash tools/build.sh      # dotnet build → build/mods/Ultima/
bash tools/deploy.sh     # copie dans <jeu>/mods/Ultima + tail log
bash tools/dev.sh        # build + deploy + tail
```

## Carte du code

`docs/10-source-map.md` (systèmes) et `docs/11-api-catalog.md` (API vérifiées).
Ne devine jamais un nom : `rg "class NCombatUi" ~/sts2/game/pck/src/Core`.

## Les 4 features (détail : docs/05-feature-spec.md)

1. `DamageForecast` — dégâts par ennemi : actuel / Weak / Vulnerable / létal.
2. `LethalIndex` — sur `NxM`, quel coup passe sous 0 PV (block inclus).
3. `TurnPriority` — cartes « payoff si première carte du tour » + alerte multi.
4. `RunScore` + `PathRisk` — contribution dégâts, score de run, risque des embranchements.

## Pièges vérifiés (audits d'octobre 2026)

- **Aucun `.json` dans `mods/` sauf le manifest** : le loader parse tout `.json` comme manifeste.
  Config en `.cfg`/`.ini` ou hors du dossier scanné.
- **IDs de messages custom = dépendants du mod-set** : handshake obligatoire, mêmes mods des
  deux côtés. BaseLib fixe les IDs 128-254 pour cette raison ; on ne dépend pas de BaseLib.
- **Ne pas copier STS2MCP tel quel** : son GET mute l'état (`OpenInventory`, `ForceClick`).
- **999dulgi cible ≥ 0.110**, CombatSolver ≥ 0.111, nous sommes en **0.107.1** : revalider.
- Pipeline de dégâts : enchant → additif → multiplicatif → cap → block → HP. Block après tout.

## Anti-patterns déjà payés par la communauté

- Patcher les surcharges de `GetDescriptionForPile` → texte dupliqué.
- Remplacer `OnPlay` d'une carte pour observer → corrompt le pipeline de piles.
- `SetMouseIgnoreRecursive` sans réautoriser le bouton reset → overlay incliquable.
- ScrollContainer sans `SizeFlagsVertical=ExpandFill` → layout effondré.
- Config dans `%APPDATA%` → cassé sous Linux : utiliser `user://` ou le dossier du mod.
