# 04 — Trackers existants : ce qu'on prend, ce qu'on jette

Analyse faite sur les dépôts clonés dans `research/repos/` (octobre 2026).

## 1. `999dulgi/sts2decktracker` — le tracker de piles (C#)

**Ce qu'il fait** : affiche pioche + défausse pendant le combat, et le « top card »
(prochaine carte) via un tracking des ajouts en haut de pioche.

**Mécanique** (vérifiée dans le code) :
- Harmony Postfix sur `NCombatUi._Ready` → crée les panneaux à chaque combat.
- Harmony Prefix sur `CardPileCmd.Add` → enregistre les cartes ajoutées au top.
- Panneaux Godot `Control` faits main, `MouseFilter = Ignore` + `_UnhandledInput` pour le scroll.
- Config JSON, position mémorisée, opacité au repos.

**À reprendre** : injection UI sur `_Ready`, pattern click-through, gestion des positions.
**À éviter** : lecture de config dans `%APPDATA%` (cassé sous Linux) ; architecture
« un panneau par pile » (nous on veut une seule surface compacte).

## 2. `ronishae/sts2-deck-tracker` — attribution de dégâts (C#)

**Ce qu'il fait** : attribue les dégâts à la carte/relique/potion qui les a produits,
sur le run, avec export CSV. Gère Vulnerable (FIFO), poison (parts proportionnelles),
forge (queue), doom, orbes (focus), génération de cartes (arbre).

**Leçon clé** : l'attribution est un problème de **comptabilité d'ordre**, pas de mesure.
Il existe des cas non attribuables (Slow, Accelerant, replay) — il les documente au lieu
de mentir. C'est exactement l'état d'esprit à garder.

**À reprendre** : modèle de contributions + export CSV ; la liste de ses cas limites est
une spec de tests gratuite.

## 3. `thequantumfalcon/spirescope` — dashboard local (Python)

**Ce qu'il fait** : lit les **saves** (`saves/profile1/...`), sert un dashboard web local
(SSE), historique de runs, analyse de deck, support co-op `?player=N`.
Pas d'overlay in-game : outil post-run / deuxième écran.

**À reprendre** : le format des saves et l'analyse de runs (source de vérité pour le
scoring post-run), l'idée SSE pour un éventuel bridge.
**À éviter** : dépendance à un serveur web externe pour l'UI principale.

## 4. `MrAnime-0001/Spire-Forge` — build advisor web

Analyse de synergies/archétypes hors jeu. Utile comme base de **scoring de cartes**,
mais pas d'accès au state live.

## 5. `Sezmol/spire-scryer` — overlay Twitch

Mod → Cloudflare Worker → Twitch PubSub → extension. Bonne démo qu'un mod peut pousser
du JSON à l'extérieur ; inutile pour nous (latence, dépendance cloud).

## 6. `jiegec/STS2FirstMod` + `Alchyr/ModTemplate-StS2` + `fresh-milkshake/Modding-Tutorial`

Templates et documentation de référence. `ModTemplate-StS2` = squelette de mod maintenu ;
`Modding-Tutorial` = handbook versionné (setup, manifest, hooks, packaging, UI native).

## Synthèse : ce qui n'existe PAS et qu'ULTIMA apporte

| Feature | 999dulgi | ronishae | spirescope | ULTIMA |
|---|---|---|---|---|
| Pioche/défausse | ✅ | – | post-run | ✅ |
| Top card | ✅ | – | – | ✅ |
| Attribution dégâts | – | ✅ | – | ✅ (inspiré) |
| **Forecast Weak/Vuln par ennemi** | – | – | – | **✅** |
| **Index du coup létal (16x3)** | – | – | – | **✅** |
| **Alerte multi « carte à jouer en premier »** | – | – | – | **✅** |
| **Broadcast d'infos entre joueurs** | – | – | – | **✅** |
| Risque de chemin | – | – | partiel | **✅** |
| UI compacte unique | partiel | ✅ | – | **✅** |

Aucun tracker existant ne calcule de létalité ni ne synchronise des alertes entre joueurs.
C'est notre créneau.
