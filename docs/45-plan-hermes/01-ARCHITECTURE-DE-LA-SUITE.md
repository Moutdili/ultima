# 01 — Architecture de la suite du plan (chef, 2026-11-10)

> Décision de Mio (2026-11-10) : le chef conçoit la suite et laisse la documentation ; Hermes exécute les tâches
> avec ses sous-agents. Ce fichier dit **dans quel ordre**, **qui touche quoi** et **quand le chef reprend la
> main**. Lis-le après `00-LISEZMOI.md`, avant d'ouvrir une tâche.

## 1. Où on en est
- Livré et mergé : T1 à T4 (v1.4), contrat v1.5 (`docs/46`), contrat v1.6 (`docs/51`).
- En cours chez toi : T5 (rapport `status/hermes-s36.md` : §1, §3, §4 faits mais **non poussés**, §2 reste).
- Confié, rien de livré : T7, T8, T9.
- Nouveau aujourd'hui : T6 débloqué et réécrit, T10, T11, T12.
- Prouvé sur le vrai jeu (fenêtre n°2) : chaîne jeu → mémoire → daemon → TUI/HUD, récompense, shop, survol.
  **Jamais prouvé** : événement, feu de camp, combat, co-op en direct, compagnon, FPS.

## 2. Cinq voies, une par dossier
Une voie = un sous-agent à la fois = un ensemble de dossiers. Deux voies ne touchent jamais le même fichier :
c'est ce qui permet de les lancer en parallèle sans conflit de fusion.

| Voie | Dossiers | Skills |
|---|---|---|
| **C** Calcul | `Ultima.Engine/Scoring/**` (sauf `Review/`), `Ultima.Engine/Paths/**`, leurs tests, `data/calibration/weights.json`, `data/catalog/**`, `data/meta/**` | `sts2-meta-scoring`, `sts2-combat-math`, `sts2-map-paths` |
| **R** Revue et données | `Ultima.Engine/Scoring/Review/**`, `Ultima.Engine/Scoring/Personal/**`, `Ultima.Engine/Scoring/Stats/**` (nouveau), leurs tests, `tools/index_*.py`, `tools/backtest/**`, `tools/calibration/**`, `data/calibration/*` sauf `weights.json` | `sts2-save-format`, `sts2-meta-scoring` |
| **D** Daemon et miroir | `Ultima.Daemon/**`, `Ultima.Mirror/**`, leurs tests | `sts2-daemon`, `sts2-memory-mirror`, `sts2-game-api` |
| **H** HUD | `overlay/gnome/**` | `sts2-external-overlay`, `sts2-design-system`, `sts2-performance` |
| **S** Console et compagnon | `tools/console/**`, `overlay/companion/**`, `tools/install.sh`, `tools/uninstall.sh` | `sts2-console-tui`, `sts2-companion-app` |

Toutes les voies chargent aussi `sts2-engine-contract`. **Hors voie pour tout le monde** (chef seul) :
`Ultima.Engine/Contracts/**`, `schemas/**`, `docs/22`, `docs/44`, `docs/46`, `docs/51`, `docs/ipc/*.sample.json`,
`tools/verify-all.sh`, `STATUS.md`.

Règle de frontière : quand une voie a besoin d'une autre (le daemon appelle un calcul qui n'existe pas encore), elle
code contre l'**interface du contrat** (`IRestAdvisor`, `IStatsBuilder`, `IScorer`…), dont l'implémentation par
défaut rend `null`. Elle n'attend pas l'autre voie et n'écrit pas dans ses dossiers.

## 3. Vagues
Chaque case renvoie à une section d'une fiche. Dans une vague, les cinq voies tournent en parallèle. Une vague est
finie quand chaque voie a poussé sa branche avec la porte verte et son rapport.

| Vague | C Calcul | R Revue et données | D Daemon et miroir | H HUD | S Console et compagnon |
|---|---|---|---|---|---|
| **1** Dette et v1.5 | T5 (pousser §1/3/4, finir §2), T8 §2.2-2.4 | T9 §1-2, T12 §1 | T8 §1 (A1, A2, B1, B4, B5, C2, D2, D5), T8 §3 | T8 §1 (A1, A3, B5, B6, C1, C2, D2-D6) | T8 §1 (A1, A3, B5, C1, C2, D5, D6), T8 §2.1 |
| **2** Choix et v1.6 | T10 §2-4, T9 §3 | T12 §2-3 | T7 §5.1-5.2, T10 §1 | T7 §5.3, T10 §5 | T7 §5.4, T10 §5 |
| **3** Stats et combat préparé | T6 §1 (A2, B3, C5) | T11 §1 | T11 §2, T6 §2 | T6 §3 | T11 §3 |
| **4** Combat publié | réglages après mesure | — | T6 §4 | T6 §4 | T6 §4 |

- **Vague 1** ne dépend de rien : elle peut partir tout de suite, les cinq voies ensemble.
- **Vague 2** part quand la vague 1 est mergée par le chef (T7 a besoin des décisions v1.5 appliquées ; T10 a
  besoin de la revue corrigée pour ses tests sur la fixture).
- **Vague 3** part après la vague 2. Elle ne dépend pas de la fenêtre de test n°3.
- **Vague 4** est **bloquée par la fenêtre de test n°3** : `EngineState.Combat` n'est publié qu'après la mesure FPS.

Si une voie finit avant les autres, elle prend sa case de la vague suivante **seulement** si la colonne
« Dépend de » de la fiche le permet. Sinon elle s'arrête et le dit dans son rapport.

## 4. Points de contrôle du chef
| Moment | Ce que fait le chef | Ce qu'il te faut avoir rendu |
|---|---|---|
| Fin de vague 1 | Relit, merge, répare, relance la porte | 5 branches + rapports, demandes de contrat v1.6 |
| Fin de vague 2 | Merge, puis **fenêtre de test n°3** avec Mio (`FT3-fenetre-de-test-3.md`) | Liste des gestes à faire en jeu, par voie |
| Après la fenêtre n°3 | Décide : relecture 1 Hz gardée ou non, lectures en combat activées ou non | — |
| Fin de vague 3 | Merge, contrat v1.7 si des demandes le justifient | — |
| Fin de vague 4 | Fenêtre de test n°4 (combat, co-op en direct) | Liste des gestes |

## 5. Branches, rapports, porte
- Une branche par voie et par vague, depuis `origin/main` à jour : `hermes/v<vague>-<voie>` (`hermes/v1-calcul`,
  `hermes/v1-revue`, `hermes/v1-daemon`, `hermes/v1-hud`, `hermes/v1-surfaces`). Tu ne pousses pas sur `main` :
  le chef merge.
- Un rapport par branche : `status/hermes-sNN.md` (suite de ta numérotation, à partir de s37), au format de
  `00-LISEZMOI.md` §4. En tête : la vague, la voie, les sections de fiches couvertes.
- Porte : `tools/verify-all.sh` sort 0 **sur la branche, après fusion de `origin/main`**. Colle la sortie entière
  dans le rapport, pas un extrait.
- Un critère de fiche = un test nommé. Le rapport donne le nom du test en face de chaque critère.
- Ce qui n'a tourné que sur fixture ou cible simulée va dans « NON prouvé », avec le geste à faire en jeu.

## 6. Ce qui n'est pas dans ce plan
| Sujet | Pourquoi | Qui |
|---|---|---|
| Manques du TUI | Mio doit donner sa liste (« il manque une tonne de trucs », fenêtre n°2) | Mio, puis une fiche T13 |
| Design des surfaces | Décision de Mio : en dernier | chef, après la vague 4 |
| Note de « passer » (`−0,35` magique, `status/hermes-s36.md` §5) | Ta proposition attend le go de Mio | Mio |
| Replays `latest.mcr`, export CSV, collection et succès (docs/25 D4, D5, E1, E2) | Après les statistiques | plus tard |
| Installation réelle, réécriture finale de l'historique Git | Environnement de Mio, dépôt public | chef |

## 7. Règles qui ne changent pas
Celles de `00-LISEZMOI.md` §3 s'appliquent à chaque vague : contrat intouchable, lecture seule absolue du jeu,
bureau et environnement réels de Mio intouchables, dépôt public sans asset ni identifiant, commits signés Hermes
sans mention d'assistant, jamais `--force`.
