#!/usr/bin/env bash
# Chaîne de décompilation STS2 — install user-space, aucun sudo requis.
set -uo pipefail

TOOLS="$HOME/tools"
DOTNET_DIR="$HOME/.dotnet"
mkdir -p "$TOOLS" "$DOTNET_DIR"

echo "== 1/4 dotnet SDK 9 =="
if [ ! -x "$DOTNET_DIR/dotnet" ]; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$TOOLS/dotnet-install.sh"
  bash "$TOOLS/dotnet-install.sh" --channel 9.0 --install-dir "$DOTNET_DIR" --no-path
else
  echo "déjà installé"
fi
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$DOTNET_DIR/tools:$PATH"
"$DOTNET_DIR/dotnet" --version || { echo "FAIL dotnet"; exit 1; }

echo "== 2/4 ilspycmd =="
if [ ! -x "$DOTNET_DIR/tools/ilspycmd" ]; then
  "$DOTNET_DIR/dotnet" tool install --global ilspycmd 2>&1 | tail -5
fi
ls -l "$DOTNET_DIR/tools/ilspycmd" 2>/dev/null || echo "WARN ilspycmd absent"

echo "== 3/4 GDRE Tools =="
if [ ! -x "$TOOLS/gdre/gdre_tools" ]; then
  curl -fsSL -o "$TOOLS/gdre.zip" \
    https://github.com/GDRETools/gdsdecomp/releases/download/v2.7.4/GDRE_tools-v2.7.4-linux.zip
  rm -rf "$TOOLS/gdre" && mkdir -p "$TOOLS/gdre"
  unzip -q -o "$TOOLS/gdre.zip" -d "$TOOLS/gdre"
fi
chmod +x "$TOOLS"/gdre/* 2>/dev/null
find "$TOOLS/gdre" -maxdepth 2 -type f -name "gdre_tools*" -exec ls -l {} \;

echo "== 4/4 versions =="
"$TOOLS/gdre/gdre_tools" --gdre-version 2>&1 | head -3
echo "--- OK toolchain ---"
