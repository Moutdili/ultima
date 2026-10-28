# 20 — Pivot : ULTIMA devient une application externe (façon Hearthstone Deck Tracker)

> Statut : **contrat d'architecture v2** (remplace l'approche « mod » de SPEC.md §6-§7 et docs/15-16).
> Chaque affirmation de ce document est sourcée par une observation faite sur la machine de
> Mio le 2026-10-28 (commande + chemin). Ce qui n'est pas encore prouvé est marqué **[SPIKE]**.

---

## 0. Pourquoi on change tout (post-mortem du run v1)

| # | Fait observé | Cause racine | Leçon |
|---|---|---|---|
| 1 | Progression du joueur « disparue » | Tout mod chargé fait basculer le jeu sur `steam/<id>/modded/profileN/` (`Core/Saves/UserDataPathProvider.cs:43`, docs/19 §5). Observé : `modded/profile1/saves/current_run.save` | **Un tracker ne doit jamais être un mod.** Contrainte n°1 du produit, absente du brief v1. |
| 2 | Aucun overlay, aucune touche | `BadImageFormatException: Bad IL range` dans `Godot.Control.InvokeGodotClassMethod` (logs Godot 21:32-21:54). Décision chef D1 : build contre le GodotSharp NuGet au lieu du `GodotSharp.dll` du jeu → ABI incompatible | Une porte « build vert » ne prouve rien sur le comportement réel. |
| 3 | Touches mortes même après correctifs | Le jeu consomme les touches dans `NInputManager._UnhandledKeyInput` (docs/19 §3) | Les hotkeys doivent vivre HORS du jeu. |
| 4 | Overlay invisible | Rail 8 px à 30 % d'opacité (docs/19 §4) | « Discret » ≠ « invisible » : la charte doit être validée sur capture réelle. |
| 5 | Skills jamais chargées par les agents | Briefs qui ne les imposaient pas (STATUS D10) | Chaque brief impose ses skills + preuve de chargement. |
| 6 | Portes G4/G5 « humaines » jamais atteintes | Aucune boucle de vérification en conditions réelles pendant le run | Chaque jalon a une preuve observable sur la vraie machine. |

---

## 1. Non-négociables v2

1. **Zéro mod, zéro fichier dans le dossier du jeu**, zéro patch, zéro injection de code.
2. **Lecture seule** : on n'écrit jamais dans `~/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2/`
   (saves, settings, logs). On copie, on lit, on ne touche pas.
3. **Progression intacte** : le jeu tourne en profil vanilla (`steam/<id>/profileN/`). Vérifiable :
   aucun `modded/` modifié après une session ULTIMA.
4. Accès au processus du jeu : **lecture mémoire uniquement** (comme HearthMirror pour HDT),
   jamais d'écriture, jamais de Frida/injection. [Validation de Mio requise — cf. §8]
5. L'application externe ne doit jamais faire planter, ralentir (≤ 1 % CPU au repos) ni voler
   le focus du jeu.
6. `tools/deploy.sh` (copie dans `mods/`) est **retiré** : plus rien ne doit jamais réinstaller le mod.

---

## 2. Inventaire des données lisibles de l'extérieur (prouvé)

Racine : `D = ~/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2/steam/76561199515637309/`

| Source | Format | Rafraîchi quand | Contenu utile |
|---|---|---|---|
| `D/profileN/saves/current_run.save` | **JSON clair** (schema_version 16) | entrée de salle + fin de combat (log : `Wrote N bytes to current_run.save`) | deck (`players[].deck[].id`, étage d'ajout), reliques, or, PV, énergie max, slots de potion ; **carte complète de chaque acte** (`acts[].saved_map` : 60 points typés `monster/unknown/rest_site/elite/treasure/shop`, `children`, boss) ; **boss de l'acte** (`boss_id`) ; **séquences pré-tirées** `normal_encounter_ids` (15), `elite_encounter_ids` (15), `event_ids` (28) + compteurs `*_visited` ; sac de reliques à venir (`relic_grab_bag`, `shared_relic_grab_bag`) ; cotes des salles `?` (`odds`) ; graine + compteurs RNG ; historique par étage (`map_point_history` : cartes proposées/choisies, dégâts, or) ; position (`visited_map_coords`) |
| `D/profileN/saves/history/*.run` | JSON clair | fin de run | **250 runs de Mio (26 victoires)** : par étage, choix de cartes/reliques, rencontres, dégâts subis, cause de mort → données de méta PERSONNELLE |
| `D/profileN/saves/progress.save` | JSON clair | fréquent | stats par personnage/ancien (wins/losses), déblocages |
| `logs/godot.log` | texte | temps réel | niveau salle seulement : `Preloading 'Combat Room'`, `Card rarity: Rolled…`, `Player N selected card reward`, votes de carte, écritures de save. **Aucun détail de combat, aucune carte proposée.** |
| `D/profileN/replays/latest.mcr` | binaire (« SIMH tape ») | fin de combat ? | flux de replay — format inconnu [SPIKE optionnel] |
| `settings.save` | JSON | — | `fullscreen: true`, 1920×1080, `fps_limit 90` |
| `~/sts2/game/pck/` | source C# + assets | statique | 578 cartes, reliques, pouvoirs, 123 monstres, rencontres par acte, règles de map/odds, **localisation 16 langues** (`localization/fra`…), **illustrations de cartes** |
| **Mémoire du processus** | tas managé CoreCLR **9.0.7** | temps réel | tout l'état vivant (écran de récompense, shop, combat, main). Faisabilité : `ptrace_scope = 0`, le jeu embarque `libmscordaccore.so` (DAC) + `createdump` → **ClrMD peut s'attacher en lecture** [SPIKE S2] |

**Conséquence clé** (précision de Mio : le scoring sert aux **récompenses de cartes** et au **shop**) :
les cartes proposées n'existent dans AUCUN fichier au moment du choix (les tirages
`Card rarity: Rolled` ont lieu APRÈS l'écriture de la save). Le scoring temps réel dépend donc de :
1. **Miroir mémoire (S2)** — voie principale, celle de HDT ;
2. **Vision (S3)** — repli : capture de la fenêtre du jeu (X11/XComposite, le jeu tourne sous
   XWayland) + reconnaissance des illustrations de cartes (on possède tous les assets) ;
3. **Prédiction par graine (S4)** — recherche : RNG déterministe + source du jeu. Lourd, fragile
   aux patchs EA. Seulement si S2 et S3 échouent.

---

## 3. Ce que chaque fonction P0 consomme

| Fonction (demande de Mio) | Données | Source |
|---|---|---|
| Deck tracker (deck courant, reliques, potions, PV/or) | deck, reliques, potions | save (L1) |
| Infos reliques / attaques / pouvoirs à la demande | textes localisés FR, valeurs | pck (catalogue + localisation) |
| Decks potentiels (archétypes, « vers quoi je vais ») | deck + méta | save + base méta (§4) |
| **Scoring de carte en récompense** (façon HearthArena) | cartes proposées + deck + reliques + boss de l'acte | **mémoire (S2)** / vision (S3) + save + méta |
| **Scoring au shop** | inventaire du shop + prix + or | **mémoire (S2)** / vision (S3) |
| Scoring du deck vs reliques et boss de l'acte | deck, reliques, `boss_id`, acte | save + méta + stats perso |
| Système de chemins (carte) | DAG `saved_map`, position, odds `?` | save |
| « Sur ce chemin : X combats, Y élites, Z feux… » | énumération des chemins | save (calcul pur) |
| Risque d'un chemin | dégâts moyens par rencontre (250 runs perso) + PV actuels | history + save |

---

## 4. Base de connaissance « méta » (expertise)

Trois couches, toujours affichées avec leur **source et leur date** (pas de nombre magique) :
1. **Statique jeu** : catalogue régénéré depuis le source (`tools/index_source.py`) — coûts,
   raretés, mots-clés, synergies détectables (pouvoirs appliqués, cartes générées, conditions).
2. **Personnelle** : 250 runs de Mio → taux de victoire quand la carte X est prise (avec
   intervalle de confiance : peu de données ⇒ on le dit), dégâts moyens par rencontre, causes de mort.
3. **Communautaire** : tier lists et archétypes publics pour v0.107.x (recherche web, sources
   citées, date de patch). Early Access : toute source plus ancienne que le patch est marquée.

Score affiché = combinaison explicable : « 72 = base 60 (tier A communauté) + 8 (synergie
poison : 5 cartes) + 4 (contre Waterfall Giant : multi-coups) ». Chaque terme est cliquable/F9.

---

## 5. Architecture cible

```
┌────────────────────── ultima-daemon (.NET 9, hors jeu) ──────────────────────┐
│ Sources                  Modèle                 Intelligence (Ultima.Core)    │
│  FileWatch (inotify) ──► RunState ──────────►  DeckScore · RewardScore       │
│  LogTail (godot.log) ──► Events                 ShopScore · PathAnalysis      │
│  Mirror (ClrMD, RO) ───► LiveScreen(reward/     Archetypes · MetaStats        │
│  Vision (XComposite)      shop/combat)          (+ Forecast/Lethal v1 réutilisés)
│  KnowledgeBase (pck + localisation + méta + history)                          │
│                         ──► IPC local (socket Unix, JSON versionné) ──►       │
└──────────────────────────────────────────────────────────────────────────────┘
                                   │
                ┌──────────────────┴──────────────────┐
         ultima-overlay (moteur choisi par S1)    ultima ctl (CLI : toggle/détail,
         fenêtre au-dessus du jeu, click-through   branchée sur raccourcis GNOME)
```

- `Ultima.Core` (v1) est **réutilisé** tel quel : DamageMath, LethalIndex, ForecastEngine,
  PriorityRules, DamageLedger, CsvWriter — 82 tests verts.
- Le projet `Ultima/` (mod Godot) est **archivé** (plus compilé, plus déployé).
- IPC versionné : l'overlay ne lit jamais le jeu, il affiche ce que le daemon publie
  (même principe que v1 : l'UI reçoit des DTO).

### Moteur d'overlay (P1) — décision par spike S1, pas par préférence

Contexte prouvé : **GNOME Shell 50.6 sous Wayland**, jeu en **X11 via XWayland**, **plein écran**.
Wayland/GNOME n'offre ni layer-shell ni « always on top » aux clients Wayland.

| Candidat | Principe | + | − |
|---|---|---|---|
| **A. Fenêtre X11 override-redirect** (Avalonia/.NET ou GTK, `GDK_BACKEND=x11`) | fenêtre XWayland ARGB sans gestionnaire, région d'entrée vide (XFixes) = click-through | un seul langage (C#), réutilise Core ; même serveur X que le jeu | au-dessus d'un jeu **plein écran** sous Mutter : NON PROUVÉ ; pas de vrai flou du jeu (sauf capture XComposite) |
| **B. Extension GNOME Shell** (GJS) + daemon | acteur dessiné par le compositeur, au-dessus de tout, `Shell.BlurEffect` = **vrai verre**, raccourcis natifs | rendu le plus « pro », vrai glassmorphism, hotkeys propres | GNOME uniquement ; itération plus lente (session imbriquée `gnome-shell --devkit`) ; JS |
| **C. Fenêtre compagnon** (2e écran / fenêtre normale) | sans superposition | marche partout | ce n'est pas un overlay |

Raccourcis : extension (B) ou raccourcis personnalisés GNOME → `ultima ctl toggle` (A). Plus
jamais de touches lues dans le jeu.

---

## 6. Jalons et portes (preuve en conditions réelles obligatoire)

| Jalon | Contenu | Porte (preuve observable) |
|---|---|---|
| **S1** spike overlay | A et B en parallèle : un rectangle « ULTIMA » au-dessus du jeu en plein écran, click-through, toggle par raccourci | capture d'écran du jeu en combat avec le rectangle ; clic sur une carte du jeu à travers ; FPS du jeu inchangé |
| **S2** spike miroir | attache ClrMD en lecture seule au processus du jeu, lit PV/or/deck et compare à la save | valeurs mémoire == save == écran ; aucune écriture (ClrMD lecture seule), jeu non perturbé |
| **S3** spike vision (si S2 échoue) | capture fenêtre du jeu + reconnaissance de 3 cartes de récompense | 3/3 cartes identifiées sur 5 écrans réels |
| **P0-a** données fichiers | parsers save/history/progress + KnowledgeBase FR | tests sur les **250 runs réels** + save live ; deck affiché == écran |
| **P0-b** chemins | énumération des chemins, comptes par type, risque | sur la save réelle : chemins de la position au boss, comptes vérifiés à la main sur la carte du jeu |
| **P0-c** scoring | deck / récompense / shop, explicable | 10 situations réelles notées par Mio vs score ULTIMA |
| **P1** overlay v2 | moteur retenu + charte (docs/17 + skills) | captures 1080p ; aucun chevauchement avec la main/intents ; verre visible |

---

## 7. Risques et parades

| Risque | Parade |
|---|---|
| Overlay masqué par le plein écran | S1 teste les deux moteurs ; repli : demander le mode fenêtré sans bordure, sinon C |
| ClrMD bloqué (bac à sable Flatpak, espace de noms) | tester l'attache depuis l'hôte puis via `flatpak-spawn` ; repli S3 vision |
| Patch EA renomme les champs mémoire | couche Mirror isolée + vérification croisée avec la save à chaque salle (si divergence → mode dégradé « fichiers seulement », annoncé à l'écran) |
| Données méta communautaires périmées | date de patch affichée ; pondération vers les 250 runs perso |
| Spoil (séquences pré-tirées dans la save) | interdit de lecture (§8.2), vérifié par grep dans le code du daemon |
| Hermes et le swarm écrivent en même temps sur main | protocole docs/21 §6 |

---

## 8. Décisions de Mio (2026-10-28)

1. **Lecture mémoire : OUI** (comme HearthMirror), lecture seule stricte. Voie principale du
   temps réel. La vision n'est qu'un repli si S2 échoue.
2. **Probabiliste seulement.** Le daemon **ne lit PAS** `normal_encounter_ids`,
   `elite_encounter_ids`, `event_ids`, `relic_grab_bag`, `shared_relic_grab_bag`, `rng`, `odds`
   joueur : ni affichés, ni utilisés dans un calcul (sinon fuite indirecte). Aussi masqués
   (arbitrage A, appliqué) : variante, boss et ancien des actes FUTURS ; valeurs des tirages de
   rareté du log (`Card rarity: Rolled x, need < y`) — seul le signal « tirage » est publié. Autorisé : ce que
   le joueur voit en jeu — type des cases de la carte, boss de l'acte, deck, reliques, PV, or,
   historique déjà joué, et les cotes PUBLIQUES des cases `?` décrites par le source du jeu.
3. **Plein écran obligatoire.** Le moteur d'overlay doit passer au-dessus du jeu en plein
   écran → avantage à l'extension GNOME Shell (C2) ; C1 reste en concurrence.
4. **Hermes reste libre** sur main. Le swarm s'adapte : agents sur branches, `git pull
   --ff-only` avant chaque push du chef, jamais de `--force`, on ne touche pas aux fichiers
   en cours de modification par Hermes (`git status` avant chaque merge).
