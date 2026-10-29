---
name: sts2-daemon
description: Use when building or changing Ultima.Daemon — event-driven orchestration of saves, godot.log and the read-only memory mirror, screen detection, mirror child-process supervision, publishing EngineState, settings reload, degraded modes and performance budgets.
---

# ULTIMA — daemon (architecture)

## Principe : piloté par événements, jamais par sondage aveugle
| Signal | Source prouvée | Action |
|---|---|---|
| save réécrite | `SaveWatcher` (tmp + rename) | relire RunState, recalculer deck/reliques/chemins |
| `Preloading 'Combat Room'` | `GodotLogTail` | Screen=Combat (aucune lecture mémoire pendant le combat en v1) |
| `Card rarity: Rolled` puis écran récompense | log + miroir `--sections reward` | lire l'offre (prouvé : lisible AVANT le clic) |
| entrée marchand | miroir `CurrentRoomType == MerchantRoom` | lire `--sections shop` ; relire après achat (or qui change dans la save/mémoire) |
| `Player N selected card reward` | log | Screen=Map après la récompense |
| `MapVote` / save | log + save | Screen=Map, recalcul des chemins |
Le miroir n'est appelé QUE sur ces transitions, avec anti-rebond (≥ 250 ms) — jamais en boucle.

## Miroir mémoire
- Processus ENFANT `build/mirror/ultima-mirror probe --sections … [--compact] [--timeout ms]` (env `DOTNET_ROOT`).
  Sortie actuelle : en-têtes texte puis, par section, une ligne `[reward] …` suivie d'un objet JSON.
  Le Câbleur possède la CLI du probe (`Ultima.Mirror/src/Ultima.Mirror.Probe/**`) et y ajoute `--json`
  (une ligne JSON par appel, sans texte) ; la bibliothèque de lecture `Ultima.Mirror/src/Ultima.Mirror/**` reste gelée.
  Délai max 3 s,
  tué au-delà ; jamais plus d'un en vol. Un plantage du lecteur ne doit jamais toucher le daemon ni le jeu.
- Lecture seule (process_vm_readv) ; `ReadGuard` refuse les champs cachés.
- Vérification croisée save↔mémoire à chaque salle (PV, or, deck) ; divergence → `Degraded`.

## Robustesse
Jeu fermé → `GameRunning=false`, état fichiers seulement. Patch EA (`ultima-mirror layout` code 4) →
`Degraded="lecture mémoire indisponible (patch du jeu)"`. Save illisible → `.backup`, sinon dernier état valide.

## Performance (exigence Mio : baisses de FPS ressenties à la pioche)
- `nice 10` + classe E/S idle ; 0 % CPU au repos (aucun timer actif hors transitions).
- Aucune lecture mémoire pendant un combat (v1) ; mesure du coût de chaque lecture (log `mirror ms`).
- Publication seulement si l'état a CHANGÉ (hash), écriture atomique.

## Interdits
Écrire dans le dossier du jeu ou ses données ; lancer le jeu ; injecter ; utiliser les données cachées.
