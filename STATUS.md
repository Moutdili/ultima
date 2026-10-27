# STATUS — run ULTIMA (orchestrateur)

## Chrono

| Repère | Heure |
|---|---|
| Départ (`date +%H:%M`) | **20:15** (2026-10-27) |
| T+50 (limite vague 2) | 21:05 |
| T+70 (limite vague 3) | 21:25 |
| **T+90 — coupe, plus aucune feature** | **21:45** |
| Fin de budget (rapport rendu) | 22:15 |

## Portes (docs/15 §6)

| Porte | État | Preuve |
|---|---|---|
| G1 Compile | ✅ M0 | `bash tools/build.sh` → 0 Warning(s), 0 Error(s) |
| G2 Tests | ✅ M0 | `dotnet test … -c Release` → Passed 12/12 |
| G3 Read-only | ✅ M0 | grep des appels interdits → aucune ligne (exit 1) |
| G4 Charge (`overlay injected`) | ⏳ humain | jeu non lançable par l'agent ; pas encore de `ultima.log` |
| G5 Exactitude (3 rencontres) | ⏳ humain | — |
| G6 CI | ✅ main@074e06e | GitHub API : workflow `CI` completed/success sur `074e06e` |

## M0 — bootstrap (20:15 → 20:30)

- `bash tools/doctor.sh` → `== Résultat : 14 ok / 0 manquants ==` (jeu Flatpak détecté, v0.107.1)
- `dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release` → `Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12`
- `bash tools/build.sh && bash tools/deploy.sh` → `Ultima.dll`, `Ultima.Core.dll`, `Ultima.json` dans `<jeu>/mods/Ultima/`
- Catalogue : `stats.json` = dict (578 cartes, 123 monstres, 34 candidates prioritaires) ;
  `priority_cards.json` = liste de 34 objets `{class, file, base, cost, type, rarity, target, vars,
  glow_gold, glow_red, turn_conditional, priority_candidate, tags?, upgrade_delta?}`.
- Concurrence : `074e06e` (Hermes, Makefile + Containerfile) est arrivé sur main pendant M0.
  Aucun chevauchement. **Règle : `git pull --ff-only` avant chaque push de main.**

## Décisions du chef (écarts documentés au prompt maître)

**D1 — Ultima.csproj passe à `Godot.NET.Sdk/4.5.1` (bug bloquant trouvé en M0).**
Avec `Microsoft.NET.Sdk`, les générateurs de source Godot ne tournent pas : `HasGodotClassMethod`
n'est pas généré, donc le moteur n'appelle JAMAIS `_Ready`/`_Process`/`_UnhandledInput`/`_Input`
d'un node du mod (l'overlay V0.1 aurait été injecté mais vide, F7/F8 morts — G4 serait passé à tort).
Preuve : `strings Ultima.dll | grep -c HasGodotClassMethod` → 0 avant, 1 après.
Même SDK que la prior art (`sts2decktracker.csproj:2`, ModTemplate). `OutputPath` forcé sur
`bin/Release/net9.0/` pour garder `tools/build.sh` inchangé ; `.godot/` ajouté au `.gitignore`.

**D2 — Contrat DTO figé par le chef dans `Ultima.Core/Model/CombatSnapshot.cs`** (et non
`Ultima/Model/`). Données pures → testables en CI sans le jeu (la CI ne compile que Ultima.Core).
Membres positionnels = docs/15 §4 à l'identique ; extensions init-only (Players, CombatId, IsWeak,
RawPerHit, Weak/VulnerableMultiplier, PerTarget, ClassName, GlowsGold, Playable).

**D3 — Contrat de sortie figé : `Ultima.Core/Model/ForecastDtos.cs`** (`AttackForecast`,
`EnemyForecast`, `TurnForecast`, `PlayerThreat`, `PriorityAlert`). Conséquence : B1 (forecast) et
B2 (multi) ne dépendent plus du merge de A1 → **lancés en vague 1** avec A1/A2/A3 (docs/15 §4 :
« A2 et B1/B2 codent contre ce contrat, sans attendre A1 »). Chemin critique raccourci de ~25 min.

**D4 — Voie B (INetMessage custom) OFF par défaut** (`ultima.cfg` → `[net] custom_messages=false`).
Le comportement d'un client vanilla face à un ID inconnu est NON VÉRIFIÉ
(`research/notes/multiplayer.md:325,405`) : décision conservatrice. Voie A (HoverChanged, aucun
message) reste active. Le ping F7 de la V0.1 sera aussi soumis à ce drapeau.

**D5 — Ressources embarquées dans `Ultima.dll`** (`UI/Shaders/*.gdshader`,
`data/catalog/priority_cards.json`) : aucun fichier .json dans `mods/` (R7), rien à déployer en plus.

**D6 — `Ultima/Config/**` (UltimaConfig, `mods/Ultima/ultima.cfg`) appartient au chef.**
`ModEntry` charge la config au boot et logge `ULTIMA ready — game <version>` (SPEC §10.2).

## Propriété des fichiers (ce run)

| Agent | Branche | Possède |
|---|---|---|
| Chef | main | `Ultima/ModEntry.cs`, `Ultima/Patches/**`, `Ultima/Config/**`, `Ultima/Ultima.csproj`, `Ultima.Core/Model/**`, `STATUS.md`, `.gitignore` |
| A1 Reader | m1/reader | `Ultima/GameApi/**` |
| A2 UI | m2/ui | `Ultima/UI/**` |
| A3 Catalog | m3/catalog | `tools/index_source.py`, `data/catalog/**`, `docs/11*` |
| B1 Forecast | m4/forecast | `Ultima/Features/Forecast/**`, `Ultima.Core/Forecast/**`, `Ultima.Tests/ForecastTests.cs` |
| B2 Multi | m5/multi | `Ultima/Features/Multi/**`, `Ultima/Features/Priority/**`, `Ultima.Tests/PriorityTests.cs` |
| A4 Design QA | m6/design-qa | `docs/18-design-qa.md` (lecture seule ailleurs) |

## Journal de merge

_(rempli après chaque merge : build + test + grep read-only + staging)_
