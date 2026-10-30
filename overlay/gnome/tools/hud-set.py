#!/usr/bin/env python3
"""Règle les HudSettings (contrat v1.3) dans ~/.config/ultima/settings.json, pour la fenêtre de test.

Le compagnon (page HUD) édite aussi ce bloc ; le TUI `ultima` pas encore. Ce script, lui, se lance
depuis un terminal pendant la fenêtre de test et ne touche QUE le bloc « hud ».
Bornes du compagnon (interface) : hauteur 0,30-0,90, opacité 0,20-1 ; le HUD ramène à 0,2-0,85 et 0,35-1. Écriture atomique (fichier temporaire dans le même dossier + rename), comme le
compagnon ; les autres clés (« modules »…) sont gardées telles quelles. Ne lit ni n'écrit rien du jeu.

    tools/hud-set.py                       # affiche le bloc hud actuel
    tools/hud-set.py quality=loupe         # eco | verre | loupe
    tools/hud-set.py restOpacity=0.8 side=left heightFraction=0.6 gradesOnly=true
    tools/hud-set.py --reset               # retire le bloc hud (défauts du contrat)

Le HUD (extension) relit le fichier sur changement ; le daemon aussi, et republie state.settings.hud.
"""
import json
import os
import sys
import tempfile

FIELDS = {
    "quality": str, "heightFraction": float, "restOpacity": float, "side": str,
    "deckScoreInCombat": bool, "gradesOnly": bool,
}


def path():
    base = os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config")
    return os.path.join(base, "ultima", "settings.json")


def parse(arg):
    key, sep, raw = arg.partition("=")
    if not sep or key not in FIELDS:
        sys.exit(f"argument inconnu : {arg!r} (champs : {', '.join(FIELDS)})")
    kind = FIELDS[key]
    if kind is bool:
        if raw not in ("true", "false"):
            sys.exit(f"{key} : true ou false")
        return key, raw == "true"
    if kind is float:
        try:
            return key, float(raw)
        except ValueError:
            sys.exit(f"{key} : nombre attendu")
    return key, raw


def main(argv):
    p = path()
    data = {"modules": []}
    if os.path.exists(p):
        with open(p, encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            sys.exit(f"{p} : racine non objet, rien modifié")
    if not argv:
        print(json.dumps(data.get("hud"), ensure_ascii=False, indent=2))
        return
    if argv == ["--reset"]:
        data.pop("hud", None)
    else:
        hud = data.get("hud") if isinstance(data.get("hud"), dict) else {}
        hud.update(dict(parse(a) for a in argv))
        data["hud"] = hud
    data.setdefault("modules", [])
    os.makedirs(os.path.dirname(p), exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=".settings.", suffix=".tmp", dir=os.path.dirname(p))
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, p)
    print(f"{p} : hud = {json.dumps(data.get('hud'), ensure_ascii=False)}")


if __name__ == "__main__":
    main(sys.argv[1:])
