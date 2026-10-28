#!/usr/bin/env bash
# test-window.sh — cadre d'une « fenêtre de test » (docs/21 §6) : le jeu tourne (lancé par
# Mio), on déroule une liste d'étapes écrite à l'avance, chaque étape produit des captures,
# l'audit inotify tourne en fond, et un rapport Markdown sort dans ~/sts2-proofs/<date>/.
# Ce script ne lance JAMAIS le jeu.
#
# Usage : test-window.sh ETAPES.steps [options]
#   --no-game            ne pas exiger le jeu (répétition à blanc)
#   --game-proc RE       motif pgrep -f du processus « jeu » (défaut : nom exact SlayTheSpire2)
#   --class RE / --name RE   transmis à capture-game-x11.sh (détection de la fenêtre)
#   --auto [S]           pas d'attente clavier : les étapes « manuel » attendent S s (défaut 5)
#   --no-composed        pas de capture par le portail (si l'autorisation n'est pas donnée)
#   --out DIR            dossier racine (défaut ~/sts2-proofs)
#
# Format du fichier d'étapes (une directive par ligne, # = commentaire) :
#   manuel: <consigne pour Mio>     affichée, attend Entrée puis demande le résultat observé
#   cmd: <commande shell>           exécutée (bash -c ; $ULTIMA_TOOLS = dossier tools/, $STEP_DIR = dossier
#                                   du rapport), sortie et code retour dans le rapport
#   attendre: <secondes>
#   capture: <libellé>              capture seule
#   attendu: <résultat attendu>     rattaché à l'étape précédente (écrit À L'AVANCE)
# Chaque étape (sauf attendu) déclenche : capture composée + capture X11 de la fenêtre du jeu
# + vérification que le processus du jeu est toujours vivant.
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
PROOF="$HERE/../proof"
AUDIT="$HERE/../audit/no-write-audit.sh"
STEPS=''; NO_GAME=0; GAME_PROC=''; XARGS=(); AUTO=''; COMPOSED=1; OUT="$HOME/sts2-proofs"

while [[ $# -gt 0 ]]; do
  case $1 in
    --no-game) NO_GAME=1; shift ;;
    --game-proc) GAME_PROC=$2; shift 2 ;;
    --class|--name) XARGS+=("$1" "$2"); shift 2 ;;
    --auto) if [[ ${2:-} =~ ^[0-9.]+$ ]]; then AUTO=$2; shift 2; else AUTO=5; shift; fi ;;
    --no-composed) COMPOSED=0; shift ;;
    --out) OUT=$2; shift 2 ;;
    -h|--help) sed -n '2,26p' "$0"; exit 0 ;;
    -*) echo "option inconnue : $1" >&2; exit 2 ;;
    *) STEPS=$1; shift ;;
  esac
done
[[ -f $STEPS ]] || { echo "fichier d'étapes requis (voir --help)" >&2; exit 2; }

DIR="$OUT/$(date +%F)/$(date +%H%M%S)-$(basename "$STEPS" .steps)"
CAP="$DIR/captures"; mkdir -p "$CAP"
REPORT="$DIR/rapport.md"; RUNLOG="$DIR/run.log"
say() { echo "[test-window $(date +%H:%M:%S)] $*" | tee -a "$RUNLOG" >&2; }

game_pids() {
  local p
  if [[ -n $GAME_PROC ]]; then
    for p in $(pgrep -f -- "$GAME_PROC"); do  # exclut ce script et les shells qui citent le motif
      [[ $p == "$$" ]] && continue
      tr '\0' ' ' <"/proc/$p/cmdline" 2>/dev/null | grep -qE 'test-window|bash -c' && continue
      echo "$p"
    done
  else pgrep -x SlayTheSpire2 || true; fi
}

# ------------------------------------------------------------------ préliminaires
GPIDS=$(game_pids | tr '\n' ' ')
if [[ -z ${GPIDS// } ]]; then
  if [[ $NO_GAME -eq 1 ]]; then say "jeu absent (--no-game : répétition à blanc)"
  else say "ABANDON : le jeu ne tourne pas (pgrep ${GAME_PROC:-SlayTheSpire2}). Mio doit le lancer."; exit 2; fi
fi
GPID=${GPIDS%% *}
say "dossier de preuves : $DIR"

{
  echo "# Fenêtre de test — $(basename "$STEPS")"
  echo
  echo "- Date : $(date '+%F %T %z') — machine : $(hostname) — noyau $(uname -r)"
  echo "- Session : ${XDG_SESSION_TYPE:-?} / $(gnome-shell --version 2>/dev/null || echo 'gnome-shell ?') / DISPLAY=${DISPLAY:-}"
  echo "- Fichier d'étapes : \`$(realpath "$STEPS")\` (sha256 \`$(sha256sum "$STEPS" | cut -c1-16)\`)"
  if [[ -n ${GPIDS// } ]]; then
    echo "- Processus du jeu : PID hôte $GPIDS — \`$(tr '\0' ' ' </proc/"$GPID"/cmdline | cut -c1-160)\` — démarré $(ps -o lstart= -p "$GPID")"
  else
    echo "- Processus du jeu : **absent** (répétition à blanc)"
  fi
  echo
  echo "## Audit avant session (no-write-audit check)"
  echo '```'
} >"$REPORT"
"$AUDIT" check --write-baseline "$DIR/modded-baseline.sha256" >>"$REPORT" 2>&1; PRE=$?
echo '```' >>"$REPORT"

# ------------------------------------------------------------------ audit en fond
AUDIT_LOG="$DIR/audit.log"
"$AUDIT" watch --log "$AUDIT_LOG" 2>>"$RUNLOG" &
AUDIT_PID=$!
for _ in $(seq 1 300); do grep -q '^# PRET' "$AUDIT_LOG" 2>/dev/null && break; sleep 0.1; done
grep -q '^# PRET' "$AUDIT_LOG" 2>/dev/null && say "audit prêt (pid $AUDIT_PID)" || say "ATTENTION : audit pas prêt après 30 s"
cleanup() { kill -TERM "$AUDIT_PID" 2>/dev/null; wait "$AUDIT_PID" 2>/dev/null; }
trap 'cleanup; say "interrompu"; exit 130' INT TERM

# ------------------------------------------------------------------ étapes
rows=(); n=0
shot() {  # shot <label> -> "liens markdown"
  local lab=$1 links='' f rc
  if [[ $COMPOSED -eq 1 ]]; then
    f=$(python3 "$PROOF/capture-composed.py" --out-dir "$CAP" --label "$lab" --timeout 20 2>>"$RUNLOG"); rc=$?
    [[ $rc -eq 0 ]] && links+="[écran](captures/$(basename "$f")) " || links+="écran:ERR$rc "
  fi
  f=$("$PROOF/capture-game-x11.sh" --out-dir "$CAP" --label "$lab" "${XARGS[@]}" 2>>"$RUNLOG"); rc=$?
  if [[ $rc -eq 0 ]]; then links+="[fenêtre](captures/$(basename "$f"))"
  elif [[ $rc -eq 5 ]]; then links+="[fenêtre UNIE](captures/$(basename "$f"))"
  else links+="fenêtre:ERR$rc"; fi
  echo "$links"
}
alive() { if [[ -z ${GPID:-} ]]; then echo "—"; elif kill -0 "$GPID" 2>/dev/null; then echo "oui"; else echo "**NON**"; fi; }
esc() { local s=${1//|/\\|}; echo "${s//$'\n'/ }"; }

while IFS= read -r raw || [[ -n $raw ]]; do
  line=${raw#"${raw%%[![:space:]]*}"}
  [[ -z $line || $line == \#* ]] && continue
  kind=${line%%:*}; arg=${line#*:}; arg=${arg# }
  case $kind in
    attendu)
      [[ $n -gt 0 ]] && rows[$((n-1))]=${rows[$((n-1))]/@@ATTENDU@@/$(esc "$arg")}
      continue ;;
    manuel)
      n=$((n+1)); say "étape $n (manuel) : $arg"
      if [[ -n $AUTO ]]; then sleep "$AUTO"; obs="(auto, ${AUTO}s)"
      else
        read -r -p ">>> $arg — Entrée quand c'est fait " _ </dev/tty
        read -r -p ">>> résultat observé (o = conforme, n = non, ou texte) : " obs </dev/tty
      fi ;;
    cmd)
      n=$((n+1)); say "étape $n (cmd) : $arg"
      ULTIMA_TOOLS="$HERE/.." STEP_DIR="$DIR" bash -c "$arg" >"$DIR/step-$(printf %02d $n).out" 2>&1; rc=$?
      obs="code $rc — [sortie](step-$(printf %02d $n).out)" ;;
    attendre)
      n=$((n+1)); say "étape $n (attendre ${arg}s)"; sleep "$arg"; obs="—" ;;
    capture)
      n=$((n+1)); say "étape $n (capture) : $arg"; obs="—" ;;
    *) say "directive inconnue ignorée : $raw"; continue ;;
  esac
  links=$(shot "step$(printf %02d $n)")
  rows+=("| $n | $kind | $(esc "$arg") | @@ATTENDU@@ | $(esc "$obs") | $(alive) | $links |")
done <"$STEPS"

# ------------------------------------------------------------------ fin
cleanup; trap - INT TERM
AUD_SUM=$(grep '^# fin' "$AUDIT_LOG" | tail -1)
{
  echo
  echo "## Étapes"
  echo
  echo "| # | type | consigne / commande | attendu (écrit avant) | observé | jeu vivant | captures |"
  echo "|---|---|---|---|---|---|---|"
  for r in "${rows[@]}"; do echo "${r/@@ATTENDU@@/—}"; done
  echo
  echo "## Audit pendant la session (no-write-audit watch)"
  echo
  echo "- Journal brut : [audit.log](audit.log)"
  echo "- Résumé : \`${AUD_SUM:-# pas de résumé}\`"
  echo '```'
  grep -E $'\t(ALERTE|VIOLATION)\t' "$AUDIT_LOG" | head -50 || true
  sed -n '/^# fichiers écrits/,$p' "$AUDIT_LOG"
  echo '```'
  echo
  echo "## Audit après session (modded/ comparé au manifeste d'avant)"
  echo '```'
} >>"$REPORT"
"$AUDIT" check --baseline "$DIR/modded-baseline.sha256" >>"$REPORT" 2>&1; POST=$?
NVIOL=$(grep -cE $'\t(ALERTE|VIOLATION)\t' "$AUDIT_LOG")
{
  echo '```'
  echo
  verdict=PASS; [[ $PRE -ne 0 || $POST -ne 0 || $NVIOL -ne 0 ]] && verdict=FAIL
  echo "## Verdict G-nonneg : **$verdict** (check avant=$PRE, après=$POST, alertes+violations=$NVIOL)"
  echo
  echo "_Les colonnes « observé » des étapes manuelles sont saisies par Mio ; le verdict fonctionnel_"
  echo "_de chaque étape se lit dans les captures, pas dans ce script._"
} >>"$REPORT"
say "rapport : $REPORT"
echo "$REPORT"
[[ $verdict == PASS ]]
