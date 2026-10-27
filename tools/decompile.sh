#!/usr/bin/env bash
# Décompilation complète de Slay the Spire 2 (v0.107.1) — tourne sur fedora.
set -uo pipefail

G="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2"
DLL="$G/data_sts2_linuxbsd_x86_64/sts2.dll"
PCK="$G/SlayTheSpire2.pck"
OUT="$HOME/sts2/game"
LOGS="$HOME/sts2/logs"
mkdir -p "$OUT" "$LOGS"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

echo "[$(date -Is)] START"
echo "game=$("$DOTNET_ROOT/dotnet" --version) ilspy=$(command -v ilspycmd)"

# --- 1. C# : sts2.dll -> projet ILSpy (source lisible, non obfusquée) ---
if [ ! -d "$OUT/csharp" ] || [ -z "$(ls -A "$OUT/csharp" 2>/dev/null)" ]; then
  echo "[$(date -Is)] ilspy: start"
  ilspycmd -p -o "$OUT/csharp" --nested-directories "$DLL" > "$LOGS/ilspy.log" 2>&1
  echo "[$(date -Is)] ilspy: exit=$? files=$(find "$OUT/csharp" -name '*.cs' 2>/dev/null | wc -l)"
else
  echo "[$(date -Is)] ilspy: déjà fait"
fi

# --- 2. PCK : projet Godot complet (scenes, assets, localization, src) ---
if [ ! -d "$OUT/pck" ] || [ -z "$(ls -A "$OUT/pck" 2>/dev/null)" ]; then
  echo "[$(date -Is)] gdre: start"
  ( cd "$HOME/tools/gdre" && ./gdre_tools --headless \
      "--recover=$PCK" \
      "--output=$OUT/pck" \
      "--csharp-assembly=$DLL" \
      --skip-loading-resource-strings ) > "$LOGS/gdre.log" 2>&1
  echo "[$(date -Is)] gdre: exit=$? files=$(find "$OUT/pck" -type f 2>/dev/null | wc -l)"
else
  echo "[$(date -Is)] gdre: déjà fait"
fi

echo "[$(date -Is)] DONE"
du -sh "$OUT"/* 2>/dev/null
