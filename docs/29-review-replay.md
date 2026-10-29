# 29 — Review / replay tool-assisted d'une run passée (spec de conception)

> **Statut** : proposition de conception (Hermes, S10), 2026-10-29. Aucun code C# ni contrat modifié
> par ce document. Les demandes de contrat sont en §5.3, à arbitrer par le chef.
> **À lire avec** `docs/27` (méthode G/ΔG), `docs/28` (état + trouvailles data), `docs/22`
> (contrat moteur v1.1), `docs/14` (charte UI), `docs/17` (rail).
>
> **But produit** : revoir une run passée comme on revoit une partie d'échecs sur chess.com —
> dérouler la partie, mettre en pause, changer de vitesse à la volée, et voir s'arrêter le temps là
> où une erreur a été commise, avec la raison chiffrée. Pour montrer une partie à un ami, ou revoir
> ses propres erreurs.

---

## 0. Résumé (le produit en cinq phrases)

1. La review est une **revue de décisions et de trajectoire** : on rejoue une run étage par étage, avec
   l'état reconstruit du joueur (PV, or, deck, reliques, potions) et les options qui lui étaient
   offertes.
2. Chaque option reçoit sa **valeur contextuelle ΔG** (docs/27), et le **regret** d'un point est
   l'écart entre la meilleure option et celle qui a été prise.
3. Le regret est classé en **info / inexactitude / erreur / blunder**, avec un **ralenti automatique**
   quand ça dépasse 12 points de G, et la **justification affichée** (les termes du score, chacun
   sourcé).
4. La lecture est contrôlée comme un lecteur vidéo : **pause, 0,5×/1×/2×/4× à la volée, saut d'étage,
   retour arrière**, barre de scrubbing colorée par bande de regret.
5. Le calcul tourne **dans le moteur**, pas dans l'UI ; l'UI ne fait qu'animer une timeline déjà
   calculée. Un vrai replay de combat n'est **pas** possible depuis la data de run seule : il faut un
   sidecar de replay qui n'existe que pour 8 % des runs locales (§1.4) — donc hors MVP, et la review
   ne prétend jamais montrer les tours d'un combat.

---

## 1. Ce que la data permet — et ne permet pas

### 1.1 Le corpus réellement mesuré

Mesures faites le 2026-10-29 sur `~/sts2` (repro en annexe A) :

| Mesure | Valeur |
|---|---|
| Runs locales (`data/runs/**/*.run`, `saves/history`) | **273** |
| Étages joués (somme des `map_point_history[][]`) | **7 581** |
| Enregistrements joueur-étage (`player_stats[]`, co-op inclus) | **15 213** |
| Offres de cartes (étages-joueurs) | **7 552**, dont **5 830 à exactement 3 cartes** |
| Offres de reliques | **5 685** |
| Offres de potions | **4 381** |
| Choix d'Ancien (`ancient_choice`) | **1 042**, **toujours 3 options** |
| Choix de repos (`rest_site_choices`) | **2 727** (SMITH 1 916, HEAL 690, autres 121) |
| Retraits de carte (`cards_removed`) | **1 033** |
| Transformations (`cards_transformed`) | **244** |
| Étages de marchand | **508** |
| Export public v0.107.1 | **785 121 runs**, **0** occurrence de `has_replay` |
| Runs avec `has_replay: true` (local) | **22 / 273 (8,1 %)** — 22/22 servent un replay (10,3 Mo au total) |

### 1.2 Tableau honnête : ce qu'une run contient

`✗` = absent, `~` = approchable avec une réserve explicite, `✓` = présent tel quel.
« Save » = `history/*.run` (ce que lit `Ultima.Saves/HistoryReader`) ; la colonne « timeline » dit ce
que la review peut afficher.

| Élément | Save | Granularité | Timeline | Réserve |
|---|---|---|---|---|
| Type de salle traversé | ✓ | étage | ✓ | `map_point_type` + `rooms[].room_type` |
| Rencontre / événement | ✓ | étage | ✓ | `rooms[0].model_id` (« ENCOUNTER.X », « EVENT.Y ») |
| Monstres du combat | ✓ | étage | ✓ | `rooms[].monster_ids` (présents sur 3 401 des 7 607 salles) |
| **Nombre de tours d'un combat** | ✓ | combat | ✓ | `rooms[].turns_taken` (ex. 5 tours sur Shrinker Beetle) |
| PV / PV max | ✓ | fin d'étage | ✓ | `current_hp`, `max_hp` — **pas** les PV d'entrée de l'étage 1 |
| Or | ✓ | fin d'étage | ✓ | `current_gold` |
| Dégâts subis / soins | ✓ | étage | ✓ | `damage_taken`, `hp_healed` (tous dégâts confondus : combat + événement) |
| Or gagné / dépensé / volé | ✓ | étage | ✓ | `gold_gained`, `gold_spent`, `gold_lost`, `gold_stolen` |
| Cartes proposées + prise | ✓ | étage | ✓ | `card_choices[].card.id`, `was_picked`, `floor_added_to_deck` |
| Cartes gagnées (hors choix) | ✓ | étage | ✓ | `cards_gained` (événement, récompense, achat) |
| Cartes retirées / transformées | ✓ | étage | ✓ | `cards_removed`, `cards_transformed` (avec `floor_added_to_deck`) |
| Cartes améliorées / dégradées | ✓ | étage | ✓ | `upgraded_cards`, `downgraded_cards` (ids seulement, **pas le numéro d'exemplaire**) |
| Enchantements | ✓ | étage | ✓ | `cards_enchanted` (id + enchantement) |
| Reliques proposées + prise | ✓ | étage | ✓ | `relic_choices[]`, `ancient_choice[]`, `bought_relics` |
| Reliques retirées | ✓ | étage | ✓ | `relics_removed` |
| Reliques finales, avec étage d'entrée | ✓ | run | ✓ | `players[].relics[].floor_added_to_deck` → état par étage reconstructible |
| Potions proposées / achetées / utilisées / jetées | ✓ | étage | ~ | `potion_choices`, `bought_potions`, `potion_used`, `potion_discarded` — le **ramassage hors choix** n'est pas journalisé |
| Choix d'Ancien (3 options) | ✓ | étage | ✓ | `ancient_choice[].was_chosen` |
| Choix d'événement | ~ | étage | ~ | `event_choices[]` ne contient **que le titre choisi** : aucune alternative → **regret impossible** |
| Choix de repos | ~ | étage | ~ | `rest_site_choices` = option retenue seulement (menu implicite) |
| Prix d'un article en shop | ✗ | — | ✗ | seul `gold_spent` total de l'étage ; les prix n'existent que dans le replay NDJSON |
| Alternatives de chemin sur la carte | ✗ | — | ✗ | la save d'historique n'a **pas** le graphe de la carte : on connaît la trajectoire suivie, pas les branches écartées |
| **Cartes jouées pendant un combat** | ✗ | — | ✗ | **aucune** donnée de tour : ni main, ni pioche, ni énergie, ni ordre |
| **Dégâts par tour / par carte** | ✗ | — | ✗ | idem |
| Intentions / moves de monstre | ✗ | — | ✗ | seuls les moves *possibles* sont connus (catalogue), pas la rotation |
| PV au début de la run | ✗ | — | ✗ | l'étage 1 n'expose que l'état **après** ; l'or de départ, lui, se déduit (`gold_gained`/`gold_spent`) |
| Deck final, reliques finales | ✓ | run | ✓ | `players[].deck[]`, `players[].relics[]` |

**Conséquence de conception** : la review affiche trois choses, et rien d'autre —
(a) **ce qui est arrivé** (trajectoire, PV/or/dégâts par étage), (b) **ce qui était proposé**
(options + ΔG), (c) **ce qui a été choisi** et l'écart. Toute formulation du type « tu aurais dû jouer
Bash au tour 3 » est **interdite** par la data.

### 1.3 Pourquoi l'absence de tours n'est pas un détail cosmétique

Sans tours, on ne peut pas noter :

- l'ordre de jeu dans un combat (la compétence la plus discriminante à haut niveau) ;
- la gestion de l'énergie et de la pioche ;
- l'usage d'une potion *au bon moment* (on sait qu'elle a été utilisée à l'étage 9, pas au tour 2) ;
- le ciblage (quel monstre frappé en premier).

On peut en revanche noter, avec de la data solide : **le choix de carte, le choix de relique, le choix
d'Ancien, le retrait, l'amélioration au repos, l'achat, et la décision de continuer à bas PV**. C'est
déjà l'essentiel du win rate sur ce jeu, et c'est exactement ce que la méthode ΔG (docs/27) sait
évaluer. La review est donc une **revue de build et de trajectoire** : « ta run n'est pas morte au
boss, elle est morte au choix 9 ».

### 1.4 Le point à arbitrer : les replays existent, mais hors corpus

`docs/28 §2.4` dit « `has_replay` vrai pour 1 run sur 273, `/api/runs/{hash}/replay` → 404 sinon ».
**Ce n'est pas ce que je mesure sur le corpus local, au 2026-10-29** (le chiffre du brief vient
probablement d'un test fait avant que les replays soient publiés, ou sur un échantillon filtré) :

- **22 des 273 runs locales** portent `has_replay: true` (8,1 %) ;
- pour ces 22 runs, `GET /api/runs/{hash}/replay` répond **HTTP 200** (22/22 testés), **10,3 Mo** au
  total, ~470 Ko par run ;
- le payload est du **NDJSON d'événements** (`replay_version: 7`, mods `BaseLib` + `SpireCodex` +
  `UnifiedSavePath`) : `combat_start`, `draw_order`, `play`, `hit`, `block`, `intent`, `energy`,
  `turn`, `end_turn`, `combat_end`, `shop` (avec **prix par article**), `decision` (avec **toutes les
  options**), `rest`, `buy`, `remove`… 5 226 enregistrements pour la run testée, 438 `play`,
  640 `hit`, 178 `intent`.
- En revanche, l'export public (**785 121 runs**) ne contient **ni** le champ `has_replay` **ni**
  aucun tour : `zcat … | grep -c '"has_replay"'` → **0** sur tout le fichier.

Autrement dit : **la data de run est muette sur les combats ; un sidecar de replay existe pour 8 % des
runs locales, produit par un mod, et il n'est ni dans l'export ni dans le fichier `.run`.**

Trois options, à trancher par Mio (je recommande la 1 pour le MVP) :

1. **MVP sans combat** (recommandé) : la review ne dépend que du `.run`. Elle marche sur 100 % des
   runs, hors ligne, sans réseau, et reste valable pour le corpus public.
2. **Voie B, plus tard** : quand un replay NDJSON est disponible pour la run, ajouter un onglet
   « combat » (mains, cartes jouées, dégâts par tour, prix en shop). Nécessite le réseau, un cache
   disque, et une UI distincte — donc **pas** dans la même livraison.
3. **Mod local** : activer le mod SpireCodex sur la machine de Mio pour que **toutes** les runs
   futures aient un replay → la voie B devient la norme pour les runs récentes, les anciennes restent
   en revue de décisions. Coût : dépendance à un mod tiers, format non garanti, et questions de
   confidentialité (le replay part chez spire-codex pour être servi).

**Décision structurante** : tout le schéma ci-dessous est prévu pour que la voie B s'ajoute **sans
casser** la timeline (un `point.combat` optionnel, `null` aujourd'hui). On ne code rien de la voie B
maintenant.

### 1.5 Ce qu'il faudrait pour un vrai replay combat

| Besoin | État |
|---|---|
| Journaliser chaque événement de combat (pioche, jeu, dégâts, blocs, intentions) | **fait par le mod SpireCodex** (`replay_version 7`) — pas à réécrire |
| Versionner le format | `replay_version` + `run_schema_version` présents dans le header |
| Rapatrier le fichier | `GET /api/runs/{hash}/replay` (réseau, service tiers) |
| Servir hors ligne | cache local `~/.cache/ultima/replay/<hash>.ndjson` (~0,5 Mo/run) |
| Rejouer dans le moteur | **gros** : machine à états de combat complète (pioche, énergie, pouvoirs, orbes). Hors périmètre ; la voie B se contenterait d'un **défilement d'événements** issus du log, pas d'une re-simulation |
| Couvrir les runs sans replay | impossible rétroactivement |

Le point important : **on n'a pas besoin de re-simuler le combat** pour faire une review de combat
utile — le log contient déjà ce qui s'est passé. Re-simuler serait un autre produit (un bac à sable).

---

## 2. Modèle de données d'une timeline

### 2.1 Vue d'ensemble

```
ReviewTimeline
├── run        : identité (runId, build, mode, ascension, perso, seed, issue, source)
├── lanes[]    : un joueur (co-op) → deck final, reliques finales
├── states[]   : états datés (PV, or, deck, reliques, potions) — un par changement réel
├── points[]   : un par étage observable → décision + options + regret
└── summary    : agrégats (regret total, pires points, par acte) + coverage (ce qui n'a pas pu être noté)
```

Trois choix de conception, chacun justifié par la data :

1. **`states[]` séparé de `points[]`.** Un deck de 28 cartes répété à chaque point ferait 28 × 25
   lignes de JSON. Les points référencent un état par **index** (`stateBefore` / `stateAfter`), et
   deux points consécutifs peuvent partager le même index quand rien n'a bougé. C'est aussi ce qui
   rend le scrubbing bon marché : l'UI saute d'état en état.
2. **Un point par étage, pas par décision.** Un étage de marchand contient plusieurs décisions
   (achats + retrait) ; un étage de repos aussi (repos ou forger). Le point porte donc `kind` +
   `options[]` (jusqu'à 8) et `chosen`. On garde la granularité « étage » parce que c'est celle de la
   data et celle du rail (une pastille = un étage).
3. **`regret: null` est une valeur normale.** Elle veut dire « non évaluable », avec une `note` qui
   dit pourquoi. Jamais 0 (voir §3.5).

### 2.2 Reconstruction de l'état à l'étage N (règles exactes)

Tout est reconstructible **vers l'arrière** depuis le deck final, parce que chaque carte et chaque
relique du deck final porte `floor_added_to_deck` :

```
deck(N) = { c ∈ deck_final : c.floor_added_to_deck ≤ N }
        ∪ { c ∈ cards_removed∪cards_transformed.original : floor(c) > N ∧ c.floor_added_to_deck ≤ N }
reliques(N) = { r ∈ reliques_finales : r.floor_added_to_deck ≤ N }
            ∪ { r ∈ relics_removed : floor(r) > N ∧ r.floor_added_to_deck ≤ N }
```

La seconde condition (`floor_added_to_deck ≤ N`) est **obligatoire** : sans elle, une carte acquise à
l'étage 9 puis transformée à l'étage 12 apparaîtrait dès l'étage 2. C'est le seul piège réel de la
règle — vérifié : la version sans cette condition fait apparaître `EVIL_EYE` (prise à l'étage 9) dès
l'étage 2 sur la run de test.

Réglages fins, avec leurs limites :

| Élément | Règle | Limite assumée |
|---|---|---|
| Améliorations | partir du niveau final, **retirer 1** pour chaque `upgraded_cards` d'étage > N, **ajouter 1** pour chaque `downgraded_cards` d'étage > N | `upgraded_cards` ne donne que l'id : avec 3 × STRIKE, on ne sait pas **laquelle** a été améliorée. Convention : on décrémente la copie la plus récemment ajoutée. Le **multiset** reste exact, l'identité des copies non — sans impact sur le scoring (qui ne regarde que le multiset) |
| Transformations | pour N < étage(f) : la carte d'origine est dans le deck ; après : la carte finale | la carte finale n'a pas toujours `floor_added_to_deck` = étage de la transformation → utiliser l'étage de l'enregistrement `cards_transformed`, pas le champ de la carte |
| Potions | accumuler `potion_choices` prises + `bought_potions` − `potion_used` − `potion_discarded` | le ramassage hors choix n'est pas journalisé → état **approximatif**, affiché avec une puce « ~ » et jamais utilisé dans un ΔG |
| PV / or d'entrée | = `current_hp` / `current_gold` de l'étage N−1 | **étage 1** : inconnu (la save n'a que l'état après) → `stateBefore: null`, la timeline démarre sur `stateAfter` |
| Dégâts | `damage_taken` de l'étage | combat + événement confondus, non séparables |

**Validation faite avant d'écrire la spec** (273 runs, 15 213 étages-joueurs) :

- 0 carte du deck final sans `floor_added_to_deck` ;
- 0 carte avec `floor_added_to_deck` > nombre d'étages de la run ;
- 0 étage où la taille de `deck(N)` baisse sans `cards_removed`/`cards_transformed` enregistré.

→ la règle arrière est **cohérente sur 100 % du corpus local**. En revanche l'**accumulation avant**
(départ = deck de départ + gains − retraits) ne referme sur le deck final que pour **~11 % des
joueurs-runs** (53/477) : des cartes apparaissent ou disparaissent sans être journalisées (événements,
mods). **Conclusion : on reconstruit toujours vers l'arrière, jamais par accumulation.** C'est une
contrainte d'implémentation, pas un détail.

### 2.3 Schéma (extrait d'un point réel)

Extrait de la run `4957a011bbd77f58` (Ironclad, A8, v0.107.1, morte étage 25 sur
`ENCOUNTER.DECIMILLIPEDE_ELITE`). Les états, les options et les rencontres sont **réels** ; les
valeurs `deltaG` sont **illustratives** (le moteur ΔG n'est pas encore branché, docs/28 §5) et sont
donc marquées `confidence: "provisional"`.

```json
{
  "index": 0,
  "floor": 2,
  "actIndex": 0,
  "roomType": "monster",
  "modelId": "ENCOUNTER.SHRINKER_BEETLE_WEAK",
  "monsterIds": ["MONSTER.SHRINKER_BEETLE"],
  "turnsTaken": 5,
  "playerId": 1,
  "kind": "card_reward",
  "stateBefore": 0,
  "stateAfter": 1,
  "delta": {"damageTaken": 10, "hpHealed": 6, "goldDelta": 15, "maxHpDelta": 0},
  "chosen": {"kind": "card", "id": "CARD.TREMBLE"},
  "options": [
    {"kind": "card", "id": "CARD.TREMBLE", "label": "Tremble", "deltaG": 2.0, "picked": true,
     "available": true, "confidence": "provisional",
     "terms": [{"label": "rang Spire Codex", "points": 2, "source": "Spire Codex — consensus 52/100"},
               {"label": "fit : 0 manque comblé", "points": 0, "source": "deck : 12 cartes, bloc 9/tour"}]},
    {"kind": "card", "id": "CARD.HAVOC", "label": "Havoc", "deltaG": 4.5, "picked": false,
     "available": true, "confidence": "provisional",
     "terms": [{"label": "rang Spire Codex", "points": 4, "source": "Spire Codex — consensus 61/100"},
               {"label": "fit : moteur de pioche", "points": 1, "source": "deck sans pioche (0 carte)"},
               {"label": "besoin PV : 60/80", "points": -1, "source": "bande 60-80 % déjà couverte"}]},
    {"kind": "card", "id": "CARD.BATTLE_TRANCE", "label": "Battle Trance", "deltaG": -1.5, "picked": false,
     "available": true, "confidence": "provisional",
     "terms": [{"label": "rang Spire Codex", "points": 1, "source": "Spire Codex — consensus 40/100"},
               {"label": "anti-synergie : pas de moteur", "points": -3, "source": "deck sans carte à 0 coût"}]},
    {"kind": "skip", "id": null, "label": "Passer", "deltaG": 0.0, "picked": false, "available": true,
     "confidence": "provisional", "terms": []}
  ],
  "regret": {"deltaG": 2.5, "band": "info", "bestId": "CARD.HAVOC", "confidence": "provisional",
             "basis": "max(ΔG) − ΔG(choix) = 4,5 − 2,0 ; écart meilleur/2e = 2,5 < 3 → plafond « info »"},
  "note": null
}
```

Le **schéma complet** (run, lanes, 5 états, 4 points dont un événement et un shop « non évaluables »,
summary, coverage) est en **annexe C** — 400 lignes, validé par `json.loads`.

### 2.4 Invariants du schéma

À faire respecter par le producteur (tests, §7.2) :

1. `points[i].index == i`, `points[i].floor` **strictement croissant**.
2. `stateBefore` / `stateAfter` ∈ `[0, states.length)`, et `stateBefore ≤ stateAfter`.
3. `points[i].stateBefore == points[i-1].stateAfter` (sauf premier point, où `stateBefore` peut être
   `null`).
4. `chosen` ∈ `options` (même `kind` + `id`), et **exactement un** `option.picked == true` — sauf
   point sans choix (`kind: "none"`).
5. Chaque option évaluée a **au moins un** terme avec `source` non vide (règle « pas de phrase sans
   chiffre derrière », docs/27 §4).
6. `regret == null` ⟺ `band == "not_evaluable"` (pas de bande sans regret).
7. Aucune option de type `curse`/`status` dans les options proposées (ce ne sont pas des choix).

---

## 3. Détection d'« erreur » : le regret

### 3.1 Définition

Pour un point `p` et l'état `S` du joueur à cet instant :

```
ΔG(option) = G(S ⊕ option) − G(S)          (points de G, échelle 0..100 — docs/27 §7bis)
ΔG(passer) = 0                             (par définition : rien ne change)
regret(p)  = max(0,  max_{o ∈ options} ΔG(o) − ΔG(choix_réel))
```

Propriétés voulues, et comment on les obtient :

- **Idempotence** : on ne compare jamais deux options entre elles dans l'absolu, seulement leur effet
  sur le **même** état. Une 2ᵉ relique de bloc dans un deck déjà couvert vaut ≈ 0 → ne crée pas de
  regret artificiel.
- **Comparabilité inter-types** : carte, relique, potion, retrait, chemin parlent tous la même unité
  (ΔG), donc un regret de 15 sur une relique et un regret de 15 sur une carte sont commensurables.
  C'est ce qui permet **une seule échelle de bandes** pour toute la review.
- **Co-op** : le regret est calculé **par joueur**, sur son propre état. On ne somme jamais deux
  regrets de deux joueurs (la relique ne va qu'à un seul) ; l'écran affiche la voie du joueur local
  par défaut, avec un sélecteur. Si l'écart entre le 1ᵉʳ et le 2ᵉ joueur est **< 3**, c'est un match
  nul → feuille-pierre-ciseaux (décision Mio, docs/27 §7), et la review affiche « match nul ».

**Pont avec le contrat actuel.** `IScorer` (v1.1) rend un `Score.Total` 0..100, pas un ΔG. En
attendant `Score.DeltaG` (demande §5.3), la review utilise :

```
ΔG ≈ (Score(option).Total − ScoreSkip.Total) / 2
```

parce que `note = clamp(50 + 2·ΔG + prior)` (docs/27 §7bis). Cela suppose l'invariant
`ScoreSkip.Total == 50` dans un état neutre : **à vérifier** (test dédié), sinon les bandes glissent.
Toute timeline stocke `scorerVersion` + `weightsHash` pour rester explicable après un changement de
poids.

### 3.2 Barème proposé

Échelle en **points de G** (pas en centipawns : ΔG est déjà une variation de score 0..100).

| Regret | Bande | Couleur (docs/14 §2.1) | Rail | Panneau | Ralenti auto |
|---|---|---|---|---|---|
| `0` | `best` | `green` #7FFF00 | pastille pleine | « meilleur choix » | non |
| `0 < r < 5` | `info` | `cream` #FFF6E2 à 60 % | petit point | rien | non |
| `5 ≤ r < 12` | `inaccuracy` | `orange` #FFA518 | point orange | chip | non (option) |
| `12 ≤ r < 25` | `error` | `red` #FF5555 | point rouge | carte de justification | **oui**, 0,5× pendant 3 s |
| `r ≥ 25` | `blunder` | `redGlow` #FF0000 | point rouge vif | carte + « vu » | **oui**, 0,5× pendant 5 s |
| — | `not_evaluable` | `cream` 35 % | point creux | « ? » + raison | non |

Bornes explicites : `5` appartient à `inaccuracy`, `12` à `error`, `25` à `blunder`. Aucune bande
n'est « ouverte » : le classement est total et testable.

**Ces seuils sont une hypothèse, pas une vérité.** Ils sont calibrés à vue sur l'échelle ΔG (où un
bon fit vaut 10-20 points) et doivent être **validés par le backtest** (docs/27 §5). Critère
d'acceptation de la calibration : sur les 273 runs locales, la distribution attendue est
≈ 55-70 % `best`/`info`, 15-25 % `inaccuracy`, 8-15 % `error`, 2-6 % `blunder`. Si on obtient 40 % de
blunders, l'échelle est fausse (ou ΔG est mal normalisé) — on ne « corrige » pas en baissant les
seuils sans le dire.

### 3.3 Anti-faux-positifs (règles obligatoires)

Un review tool qui crie « blunder » à tort est pire qu'inutile. Quatre garde-fous :

1. **Match nul** : si `ΔG(meilleur) − ΔG(2ᵉ) < 3`, la bande est **plafonnée à `info`**, quel que soit
   le regret. Un écart sous le bruit du modèle n'est pas une erreur (même seuil que le tie-break RPS
   de docs/27 §7).
2. **Confiance** : si une option est `confidence: "provisional"` (effets de carte manquants), la bande
   est plafonnée à `inaccuracy`, et l'UI l'écrit (« estimation, effets de carte incomplets »).
3. **Le prior ne suffit pas** : un regret qui ne vient **que** du rang intrinsèque (terme L0, ±5)
   n'est pas affiché comme erreur. Il faut au moins un terme **contextuel** (fit, besoin, équipe) pour
   justifier la bande.
4. **Passer est un choix** : si le joueur a passé et que toutes les options avaient `ΔG ≤ 0`, le
   regret vaut 0 et le point est `best`. Symétriquement, si le joueur a pris l'option à `ΔG` max,
   regret = 0 même si la valeur absolue est faible.

Règles de propreté en amont : dédupliquer les offres répétées du même étage (déjà fait par
`PersonalStats`, docs/status S2), ignorer les malédictions/statuts dans les offres, et ne pas produire
de point pour un étage sans aucune décision ni changement d'état.

### 3.4 Justification affichée

Une bande sans raison est un jugement, pas un outil. Chaque point porte donc ses **termes**, et l'UI
les affiche dans cet ordre : le choix réel, le meilleur choix, puis **les 3 termes qui expliquent
l'écart**.

> **Étage 5 — Perfected Strike pris. Il y avait mieux : Bloodletting (ΔG +18,5 vs +6,0).**
> `+8` rang Spire Codex (consensus 70/100) · `+9` fit : ton deck n'a aucune source d'énergie et 13
> cartes à ≥ 1 coût · `+1,5` besoin PV (60/80) — regret **12,5 → erreur**.

Chaque terme porte `label`, `points`, `source` (contrat `ScoreTerm`, enrichi de `Evidence` en v1.2
pour citer les cartes concernées, docs/28 §6). Pas de terme sans source : un terme de fit cite les
cartes qui le motivent (« 5 cartes Strike » / « 0 carte d'énergie »), pas un compteur anonyme.

### 3.5 Le cas « non évaluable » (jamais un zéro menteur)

Quatre causes fréquentes, chacune avec son libellé :

| Cause | Exemple réel | Libellé UI |
|---|---|---|
| Aucune alternative enregistrée | événement `EVENT.SUNKEN_STATUE` : `event_choices` ne garde que le titre choisi | « le jeu n'enregistre pas les options de cet événement » |
| Prix inconnu | étage 4 : 7 cartes en vitrine, `gold_spent` total = 100 | « prix par article absent — valeur sans le coût » |
| Effets de carte incomplets | `data/catalog/cards.json` n'a **aucun** champ `damage`/`block` (0/578) ; `cards_effects.json` est en chantier (docs/28 §5) | « estimation : effets de carte manquants » |
| ΔG indisponible (moteur pas branché) | tout le MVP tant que G n'existe pas | « non calculé » |

Un point non évaluable **compte** dans `coverage` (pour que l'utilisateur sache ce qui manque) et
**ne compte pas** dans les agrégats de regret. Un `regret: 0` par défaut serait un mensonge silencieux
— c'est exactement le genre de faux signal que le projet s'interdit (docs/27 §5).

### 3.6 Deuxième signal, gratuit : l'anomalie de trajectoire

Sans tours, la data permet quand même de dire « ce combat s'est mal passé » — et c'est vérifiable :

```
anomalie(étage) = damage_taken ≥ 2 × avg_damage(personnage, rencontre)  et damage_taken ≥ 15
               ou turns_taken ≥ 1,8 × avg_turns(personnage, rencontre)
```

`avg_damage` / `avg_turns` viennent de `data/codex/encounter_stats.json` (50 rencontres, `n` par
personnage). Exemples **réels** sur la run de test :

| Étage | Rencontre | Subi | Moyenne Ironclad | Tours | Moyenne | Lecture |
|---|---|---|---|---|---|---|
| 21 | `HUNTER_KILLER_NORMAL` | **68** | 26,9 (n = 75 459) | **8** | 4,41 | anomalie nette : 2,5 × la moyenne |
| 25 | `DECIMILLIPEDE_ELITE` | 45 | 45,1 (n = 173 906) | 6 | 4,34 | **normal** — mais fatal car entré à 45/86 PV |
| 7 | `BYGONE_EFFIGY_ELITE` | 31 | 24,9 (n = 149 670) | 5 | 4,39 | dans la norme |

C'est ce qui permet la phrase honnête : « la run n'est pas morte sur l'élite fatale, elle est morte à
l'étage 21 où tu as pris 68 dégâts (moyenne 27) ». Ce signal porte **toujours** son `Basis`
(« n=75 459 Ironclad, v0.107.1 ») et n'est **jamais** présenté comme une erreur de jeu : c'est un
symptôme, pas un diagnostic. Il est utile même quand ΔG est indisponible, donc il ship dans le MVP.

Attention : `encounter_stats.json` est dans `data/codex/` (**gitignoré**, docs/28 §3). Pour le
packaging, il faut un extrait commité (`data/catalog/encounter_benchmarks.json`, ~50 lignes :
`encounter_id`, `character`, `n`, `avg_damage`, `avg_turns`) — sinon la feature ne marche pas chez un
ami qui clone.

### 3.7 Agrégats de run

`ReviewSummary` répond à « où ai-je perdu cette run ? » :

- `regretTotal`, `regretMean` (par point évalué), `scored` / `notEvaluable` ;
- `worstPoints[]` (les 5 plus gros regrets, index + étage + libellé) ;
- `byAct[]` : regret par acte → « acte 1 : 12, acte 2 : 41, acte 3 : 0 » ;
- `concentration` : part du regret total portée par les 3 pires points. C'est la statistique la plus
  utile en pratique (elle dit si la run s'est jouée sur 2-3 décisions ou si c'est un lent
  effritement).

---

## 4. Contrôles de lecture

### 4.1 Table des commandes

| Action | Touche | Comportement |
|---|---|---|
| Lecture / pause | `Espace` | bascule ; l'état de pause est mémorisé par run |
| Vitesse | `1` `2` `3` `4` | 0,5× / 1× / 2× / 4× — **prise en compte au point suivant**, pas de redémarrage |
| Étage suivant / précédent | `K` / `J` | saute d'un point à l'autre (pas d'un état) |
| Recule / avance de 5 s | `←` / `→` | dans la timeline (le temps est simulé, voir §4.2) |
| Prochain point en erreur | `E` | saute au prochain `band ≥ error` |
| Prochain blunder | `Maj+E` | saute au prochain `band == blunder` |
| Afficher / masquer la justification | `I` | la carte de termes |
| Joueur suivant (co-op) | `P` | change de voie |
| Partager | `S` | ouvre le partage (§4.4) |
| Quitter la review | `Échap` | rend le focus au jeu |

Toutes ces touches ne sont actives **que si la review a le focus** ; sinon elles appartiennent au jeu
(règle déjà appliquée par le rail GNOME, docs/14 §7.1).

### 4.2 Sémantique du temps (et de la vitesse)

Il n'y a pas de vidéo : la timeline est une suite de points. Le temps est donc **simulé** :

```
dwell(point) = 2 500 ms / vitesse          // temps d'affichage d'un point
ralenti      : vitesse_effective = max(0,5, vitesse × 0,5) pendant la durée de la carte
```

- Changer la vitesse **ne redémarre pas** le point courant : le nouveau `dwell` s'applique au point
  suivant. C'est la demande « vitesse modifiable à la volée » sans effet de saccade.
- La pause gèle le compte à rebours ; l'utilisateur peut changer de vitesse pendant la pause, l'effet
  est immédiat au retour en lecture.
- Un point `not_evaluable` a un `dwell` court (1 200 ms) : il n'y a rien à lire.
- La barre de scrubbing est indexée sur les **points**, pas sur le temps : cliquer à 40 % va au point
  `floor(0,4 × N)`. Le temps n'est pas une ressource ici.

### 4.3 Ralenti automatique sur erreur

Déclencheur : `regret ≥ 12` (bande `error`) ou `≥ 25` (`blunder`). Comportement :

1. la vitesse de lecture passe à `0,5×` (jamais en dessous : c'est la vitesse la plus lente offerte) ;
2. la **carte de justification** apparaît au-dessus du panneau (fondu 150 ms), avec le choix réel, le
   meilleur choix et les 3 termes ;
3. le point reste affiché 3 s (`error`) ou 5 s (`blunder`) **après la fin de l'animation** ;
4. reprise automatique à la vitesse choisie par l'utilisateur avant le ralenti.

Réglage (compagnon) : `ralenti auto = jamais | erreurs | blunders seulement` — défaut `erreurs`.
Pendant un partage/une démo, `jamais` est probablement le bon défaut (on ne veut pas être interrompu
devant un ami) ; à trancher.

Garde-fou : le ralenti **ne bloque jamais** la lecture. Si l'utilisateur appuie sur `Espace` ou
change de vitesse pendant le ralenti, sa décision gagne et le ralenti est annulé.

### 4.4 Mode partage

Trois niveaux, du plus sûr au plus pratique :

| Niveau | Mécanisme | Avantages | Limites |
|---|---|---|---|
| **MVP** | exporter un fichier `review.json` (~40-120 Ko, la timeline complète) + bouton « copier le chemin » | marche hors ligne, aucune fuite, l'ami n'a besoin de rien | il faut se passer le fichier (Discord, clé USB) |
| **v1.1** | lien `ultima://review/<hash>?b=<build>&p=<point>` résolu via `GET /api/runs/shared/{hash}` | un clic, pas de transfert | **réseau requis** ; ne marche que si la run est **publique** sur spire-codex, sinon 404 → message explicite « cette run n'est pas publique, joins le fichier » |
| **plus tard** | URL web `https://…/r/<hash>?p=12` avec la review rendue en HTML | s'ouvre dans un navigateur, zéro install | hébergement, coût, et il faut décider ce qu'on expose |

Deux points de vigilance, à écrire dans l'UI :

- **Le hash EST l'identité de la run.** Quiconque l'a voit le deck complet, les PV étage par étage, le
  chemin et l'issue. Ce n'est pas anonyme — c'est déjà le contrat de spire-codex, mais il faut le dire
  au moment du partage.
- **Aucun envoi automatique.** Ultima n'uploade jamais une run locale ; le partage par hash ne
  fonctionne que pour les runs que le joueur a déjà publiées (ou via le fichier).

`seed` ne suffit pas à retrouver une run (plusieurs runs par seed, et le RNG dépend de la version) :
le lien porte donc **hash + build**, le seed n'est qu'un affichage.

### 4.5 États vides et erreurs

| Cas | Affichage |
|---|---|
| Run sans aucune décision (run tuée à l'étage 1) | panneau avec « rien à revoir — run terminée à l'étage 1 » ; pas de rail |
| ΔG indisponible | bandeau « review non chiffrée : le moteur de score n'est pas branché » + trajectoire seule (PV, or, dégâts, anomalies) |
| Run d'un autre build que le catalogue | bandeau « build v0.111.0 : les notes peuvent être fausses » + `dataBasis` visible |
| Timeline absente/corrompue | « review indisponible » + bouton « recalculer » (relance le build moteur) |
| Co-op sans L3 | les regrets de reliques sont `not_evaluable` (« choix d'équipe non évalué »), les cartes restent notées |

---

## 5. Intégration (moteur, daemon, UI)

### 5.1 D'où vient la run

| Source | Chemin | Identité | Disponibilité |
|---|---|---|---|
| Historique local | `saves/history/<start_time>.run` via `HistoryReader.ReadAll` | `FileName` = hash 16 hex (= le hash spire-codex, vérifié : `/api/runs/shared/4957a011bbd77f58` renvoie la même run) | 273 runs, hors ligne |
| Corpus public (export) | `data/index/runs-v0.107.1.jsonl.gz` (une ligne = une run, décisions déjà extraites) | champ `h` | 785 121 runs, hors ligne, **gitignoré** |
| Corpus public (API) | `GET /api/runs/shared/{hash}` → JSON complet identique à un `.run` | hash | réseau, cache disque |

La review ne lit **jamais** l'index compact pour construire une timeline : l'index perd les
`floor_added_to_deck` et les états intermédiaires (`pl[].hp/mhp` sont `null`). Il sert au backtest et
à la recherche de runs, pas à la review. **Source de vérité = le JSON complet**, local ou récupéré.

### 5.2 Où tourne le calcul

```
history/*.run ──► Ultima.Saves (RunRecord) ──► Ultima.Engine/Review/ReviewBuilder ──► ReviewTimeline (JSON)
                                                        │
                                              IScorer (ΔG par option)
                                                        │
                            $XDG_RUNTIME_DIR/ultima/review/<hash>.json  ◄── UI (rail + panneau)
```

- **Moteur** : `Ultima.Engine/Review/` — fonction pure `Build(RunRecord, options) → ReviewTimeline`.
  Aucune dépendance UI, aucun accès disque hors catalogue, testable sur fixtures.
- **Pourquoi pas l'UI** : 25-60 points × 3-4 options = 100-250 appels de scoring par run. Le
  compagnon (Python/GTK) n'a pas le catalogue ni les poids ; dupliquer le scoring en Python créerait
  deux vérités. L'UI ne fait qu'animer.
- **Coût** : cible < 200 ms par run (non mesuré). Cache mémoire par `(état, option)`, et cache disque
  de la timeline par `(hash, scorerVersion, weightsHash)` → une run déjà revue s'ouvre instantanément ;
  un changement de poids invalide le cache (c'est le but).
- **Un seul passage** : tout est calculé à l'ouverture, pas de calcul paresseux pendant l'animation
  (sinon la vitesse 4× devient saccadée).

### 5.3 Demande de contrat v1.2 — `ReviewTimeline`

> **Formulé comme demande, pas comme modification.** Aucun fichier de `Ultima.Engine/Contracts/**`
> n'est touché par cette tâche ; le chef arbitre et implémente.

```csharp
// ---- Demande v1.2 (review) : types ADDITIFS, aucun champ existant renommé ----

/// <summary>Timeline complète d'une run passée. Calculée hors ligne par le moteur, jamais pendant le jeu.</summary>
public sealed record ReviewTimeline(
    int ReviewVersion,                 // 1
    ReviewRun Run,
    IReadOnlyList<ReviewLane> Lanes,   // un par joueur (co-op)
    IReadOnlyList<ReviewState> States, // états datés, référencés par index
    IReadOnlyList<ReviewPoint> Points,
    ReviewSummary Summary,
    ReviewCoverage Coverage);

public sealed record ReviewRun(
    string RunId, string BuildId, string GameMode, int Ascension, string CharacterId,
    string? Seed, DateTimeOffset StartedAt, int RunTimeSeconds,
    bool Win, bool WasAbandoned, string? KilledBy,
    string Source,                     // "local:history/…" | "corpus:<build>"
    string ScorerVersion, string? WeightsHash, string DataBasis);

public sealed record ReviewLane(ulong PlayerId, string CharacterId,
    IReadOnlyList<CardView> FinalDeck, IReadOnlyList<RelicView> FinalRelics);

public sealed record ReviewState(
    int Index, int Floor, int Hp, int MaxHp, int Gold,
    IReadOnlyList<CardView> Deck, IReadOnlyList<RelicView> Relics, IReadOnlyList<PotionView> Potions);

public sealed record ReviewPoint(
    int Index, int Floor, int ActIndex, string RoomType, string? ModelId,
    IReadOnlyList<string> MonsterIds, int TurnsTaken, ulong PlayerId,
    int? StateBefore, int StateAfter,     // null = état d'entrée inconnu (étage 1)
    ReviewDelta Delta,
    ReviewKind Kind,                      // card_reward | relic_reward | ancient | shop | rest | event | removal | transform | potion | none
    ReviewChoice? Chosen,
    IReadOnlyList<ReviewOption> Options,
    ReviewRegret? Regret,                 // null = non évaluable
    string? Note);

public sealed record ReviewDelta(int DamageTaken, int HpHealed, int GoldDelta, int MaxHpDelta);

public sealed record ReviewChoice(string Kind, string? Id);

/// <summary>Nature du point. `none` = étage sans décision (état seul).</summary>
public enum ReviewKind { None, CardReward, RelicReward, Ancient, Shop, Rest, Event, Removal, Transform, Potion }

public sealed record ReviewOption(string Kind, string? Id, string Label,
    double? DeltaG, bool Picked, bool Available, string? UnavailableReason,
    string Confidence,                    // full | provisional | none
    IReadOnlyList<ScoreTerm> Terms);

public sealed record ReviewRegret(double DeltaG, string Band, string? BestId,
    string Confidence, string Basis);

public sealed record ReviewSummary(int Points, int Scored, int NotEvaluable,
    double RegretTotal, double RegretMean,
    IReadOnlyList<int> WorstPoints, double Concentration,
    IReadOnlyList<ReviewActRegret> ByAct, string? Note);

public sealed record ReviewActRegret(int ActIndex, int Scored, double RegretTotal);

public sealed record ReviewCoverage(int Scored, int NotEvaluable,
    IReadOnlyList<string> Reasons, string DataBasis);

public static class RegretBands
{
    public const string Best = "best", Info = "info", Inaccuracy = "inaccuracy",
        Error = "error", Blunder = "blunder", NotEvaluable = "not_evaluable";
    public static string FromRegret(double r) => r <= 0 ? Best : r < 5 ? Info : r < 12 ? Inaccuracy : r < 25 ? Error : Blunder;
}
```

> Vérifié : ce bloc **compile tel quel** contre le contrat v1.1 (projet de contrôle référençant
> `Ultima.Engine`, `dotnet build` → 0 erreur, 0 avertissement). Les types référencés (`CardView`,
> `RelicView`, `PotionView`, `ScoreTerm`) sont ceux du contrat existant, inchangés.

Trois demandes annexes, dans l'ordre d'utilité :

1. **`Score.DeltaG`** (`double?`) — évite le proxy `(Total − SkipTotal)/2` et son hypothèse
   `SkipTotal == 50`. Additif, v1.2.
2. **`EngineState.Review`** (`ReviewTimeline?`, null hors review) + `ModuleIds.Review` — l'UI n'a pas
   d'autre canal pour recevoir la timeline. Alternative préférable si le câbleur la défend : un
   **one-shot** sur le socket (`{"op":"review.open","runId":"…"}` → `{"ok":true,"path":"…"}`), parce
   que la review n'est pas un état vivant et n'a rien à faire dans un message réécrit 10×/s.
   *Recommandation : one-shot + fichier, `EngineState.Review` seulement si le one-shot coûte trop cher.*
3. **`ScoreTerm.Evidence`** (déjà demandé par docs/28 §6) — c'est ce qui permet « +9 fit : 0 carte
   d'énergie » plutôt que « +9 fit ».

### 5.4 Ce qui n'est PAS demandé

- Aucun champ de combat dans le contrat (`hand`, `turn`, `intent`) : la data ne l'a pas.
- Aucun changement de `RunView`, `RewardView`, `ShopView`, `PathsView` : la review est un **objet
  séparé**, pas une extension de l'état live.
- Aucune dépendance réseau dans le moteur : la récupération d'une run publique est un travail
  d'adaptateur (outil/daemon), pas du `ReviewBuilder`.

---

## 6. Contraintes UI (rail discret, verre, click-through)

### 6.1 Le rail : réutiliser l'existant, pas en ajouter un

Le rail existe déjà : `overlay/gnome/ultima@mio/geometry.js` — **14 px** de large (8 px était
invisible, leçon v1), ancré au bord droit, panneau 260 px, zone de survol avec 10 px de tolérance,
repli automatique après 2,5 s. La review **change le sens des pastilles**, elle n'ajoute pas de rail :

| Mode | Pastille de rail | Sens |
|---|---|---|
| Jeu (aujourd'hui) | 8 × 8 px, teinte = urgence | « il y a quelque chose à regarder » |
| Review | 8 × 8 px, teinte = bande de regret | un étage ; clic = saut au point |

- Le rail reste **click-through** en dehors de la zone de survol (`reactive: false` sur les pastilles,
  déjà le cas) ; seule la bande de survol devient `Stop`.
- En review, le rail est **toujours visible** (il porte la carte des étages) ; en jeu, le module
  `review` est **désactivé par défaut**.
- Échelle : une pastille par étage dans la hauteur du rail (36 % de l'écran, soit ~389 px en 1080p →
  7,9 px par étage pour une run de 49). Si le nombre de points descend sous 3 px par pastille (rail
  court, run longue), on agrège : 1 pastille = 2 étages, avec le détail au survol.

### 6.2 Le panneau

Un seul panneau, **temporaire**, en bas au centre, qui n'existe qu'en review :

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ ◀◀   ⏸    ▶▶   │  Étage 5 · Acte 1 · Chasseur Tueur  │  ⚠ 12,5 (erreur)  │ 0,5× 1× 2× 4× │
│ ▁▁▂▃▂▁▁▃▅▇▅▃▁▁▂▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁          │
└──────────────────────────────────────────────────────────────────────────────┘
```

- Largeur 960 px, hauteur 112 px, verre « Frosted Obsidian » (`docs/14 §4` : `glass_tint`
  `rgba(0.043,0.055,0.078,0.72)`, bordure or `rgba(0.937,0.784,0.318,0.85)` à 1 px), rayon 6 px,
  aucune barre de titre.
- **Scrub bar = courbe de regret cumulé**, pas une barre de progression muette : chaque segment est
  coloré par sa bande. C'est la « eval bar » de chess.com, mais honnête — elle montre le regret, pas
  une probabilité de victoire inventée.
- La carte de justification (420 px) apparaît **au-dessus** du panneau, seulement en pause/survol ou
  pendant un ralenti ; elle disparaît au retour en lecture normale.
- En jeu (si la review est ouverte pendant une run live), le panneau **s'ancre au rail** (à droite,
  jamais en bas) : la zone basse est réservée à la main et à *End Turn* (docs/14 §7.1, `MouseFilter`
  `Ignore` sur la racine).

### 6.3 Tokens

| Élément | Valeur | Source |
|---|---|---|
| Fond | `glass_tint` 0.043/0.055/0.078, α 0.72 | docs/14 §4 |
| Bordure | `gold` #EFC851, α 0.85, 1 px | docs/14 §2.1 |
| Titres | `kreon_bold` 16-18 px | docs/14 §3 |
| Chiffres (regret, ΔG) | `source_code_pro_semibold` 15 px | docs/14 §3 |
| Bandes | `green` / `cream` / `orange` / `red` / `redGlow` | docs/14 §2.1 |
| Transition | `Tween` cubic out, **150 ms** | docs/14 §7.2 |
| Opacité au repos | 0.25-0.30, réveil au survol | docs/14 §1.2 |

### 6.4 Règles d'or (non négociables)

1. **Aucun cadre permanent ajouté.** En jeu, la seule trace d'Ultima reste le rail 14 px. La review
   n'existe que quand elle est ouverte ; à la fermeture, tout est libéré.
2. **Click-through partout sauf le panneau.** `MouseFilter.Ignore` sur la racine ; `Stop` uniquement
   sur les contrôles du panneau et la bande de survol du rail.
3. **Pas de blur quand le rail est seul** (« rail = aplat, flou seulement panneau ouvert », leçon
   déjà consignée dans `plan.js`).
4. **Zéro polling** : la review est un objet statique ; l'UI ne recalcule rien, n'interroge rien.
5. **Aucun chiffre sans source.** Un terme sans `source` ne s'affiche pas ; une bande sans `basis` non
   plus.

---

## 7. Livraison

### 7.1 MVP — « S10-a : review de décisions »

Périmètre strict, livrable seul :

1. `Ultima.Engine/Review/ReviewBuilder` : `RunRecord` → `ReviewTimeline` (états, points, options,
   regret, summary, coverage). Décisions notées : **carte, relique, Ancien, retrait**. Marché,
   événement, repos : présents dans la timeline mais `regret: null` (raisons de §3.5).
2. **Anomalies de trajectoire** (§3.6) — fonctionne même sans ΔG, donc livrable tout de suite.
3. Panneau + rail de review dans le compagnon / l'extension GNOME : lecture, pause, 0,5×/1×/2×/4×,
   saut d'étage, retour arrière, prochain point en erreur, carte de justification.
4. Export `review.json` (partage par fichier). Pas de lien, pas de réseau.

**Critères d'acceptation** (mesurables, ce sont les tests de §7.2) :

- les 273 runs locales produisent une timeline sans exception, en < 200 ms chacune ;
- `points` est strictement croissant en étage et couvre tous les étages avec décision ou changement
  d'état ;
- le deck reconstruit est **monotone** : `deck(N) ⊇ deck(N−1)` sauf retrait/transformation enregistré
  à l'étage N, et `deck(N_max)` = deck final (multiset) — les deux mesurés à 0 violation sur
  15 213 étages-joueurs (§2.2) ;
- aucun point n'a de bande si `deltaG` est absent ; aucun terme sans `source` ;
- le JSON fait l'aller-retour (sérialisation → désérialisation) sans perte.

### 7.2 Tests (à écrire avec le code, pas après)

| # | Test | Invariant vérifié |
|---|---|---|
| 1 | `Timeline_has_one_point_per_observed_floor` | §2.4-1 |
| 2 | `Deck_at_floor_is_reconstructed_as_multiset` | §2.2 (fixture : run 4957a011bbd77f58, étages 2 / 5 / 24) |
| 3 | `Relic_state_respects_floor_added` | §2.2 |
| 4 | `Regret_is_zero_when_best_option_chosen` | §3.1 |
| 5 | `Skip_is_the_zero_baseline` | §3.1 |
| 6 | `Band_boundaries_are_exact` (4,99 / 5 / 11,99 / 12 / 24,99 / 25) | §3.2 |
| 7 | `Missing_effects_yields_not_evaluable_not_zero` | §3.5 |
| 8 | `Tie_below_three_caps_band_to_info` | §3.3-1 |
| 9 | `Coop_points_are_per_player_and_never_summed` | §3.1 |
| 10 | `Every_displayed_term_has_a_source` | §2.4-5 |
| 11 | `Timeline_roundtrips_json` | §7.1 |
| 12 | `Trajectory_anomaly_uses_benchmark_and_basis` | §3.6 |

Les fixtures existent déjà (`fixtures/` + les 273 runs) : le test 2 se fait sur la run réelle citée,
les autres sur des runs mutées (comme l'a fait S2 pour `PersonalStats`).

### 7.3 Plus tard (dans cet ordre de valeur)

| # | Brique | Dépend de |
|---|---|---|
| 1 | Lien de partage `ultima://review/<hash>` | runs publiques, adaptateur API |
| 2 | Prix en shop + regret d'achat | replay NDJSON (voie B) ou table de prix par build |
| 3 | Notes/annotations personnelles par point (« pourquoi j'ai pris ça ») | stockage local, aucun contrat |
| 4 | Review d'une run du corpus public (785 k) pour comparer ses choix à la population | index + `ReviewBuilder` sur JSON récupéré |
| 5 | Onglet combat (mains, tours, dégâts) | voie B (§1.4) |
| 6 | Annotations communautaires | serveur, modération, **hors périmètre produit pour l'instant** |
| 7 | Mode « review partagée » (deux curseurs, comme un chat) | temps réel, hors périmètre |

### 7.4 Hors périmètre (à dire explicitement)

- Rejouer le combat (re-simulation) : autre produit, autre moteur.
- Noter le chemin : les alternatives ne sont pas dans la data (§1.2).
- Noter les événements : idem.
- « Probabilité de victoire » : on n'a pas de modèle calibré ; le regret n'est pas une probabilité.

---

## 8. Risques et questions ouvertes

| # | Risque | Impact | Mitigation |
|---|---|---|---|
| 1 | **ΔG n'existe pas encore** (dépend de G + effets de cartes, docs/28 §5) | tout le chiffrage est provisoire | proxy `(Total−SkipTotal)/2`, `confidence: provisional` affiché, `scorerVersion` stocké ; les seuils seront recalibrés au premier vrai ΔG |
| 2 | **Effets de cartes absents** : `data/catalog/cards.json` n'a **aucun** `damage`/`block` (0/578) | le fit par manques (L1) est aveugle sur l'offense | `cards_effects.json` (chantier S7) ; en attendant, `confidence: provisional` et bande plafonnée |
| 3 | **Seuils non validés** | faux « blunders » en série | critère de distribution (§3.2) sur 273 runs avant de figer |
| 4 | **Co-op à 39 %** du corpus, L3 absent | regrets de reliques faux si on les calcule en solo | co-op → reliques `not_evaluable`, cartes notées |
| 5 | **Bruit du hash/partage** | l'utilisateur croit partager un lien privé | message explicite « le hash rend la run publique/lisible » |
| 6 | **Perf UI à 4×** | animation saccadée | tout est précalculé ; `dwell` minimum 300 ms |
| 7 | **Replay = service tiers** | dépendance, disponibilité, ToS | voie B isolée, cache local, jamais dans le chemin critique |
| 8 | **`encounter_stats.json` gitignoré** | §3.6 muet chez un ami | extrait commité `data/catalog/encounter_benchmarks.json` (< 50 lignes) |

Questions à trancher par Mio :

1. **Voie B** : on l'ouvre (et on active le mod localement) ou on reste en review de décisions pour
   tout le monde ? (§1.4)
2. **Ralenti par défaut** : `erreurs` (pédagogique) ou `jamais` (mode démo devant un ami) ?
3. **Partage** : fichier seulement au MVP, ou lien dès la v1.1 (donc réseau) ?
4. **Nom affiché** : « Review », « Replay », « Revue » ? (« replay » est trompeur sans combat ; je
   propose **« Revue »** en FR, `review` en interne.)

---

## Annexe A — Repro des mesures

```bash
cd ~/sts2

# 1. Volumétrie du corpus local (273 runs, 7 581 étages, 15 213 étages-joueurs)
python3 - <<'PY'
import json,glob,collections
floors=0; pf=0; runs=0; dec=collections.Counter()
for p in glob.glob('data/runs/*/*.run'):
    d=json.load(open(p)); runs+=1
    for act in d.get('map_point_history') or []:
        for pt in act:
            floors+=1
            for ps in pt['player_stats']:
                pf+=1
                if ps.get('card_choices'): dec['card']+=1
print(runs, floors, pf, dict(dec))
PY

# 2. has_replay dans le corpus local
python3 -c "import json,glob; print(sum(1 for p in glob.glob('data/runs/*/*.run') if json.load(open(p)).get('has_replay')))"   # → 22

# 3. L'export public ne contient pas le champ (0 occurrence sur 785 121 lignes, ~34 s)
zcat data/exports/runs-v0.107.1.jsonl.gz | grep -c '"has_replay"'    # → 0

# 4. Un replay est bien servi pour les runs qui le portent
curl -s -o /tmp/r.ndjson -w '%{http_code} %{size_download}\n' \
  https://spire-codex.com/api/runs/7287113b54a6745b/replay              # → 200 851961
head -1 /tmp/r.ndjson | cut -c1-200                                     # → {"t":"header","replay_version":7,…

# 5. Reconstruction du deck : 3 invariants sur 15 213 étages-joueurs (0 violation)
#    (script de contrôle : cartes sans floor_added, cartes au-delà du dernier étage,
#     baisses de deck sans retrait/transformation enregistré)
```

## Annexe B — Correspondance champ de save → champ de timeline

| Save (`history/*.run`) | Timeline | Remarque |
|---|---|---|
| `players[].id` | `lanes[].playerId` / `points[].playerId` | 1 en solo, SteamID64 en co-op |
| `players[].deck[].id` + `floor_added_to_deck` + `current_upgrade_level` | `states[].deck[]` | reconstruction §2.2 |
| `players[].relics[]` (`floor_added_to_deck`) | `states[].relicIds[]` | idem |
| `map_point_history[act][floor].map_point_type` | `points[].roomType` | 8 types (`RoomTypes`) |
| `…rooms[0].model_id` | `points[].modelId` | « ENCOUNTER.X » / « EVENT.Y » |
| `…rooms[0].monster_ids` | `points[].monsterIds` | vide hors combat |
| `…rooms[0].turns_taken` | `points[].turnsTaken` | tours du combat, pas détail |
| `…player_stats[].current_hp` / `max_hp` | `states[].hp` / `maxHp` | **après** l'étage |
| `…player_stats[].current_gold` | `states[].gold` | après l'étage |
| `…player_stats[].damage_taken` / `hp_healed` | `points[].delta` | combat + événement |
| `…card_choices[]` (`card.id`, `was_picked`) | `points[].options[]` (`kind: card`) | + `skip` toujours ajouté |
| `…relic_choices[]` / `ancient_choice[]` | `options[]` (`kind: relic`) | `ancient_choice` = 3 options sûres |
| `…cards_removed[]` / `cards_transformed[]` | `options[]` (`kind: removal`) + reconstruction | idem |
| `…rest_site_choices[]` | `points[].chosen` (`kind: rest`) | options non connues → `regret: null` |
| `…event_choices[].title.key` | `points[].chosen` (`kind: event`) | options non connues → `regret: null` |
| `…gold_spent` | `delta.goldDelta` (négatif) | pas de prix par article |
| `win` / `was_abandoned` / `killed_by_encounter` | `run.win` / `wasAbandoned` / `killedBy` | — |
| `build_id`, `seed`, `start_time`, `run_time` | `run.*` | `build_id` filtre le catalogue |

## Annexe C — Exemple complet de timeline (run 4957a011bbd77f58)

États, options, rencontres et PV **réels** ; `deltaG`, `regret` et `summary` **illustratifs** (§2.3) —
le moteur ΔG n'est pas encore branché. Validé par `json.loads`. Extrait de 4 points (la run en a 25).

```json
{
  "reviewVersion": 1,
  "contractVersion": 1,
  "run": {
    "runId": "4957a011bbd77f58",
    "buildId": "v0.107.1",
    "gameMode": "standard",
    "ascension": 8,
    "characterId": "CHARACTER.IRONCLAD",
    "seed": "78J5E957Z8",
    "startedAt": "2026-10-28T14:36:51Z",
    "runTimeSeconds": 1383,
    "win": false,
    "wasAbandoned": false,
    "killedBy": "ENCOUNTER.DECIMILLIPEDE_ELITE",
    "source": "local:history/4957a011bbd77f58.run",
    "scorerVersion": "s10-a/1",
    "weightsHash": "n/a (ΔG illustratif)",
    "dataBasis": "data/catalog@9c25fec + Spire Codex v0.107.1"
  },
  "lanes": [
    {
      "playerId": 1,
      "characterId": "CHARACTER.IRONCLAD",
      "finalDeckCount": 28,
      "finalRelicIds": [
        "RELIC.BLACK_BLOOD",
        "RELIC.CENTENNIAL_PUZZLE",
        "RELIC.HEFTY_TABLET",
        "RELIC.JUZU_BRACELET",
        "RELIC.LETTER_OPENER",
        "RELIC.LUCKY_FYSH",
        "RELIC.MEAL_TICKET",
        "RELIC.OLD_COIN",
        "RELIC.SWORD_OF_STONE",
        "RELIC.TOUCH_OF_OROBAS"
      ]
    }
  ],
  "states": [
    {
      "index": 0,
      "floor": 1,
      "hp": 64,
      "maxHp": 80,
      "gold": 99,
      "deck": [
        "CARD.ASCENDERS_BANE",
        "CARD.BASH",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.HELLRAISER+",
        "CARD.INJURY",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD"
      ],
      "relicIds": ["RELIC.HEFTY_TABLET"]
    },
    {
      "index": 1,
      "floor": 2,
      "hp": 60,
      "maxHp": 80,
      "gold": 114,
      "deck": [
        "CARD.ASCENDERS_BANE",
        "CARD.BASH",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.HELLRAISER+",
        "CARD.INJURY",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.TREMBLE"
      ],
      "relicIds": ["RELIC.HEFTY_TABLET"]
    },
    {
      "index": 2,
      "floor": 3,
      "hp": 60,
      "maxHp": 80,
      "gold": 114,
      "deck": [
        "CARD.ASCENDERS_BANE",
        "CARD.BASH",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.HELLRAISER+",
        "CARD.INJURY",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.TREMBLE"
      ],
      "relicIds": ["RELIC.HEFTY_TABLET", "RELIC.SWORD_OF_STONE"]
    },
    {
      "index": 3,
      "floor": 4,
      "hp": 60,
      "maxHp": 80,
      "gold": 14,
      "deck": [
        "CARD.ASCENDERS_BANE",
        "CARD.BASH",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.HELLRAISER+",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.TREMBLE"
      ],
      "relicIds": ["RELIC.HEFTY_TABLET", "RELIC.SWORD_OF_STONE"]
    },
    {
      "index": 4,
      "floor": 5,
      "hp": 60,
      "maxHp": 80,
      "gold": 29,
      "deck": [
        "CARD.ASCENDERS_BANE",
        "CARD.BASH",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.HELLRAISER+",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.STRIKE_IRONCLAD",
        "CARD.TREMBLE",
        "CARD.PERFECTED_STRIKE"
      ],
      "relicIds": ["RELIC.HEFTY_TABLET", "RELIC.SWORD_OF_STONE"]
    }
  ],
  "points": [
    {
      "index": 0,
      "floor": 2,
      "actIndex": 0,
      "roomType": "monster",
      "modelId": "ENCOUNTER.SHRINKER_BEETLE_WEAK",
      "monsterIds": ["MONSTER.SHRINKER_BEETLE"],
      "turnsTaken": 5,
      "playerId": 1,
      "kind": "card_reward",
      "stateBefore": 0,
      "stateAfter": 1,
      "delta": {"damageTaken": 10, "hpHealed": 6, "goldDelta": 15, "maxHpDelta": 0},
      "chosen": {"kind": "card", "id": "CARD.TREMBLE"},
      "options": [
        {
          "kind": "card",
          "id": "CARD.TREMBLE",
          "label": "Tremble",
          "deltaG": 2.0,
          "picked": true,
          "available": true,
          "confidence": "provisional",
          "terms": [
            {"label": "rang Spire Codex", "points": 2, "source": "Spire Codex — consensus 52/100"},
            {"label": "fit : 0 manque comblé", "points": 0, "source": "deck : 12 cartes, bloc 9/tour"},
            {"label": "prior intrinsèque", "points": 0, "source": "table locale"}
          ]
        },
        {
          "kind": "card",
          "id": "CARD.HAVOC",
          "label": "Havoc",
          "deltaG": 4.5,
          "picked": false,
          "available": true,
          "confidence": "provisional",
          "terms": [
            {"label": "rang Spire Codex", "points": 4, "source": "Spire Codex — consensus 61/100"},
            {"label": "fit : moteur de pioche", "points": 1, "source": "deck sans pioche (0 carte)"},
            {"label": "besoin PV : 60/80", "points": -1, "source": "bande 60-80 % → 5 pts, déjà couvert"}
          ]
        },
        {
          "kind": "card",
          "id": "CARD.BATTLE_TRANCE",
          "label": "Battle Trance",
          "deltaG": -1.5,
          "picked": false,
          "available": true,
          "confidence": "provisional",
          "terms": [
            {"label": "rang Spire Codex", "points": 1, "source": "Spire Codex — consensus 40/100"},
            {"label": "anti-synergie : pas de moteur", "points": -3, "source": "deck sans carte à 0 coût"},
            {"label": "besoin PV : 60/80", "points": 0, "source": "aucun soin dans l'offre"}
          ]
        },
        {
          "kind": "skip",
          "id": null,
          "label": "Passer",
          "deltaG": 0.0,
          "picked": false,
          "available": true,
          "confidence": "provisional",
          "terms": []
        }
      ],
      "regret": {"deltaG": 2.5, "band": "info", "bestId": "CARD.HAVOC", "confidence": "provisional", "basis": "max(ΔG) − ΔG(choix) = 4,5 − 2,0 ; écart meilleur/2e = 2,5 < 3 → plafond « info »"},
      "note": null
    },
    {
      "index": 1,
      "floor": 3,
      "actIndex": 0,
      "roomType": "unknown",
      "modelId": "EVENT.SUNKEN_STATUE",
      "monsterIds": [],
      "turnsTaken": 0,
      "playerId": 1,
      "kind": "event",
      "stateBefore": 1,
      "stateAfter": 2,
      "delta": {"damageTaken": 0, "hpHealed": 0, "goldDelta": 0, "maxHpDelta": 0},
      "chosen": {"kind": "relic", "id": "RELIC.SWORD_OF_STONE"},
      "options": [
        {
          "kind": "relic",
          "id": "RELIC.SWORD_OF_STONE",
          "label": "Sword of Stone",
          "deltaG": null,
          "picked": true,
          "available": true,
          "confidence": "none",
          "terms": []
        }
      ],
      "regret": null,
      "note": "options non enregistrées : event_choices ne garde que le titre choisi (aucune alternative connue)"
    },
    {
      "index": 2,
      "floor": 4,
      "actIndex": 0,
      "roomType": "shop",
      "modelId": null,
      "monsterIds": [],
      "turnsTaken": 0,
      "playerId": 1,
      "kind": "shop",
      "stateBefore": 2,
      "stateAfter": 3,
      "delta": {"damageTaken": 0, "hpHealed": 0, "goldDelta": -100, "maxHpDelta": 0},
      "chosen": {"kind": "removal", "id": "CARD.INJURY"},
      "options": [
        {
          "kind": "removal",
          "id": "CARD.INJURY",
          "label": "Retrait — Injury",
          "deltaG": 3.0,
          "picked": true,
          "available": true,
          "confidence": "provisional",
          "terms": [
            {"label": "malédiction retirée", "points": 3, "source": "deck : 1 malédiction"}
          ]
        },
        {
          "kind": "card",
          "id": "CARD.FEEL_NO_PAIN",
          "label": "Feel No Pain",
          "deltaG": 9.0,
          "picked": false,
          "available": true,
          "confidence": "provisional",
          "terms": []
        },
        {
          "kind": "card",
          "id": "CARD.SHOCKWAVE",
          "label": "Shockwave",
          "deltaG": 6.5,
          "picked": false,
          "available": true,
          "confidence": "provisional",
          "terms": []
        }
      ],
      "regret": null,
      "note": "prix par article absents de la save (seul gold_spent total = 100) → regret non calculé sur le shop"
    },
    {
      "index": 3,
      "floor": 5,
      "actIndex": 0,
      "roomType": "monster",
      "modelId": "ENCOUNTER.FUZZY_WURM_CRAWLER_WEAK",
      "monsterIds": ["MONSTER.FUZZY_WURM_CRAWLER"],
      "turnsTaken": 4,
      "playerId": 1,
      "kind": "card_reward",
      "stateBefore": 3,
      "stateAfter": 4,
      "delta": {"damageTaken": 6, "hpHealed": 6, "goldDelta": 15, "maxHpDelta": 0},
      "chosen": {"kind": "card", "id": "CARD.PERFECTED_STRIKE"},
      "options": [
        {
          "kind": "card",
          "id": "CARD.PERFECTED_STRIKE",
          "label": "Perfected Strike",
          "deltaG": 6.0,
          "picked": true,
          "available": true,
          "confidence": "provisional",
          "terms": [
            {"label": "rang Spire Codex", "points": 5, "source": "Spire Codex — consensus 63/100"},
            {"label": "fit : 5 cartes « Strike » dans le deck", "points": 1, "source": "deck au sol 5"}
          ]
        },
        {
          "kind": "card",
          "id": "CARD.BLOODLETTING",
          "label": "Bloodletting",
          "deltaG": 18.5,
          "picked": false,
          "available": true,
          "confidence": "provisional",
          "terms": [
            {"label": "rang Spire Codex", "points": 8, "source": "Spire Codex — consensus 70/100"},
            {"label": "fit : énergie, manque détecté", "points": 9, "source": "deck : 0 carte d'énergie, 13 cartes ≥ 1 coût"},
            {"label": "besoin PV : 60/80", "points": 1.5, "source": "bande 60-80 % → 5 pts"}
          ]
        },
        {
          "kind": "card",
          "id": "CARD.ANGER",
          "label": "Anger",
          "deltaG": -2.0,
          "picked": false,
          "available": true,
          "confidence": "provisional",
          "terms": [
            {"label": "rang Spire Codex", "points": 0, "source": "Spire Codex — consensus 45/100"},
            {"label": "anti-synergie : dilue un deck 13 cartes", "points": -2, "source": "deck : 13 cartes, aucune pioche"}
          ]
        },
        {
          "kind": "skip",
          "id": null,
          "label": "Passer",
          "deltaG": 0.0,
          "picked": false,
          "available": true,
          "confidence": "provisional",
          "terms": []
        }
      ],
      "regret": {"deltaG": 12.5, "band": "error", "bestId": "CARD.BLOODLETTING", "confidence": "provisional", "basis": "max(ΔG) − ΔG(choix) = 18,5 − 6,0 ; écart meilleur/2e = 12,5 ≥ 3"},
      "note": null
    }
  ],
  "summary": {
    "points": 4,
    "scored": 2,
    "notEvaluable": 2,
    "regretTotal": 15.0,
    "regretMean": 7.5,
    "worstPoints": [3],
    "worstFloor": 5,
    "concentration": 0.83,
    "byAct": [
      {"actIndex": 0, "scored": 2, "regretTotal": 15.0}
    ],
    "note": "extrait de 4 points sur les 25 de la run"
  },
  "coverage": {
    "scored": 2,
    "notEvaluable": 2,
    "reasons": ["event : options non enregistrées (1)", "shop : prix absents (1)"],
    "dataBasis": "273 runs locales, build v0.107.1"
  }
}
```
