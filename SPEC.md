# SPEC — ULTIMA · Slay the Spire 2 Combat Intelligence

> Réécriture professionnelle du brief initial. Ce document est la **source de vérité**
> du projet : il remplace le prompt d'origine et sert de contrat entre le commanditaire
> (Mio), l'agent de recherche (Hermes) et l'agent d'implémentation (Claude Code).

---

## 1. Contexte

Slay the Spire 2 (Early Access, `v0.107.1`) est un roguelike deckbuilder en Godot 4.5.1 / C#
(`net9.0`). MegaCrit **shippe le code source C#** dans son `.pck` : le reverse engineering
est donc légitime, documenté et non obfusqué. La communauté a produit une dizaine de mods
de tracking, mais aucun n'adresse la décision tactique immédiate : *« ce tour, qu'est-ce
qui me tue, et qui doit jouer en premier ? »*.

## 2. Objectif

Livrer **ULTIMA**, un mod de combat intelligence **read-only** pour STS2, qui répond en
temps réel à trois questions et alimente l'analyse de run :

1. **Survie** — quels dégâts vais-je prendre, avec et sans Weak/Vulnerable, et à quel coup
   précis de la séquence je meurs ?
2. **Coordination** — en co-op, qui détient une carte à jouer en priorité, et comment le
   faire savoir sans casser la partie ?
3. **Progression** — qu'est-ce que mon deck produit réellement, et quel chemin prendre ?

Contrainte structurante : le mod **observe**, il ne modifie jamais l'état de jeu.

## 3. Périmètre

### Dans le périmètre (V1)

| ID | Fonction | Priorité |
|---|---|---|
| F1 | Forecast des dégâts d'intent par ennemi et par cible, avec variantes Weak / Vulnerable | P0 |
| F2 | Index du coup létal sur une séquence `NxM` (block + HP + overkill) | P0 |
| F3 | Détection des cartes à payoff conditionnel + alerte de priorité de tour (solo et co-op) | P1 |
| F4 | Partage des alertes entre joueurs (voie sûre d'abord, message custom ensuite) | P1 |
| F5 | Attribution des dégâts par carte / relique / potion + export CSV | P1 |
| F6 | Risque des embranchements de map (léger, explicable) | P2 |
| F7 | UI overlay compacte, déplaçable, hotkeys, opacité au repos | P0 |

### Hors périmètre

- Automatisation de jeu, bot, « play for me ».
- Upload cloud de statistiques (V2, opt-in uniquement).
- Support Windows/macOS (non bloquant, mais aucune dépendance Windows-only).
- Notes de cartes / tier lists (V2, cf. `ebadon16/sts2-advisor` comme prior art).

## 4. Exigences fonctionnelles détaillées

### F1 — DamageForecast

- Pour chaque ennemi en intent d'attaque, afficher : dégâts **actuels**, dégâts **si Weak**,
  dégâts **si Vulnerable**, total sur la séquence.
- Les dégâts « actuels » doivent être **identiques à l'affichage du jeu** (même pipeline).
- Les variantes sont des hypothèses explicitement libellées, jamais présentées comme certaines.
- Cas d'erreur : monstre déjà Weak → ne pas re-multiplier ; afficher « déjà affaibli ».

**Acceptation** : sur 3 rencontres distinctes, les valeurs « actuelles » matchent l'écran
du jeu au chiffre près ; les variantes sont cohérentes avec le modèle `WeakPower` (×0.75,
modulé par `PaperKrane` / `DebilitatePower`).

### F2 — LethalIndex

- Entrées : `perHit`, `repeats`, `block`, `hp` de la cible.
- Sortie : index du premier coup qui met la cible ≤ 0 PV, avec block consommé et overkill.
- Multi : calculer pour chaque joueur, afficher le pire cas.

**Acceptation** : `16x3` sur 35 PV → « 3e coup létal ». `16x3` avec 20 block et 35 PV →
2e coup létal, overkill affiché. `16x3` avec block ≥ 48 → « tout bloqué ».

### F3 — TurnPriority

- Catalogue statique des cartes à payoff conditionnel (généré depuis le source, pas saisi à la main).
- Détection dynamique : carte en main jouable + condition active.
- Alerte locale : badge sur la carte et ligne dans l'overlay.
- Alerte distante : visible par les autres joueurs (cf. F4).

**Acceptation** : une carte du catalogue en main déclenche le badge ; l'alerte disparaît
quand la carte est jouée, défaussée, ou le tour terminé.

### F4 — Partage multi

- **Voie A (obligatoire)** : exploiter l'état déjà synchronisé par le jeu
  (`RunManager.Instance.HoveredModelTracker.GetHoveredModel(playerId)` + `HoverChanged`).
  Aucun message custom. Doit fonctionner dès la V1.
- **Voie B (optionnelle)** : `INetMessage` custom pour les informations non répliquées
  (ex. « je tiens une carte prioritaire »). Non éprouvée publiquement → handshake,
  versioning, et dégradation silencieuse si le pair ne répond pas.

**Acceptation** : avec deux clients moddés, une sélection de carte prioritaire par le
joueur B est visible chez A. Avec un client vanilla, aucun crash, aucune erreur de
désérialisation.

### F5 — RunScore

- Attribution des dégâts par entité, sur le combat et sur le run.
- Export CSV relisible : une ligne par carte et par combat.
- Les cas non attribuables sont listés, pas inventés.

**Acceptation** : le CSV s'ouvre, les totaux par combat se réconcilient avec les dégâts
observés, et l'UI affiche explicitement les sources non attribuées.

### F6 — PathRisk

- Pour chaque embranchement accessible : type de salle, risque estimé, base de l'estimation.
- Le calcul affiche ses hypothèses (constantes par acte, HP restant, score de deck).

**Acceptation** : sur une map réelle, les 2-3 embranchements accessibles sont annotés avec
une justification lisible, pas un nombre magique.

### F7 — UI

- Une seule surface compacte (< 15 % de l'écran), ancrée à droite, jamais sur la main ni
  sur les intents.
- Opacité au repos 0.25-0.35, réveil au survol, `F8` toggle, `F9` détail.
- Click-through, sauf contrôles explicitement interactifs.
- Style aligné sur le thème du jeu.

**Acceptation** : lisible en 1080p et 1440p ; aucun clic avalé ; aucun chevauchement avec
la main, les intents ou les boutons de fin de tour.

## 5. Exigences non fonctionnelles

| Domaine | Exigence |
|---|---|
| **Read-only** | Aucune mutation d'état, aucune commande de jeu. Revue de code obligatoire sur ce point. |
| **Robustesse** | Tout patch Harmony est encapsulé dans try/catch avec log fichier. Un mod ne doit jamais crasher le jeu. |
| **Performance** | Recalcul sur événement, pas par frame. Coût cible < 1 ms par mise à jour. |
| **Compatibilité** | Early Access : toute API utilisée est vérifiée dans le source et la version testée est loggée au boot. |
| **Portabilité** | Linux Fedora natif (Flatpak) en cible primaire ; aucune API Windows-only. |
| **Données** | Zéro valeur de jeu codée en dur : tout est lu dans les modèles (`DamageCalc`, `DynamicVars`, pouvoirs). |
| **Licences** | Aucun copier-coller de code tiers non licencié. Lire pour comprendre, réimplémenter. |

## 6. Environnement technique

```
Jeu    : ~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2
Source : ~/sts2/game/pck/src/Core        (code original C#, 3 400 fichiers)
         ~/sts2/game/csharp              (ILSpy, complément)
Mods   : <jeu>/mods/Ultima/{Ultima.json, Ultima.dll}
Build  : ~/.dotnet/dotnet (SDK 9 + 10, user-space, pas de sudo)
Decomp : ~/tools/gdre/gdre_tools (2.7.4) · ~/.dotnet/tools/ilspycmd (11.2)
MCP    : .mcp.json → rea (rea-agents 6.8, 229 outils)
Skills : ~/.claude/skills/sts2-* (6 skills installées)
```

## 7. Plan de livraison (jalons avec porte de validation)

| Jalon | Contenu | Porte (preuve exigée) |
|---|---|---|
| J0 | Toolchain + décompilation + index | `tools/doctor.sh` vert, catalogue JSON non vide |
| J1 | Mod chargé + overlay injecté | log `overlay injected` + capture d'écran |
| J2 | F1 + F2 en logique pure + tests | tests unitaires verts, valeurs confrontées à l'écran |
| J3 | F1 + F2 dans l'UI | capture en combat, 3 rencontres |
| J4 | F3 local + F5 CSV | badge visible, CSV relisible |
| J5 | F4 voie A (multi, sans message) | 2 clients, alerte croisée |
| J6 | F6 + F4 voie B | map annotée, message custom avec dégradation propre |

**Règle** : aucun jalon ne démarre sans la preuve du précédent. Pas de big bang.

## 8. Protocole de validation

1. **Preuve d'exécution**, pas de description : build réel, log réel, capture réelle.
2. **Confrontation au jeu** : toute valeur affichée doit être comparée à l'écran du jeu.
3. **Test de dégradation** : client vanilla, mod désactivé, combat quitté en cours.
4. **Revue read-only** : grep des appels interdits (`CardPileCmd`, `DamageCmd`, `PowerCmd.Apply`,
   `CreatureCmd`) dans le code du mod → doit être vide hors commentaires.
5. **Revue de version** : chaque patch Harmony référence la version du jeu vérifiée.

## 9. Risques et parades

| Risque | Impact | Parade |
|---|---|---|
| API renommée par un patch EA | Mod cassé | Couche `GameApi` isolée + log de version + tests de fumée |
| Aucun précédent public d'`INetMessage` | F4 instable | Voie A obligatoire, voie B optionnelle avec dégradation |
| CombatSolver devient Linux/multi | Perte du différenciateur | Rester instantané et in-process ; le solveur reste lent |
| STS2MCP ajoute un overlay | Concurrence directe | Se différencier sur la létalité et le co-op |
| Complexité de l'attribution de dégâts | F5 fausse | Documenter les cas non attribuables, ne jamais inventer |
| Early Access : scope qui bouge | Dérive | Un jalon = une feature, pas de refactor opportuniste |

## 10. Définition de « terminé »

1. `dotnet build` : 0 erreur, 0 warning critique.
2. Mod chargé, log `ULTIMA ready` avec version du jeu.
3. F1/F2 exacts sur 3 rencontres, 2 personnages, 1080p et 1440p.
4. F3/F4 fonctionnels avec 2 clients ; aucun crash avec un client vanilla.
5. F5 : CSV réconcilié.
6. 30 minutes de jeu sans crash ni warning Harmony.
7. `tools/doctor.sh` vert sur une machine fraîche après `install-toolchain.sh`.

## 11. Livrables du handoff

| Livrable | Emplacement |
|---|---|
| Spécification (ce document) | `SPEC.md` |
| Manuel d'opération agent | `AGENT-MANUAL.md` |
| Dossier de connaissance | `docs/00` → `docs/15` |
| Catalogue de données du jeu | `data/catalog/*.json` |
| Scaffold compilable | `Ultima/` |
| Outils | `tools/` (doctor, build, deploy, dev, decompile, index) |
| Skills Claude | `~/.claude/skills/sts2-*` + `skills-src/` |
| MCP | `.mcp.json` (REA) |
| Prompt d'exécution | `docs/12-kickoff.md` |

## 12. Glossaire

- **Intent** : action annoncée par un ennemi pour le tour suivant (attaque, défense, buff).
- **Pipeline de dégâts** : chaîne `Hook.ModifyDamage` → hooks additifs, multiplicatifs, cap.
- **Weak / Vulnerable** : états réduisant / augmentant les dégâts (multiplicatifs).
- **Payoff conditionnel** : effet d'une carte conditionné à l'état du tour (ex. première carte).
- **Read-only** : le mod lit l'état, n'exécute aucune action de jeu.
- **PeerInput** : état d'interaction des joueurs distants synchronisé par le jeu (survol, sélection).
