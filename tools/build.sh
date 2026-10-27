#!/usr/bin/env bash
# Build du mod en Release et staging dans build/mods/Ultima/
set -euo pipefail
cd "$(dirname "$0")/.."

DATA="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64"
export DOTNET_ROOT="$HOME/.dotnet"
DOTNET="$HOME/.dotnet/dotnet"

"$DOTNET" build Ultima/Ultima.csproj -c Release -p:Sts2DataDir="$DATA" "$@"

OUT="build/mods/Ultima"
mkdir -p "$OUT"
# Ultima.dll + toutes les dépendances managées produites (Ultima.Core.dll).
# Les assemblies du jeu ne sont PAS copiées (Private=false) : elles viennent du jeu.
cp -f Ultima/bin/Release/net9.0/Ultima.dll "$OUT/"
cp -f Ultima/bin/Release/net9.0/Ultima.Core.dll "$OUT/" 2>/dev/null || true
cp -f Ultima/Ultima.json "$OUT/"
echo "→ staging: $(pwd)/$OUT"
ls -l "$OUT"
