# Schéma vérifié des fichiers de Slay the Spire 2 (v0.107.1) — lus par `Ultima.Saves`

Observé le 2026-10-28 sur des **copies** des données de Mio (`python3 -I`, dump récursif
type/exemple sur tous les fichiers), recoupé avec le source du jeu (`~/sts2/game/pck/src/Core`).
Colonne **DTO** : ✅ = déclaré et lu par `Ultima.Saves` ; — = volontairement non déclaré
(donc jamais désérialisé) ; ⛔ = **interdit** (décision de Mio, docs/20 §8.2).
Colonne **Preuve** : `obs N` = vu N fois dans les fichiers réels ; `src` = seulement dans le source.

Racine `ROOT = ~/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2`,
compte `ACC = ROOT/steam/<SteamID64>`, profil `P = ACC/profile<N>` (jamais `ACC/modded/…`).

## 1. `ACC/profile.save` — `Core/Saves/ProfileSave.cs`

| Champ | Type | Exemple | DTO | Preuve |
|---|---|---|---|---|
| `last_profile_id` | int | `1` | ✅ (défaut 1, ProfileSave.cs:19) | obs 1 |
| `schema_version` | int | `2` | ✅ | obs 1 |

Note : ce fichier est commun aux profils vanilla et modded ; `Ultima.Saves` en déduit `profile<N>`
**vanilla** uniquement (`UserDataPathProvider.GetProfileDir` ajoute `modded/` quand un mod est chargé).

## 2. `P/saves/current_run.save` (solo) / `current_run_mp.save` (co-op, hôte) — `Core/Saves/SerializableRun.cs`, `schema_version` 16

Fichier disponible : **un seul**, dans `modded/profile1` (exemple de FORMAT ; fixture anonymisée
`root/steam/70000000000000001/profile1/saves/current_run.save`). `current_run_mp.save` : chemin prouvé
par le source (`RunSaveManager.cs:30`), jamais observé.

### Racine
| Champ | Type | Exemple | DTO | Preuve |
|---|---|---|---|---|
| `schema_version` | int | `16` | ✅ | obs 1 |
| `acts` | array<act> | 3 actes | ✅ | obs 1 |
| `current_act_index` | int | `0` | ✅ | obs 1 |
| `players` | array<player> | 1 joueur | ✅ | obs 1 |
| `visited_map_coords` | array<{col,row}> | `[{3,0},{4,1}]` (dernier = salle où se trouve le joueur) | ✅ | obs 1 |
| `map_point_history` | array<array<floor>> | 1 étage (Neow) | ✅ | obs 1 |
| `ascension` | int | `0` | ✅ | obs 1 |
| `game_mode` | string | `"standard"` | ✅ | obs 1 |
| `modifiers` | array<{id}> | `[]` | ✅ | obs 1 |
| `start_time` / `save_time` | int (unix s) | `1793134342` / `1793134358` | ✅ | obs 1 |
| `run_time` / `win_time` | int (s) | `16` / `0` | ✅ | obs 1 |
| `platform_type` | string | `"steam"` | ✅ | obs 1 |
| `num_reloads`, `extra_fields`, `pre_finished_room`, `map_drawings` (base64 gzip) | — | — | — | obs 1 |
| `rng` | object | — | ⛔ | obs 1 |
| `odds` | object | — | ⛔ | obs 1 |
| `shared_relic_grab_bag` | object | — | ⛔ | obs 1 |

### `acts[]` — `Core/Saves/Runs/SerializableActModel.cs`
| Champ | Type | Exemple | DTO | Preuve |
|---|---|---|---|---|
| `id` | string | `"ACT.UNDERDOCKS"` | ✅ (masqué si acte futur) | obs 3 |
| `rooms.boss_id` | string | `"ENCOUNTER.WATERFALL_GIANT_BOSS"` | ✅ (masqué si acte futur) | obs 3 |
| `rooms.second_boss_id` | string\|null | `null` | ✅ (masqué si acte futur) | obs 3 (toujours null) |
| `rooms.ancient_id` | string | `"EVENT.NEOW"` | ✅ (masqué si acte futur) | obs 3 |
| `rooms.normal_encounter_ids`, `rooms.elite_encounter_ids`, `rooms.event_ids` | array | — | ⛔ | obs 3 |
| `rooms.normal_encounters_visited`, `rooms.elite_encounters_visited`, `rooms.events_visited`, `rooms.boss_encounters_visited` | int | — | ⛔ | obs 3 |
| `saved_map` | object | présent pour l'acte courant seulement | ✅ | obs 1/3 |

**Masquage des actes futurs** : la save contient déjà variante, boss et ancien des actes 2 et 3
(ex. `ACT.HIVE` / `ENCOUNTER.THE_INSATIABLE_BOSS`). Le joueur ne les voit pas en jeu → `ActInfo`
les met à `null` quand `index > current_act_index` (`IsRevealed = false`). **À valider par Mio.**

### `acts[].saved_map` — `Core/Saves/Runs/SerializableActMap.cs`, `SerializableMapPoint.cs`
| Champ | Type | Exemple | DTO | Preuve |
|---|---|---|---|---|
| `width` / `height` | int | `7` / `16` | ✅ | obs 1 |
| `points[]` | array<point> | 60 points | ✅ | obs 60 |
| `points[].coord` | {col,row} | `{0,1}` | ✅ | obs 60 |
| `points[].type` | string | `monster` ×26, `unknown` ×12, `rest_site` ×11, `elite` ×5, `treasure` ×3, `shop` ×3 | ✅ | obs 60 |
| `points[].children[]` | array<{col,row}> | `(4,1) → [(4,2)]` ; rangée 15 → `[(3,16)]` | ✅ | obs 72 arcs |
| `points[].can_modify` | bool | `true` | — | obs 49 |
| `start` | point | `{coord (3,0), type "ancient", children [(0,1),(2,1),(4,1),(6,1)]}` | ✅ | obs 1 |
| `boss` | point | `{coord (3,16), type "boss"}` (pas de `children`) | ✅ | obs 1 |
| `second_boss` | point | — | ✅ | src seulement |
| `start_coords` | array<{col,row}> | `[(2,1),(0,1),(4,1),(6,1)]` | ✅ | obs 4 |

Valeurs d'énumération (`Core/Map/MapPointType.cs`, snake_case) : `unassigned, unknown, shop,
treasure, rest_site, monster, elite, boss, ancient`. Les 250 runs n'en contiennent aucune autre.

### `players[]` — `Core/Saves/Runs/SerializablePlayer.cs`
| Champ | Type | Exemple | DTO | Preuve |
|---|---|---|---|---|
| `net_id` | uint64 | `1` (solo) ; SteamID64 en co-op (vu dans les .run) | ✅ | obs 1 |
| `character_id` | string | `"CHARACTER.SILENT"` | ✅ | obs 1 |
| `current_hp` / `max_hp` | int | `70` / `70` | ✅ | obs 1 |
| `gold` | int | `99` | ✅ | obs 1 |
| `max_energy` | int | `3` | ✅ | obs 1 |
| `max_potion_slot_count` | int | `3` | ✅ | obs 1 |
| `base_orb_slot_count` | int | `0` | ✅ | obs 1 |
| `deck[]` | array<card> | 14 cartes | ✅ | obs 14 |
| `relics[]` | array<relic> | `RELIC.RING_OF_THE_SNAKE` (étage 1), `RELIC.KALEIDOSCOPE` | ✅ | obs 2 |
| `potions[]` | array<{id, slot_index}> | **absent** de la save disponible (liste vide omise) ; format vu dans les .run : `{"id":"POTION.FIRE_POTION","slot_index":0}` | ✅ | src (`SerializablePlayer.cs:44`) + obs 203 dans .run |
| `rng`, `odds`, `relic_grab_bag` | object | — | ⛔ | obs 1 |
| `extra_fields`, `unlock_state`, `discovered_*` | — | — | — | obs / src |

### Carte (`deck[]`, choix, gains) — `Core/Saves/Runs/SerializableCard.cs`
| Champ | Type | Exemple | DTO | Preuve |
|---|---|---|---|---|
| `id` | string | `"CARD.STRIKE_SILENT"` | ✅ | obs 14 (save), 7141 (.run) |
| `floor_added_to_deck` | int | `1` | ✅ | obs 14 / 7141 |
| `current_upgrade_level` | int (absent = 0) | `1` | ✅ | **0 dans la save dispo** ; obs 1375 dans les decks .run |
| `enchantment` | {id, amount} | `{"id":"ENCHANTMENT.SOULS_POWER","amount":1}` | ✅ | obs 320 (.run) |
| `props` | object | `{"ints":[{"name":"SpoilsActIndex","value":1}]}` | — | obs 42 (.run) |

### Relique — `Core/Saves/Runs/SerializableRelic.cs`
`id` ✅ (`"RELIC.BURNING_BLOOD"`), `floor_added_to_deck` ✅ (`1`), `props` — (compteurs internes, obs 337).

## 3. `map_point_history[][]` (save ET .run) — `Core/Runs/History/MapPointHistoryEntry.cs`

Étage = index global 1-based à travers les actes (vérifié : sur 4684 étages réels, aucune carte gagnée
n'a `floor_added_to_deck` > index ; 258 égalités, 129 copies d'un étage antérieur).

| Champ | Type | Exemple | DTO | Preuve (250 .run) |
|---|---|---|---|---|
| `map_point_type` | string | `"monster"` | ✅ | obs 4684 |
| `rooms[].room_type` | string | `monster, event, rest_site, elite, shop, treasure, boss` | ✅ | obs 4702 |
| `rooms[].model_id` | string | `"ENCOUNTER.NIBBITS_WEAK"`, `"EVENT.NEOW"` | ✅ | obs 3358 |
| `rooms[].monster_ids[]` | string | `"MONSTER.NIBBIT"` | ✅ | obs 2235 listes |
| `rooms[].turns_taken` | int | `3` | ✅ | obs 4702 |
| `player_stats[].player_id` | uint64 | `1` / SteamID64 | ✅ | obs 6547 |
| `…current_hp`, `max_hp`, `current_gold`, `damage_taken`, `hp_healed`, `max_hp_gained`, `max_hp_lost`, `gold_gained`, `gold_spent`, `gold_lost`, `gold_stolen` | int | `damage_taken` = `13` | ✅ | obs 6547 chacun |
| `…card_choices[] {card, was_picked}` | array | `{card:{id:"CARD.SETUP_STRIKE"}, was_picked:true}` | ✅ | obs 12179 |
| `…cards_gained[]` | array<card> | `CARD.SETUP_STRIKE` | ✅ | obs 4015 |
| `…cards_removed[]` | array<card> | | ✅ | obs 295 |
| `…cards_transformed[] {original_card, final_card}` | array | | ✅ | obs 216 |
| `…cards_enchanted[] {card, enchantment}` | array | `enchantment: "ENCHANTMENT.SHARP"` | ✅ | obs 201 |
| `…upgraded_cards[]`, `downgraded_cards[]` | string | `"CARD.STRIKE_SILENT"` | ✅ | obs 849 / 25 |
| `…relic_choices[] {choice, was_picked}` | array | `RELIC.PHIAL_HOLSTER` | ✅ | obs 3214 |
| `…relics_removed[]` | string | `RELIC.CAPTAINS_WHEEL` | ✅ | obs 20 |
| `…potion_choices[] {choice, was_picked}` | array | `POTION.FIRE_POTION` | ✅ | obs 3026 |
| `…potion_used[]`, `potion_discarded[]` | string | `POTION.SKILL_POTION` | ✅ | obs 1268 / 111 |
| `…rest_site_choices[]` | string | `"HEAL"` | ✅ | obs 1081 |
| `…bought_relics[]`, `bought_potions[]`, `bought_colorless[]` | string | | ✅ | obs 125 / 51 / 89 |
| `…event_choices[].title.key` | string | `"BYRDONIS_NEST.pages.INITIAL.options.EAT.title"` | ✅ (clé seulement) | obs 1846 |
| `…event_choices[].variables` | object | textes localisés | — | obs 714 |
| `…ancient_choice[] {TextKey, title, was_chosen}` | array | `TextKey "KALEIDOSCOPE"` | ✅ | obs 1476 |
| `…completed_quests[]` | string | `CARD.SPOILS_MAP` | ✅ | obs 22 |
| `…stolen_loot` | int | `0` | — | obs 6320 |

## 4. `P/saves/history/<start_time>.run` — `Core/Runs/RunHistory.cs`, `schema_version` 9

250 fichiers (+137 `.run.backup`, ignorés). Toutes les runs : `schema_version` 9 ;
`build_id` v0.107.1 ×247, v0.103.3 ×3 ; 213 solo, 20 à 2, 13 à 3, 4 à 4 joueurs.

| Champ | Type | Exemple | DTO | Preuve |
|---|---|---|---|---|
| `build_id` | string | `"v0.107.1"` | ✅ | obs 250 |
| `win` / `was_abandoned` | bool | 26 victoires ; 142 abandons | ✅ | obs 250 |
| `killed_by_encounter` | string | `"ENCOUNTER.INKLETS_NORMAL"`, `"NONE.NONE"` (→ null). **Un abandon en combat garde la rencontre.** | ✅ | obs 250 |
| `killed_by_event` | string | `"EVENT.SLIPPERY_BRIDGE"`, `"EVENT.NEOW"` (28, abandon à Neow), `"NONE.NONE"` | ✅ | obs 250 |
| `ascension` | int | `0` | ✅ | obs 250 |
| `game_mode` | string | `standard` ×249, `daily` ×1 | ✅ | obs 250 |
| `acts[]` | string | `"ACT.OVERGROWTH"` | ✅ | obs 750 |
| `modifiers[].id` | string | `"MODIFIER.VINTAGE"` | ✅ | obs 3 |
| `start_time`, `run_time` | int | `1780696537`, `413` | ✅ | obs 250 |
| `players[].id` | uint64 | `1` (solo) ; SteamID64 en co-op | ✅ | obs 308 |
| `players[].character` | string | `"CHARACTER.IRONCLAD"` (≠ `character_id` de la save !) | ✅ | obs 308 |
| `players[].deck[]`, `relics[]` | card / relic | | ✅ | obs 7141 / 2238 |
| `players[].potions[] {id, slot_index}` | array | `POTION.FIRE_POTION`, slot `0` | ✅ | obs 203 |
| `players[].max_potion_slot_count` | int | `3` | ✅ | obs 308 |
| `players[].badges[] {id, rarity}` | array | `HEALER` / `bronze` | — | obs 222 |
| `seed` | string | — | — (exclu, retiré des fixtures) | obs 250 |
| `platform_type` | string | `"steam"` | — | obs 250 |

Joueur local : solo → l'unique joueur (`id` 1) ; co-op → `id` == SteamID64 du dossier `steam/<id>`
(vérifié : 250/250 runs ont un joueur local identifiable).

## 5. `P/saves/progress.save` — `Core/Saves/SerializableProgress.cs`, `schema_version` 21

| Champ | Type | Exemple | DTO | Preuve |
|---|---|---|---|---|
| `character_stats[] {id, total_wins, total_losses, max_ascension, preferred_ascension, best_win_streak, current_streak, fastest_win_time, playtime}` | array | `SILENT 14/102, asc 4` | ✅ | obs 6 |
| `character_stats[].badges[]` | array | `{id:"ELITE", count:5, rarity:"bronze"}` | — | obs 44 |
| `encounter_stats[] {encounter_id, fight_stats[{character,wins,losses}]}` | array | `ENCOUNTER.NIBBITS_WEAK` | ✅ | obs 84 |
| `enemy_stats[] {enemy_id, fight_stats[]}` | array | `MONSTER.NIBBIT` | ✅ | obs 109 |
| `card_stats[] {id, times_picked, times_skipped, times_won, times_lost}` | array | `CARD.STRIKE_IRONCLAD 0/0/2/12` | ✅ | obs 489 |
| `ancient_stats[] {ancient_id, character_stats[]}` | array | `EVENT.NEOW` | ✅ | obs 8 |
| `floors_climbed`, `total_playtime` | int | `4684`, `334987` | ✅ | obs 1 |
| `unique_id` | string | — | — (retiré des fixtures) | obs 1 |
| `discovered_*`, `epochs`, `ftue_completed`, `wongo_points`, … | — | — | — | obs 1 |

## 6. Écriture des fichiers par le jeu — `Core/Saves/GodotFileIo.cs`

- `WriteFile` : écrit `X.tmp` puis **renomme** en `X` (l.110-121) → la cible n'est jamais écrite en
  place ; le signal « écriture terminée » est le renommage (inotify `IN_MOVED_TO`).
- `.backup` : copie via `X.backup.tmp` + renommage (l.277-313).
- Fin de run : `current_run.save` et son `.backup` sont supprimés (`RunSaveManager.cs:185`).
- `current_run_mp.save` n'est écrit que par l'hôte (`RunSaveManager.SaveRun`) : un client co-op
  n'a **aucune** save de run locale.

## 7. `ROOT/logs/godot.log` — texte, pas d'horodatage par ligne

Rotation : au démarrage, l'ancien log devient `godot<AAAA-MM-JJTHH.MM.SS>.log` (observé ×4).

| Événement `Ultima.Saves` | Ligne (réelle) | Source |
|---|---|---|
| `RoomEntered` | `[INFO] Preloading 'Combat Room' assets... count=11 vfx=0` (obs : Combat, Event ; src : Treasure, Merchant, RestSite) | `Core/Assets/PreloadManager.cs:101-127`, `AssetLoadingSession.cs:78` |
| `CombatRoomCreated` | `[INFO] Creating NCombatRoom with mode=ActiveCombat encounter=SLIMES_WEAK.` | `Core/Nodes/Rooms/NCombatRoom.cs:174` |
| `CardRewardRolled` | `[INFO] Card rarity: Rolled 0.47728595, need < -0.020000001 for rare (offset = -0.05)` — **valeurs non exposées** | classe de rareté de carte, l.100 |
| `CardRewardSelected` | `[INFO] Player 1 selected card reward` | `Core/Rewards/CardReward.cs:185` |
| `MapVote` | `[INFO] Player vote changed for 1: ->MapVote (gen: 1 coord: (4, 2))` | `Core/Nodes/Screens/Map/NMapScreen.cs:461`, `MapVote.cs:38` |
| `SaveWritten` | `[INFO] Wrote 42463 bytes to path=user://steam/<id>/modded/profile1/saves/current_run.save save_dir=…` (doublon « in steam remote store » ignoré) | `GodotFileIo.cs:121,144` |
| `RunContinued` | `[INFO] Continuing run with character: CHARACTER.IRONCLAD` | `Core/Nodes/Screens/MainMenu/NMainMenu.cs:405` |

Ordre observé (log 21.54.36) : `Wrote …current_run.save` (entrée de salle) → `Preloading 'Combat Room'`
→ `Creating NCombatRoom` → `Wrote …current_run.save` (fin de combat) → `Card rarity: Rolled` ×3 →
`Player 1 selected card reward` → `Player vote changed` → `Wrote …current_run.save` (salle suivante).
Les cartes proposées n'apparaissent donc dans la save qu'APRÈS le choix (au plus tôt à l'écriture suivante).
Lignes vues mais non exploitées : `Player 1 obtained CARD.STONE_ARMOR from card reward`,
`Player 1 obtained 15 gold from reward`.

## 8. Fixtures de ce dossier

`root/` reproduit l'arborescence du jeu (compte factice `70000000000000001`), produite par
`anonymize.py` (voir son en-tête) : `profile.save`, `profile1/saves/{current_run.save, progress.save,
history/*.run}` et un **leurre** `modded/profile1/saves/current_run.save` (gold 999, hp 1).
Runs : `win_solo` (v0.107.1, A1, 48 étages), `death_solo` (boss KNOWLEDGE_DEMON, 3 potions),
`abandon_solo` (abandon en élite), `coop4` (v0.103.3, 4 joueurs, victoire), `old_build` (v0.103.3),
`killed_by_event` (SLIPPERY_BRIDGE), `daily_modifiers` (3 modificateurs).
`godot.sample.txt` : lignes 280-640 du log de session 21.54.36, sans chemin `/home`, ID remplacés.
