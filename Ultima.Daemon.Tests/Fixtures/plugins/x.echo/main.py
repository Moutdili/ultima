#!/usr/bin/env python3
"""Plugin ULTIMA d'exemple (docs/44 §7) — le plus petit plugin correct possible.

Protocole : une ligne JSON sur stdin  {"api":1,"state":{…EngineState…}}
            une ligne JSON sur stdout {"rows":[{"text":…,"tone":…,"grade":…?}]}

Règles suivies : aucune lecture du jeu, aucune écriture, sortie ≤ 20 lignes de ≤ 160 caractères,
UTF-8 explicite (le daemon lit de l'UTF-8, quel que soit le locale du processus).
"""
import json
import sys


def rows(state):
    run = state.get("run") or {}
    gold = run.get("gold")
    text = "or inconnu" if gold is None else f"{gold} or"
    return [
        {"text": f"écran {state.get('screen', '?')}", "tone": "muted"},
        {"text": text, "tone": "accent"},
    ]


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except (AttributeError, ValueError):
        pass
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            message = json.loads(line)
        except json.JSONDecodeError:
            continue  # ligne illisible : on ignore, le daemon comptera un échec
        if not isinstance(message, dict) or message.get("api") != 1:
            continue
        state = message.get("state") or {}
        sys.stdout.write(json.dumps({"rows": rows(state)}, ensure_ascii=False) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
