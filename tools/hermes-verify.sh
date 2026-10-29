#!/usr/bin/env bash
# Porte de vérification de la partie CALCUL (Hermes) — à lancer avant tout merge.
# Usage : bash tools/hermes-verify.sh [--fast]
set -uo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

FAIL=0
say() { printf '\n=== %s ===\n' "$1"; }
ok()  { echo "PASS  $1"; }
ko()  { echo "FAIL  $1"; FAIL=1; }

say "1. BUILD (warnings = erreurs)"
if dotnet build Ultima.Engine.Tests/Ultima.Engine.Tests.csproj -c Release -warnaserror -v q >/tmp/hv-build.log 2>&1; then
  ok "build propre"
else
  ko "build"; tail -25 /tmp/hv-build.log
fi

say "2. TESTS (toute la suite)"
if dotnet test Ultima.Engine.Tests/Ultima.Engine.Tests.csproj -c Release --no-build -v q >/tmp/hv-test.log 2>&1; then
  tail -3 /tmp/hv-test.log
  ok "tests"
else
  ko "tests"; tail -30 /tmp/hv-test.log
fi

say "3. CHAMPS INTERDITS (probabiliste seulement)"
# Le moteur ne doit jamais lire les séquences pré-tirées ni les cotes cachées.
HITS=$(grep -rInE 'normal_encounter_ids|elite_encounter_ids|event_ids|relic_grab_bag|shared_relic_grab_bag|\brng\b|\bodds\b' \
        Ultima.Engine/Scoring Ultima.Engine/Paths 2>/dev/null | grep -v '^\s*//' || true)
if [ -z "$HITS" ]; then ok "aucun champ interdit"; else ko "champ interdit trouvé"; echo "$HITS"; fi

say "4. STUBS / TODO restants dans la zone CALCUL"
STUBS=$(grep -rInE 'TODO|FIXME|NotImplementedException|throw new NotSupported' \
        Ultima.Engine/Scoring Ultima.Engine/Paths 2>/dev/null | grep -v '///' || true)
if [ -z "$STUBS" ]; then ok "aucun stub"; else ko "stub restant"; echo "$STUBS"; fi

say "5. CONTRATS INTACTS"
if git diff --quiet origin/main -- Ultima.Engine/Contracts Ultima.Saves Ultima.Mirror Ultima.Daemon overlay 2>/dev/null; then
  ok "aucune modification hors périmètre"
else
  ko "modification hors périmètre"; git diff --stat origin/main -- Ultima.Engine/Contracts Ultima.Saves Ultima.Mirror Ultima.Daemon overlay
fi

say "RÉSULTAT"
if [ "$FAIL" -eq 0 ]; then echo "PASS — prêt à livrer"; else echo "HOLD — corriger les points ci-dessus"; fi
exit "$FAIL"
