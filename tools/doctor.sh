#!/usr/bin/env bash
# Vérifie toolchain, jeu, source décompilée, mod déployé.
set -uo pipefail
ok=0; ko=0
chk() { if eval "$2" >/dev/null 2>&1; then printf "  ✅ %s\n" "$1"; ok=$((ok+1)); else printf "  ❌ %s\n" "$1"; ko=$((ko+1)); fi; }
info() { printf "  ·  %s\n" "$1"; }

GAME="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2"
DATA="$GAME/data_sts2_linuxbsd_x86_64"

echo "== Machine =="
info "host=$(hostname) user=$(whoami)"
info "espace libre /home: $(df -h "$HOME" | tail -1 | awk '{print $4}')"

echo "== Toolchain =="
chk "dotnet SDK" "~/.dotnet/dotnet --list-sdks 2>/dev/null | grep -q ."
chk "ilspycmd" "~/.dotnet/tools/ilspycmd --version"
chk "GDRE tools" "test -x ~/tools/gdre/gdre_tools"
chk "node >= 22" "node --version"
chk "java" "java -version"

echo "== Jeu =="
chk "racine jeu" "test -d '$GAME'"
chk "sts2.dll" "test -f '$DATA/sts2.dll'"
chk "SlayTheSpire2.pck" "test -f '$GAME/SlayTheSpire2.pck'"
chk "0Harmony.dll" "test -f '$DATA/0Harmony.dll'"
chk "GodotSharp.dll" "test -f '$DATA/GodotSharp.dll'"
info "version jeu: $(grep -m1 '"version"' "$GAME/release_info.json" 2>/dev/null | cut -d'"' -f4)"

echo "== Décompilation =="
chk "source original (pck/src)" "test -d '$HOME/sts2/game/pck/src/Core'"
chk "source ILSpy" "test -d '$HOME/sts2/game/csharp'"
info "fichiers .cs source: $(find "$HOME/sts2/game/pck/src" -name '*.cs' 2>/dev/null | wc -l)"

echo "== Mod =="
chk "dossier mods/Ultima" "test -d '$GAME/mods/Ultima'"
chk "Ultima.dll déployé" "test -f '$GAME/mods/Ultima/Ultima.dll'"
info "log: $GAME/mods/Ultima/ultima.log"

echo
echo "== Résultat : $ok ok / $ko manquants =="
[ "$ko" -eq 0 ] || echo "→ lancer tools/install-toolchain.sh ou tools/decompile.sh pour compléter."
