# 03 — Playbook de décompilation reproductible

## Résultat attendu

```
~/sts2/game/
  pck/           projet Godot récupéré (source C# original + assets + localization)
  csharp/        projet ILSpy (assemblies sans source)
```

## Outils (user-space, aucun sudo)

| Outil | Version | Rôle |
|---|---|---|
| `dotnet` | SDK 9.0.320 + 10.0.403 dans `~/.dotnet` | runtime + build |
| `ilspycmd` | 11.2 (`~/.dotnet/tools`) | décompile `sts2.dll` |
| `gdre_tools` | 2.7.4 (`~/tools/gdre`) | extrait/recover le `.pck` |
| REA (`rea-agents`) | 6.8 via npx | analyse .NET/native avec preuve (MCP) |

Installation : `bash tools/install-toolchain.sh` (idempotent).
⚠️ ilspycmd récent exige SDK 10 : le script installe 9 **et** 10.
⚠️ GDRE livre `gdre_tools.x86_64` : le script crée le symlink `gdre_tools`.

## Commandes

```bash
# 1. Source C# original + assets
cd ~/tools/gdre
./gdre_tools --headless \
  --recover="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck" \
  --output="$HOME/sts2/game/pck" \
  --csharp-assembly="$HOME/.var/app/.../data_sts2_linuxbsd_x86_64/sts2.dll" \
  --skip-loading-resource-strings

# 2. Assemblies sans source (GodotPlugins, etc.)
ilspycmd -p -o ~/sts2/game/csharp --nested-directories \
  "$HOME/.var/app/.../data_sts2_linuxbsd_x86_64/sts2.dll"
```

`bash tools/decompile.sh` fait les deux, log dans `~/sts2/logs/`, et reprend là où il
s'est arrêté (idempotent). Il tourne dans tmux pour survivre à une déconnexion SSH.

## Coût réel mesuré

- PCK : ~2.8 Go extraits, 27 065 fichiers, quelques minutes (CPU-bound, load ~13).
- ILSpy : 3 423 fichiers `.cs`, ~26 Mo.
- Espace : prévoir ~6 Go (source + assets + build).

## Après extraction : indexation

```bash
python3 tools/index_source.py --src ~/sts2/game/pck/src/Core --out ~/sts2/data/catalog
```

Produit :
- `cards.json` (nom, classe, coût, type, rareté, personnage, texte localisé EN/FR)
- `monsters.json` (classe, actes, HP, moves + dégâts déclarés)
- `powers.json` (hooks de modification de dégâts)
- `first_card_cards.json` (cartes dont le payoff dépend d'être jouée en premier)
- `docs/11-api-catalog.md` (markdown lisible par l'agent)

## Mise à jour après un patch du jeu

1. `bash tools/decompile.sh --force` (relance GDRE + ILSpy)
2. `python3 tools/index_source.py` (réindexe)
3. `bash tools/diff_version.py` → liste les fichiers changés depuis l'ancienne version
4. Re-vérifier chaque patch Harmony (le doc impose de noter la version testée).

## REA : où il sert vraiment

REA excelle sur les **binaires natifs** (l'exe Godot, `libfmod`, `libsentry`) et les
assemblies .NET sans source. Pour STS2, le gros du code est déjà en clair dans le pck :
REA est utilisé en **second passage** pour :
- vérifier une signature côté binaire (`inspector` .NET : metadata/CIL),
- comparer deux versions du jeu (`compare-managed-members`),
- tracer une fonction native (crash, input, audio).

MCP : `.mcp.json` du repo. Vérification : `npx rea-agents mcp doctor`.
