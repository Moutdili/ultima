#!/usr/bin/env bash
# capture-game-x11.sh — capture de la SEULE fenêtre du jeu via X11 (XWayland), sans overlay.
#
# Détection : parcourt _NET_CLIENT_LIST de la racine X (DISPLAY, défaut :0) et compare
# WM_CLASS / _NET_WM_NAME à des regex (insensibles à la casse). Puis `import -window <id>`.
# Ne voit PAS ce qui est dessiné par le compositeur Wayland (overlay GNOME Shell, fenêtres
# Wayland natives) : pour « ce que Mio voit », utiliser capture-composed.py.
#
# Usage :
#   capture-game-x11.sh [--class RE] [--name RE] [--id 0xWIN] [--out-dir DIR] [--label TXT]
#   capture-game-x11.sh --list            # liste les fenêtres X11 clientes (id, pid, classe, titre)
# Défauts : --class 'slay the spire 2|slaythespire2|godot_engine'  (Godot 4 met
#   res_name=Godot_Engine, res_class=<nom du projet> ; NON vérifié sur le vrai jeu, cf. README).
# Sortie : chemin du PNG sur stdout ; diagnostics sur stderr.
# Codes : 0 OK, 2 aucune fenêtre trouvée, 3 plusieurs candidates (préciser), 4 import a échoué,
#         5 image unie (noire ?) — le fichier est gardé mais signalé.
set -euo pipefail

export DISPLAY="${DISPLAY:-:0}"
CLASS_RE='slay the spire 2|slaythespire2|godot_engine'
NAME_RE=''
WIN_ID=''
OUT_DIR="$HOME/sts2-proofs"
LABEL=''
LIST=0
CLASS_SET=0

die() { echo "[capture-game-x11] $1" >&2; exit "${2:-1}"; }
log() { echo "[capture-game-x11 $(date +%H:%M:%S)] $*" >&2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --class) CLASS_RE="$2"; CLASS_SET=1; shift 2 ;;
    --name) NAME_RE="$2"; shift 2 ;;
    --id) WIN_ID="$2"; shift 2 ;;
    --out-dir) OUT_DIR="$2"; shift 2 ;;
    --label) LABEL="-$2"; shift 2 ;;
    --list) LIST=1; shift ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) die "option inconnue : $1" 1 ;;
  esac
done

for t in xprop import magick; do command -v "$t" >/dev/null || die "outil manquant : $t" 1; done
xprop -root _NET_SUPPORTED >/dev/null 2>&1 || die "serveur X injoignable sur DISPLAY=$DISPLAY" 1

win_ids() { xprop -root _NET_CLIENT_LIST 2>/dev/null | grep -o '0x[0-9a-f]*' || true; }
prop() {  # prop <id> <ATOM> -> valeur brute après le '='
  xprop -id "$1" "$2" 2>/dev/null | sed -n 's/^[^=]*= //p' | head -1
}
describe() {
  local id=$1
  printf '%s\tpid=%s\tclass=%s\tname=%s\n' "$id" "$(prop "$id" _NET_WM_PID)" \
    "$(prop "$id" WM_CLASS)" "$(prop "$id" _NET_WM_NAME || true)"
}

if [[ $LIST -eq 1 ]]; then
  ids=$(win_ids)
  [[ -z "$ids" ]] && { log "aucune fenêtre X11 cliente (_NET_CLIENT_LIST vide)"; exit 0; }
  for id in $ids; do describe "$id"; done
  exit 0
fi

# --name seul : la regex de classe par défaut ne s'applique plus
[[ -n "$NAME_RE" && $CLASS_SET -eq 0 ]] && CLASS_RE=''

if [[ -z "$WIN_ID" ]]; then
  matches=()
  for id in $(win_ids); do
    cls=$(prop "$id" WM_CLASS); nm=$(prop "$id" _NET_WM_NAME); [[ -z "$nm" ]] && nm=$(prop "$id" WM_NAME)
    ok=1
    [[ -n "$CLASS_RE" ]] && ! grep -qiE "$CLASS_RE" <<<"$cls" && ok=0
    [[ -n "$NAME_RE" ]] && ! grep -qiE "$NAME_RE" <<<"$nm" && ok=0
    [[ $ok -eq 1 ]] && matches+=("$id")
  done
  [[ ${#matches[@]} -eq 0 ]] && die "aucune fenêtre X11 ne correspond (class~/$CLASS_RE/ name~/$NAME_RE/). Voir --list." 2
  if [[ ${#matches[@]} -gt 1 ]]; then
    for id in "${matches[@]}"; do describe "$id" >&2; done
    die "${#matches[@]} fenêtres candidates : préciser --id/--name" 3
  fi
  WIN_ID=${matches[0]}
fi

log "fenêtre retenue : $(describe "$WIN_ID")"
geom=$(prop "$WIN_ID" _NET_WM_STATE); log "_NET_WM_STATE=${geom:-<vide>}"
mkdir -p "$OUT_DIR"
out="$OUT_DIR/game-x11-$(date +%Y%m%d-%H%M%S-%3N)${LABEL}.png"
import -window "$WIN_ID" "$out" 2>&1 >&2 || die "import -window $WIN_ID a échoué" 4
[[ -s "$out" ]] || die "fichier vide : $out" 4

read -r w h mean sd < <(magick identify -format '%w %h %[fx:mean] %[fx:standard_deviation]\n' "$out" | head -1)
log "image ${w}x${h} moyenne=${mean} écart-type=${sd}"
echo "$out"
if awk -v s="$sd" 'BEGIN{exit !(s < 0.005)}'; then
  log "ATTENTION : image quasi unie (écart-type < 0.005) — fenêtre noire/non rendue ?"
  exit 5
fi
