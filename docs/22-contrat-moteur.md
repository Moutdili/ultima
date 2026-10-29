# 22 — Contrat moteur v1 (vague 1)

Source de vérité : `Ultima.Engine/Contracts/*.cs` (chef). Figé à partir des données réelles de la
fenêtre de test n°1. **Design en dernier** (décision Mio) : l'affichage est fonctionnel, texte brut.

## Exigences de Mio (2026-10-29)
- Modèle HDT : **overlay** au-dessus du jeu + **fenêtre compagnon** (infos pratiques, réglages).
- **Personnalisable** : modules affichés, ordre, visibilité (`OverlaySettings`).
- Overlay par défaut : **deck avec score**, **reliques avec score**, **cartes à bonus spécial**
  (enchantements : Sown, Swift « Célérité »… 24 types, `Models/Enchantments/`), **malédictions très visibles**.
- Scoring au moment des **récompenses** et au **shop** (cartes + reliques + potions + retrait).

## Architecture
```
Ultima.Saves (fichiers) ─┐
ultima-mirror (enfant)  ─┼─► Ultima.Daemon ──► $XDG_RUNTIME_DIR/ultima/state.json (+ socket) ──► overlay GNOME
Ultima.Knowledge        ─┤        │                                                       └──► compagnon (vague 2)
Ultima.Engine (scoring, ─┘        └── réglages : ~/.config/ultima/settings.json (hors dossier du jeu)
  chemins, contrats)
```

## Propriété (vague 1)
| Agent | Possède |
|---|---|
| Chef | `Ultima.Engine/Contracts/**`, `docs/22*`, `Ultima.sln`, CI |
| Encyclopédiste | `Ultima.Knowledge/**`, `Ultima.Knowledge.Tests/**`, `data/kb/**`, `tools/kb/**` |
| **Hermes** (calcul, ses sous-agents — docs/23) | `Ultima.Engine/Scoring/**`, `Ultima.Engine/Paths/**`, `Ultima.Engine.Tests/Scoring/**`, `Ultima.Engine.Tests/Paths/**` |
| Câbleur | `Ultima.Daemon/**`, `Ultima.Daemon.Tests/**`, `docs/ipc/**` |
| Verrier GNOME | `overlay/gnome/**` |
