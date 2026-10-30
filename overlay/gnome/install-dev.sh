#!/usr/bin/env bash
# Installe l'extension ULTIMA (ultima@mio) pour l'utilisateur courant — SANS l'activer.
#
#   overlay/gnome/install-dev.sh            # copie + compile le schéma
#   overlay/gnome/install-dev.sh --check    # valide seulement (aucune écriture hors /tmp)
#   overlay/gnome/install-dev.sh --uninstall
#
# Sous Wayland, GNOME Shell ne découvre une NOUVELLE extension qu'après une
# déconnexion / reconnexion. Ensuite : gnome-extensions enable ultima@mio
# (voir overlay/gnome/TEST-WINDOW.md). Ce script ne touche ni au jeu ni à ses saves.
set -euo pipefail

UUID="ultima@mio"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$HERE/$UUID"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/gnome-shell/extensions/$UUID"
FILES=(metadata.json extension.js geometry.js state.js render.js plan.js grammar.js hud.js effects.js appearance.js theme.js themes/sts.json)

die() { echo "ERREUR : $*" >&2; exit 1; }

check() {
    command -v glib-compile-schemas >/dev/null || die "glib-compile-schemas absent"
    command -v gnome-extensions >/dev/null || die "gnome-extensions absent"
    for f in "${FILES[@]}"; do [[ -f "$SRC/$f" ]] || die "fichier manquant : $f"; done
    python3 - "$SRC/metadata.json" <<'PY'
import json, sys
m = json.load(open(sys.argv[1]))
assert m["uuid"] == "ultima@mio", m["uuid"]
assert "50" in m["shell-version"], m["shell-version"]
assert m["settings-schema"] == "org.gnome.shell.extensions.ultima"
print("metadata.json OK :", m["uuid"], m["shell-version"], m["settings-schema"])
PY
    local tmp; tmp="$(mktemp -d)"
    glib-compile-schemas --strict --targetdir="$tmp" "$SRC/schemas"
    echo "schéma OK : $(ls "$tmp")"
    local extra=()
    for f in "${FILES[@]}"; do
        [[ "$f" == metadata.json || "$f" == extension.js ]] || extra+=("--extra-source=$f")
    done
    gnome-extensions pack --force --out-dir="$tmp" "${extra[@]}" "$SRC" >/dev/null
    # Le paquet contient bien tous les modules (un oubli = extension cassée en session).
    for f in "${FILES[@]}"; do
        python3 -c 'import sys, zipfile; sys.exit(0 if sys.argv[2] in zipfile.ZipFile(sys.argv[1]).namelist() else 1)' \
            "$tmp"/*.zip "$f" || die "absent du paquet : $f"
    done
    echo "pack OK : $(ls "$tmp"/*.zip)"
    rm -rf "$tmp"
    # Aucune touche F dans les raccourcis (la rangée F du portable de Mio envoie des touches média).
    if grep -nE "<default>.*'[^']*F[0-9]+'" "$SRC/schemas/"*.gschema.xml; then die "touche F dans le schéma"; fi
    # Modules purs : tests sans session GNOME (rendu, lecture d'état, réglages HUD, planificateur, géométrie).
    command -v gjs >/dev/null || die "gjs absent"
    gjs -m "$HERE/tests/test-pure.js" | tail -n 1
    gjs -m "$HERE/tests/test-theme.js" | tail -n 1
    gjs -m "$HERE/tests/test-hud.js" | tail -n 1
    gjs -m "$HERE/tests/test-render.js" | tail -n 1
    if command -v node >/dev/null; then
        (cd "$HERE" && node tests/test-theme.js | tail -n 1 && node tests/test-hud.js | tail -n 1 &&
            node tests/test-render.js | tail -n 1 &&
            node --import ./tests/shell-harness/register.mjs tests/test-extension.mjs | tail -n 1)
    else
        echo "node absent : harnais extension (tests/test-extension.mjs) non exécuté"
    fi
    local ver; ver="$(gnome-shell --version 2>/dev/null || true)"
    echo "shell local : ${ver:-inconnu}"
}

case "${1:-}" in
    --check)
        check
        ;;
    --uninstall)
        if command -v gnome-extensions >/dev/null; then
            gnome-extensions disable "$UUID" 2>/dev/null || true
        fi
        rm -rf -- "$DEST"
        echo "désinstallé : $DEST (se reconnecter pour décharger complètement)"
        ;;
    "")
        check
        mkdir -p "$DEST/schemas"
        for f in "${FILES[@]}"; do install -m 0644 "$SRC/$f" "$DEST/$f"; done
        install -m 0644 "$SRC/schemas/"*.gschema.xml "$DEST/schemas/"
        glib-compile-schemas --strict "$DEST/schemas"
        echo "installé dans $DEST (NON activé)"
        echo "Étape suivante : se déconnecter / se reconnecter, puis : gnome-extensions enable $UUID"
        ;;
    *)
        die "option inconnue : $1"
        ;;
esac
