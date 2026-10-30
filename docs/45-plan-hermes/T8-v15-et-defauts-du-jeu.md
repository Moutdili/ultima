# T8 — Appliquer le contrat v1.5 + défauts vus sur le vrai jeu (fenêtre de test n°2)

**À lire d'abord** : `docs/46-contrat-v15.md` (réponses du chef à tes 18 demandes + ajouts v1.5).
**Skills** : selon la partie touchée (`sts2-daemon`, `sts2-console-tui`, `sts2-companion-app`, `sts2-external-overlay`,
`sts2-meta-scoring`, `sts2-memory-mirror`). Périmètres habituels de T1-T5.

## 1. Appliquer les décisions de docs/46 §1 (chaque surface, un test par décision)
- A1 : `EventView.Name` / `EventOptionView.Label` nullables ⇒ « — » partout.
- A2 : `ContractShapeTests` vérifie la forme EXACTE de `docs/ipc/state.v14.sample.json` (déjà rafraîchi en v1.5
  par le chef : `combat`/`restSite` à null, `contractMinor` 5) ; retirer la tolérance des champs additifs v1.4/v1.5.
- A3 : `auto-perso` depuis `run.characterId` — une fonction par surface, mêmes cas de test.
- B1 : daemon : `grade` de module hors S..F ⇒ `null`. B4 : table d'écrans unique (`ScreenIds.All`).
- B5 : daemon remplit `CardView.CostKind` ; surfaces : « X » vs « injouable ». B6 : ton `shadow`.
- C1 : `appearance.hud.position.side` fait autorité partout ; écrivains synchronisent `hud.side`.
- C2 : priorité des écrans `ModuleSetting.Screens` > `CustomModuleView.Screens` > défaut, appliquée par chaque surface.
- D2/D3/D4/D5 : confirmer par des tests (désactivé atténué, `background`/`border` surchargeables, `tintOpacity`
  = verre ouvert, bornes ramenées + avertissement `ultima doctor`).
- D6 : le HUD passe entièrement aux tons du contrat ; supprimer le shim `palette.py` du compagnon et mettre à jour
  `test-render.js`.
- `ultima` : `CONTRACT_MINOR = 5` est déjà posé ; afficher `CostKind` et `Community` là où les notes sont détaillées.

## 2. Défauts vus sur le vrai jeu (à reproduire par un test, puis corriger)
1. **Polices du jeu absentes** : `~/.local/share/ultima/fonts/jeu/` est vide après `tools/install.sh` (`ultima font
   list` : jeu 0). L'extraction locale (`game/pck/fonts`, dossier ignoré par git, ou l'installation Steam) doit
   remplir `jeu/` ; jamais de commit de ces fichiers.
2. **« Courbe de coût à 1 énergie −4 »** sur TOUTES les cartes à 1 énergie, deux récompenses de suite, deck de 15-16
   cartes (Nécrolieuse A0, actes 1, étages 6 et 9), y compris après `ecf4c34`. Le texte précédent disait « 2/4 cartes
   à 1 énergie » pour un deck de 14. Vérifier le comptage sur le deck réel (fixture tirée d'une save anonymisée).
3. **Retrait noté F 75 or** (« meilleur retrait : Bodyguard ») au shop de l'étage 6 avec un deck de départ quasi
   intact : un retrait de Frappe/Défense à 75 or ne devrait pas être F. Revoir l'échelle unique du retrait.
4. **Notes C tassées au shop** : 7 articles sur 13 en C (Mio confirme C pour ce shop, mais vérifier que l'échelle
   n'écrase pas les écarts : 3 potions toutes C, cartes communes toutes C).

## 3. Combat et feu de camp (T6) — préparer, NE PAS publier `combat` avant la mesure FPS
- Daemon : `RestSiteView` (options visibles + note + carte conseillée pour l'amélioration) — peut être publié.
- Calcul : note du repos vs amélioration (PV, prochain élite/boss, meilleure cible d'amélioration).
- `CombatView` : écrire le code derrière un réglage désactivé par défaut (`combatReads: false` dans `settings.json`,
  hors contrat), avec un test qui prouve zéro lecture mémoire en combat quand il est désactivé. Le chef et Mio
  mesureront les FPS avant de l'activer.

## 4. Critères
- `tools/verify-all.sh` vert avant chaque push ; un test par décision de docs/46 §1 et par défaut du §2.
- Rapport : fait / prouvé / NON prouvé / demandes de contrat restantes (dans `demandes-de-contrat.md`, section v1.5).
