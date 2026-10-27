#!/usr/bin/env bash
# Build du mod en Release et staging dans build/mods/Ultima/
set -euo pipefail
cd "$(dirname "$0")/.."

# Détection auto de l'install (surchargeable : STS2_GAME=/chemin)
GAME_DIR="${STS2_GAME:-}"
if [ -z "$GAME_DIR" ]; then
  for c in \
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2" \
    "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2" \
    "$HOME/.steam/steam/steamapps/common/Slay the Spire 2"; do
    [ -d "$c" ] && GAME_DIR="$c" && break
  done
fi
[ -n "$GAME_DIR" ] || { echo "Jeu introuvable. Passer STS2_GAME=/chemin/vers/Slay the Spire 2"; exit 1; }
DATA="$GAME_DIR/data_sts2_linuxbsd_x86_64"
# dotnet : PATH (conteneur) sinon install user-space de l'hote
DOTNET="$(command -v dotnet || true)"
[ -n "$DOTNET" ] || DOTNET="$HOME/.dotnet/dotnet"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

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
