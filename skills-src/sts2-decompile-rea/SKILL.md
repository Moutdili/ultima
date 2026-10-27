---
name: sts2-decompile-rea
description: Use when re-extracting Slay the Spire 2 sources/assets after a game patch, or when using REA (Reverse Engineer Anything) on sts2.dll and native binaries.
---

# Décompilation STS2 + REA

## Ré-extraction (après un patch du jeu)

```bash
cd ~/sts2
bash tools/decompile.sh --force     # GDRE (pck) + ILSpy (dll), idempotent, logs dans logs/
python3 tools/index_source.py --src game/pck/src/Core --out data/catalog --repo .
```

Détail des commandes dans `docs/03-decompile.md`.

## Ce que contient le .pck

`src/Core/**/*.cs` = **le code source C# original shippé par MegaCrit** (pas du pseudo-code).
C'est la référence. ILSpy ne sert que pour les assemblies sans source.

## REA (Reverse Engineer Anything)

MCP projet : `.mcp.json` (`npx -y rea-agents@latest mcp serve`).
Vérifier : `npx rea-agents@latest mcp doctor`.
Enregistrer : `npx rea-agents@latest mcp add --agent claude-code --no-global`.

Usage pertinent ici :
- .NET : `rea` inspecte `sts2.dll` (metadata, CIL) **sans moteur natif**.
- natif : `SlayTheSpire2`, `libfmod*`, `libsentry*` — nécessite Ghidra/Hopper/IDA.
- comparaison de versions : `compare-managed-members` entre deux `sts2.dll`.

Ne pas utiliser REA pour le code C# déjà présent dans le pck : c'est plus lent et moins
lisible que de lire `src/Core`.

## Pièges

- ilspycmd ≥ 11 exige le SDK 10 (`~/.dotnet` contient 9 et 10).
- GDRE livre `gdre_tools.x86_64` → symlink `gdre_tools` créé par `install-toolchain.sh`.
- GDRE écrit des barres de progression avec `\r` : ne jamais `tail` son log brut,
  filtrer avec `tr '\r' '\n' | tail`.
- Le .pck fait 1.9 Go → ~2.8 Go extraits ; prévoir l'espace.
