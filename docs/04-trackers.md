# 04 — Paysage concurrentiel (balayage réel, octobre 2026)

Méthode : GitHub Search API (5 requêtes, 25-32 résultats chacune), Steam Workshop
(appid 2868840), Nexus, puis **clone et lecture du code** de 10 dépôts.
Tous les chiffres viennent de l'API ou du code, pas d'un souvenir.

## Vue d'ensemble

| Projet | ★ | Lang | Dernier push | Ce qu'il fait |
|---|---:|---|---|---|
| **Gennadiyev/STS2MCP** | 533 | C# | 2026-07-29 | Mod + REST localhost:15526 + MCP : expose l'état complet et permet à un agent de **jouer** (SP + co-op) |
| **Alchyr/BaseLib-StS2** | 460 | C# | 2026-10-07 | Librairie de base pour mods de contenu |
| **Alchyr/ModTemplate-StS2** | 306 | C# | 2026-10-16 | Template de projet de mod |
| **Torch1230/CombatSolver** | 129 | C# | 2026-10-07 | **Solveur de combat** : simule main, deck, intents, potions, plusieurs tours ; recommande une route |
| **erasels/Minty-Spire-2** | 107 | C# | 2026-09-09 | Compilation QoL |
| **Rain156/sts2-RMP-Mods** | 141 | C# | 2026-05-24 | Lobby multi > 4 joueurs |
| **emptylower/STS2-Game-Lobby** | 107 | C# | 2026-09-23 | Lobby multi tiers |
| **thequantumfalcon/spirescope** | 28 | Python | 2026-10-10 | Dashboard local (SSE) : deck, runs, analyse, co-op `?player=N` |
| **BAIGUANGMEI/STS2-DamageTracker** | 22 | C# | 2026-04-12 | Overlay **multi** : dégâts par joueur (run + combat) |
| **freude916/sts2-multiplayer-damage-stat** | 20 | C# | 2026-03-07 | Dégâts par joueur en co-op, via un patch de `NMultiplayerPlayerState` |
| **999dulgi/sts2decktracker** | 6 | C# | 2026-08-08 | Pioche / défausse / top card (overlay Godot) |
| **ronishae/sts2-deck-tracker** | 3 | C# | 2026 | **Attribution des dégâts** par carte/relique/potion + CSV |
| **Hsyuan54670/...Damage-Logger** | 2 | C# | 2026-03-16 | HUD de dégâts co-op |
| **ebadon16/sts2-advisor** | 6 | C# | 2026-09-30 | Overlay de **notes de cartes** (S/A/B/C) + win-delta + synergie |
| **yogan/sts2-tierlist-overlay** | 0 | C# | 2026-10-26 | Tier list communautaire sur cartes/reliques/potions in-game |
| **Divank122/CardStats** | 0 | C# | 2026-04-01 | Stats de cartes |
| **davekalina/spire2-winrate-tracker** | 0 | C# | 2026-09-05 | Écran de win rates dans le compendium |
| **univwang/sts2-RelicStatsTracker** | 1 | C# | 2026-03-18 | Stats de reliques |
| **BAKAOLC/STS2-ViewedCardsStatistics** | 7 | C# | 2026 | Cartes vues / prises |
| **crazytan/SpireTracker** | 0 | C# | 2026-03-22 | Badge NEW sur les reliques non découvertes |
| **MrAnime-0001/Spire-Forge** | 1 | JS | 2026-10-21 | Conseiller de build web |
| **Sezmol/spire-scryer** | — | C# | 2026 | Overlay Twitch (mod → Worker → PubSub) |
| **Spire Codex** (site + overlay) | — | web | 2026-05 | Base de données + overlay + **tracker qui lit `current_run.save`** |
| **Adventure Log** (Workshop 3748601333) | — | C# | 2026 | Journal de combat scrollable (cartes jouées, dégâts, pouvoirs) |
| **STS2 Modding Assistant MCP** (Nexus 345) | — | — | 2026 | MCP 151 outils pour modder STS2 |
| **proto-git/sts2-coach** | 1 | TS | 2026-04-27 | Coach vocal + overlay (save watcher) |

## Analyse des 5 qui comptent vraiment

### 1. STS2MCP — la référence pour LIRE l'état

- Mod C# qui démarre un **serveur HTTP sur `localhost:15526`** + MCP optionnel.
- `McpMod.StateBuilder.cs` : `BuildBattleState`, `BuildPlayerState`, `BuildEnemyState`,
  `BuildPileCardList`, `BuildCardInfo`… → **la carte complète de ce qu'on peut lire**.
- Extrait d'intent (lignes ~1342-1360) :
  ```csharp
  foreach (var intent in moveState.Intents)
      ["type"] = intent.IntentType.ToString(),
      string label = intent.GetIntentLabel(targets, creature).GetFormattedText();
  ```
- Testé sur `v0.103.2` (nous : v0.107.1) — attention aux écarts.
- **Ce qu'on reprend** : la structure de lecture (players, hand, piles, enemies, intents,
  relics, potions), l'idée d'un bridge HTTP local pour les outils externes.
- **Ce qu'on ne prend pas** : il *agit* sur le jeu (agent qui joue). Nous restons read-only.

### 2. CombatSolver — le solveur (et pourquoi on ne l'utilise pas tel quel)

- Simule la main, le deck, les intents, les potions, les reliques, le RNG, **sur plusieurs tours**.
- Expose `PreCombatForecastApi` (API v6) aux autres mods : prévision de **战损** (dégâts subis
  sur tout le combat) + route recommandée.
- Contraintes : **Windows uniquement**, **solo uniquement**, lance un **processus headless**
  du jeu pour simuler. 69 Mo de dépôt.
- **Ce que ça prouve** : le calcul de dégâts prévisionnels est faisable et intéressant.
- **Pourquoi ULTIMA reste différent** : in-process, Linux, multi, et réponse **instantanée**
  par coup (`16x3` → 3e coup létal) au lieu d'une recherche arborescente de plusieurs secondes.
  On peut s'en inspirer plus tard pour un mode « simulation profonde » optionnel.

### 3. Les trackers multi existants — et la découverte importante

`BAIGUANGMEI/STS2-DamageTracker` (22★) et `freude916/sts2-multiplayer-damage-stat` (20★)
affichent les dégâts **par joueur** en co-op.

**Comment ils font ?** Vérifié par grep : **aucun `INetMessage`, aucun `SendMessage`,
aucun `RegisterMessageHandler`.** Ils lisent simplement l'état déjà synchronisé par le jeu
(`NMultiplayerPlayerState`, `RunManager.Instance.NetService` pour connaître le type de session).

→ Conclusion : **personne n'a encore écrit de message réseau custom dans un mod STS2 public.**
Deux conséquences :
1. Notre feature « broadcast » est réellement nouvelle (bon pour la différenciation).
2. Elle est **non éprouvée** → il faut une voie de repli qui marche sans réseau custom.
   Voir `docs/08-multiplayer.md` (revu) : le cœur de F3 passe par `HoveredModelTracker`.

### 4. `ronishae/sts2-deck-tracker` — l'attribution de dégâts

Le plus abouti sur la comptabilité : Vulnerable FIFO, poison au prorata, forge en queue,
doom, orbes/focus, génération de cartes en arbre. Il **documente ses cas non attribuables**
au lieu de mentir. C'est le modèle à reprendre pour F5 (RunScore).

### 5. UI : `999dulgi` et les overlays de notes

- `999dulgi` = le pattern d'injection le plus propre (Harmony sur `NCombatUi._Ready`,
  click-through, position mémorisée).
- `ebadon16/sts2-advisor` et `yogan/sts2-tierlist-overlay` montrent le pattern « note/grade
  affiché à côté de la carte » : c'est exactement le placement qu'on veut pour les badges
  de létalité.

## Matrice concurrentielle

| Feature | Qui le fait | ULTIMA |
|---|---|---|
| Pioche / défausse / top card | 999dulgi | ✅ |
| Dégâts par joueur (co-op) | BAIGUANGMEI, freude916, Hsyuan | ✅ (en mieux : + % PV) |
| Attribution dégâts par carte | ronishae | ✅ |
| Journal de combat | Adventure Log | ✅ (compact) |
| Lecture d'état pour agent | STS2MCP | ✅ (read-only) |
| Notes / tier lists | ebadon16, yogan, Spire Codex | ❌ (V2) |
| Simulation multi-tours | CombatSolver (Windows) | ❌ (V3 optionnel) |
| **Dégâts hypothétiques avec Weak** | **personne** | ✅ |
| **Index du coup létal (16x3)** | **personne** | ✅ |
| **Alerte « carte à jouer en premier »** | **personne** | ✅ |
| **Broadcast d'alertes entre joueurs** | **personne** | ✅ (nouveau) |
| Risque de chemin in-game | partiel (CombatSolver, Windows) | ✅ (léger, Linux) |

## Ce qu'on réutilise (et sous quelle licence)

| Source | Réutilisation | Prudence |
|---|---|---|
| STS2MCP | structure de lecture d'état, liste des champs utiles | **réimplémenter**, pas copier |
| ronishae | modèle d'attribution + liste de cas limites | **réimplémenter** |
| 999dulgi | pattern d'injection UI | **réimplémenter** |
| BaseLib | conventions de packaging | dépendance inutile pour un mod read-only |

**Règle** : on lit pour comprendre, on réimplémente. Aucun copier-coller de code tiers
sans vérifier la licence du dépôt.

## Risques concurrentiels (à surveiller)

1. **CombatSolver passe Linux + multi** → il mangerait F1/F2. Aujourd'hui : Windows/solo.
2. **STS2MCP ajoute un overlay** → il a déjà toute la lecture d'état.
3. **Spire Codex** couvre lookup + tracker via save file (temps réel annoncé).
4. **25 000+ entrées Workshop** : la découvrabilité est un problème produit, pas technique.

Notre défense : être **in-process, Linux, multi, read-only, et instantané**. Un solveur
qui réfléchit 3 secondes ne répond pas à « est-ce que ce coup me tue maintenant ».
