#!/usr/bin/env bash
# ULTIMA — désinstallation : retire exactement ce que liste ~/.local/share/ultima/manifest.json,
# remet en place ce que l'installation avait sauvegardé (ex. un ancien ~/.local/bin/ultima).
#
#   tools/uninstall.sh [--prefix DIR] [--purge] [-y]
#     --purge   retire aussi ~/.config/ultima (réglages), ~/.local/state/ultima (journal) et le dossier IPC
#     -y        sans confirmation (obligatoire hors terminal)
# Équivalent : tools/install.sh --uninstall … ou, une fois installé, ultima uninstall …
set -euo pipefail
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/install.sh" --uninstall "$@"
