# 30 — Format des replays de combat (NDJSON) et ce qu'ils permettent de valider

> Mesuré sur les **22 replays** réellement téléchargés le 2026-10-29 (`data/replays/*.ndjson`,
> 10,34 Mo, 60 920 événements). Tout ce qui suit a été **lu dans les fichiers**, pas déduit d'une
> doc externe. Les champs listés existent ; ceux qui manquent sont signalés comme manquants.
>
> Outils : `tools/fetch_replays.py` (téléchargement), `tools/parse_replays.py` (index),
> `tools/validate_replays.py` (validation chiffrée). Sorties dans `data/replays/` (gitignoré).

---

## 1. Provenance et disponibilité

| Point | Valeur mesurée |
|---|---|
| Endpoint | `GET https://spire-codex.com/api/runs/{hash}/replay` |
| Type de contenu | `application/x-ndjson` (une ligne = un événement JSON) |
| Runs locales | **273** (`data/runs/**/*.run`) |
| Runs avec `has_replay: true` | **22** (8,1 %) |
| Téléchargements réussis | **22 / 22** (HTTP 200) |
| Run sans replay | HTTP **404** (vérifié sur `0077b156e966677b`) |
| Taille | **10,34 Mo** au total · moyenne **470 Ko** · médiane 331 Ko · min 9 Ko · max 1 353 Ko |
| Lignes par fichier | min 15 · médiane 2 226 · max 8 018 |
| Producteur | mod `SpireCodex v1.2.1` (+ `BaseLib v3.3.8`, `UnifiedSavePath`) |
| Versions de format | `replay_version: 7`, `run_schema_version: 9` |
| Builds rencontrés | `v0.107.1` (9 runs), `v0.111.0` (11), `0.1.0` (2, build non renseigné côté jeu) |

Les fichiers courts ne sont pas tronqués : leur dernier événement est `end` et ils correspondent à
des runs mortes à l'étage 1-2 (9 Ko = 15 lignes).

**Volume si l'on voulait tout** : 785 121 runs × 8,1 % ≈ **63 600 replays** ; à 470 Ko de moyenne
cela fait **≈ 30 Go** de NDJSON (et ~13 h de téléchargement séquentiel au débit mesuré de
0,77 s/run, sans compter les 429). Ce n'est pas un corpus à rapatrier : c'est un **sidecar** qu'on
lit run par run quand elle existe.

---

## 2. Enveloppe commune à tous les événements

Chaque ligne est un objet JSON avec :

| Champ | Sens observé |
|---|---|
| `t` | type d'événement (66 types distincts, §3) |
| `s` | numéro de séquence, **monotone croissant sur tout le fichier** (0 violation sur 60 920 lignes, segments compris) |
| `ms` | horodatage relatif **en millisecondes depuis le début du segment** ; repart à 0 à chaque `header` (reprise/rechargement) |
| `floor` | étage courant (0 avant le premier étage ; 1..49 ensuite) |
| `act` | acte courant (1..3 ; 1 avant le premier étage) |

Un run peut contenir **plusieurs `header`** : 35 en-têtes pour 22 runs (13 runs reprises au moins
une fois). Chaque en-tête ouvre un nouveau segment : `s` continue d'incrémenter, `ms` repart à 0 et
le champ `reloads` du header donne le nombre de rechargements déjà vus.

---

## 3. Inventaire des 66 types d'événements

Décompte sur les 22 fichiers. La colonne « champs toujours présents » liste les clés présentes dans
**toutes** les occurrences du type (les champs optionnels sont cités dans les sections suivantes).

| Type | Nb | Champs toujours présents |
|---|---|---|
| `draw` | 8 746 | `c, id, turn_start` (+ `deck_c` presque toujours) |
| `hit` | 6 772 | `src, dst, dmg, blocked, killed, dmg_type` |
| `play` | 5 470 | `c, id, up, auto, play_index, play_count, turn, cost_paid, cost_value, stars_paid, stars_value, result_pile` |
| `power` | 5 421 | `src, id, n, amount, tgt` |
| `energy` | 4 032 | `d, energy` (+ `n, max, carry, src`) |
| `block` | 3 420 | `src, n, left, reason` |
| `turn` | 2 620 | `combat_id, attempt_id, n, round, side` |
| `intent` | 2 349 | `src, src_cid, id, at` |
| `end_turn` | 2 243 | `n, round, side` |
| `power_lost` | 1 881 | `id, n, amount, tgt` |
| `move` | 1 532 | `src, src_cid, id` |
| `flush` | 1 262 | `flushed_c, retained_c` |
| `exhaust` | 892 | `c, id, ethereal` |
| `decision` | 852 | `decision_id, decision_type, source, n_presented, n_selectable, options` |
| `generate` | 831 | `c, id` |
| `hp` | 827 | `dst, d, hp, src` (`src` ∈ `heal`, `loss`, `set`) |
| `orb_passive` | 715 | `id, oid, val` |
| `deck` | 633 | `cards` (liste complète du deck) |
| `resolve` | 600 | `reward_kind` |
| `death` | 575 | `tgt, killed_by, removal_prevented` |
| `max_hp` | 567 | `dst, d, hp, max_hp` |
| `pick` | 564 | `n_picked, selector, cards` |
| `orb_evoke` | 552 | `id, oid, val, kept` |
| `room` | 541 | `kind, room_type, depth, coord` |
| `orb_channel` | 519 | `id, oid` |
| `outcome` | 510 | `decision_id, outcome` |
| `room_exit` | 504 | `kind, depth` |
| `upgrade` | 459 | `c, id` |
| `discard` | 417 | `c, id` |
| `attack` | 394 | `atk, hits, src` |
| `acquire` | 375 | `c, id, source` |
| `afflict` | 357 | `c, id, affliction, amount, amount_total` |
| `shuffle` | 348 | `n_draw, order_c` |
| `gold` | 323 | `gold` |
| `combat_start` | 253 | `combat_id, attempt_id, encounter, enemies, allies` |
| `draw_order` | 253 | `order_c, order_deck_c` |
| `combat_end` | 221 | `combat_id, attempt_id, result, turns, rounds` (`hp_lost_total` presque toujours) |
| `unafflict` | 202 | `c, id, affliction, amount` |
| `relic` | 178 | `id` |
| `forge` | 140 | `amount, blades, src` |
| `potion_got` / `potion_start` / `potion_used` | 128 / 120 / 120 | `id` |
| `spawn` | 116 | `cid, id, hp, max_hp, side` |
| `stars` | 115 | `d, stars` |
| `shop` | 113 | `gold, removal_cost, removal_stocked, cards, relics, potions` |
| `rest` | 113 | `option` |
| `event_proceed` | 105 | `event_id, page_index` |
| `buy` | 76 | `kind, cost_current, cost_resource, gold_on_hand` |
| `transform` | 63 | `from_c, from_id, to_c, to_id` |
| `relic_counter` | 62 | `id, n` |
| `map` | 60 | `gen, boss, ancient, nodes` |
| `power_negated` | 58 | `id, n_intended, src, tgt` |
| `enchant` | 46 | `c, id, enchantment, amount` |
| `act` | 39 | `name` |
| `remove` | 38 | `c, id` |
| `header` | 35 | `replay_version, run_schema_version, seed, build_id, build_id_full, mod_version, character, ascension, starting_max_hp, game_mode, modifiers, mods, harmony_owners, full_console, start_time, platform_type, player_count, reloads, starting_deck, starting_relics, relics, potions` |
| `end` | 35 | `terminal_reason, run_time, floors, is_game_over, hp, max_hp, final_deck, final_relics, capture_status` |
| `remap` | 34 | `cards, exact` |
| `hp_loss` | 27 | `dmg, blocked, dmg_type` |
| `map_room` | 17 | (rien de plus que l'enveloppe) |
| `potion_dropped` | 16 | `id` |
| `downgrade` | 16 | `c, id, from_up, up` |
| `resume` | 13 | `reloads, combat_id, wall_clock, run_time, hp, gold, deck_size, relics, potions` |
| `relic_lost` | 3 | `id` |
| `quest` | 2 | `id` |

**253 `combat_start` pour 221 `combat_end`** : 32 combats n'ont pas de fin explicite (mort du
joueur, abandon, ou run reprise). Ils restent exploitables : les événements de combat sont présents.

---

## 4. Modèle d'un combat

### 4.1 Ouverture

```json
{"t":"combat_start","combat_id":"1.2:SHRINKER_BEETLE_WEAK","attempt_id":0,
 "encounter":"SHRINKER_BEETLE_WEAK",
 "enemies":[{"i":0,"cid":1,"id":"SHRINKER_BEETLE","hp":41,"max_hp":41}],"allies":[]}
```

- `combat_id` = `"<acte>.<étage>:<ENCOUNTER>"` ; `attempt_id` distingue les **reprises**
  (deux combats au même étage ont `attempt_id` 0 puis 1).
- `encounter` est l'identifiant du catalogue (`SHRINKER_BEETLE_WEAK`) : c'est la clé de
  `ThreatModel.MaxHit`.
- `enemies[].cid` est l'**identité d'instance** utilisée par tous les événements suivants ;
  `i` est l'index d'affichage.
- `allies` contient les sbires du joueur (`OSTY`), pas les coéquipiers.

### 4.2 Tours

```
turn {n:1, round:1, side:"player"}  … end_turn {n:1, side:"player"}
turn {n:1, round:1, side:"enemy"}   … end_turn {n:1, side:"enemy"}
```

`n == round` sur 2 620/2 620 tours : `n` n'est pas un compteur distinct. `turns == rounds` sur
221/221 `combat_end`. En co-op, il y a **un `turn` joueur par joueur** avec le même `round`.

### 4.3 Cartes jouées

```json
{"t":"play","c":21,"id":"PREPARED","up":0,"auto":false,"play_index":0,"play_count":1,
 "turn":1,"deck_c":17,"cost_paid":0,"cost_value":0,"stars_paid":0,"stars_value":0,
 "result_pile":"discard"}
```

`up` est un **entier** (0, 1, 2, 3 observés) : 0 = non améliorée ; `> 0` = améliorée.
`cost_paid` / `cost_value` distinguent le coût payé du coût affiché (réductions).
`x_value` n'apparaît que sur les cartes X (43 occurrences).
`target` / `target_cid` n'apparaissent que sur les cartes ciblées (94/438 dans un run donné).
`keywords` apparaît quand la carte jouée porte un mot-clé (Exhaust, Retain…).

### 4.4 Dégâts

```json
{"t":"hit","src":"player","dst":"SHRINKER_BEETLE","dmg":4,"blocked":0,"killed":false,
 "card":"STRIKE_DEFECT","dst_cid":1,"dmg_type":["move"],"atk":41,"mods":["STRENGTH_POWER"]}
```

Règles déduites et **vérifiées** (voir §6, cohérence 219/219) :

- `dmg` = **PV réellement perdus par la cible, après blocage**.
- `blocked` = dégâts absorbés par le bloc de la cible.
- `overkill` (optionnel, 34 occurrences) = excédent au-delà des PV restants, présent seulement quand
  la cible meurt d'un coup surdimensionné.
- **Dégâts bruts d'un coup = `dmg + blocked + overkill`.** Exemple : `dmg:3, overkill:15` sur une
  cible à 3 PV pour une attaque de 18.
- ⚠️ `atk` **n'est pas un montant de dégâts** : c'est un identifiant croissant d'attaque (41, 42,
  4903, 4905…). Ne jamais l'additionner.
- `mods` = liste des pouvoirs qui ont modifié ce coup (`STRENGTH_POWER`, `WEAK_POWER`,
  `VULNERABLE_POWER`, `PEN_NIB`, `LETHALITY_POWER`, `INTANGIBLE_POWER`, `SHRINK_POWER`,
  `ACCURACY_POWER`, `DOUBLE_DAMAGE_POWER`, `HARD_TO_KILL_POWER`, `FLUTTER_POWER`, `SLOW_POWER`,
  `CONQUEROR_POWER`, `CALCIFY_POWER`, `PHANTOM_BLADES_POWER`, `COLOSSUS_POWER`, `SURROUNDED_POWER`,
  `TAINTED_POWER`, `VIGOR_POWER`, `TRACKING_POWER`, `STRIKE_DUMMY`, `FAKE_STRIKE_DUMMY`…).
  **Champ absent = aucun modificateur** : c'est la population « brut » de la validation.
- `dmg_type` = **étiquettes non exclusives** (`move`, `unpowered`, `unblockable`, `skiphurtanim`),
  pas une énumération fermée.
- `src` / `dst` valent `"player"` pour **le camp joueur entier**. En co-op, les deux joueurs sont
  fusionnés : `hit` ne porte ni `pid` ni `mine`, donc **la répartition des dégâts par joueur est
  impossible** à partir de `hit`. Seuls `hp`, `pick`, `resume` portent `mine: true/false`.
- `card` n'apparaît que si le coup vient d'une carte (105/640 dans un run, 2 277 coups sur 22
  fichiers) ; les attaques de monstres n'ont pas ce champ.
- Certains `hit` n'ont **pas de `src_cid`** (510/640 dans un run). Pour regrouper les coups d'un
  même move, il faut alors passer par l'événement `move` qui précède, pas par le nom.

### 4.5 Bloc

```json
{"t":"block","src":"player","n":5,"left":5,"reason":"gained","card":"DEFEND_DEFECT"}
```

`reason` ∈ {`gained` (2 385), `cleared` (507), `broken` (408), `prevented` (64), `lost` (56)}.
`n` = quantité de l'événement, `left` = bloc restant après l'opération.

### 4.6 Intentions et moves

```json
{"t":"intent","src":"SHRINKER_BEETLE","src_cid":1,"id":"CHOMP_MOVE","at":"turn_start",
 "intents":["attack"],"dmg":8,"hits":1}
{"t":"move","src":"SHRINKER_BEETLE","src_cid":1,"id":"CHOMP_MOVE","intents":["attack"]}
```

- `at` ∈ {`turn_start` (1 939), `turn_end` (312), `set` (82), `roll` (16)}. **La même intention peut
  être réémise** : au tour 2 de l'exemple Shrinker Beetle, `CHOMP_MOVE` est annoncée à 8 puis
  **réémise à 6** après que le joueur a appliqué Faiblesse. La dernière valeur avant le `move` fait
  foi.
- `dmg` et `hits` ne sont présents que sur les intentions chiffrées (130/178).
- `id` du replay porte le suffixe `_MOVE` (`CHOMP_MOVE`) ; le catalogue expose `CHOMP`. La
  normalisation « retirer `_MOVE` » suffit sur les 22 fichiers.
- `move` **ne porte pas** `dmg` : pour connaître les dégâts d'un move il faut l'`intent` associée ou
  le `hit` qui suit.
- `intents` ∈ {`attack`, `buff`, `debuff`, `defend`, `statuscard`, `stun`, `heal`, `summon`,
  `debuffstrong`, `sleep`, `carddebuff`, `deathblow`, `escape`}.

### 4.7 Points de vie

```json
{"t":"hp","dst":"player","d":"60","hp":60,"src":"heal"}
```

`hp` porte une **valeur absolue** (`hp`) et un delta (`d`, négatif pour une perte, 0 possible en cas
d'overheal). `src` ∈ {`heal`, `loss`, `set`}. Les dégâts de combat **ne produisent pas d'événement
`hp`** : il faut les déduire des `hit` (c'est ce que fait `parse_replays.py`).

### 4.8 Fin de combat

```json
{"t":"combat_end","result":"victory","combat_id":"1.2:…","attempt_id":0,
 "turns":4,"rounds":4,"hp_lost_total":3}
```

`result` ∈ {`victory` (213), `loss` (8)}. `hp_lost_total` = somme des PV perdus sur le combat,
**hors soins** : identique à la somme des `hit.dmg` subis + `hp_loss.dmg` sur **219/219** combats
terminés — c'est le contrôle de cohérence interne du format.

### 4.9 Pendant et hors combat (utilisable pour la review)

- `decision` (852) : `decision_type` (`card_reward`, `map_node`, `relic`, `deck_select_upgrade`,
  `rest`, `shop`, `event`…), `n_presented`, `n_selectable`, `decline_available`, `can_reroll`,
  `options[]` avec `option_kind`, `option_id`, `up`, `instance_id`, `presented`, `selectable`.
  **Toutes les options sont là** — c'est ce que la save `.run` ne donne pas.
- `outcome` (510) : la réponse du joueur (`outcome: "chosen"`, `option_index`, `option_id`).
- `shop` (113) : `gold`, `removal_cost`, `removal_stocked`, et **le prix de chaque article**
  (`cards[].cost`, `sale`, `pool`, `stocked`), idem `relics[]`, `potions[]`.
- `buy` (76) : `kind`, `cost_current`, `cost_resource`, `gold_on_hand`.
- `rest` (113) : `option` (`SMITH`/`HEAL`…), `mimicked`.
- `upgrade` / `remove` / `transform` / `enchant` : l'action exacte sur la carte, avec `decision_id`.
- `map` / `room` / `room_exit` / `act` : carte complète du graphe (`nodes`, `boss`, `ancient`) — ce
  que la save ne contient pas non plus.
- `end` : `terminal_reason`, `final_deck`, `final_relics`, `capture_status`.

---

## 5. Ce qui est validable, et ce qui ne l'est pas

### 5.1 Validable

| Objet | Mesure | Résultat |
|---|---|---|
| **Dégâts d'une carte fixe** (`CardEffects.Damage(id, up)` × coups) | coups **sans modificateur**, plafonnement par les PV déduit | **866 / 956 = 90,6 %** exacts |
| Dégâts d'un **move de monstre** (valeur du catalogue) | attaques sans modificateur, barème d'ascension mesuré | **295 / 389 = 75,8 %** exacts |
| `ThreatModel.MaxHit(rencontre, asc)` comme **majorant** | tour 1 observé ≤ MaxHit | **211 / 246 = 85,8 %** (rencontre) · **231 / 246 = 93,9 %** (instances réelles) |
| `MaxHit` **égal** au pire coup joué | tour 1 = pire move du monstre | **12 / 246 = 4,9 %** |
| PV par tour, dégâts subis par tour | `hp` + `hit` | reconstruits, cohérents avec `hp_lost_total` sur 219/219 combats |
| Prix en shop, options de décision | `shop`, `decision`, `outcome` | présents tels quels |

### 5.2 Non validable (et pourquoi)

| Cas | Volume | Raison |
|---|---|---|
| Cartes à dégâts **dynamiques** (Sovereign Blade, Thrash, Squeeze, Unleash, Ricochet, Bully, Protector, Precise Cut…) | **341** coups | le codex porte `CalculationBase` / `ExtraDamage` / `CalculationExtra` : la valeur dépend de l'état (cartes en main, or, cartes exhaustées) et n'est pas une constante |
| Cartes **absentes** du catalogue | **12** coups (`THE_BALL`, …) | jetons non indexés dans `cards_effects.json` |
| Coups **de sbires** (`src != "player"`, `card` renseigné) | **152** | c'est OSTY qui frappe, pas le joueur : la carte donne l'ordre, pas les dégâts |
| Coups modifiés par un pouvoir **non modélisé** | 425 / 754 | Force/Faiblesse/Vulnérable sont modélisés ; Pen Nib, Lethality, Intangible, Shrink, Accuracy, Double Damage, Hard to Kill, Flutter, Slow, Conqueror ne le sont pas (mais sont **nommés** dans `hit.mods`) |
| **Répartition co-op** des dégâts | 4 runs multi-joueurs | `hit` ne distingue pas les joueurs |
| **Rotation** des moves | — | le NDJSON donne le move joué, pas la règle de choix ; `ThreatModel` a raison de ne pas la prédire |
| **Excédent** de dégâts sur le joueur | 74 attaques | quand le joueur meurt, `overkill` n'est pas journalisé : `dmg` est plafonné aux PV restants |
| `hit_count` du catalogue | **33 / 929** attaques | le nombre de coups réel diffère de `damage.hit_count` (ex. `BOWLBUG_ROCK.HEADBUTT` : 3 coups × 10 en v0.111, 1 × 15 au catalogue) |

---

## 6. Ce que la validation change pour le moteur (à arbitrer, aucun C# modifié)

1. **Le barème d'ascension du catalogue est binaire, le jeu ne l'est pas.** Mesuré sur les
   intentions non modifiées :

   | Ascension | = valeur `normal` | = valeur `ascension` | Barème réel |
   |---|---|---|---|
   | A0 | 56 | 0 | normal |
   | A3 | 1 | 0 | normal |
   | A5 | 50 | 0 | normal |
   | A8 | 58 | 0 | normal |
   | A10 | 9 | 121 | **ascension** |

   `MoveThreat.DamageAt` renvoie la variante ascension dès `ascension > 0`. Sur ces replays, A1..A9
   utilisent encore les valeurs **normales** : `MaxHit` **surestime** donc les dégâts à A1..A9
   (206/389 exact seulement avec la règle actuelle, 295/389 avec le barème mesuré). Le seuil exact
   est entre A9 et A10 ; il faudrait un troisième palier (ou lire le niveau d'ascension nommé du
   codex) pour le représenter.

2. **`MaxHit` ignore le nombre de copies.** 45 des 246 combats contiennent **plus d'exemplaires que
   la rencontre n'en liste** (`EXOSKELETONS_NORMAL` → 4 exosquelettes ; `CORPSE_SLUGS_WEAK` → 2
   limaces). `MaxHit` additionne la liste du catalogue une fois : il **sous-estime** alors le pire
   tour. Le majorant correct se calcule sur les `enemies[].cid` du `combat_start`
   (`instance_max_hit` dans `tools/validate_replays.py`), et c'est ce qui fait passer le taux de
   85,8 % à 93,9 %.

3. **`hit_count` est incomplet** pour 33 attaques sur 929 : un move peut frapper 3 fois alors que le
   catalogue dit 1. Pour un modèle de menace, cela change le pire coup d'un facteur 3.

4. **Le reste des écarts est explicable, pas magique** : 74 attaques sont tronquées par la mort du
   joueur (l'excédent n'est pas écrit), 6 dépassent le catalogue (move retravaillé entre builds,
   ex. `HEADBUTT`).

---

## 7. Repro

```bash
# 1. télécharger (reprise automatique, backoff sur 429)
python3 -I tools/fetch_replays.py --runs-dir data/runs            # 22 fichiers, 10 Mo, ~17 s
python3 -I tools/fetch_replays.py --list                          # inventaire sans réseau

# 2. index + rapport lisible
python3 -I tools/parse_replays.py                                 # data/replays/index.json + report.md

# 3. validation (les 3 contrôles + cohérence interne)
python3 -I tools/validate_replays.py                              # data/replays/validation.{json,md}
python3 -I tools/validate_replays.py --codex data/codex           # + classification des cartes dynamiques
```

`--codex` lit `data/codex/cards.json` (gitignoré, régénérable par `tools/fetch_codex_static.py`)
pour écarter les cartes à dégâts calculés. Sans ce fichier, elles restent dans le dénominateur et le
taux « coups non modifiés » tombe de **90,6 % à 88,6 %** — même mesure, périmètre plus large.

`--engine-truth <dump.json>` compare la réplique Python de `ThreatModel.MaxHit` au moteur C# réel :
**1 827 / 1 827 valeurs identiques** (87 rencontres × 21 ascensions) sur le dump du 2026-10-29.

`data/replays/` est **gitignoré** : ni les NDJSON, ni l'index, ni les rapports ne sont commités.
