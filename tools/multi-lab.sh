#!/usr/bin/env bash
# Lance 2 instances STS2 en multi LOCAL (FastMP, port 33771) pour tester le broadcast ULTIMA.
# Usage : bash tools/multi-lab.sh [host|join]
#   host  : instance hôte seule (--fastmp=host_standard)
#   join  : client seul (--fastmp=join --clientId=1001)
#   (vide): les deux, logs séparés, Ctrl-C pour tout arrêter.
set -uo pipefail

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
EXE="$GAME/SlayTheSpire2"
[ -x "$EXE" ] || { echo "exe introuvable: $EXE"; exit 2; }

LAB="${HOME}/ultima-lab"; mkdir -p "$LAB"
LOGDIR="$GAME/mods/Ultima"

port_busy() { (ss -ltn 2>/dev/null || netstat -ltn 2>/dev/null) | grep -q ":33771 "; }

if port_busy; then
  echo "⚠️  port 33771 déjà occupé (une instance tourne ?)."
fi

echo "Jeu      : $GAME"
echo "Logs mod : $LOGDIR/ultima.log"
echo "⚠️  Les deux instances partagent le même dossier user:// (profils/saves)."
echo "    Si conflit de profil : lancer le client avec un HOME différent."
echo

start_host() { ( cd "$GAME" && "$EXE" --fastmp=host_standard >"$LAB/host.out" 2>&1 ) & echo "host PID $!"; }
start_join() { ( cd "$GAME" && "$EXE" --fastmp=join --clientId=1001 >"$LAB/client.out" 2>&1 ) & echo "client PID $!"; }

case "${1:-both}" in
  host) start_host ;;
  join) start_join ;;
  both)
    start_host
    sleep 6            # laisser l'hôte ouvrir le port 33771
    start_join
    echo
    echo "En combat : F7 sur une instance → l'autre doit afficher 'PING from …'."
    echo "Ctrl-C pour arrêter les deux."
    trap 'pkill -f "fastmp=" 2>/dev/null; echo; echo "instances arrêtées"' INT TERM
    wait
    ;;
  *) echo "usage: $0 [host|join]"; exit 1 ;;
esac
