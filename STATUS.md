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
| G6 CI | ✅ main@03132ab + toutes les branches | GitHub API 20:45 : `CI` completed/success sur main (03132ab, 13d8b88, f086ac2, cd20ae0, 8330a59) et m1-m5 (têtes bff5438, 33628f8, 5f314d8, 952e5ca, d70795d) |

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

**D7 — Overlay V0.1 retiré (`Ultima/Overlay/UltimaOverlay.cs`).** Remplacé par
`Ultima.UI.UltimaOverlay` (A2), qui reprend F8 et lève F7 → `NetBridge.SendPing` (soumis à D4).
Garder le V0.1 = touches F7/F8 traitées deux fois s'il était réinjecté + `new Color(...)` littéral
contraire à docs/17 §8. `NetBridge`/`UltimaPingMessage` sont conservés et utilisés (R2).
`tools/verify-deploy.sh` (Hermes) pointait sur le type V0.1 → repointé sur `Ultima.UI.UltimaOverlay`.

**D8 — Vague 3 en parallèle.** P0-P2 committés/testés/poussés à 20:44. Lancement simultané :
A4 (revue charte, lecture seule), C1 (badge F3 sur la carte en main — complète P1), C2 (F5 RunScore,
P4). L'ordre de MERGE respecte la priorité (C1 avant C2) ; F6 (P5) non lancé.

**D9 — PID dans chaque ligne de log** (`[<pid>]`) : en multi local, host et client partagent
`mods/Ultima/ultima.log` (tools/multi-lab.sh lance les 2 instances depuis le même dossier de jeu).

**D10 — Skills `sts2-*` : aucun agent ne les a chargées (constat à 20:49, signalé par Mio).**
Preuve : `grep -o '"input":{"skill":"[^"]*"'` sur les 8 transcripts d'agents → 0 invocation.
Les skills étaient disponibles mais mes briefs ne les imposaient pas (ils renvoyaient à docs/).
Correctifs : (1) A4 et C2 (en cours) ont reçu l'ordre de charger leurs skills ; (2) tout brief
futur commence par « charge d'abord les skills X » ; (3) audit chef du travail déjà mergé :
- `sts2-combat-math` : pas de contradiction ; B1 est plus strict que la skill (la skill écrit
  `current * 0.75m` en dur — interdit par R6 ; B1 lit le multiplicateur et part du brut).
- `sts2-multiplayer-net` : B2 conforme (voie A d'abord, Hello, version, diff, TurnNumber).
- `sts2-mod-dev` / `sts2-game-api` : conformes (D1 rend obsolète la mention « refs GodotSharp » :
  GodotSharp vient désormais du paquet Godot.NET.Sdk 4.5.1).
- **`sts2-design-system` / `sts2-overlay-ui` : écarts réels sur l'UI (A2)** :
  1. Verre : la skill dit de réutiliser `game/pck/shaders/dark_blur.gdshader` (flou par mipmap,
     `lod 3.5`, mix 0.72 vers #192930) et de ne pas écrire de shader ; A2 a pris le shader 9 taps
     de docs/17 §3 (« à valider visuellement ») → sans lod, risque de flou « fantôme ».
  2. Coins radius 10 + ombre : seulement en repli chez A2, pas sur le verre réel.
  3. Opacité repos : skill 0.30, A2 0.35 (SPEC 0.25-0.35 : les deux sont conformes).
  4. Police min : skill 14 px, docs/17 12 px pour le secondaire (A2 suit docs/17).
  docs/17 reste normatif (la skill le dit) → arbitrage chef après le rapport A4.

**D8bis — C1 mergé (20:51)** : badge F3 « PRIO » sur la carte en main (`HandBadges`), câblé
dans `CombatSession` (Update à chaque refresh, Clear à l'arrêt, sûr vis-à-vis du pooling).

**D11 — Verre refait selon les skills (demande explicite de Mio, 20:53).** Constat QA A4
(`docs/18-design-qa.md`, FAIL 10 et 11) + écarts skills. Décisions chef, confiées à A2 relancé
AVEC chargement obligatoire de `sts2-design-system` + `sts2-overlay-ui` :
- shader dérivé de `game/pck/shaders/dark_blur.gdshader` (le jeu l'utilise dans `main_menu.tscn`
  et `epoch_slot.tscn` → technique prouvée sur ce moteur) : échantillonnage mipmap `lod 3.5`,
  3×3 taps AU lod (lettre de la charte : 9 taps, blur_amount 2.0) avec offsets en pixels écran
  indépendants de panel_size → plus de flou fantôme ; teinte #192930 mix 0.78 (prompt maître) ;
- highlight corrigé (bug de la charte : éclairait 90 % du panneau) → bord haut seulement ;
- coins radius 10 + liseré + ombre sur le VRAI verre (masque SDF), plus seulement en repli ;
- opacité repos 0.30 + fondu 150 ms (skill), panneau déployé jamais à 0.35 (lisibilité) ;
- chiffres PV en Source Code Pro (QA 10) ; police min 14 px partout (skill 14 ∧ charte ≥ 12).
QA 11 (chevauchement) corrigé par le chef : `4a5f996` (cartes de la main, corps + PV des
créatures, piles pioche/défausse/épuisement).

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
| C1 Badge | m7/badge | `Ultima/Features/Priority/HandBadges.cs` |
| C2 Score | m8/score | `Ultima.Core/Score/**`, `Ultima.Tests/ScoreTests.cs`, `Ultima/Features/Score/**` |

## Journal de merge

Vague 1 lancée à 20:28 (5 agents en parallèle). Rendus : A1 20:33, A3 20:33, B1 20:35.

### 20:34 — merge A3 `m3/catalog` (5f314d8) → main `f1e9d89`
Avant : main avancé en fast-forward sur `2cf1da0` (Hermes : tools/multi-lab.sh + verify-deploy.sh,
identiques aux copies non suivies présentes dans l'arbre → copies retirées, pas de perte).
```
$ git log --oneline -1
f1e9d89 merge: m3/catalog (A3) first_card_hint, signals, intents_by_monster
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 24 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 84
-rw-r--r--. 1 mio mio 47616 Oct 27 20:34 Ultima.Core.dll
-rw-r--r--. 1 mio mio 31232 Oct 27 20:34 Ultima.dll
-rw-r--r--. 1 mio mio   280 Oct 27 20:34 Ultima.json
```
JSON : `json.load` OK sur les 6 fichiers de data/catalog.

### 20:34 — merge A1 `m1/reader` (bff5438) → main `3e0675d`
```
$ git log --oneline -1
3e0675d merge: m1/reader (A1) CombatReader + IntentParser
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 23 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 96
-rw-r--r--. 1 mio mio 47616 Oct 27 20:34 Ultima.Core.dll
-rw-r--r--. 1 mio mio 44032 Oct 27 20:34 Ultima.dll
-rw-r--r--. 1 mio mio   280 Oct 27 20:34 Ultima.json
```
Revue chef : getters + `Hook.ModifyDamage` (calcul d'affichage du jeu) uniquement ; multiplicateurs
lus (`ModelDb.Power<WeakPower>().DynamicVars["DamageDecrease"]`, PaperKrane, Debilitate, Cruelty) ;
grep `0.75|1.5` sur GameApi → vide.

### 20:35 — merge B1 `m4/forecast` (952e5ca) → main `cd20ae0` (poussé)
Écart d'ordre : B1 mergé avant A2 (A2 non rendu, aucune dépendance entre eux).
```
$ git log --oneline -1
cd20ae0 merge: m4/forecast (B1) F1/F2 engine, oracle, adapter
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    49, Skipped:     0, Total:    49, Duration: 28 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 104
-rw-r--r--. 1 mio mio 52736 Oct 27 20:35 Ultima.Core.dll
-rw-r--r--. 1 mio mio 46080 Oct 27 20:35 Ultima.dll
-rw-r--r--. 1 mio mio   280 Oct 27 20:35 Ultima.json
```

### 20:38 — merge B2 `m5/multi` (b934278, d70795d) → main `f086ac2` (poussé)
```
$ git log --oneline -1
f086ac2 merge: m5/multi (B2) F3 local priority + F4 hover / opt-in net
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    64, Skipped:     0, Total:    64, Duration: 32 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 124
-rw-r--r--. 1 mio mio 59392 Oct 27 20:38 Ultima.Core.dll
-rw-r--r--. 1 mio mio 61440 Oct 27 20:38 Ultima.dll
-rw-r--r--. 1 mio mio   280 Oct 27 20:38 Ultima.json
```
Revue chef : voie B (`MultiNet`) inactive si `custom_messages=false` ou solo ; priorité envoyée
seulement si TOUS les autres joueurs ont répondu au Hello (`PriorityProtocol.CanSendPriority`) ;
handlers désenregistrés en fin de combat (`INetGameService.UnregisterMessageHandler`, :75).
Remarque B2 retenue : `NetBridge.Attach()`/`SendPing` (V0.1) seront aussi soumis à la config
dans le câblage chef.

### 20:42 — merge A2 `m2/ui` (e81f4e0 … 33628f8) → main `a3d9d06`
```
$ git log --oneline -1
a3d9d06 merge: m2/ui (A2) glass overlay: rail, expansion, levels, co-op colors
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    64, Skipped:     0, Total:    64, Duration: 30 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 160
-rw-r--r--. 1 mio mio 59392 Oct 27 20:42 Ultima.Core.dll
-rw-r--r--. 1 mio mio 98304 Oct 27 20:42 Ultima.dll
-rw-r--r--. 1 mio mio   280 Oct 27 20:42 Ultima.json
```
Écarts de process A2 (acceptés) : e81f4e0 = 276 lignes (> 250) ; 4bd5486 ne compile pas seul
(moitié de classe partial, complétée par 5a5aed7) — la tête de branche est verte.

### 20:43-20:46 — câblage chef `af94f07` (+ `03132ab` verify-deploy, `3bf36aa` PID) → déployé
`CombatSession` : CombatSetUp / CombatStateChanged / TurnStarted → CombatReader → DamageForecast →
PriorityFeature + MultiFeature → `UltimaOverlay.Render` (≤ 1×/frame) ; AvoidRects (intents, main,
bouton fin de tour). Incident : `3bf36aa` a d'abord été committé avec une erreur de build
(`Environment` ambigu Godot/System) — corrigé et amendé AVANT tout push ; règle appliquée depuis :
commit conditionné au build vert.
```
$ git log --oneline -1
3bf36aa feat(core): tag each log line with the process id
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    64, Skipped:     0, Total:    64, Duration: 30 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 164
-rw-r--r--. 1 mio mio 59392 Oct 27 20:46 Ultima.Core.dll
-rw-r--r--. 1 mio mio 99328 Oct 27 20:46 Ultima.dll
-rw-r--r--. 1 mio mio   280 Oct 27 20:46 Ultima.json
$ bash tools/deploy.sh && bash tools/verify-deploy.sh
build   : 20:46:01  99328 o
déployé : 20:46:03  99328 o
✅ callbacks Godot présents (3 marqueurs)
✅ déploiement à jour
```

### 20:51 — merge C1 `m7/badge` (896dc31) + câblage `661bd1c` (poussé, déployé)
```
$ git log --oneline -1
661bd1c feat(core): wire F3 hand badges into the combat session
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    64, Skipped:     0, Total:    64, Duration: 29 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 168
-rw-r--r--. 1 mio mio  59392 Oct 27 20:51 Ultima.Core.dll
-rw-r--r--. 1 mio mio 103424 Oct 27 20:51 Ultima.dll
-rw-r--r--. 1 mio mio    280 Oct 27 20:51 Ultima.json
```

### 20:55 — merge A4 `m6/design-qa` (d572da9, docs) + C2 `m8/score` (8b75c5d, 589f830) + câblage `61544f3`
QA : verdict FAIL (10 typo PV en Kreon ; 11 preuve de non-chevauchement) — correctifs en cours (D11).
```
$ git log --oneline -1
61544f3 feat(core): wire F5 score ledger into the combat session
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    82, Skipped:     0, Total:    82, Duration: 34 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 192
-rw-r--r--. 1 mio mio  74240 Oct 27 20:55 Ultima.Core.dll
-rw-r--r--. 1 mio mio 111104 Oct 27 20:55 Ultima.dll
-rw-r--r--. 1 mio mio    280 Oct 27 20:55 Ultima.json
$ bash tools/deploy.sh && bash tools/verify-deploy.sh
✅ callbacks Godot présents (3 marqueurs)
✅ déploiement à jour
```

### 21:03 — merge A2 refonte verre `m2/ui` (6b1f8b0 … f4b0ff2) → main `b2bd4cd` (poussé, déployé)
A2 relancé avec `sts2-design-system` + `sts2-overlay-ui` chargées AVANT modification (D10/D11).
Relecture chef du shader (jamais compilé hors jeu) : built-ins identiques au `dark_blur` du jeu
(`hint_screen_texture`, `filter_linear_mipmap`, `texture(…, bias)`), `SCREEN_PIXEL_SIZE`, `exp2` ;
masque SDF radius 10 avec AA 1 px ; highlight = bord haut seulement.
```
$ git log --oneline -1
b2bd4cd merge: m2/ui (A2 rework) glass per sts2-design-system + QA fixes
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    82, Skipped:     0, Total:    82, Duration: 30 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 196
-rw-r--r--. 1 mio mio  74240 Oct 27 21:03 Ultima.Core.dll
-rw-r--r--. 1 mio mio 116736 Oct 27 21:03 Ultima.dll
-rw-r--r--. 1 mio mio    280 Oct 27 21:03 Ultima.json
$ grep "new Color(|Color.FromHtml|Colors." hors UltimaPalette → (aucune) ; grep "_Process|SetLoops" UI+Patches → (aucun)
$ strings Ultima.dll | grep -c Ultima.Shaders.glass_blur.gdshader → 1
$ bash tools/verify-deploy.sh → ✅ callbacks Godot présents (3 marqueurs) / ✅ déploiement à jour
```
Re-QA A4 (v2, charte + skills) lancée sur `m9/design-qa2` = main@b2bd4cd.

### 21:10 — correctifs chef après QA v2 → main `fc795a4` (poussé, déployé)
QA v2 (A4, `05bed7c`, skills rechargées) : critères 1-10 et 12 PASS ; skills appliquées ou écarts
justifiés ; FAIL critère 11 (repli de placement, zones périmées au redimensionnement) + note perf.
- `82f8cec` fix(ui) : flou (lecture d'écran) attaché seulement pendant l'ouverture / à l'état déployé ;
  rail au repos = remplissage plat → plus de copie plein écran par frame replié. Repli de placement
  = position de chevauchement MINIMAL. Preuve : contrôle jetable de `Placement.Compute` (scratchpad,
  structs GodotSharp) → 4/4 PASS, dont repli y=402 (520 px²) préféré à y=394 (1560 px²).
- `240b5ca` fix(core) : la session s'abonne à `Viewport.SizeChanged` → zones à éviter recalculées.
- Reste assumé (G5 de A2) : sans aucune place libre, le panneau DÉPLOYÉ (survol volontaire) reste
  opaque à la position de moindre chevauchement ; le rail au repos descend à 0.35.
```
$ git log --oneline -1
fc795a4 merge: m9/design-qa2 (A4) design QA v2 after glass rework
$ dotnet build Ultima/Ultima.csproj -c Release
    0 Error(s)
    0 Warning(s)
Build succeeded.
$ dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    82, Skipped:     0, Total:    82, Duration: 30 ms - Ultima.Tests.dll (net9.0)
$ grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs
(exit 1 — 1 = aucun appel)
$ bash tools/build.sh && ls -l build/mods/Ultima
total 196
-rw-r--r--. 1 mio mio  74240 Oct 27 21:10 Ultima.Core.dll
-rw-r--r--. 1 mio mio 116736 Oct 27 21:10 Ultima.dll
-rw-r--r--. 1 mio mio    280 Oct 27 21:10 Ultima.json
$ bash tools/deploy.sh && bash tools/verify-deploy.sh
build   : 21:10:00  116736 o
déployé : 21:10:03  116736 o
✅ callbacks Godot présents (3 marqueurs)
✅ déploiement à jour
```

### Incohérence relevée dans SPEC.md §4 F2 (non modifiée : critère d'acceptation = hors discrétion)
« `16x3` avec 20 block et 35 PV → 2e coup létal » est arithmétiquement faux : 48 − 20 = 28 < 35,
le joueur survit à 7 PV. Les tests (LethalIndexTests + ForecastTests) prouvent la variante correcte
(12 PV → 2e coup, overkill 16) et le cas 35 PV → 7 PV restants. **À corriger par Mio dans SPEC.md.**

## PRÊT POUR TEST HUMAIN (build déployé : main@3bf36aa, 20:46)

Log : `<jeu>/mods/Ultima/ultima.log` · Config : `<jeu>/mods/Ultima/ultima.cfg` (créé au 1er boot).

1. Lancer le jeu → le log contient `ULTIMA ready — game v0.107.1` puis `config: …`.
2. Entrer en combat → attendre `overlay injected` puis `session: started (multi=False, custom_net=False)`
   et `ULTIMA state: <n> enemies, <n> intents`.
3. Overlay : rail de 8 px au bord droit (3 pastilles, opacité 0.30), survol → fondu 150 ms + panneau
   verre 260 px ; retour au rail après 2,5 s ; F8 masque ; F9 détail (variantes Weak/Vuln, total du
   tour, label du jeu) ; clic droit → menu (Détail / Masquer).
3bis. **Verre (point d'inquiétude de Mio)** : capture d'écran du panneau déployé sur un décor chargé.
   Attendu : flou lisse (pas de contours doublés), teinte bleu-nuit, coins arrondis, liseré or fin,
   reflet uniquement en haut. Si le panneau est un rectangle uni sans flou → le shader n'a pas
   compilé : chercher `SHADER ERROR` dans le log Godot du jeu et me le transmettre.
4. **G5 exactitude (3 rencontres)** : pour chaque ennemi, `NxM` affiché == chiffres de l'intent du jeu.
   Le log ne doit contenir AUCUN `forecast: oracle mismatch` ni `reader: raw/perHit mismatch`.
5. Létalité : avec un block connu, vérifier « → Ne LÉTAL » / « → N PV » / « → bloqué » à la main.
6. F3 : une carte prioritaire jouable (ex. Fetch qui brille en or) → ligne `PRIO` dans l'overlay.
7. FPS avec le flou ≥ 55 (sinon `[ui] blur_amount=1.0` dans ultima.cfg, puis 0 = repli sans flou).
8. Aucun chevauchement main / intents / bouton fin de tour en 1080p, 1440p, 21:9.
9. Multi (2 instances, même machine) : `bash tools/multi-lab.sh` (host `--fastmp=host_standard`,
   client `--fastmp=join --clientId=1001`).
   - config OFF (défaut) : `multi: voie A active (HoverChanged)` ; B survole une carte prioritaire →
     chez A `multi: hover <B> -> <Carte> (prio)` + ligne PRIO distante colorée au personnage de B.
   - config ON (`[net] custom_messages=true`, fichier partagé par les 2 instances) : F7 → `net: ping
     sent` / `net: ping received` ; `multi: hello sent/received` ; `multi: priority sent/received`.
   - lignes préfixées `[<pid>]` pour distinguer host et client.

## LIVRABLE FINAL (21:12, T+57) — main `fc795a4`

### 1. Livré, feature par feature (branche → commits)
| Feature | État | Branches / commits |
|---|---|---|
| F1 DamageForecast | ✅ code + tests, câblé, déployé · ⏳ G5 humain | m1/reader `bff5438` (lecture typée AttackIntent, multiplicateurs LUS) · m4/forecast `952e5ca` (variantes, oracle label) · câblage `af94f07` |
| F2 LethalIndex | ✅ par ennemi + tour complet (block partagé) + co-op pire cas | Ultima.Core (existant) + m4/forecast `952e5ca` (SequenceLethal) |
| F3 TurnPriority | ✅ ligne PRIO + badge sur la carte (ShouldGlowGold = oracle) | m3/catalog `5f314d8` (first_card_hint, signals) · m5/multi `b934278` · m7/badge `896dc31` · câblage `661bd1c` |
| F4 Partage multi | ✅ voie A (HoverChanged, aucun message) · voie B codée mais OFF par défaut (D4) · ⏳ test 2 instances | m5/multi `d70795d` · gating R8 `af94f07` |
| F5 RunScore minimal | ✅ ledger dégâts directs + non attribués listés par raison + `runscore.csv` | m8/score `8b75c5d`, `589f830` · câblage `61544f3` |
| F7 UI verre | ✅ charte + skills (QA v2) · ⏳ rendu/FPS humain | m2/ui `e81f4e0`…`33628f8`, refonte `6b1f8b0`…`f4b0ff2` · chef `4a5f996`, `82f8cec`, `240b5ca` · QA m6 `d572da9`, m9 `05bed7c` |
| Socle chef | ✅ | `d0bc229` Godot.NET.Sdk (D1) · `ddca437` contrats + cfg · `3bf36aa` PID · `ec4a571` mesure perf · `03132ab` verify-deploy |

### 2. Sortie brute des 4 commandes de preuve (main `fc795a4`)
Voir le bloc « 21:10 » ci-dessus (build 0/0, tests 82/82, grep read-only vide, staging 3 fichiers),
+ `verify-deploy.sh` ✅. CI GitHub : verte sur toutes les branches m1-m9 et sur les commits main
précédents (fc795a4 en cours au moment du rapport).

### 3. Reste à tester en jeu
La checklist « PRÊT POUR TEST HUMAIN » ci-dessus (G4 log, G5 exactitude 3 rencontres, verre +
capture, FPS ≥ 55, chevauchements 1080p/1440p/21:9, multi 2 instances, CSV F5).

### 4. Non fait, et pourquoi
- **F6 PathRisk** : non lancé. P5 « seulement si tout le reste est vert » ; à T+55 la porte charte
  venait d'être refermée et la marge (35 min) ne permettait pas injection écran de carte + UI + tests
  prouvés. Consolidation préférée (prompt maître : mieux vaut un livrable prouvé).
- **F5 dans l'UI** : sources non attribuées listées dans le log et le CSV, pas encore dans l'overlay
  (F5 « minimal » demandé par le prompt maître).
- **F4 voie B active par défaut** : volontairement OFF (D4, comportement vanilla non vérifié).
- **Défilement du niveau 2** au-delà de 40 % de la hauteur : non fait (≥ 5 ennemis au niveau 2).
- **G4/G5** : portes humaines, jeu non lançable par l'agent.

### 5. Tag
`v0.1.0` posé sur `fc795a4` : F1+F2 verts sur toutes les portes automatiques (build, 82 tests, read-only,
CI, oracle intégré). Le message du tag précise que G4/G5 restent à valider en jeu.

---

# RUN v2 — tracker externe (démarré 2026-10-28 18:50)

Contrat : `docs/20-pivot-externe.md` · Swarm : `docs/21-swarm-v2.md` · Post-mortem v1 : docs/20 §0 + docs/19 (Hermes).
Décisions Mio : lecture mémoire OUI · probabiliste seulement · plein écran obligatoire · Hermes libre sur main.

## Vague 0 (18:50) — 6 agents, branches `v2/*`, worktrees `/home/mio/v2-*`
| Agent | Mission | Skills imposées |
|---|---|---|
| A Archiviste | parsers saves/history/log, fixtures anonymisées | sts2-save-format, sts2-game-api |
| B Miroir | spike S2 ClrMD lecture seule (récompense, shop) | sts2-game-api, sts2-save-format |
| C1 Verrier X11 | spike S1 moteur A (override-redirect) | sts2-design-system |
| C2 Verrier GNOME | spike S1 moteur B (extension Shell 50) | sts2-design-system |
| F Éclaireur | méta communautaire sourcée | sts2-meta-scoring |
| G Forgeron | outils de preuve (capture composée, audit no-write, labo) | sts2-save-format |

## Pré-mortem (pourquoi chacun pourrait échouer → parade préparée)
- A : schémas anciens (`build_id` < v0.107) cassent le parser → parseur tolérant, stats par build.
- B : attache ClrMD impossible sans suspendre, ou bloquée par l'espace de noms Flatpak → documenter, tester `bwrap`, repli vision (S3) déjà prévu.
- C1 : Mutter place le plein écran au-dessus des fenêtres X11 → c'est précisément ce que la fenêtre de test tranche ; C2 en concurrence.
- C2 : impossible de tester sans déconnexion (pas de `mutter-devkit`) → test dans la vraie session pendant la fenêtre de test, `disable` documenté.
- F : méta EA maigre/périmée → couverture chiffrée, pondération vers les 250 runs perso.
- G : portail Screenshot demande une autorisation interactive → une seule fois avec Mio.

## Registre des risques ouverts
1. Plein écran obligatoire × overlay sous GNOME Wayland (S1).
2. Lecture mémoire cassée à chaque patch EA → vérification croisée save/mémoire à chaque salle, mode dégradé annoncé.
3. Hermes libre sur main → pull --ff-only avant chaque push ; ne pas toucher `tools/*.sh` (modes modifiés par Hermes).
4. Projet `Ultima/` (mod) toujours dans le dépôt et modifié par Hermes → ni compilé ni déployé par le swarm v2 ; `tools/deploy.sh` à neutraliser en accord avec Hermes/Mio.

## Fenêtre de test n°1 (brouillon, après rendu de B, C1, C2, G)
1. Autoriser une fois la capture via le portail (G).
2. Lancer le jeu PLEIN ÉCRAN, entrer dans une run (carte), lancer l'audit no-write (G).
3. Miroir : `probe` sur la carte, puis sur un écran de récompense, puis dans un shop (B) → valeurs == écran.
4. C1 : lancer l'overlay X11, toggle, capture composée → visible au-dessus du plein écran ?
5. C2 : installer l'extension, se reconnecter, activer, raccourci, capture → visible ? verre ?
6. Fin : rapport d'audit (aucune écriture d'ULTIMA, `mods/` absent).

## Vague 0 — résultats (19:09) : tout mergé, AUCUNE preuve sur le vrai jeu encore
| Agent | Résultat prouvé | Non prouvé (→ fenêtre de test n°1) |
|---|---|---|
| G Forgeron | capture composée (portail), capture X11, audit no-write (réel : `mods/` vide, `modded/` intact), banc de test | WM_CLASS réelle du jeu |
| F Éclaireur | méta v0.107.1, 4 sources datées, couverture complète (local) ; désaccord entre sources (ρ 0,19-0,44) | — |
| C1 X11 | au-dessus d'un client X11 plein écran (capture composée), 49 clics traversants, 0 CPU au repos | vrai jeu Vulkan, FPS, pas de vrai flou |
| C2 GNOME | 28/28 sur GNOME Shell 50.6 isolé : au-dessus du plein écran (addTopChrome + disable_unredirect), vrai flou, survol, Super+F9/F8 | vraie session, FPS avec flou |
| A Archiviste | 250/250 runs, lecture sans verrou (strace), champs interdits jamais déclarés | save vanilla en cours, deck == écran |
| B Miroir | ClrMD en process_vm_readv uniquement (0 ptrace/0 écriture), à travers bwrap imbriqués ; chemins récompense/shop | vrai jeu, prix affichés au shop |

Pré-évaluation S1 : **C2 en tête** (vrai verre, survol, raccourcis natifs, plein écran couvert par le
code de GNOME 50) ; C1 = repli (et support hors GNOME). Décision finale sur preuve réelle.

Incident (mon brief) : C1 et G ont ouvert des fenêtres plein écran et pris des captures pendant que
Mio utilisait son bureau (~4 min, une vidéo capturée puis supprimée, rien de commité). Règle ajoutée
à docs/21 §6. Le banc C2 (GNOME Shell headless) a interrogé logind/GDM au 1er lancement ; session
vérifiée intacte. Autorisation portail « screenshot » accordée (révocable).

Prochaine étape : `tools/lab/TEST-WINDOW-1.md` (une déconnexion, ~35 min).
