"""ULTIMA — console `ultima` : TUI (sans argument), sous-commandes scriptables, installation.

Bibliothèque standard seulement pour la CLI ; le TUI importe Textual à la demande (venv
`~/.local/share/ultima/venv`, voir tools/install.sh).
"""

VERSION = "0.1.0"

# Contrat moteur supporté (Ultima.Engine/Contracts : EngineState.CurrentVersion / ContractInfo.CurrentMinor).
CONTRACT_MAJOR = 1
CONTRACT_MINOR = 3
