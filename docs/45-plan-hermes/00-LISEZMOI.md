# 45 — Plan de travail pour Hermes (2026-10-30)

> Décision de Mio : Hermes exécute ce dossier avec ses propres sous-agents ; le chef garde le contrat,
> l'intégration, les réparations et les tests sur le vrai jeu avec Mio. Lis ce fichier en entier, puis
> les tâches dans l'ordre. Chaque tâche est autonome (rôle, contexte, périmètre, critères, rendu).

## 1. Ce qui reste à faire, classé

> **Mise à jour du 2026-11-10** : l'ordre d'exécution, les voies parallèles et les points de contrôle sont dans
> **`01-ARCHITECTURE-DE-LA-SUITE.md`**. Lis-le juste après ce fichier.

| # | Tâche | État | Vague | Fichier |
|---|---|---|---|---|
| T1-T4 | Daemon, console, compagnon, HUD en v1.4 | **mergé** (2026-10-30) | — | `T1` à `T4` |
| T5 | Calcul : retrait unique, pouvoirs comportementaux, derniers trous, Anciens | en cours (`status/hermes-s36.md`, non poussé) | 1 | `T5-calcul.md` |
| T6 | Combat : piles comptées, probabilité de pioche, dégâts entrants, létal, potions | **débloqué**, publication après mesure FPS | 3-4 | `T6-combat-et-fonctions-hdt.md` |
| T7 | Notes sous les choix façon HDT (récompenses + shop), disparition immédiate, opacité 0,8 → 1,0 | à faire | 2 | `T7-notes-sous-les-choix.md` |
| T8 | Contrat v1.5 appliqué (18 décisions) + défauts vus en jeu + feu de camp + combat derrière un réglage | à faire | 1 | `T8-v15-et-defauts-du-jeu.md` |
| T9 | Leçons de la victoire co-op A10 : revue de run en co-op, biais de l'historique, décisions de calcul | à faire | 1-2 | `T9-run-coop-a10-gagnee.md` |
| T10 | Contrat v1.6 appliqué : double boss, feu de camp multi-actions, offres enchaînées, 8 potions, co-op dans les notes | à faire | 2 | `T10-contrat-v16-applique.md` |
| T11 | Statistiques et historique (op `stats.open`, console, compagnon) | à faire | 3 | `T11-statistiques.md` |
| T12 | Données : index des runs, biais solo/co-op, calibration, échelle des notes | à faire | 1-2 | `T12-donnees-et-calibration.md` |
| FT3 | Fenêtre de test n°3 sur le vrai jeu (FPS, cases, feu, co-op) | **chef + Mio**, après la vague 2 | — | `FT3-fenetre-de-test-3.md` |
| — | Contrats (v1.6 livré : `docs/51`), intégration, réparations, installation réelle, réécriture finale de l'historique | **chef** | — | — |

Toutes les tâches sont pour **Hermes**, sauf mention contraire. Dans une vague, les cinq voies (calcul, revue et
données, daemon et miroir, HUD, console et compagnon) tournent en parallèle, chacune dans ses dossiers.

## 2. Lecture obligatoire avant toute tâche
1. `docs/44-personnalisation.md` (contrat v1.4), `docs/46-contrat-v15.md`, `docs/51-contrat-v16.md` +
   `schemas/*.schema.json` + `themes/sts.json`.
2. `Ultima.Engine/Contracts/*.cs` (de `EngineContracts.cs` à `EngineContractsV16.cs`) et `docs/22-contrat-moteur.md`.
3. Les skills `sts2-*` citées dans chaque tâche (dossier partagé des skills), à transmettre à tes sous-agents.
4. `STATUS.md`, section « Reprise 2026-10-30 » (décisions de Mio et du chef).

## 3. Règles communes (elles s'ajoutent aux tiennes)
- **Porte : `tools/verify-all.sh` doit sortir 0 avant chaque push.** Depuis le 2026-11-10 tu pousses des branches
  `hermes/v<vague>-<voie>`, le chef merge sur main. Il lance .NET, le compagnon
  (pytest), le HUD (`npm test`) et la console (pytest + Textual ; Python via `ULTIMA_TEST_PYTHON` ou le venv
  `~/.cache/ultima-tui-venv`). `hermes-verify` seul ne suffit plus : il ne voit pas les suites Python/JS.
- **Contrat** (`Ultima.Engine/Contracts/**`, `schemas/**`, `docs/22`, `docs/44`, `docs/46`, `docs/51`) : propriété du
  chef. Besoin d'un champ ⇒ écris la demande dans `demandes-de-contrat.md` (section « v1.6 ») et continue
  sur le reste ; ne le modifie pas toi-même. Exception : les ajouts explicitement prévus dans une tâche.
- **Lecture seule absolue** : jamais lire la mémoire du VRAI jeu, s'y attacher, le lancer ; jamais écrire dans
  son dossier ou ses données ; probabiliste seulement (aucun tirage caché, ni ordre de la pioche).
- **Bureau de Mio** : aucune fenêtre visible, capture d'écran, entrée simulée, installation/activation de
  l'extension, `gsettings`/`dconf`, ni lancement de GNOME Shell. Tests en headless uniquement.
- **Environnement réel intouchable** : le daemon de Mio (lancé à la main), `~/.config/ultima/`,
  `~/.local/share/ultima/`, `~/.local/state/ultima/`, `$XDG_RUNTIME_DIR/ultima/`, `~/.local/bin/ultima`,
  `~/.config/fontconfig/`. Tests avec `HOME`/`XDG_*`/`ULTIMA_*` temporaires.
- **Dépôt public** : aucune police non libre ni police du jeu commitée, aucun texte/asset du jeu, aucun chemin
  utilisateur ni identifiant Steam.
- **Git** : tes commits restent signés Hermes ; aucune ligne Co-Authored-By, aucune mention d'un assistant ou
  d'un éditeur d'IA. `git fetch` + rebase/merge de origin/main avant push ; jamais `--force`.

## 4. Rendu attendu (par tâche)
`status/hermes-sNN.md` : fait / **prouvé** (commande + sortie brute courte) / **NON prouvé** (rien n'a tourné sur
le vrai jeu ni dans la vraie session tant que le chef et Mio ne l'ont pas testé) / reste / demandes de contrat.
Le chef relit, répare si besoin et prépare les tests réels avec Mio.
