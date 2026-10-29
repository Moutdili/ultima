#!/usr/bin/env bash
# tools/check-offline.sh — PREUVE qu'un clone du dépôt fonctionne HORS LIGNE, sans les gros corpus.
#
# Exigence Mio (docs/28 §3) : un ami qui clone Ultima ne doit rien télécharger de lourd ; le moteur
# ne lit QUE des fichiers commités. Ce script le vérifie de bout en bout :
#
#   1. exporte UNIQUEMENT les fichiers trackés par git (`git archive`, donc exactement le contenu
#      d'un clone frais) dans un dossier temporaire ;
#   2. vérifie qu'aucun corpus lourd (data/exports, data/index, data/runs, data/codex, game/) n'y
#      est présent, et qu'aucun chemin utilisateur (/home/<user>, worktree) n'est commité ;
#   3. `dotnet build` puis `dotnet test` sur ce clone propre (les 3 projets de test) : si un test
#      dépend d'un fichier non commité, il échoue ICI ;
#   4. rejoue les tests dans un namespace réseau vide (unshare -n) quand c'est possible : preuve
#      qu'aucune donnée n'est téléchargée à l'exécution.
#
# Usage : bash tools/check-offline.sh [--ref <ref>] [--keep] [--fast]
#   --ref <ref>  commit/tag à tester (défaut : HEAD)
#   --keep       conserve le clone temporaire (chemin affiché) pour inspection
#   --fast       saute le rejeu en namespace réseau
# Sortie : code 0 = PASS, 1 = HOLD.

set -uo pipefail

REF="HEAD"
KEEP=0
FAST=0
while [ $# -gt 0 ]; do
  case "$1" in
    --ref) REF="${2:-}"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    --fast) FAST=1; shift ;;
    -h|--help) sed -n '2,24p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "argument inconnu : $1" >&2; exit 2 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null)" || {
  echo "FAIL  ce script doit vivre dans un dépôt git" >&2; exit 1; }

# Sortie anglaise : les compteurs de dotnet test sont parsés ci-dessous.
export DOTNET_CLI_UI_LANGUAGE=en DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [ -d "$HOME/.dotnet" ]; then export PATH="$HOME/.dotnet:$PATH"; fi
# Aucune donnée ne doit venir d'ailleurs que du clone.
unset ULTIMA_DATA STS2_GAME STS2_CATALOG 2>/dev/null || true

FAIL=0
say() { printf '\n=== %s ===\n' "$1"; }
ok()  { echo "PASS  $1"; }
ko()  { echo "FAIL  $1"; FAIL=1; }
warn(){ echo "WARN  $1"; }
kb()  { awk -v b="${1:-0}" 'BEGIN{ printf "%.1f Ko", b/1024 }'; }

SHORT="$(git -C "$REPO_ROOT" rev-parse --short "$REF" 2>/dev/null)" || {
  echo "FAIL  ref inconnue : $REF" >&2; exit 1; }

CLONE="$(mktemp -d "${TMPDIR:-/tmp}/ultima-offline-XXXXXX")" || exit 1
cleanup() { [ "$KEEP" -eq 1 ] || rm -rf "$CLONE"; }
trap cleanup EXIT

say "0. EXPORT GIT (fichiers trackés uniquement) — ref $REF ($SHORT)"
if git -C "$REPO_ROOT" archive "$REF" | tar -x -C "$CLONE"; then
  N_FILES=$(find "$CLONE" -type f | wc -l)
  ARCHIVE_BYTES=$(git -C "$REPO_ROOT" archive "$REF" | wc -c)
  EXTRACT_BYTES=$(du -sb "$CLONE" | cut -f1)
  ok "clone extrait : $N_FILES fichiers, archive $(kb "$ARCHIVE_BYTES") (tar), $(kb "$EXTRACT_BYTES") sur disque"
else
  ko "extraction git archive"
  echo "HOLD — clone impossible"; exit 1
fi
echo "      clone : $CLONE"

say "1. AUCUN CORPUS LOURD DANS LE CLONE"
for heavy in data/exports data/index data/runs data/codex game research/repos; do
  if [ -e "$CLONE/$heavy" ]; then
    ko "corpus non commité présent dans le clone : $heavy"
  else
    ok "absent (normal) : $heavy"
  fi
done

DATA_BYTES=$(du -sb "$CLONE/data" 2>/dev/null | cut -f1)
if [ -d "$CLONE/data/catalog" ] && [ -d "$CLONE/data/meta" ]; then
  ok "data/ commité = $(kb "${DATA_BYTES:-0}") (catalog + meta)"
else
  ko "data/catalog ou data/meta manquant dans le clone"
fi

say "2. AUCUN CHEMIN UTILISATEUR / WORKTREE DANS LE CODE ET LES DONNÉES COMMITÉS"
HITS=$(grep -rn --include='*.cs' --include='*.json' -E '/home/[a-z]|/Users/[^"]|\\\\Users\\\\|wt-s[0-9]+|/worktree' "$CLONE" 2>/dev/null \
       | grep -v '/obj/' | grep -v '/bin/' || true)
if [ -z "$HITS" ]; then
  ok "aucun chemin absolu utilisateur dans les *.cs / *.json"
else
  ko "chemin absolu utilisateur trouvé"; echo "$HITS" | head -20
fi

say "3. BUILD DU CLONE PROPRE (le jeu n'est PAS requis)"
BUILD_OK=1
dotnet build "$CLONE/Ultima.Engine.Tests/Ultima.Engine.Tests.csproj" -c Release -warnaserror -v q \
  >"$CLONE/.build-engine.log" 2>&1 || { BUILD_OK=0; tail -20 "$CLONE/.build-engine.log"; }
if [ "$BUILD_OK" -eq 1 ]; then ok "Ultima.Engine.Tests (-warnaserror)"; else ko "build Ultima.Engine.Tests"; fi

for proj in Ultima.Tests Ultima.Saves.Tests; do
  if dotnet build "$CLONE/$proj/$proj.csproj" -c Release -v q >"$CLONE/.build-$proj.log" 2>&1; then
    ok "build $proj"
  else
    ko "build $proj"; tail -20 "$CLONE/.build-$proj.log"
  fi
done

if [ "$BUILD_OK" -eq 0 ]; then
  echo; echo "HOLD — build cassé dans un clone propre (au-dessus)"; exit 1
fi

# --- exécution des tests ---------------------------------------------------------------------
# $1 projet, $2 ignorés attendus (tests « données réelles » qui exigent les sauvegardes du jeu)
run_tests() {
  local proj="$1" expected_skipped="$2" log="$CLONE/.test-$1.log" line failed passed skipped
  if ! dotnet test "$CLONE/$proj/$proj.csproj" -c Release --no-build -v q >"$log" 2>&1; then
    ko "$proj : échec(s)"; grep -E '^\s*(Failed|X) ' "$log" | head -20; tail -15 "$log"
    return 1
  fi
  line=$(grep -E '^(Passed!|Failed!)' "$log" | tail -1)
  failed=$(printf '%s' "$line"  | sed -n 's/.*Failed:[[:space:]]*\([0-9]*\).*/\1/p')
  passed=$(printf '%s' "$line"  | sed -n 's/.*Passed:[[:space:]]*\([0-9]*\).*/\1/p')
  skipped=$(printf '%s' "$line" | sed -n 's/.*Skipped:[[:space:]]*\([0-9]*\).*/\1/p')
  : "${failed:=?}" "${passed:=?}" "${skipped:=?}"
  if ! [[ "$failed" =~ ^[0-9]+$ && "$passed" =~ ^[0-9]+$ && "$skipped" =~ ^[0-9]+$ ]]; then
    ko "$proj : compteurs de test illisibles"; tail -15 "$log"; return 1
  fi
  if [ "$failed" != "0" ]; then
    ko "$proj : $failed échec(s), $passed réussite(s)"; return 1
  fi
  # Un test qui s'auto-ignore parce qu'un fichier manque est exactement le piège visé : on
  # n'accepte que les ignorés « données réelles » (sauvegardes du jeu, cf. TestPaths).
  if [ "$skipped" -gt "$expected_skipped" ] 2>/dev/null; then
    ko "$proj : $skipped test(s) ignoré(s) (attendu ≤ $expected_skipped) — un fichier requis manque ?"
    grep -iE 'skip' "$log" | head -10
    return 1
  fi
  ok "$proj : $passed réussite(s), $skipped ignoré(s) ≤ $expected_skipped"
  return 0
}

say "4. TESTS DU CLONE PROPRE (aucun fichier non commité disponible)"
rm -f "$CLONE"/.build-*.log "$CLONE"/.test-*.log
run_tests Ultima.Engine.Tests 0
run_tests Ultima.Tests 0
# 4 tests de terrain exigent les sauvegardes réelles du jeu (data non livrable, cf. TestPaths.cs).
run_tests Ultima.Saves.Tests 4

if [ "$FAST" -eq 0 ]; then
  say "5. REJEU HORS RÉSEAU (namespace réseau vide)"
  if command -v unshare >/dev/null 2>&1 && unshare -rn true >/dev/null 2>&1; then
    NET_FAIL=0
    for proj in Ultima.Engine.Tests Ultima.Tests Ultima.Saves.Tests; do
      # lo reste nécessaire : vstest se connecte au testhost par la boucle locale.
      if unshare -rn sh -c "ip link set lo up 2>/dev/null; cd '$CLONE' && dotnet test '$proj/$proj.csproj' -c Release --no-build -v q" \
           >"$CLONE/.net-$proj.log" 2>&1; then
        ok "$proj : vert sans aucun accès réseau"
      else
        ko "$proj : échoue sans réseau"; tail -15 "$CLONE/.net-$proj.log"; NET_FAIL=1
      fi
    done
    [ "$NET_FAIL" -eq 0 ] && echo "      (aucune socket externe : données lues sur disque uniquement)"
  else
    warn "namespace réseau indisponible (unshare -rn) : preuve réseau non rejouée"
  fi
else
  say "5. REJEU HORS RÉSEAU"
  warn "sauté (--fast)"
fi

say "6. POIDS D'UN CLONE"
echo "      archive git (compressé, ce que télécharge l'ami) : $(kb "$ARCHIVE_BYTES")"
echo "      extrait sur disque                              : $(kb "$EXTRACT_BYTES")"
echo "      dont data/ commité (catalog + meta)             : $(kb "${DATA_BYTES:-0}")"

say "RÉSULTAT"
if [ "$FAIL" -eq 0 ]; then
  echo "PASS — clone utilisable hors ligne, aucun corpus lourd requis ($N_FILES fichiers, $(kb "$ARCHIVE_BYTES"))"
else
  echo "HOLD — corriger les points ci-dessus"
fi
exit "$FAIL"
