# 09 — Runbook Fedora

## Prérequis machine

- Fedora, session utilisateur `mio`, **pas de sudo** (installer en user-space).
- Jeu Slay the Spire 2 installé via **Flatpak Steam**.
- Tailscale (accès depuis fedora-hp).

## Installation de la toolchain (une fois)

```bash
bash tools/install-toolchain.sh
# → ~/.dotnet (SDK 9 + 10), ilspycmd, ~/tools/gdre
```

Vérification :

```bash
~/.dotnet/dotnet --version                  # 9.x / 10.x
~/.dotnet/tools/ilspycmd --version
~/tools/gdre/gdre_tools --gdre-version      # GDRE v2.7.4
```

## Décompilation (une fois, ~10 min)

```bash
tmux new -s decomp 'bash tools/decompile.sh'
# suivi :
tmux attach -t decomp
```

Sorties : `game/pck/` (source original) et `game/csharp/` (ILSpy).

## Indexation du contenu

```bash
python3 tools/index_source.py --src game/pck/src/Core --out data/catalog
# → data/catalog/*.json + docs/11-api-catalog.md
```

## Développement du mod

```bash
bash tools/build.sh     # dotnet build -c Release
bash tools/deploy.sh    # copie build/mods/Ultima → <jeu>/mods/Ultima
bash tools/dev.sh       # les deux + tail du log
```

Lancer le jeu (Steam → Slay the Spire 2). Le log du mod :
`<racine jeu>/mods/Ultima/ultima.log`.

## Test multi (2 instances locales)

```bash
# terminal 1 : lancer le jeu normalement (host)
# terminal 2 : deuxième instance (si le jeu l'autorise via Steam, sinon 2e machine)
```

À défaut de 2 comptes Steam : tester le multi avec un ami sur le tailnet, ou valider
la sérialisation des messages en unit test (Core/Net testable sans réseau).

## Pièges Flatpak

| Symptôme | Cause | Fix |
|---|---|---|
| Mod non listé | dossier `mods/` absent | `mkdir -p "<racine jeu>/mods/Ultima"` |
| Dll non chargée | manifest `has_dll:false` ou nom ≠ id | nom de fichier = `<id>.dll` |
| Config inaccessible | chemin `%APPDATA%` codé en dur | utiliser `user://` ou dossier du mod |
| Permission refusée | fichier root-owned (si install via sudo) | `chown -R mio:` sur le dossier mod |

## Récupérer les infos de version

```bash
G="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2"
cat "$G/release_info.json"
```

À logger au démarrage du mod : si la version change, les patches Harmony doivent être revus.
