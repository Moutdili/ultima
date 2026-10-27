Tu travailles dans /home/mio/sts2 (dépôt git `Moutdili/ultima`, branche main, remote SSH déjà configuré).
Projet : ULTIMA, mod de combat intelligence READ-ONLY pour Slay the Spire 2 (v0.107.1, Godot 4.5.1, C# net9.0), sur Linux Fedora.

BUDGET : 120 minutes. Vérifie l'heure au départ (`date +%H:%M`) et tiens un chrono interne.
À T+90, tu arrêtes toute feature non committée et tu passes au rapport final. Un livrable
qui compile et qui est prouvé vaut mieux que sept features à moitié faites.

════════════════════════════════════════════════════════════════════════
1. NON-NÉGOCIABLES
════════════════════════════════════════════════════════════════════════

R1. READ-ONLY. Le mod observe. Interdiction absolue d'appeler CardPileCmd, DamageCmd,
    PowerCmd.Apply, CreatureCmd, ni de muter un modèle. Tu peux vérifier à tout moment :
    `grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs`
    doit ne rien retourner hors commentaires.

R2. NE REFAIS PAS CE QUI EXISTE. Déjà écrit, testé et poussé :
    - Ultima.Core : LethalIndex + DamageMath + 12 tests verts (`dotnet test`).
    - Ultima/Net : UltimaPingMessage + NetBridge (broadcast custom, compile).
    - Ultima/Overlay : V0.1 (F7 ping, F8 hide).
    - Container, CI, Makefile, tools/doctor.sh (14 checks), data/catalog/*.json.
    Tu ne réécris pas ces fichiers. Tu les utilises.

R3. ZÉRO RECHERCHE. Toute la connaissance est dans docs/. Tu ne lis PAS game/pck/src/Core
    en entier, tu ne fais pas de `rg` exploratoire de plus de 2 commandes par question.
    Si une API te manque, elle est dans docs/13-prior-art-playbook.md ou docs/11-api-catalog.md.
    Si elle n'y est pas, tu ouvres UN fichier précis et tu cites fichier:ligne.

R4. UN FICHIER, UN PROPRIÉTAIRE. Voir §5. Tu n'édites jamais un fichier hors de ton périmètre :
    si tu as besoin d'un changement ailleurs, tu le notes dans status/<agent>.md et le
    chef d'orchestre (toi, hors sous-agent) l'applique.

R5. CHAQUE PATCH HARMONY EST DANS UN try/catch qui logge dans ModEntry.Write.
    Un mod ne crashe jamais le jeu.

R6. PAS DE VALEUR DE JEU EN DUR. Dégâts, coûts, multiplicateurs : lus dans les modèles
    (DamageCalc, DynamicVars, pouvoirs) ou passés en paramètre. Une constante de jeu dans
    le code = bug.

R7. COMMIT + PUSH IMMÉDIAT après chaque unité de travail. Jamais de `git add -A`.
    Jamais de `--force`. Jamais de merge de main dans une branche.

════════════════════════════════════════════════════════════════════════
2. LECTURE (10 min max, dans cet ordre, en entier)
════════════════════════════════════════════════════════════════════════

1. SPEC.md                          → le contrat produit (F1-F7, critères d'acceptation)
2. AGENT-MANUAL.md                  → environnement, boucle de build, anti-patterns
3. docs/13-prior-art-playbook.md    → LES APIs exactes à utiliser + les 12 pièges
4. docs/15-swarm-playbook.md        → topologie git, ownership, portes, protocole de merge

Ne lis rien d'autre avant M0. Les 2 548 lignes d'audit sont dans research/notes/*.md :
tu n'y vas QUE si docs/13 ne suffit pas, et alors tu lis la section concernée, pas le fichier.

════════════════════════════════════════════════════════════════════════
3. M0 — BOOTSTRAP ET PREUVE (10 min, toi seul, aucune délégation)
════════════════════════════════════════════════════════════════════════

a) `bash tools/doctor.sh` → doit finir sur "14 ok / 0 manquants". Si non : corrige
   l'environnement avant tout le reste (le jeu est en Flatpak, chemin auto-détecté).
b) `dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release` → 12/12 verts.
c) `bash tools/build.sh && bash tools/deploy.sh` → Ultima.dll + Ultima.Core.dll + json
   dans `<jeu>/mods/Ultima/`.
d) Lis `data/catalog/stats.json` et `data/catalog/priority_cards.json` (formes seulement).
e) Écris `STATUS.md` avec le chrono de départ et l'état des portes G1-G6 (docs/15 §6).
f) Commit `chore: bootstrap verified` et push.

Si M0 échoue et que tu ne peux pas corriger en 10 min : STOP, écris le blocage dans
STATUS.md et rends la main. Ne pars pas en exploration.

════════════════════════════════════════════════════════════════════════
4. PLAN DE LIVRAISON (priorité stricte)
════════════════════════════════════════════════════════════════════════

P0  F1 DamageForecast + F2 LethalIndex câblés dans l'UI     (le différenciateur)
P1  F3 TurnPriority local (ShouldGlowGold + catalogue)      (visible, rapide)
P2  F4 multi : vérifier le POC ping, puis alerte partagée   (la « folie »)
P3  F7 polish UI (opacité, placement, pas de chevauchement)
P4  F5 RunScore minimal (dégâts directs + CSV)
P5  F6 PathRisk (seulement si tout le reste est vert)

Tu ne descends dans la liste QUE si le niveau courant est committé, testé, poussé.
À T+90 : tu t'arrêtes où tu es, tu finis le rapport.

════════════════════════════════════════════════════════════════════════
5. SWARM — vagues et propriété
════════════════════════════════════════════════════════════════════════

Tu es le chef d'orchestre. Tu ne codes PAS les features toi-même : tu prépares, tu merges,
tu vérifies, tu corriges les coutures. Tu possèdes uniquement :
  Ultima/ModEntry.cs · Ultima/Patches/** · Ultima/Ultima.csproj · STATUS.md

Prépare les worktrees AVANT de spawner :

```bash
git worktree add ../wt-reader  -b m1/reader
git worktree add ../wt-ui      -b m2/ui
git worktree add ../wt-catalog -b m3/catalog
```

VAGUE 1 — trois sous-agents en parallèle (25 min chacun, max)

┌─ A1 READER ────────────────────────────────────────────────────────────
│ Worktree : /home/mio/wt-reader   Branche : m1/reader
│ Possède  : Ultima/GameApi/**, Ultima/Model/**
│ Tâche :
│  1. Créer Ultima/Model/CombatSnapshot.cs EXACTEMENT selon le contrat de docs/15 §4.
│  2. Créer Ultima/GameApi/CombatReader.cs : lire joueur local (HP, block, énergie),
│     ennemis (HP, block, pouvoirs), et pour chaque ennemi ses intents avec
│     `intent.GetIntentLabel(targets, creature).GetFormattedText()`.
│     APIs exactes : docs/13 §1 (BuildEnemyState, BuildPlayerState, CombatId, IsLiveNode).
│  3. Ultima/GameApi/IntentParser.cs : extraire perHit/repeats depuis un AttackIntent
│     (`DamageCalc()`, `Repeats`, `GetSingleDamage`) — PAS depuis le texte.
│  4. Un log `ULTIMA state: <n> enemies, <n> intents` à chaque snapshot, via ModEntry.Write.
│ Interdit : toucher UI/, Patches/, Net/. Aucun appel de commande de jeu.
│ DONE : dotnet build OK + 1 commit `feat(reader): ...` + push -u origin m1/reader.
└────────────────────────────────────────────────────────────────────────

┌─ A2 UI ────────────────────────────────────────────────────────────────
│ Worktree : /home/mio/wt-ui   Branche : m2/ui
│ Possède  : Ultima/UI/**
│ Tâche :
│  1. UltimaOverlay devient une composition : Header (HP/block/tour) + List<EnemyRow>
│     + PriorityStrip + Footer(score). Elle reçoit un CombatSnapshot et se rend.
│     Elle NE LIT PAS le jeu (pas de using MegaCrit hors NCombatUi pour l'injection).
│  2. EnemyRow affiche : nom, `16x3`, ligne létale en rouge si LethalAt != null
│     (utilise Ultima.Core.Damage.LethalIndex.Describe).
│  3. Click-through (MouseFilter=Ignore), F8 toggle, opacité repos 0.30, largeur 260px,
│     ancré à droite. Ne recouvre jamais la main ni les intents (IntentOverlapHelper).
│  4. Gérer le cas "pas de données" avec un état vide explicite (jamais de faux chiffre).
│ Interdit : lire l'état du jeu, toucher Net/, GameApi/, Patches/.
│ DONE : dotnet build OK + 1 commit `feat(ui): ...` + push -u origin m2/ui.
└────────────────────────────────────────────────────────────────────────

┌─ A3 CATALOG ───────────────────────────────────────────────────────────
│ Worktree : /home/mio/wt-catalog   Branche : m3/catalog
│ Possède  : tools/index_source.py, data/catalog/**, docs/11*
│ Tâche :
│  1. Vérifier que tools/index_source.py tourne (python3) et régénère data/catalog.
│  2. Ajouter au catalogue un champ `first_card_hint` calculé depuis le source :
│     cartes qui overrident ShouldGlowGoldInternal OU utilisent CardPlaysFinished.
│     Le JSON doit sortir dans data/catalog/priority_cards.json (liste existante à enrichir).
│  3. Produire data/catalog/intents_by_monster.json : pour chaque monstre, ses moves
│     offensifs avec les valeurs résolues (déjà dans monsters.json — extraire, ne pas recalculer).
│  4. Documenter la commande de régénération en tête du script.
│ Interdit : toucher Ultima/ (aucun fichier C#).
│ DONE : JSON valides (json.load OK) + 1 commit `feat(catalog): ...` + push -u origin m3/catalog.
└────────────────────────────────────────────────────────────────────────

Tu merges dans cet ordre : A3 → A1 → A2. Après chaque merge : `dotnet build` + `dotnet test`.
Si rouge : `git revert -m 1 <sha>` immédiatement, tu corriges la couture toi-même.

VAGUE 2 — après merge de A1 (contrat disponible), si T+50 ou moins

```bash
git worktree add ../wt-forecast -b m4/forecast
git worktree add ../wt-multi    -b m5/multi
```

┌─ B1 FORECAST ──────────────────────────────────────────────────────────
│ Worktree : /home/mio/wt-forecast   Branche : m4/forecast
│ Possède  : Ultima/Features/Forecast/**
│ Tâche :
│  1. DamageForecast : à partir des intents lus, produire pour chaque ennemi
│     {current, withWeak, withVulnerable}. current = GetSingleDamage (déjà le pipeline réel).
│     Variantes : multiplier par les multiplicateurs LUS dans les modèles (docs/13 §1),
│     ne pas re-hardcoder 0.75. Si le dealer a déjà Weak → afficher "déjà affaibli".
│  2. Tests unitaires dans Ultima.Tests/ForecastTests.cs (cas : pas de double comptage).
│  3. Brancher dans le Patches de composition (tu fournis un `IReadOnlyList<EnemyAttack>`
│     à l'UI ; le chef fait le câblage du patch).
│ DONE : build + tests verts + commit + push.
└────────────────────────────────────────────────────────────────────────

┌─ B2 MULTI ─────────────────────────────────────────────────────────────
│ Worktree : /home/mio/wt-multi   Branche : m5/multi
│ Possède  : Ultima/Features/Multi/**
│ Tâche :
│  1. Vérifier le POC existant : NetBridge doit logger `net: attached` en combat multi.
│  2. Ajouter UltimaPriorityMessage (playerId, cardId, turnNumber, modVersion) sur le
│     modèle d'UltimaPingMessage, + UltimaHello (handshake/version).
│  3. S'abonner à RunManager.Instance.HoveredModelTracker.HoverChanged (docs/13 §3) et
│     émettre une alerte locale quand la carte survolée est dans priority_cards.json.
│  4. Ne broadcaster QU'AUX peers qui ont répondu à UltimaHello. Jamais vers un vanilla.
│  5. Tout est en try/catch + log. Rien ne doit throw si le net est null.
│ DONE : build + tests verts + commit `feat(multi): ...` + push.
└────────────────────────────────────────────────────────────────────────

VAGUE 3 — seulement si T+70 ou moins : F3 local (badge ShouldGlowGold) et F7 polish.
Sinon : rapport.

════════════════════════════════════════════════════════════════════════
6. PROTOCOLE DE COMMIT ET DE PREUVE
════════════════════════════════════════════════════════════════════════

Format :
`<type>(<scope>): <impératif ≤72>` puis corps : ce que ça fait / fichiers / **preuve**
(la commande exécutée et son résultat). Types : feat fix test docs chore refactor.
Scopes : reader ui forecast multi catalog ci core.

Après CHAQUE merge, exécute et colle la sortie dans STATUS.md :
- `dotnet build Ultima/Ultima.csproj -c Release`
- `dotnet test Ultima.Tests/Ultima.Tests.csproj -c Release`
- `grep -rn "CardPileCmd\|DamageCmd\|PowerCmd.Apply\|CreatureCmd" Ultima/ --include=*.cs`
- `bash tools/build.sh && ls -l build/mods/Ultima`

Ne déclare jamais « fait » sans la sortie réelle de ces commandes.

════════════════════════════════════════════════════════════════════════
7. VÉRIFICATION EN JEU (porte humaine)
════════════════════════════════════════════════════════════════════════

Tu ne peux pas lancer le jeu. Quand ton build est déployé, tu écris dans STATUS.md :

  PRÊT POUR TEST HUMAIN :
  - lancer le jeu, entrer en combat
  - attendre `overlay injected` dans mods/Ultima/ultima.log
  - vérifier que les dégâts affichés == les dégâts du jeu (3 ennemis)
  - F7 pour tester le ping (2 instances : --fastmp=host_standard / --fastmp=join --clientId=1001)

Tu attends le retour. Tu ne « supposes » pas que ça marche.

════════════════════════════════════════════════════════════════════════
8. SI TU ES BLOQUÉ
════════════════════════════════════════════════════════════════════════

- API introuvable → 2 tentatives max, puis tu notes dans status/ et tu passes à la feature
  suivante. Tu ne bloques jamais le run entier sur une API.
- Merge conflict → tu le résous toi-même (tu es le seul à merger).
- CI rouge → tu regardes le job, tu corriges, tu repush. Tu ne merges jamais du rouge.
- Doute sur la sémantique d'un dégât → l'oracle est le label d'intent du jeu (docs/13 §9).

════════════════════════════════════════════════════════════════════════
9. LIVRABLE FINAL (dans STATUS.md + réponse à l'utilisateur)
════════════════════════════════════════════════════════════════════════

1. Ce qui est livré, feature par feature, avec le hash de commit de chaque branche.
2. La sortie brute des 4 commandes de preuve (§6).
3. Ce qui reste à tester en jeu (checklist §7).
4. Ce qui n'a pas été fait et pourquoi (une ligne par feature abandonnée).
5. Le tag `v0.1.0` si F1+F2 sont verts.

Commence par `date +%H:%M` puis §2. Ne me demande pas de clarification : si une information
manque, prends la décision la plus conservatrice, note-la dans STATUS.md, et avance.
