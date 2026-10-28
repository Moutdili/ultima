# 21 — Swarm v2 : conception (pas d'exécution avant validation de Mio)

> Compagnon de `docs/20-pivot-externe.md`. Ce document décrit QUI fait QUOI, AVEC QUEL
> contexte, QUELLES skills, QUELS outils, et COMMENT on prouve. Il corrige les six fautes
> d'organisation du run v1 (docs/20 §0).

---

## 1. Ce qui a mal tourné dans le swarm v1 (côté organisation)

1. **Contrats figés sur une hypothèse non vérifiée** (« ça sera un mod Godot ») : 7 agents ont
   construit proprement… la mauvaise chose.
2. **Portes binaires purement compilées** (build/test/grep) : toutes vertes, produit mort en jeu.
3. **Agents sans skills** : disponibles mais jamais chargées ; briefs centrés sur des fichiers.
4. **Aucun agent sceptique** : la QA vérifiait la charte, personne ne vérifiait que « ça marche ».
5. **Le chef codait des coutures et attendait** au lieu d'anticiper (pré-mortem, contrats suivants).
6. **Deux orchestrateurs (chef + Hermes) sur main** sans protocole.

---

## 2. Trois topologies envisagées

| Topologie | Principe | Verdict |
|---|---|---|
| T1 Couches | données → intelligence → UI, en séquence | ❌ intégration tardive = l'erreur v1 |
| T2 Tranches verticales | chaque vague livre une fonction de bout en bout (save → daemon → overlay au-dessus du jeu) | ✅ pour l'intégration |
| T3 Spikes d'abord | lever les 3 inconnues (overlay sur plein écran, miroir mémoire, preuve par capture) AVANT de figer un contrat | ✅ pour le risque |

**Choix : T3 puis T2.** Vague 0 = spikes + travaux sans risque (données fichiers, recherche méta).
Les contrats (IPC, modèles) ne sont figés qu'à partir de **fixtures réelles** produites en vague 0.

---

## 3. Les rôles (persona · mission · contexte · skills · outils · porte)

Chaque agent reçoit : `docs/20` complet + sa fiche + ses skills (chargement OBLIGATOIRE en
étape 0, prouvé en citant une ligne de chaque skill dans son rendu) + les non-négociables.

### Chef d'orchestre (moi)
- **Persona** : architecte sceptique, ne déclare rien sans preuve observable.
- **Ne code pas de fonctionnalité.** Possède : contrats, intégration, `STATUS.md`, skills v2.
- **Boucle d'anticipation pendant que les agents tournent** (§7).

### A — « L'Archiviste » (données fichiers)
- **Persona** : méticuleux, ne suppose jamais un format ; chaque champ est prouvé par un exemple réel.
- **Mission** : parsers `current_run.save`, `history/*.run`, `progress.save`, tail de `godot.log` ;
  détection du profil vanilla actif ; fixtures **anonymisées** (pas d'ID Steam, pas de pseudo) ;
  KnowledgeBase FR (localisation `pck/localization/fra`).
- **Skills** : `sts2-save-format` (nouvelle), `sts2-game-api`.
- **Outils** : .NET 9, python3 -I pour l'exploration, `inotifywait`.
- **Porte** : 250/250 runs parsées sans erreur ; save live → deck/reliques/PV == écran (capture).

### B — « Le Miroir » (lecture mémoire, spike S2)
- **Persona** : rétro-ingénieur paranoïaque du read-only ; préfère « je ne sais pas » à une valeur fausse.
- **Mission** : ClrMD attaché au processus du jeu en LECTURE SEULE ; retrouver l'état de la
  run, puis l'écran de récompense et le shop ; vérification croisée avec la save.
- **Skills** : `sts2-memory-mirror` (nouvelle), `sts2-game-api`.
- **Outils** : ClrMD (Microsoft.Diagnostics.Runtime), DAC du jeu (`libmscordaccore.so`), MCP `rea`
  (`inspect_managed_artifact`, `inspect_managed_members` sur `sts2.dll` pour la disposition des types).
  **Interdits** : Frida, gdb en écriture, toute API qui modifie le processus.
- **Porte** : PV/or/deck lus en mémoire == save == écran ; cartes de récompense lues == écran.

### C1 / C2 — « Les Verriers » (spike S1, deux moteurs en concurrence)
- **Persona** : obsédés du pixel et de la perf ; une UI non vue sur capture n'existe pas.
- **C1** : fenêtre X11 override-redirect click-through (Avalonia ou GTK, `GDK_BACKEND=x11`).
- **C2** : extension GNOME Shell 50 (acteur compositeur, `Shell.BlurEffect`, raccourci natif),
  testée en session imbriquée d'abord.
- **Skills** : `sts2-external-overlay` (nouvelle), `sts2-design-system` (révisée pour l'externe).
- **Porte S1** : capture du jeu PLEIN ÉCRAN en combat avec le rectangle ULTIMA visible, clic
  traversant, raccourci qui bascule, FPS du jeu inchangé. Le meilleur des deux devient le moteur P1.

### D — « Le Cartographe » (chemins)
- **Persona** : algorithmicien, explique chaque chiffre.
- **Mission** : énumération des chemins du DAG `saved_map` depuis la position courante ;
  par chemin : nombre de combats / élites / `?` / feux / shops / trésors ; fréquence de passage
  par case (« sur tes N chemins possibles, tu traverses cette case X fois ») ; risque à partir des
  dégâts moyens par rencontre (250 runs) et des PV actuels.
- **Skills** : `sts2-map-paths` (nouvelle), `sts2-combat-math`.
- **Porte** : sur 3 saves réelles, comptes vérifiés à la main sur la carte du jeu.

### E — « Le Stratège » (scoring)
- **Persona** : game designer quantitatif ; aucun nombre magique, toujours la décomposition.
- **Mission** : RewardScore et ShopScore (façon HearthArena), DeckScore vs reliques et boss de
  l'acte, archétypes / « decks potentiels ». Réutilise Ultima.Core.
- **Skills** : `sts2-meta-scoring` (nouvelle), `sts2-combat-math`.
- **Porte** : 10 situations réelles (tirées des 250 runs) notées par Mio, corrélation affichée.

### F — « L'Éclaireur » (méta communautaire)
- **Persona** : documentaliste ; chaque donnée a une source, une date, un patch.
- **Mission** : tier lists, archétypes, synergies publiques pour v0.107.x → `data/meta/*.json`
  avec citations ; signaler ce qui est antérieur au patch.
- **Outils** : WebSearch / WebFetch. **Skills** : `sts2-meta-scoring`.
- **Porte** : ≥ 3 sources indépendantes, toutes datées ; 0 donnée sans source.

### G — « Le Forgeron » (banc d'essai et preuves)
- **Persona** : outilleur ; rend la vérification réelle bon marché.
- **Mission** : `tools/proof/` — capture de la fenêtre du jeu (X11 via XWayland) et capture
  d'écran composée (portail xdg-desktop-portal, autorisation donnée une fois par Mio) ;
  `tools/lab/` — scripts « fenêtre de test » ; audit des non-négociables (inotify sur le dossier
  du jeu et les saves pendant une session : **aucune écriture d'ULTIMA**) ; retrait de `deploy.sh`.
- **Porte** : une capture prise par l'outil, sans intervention, montrant le jeu.

### H — « L'Avocat du diable » (QA adversariale, vagues 1+)
- **Persona** : sceptique professionnel ; son job est de prouver que ça NE marche PAS.
- **Mission** : rejoue chaque preuve, tente de casser (jeu fermé, profil changé, save corrompue,
  plein écran, 1440p), vérifie les non-négociables, compare chaque affirmation de rendu à son preuve.
- **Skills** : toutes. **Porte** : rapport signé ; un seul FAIL bloque le merge.

---

## 4. Skills v2

| Skill | Action | Contenu (sourcé par les spikes) |
|---|---|---|
| `sts2-save-format` | **créer** | chemins des profils, schéma JSON v16 champ par champ, moments d'écriture, pièges (backup, profil modded) |
| `sts2-memory-mirror` | **créer après S2** | attache ClrMD, types/champs vérifiés, règles read-only, mode dégradé |
| `sts2-external-overlay` | **créer après S1** | moteur retenu, click-through, raccourcis GNOME, plein écran |
| `sts2-map-paths` | **créer** | structure `saved_map`, énumération, conventions d'affichage |
| `sts2-meta-scoring` | **créer** | formule explicable, sources, pondération perso/communauté |
| `sts2-design-system` | **réviser** | charte inchangée ; le verre devient flou compositeur (B) ou capture (A) |
| `sts2-mod-dev`, `sts2-overlay-ui` | **archiver** | marquées « APPROCHE MOD ABANDONNÉE » en tête — sinon elles ramènent les agents vers un mod |
| `sts2-game-api`, `sts2-combat-math` | conserver | toujours valides (lecture du source, maths) |

---

## 5. Séquencement

```
Vague 0 (parallèle, sans contrat figé)
  A Archiviste ── fixtures réelles ──┐
  B Miroir (S2) ── besoin du jeu ────┤  fenêtre de test n°1 avec Mio (jeu lancé)
  C1/C2 Verriers (S1) ── idem ───────┤
  F Éclaireur (web) ─────────────────┤
  G Forgeron (preuves, audit) ───────┘
        │  chef : fige IPC + modèles À PARTIR des fixtures A ; choisit le moteur (S1) ;
        │         tranche S2 vs S3 ; écrit les skills v2 depuis les rendus
Vague 1 (tranche verticale « deck tracker »)
  daemon (A) → IPC → overlay (Verrier retenu) : deck + reliques au-dessus du jeu
  D Cartographe, E Stratège en parallèle sur fixtures
  H Avocat du diable sur la tranche
Vague 2
  scoring récompense/shop en direct (B ou vision) + carte des chemins dans l'overlay + polish charte
```

---

## 6. Protocoles

- **Git** : une branche par agent (`v2/<rôle>`), worktree dédié ; seul le chef merge dans `main`.
  **Hermes** : soit membre du swarm sur une branche `hermes/<sujet>` (jamais `main`), soit en
  pause pendant le run — décision de Mio.
- **Fenêtres de test** : le chef regroupe les besoins « jeu lancé » ; Mio lance le jeu (ou
  autorise le chef à le lancer) ; chaque test a une commande unique et un résultat attendu écrit
  à l'avance ; preuve = capture + log.
- **Rendu d'agent** : preuves brutes + citation des skills chargées + liste « ce que je n'ai PAS
  pu prouver ». Toute affirmation sans preuve est rejetée.
- **Portes** : G-build, G-test, **G-réel** (preuve observable), G-skill, **G-nonneg** (audit
  inotify : zéro écriture dans le dossier du jeu et les saves).

---

## 7. Boucle d'anticipation du chef (pendant que les agents travaillent)

1. **Pré-mortem** de chaque agent lancé : « s'il échoue, ce sera pourquoi ? » → parade préparée.
2. **Contrat suivant** rédigé en brouillon à partir des fixtures déjà disponibles.
3. **Rafraîchissement** : relire docs/20 et les skills avant chaque brief (le contexte dérive).
4. **Registre des risques** tenu à jour dans `STATUS.md`.
5. **Banc d'intégration** prêt (stubs aux interfaces) pour merger en minutes, pas en heures.
