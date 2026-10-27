#!/usr/bin/env bash
# Vérifie que le mod DÉPLOYÉ est bien le dernier build (piège vécu : tester un DLL périmé).
set -uo pipefail
cd "$(dirname "$0")/.."

GAME="${STS2_GAME:-}"
if [ -z "$GAME" ]; then
  for c in \
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2" \
    "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2" \
    "$HOME/.steam/steam/steamapps/common/Slay the Spire 2"; do
    [ -d "$c" ] && GAME="$c" && break
  done
fi
[ -n "$GAME" ] || { echo "Jeu introuvable (STS2_GAME=...)"; exit 2; }

SRC="Ultima/bin/Release/net9.0/Ultima.dll"
DST="$GAME/mods/Ultima/Ultima.dll"

[ -f "$SRC" ] || { echo "❌ pas de build: $SRC absent → bash tools/build.sh"; exit 1; }
[ -f "$DST" ] || { echo "❌ mod non déployé → bash tools/deploy.sh"; exit 1; }

s=$(stat -c %Y "$SRC"); d=$(stat -c %Y "$DST")
echo "build   : $(date -d @$s +%H:%M:%S)  $(stat -c %s "$SRC") o"
echo "déployé : $(date -d @$d +%H:%M:%S)  $(stat -c %s "$DST") o"

if [ "$s" -gt "$d" ]; then
  echo "❌ PÉRIMÉ : le build est plus récent que le déployé → bash tools/deploy.sh"
  exit 1
fi

# Le SDK Godot doit avoir généré le dispatch des callbacks (_Ready etc.).
if command -v ilspycmd >/dev/null 2>&1; then
  n=$(ilspycmd -t Ultima.Overlay.UltimaOverlay "$DST" 2>/dev/null | grep -cE "ScriptPath|HasGodotClassMethod" || true)
  if [ "${n:-0}" -lt 1 ]; then
    echo "❌ callbacks Godot ABSENTS du DLL (Microsoft.NET.Sdk au lieu de Godot.NET.Sdk)"
    exit 1
  fi
  echo "✅ callbacks Godot présents ($n marqueurs)"
fi

echo "✅ déploiement à jour"
