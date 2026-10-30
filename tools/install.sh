#!/usr/bin/env bash
# ULTIMA — installation propre sous ~/.local (Fedora 44, sans sudo, idempotente).
#
#   tools/install.sh [options]              installe ou met à jour
#   tools/install.sh --check                vérifie les prérequis, n'écrit rien
#   tools/install.sh --uninstall [--purge] [-y]
#                                           retire exactement ce que liste le manifeste
#                                           (--purge : réglages, journal et dossier IPC aussi, après confirmation)
#
# Options :
#   --prefix DIR      racine (défaut ~/.local) : DIR/share/ultima (installation) et DIR/bin/ultima (commande)
#   --no-build        ne pas compiler : reprendre --build-dir (défaut <dépôt>/build : daemon/ et mirror/)
#   --build-dir DIR   dossier contenant daemon/ et mirror/ déjà publiés (implique --no-build)
#   --no-venv         pas de venv Textual (sous-commandes seulement, pas de TUI)
#   --wheels DIR      venv hors ligne : pip --no-index --find-links DIR
#   --no-extension    ne pas copier l'extension GNOME ultima@mio
#   --systemd         écrire l'unité utilisateur ultima-daemon.service (jamais activée ici)
#   --kb auto|yes|no  base FR (tools/kb/build-kb) : auto = seulement si kb.json est absent (défaut auto)
#
# Disposition : ~/.local/share/ultima/{bin,lib/{daemon,mirror,console},data,venv,manifest.json,backup/},
# lien ~/.local/bin/ultima. Un ~/.local/bin/ultima existant qui n'est pas à l'installation est sauvegardé
# dans ~/.local/share/ultima/backup/ et remis en place par --uninstall. HOME, XDG_DATA_HOME,
# XDG_CONFIG_HOME et XDG_STATE_HOME sont respectés (tests : HOME temporaire).
# Ne lance ni n'arrête aucun daemon, n'active pas l'extension, n'appelle pas systemctl.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/.." && pwd)"
console="$repo/tools/console"

prefix="" ; build=1 ; build_dir="" ; venv=1 ; wheels="" ; extension=1 ; systemd=0 ; kb="auto"
mode="install" ; purge=0 ; yes=0

die()  { printf '\033[1;38;2;255;85;85m✗\033[0m %s\n' "$*" >&2; exit 1; }
ok()   { printf '\033[1;38;2;127;255;0m✓\033[0m %s\n' "$*"; }
warn() { printf '\033[1;38;2;255;165;24m!\033[0m %s\n' "$*"; }
step() { printf '\033[38;2;239;200;81m·\033[0m %s\n' "$*"; }
if [[ -n "${NO_COLOR:-}" || ! -t 1 ]]; then
    die()  { printf '✗ %s\n' "$*" >&2; exit 1; }
    ok()   { printf '✓ %s\n' "$*"; }
    warn() { printf '! %s\n' "$*"; }
    step() { printf '· %s\n' "$*"; }
fi

while [[ $# -gt 0 ]]; do
    case "$1" in
        --prefix) prefix="${2:?--prefix DIR}"; shift 2 ;;
        --prefix=*) prefix="${1#*=}"; shift ;;
        --no-build) build=0; shift ;;
        --build-dir) build=0; build_dir="${2:?--build-dir DIR}"; shift 2 ;;
        --no-venv) venv=0; shift ;;
        --wheels) wheels="${2:?--wheels DIR}"; shift 2 ;;
        --no-extension) extension=0; shift ;;
        --systemd) systemd=1; shift ;;
        --kb) kb="${2:?--kb auto|yes|no}"; shift 2 ;;
        --check) mode="check"; shift ;;
        --uninstall|uninstall) mode="uninstall"; shift ;;
        --purge) purge=1; shift ;;
        -y|--yes) yes=1; shift ;;
        -h|--help) sed -n '2,26p' "$0"; exit 0 ;;
        *) die "option inconnue : $1 (tools/install.sh --help)" ;;
    esac
done
[[ "$kb" =~ ^(auto|yes|no)$ ]] || die "--kb auto|yes|no"
[[ "$(id -u)" != 0 ]] || die "ne pas lancer en root : ULTIMA s'installe pour l'utilisateur (~/.local)"
[[ -n "${HOME:-}" && -d "$HOME" ]] || die "HOME invalide"

if [[ -n "$prefix" ]]; then
    prefix="$(realpath -m -- "$prefix")"     # sans le créer : l'installateur note les dossiers qu'il crée
    [[ "$prefix" != / ]] || die "--prefix / refusé"
    export ULTIMA_HOME="$prefix/share/ultima" ULTIMA_BIN_DIR="$prefix/bin"
fi

# Python : celui du système (3.11+ : option -P du lanceur), avec le module venv.
py="$(command -v python3 || true)"
[[ -n "$py" ]] || die "python3 introuvable (dnf install python3)"
"$py" -c 'import sys; sys.exit(sys.version_info < (3, 11))' || die "Python ≥ 3.11 requis ($("$py" -V))"
run_installer() { PYTHONPATH="$console" "$py" -B -P -m ultima_console.installer "$@"; }

if [[ "$mode" == "uninstall" ]]; then
    args=(uninstall)
    (( purge )) && args+=(--purge)
    (( yes )) && args+=(-y)
    exec env PYTHONPATH="$console" "$py" -B -P -m ultima_console "${args[@]}"
fi

# ---------- prérequis ----------
step "dépôt : $repo"
fails=0
check() { "$@" >/dev/null 2>&1; }
ok "python3 $("$py" -c 'import platform; print(platform.python_version())')"
if (( venv )); then
    check "$py" -c 'import venv, ensurepip' && ok "module venv + ensurepip" \
        || { warn "module venv/ensurepip absent (dnf install python3) — ou --no-venv"; fails=1; }
fi
dotnet=""
for c in "${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}" "$HOME/.dotnet/dotnet" "$(command -v dotnet || true)"; do
    [[ -n "$c" && -x "$c" ]] && { dotnet="$c"; break; }
done
if (( build )); then
    if [[ -z "$dotnet" ]]; then
        warn "dotnet introuvable (DOTNET_ROOT, ~/.dotnet ou PATH) — tools/install-toolchain.sh, ou --build-dir"; fails=1
    elif "$dotnet" --list-sdks 2>/dev/null | grep -q '^9\.'; then
        ok "SDK .NET 9 ($dotnet)"
    else
        warn "SDK .NET 9 absent ($dotnet --list-sdks)"; fails=1
    fi
else
    build_dir="${build_dir:-$repo/build}"
    if [[ -x "$build_dir/daemon/ultima-daemon" ]]; then ok "daemon déjà publié : $build_dir/daemon"
    else warn "$build_dir/daemon/ultima-daemon absent (retirer --no-build)"; fails=1; fi
    [[ -x "$build_dir/mirror/ultima-mirror" ]] && ok "miroir déjà publié : $build_dir/mirror" \
        || warn "$build_dir/mirror/ultima-mirror absent : lecture mémoire indisponible"
fi
if [[ -n "$dotnet" ]] && "$dotnet" --list-runtimes 2>/dev/null | grep -q '^Microsoft.NETCore.App 9\.'; then
    ok "runtime .NET 9 (exécution du daemon)"
else
    warn "runtime Microsoft.NETCore.App 9 introuvable : le daemon ne démarrera pas"
fi
if (( extension )); then
    command -v glib-compile-schemas >/dev/null && ok "glib-compile-schemas" \
        || warn "glib-compile-schemas absent : schéma de l'extension non compilé"
fi
command -v git >/dev/null && ok "git" || warn "git absent : données copiées sans liste git"
[[ -f "$console/requirements.txt" ]] || { warn "tools/console/requirements.txt manquant"; fails=1; }
scope="$(cat /proc/sys/kernel/yama/ptrace_scope 2>/dev/null || echo '?')"
[[ "$scope" == 0 ]] && ok "ptrace_scope 0" || warn "ptrace_scope=$scope : lecture mémoire refusée tant qu'il ne vaut pas 0"
(( fails == 0 )) || die "prérequis manquants (voir ci-dessus)"
if [[ "$mode" == "check" ]]; then ok "prérequis OK (rien n'a été écrit)"; exit 0; fi

# ---------- build ----------
stage="$(mktemp -d "${TMPDIR:-/tmp}/ultima-install.XXXXXX")"
trap 'rm -rf -- "$stage"' EXIT
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# dotnet et NuGet écrivent sous $HOME (store X509, migrations NuGet, sentinelles) : on leur donne un HOME
# jetable dans le staging, en gardant le cache de paquets de l'utilisateur (pas de téléchargement).
# Aucun serveur de build laissé en mémoire après l'installation (y compris le `dotnet run` de build-kb).
dotnet_env=(env HOME="$stage/home" DOTNET_CLI_HOME="$stage/home" MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
            NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}" DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "${dotnet:-/}")}")
if (( build )); then
    mkdir -p "$stage/home"
    step "build Release : Ultima.Daemon"
    "${dotnet_env[@]}" "$dotnet" publish "$repo/Ultima.Daemon" -c Release -o "$stage/daemon" --nologo -v q --disable-build-servers \
        >"$stage/build.log" 2>&1 || { tail -20 "$stage/build.log" >&2; die "échec du build du daemon"; }
    step "build Release : Ultima.Mirror.Probe (ultima-mirror)"
    "${dotnet_env[@]}" "$dotnet" publish "$repo/Ultima.Mirror/src/Ultima.Mirror.Probe" -c Release -o "$stage/mirror" \
        --nologo -v q --disable-build-servers >"$stage/build.log" 2>&1 || { tail -20 "$stage/build.log" >&2; die "échec du build du miroir"; }
    ok "build Release"
else
    cp -a -- "$build_dir/daemon" "$stage/daemon"
    [[ -d "$build_dir/mirror" ]] && cp -a -- "$build_dir/mirror" "$stage/mirror"
fi
find "$stage/daemon" "$stage/mirror" -name '*.pdb' -delete 2>/dev/null || true

if (( extension )) && command -v glib-compile-schemas >/dev/null; then
    mkdir -p "$stage/extension-schemas"
    glib-compile-schemas --strict --targetdir="$stage/extension-schemas" "$repo/overlay/gnome/ultima@mio/schemas"
fi

# ---------- base FR ----------
kb_args=()
kb_json="${XDG_DATA_HOME:-$HOME/.local/share}/ultima/kb/kb.json"
if [[ "$kb" == yes || ( "$kb" == auto && ! -f "$kb_json" ) ]]; then
    game="${ULTIMA_GAME_DIR:-/home/mio/sts2/game}"
    if [[ -d "$game" && -n "$dotnet" ]]; then
        step "base FR : tools/kb/build-kb (lecture seule de $game)"
        mkdir -p "$stage/home"
        if "${dotnet_env[@]}" PATH="$(dirname "$dotnet"):$PATH" \
            "$repo/tools/kb/build-kb" --game "$game" --out "$stage/kb" >"$stage/kb.log" 2>&1; then
            kb_args=(--kb-stage "$stage/kb"); ok "base FR générée"
        else
            tail -5 "$stage/kb.log" >&2; warn "base FR non générée : noms = identifiants (relancer tools/kb/build-kb)"
        fi
    else
        warn "base FR non générée (jeu extrait introuvable : $game) — tools/kb/build-kb plus tard"
    fi
elif [[ -f "$kb_json" ]]; then
    ok "base FR existante conservée : $kb_json"
fi

# ---------- déploiement + manifeste ----------
args=(install --repo "$repo" --stage "$stage" "${kb_args[@]}")
(( extension )) || args+=(--no-extension)
(( systemd )) && args+=(--systemd)
(( venv )) || args+=(--no-venv)
[[ -n "$wheels" ]] && args+=(--wheels "$wheels")
step "déploiement"
run_installer "${args[@]}"

home="${ULTIMA_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/ultima}"
bindir="${ULTIMA_BIN_DIR:-$HOME/.local/bin}"
echo
PYTHONPATH="$console" "$py" -B -P -m ultima_console banner || true
echo
ok "ULTIMA installé : $home (commande $bindir/ultima)"
case ":$PATH:" in *":$bindir:"*) ;; *) warn "$bindir n'est pas dans le PATH" ;; esac
echo
echo "Étapes suivantes (non faites par ce script) :"
echo "  ultima doctor                     vérifier l'installation"
echo "  ultima start                      lancer le daemon (ou ultima, puis s)"
(( extension )) && echo "  se reconnecter, puis ultima hud on  activer l'extension GNOME $(printf %s 'ultima@mio')"
(( systemd )) && echo "  systemctl --user daemon-reload && systemctl --user enable --now ultima-daemon.service"
echo "  tools/install.sh --uninstall      tout retirer (réglages conservés ; --purge pour les retirer aussi)"
