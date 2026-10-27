#!/usr/bin/env bash
# Boucle courte : build + deploy + tail du log du mod.
set -euo pipefail
cd "$(dirname "$0")/.."
bash tools/build.sh "$@"
bash tools/deploy.sh
GAME="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2"
LOG="$GAME/mods/Ultima/ultima.log"
echo "--- log (Ctrl-C pour quitter) ---"
[ -f "$LOG" ] && tail -f "$LOG" || echo "pas encore de log : lance le jeu puis entre en combat."
