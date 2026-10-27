#!/usr/bin/env bash
# Copie le staging dans le dossier mods du jeu (Flatpak) et affiche le log.
set -euo pipefail
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
[ -n "$GAME" ] || { echo "Jeu introuvable. Passer STS2_GAME=/chemin/vers/Slay the Spire 2"; exit 1; }
DEST="$GAME/mods/Ultima"

[ -d build/mods/Ultima ] || { echo "build/mods/Ultima absent → lancer tools/build.sh"; exit 1; }

mkdir -p "$DEST"
cp -f build/mods/Ultima/* "$DEST/"
echo "→ déployé dans $DEST"
ls -l "$DEST"

if [ -f "$DEST/ultima.log" ]; then
  echo "--- tail ultima.log ---"
  tail -20 "$DEST/ultima.log"
fi
