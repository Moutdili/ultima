#!/usr/bin/env python3
"""Fabrique les fixtures ANONYMISEES de Ultima.Saves a partir d'une COPIE des donnees du jeu.

Usage (toujours sur une copie, jamais sur le dossier du jeu) :
    cp -r ~/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2/steam/<id> /tmp/x/
    cp ~/.var/app/.../SlayTheSpire2/logs/godot*.log /tmp/x/
    python3 -I fixtures/saves/anonymize.py /tmp/x/<id> /tmp/x/godot2026-10-27T21.54.36.log fixtures/saves
  (le dossier source DOIT garder le nom <id> = SteamID64 du compte local)

Transformations (toutes appliquees recursivement a chaque JSON) :
  1. SUPPRESSION des champs interdits (decision de Mio, docs/20 §8.2) : sequences pre-tirees
     des actes, compteurs *_visited associes, sacs de reliques (joueur et partage), etats
     d'aleatoire et cotes (racine et joueur). Les noms exacts sont dans FORBIDDEN ci-dessous.
  2. SUPPRESSION des donnees personnelles / inutiles : seed, map_drawings (dessins du joueur),
     unlock_state, unique_id (progress), listes discovered_* / epochs / ftue (progress).
  3. REMPLACEMENT des SteamID64 (17 chiffres commencant par 7656119) partout (nombres et
     chaines) : le compte local -> 70000000000000001, les autres -> ...002, ...003 dans l'ordre
     d'apparition. Le dossier steam/<id> devient steam/70000000000000001.
  4. Le log : lignes 280-640 de la session choisie, sans aucune ligne contenant un chemin
     /home/, SteamID remplaces. Nom .txt (les *.log sont ignores par git).

Le seul current_run.save disponible vient du profil modded/ (exemple de FORMAT uniquement). Il est
range dans le profil VANILLA de la fixture. Un LEURRE est ecrit dans modded/profile1 avec gold=999
et current_hp=1 : un test verifie que ULTIMA ne le lit jamais.
"""
import json
import os
import re
import shutil
import sys

FORBIDDEN = {
    "normal_encounter_ids", "elite_encounter_ids", "event_ids",
    "normal_encounters_visited", "elite_encounters_visited", "events_visited",
    "boss_encounters_visited",
    "relic_grab_bag", "shared_relic_grab_bag",
    "rng", "odds",
}
PERSONAL = {"seed", "map_drawings", "unlock_state", "unique_id"}
PROGRESS_DROP = {"discovered_acts", "discovered_cards", "discovered_events", "discovered_potions",
                 "discovered_relics", "epochs", "ftue_completed", "unlocked_achievements"}

STEAM_RE = re.compile(r"7656119\d{10}")
FAKE_BASE = 70000000000000000
ids = {}


def fake(real: str) -> str:
    if real not in ids:
        ids[real] = str(FAKE_BASE + len(ids) + 1)
    return ids[real]


def clean(v, drop=frozenset()):
    if isinstance(v, dict):
        out = {}
        for k, x in v.items():
            if k in FORBIDDEN or k in PERSONAL or k in drop or k.endswith("_visited"):
                continue
            out[k] = clean(x, drop)
        return out
    if isinstance(v, list):
        return [clean(x, drop) for x in v]
    if isinstance(v, bool):
        return v
    if isinstance(v, int) and STEAM_RE.fullmatch(str(v)):
        return int(fake(str(v)))
    if isinstance(v, str):
        return STEAM_RE.sub(lambda m: fake(m.group(0)), v)
    return v


def dump(obj, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump(obj, f, indent=2, sort_keys=False)
        f.write("\n")


def pick_history(hist_dir):
    """Selectionne des runs representatives (critere -> premier fichier qui le remplit)."""
    runs = []
    for name in sorted(os.listdir(hist_dir)):
        if name.endswith(".run"):
            with open(os.path.join(hist_dir, name)) as f:
                runs.append((name, json.load(f)))

    def has_potions(d):
        return any(p.get("potions") for p in d["players"])

    def has_upgrade_and_enchant(d):
        deck = d["players"][0]["deck"]
        return any("current_upgrade_level" in c for c in deck) and any("enchantment" in c for c in deck)

    criteria = [
        ("win_solo", lambda d: d["win"] and len(d["players"]) == 1 and d["build_id"] == "v0.107.1"
         and has_upgrade_and_enchant(d)),
        ("death_solo", lambda d: not d["win"] and not d["was_abandoned"] and len(d["players"]) == 1
         and d["killed_by_encounter"] != "NONE.NONE" and has_potions(d)),
        ("abandon_solo", lambda d: d["was_abandoned"] and len(d["players"]) == 1
         and len(d["map_point_history"]) >= 2),
        ("coop4", lambda d: len(d["players"]) == 4),
        ("old_build", lambda d: d["build_id"] == "v0.103.3"),
        ("killed_by_event", lambda d: d["killed_by_event"] not in ("NONE.NONE", "EVENT.NEOW")),
        ("daily_modifiers", lambda d: d["game_mode"] == "daily" or d["modifiers"]),
    ]
    chosen = {}
    used = set()
    for label, pred in criteria:
        for name, d in runs:
            if name not in used and pred(d):
                chosen[label] = (name, d)
                used.add(name)
                break
    return chosen


def main():
    src, log, out = sys.argv[1], sys.argv[2], sys.argv[3]
    real_id = os.path.basename(os.path.normpath(src))
    if not STEAM_RE.fullmatch(real_id):
        sys.exit(f"le dossier source doit s'appeler <SteamID64>, recu {real_id!r}")
    fake_id = fake(real_id)  # premier => 70000000000000001
    acc = os.path.join(out, "root", "steam", fake_id)
    if os.path.exists(os.path.join(out, "root")):
        shutil.rmtree(os.path.join(out, "root"))

    with open(os.path.join(src, "profile.save")) as f:
        dump(clean(json.load(f)), os.path.join(acc, "profile.save"))

    with open(os.path.join(src, "modded", "profile1", "saves", "current_run.save")) as f:
        run = clean(json.load(f))
    dump(run, os.path.join(acc, "profile1", "saves", "current_run.save"))
    decoy = json.loads(json.dumps(run))
    decoy["players"][0]["gold"] = 999
    decoy["players"][0]["current_hp"] = 1
    dump(decoy, os.path.join(acc, "modded", "profile1", "saves", "current_run.save"))

    with open(os.path.join(src, "profile1", "saves", "progress.save")) as f:
        dump(clean(json.load(f), PROGRESS_DROP), os.path.join(acc, "profile1", "saves", "progress.save"))

    chosen = pick_history(os.path.join(src, "profile1", "saves", "history"))
    for label, (name, d) in chosen.items():
        dump(clean(d), os.path.join(acc, "profile1", "saves", "history", f"{label}.run"))
        print(f"history {label}.run <- {name}", file=sys.stderr)

    lines = []
    with open(log, errors="replace") as f:
        for i, line in enumerate(f, 1):
            if 280 <= i <= 640 and "/home/" not in line:
                lines.append(STEAM_RE.sub(lambda m: fake(m.group(0)), line))
    with open(os.path.join(out, "godot.sample.txt"), "w") as f:
        f.writelines(lines)

    print(f"ids remplaces: {len(ids)}", file=sys.stderr)


if __name__ == "__main__":
    main()
