#!/usr/bin/env bash
# Banc de preuve ISOLÉ pour ultima@mio : un vrai GNOME Shell 50 en --headless, sur un bus
# D-Bus privé (dbus-run-session), avec XDG_* privés (dconf, extensions, runtime) et un
# moniteur virtuel 1920x1080. Ne touche PAS la session de Mio : rien n'est installé ni activé
# dans ~/.local/share/gnome-shell, aucune clé dconf réelle n'est écrite.
#
# Substitut du jeu : tools/selftest/fake-game.js (fenêtre X11 plein écran via XWayland).
# Pilote : tools/selftest/ultima-selftest@mio (pointeur et clavier virtuels, captures).
#
#   overlay/gnome/tools/headless-proof.sh [DOSSIER_SORTIE]
# Sortie : selftest.json, shots/*.png, shell.log, game.log
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GNOME_DIR="$(cd "$HERE/.." && pwd)"
OUT="$(realpath -m "${1:-$(mktemp -d -t ultima-headless.XXXXXX)}")"

if [[ "${ULTIMA_HEADLESS_INNER:-}" != 1 ]]; then
    # Règle du chef (2026-10-28) : invisible pour Mio (aucune fenêtre, aucune capture de SON
    # écran), mais lance un compositeur et des entrées virtuelles : uniquement dans une fenêtre
    # de test convenue avec lui.
    if [[ "${ULTIMA_HEADLESS_OK:-}" != 1 ]]; then
        echo "Refus : définir ULTIMA_HEADLESS_OK=1 seulement pendant une fenêtre de test convenue avec Mio." >&2
        exit 2
    fi
    rm -rf -- "$OUT"
    mkdir -p "$OUT"/{run,data,config,cache,state,shots}
    chmod 700 "$OUT/run"
    # Environnement entièrement privé
    env -i HOME="$HOME" USER="${USER:-}" PATH="$PATH" LANG="${LANG:-C.UTF-8}" \
        XDG_RUNTIME_DIR="$OUT/run" XDG_DATA_HOME="$OUT/data" XDG_CONFIG_HOME="$OUT/config" \
        XDG_CACHE_HOME="$OUT/cache" XDG_STATE_HOME="$OUT/state" \
        XDG_DATA_DIRS="/usr/local/share:/usr/share" \
        ULTIMA_SELFTEST_OUT="$OUT" ULTIMA_HEADLESS_INNER=1 \
        XDG_SESSION_ID=ultima-headless-none \
        dbus-run-session -- "$0" "$OUT" 2>"$OUT/dbus.log" || true
    echo "== résultats : $OUT"
    python3 - "$OUT/selftest.json" <<'PY'
import json, sys
try:
    r = json.load(open(sys.argv[1]))
except Exception as e:
    print("selftest.json illisible :", e); sys.exit(1)
ok = sum(c["ok"] for c in r["checks"]); n = len(r["checks"])
for c in r["checks"]:
    print(("OK   " if c["ok"] else "FAIL ") + c["name"] + ("  — " + c["detail"] if c["detail"] else ""))
for note in r["notes"]:
    print("note:", note)
print(f"{ok}/{n} vérifications")
sys.exit(0 if ok == n and n > 0 else 1)
PY
    exit $?
fi

# ---------- à l'intérieur du bus privé ----------
cleanup() {
    [[ -n "${GAME_PID:-}" ]] && kill "$GAME_PID" 2>/dev/null || true
    [[ -n "${SHELL_PID:-}" ]] && kill "$SHELL_PID" 2>/dev/null || true
    wait 2>/dev/null || true
}
trap cleanup EXIT

"$GNOME_DIR/install-dev.sh" >"$OUT/install.log"
cp -r "$HERE/selftest/ultima-selftest@mio" "$XDG_DATA_HOME/gnome-shell/extensions/"

gsettings set org.gnome.shell disable-user-extensions false
gsettings set org.gnome.shell enabled-extensions "['ultima@mio', 'ultima-selftest@mio']"
gsettings set org.gnome.shell welcome-dialog-last-shown-version '999'
# Jamais de verrouillage dans l'instance de test (elle ne doit pas toucher logind ;
# XDG_SESSION_ID inexistant => pas de proxy de session, donc pas de SetLockedHint).
gsettings set org.gnome.desktop.screensaver lock-enabled false
gsettings set org.gnome.desktop.session idle-delay 0
echo "dconf privé : $(ls "$XDG_CONFIG_HOME/dconf" 2>/dev/null)" >"$OUT/env.log"

gnome-shell --headless --wayland --virtual-monitor 1920x1080 >"$OUT/shell.log" 2>&1 &
SHELL_PID=$!

# Attendre l'affichage X11 public (XWayland) annoncé par Mutter
XDISP=""
for _ in $(seq 1 300); do
    XDISP="$(grep -oE 'Using public X11 display :[0-9]+' "$OUT/shell.log" | grep -oE ':[0-9]+' | head -1 || true)"
    [[ -n "$XDISP" ]] && break
    kill -0 "$SHELL_PID" 2>/dev/null || { echo "gnome-shell s'est arrêté" >&2; exit 1; }
    sleep 0.1
done
[[ -n "$XDISP" ]] || { echo "pas d'affichage X11 annoncé" >&2; exit 1; }
XAUTH=""
for _ in $(seq 1 100); do
    XAUTH="$(ls "$XDG_RUNTIME_DIR"/.mutter-Xwaylandauth.* 2>/dev/null | head -1 || true)"
    [[ -n "$XAUTH" ]] && break
    sleep 0.1
done
echo "DISPLAY=$XDISP XAUTHORITY=$XAUTH" >>"$OUT/env.log"

DISPLAY="$XDISP" XAUTHORITY="$XAUTH" GDK_BACKEND=x11 gjs -m "$HERE/selftest/fake-game.js" >"$OUT/game.log" 2>&1 &
GAME_PID=$!

for _ in $(seq 1 1200); do
    [[ -f "$OUT/selftest-done" ]] && break
    kill -0 "$SHELL_PID" 2>/dev/null || { echo "gnome-shell s'est arrêté" >&2; break; }
    sleep 0.1
done
