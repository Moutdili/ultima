---
name: sts2-save-format
description: Use when reading Slay the Spire 2 state from OUTSIDE the game (external tracker) — save/profile paths, current_run.save JSON schema v16, history .run files, progress.save, godot.log events, write timing, and the forbidden hidden fields.
---

# STS2 — lire l'état depuis les fichiers (tracker externe, lecture seule)

Source normative : `docs/20-pivot-externe.md` §2 et §8. Tout ici a été observé le 2026-10-28.

## Chemins (Flatpak Steam, Fedora)

```
ROOT = ~/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2
ROOT/steam/<steamId64>/profileN/saves/current_run.save   # run en cours (absent hors run)
ROOT/steam/<steamId64>/profileN/saves/progress.save      # stats, déblocages
ROOT/steam/<steamId64>/profileN/saves/history/<start>.run # runs terminées (250 chez Mio)
ROOT/steam/<steamId64>/profile.save                      # profil actif
ROOT/steam/<steamId64>/modded/...                         # PROFIL MODDÉ : à ignorer
ROOT/logs/godot.log                                       # log courant (texte)
ROOT/steam/<steamId64>/settings.save                      # fullscreen, résolution
```
Profil actif : lire `profile.save` (ne jamais deviner). Ne lire QUE `profileN`, jamais `modded/`.

## Règles absolues
1. **Lecture seule.** Jamais d'écriture, de verrou, de renommage dans ROOT. Copier avant d'analyser.
2. Le jeu écrit `X.save` puis `X.save.backup` : lire après stabilisation (inotify `CLOSE_WRITE`
   + relecture si JSON invalide), repli sur `.backup`.
3. **Champs INTERDITS** (décision Mio, « probabiliste seulement ») : `acts[].rooms.normal_encounter_ids`,
   `elite_encounter_ids`, `event_ids`, `*_visited` associés, `players[].relic_grab_bag`,
   `shared_relic_grab_bag`, `rng` (racine et joueur), `players[].odds`, `odds`. Ni lus, ni affichés,
   ni utilisés dans un calcul. Le parser ne doit même pas les désérialiser (DTO sans ces champs).

## current_run.save (JSON clair, `schema_version` 16)
- `players[]` : `character_id` (`CHARACTER.SILENT`), `current_hp`, `max_hp`, `gold`, `max_energy`,
  `max_potion_slot_count`, `deck[] {id: "CARD.X", floor_added_to_deck, (upgrades ?)}`,
  `relics[] {id: "RELIC.X", floor_added_to_deck}`, `net_id`.
- `acts[]` : `id` (`ACT.UNDERDOCKS`), `rooms.boss_id` (`ENCOUNTER.X_BOSS` — visible en jeu, autorisé),
  `saved_map {width 7, height 16, start, boss, points[] {coord{col,row}, type, children[]}}`.
  Types de case : `monster`, `elite`, `unknown` (`?`), `rest_site`, `shop`, `treasure`, `ancient`, `boss`.
- `current_act_index`, `visited_map_coords[] {col,row}` (dernier = position), `ascension`, `game_mode`.
- `map_point_history[][]` : par étage, `player_stats[]` (`card_choices`, `cards_gained`, `damage_taken`,
  `current_hp`, `gold_*`, `relic_choices`…) et `rooms[] {model_id, room_type, turns_taken}`.
Vérifier tout champ non listé ici sur une save réelle avant de l'utiliser.

## Quand le fichier change (log `Wrote N bytes to …current_run.save`)
Entrée de salle et fin de combat. **Les cartes de récompense sont tirées APRÈS l'écriture**
(`Card rarity: Rolled …` puis `Player N selected card reward`) → l'offre n'est jamais dans la save
au moment du choix : elle vient du miroir mémoire (`docs/20` §2).

## history/*.run
Clés : `acts`, `ascension`, `build_id` (`v0.107.1`), `win`, `was_abandoned`, `killed_by_encounter`,
`map_point_history`, `players`, `seed`, `run_time`. Filtrer par `build_id` pour la méta courante.

## godot.log (événements utiles, niveau salle)
`Preloading 'Combat Room'`, `Preloading 'Event Room'`, `Card rarity: Rolled`, `Player N selected card
reward`, `Player vote changed … MapVote … coord: (c, r)`, `Wrote N bytes to …`. Rien sur le combat.
