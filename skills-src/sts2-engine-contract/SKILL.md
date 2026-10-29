---
name: sts2-engine-contract
description: Use whenever code produces or consumes ULTIMA engine state — EngineState/CardView/Score records, module ids and default order, IPC transport (state.json + Unix socket), settings file, contract versioning and the change process. Mandatory for daemon, overlay, companion and calculation work.
---

# ULTIMA — contrat moteur (source : `Ultima.Engine/Contracts/*.cs`, propriété du chef)

## Règle d'or
Le contrat est la frontière entre les parties. **Personne d'autre que le chef ne le modifie.**
Besoin d'un champ : écrire la demande (champ, type, raison, consommateur) dans `status/<agent>.md`.
Tout changement incrémente `EngineState.CurrentVersion` ; les consommateurs ignorent les champs inconnus
et refusent une version majeure supérieure en l'annonçant (« mettre à jour ULTIMA »).

## Flux
`Ultima.Saves` (fichiers) + `ultima-mirror` (mémoire, processus enfant) + `IKnowledge` (statique)
→ **Ultima.Daemon** → calcul (`IScorer`, `IPathAnalyzer` — Hermes) → `EngineState`
→ `$XDG_RUNTIME_DIR/ultima/state.json` (écriture atomique tmp+rename) + `state.sock` (1 ligne JSON/état)
→ overlay GNOME, compagnon. Réglages : `~/.config/ultima/settings.json` (compagnon écrit, daemon lit).

## Sérialisation
System.Text.Json, camelCase, enums en chaîne (`"Shop"`), `null` explicite. Exemple réaliste :
`docs/22-state.sample.json` (shop Necrobinder de la fenêtre de test).

## Sémantique à respecter
- **Notation S à F** : afficher `score.grade` (S, A, B, C, D, F) en premier ; `total` et les termes en détail.
  Seuils : S ≥ 85, A ≥ 70, B ≥ 55, C ≥ 40, D ≥ 25, F < 25 (`Grades.FromTotal`).
- `Score == null` → calcul indisponible → l'UI affiche « — », jamais 0, jamais une valeur inventée.
- `SourceHealth.Degraded != null` → l'UI l'affiche en tête.
- `Screen` pilote les modules contextuels : `reward` (Screen.Reward), `shop` (Screen.Shop), `paths` (Screen.Map).
- Modules (ordre par défaut voulu par Mio) : `deck`, `enchanted`, `curses`, `relics`, `potions`, puis
  `reward`, `shop`, `paths`, `deck_score`. Ordre/visibilité personnalisables (`OverlaySettings`).
- **Probabiliste seulement** : aucune donnée cachée (séquences pré-tirées, rng, odds, actes futurs) dans l'état.

## v1.2 (additif, mineure 2)
Version majeure = rupture seulement ; ajouts compatibles = `ContractMinor`. Nouveaux : `EngineState.Map/Route/Event`,
`RunView.Party`, `ScoringContext.Allies`, `Score.DeltaG` (signé : malédictions négatives, Total reste 0..100),
`ScoreTerm.Evidence`, revue de run (`ReviewTimeline`, livrée par op socket `review.open` + fichier), interfaces
`IRoutePlanner`, `IEventAdvisor`, `IReviewBuilder`. Modules `event`, `route`, `party`. Détail : docs/22 §v1.2.

## v1.3 (mineure 3) : réglages du HUD
`OverlaySettings.Hud` = `HudSettings(Quality "eco|verre|loupe", HeightFraction, RestOpacity, Side "right|left",
DeckScoreInCombat, GradesOnly)` ; défaut `("verre", 0.55, 0.62, "right", false, false)`. Écrits dans
`~/.config/ultima/settings.json` par le TUI `ultima` ou le compagnon, publiés par le daemon dans `state.settings.hud`,
appliqués par l'extension.
