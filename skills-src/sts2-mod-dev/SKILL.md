---
name: sts2-mod-dev
description: Use when building, deploying or debugging a Slay the Spire 2 C# mod on Fedora (dotnet build, manifest, mods folder, log). Covers the exact paths, the build/deploy loop, and the read-only rule.
---

# STS2 mod dev (Fedora, Flatpak)

## Vérité terrain

```
GAME=~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2
DATA=$GAME/data_sts2_linuxbsd_x86_64
mods=$GAME/mods/Ultima/{Ultima.json,Ultima.dll}
```

## Boucle

```bash
cd ~/sts2
bash tools/build.sh     # dotnet build Release, refs sts2.dll + 0Harmony + GodotSharp
bash tools/deploy.sh    # copie build/mods/Ultima -> $GAME/mods/Ultima
bash tools/dev.sh       # les deux + tail ultima.log
```

Ne jamais lancer `dotnet` du PATH système : utiliser `~/.dotnet/dotnet` (SDK 9/10 installés
en user-space, pas de sudo sur cette machine).

## Manifest

`<id>.json` à côté de `<id>.dll`. L'`id` doit matcher les noms de fichiers.
`affects_gameplay: false` pour un mod read-only (utilisable en multi).

## Règles

0. **Aucun `.json` dans `mods/` sauf le manifest** : le loader parse *tout* `.json` comme
   un manifeste. Config en `.cfg`/`.ini`, ou hors du dossier scanné.

1. **Read-only** : ne jamais muter l'état de jeu (pas de `CardPileCmd`, `DamageCmd`,
   `PowerCmd.Apply` dans le code du tracker). Observer seulement.
2. **Try/catch dans chaque patch Harmony** + log fichier : un mod ne crashe jamais le jeu.
3. Toute API utilisée doit être vérifiée dans `~/sts2/game/pck/src/Core` (source original).
4. Noter la version du jeu testée (`release_info.json`) dans le log au boot.

## Diagnostic

| Symptôme | Vérifier |
|---|---|
| Mod absent du menu | manifest invalide (JSON), id ≠ nom de fichier, `has_dll` |
| `Initialize` pas appelé | attribut `[ModInitializer(nameof(X))]` sur classe statique |
| Crash au chargement | patch Harmony sur un type/méthode renommé → try/catch + log |
| Rien dans le log | chemin du log : `$GAME/mods/Ultima/ultima.log` |

`bash tools/doctor.sh` donne l'état complet (toolchain, jeu, source, mod).
