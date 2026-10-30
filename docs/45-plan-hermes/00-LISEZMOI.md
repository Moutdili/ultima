# 45 — Plan de travail pour Hermes (2026-10-30)

> Décision de Mio : Hermes exécute ce dossier avec ses propres sous-agents ; le chef garde le contrat,
> l'intégration, les réparations et les tests sur le vrai jeu avec Mio. Lis ce fichier en entier, puis
> les tâches dans l'ordre. Chaque tâche est autonome (rôle, contexte, périmètre, critères, rendu).

## 1. Ce qui reste à faire, classé

| # | Tâche | Qui | Dépend de | Fichier |
|---|---|---|---|---|
| T1 | Daemon v1.4 : événements, enchantements, réglages, modules déclaratifs, plugins, revue de run | **Hermes** | — (repart de `origin/v2/cableur3`) | `T1-daemon-v14.md` |
| T2 | Console v1.4 : thèmes intégrés, TUI thématisé, onglet Apparence, commandes, polices, README | **Hermes** | T1 pour `plugin test` (sinon marqué « à brancher ») | `T2-console-v14.md` |
| T3 | Compagnon v1.4 : thèmes, polices, modules perso, bornes | **Hermes** | T2 (thèmes intégrés) | `T3-compagnon-v14.md` |
| T4 | HUD v1.4 : thèmes, polices, disposition, modules perso | **Hermes** | T2 (thèmes intégrés) | `T4-hud-v14.md` |
| T5 | Calcul : pouvoirs comportementaux, derniers trous, retrait unique | **Hermes** | — | `T5-calcul.md` |
| T6 | Fonctions HDT qui demandent un nouveau contrat (piles, feu de camp, stats) | **bloqué** | contrat v1.5 du chef | `T6-en-attente-contrat.md` |
| T7 | **Notes sous les choix façon HDT** (récompenses + shop), disparition immédiate, opacité 0,8 → 1,0 au survol | **Hermes** | contrat v1.5 du chef | `T7-notes-sous-les-choix.md` |
| — | Fenêtre de test n°2 sur le vrai jeu (chaîne complète, miroir 2, FPS, 10 situations) | **chef + Mio** | rien (l'état actuel de main suffit) | `overlay/gnome/TEST-WINDOW.md` |
| — | Contrat v1.5, intégration, réparations, installation réelle, réécriture finale de l'historique | **chef** | — | — |

Ordre conseillé : **T1 → T2 → (T3 ∥ T4)**, T5 quand tu veux (fichiers séparés). T3 et T4 peuvent démarrer
avant la fin de T2 en s'appuyant sur `themes/sts.json` et leurs propres fixtures.

## 2. Lecture obligatoire avant toute tâche
1. `docs/44-personnalisation.md` (contrat v1.4) + `schemas/*.schema.json` + `themes/sts.json`.
2. `Ultima.Engine/Contracts/*.cs` (dont `EngineContractsV14.cs`) et `docs/22-contrat-moteur.md`.
3. Les skills `sts2-*` citées dans chaque tâche (dossier partagé des skills), à transmettre à tes sous-agents.
4. `STATUS.md`, section « Reprise 2026-10-30 » (décisions de Mio et du chef).

## 3. Règles communes (elles s'ajoutent aux tiennes)
- **Porte : `tools/verify-all.sh` doit sortir 0 avant chaque push sur main.** Il lance .NET, le compagnon
  (pytest), le HUD (`npm test`) et la console (pytest + Textual ; Python via `ULTIMA_TEST_PYTHON` ou le venv
  `~/.cache/ultima-tui-venv`). `hermes-verify` seul ne suffit plus : il ne voit pas les suites Python/JS.
- **Contrat** (`Ultima.Engine/Contracts/**`, `schemas/**`, `docs/22`, `docs/44`) : propriété du chef. Besoin
  d'un champ ⇒ écris la demande dans ton `status/hermes-sNN.md` (section « Demandes de contrat ») et continue
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
