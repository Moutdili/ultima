#!/usr/bin/env bash
# verify-all — porte d'intégration ULTIMA : TOUTES les suites, code de sortie ≠ 0 au moindre échec.
# À lancer avant chaque push sur main (chef comme Hermes). Ne lit ni le jeu ni les vrais dossiers XDG.
#
#   tools/verify-all.sh                    # tout
#   ULTIMA_TEST_PYTHON=/chemin/python3     # Python avec textual + rich + pytest + pytest-asyncio (console)
#   ULTIMA_COMPANION_PYTHON=/usr/bin/python3  # Python avec PyGObject, si `python3` du PATH n'en a pas
#
# La console exige Textual : sans Python adéquat, la suite console est déclarée ABSENTE (échec), pas sautée.
set -uo pipefail
cd "$(dirname "$(readlink -f "$0")")/.."
fail=0

# dotnet peut vivre hors PATH (installation utilisateur ~/.dotnet).
command -v dotnet >/dev/null 2>&1 || [ ! -x "$HOME/.dotnet/dotnet" ] || PATH="$HOME/.dotnet:$PATH"
# Les sockets AF_UNIX des tests console sont limités à 108 caractères : un TMPDIR long (ou imbriqué)
# casse la suite. On impose donc une racine COURTE et fixe, quel que soit le TMPDIR hérité.
TMPDIR="$(mktemp -d /tmp/u.XXXXXX)"
export TMPDIR
trap 'rm -rf "$TMPDIR"' EXIT
say() { printf '%-10s %s\n' "$1" "$2"; }

out=$(dotnet test Ultima.sln 2>&1); code=$?
echo "$out" | grep -E '^(Passed|Failed)!' | sed -E 's/, Duration[^-]*- / /'
[ $code = 0 ] && say ".NET" "vert" || { echo "$out" | grep -E '^\s+Failed ' ; say ".NET" "ROUGE"; fail=1; }

if (cd overlay/companion && "${ULTIMA_COMPANION_PYTHON:-python3}" -m pytest -q -p no:cacheprovider >/dev/null 2>&1); then say compagnon vert
else say compagnon ROUGE; fail=1; fi

if (cd overlay/gnome && npm test >/dev/null 2>&1); then say hud vert
else say hud ROUGE; fail=1; fi

py="${ULTIMA_TEST_PYTHON:-}"
if [ -z "$py" ]; then
    for c in "$HOME/.local/share/ultima/venv/bin/python3" "$HOME/.cache/ultima-tui-venv/bin/python3" python3; do
        "$c" -c 'import textual, pytest_asyncio' 2>/dev/null && { py="$c"; break; }
    done
fi
if [ -z "$py" ]; then say console "ABSENTE (aucun Python avec textual + pytest-asyncio : ULTIMA_TEST_PYTHON)"; fail=1
elif (cd tools/console && "$py" -m pytest -q -p no:cacheprovider >/dev/null 2>&1); then say console vert
else say console ROUGE; fail=1; fi

[ $fail = 0 ] && echo "PORTE : VERTE" || echo "PORTE : ROUGE — ne pas pousser"
exit $fail
