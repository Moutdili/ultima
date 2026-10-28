#!/usr/bin/env bash
# no-write-audit.sh — audit des non-négociables docs/20 §1 (zéro fichier dans le dossier du
# jeu, lecture seule sur les données, profil modded/ intact). Cet outil n'écrit QUE dans
# ~/sts2-proofs (ou --log). Il n'écrit jamais dans les dossiers surveillés.
#
# Sous-commandes :
#   check  [--since 'YYYY-MM-DD HH:MM'] [--baseline F] [--write-baseline F]
#          Contrôle immédiat : mods/ absent ou vide ; aucun fichier *ultima* dans le jeu ou
#          les données ; modded/ non modifié depuis --since ; manifeste sha256 de modded/
#          identique à --baseline. Code 0 = PASS, 1 = FAIL.
#   watch  [--duration S] [--log F] [--paths 'DIR1:DIR2']
#          inotifywait -m -r sur le dossier du jeu + les données ; journal horodaté (µs) de
#          TOUTES les écritures (create/modify/close_write/moved/delete/attrib), avec pour
#          chaque événement les processus qui tiennent encore le fichier ouvert, et un
#          échantillonneur (1 s) des descripteurs EN ÉCRITURE ouverts par un processus ULTIMA
#          dans les arbres surveillés. Ctrl-C / SIGTERM / --duration → résumé.
#          Code 0 = aucune VIOLATION ni ALERTE, 1 sinon.
#   selftest   preuve de fonctionnement dans un dossier temporaire (aucun accès au jeu).
#
# Classes de lignes du journal :
#   EVENT     écriture observée (qui que ce soit — le jeu écrit ses saves/logs, c'est normal)
#   ALERTE    écriture dans le dossier d'installation du jeu ou dans un profil modded/
#   VIOLATION processus ULTIMA (cmdline ~ $ULTIMA_RE) détenteur d'un fd en écriture, ou
#             fichier dont le nom contient « ultima »
# Limite : inotify ne donne PAS le PID de l'écrivain. L'attribution est heuristique
# (détenteurs au moment de l'événement + échantillonnage 1 s). Une écriture ultra-brève par
# ULTIMA (open/write/close < 1 s) serait journalisée en EVENT sans attribution. Voir README.
set -uo pipefail

GAME_DIR="${STS2_GAME_DIR:-$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2}"
DATA_DIR="${STS2_DATA_DIR:-$HOME/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2}"
ULTIMA_RE="${ULTIMA_RE:-ultima}"
# processus d'outillage exclus de la détection ULTIMA (ils lisent, n'écrivent pas)
SELF_RE='no-write-audit|inotifywait|test-window|capture-composed|capture-game-x11|fake-fullscreen'
PROOFS="${STS2_PROOFS:-$HOME/sts2-proofs}"

log() { echo "[no-write-audit $(date +%H:%M:%S)] $*" >&2; }
now() { local t=$EPOCHREALTIME; printf '%(%F %T)T.%s' "${t%.*}" "${t#*.}"; }

# ---------------------------------------------------------------- utilitaires processus
cmdline_of() {  # cmdline_of <pid> -> cmdline (NUL -> espace), sans fork
  local a=()
  { mapfile -d '' -t a <"/proc/$1/cmdline"; } 2>/dev/null || return 1
  printf '%s' "${a[*]}"
}
is_ultima() {  # is_ultima <pid> -> 0 si processus ULTIMA (hors outillage d'audit)
  local cmd; cmd=$(cmdline_of "$1") || return 1
  [[ -z $cmd ]] && return 1
  shopt -s nocasematch
  [[ $cmd =~ $ULTIMA_RE && ! $cmd =~ $SELF_RE ]]; local rc=$?
  shopt -u nocasematch
  return $rc
}
ultima_pids() {  # PIDs ULTIMA (hors outillage et hors soi)
  local p
  for p in /proc/[0-9]*; do
    p=${p#/proc/}
    [[ $p == "$$" || $p == "$BASHPID" ]] && continue
    is_ultima "$p" && echo "$p"
  done
}
fd_mode() {  # fd_mode <pid> <fd> -> r | w | rw (d'après fdinfo flags)
  local fl
  fl=$(awk '/^flags:/{print $2}' "/proc/$1/fdinfo/$2" 2>/dev/null) || return
  [[ -z $fl ]] && return
  case $(( 8#$fl & 3 )) in 0) echo r ;; 1) echo w ;; *) echo rw ;; esac
}
tree_suffixes=()  # suffixes de chemin indépendants du namespace (le jeu Flatpak voit
                  # ~/.local/share/SlayTheSpire2 là où l'hôte voit ~/.var/app/.../SlayTheSpire2)
in_trees() {  # in_trees <chemin lu dans /proc> -> 0 si dans un arbre surveillé
  local s
  for s in "${tree_suffixes[@]}"; do [[ $1 == *"/$s"/* || $1 == *"/$s" ]] && return 0; done
  return 1
}
holders() {  # holders <chemin absolu> -> "pid:comm:mode,..." (processus de l'utilisateur)
  local path=$1 rel base out='' l p fd
  base=$(basename "$path")
  # on compare sur « <dernier composant de la racine>/<relatif> » (indépendant du namespace)
  for s in "${tree_suffixes[@]}"; do
    [[ $path == *"/$s/"* ]] && rel="$s/${path#*/"$s"/}" && break
  done
  rel=${rel:-$base}
  while IFS= read -r l; do
    p=${l#/proc/}; p=${p%%/*}; fd=${l##*/}
    out+="$p:$(cat "/proc/$p/comm" 2>/dev/null):$(fd_mode "$p" "$fd"),"
  done < <(find /proc/[0-9]*/fd -maxdepth 1 -lname "*/$rel" 2>/dev/null)
  echo "${out%,}"
}

# ---------------------------------------------------------------- check
cmd_check() {
  local since='' baseline='' wbase='' fail=0 n
  while [[ $# -gt 0 ]]; do
    case $1 in
      --since) since=$2; shift 2 ;;
      --baseline) baseline=$2; shift 2 ;;
      --write-baseline) wbase=$2; shift 2 ;;
      *) log "option inconnue : $1"; return 2 ;;
    esac
  done
  echo "# no-write-audit check — $(now)"
  echo "GAME_DIR=$GAME_DIR"
  echo "DATA_DIR=$DATA_DIR"
  [[ -d $GAME_DIR ]] || { echo "FAIL dossier du jeu introuvable"; return 1; }

  local mods="$GAME_DIR/mods"
  if [[ ! -e $mods ]]; then
    echo "PASS mods/ absent"
  elif [[ -d $mods && -z $(ls -A "$mods") ]]; then
    echo "PASS mods/ existe mais VIDE (mtime $(stat -c %y "$mods")) — reliquat v1 ; Mio peut faire : rmdir \"$mods\""
  else
    echo "FAIL mods/ NON VIDE :"; find "$mods" -mindepth 1 -printf '  %TY-%Tm-%Td %TH:%TM  %s o  %P\n'; fail=1
  fi

  n=$(find "$GAME_DIR" "$DATA_DIR" -iname '*ultima*' 2>/dev/null | wc -l)
  if [[ $n -eq 0 ]]; then echo "PASS aucun fichier *ultima* dans le jeu ni les données"
  else echo "FAIL $n fichier(s) *ultima* :"; find "$GAME_DIR" "$DATA_DIR" -iname '*ultima*' | sed 's/^/  /'; fail=1; fi

  local modded=() d
  for d in "$DATA_DIR"/steam/*/modded; do [[ -d $d ]] && modded+=("$d"); done
  if [[ ${#modded[@]} -eq 0 ]]; then
    echo "PASS aucun profil modded/"
  else
    for d in "${modded[@]}"; do
      echo "INFO modded/ présent : $d — dernière modif : $(find "$d" -printf '%TY-%Tm-%Td %TH:%TM:%TS %P\n' | sort | tail -1)"
      if [[ -n $since ]]; then
        n=$(find "$d" -newermt "$since" | wc -l)
        if [[ $n -eq 0 ]]; then echo "PASS modded/ non modifié depuis $since"
        else echo "FAIL $n entrée(s) de modded/ modifiée(s) depuis $since :"; find "$d" -newermt "$since" -printf '  %TY-%Tm-%Td %TH:%TM  %P\n'; fail=1; fi
      fi
    done
  fi

  local manifest
  manifest=$( { for d in "${modded[@]}"; do find "$d" -type f -printf '%TY-%Tm-%Td_%TT %s %p\n'; done; } | sort -k3 )
  if [[ -n $wbase ]]; then
    mkdir -p "$(dirname "$wbase")"
    { for d in "${modded[@]}"; do find "$d" -type f -exec sha256sum {} +; done; } | sort -k2 >"$wbase"
    printf '%s\n' "$manifest" >"$wbase.mtimes"
    echo "INFO manifeste de référence écrit : $wbase ($(wc -l <"$wbase") fichiers)"
  fi
  if [[ -n $baseline ]]; then
    local cur; cur=$( { for d in "${modded[@]}"; do find "$d" -type f -exec sha256sum {} +; done; } | sort -k2 )
    if diff <(cat "$baseline") <(printf '%s\n' "$cur" | sed '/^$/d') >/dev/null; then
      echo "PASS modded/ identique au manifeste $baseline (sha256)"
    else
      echo "FAIL modded/ diffère du manifeste $baseline :"; diff <(cat "$baseline") <(printf '%s\n' "$cur" | sed '/^$/d') | sed 's/^/  /'; fail=1
    fi
  fi
  [[ $fail -eq 0 ]] && echo "RESULTAT PASS" || echo "RESULTAT FAIL"
  return $fail
}

# ---------------------------------------------------------------- watch
cmd_watch() {
  local duration=0 logf='' paths=''
  while [[ $# -gt 0 ]]; do
    case $1 in
      --duration) duration=$2; shift 2 ;;
      --log) logf=$2; shift 2 ;;
      --paths) paths=$2; shift 2 ;;
      *) log "option inconnue : $1"; return 2 ;;
    esac
  done
  local roots=()
  if [[ -n $paths ]]; then IFS=: read -ra roots <<<"$paths"; else roots=("$GAME_DIR" "$DATA_DIR"); fi
  local r; for r in "${roots[@]}"; do [[ -d $r ]] || { log "racine absente : $r"; return 2; }; tree_suffixes+=("$(basename "$r")"); done
  [[ -z $logf ]] && { mkdir -p "$PROOFS"; logf="$PROOFS/audit-$(date +%Y%m%d-%H%M%S).log"; }
  mkdir -p "$(dirname "$logf")"
  {
    echo "# no-write-audit watch — début $(now) — pid $$"
    printf '# racine: %s\n' "${roots[@]}"
    echo "# ULTIMA_RE=$ULTIMA_RE  (exclus: $SELF_RE)"
    echo "# colonnes: horodatage<TAB>classe<TAB>événements<TAB>chemin<TAB>détenteurs pid:comm:mode"
  } >>"$logf"
  log "journal : $logf"

  local fifo; fifo=$(mktemp -u "${TMPDIR:-/tmp}/nwa.XXXXXX"); mkfifo "$fifo"
  : >"$logf.err"
  inotifywait -m -r -e create,modify,close_write,moved_to,moved_from,delete,attrib \
    --format '%e|%w%f' "${roots[@]}" >"$fifo" 2>>"$logf.err" &
  local inw=$!
  # marqueur « PRET » dans le journal quand toutes les surveillances sont posées
  # (test-window.sh l'attend avant la 1re étape ; avant lui, des écritures peuvent échapper)
  ( for _ in $(seq 1 600); do
      grep -q 'Watches established' "$logf.err" 2>/dev/null && { echo "# PRET $(now) — surveillances posées" >>"$logf"; exit; }
      sleep 0.1
    done; echo "# ERREUR surveillances non posées après 60 s : $(cat "$logf.err")" >>"$logf" ) &

  # échantillonneur : fds en écriture tenus par ULTIMA dans les arbres surveillés
  (
    while kill -0 "$inw" 2>/dev/null; do
      for p in $(ultima_pids); do
        for l in /proc/"$p"/fd/*; do
          t=$(readlink "$l" 2>/dev/null) || continue
          in_trees "$t" || continue
          m=$(fd_mode "$p" "${l##*/}")
          if [[ $m == w || $m == rw ]]; then
            printf '%s\tVIOLATION\tULTIMA_WRITE_FD\t%s\t%s:%s:%s\n' "$(now)" "$t" "$p" "$(cat /proc/$p/comm 2>/dev/null)" "$m" >>"$logf"
          fi
        done
      done
      sleep 1
    done
  ) &
  local sampler=$!

  local stopper=''
  if [[ $duration != 0 ]]; then ( sleep "$duration"; kill "$inw" 2>/dev/null ) & stopper=$!; fi
  trap 'kill $inw 2>/dev/null' INT TERM

  local line ev path cls hold game_re
  while IFS= read -r line; do
    ev=${line%%|*}; path=${line#*|}
    cls=EVENT
    [[ -n ${GAME_DIR:-} && $path == "$GAME_DIR"/* ]] && cls=ALERTE
    [[ $path == */steam/*/modded/* || $path == */steam/*/modded ]] && cls=ALERTE
    [[ ${path##*/} =~ [Uu][Ll][Tt][Ii][Mm][Aa] ]] && cls=VIOLATION
    hold=''
    [[ $ev != MODIFY* ]] && hold=$(holders "$path")
    if [[ -n $hold ]]; then
      local h; IFS=, read -ra hs <<<"$hold"
      for h in "${hs[@]}"; do
        if is_ultima "${h%%:*}" && [[ $h == *:w || $h == *:rw ]]; then cls=VIOLATION; fi
      done
    fi
    printf '%s\t%s\t%s\t%s\t%s\n' "$(now)" "$cls" "$ev" "$path" "$hold" >>"$logf"
  done <"$fifo"
  trap - INT TERM
  kill "$inw" "$sampler" ${stopper:+"$stopper"} 2>/dev/null; wait 2>/dev/null
  rm -f "$fifo"
  grep -v -e 'Setting up watches' -e 'Watches established' "$logf.err" >"$logf.err2" 2>/dev/null
  if [[ -s $logf.err2 ]]; then mv "$logf.err2" "$logf.err"; else rm -f "$logf.err" "$logf.err2"; fi

  local nev nal nvi
  nev=$(grep -c $'\tEVENT\t' "$logf"); nal=$(grep -c $'\tALERTE\t' "$logf"); nvi=$(grep -c $'\tVIOLATION\t' "$logf")
  {
    echo "# fin $(now) — EVENT=$nev ALERTE=$nal VIOLATION=$nvi"
    echo "# fichiers écrits (EVENT, par chemin) :"
    awk -F'\t' '$2=="EVENT"{c[$4]++} END{for(k in c) printf "#   %5d  %s\n", c[k], k}' "$logf" | sort -k3
  } >>"$logf"
  log "fin : EVENT=$nev ALERTE=$nal VIOLATION=$nvi — $logf"
  [[ $nal -eq 0 && $nvi -eq 0 ]]
}

# ---------------------------------------------------------------- selftest
cmd_selftest() {
  local base; base=$(mktemp -d "${TMPDIR:-/tmp}/nwa-selftest.XXXXXX")
  local root="$base/FakeSTS2Data" logf="$base/audit.log"
  mkdir -p "$root/steam/123/profile1/saves" "$root/steam/123/modded/profile1"
  log "selftest dans $base"
  STS2_GAME_DIR="$base/none" cmd_watch --paths "$root" --log "$logf" --duration 8 &
  local w=$!
  sleep 1.5
  # 1) écriture « par le jeu » (processus quelconque) : doit être EVENT
  echo '{"schema_version":16}' >"$root/steam/123/profile1/saves/current_run.save"
  # 2) écriture dans modded/ : doit être ALERTE
  echo x >"$root/steam/123/modded/profile1/touched"
  # 3) processus « ULTIMA » qui garde un fd en écriture 2,5 s : doit être VIOLATION
  #    (exec -a : la cmdline reste « ultima-fake-writer … » comme un vrai binaire ULTIMA)
  bash -c 'exec 3>>"$1"; echo intrus >&3; exec -a ultima-fake-writer sleep 2.5' ultima-fake-writer "$root/steam/123/profile1/saves/progress.save" &
  sleep 0.2
  # 4) fichier nommé ultima : VIOLATION
  touch "$root/ultima-cache.json"
  wait $w
  echo "----- journal brut ($logf)"; cat "$logf"
  local ok=1
  grep -qP '\tEVENT\tCLOSE_WRITE.*/profile1/saves/current_run.save' "$logf" || { echo "ECHEC: écriture de test non journalisée"; ok=0; }
  grep -qP '\tALERTE\t.*/modded/profile1/touched' "$logf" || { echo "ECHEC: écriture modded/ non signalée"; ok=0; }
  grep -qP '\tVIOLATION\t.*progress.save' "$logf" || { echo "ECHEC: fd en écriture ULTIMA non détecté"; ok=0; }
  grep -qP '\tVIOLATION\t.*ultima-cache.json' "$logf" || { echo "ECHEC: fichier *ultima* non détecté"; ok=0; }
  [[ $ok -eq 1 ]] && echo "SELFTEST PASS ($base)" || echo "SELFTEST FAIL ($base)"
  [[ $ok -eq 1 ]]
}

case "${1:-}" in
  check) shift; cmd_check "$@" ;;
  watch) shift; cmd_watch "$@" ;;
  selftest) shift; cmd_selftest "$@" ;;
  *) sed -n '2,30p' "$0"; exit 2 ;;
esac
