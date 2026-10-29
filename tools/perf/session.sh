#!/usr/bin/env bash
# session.sh — outillage du CHEF pendant une fenêtre de mesure de FPS (docs/perf/MESURE.md).
# Ne lance jamais le jeu, n'ouvre aucune fenêtre, ne simule aucune entrée, n'écrit jamais dans
# le dossier du jeu ni dans ses données (godot.log est seulement LU, comme le fait le daemon).
#
#   session.sh init                 crée ~/sts2-proofs/perf/<date>-<heure>/ (+ lien « current »),
#                                   y écrit info.txt (versions, alimentation, extension, MangoHud)
#   session.sh launch-option        affiche l'option de lancement Steam exacte à coller par Mio
#   session.sh mark CONDITION       horodate le début d'une condition A/B (marks.tsv)
#   session.sh godot                suit godot.log (focus, bridage 30 FPS, cartes jouées, combat)
#                                   et horodate chaque ligne dans godot-events.tsv (Ctrl+C pour finir)
#   session.sh godot-start | godot-stop   même suivi, détaché (pour un shell non interactif)
#   session.sh collect              copie les CSV MangoHud écrits depuis « init » dans csv/
#   session.sh analyze [OPTIONS]    lance analyze-frametimes.py sur la session courante
#
# Variables : ULTIMA_PERF_ROOT (défaut ~/sts2-proofs/perf),
#             MANGOHUD_OUT (défaut ~/.var/app/com.valvesoftware.Steam/ultima-perf : dossier où
#             MangoHud écrit ; même chemin vu de l'intérieur du bac à sable Flatpak/pressure-vessel),
#             GODOT_LOG (défaut ~/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2/logs/godot.log)
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=${ULTIMA_PERF_ROOT:-$HOME/sts2-proofs/perf}
MH_OUT=${MANGOHUD_OUT:-$HOME/.var/app/com.valvesoftware.Steam/ultima-perf}
GODOT_LOG=${GODOT_LOG:-$HOME/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2/logs/godot.log}
ORDER=sans-ultima,masque,rail-seul,panneau-ouvert,sans-flou,sans-unredirect,x11
TOGGLE_KEY=${MANGOHUD_TOGGLE:-Control_L+Shift_L+L}

cur() { [[ -d $ROOT/current ]] || { echo "pas de session : lancer « $0 init »" >&2; exit 2; }; echo "$ROOT/current"; }
now() { date +%FT%T.%3N; }

launch_option() {
  # log_interval=0 : une ligne par image ; no_display : pas de HUD (il coûterait lui-même du GPU) ;
  # toggle_logging : PAS de touche F (la rangée F du portable de Mio envoie des touches média).
  echo "MANGOHUD=1 MANGOHUD_CONFIG=no_display,log_interval=0,toggle_logging=$TOGGLE_KEY,output_folder=$MH_OUT %command%"
}

case ${1:-} in
  init)
    d="$ROOT/$(date +%F-%H%M%S)"; mkdir -p "$d/csv"
    ln -sfn "$d" "$ROOT/current"
    {
      echo "session perf ULTIMA — $(date '+%F %T %z')"
      echo "noyau : $(uname -r)"
      echo "gnome-shell : $(gnome-shell --version 2>/dev/null || echo '?') · mutter : $(rpm -q mutter 2>/dev/null || echo '?')"
      echo "mesa hôte : $(rpm -q mesa-vulkan-drivers 2>/dev/null || echo '?') · GL.default flatpak : $(flatpak info org.freedesktop.Platform.GL.default//26.08 2>/dev/null | awk '/Version:/{print $2}')"
      echo "MangoHud flatpak : $(flatpak info org.freedesktop.Platform.VulkanLayer.MangoHud//26.08 2>/dev/null | awk '/Version:/{print $2}' || echo 'ABSENT')"
      echo "alimentation : $(cat /sys/class/power_supply/A*/online 2>/dev/null | head -1 | sed 's/1/secteur/;s/0/BATTERIE/') · platform_profile : $(cat /sys/firmware/acpi/platform_profile 2>/dev/null || echo '?') · tuned : $(tuned-adm active 2>/dev/null | sed 's/^Current active profile: //')"
      echo "extension : $(gnome-extensions info ultima@mio 2>/dev/null | awk -F': ' '/State/{print $2}' || echo '?')"
      echo "jeu : $(pgrep -x SlayTheSpire2 >/dev/null && echo "en cours (pid $(pgrep -x SlayTheSpire2 | head -1))" || echo 'absent')"
      echo "dossier MangoHud : $MH_OUT $( [[ -d $MH_OUT ]] && echo '(existe)' || echo '(ABSENT : Mio doit le créer, voir MESURE.md)')"
      echo "option de lancement attendue : $(launch_option)"
    } | tee "$d/info.txt"
    printf '# iso_local\tcondition\n' >"$d/marks.tsv"
    echo "$d" ;;
  launch-option) launch_option ;;
  mark)
    [[ -n ${2:-} ]] || { echo "usage : $0 mark CONDITION" >&2; exit 2; }
    d=$(cur); t=$(now); printf '%s\t%s\n' "$t" "$2" >>"$d/marks.tsv"; echo "marque $t → $2" ;;
  godot)
    d=$(cur); [[ -r $GODOT_LOG ]] || { echo "godot.log illisible : $GODOT_LOG" >&2; exit 2; }
    echo "suivi de $GODOT_LOG → $d/godot-events.tsv (Ctrl+C pour arrêter)" >&2
    tail -n0 -F "$GODOT_LOG" 2>/dev/null \
      | grep --line-buffered -E 'FocusOut|FocusIn|Limiting background FPS|Restored foreground FPS|playing card|Creating NCombatRoom|Preloading .Combat Room' \
      | while IFS= read -r l; do printf '%s\t%s\n' "$(now)" "$l" | tee -a "$d/godot-events.tsv"; done ;;
  godot-start)
    d=$(cur); setsid "$0" godot >/dev/null 2>"$d/godot-follow.err" < /dev/null &
    echo $! >"$d/godot-follow.pid"; echo "suivi de godot.log détaché (pid $(cat "$d/godot-follow.pid"))" ;;
  godot-stop)
    d=$(cur); [[ -s $d/godot-follow.pid ]] || { echo "aucun suivi en cours" >&2; exit 2; }
    pid=$(cat "$d/godot-follow.pid"); kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
    rm -f "$d/godot-follow.pid"; echo "suivi arrêté ; $(grep -c . "$d/godot-events.tsv" 2>/dev/null || echo 0) événement(s) horodaté(s)" ;;
  collect)
    d=$(cur); [[ -d $MH_OUT ]] || { echo "dossier MangoHud absent : $MH_OUT" >&2; exit 2; }
    n=0
    while IFS= read -r -d '' f; do cp -p -- "$f" "$d/csv/"; n=$((n+1)); done \
      < <(find "$MH_OUT" -maxdepth 1 -name '*.csv' -newer "$d/info.txt" -print0)
    echo "$n fichier(s) copié(s) dans $d/csv (résumés *_summary.csv inclus, ignorés par l'analyse)" ;;
  analyze)
    d=$(cur); shift
    ev=(); [[ -s $d/godot-events.tsv ]] && ev=(--events "$d/godot-events.tsv")
    python3 "$HERE/analyze-frametimes.py" --marks "$d/marks.tsv" "${ev[@]}" --order "$ORDER" "$@" "$d/csv" \
      | tee "$d/analyse.md" ;;
  *) sed -n '2,21p' "$0"; exit 2 ;;
esac
