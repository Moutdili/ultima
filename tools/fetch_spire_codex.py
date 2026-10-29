#!/usr/bin/env python3
"""Télécharge les runs publiques de spire-codex.com (API /api/runs/list + /api/runs/shared/{hash}).

Le JSON est le format du jeu (mêmes champs que saves/history/*.run) : map_point_history,
player_stats par joueur, card_choices, relic_choices, was_picked. Sert de corpus de backtest.

Usage :
  python3 -I tools/fetch_spire_codex.py --pages 2 --out data/runs      # échantillon
  python3 -I tools/fetch_spire_codex.py --pages 200 --out data/runs    # tout (~10k runs)

Reprise : les fichiers déjà présents sont sautés (clé = run_hash).
"""
import argparse
import concurrent.futures as cf
import json
import os
import random
import sys
import time
import urllib.error
import urllib.request

BASE = "https://spire-codex.com"
UA = "ultima-research/0.2 (hermes; backtest corpus)"


def get(url: str, timeout: int = 30, attempts: int = 6) -> dict:
    """GET avec backoff : le serveur répond 429 en rafale (observé)."""
    delay = 0.5
    last = None
    for i in range(attempts):
        req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            last = e
            if e.code not in (429, 500, 502, 503, 504):
                raise
            retry_after = e.headers.get("Retry-After") if e.headers else None
            wait = float(retry_after) if retry_after and retry_after.isdigit() else delay
            time.sleep(wait + random.random() * 0.3)
            delay = min(delay * 2, 8.0)
        except (urllib.error.URLError, TimeoutError, ConnectionError) as e:
            last = e
            time.sleep(delay + random.random() * 0.3)
            delay = min(delay * 2, 8.0)
    raise last if last else RuntimeError("échec inconnu")


def list_page(page: int) -> list[dict]:
    return get(f"{BASE}/api/runs/list?page={page}").get("runs", [])


def detail(run_hash: str) -> dict | None:
    try:
        return get(f"{BASE}/api/runs/shared/{run_hash}")
    except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError):
        return None


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--pages", type=int, default=1, help="pages de 50 runs à lister")
    ap.add_argument("--out", default="data/runs", help="dossier de sortie")
    ap.add_argument("--workers", type=int, default=8, help="concurrence (le serveur limite en rafale)")
    ap.add_argument("--max-runs", type=int, default=0, help="0 = pas de limite")
    args = ap.parse_args()

    # 1) listing concurrent
    with cf.ThreadPoolExecutor(max_workers=args.workers) as ex:
        pages = list(ex.map(list_page, range(1, args.pages + 1)))

    listing: list[dict] = []
    seen: set[str] = set()
    for runs in pages:
        for r in runs:
            h = r.get("run_hash")
            if h and h not in seen:
                seen.add(h)
                listing.append(r)

    if args.max_runs:
        listing = listing[: args.max_runs]

    # 2) détail concurrent (sauté si déjà téléchargé)
    os.makedirs(args.out, exist_ok=True)
    existing = 0
    todo = []
    for r in listing:
        h = r["run_hash"]
        path = os.path.join(args.out, h[:2], h + ".run")
        if os.path.exists(path):
            existing += 1
            continue
        todo.append((h, path))

    ok = 0
    ko = 0

    def fetch(item):
        h, path = item
        try:
            d = detail(h)
        except Exception:
            d = None
        if d is None:
            return False
        os.makedirs(os.path.dirname(path), exist_ok=True)
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(d, f, ensure_ascii=False)
        os.replace(tmp, path)
        return True

    with cf.ThreadPoolExecutor(max_workers=args.workers) as ex:
        for res in ex.map(fetch, todo):
            ok += int(res)
            ko += int(not res)

    print(f"lister : {len(listing)} runs uniques ({args.pages} page(s))")
    print(f"déjà là: {existing} | téléchargés: {ok} | erreurs: {ko} -> {args.out}")
    return 0 if ko == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
