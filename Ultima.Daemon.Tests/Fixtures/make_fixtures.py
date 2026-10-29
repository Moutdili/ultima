#!/usr/bin/env python3
"""Fabrique les fixtures de log RÉELLES (fenêtre de test du 2026-10-29), anonymisées.

Lecture seule des logs du jeu ; écrit seulement dans ce dossier. Lancer avec `python3 -I`.
- SteamID64 -> 7000000000000000N (Mio = ...01, ordre d'apparition ensuite) ;
- /home/<user> -> /home/user ;
- valeurs des tirages de rareté masquées (« probabiliste seulement », docs/20 §8.2) : seul le signal reste.
Sorties :
- godot-solo-2026-10-29.log : log COMPLET de la session solo 13:07 (menu, continue, combat, récompense,
  abandon, marchand, fermeture) ;
- godot-coop-2026-10-29.events.log : lignes d'événements de la session co-op 12:47 (4 joueurs).
"""
import os
import re
import sys
from pathlib import Path

LOGS = Path.home() / ".var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2/logs"
HERE = Path(__file__).resolve().parent
MIO = os.environ.get("STEAM_ID", "")  # ID Steam local à anonymiser (jamais en dur dans le dépôt)
EVENT = re.compile(r"Preloading '[A-Za-z]+ Room'|Creating NCombatRoom|Card rarity: Rolled|selected card reward"
                   r"|Player vote changed|Wrote \d+ bytes to path=.*current_run|Continuing run|Abandoning run|NGame.Quit")

ids: dict[str, str] = {MIO: "70000000000000001"}


def anon(line: str) -> str:
    def sid(m):
        v = m.group(0)
        if v not in ids:
            ids[v] = f"7000000000000000{len(ids) + 1}"
        return ids[v]
    line = re.sub(r"765611\d{11}", sid, line)
    line = re.sub(r"/home/[A-Za-z0-9_.-]+", "/home/user", line)
    line = re.sub(r"Card rarity: Rolled .*", "Card rarity: Rolled <masqué>", line)
    return line


def main() -> int:
    solo = (LOGS / "godot2026-10-29T13.07.16.log").read_text(encoding="utf-8", errors="replace").splitlines()
    (HERE / "godot-solo-2026-10-29.log").write_text("\n".join(anon(l) for l in solo) + "\n", encoding="utf-8")
    coop = (LOGS / "godot2026-10-29T12.47.43.log").read_text(encoding="utf-8", errors="replace").splitlines()
    ev = [anon(l) for l in coop if EVENT.search(l)]
    (HERE / "godot-coop-2026-10-29.events.log").write_text("\n".join(ev) + "\n", encoding="utf-8")
    print(f"solo {len(solo)} lignes, coop {len(ev)} événements, ids {len(ids)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
