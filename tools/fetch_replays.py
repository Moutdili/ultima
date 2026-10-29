#!/usr/bin/env python3
"""Télécharge les replays de combat NDJSON de spire-codex.com dans data/replays/.

Source : `GET https://spire-codex.com/api/runs/{hash}/replay` (NDJSON, ~470-850 Ko).
Le hash vient des runs locales `data/runs/*/*.run` dont le champ `has_replay` est vrai
(22/273 au 2026-10-29, soit 8,1 %). Un run sans replay répond 404 : c'est normal, il est
noté `missing` dans le manifeste et n'est pas retenté.

`data/replays/` est gitignoré : rien de ce que ce script écrit ne doit être commité.

Usage :
    python3 -I tools/fetch_replays.py                # tout ce qui a has_replay
    python3 -I tools/fetch_replays.py --limit 5      # 5 runs
    python3 -I tools/fetch_replays.py --force        # re-télécharge même si présent
    python3 -I tools/fetch_replays.py --list         # liste les hashes, ne télécharge rien

Reprise : un fichier déjà présent, non vide et dont la première ligne est un `header` valide
est considéré comme acquis et sauté (sauf --force). Le manifeste `_fetch.json` garde le
statut de chaque hash (ok / missing / error), la taille, le nombre de tentatives et la date.
"""
from __future__ import annotations

import argparse
import json
import os
import random
import sys
import time
import urllib.error
import urllib.request

BASE = "https://spire-codex.com"
UA = "ultima-research/0.4 (hermes; replay sampler)"
RUNS_DIR = os.path.join("data", "runs")
OUT_DIR = os.path.join("data", "replays")
MANIFEST = "_fetch.json"

# 429 = quota, 5xx = hoquet serveur : on retente avec backoff. 404 = pas de replay, définitif.
RETRY_STATUS = {429, 500, 502, 503, 504}


def log(msg: str) -> None:
    print(msg, flush=True)


def scan_runs(runs_dir: str) -> list[dict]:
    """Runs locales avec has_replay vrai, triées par hash (ordre stable pour --limit)."""
    found: list[dict] = []
    if not os.path.isdir(runs_dir):
        raise SystemExit(f"répertoire de runs absent : {runs_dir}")
    for sub in sorted(os.listdir(runs_dir)):
        d = os.path.join(runs_dir, sub)
        if not os.path.isdir(d):
            continue
        for name in sorted(os.listdir(d)):
            if not name.endswith(".run"):
                continue
            path = os.path.join(d, name)
            try:
                with open(path, encoding="utf-8") as f:
                    run = json.load(f)
            except (OSError, json.JSONDecodeError) as e:
                log(f"! illisible {path} : {e}")
                continue
            if not run.get("has_replay"):
                continue
            players = run.get("players") or []
            found.append({
                "hash": name[: -len(".run")],
                "build": run.get("build_id"),
                "ascension": run.get("ascension"),
                "seed": run.get("seed"),
                "players": len(players),
                "character": (players[0].get("character") if players and isinstance(players[0], dict)
                              else None),
                "path": path,
            })
    found.sort(key=lambda r: r["hash"])
    return found


def valid_ndjson(path: str) -> bool:
    """Vrai si le fichier commence par un header de replay et contient au moins un événement."""
    try:
        if os.path.getsize(path) < 64:
            return False
        with open(path, encoding="utf-8") as f:
            first = f.readline()
            second = f.readline()
    except OSError:
        return False
    try:
        head = json.loads(first)
    except json.JSONDecodeError:
        return False
    if head.get("t") != "header" or "replay_version" not in head:
        return False
    if not second.strip():
        return False
    try:
        ev = json.loads(second)
    except json.JSONDecodeError:
        return False
    return bool(ev.get("t"))


def download(hash_: str, dest: str, timeout: int, attempts: int) -> tuple[str, int, str | None]:
    """Télécharge un replay. Retourne (statut, octets, détail).

    statut ∈ {ok, missing, error}. Backoff exponentiel + jitter sur 429/5xx, Retry-After
    respecté quand le serveur le donne. Écrit dans un `.part` puis renomme : jamais de
    fichier tronqué pris pour un succès à la reprise.
    """
    url = f"{BASE}/api/runs/{hash_}/replay"
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "application/x-ndjson"})
    delay = 2.0
    last = "?"
    for attempt in range(1, attempts + 1):
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                body = r.read()
            if not body:
                last = "corps vide"
                time.sleep(delay)
                delay = min(delay * 2, 60.0)
                continue
            tmp = dest + ".part"
            with open(tmp, "wb") as f:
                f.write(body)
            if not valid_ndjson(tmp):
                os.unlink(tmp)
                return "error", 0, "NDJSON invalide (pas de header)"
            os.replace(tmp, dest)
            return "ok", len(body), None
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return "missing", 0, "404 (run sans replay)"
            last = f"HTTP {e.code}"
            if e.code not in RETRY_STATUS:
                return "error", 0, last
            retry_after = e.headers.get("Retry-After") if e.headers else None
            wait = delay
            if retry_after:
                try:
                    wait = max(wait, float(retry_after))
                except ValueError:
                    pass
            log(f"  · {last} sur {hash_} — attente {wait:.0f} s ({attempt}/{attempts})")
            time.sleep(wait)
            delay = min(delay * 2, 60.0)
        except (urllib.error.URLError, TimeoutError, ConnectionError) as e:
            last = f"réseau : {e}"
            time.sleep(delay)
            delay = min(delay * 2, 60.0)
    return "error", 0, f"échec après {attempts} tentatives ({last})"


def load_manifest(out_dir: str) -> dict:
    path = os.path.join(out_dir, MANIFEST)
    if not os.path.exists(path):
        return {"generated": None, "runs": {}}
    try:
        with open(path, encoding="utf-8") as f:
            m = json.load(f)
        m.setdefault("runs", {})
        return m
    except (OSError, json.JSONDecodeError):
        return {"generated": None, "runs": {}}


def save_manifest(out_dir: str, manifest: dict) -> None:
    manifest["generated"] = time.strftime("%Y-%m-%dT%H:%M:%S%z")
    path = os.path.join(out_dir, MANIFEST)
    with open(path + ".part", "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1, sort_keys=True)
        f.write("\n")
    os.replace(path + ".part", path)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Télécharge les replays NDJSON des runs locales.")
    ap.add_argument("--runs-dir", default=RUNS_DIR)
    ap.add_argument("--out-dir", default=OUT_DIR)
    ap.add_argument("--limit", type=int, default=0, help="nombre max de runs à traiter (0 = tout)")
    ap.add_argument("--force", action="store_true", help="re-télécharge même si le fichier existe")
    ap.add_argument("--list", action="store_true", help="liste les hashes avec has_replay, sans réseau")
    ap.add_argument("--timeout", type=int, default=120)
    ap.add_argument("--attempts", type=int, default=5)
    ap.add_argument("--delay", type=float, default=0.4, help="pause entre deux runs (s)")
    ap.add_argument("--jitter", type=float, default=0.2)
    args = ap.parse_args(argv)

    runs = scan_runs(args.runs_dir)
    if not runs:
        log("aucun run avec has_replay")
        return 1

    if args.list:
        for r in runs:
            dest = os.path.join(args.out_dir, r["hash"] + ".ndjson")
            state = "présent" if valid_ndjson(dest) else "absent"
            log(f"{r['hash']}  build={r['build']} asc={r['ascension']} "
                f"joueurs={r['players']} perso={r['character']}  {state}")
        log(f"— {len(runs)} runs avec has_replay")
        return 0

    os.makedirs(args.out_dir, exist_ok=True)
    manifest = load_manifest(args.out_dir)

    todo = runs
    if not args.force:
        # La reprise filtre avant --limit : `--limit 5` deux fois de suite télécharge 10 runs.
        todo = [r for r in todo
                if not valid_ndjson(os.path.join(args.out_dir, r["hash"] + ".ndjson"))]
    already = len(runs) - len(todo)
    if args.limit and args.limit > 0:
        todo = todo[: args.limit]

    if not todo:
        log(f"rien à télécharger — {already} replay(s) déjà présent(s) sur {len(runs)}")
        return 0

    done = skipped = missing = errors = 0
    bytes_total = 0
    for i, run in enumerate(todo, 1):
        h = run["hash"]
        dest = os.path.join(args.out_dir, h + ".ndjson")
        if not args.force and valid_ndjson(dest):
            skipped += 1
            bytes_total += os.path.getsize(dest)
            log(f"[{i}/{len(todo)}] {h} déjà présent ({os.path.getsize(dest)/1024:.0f} Ko) — sauté")
            continue
        t0 = time.time()
        status, size, detail = download(h, dest, args.timeout, args.attempts)
        elapsed = time.time() - t0
        manifest["runs"][h] = {
            "status": status,
            "bytes": size,
            "detail": detail,
            "build": run["build"],
            "ascension": run["ascension"],
            "character": run["character"],
            "players": run["players"],
            "at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
            "seconds": round(elapsed, 1),
        }
        save_manifest(args.out_dir, manifest)
        if status == "ok":
            done += 1
            bytes_total += size
            log(f"[{i}/{len(todo)}] {h} ok — {size/1024:.0f} Ko en {elapsed:.1f} s")
        elif status == "missing":
            missing += 1
            log(f"[{i}/{len(todo)}] {h} 404 — pas de replay")
        else:
            errors += 1
            log(f"[{i}/{len(todo)}] {h} ERREUR — {detail}")
        if i < len(todo):
            time.sleep(args.delay + random.random() * args.jitter)

    log("")
    log(f"téléchargés {done} · déjà là {already + skipped} · 404 {missing} · erreurs {errors}")
    log(f"volume traité cette exécution = {bytes_total/1024/1024:.1f} Mo")
    return 0 if errors == 0 else 2


if __name__ == "__main__":
    sys.exit(main())
