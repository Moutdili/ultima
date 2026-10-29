#!/usr/bin/env python3
"""Télécharge les données statiques de spire-codex.com dans data/codex/.

Contenu : cartes (valeurs numériques), reliques, potions, monstres (moves/dégâts par ascension),
intents, rencontres, constantes, stats agrégées (encounters, archétypes, communauté).

Ces fichiers sont générés (reproductibles) : data/codex/ est gitignoré.
Usage : python3 -I tools/fetch_codex_static.py
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

BASE = "https://spire-codex.com"
UA = "ultima-research/0.3 (hermes)"
OUT = os.path.join("data", "codex")

ENDPOINTS = {
    "cards": "/api/cards",
    "relics": "/api/relics",
    "potions": "/api/potions",
    "monsters": "/api/monsters",
    "intents": "/api/intents",
    "encounters": "/api/encounters",
    "constants": "/api/mechanics/constants",
    "encounter_stats": "/api/runs/encounter-stats",
    "archetypes": "/api/runs/archetypes",
    "community_stats": "/api/runs/community-stats",
    "charts_meta": "/api/charts/meta",
}


def fetch(path: str, attempts: int = 5):
    delay = 0.5
    for _ in range(attempts):
        req = urllib.request.Request(BASE + path, headers={"User-Agent": UA, "Accept": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            if e.code not in (429, 500, 502, 503, 504):
                raise
            time.sleep(delay)
            delay = min(delay * 2, 8)
    raise RuntimeError(f"échec après {attempts} tentatives : {path}")


def main() -> int:
    os.makedirs(OUT, exist_ok=True)
    total = 0
    for name, path in ENDPOINTS.items():
        data = fetch(path)
        dest = os.path.join(OUT, name + ".json")
        with open(dest, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False)
        size = os.path.getsize(dest)
        total += size
        count = len(data) if isinstance(data, list) else len(data.get("encounters") or data.get("charts") or [])
        print(f"{name:16s} {size/1024:8.1f} Ko  ({count} entrées)" if count else f"{name:16s} {size/1024:8.1f} Ko")
        time.sleep(0.2)
    print(f"total {total/1024/1024:.2f} Mo -> {OUT}/")
    return 0


if __name__ == "__main__":
    sys.exit(main())
