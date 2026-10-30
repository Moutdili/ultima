#!/usr/bin/env python3
"""Règle le HUD dans ~/.config/ultima/settings.json, pour la fenêtre de test.

Deux blocs, deux jeux de clés :

  « hud » (contrat v1.3, HudSettings)        : quality, heightFraction, restOpacity, side,
                                               deckScoreInCombat, gradesOnly
  « appearance.hud » (contrat v1.4, docs/44) : theme, fontFamily, fontSize, anchor,
                                               positionSide, width, offsetX, offsetY, railEdge

Le compagnon (pages HUD et Apparence) et le TUI écrivent les mêmes blocs ; ce script se lance depuis
un terminal pendant la fenêtre de test et ne touche QUE ces deux blocs (les autres clés, « modules »…,
sont gardées telles quelles). Écriture atomique (fichier temporaire dans le même dossier + rename).

Bornes : le HUD ramène lui-même les valeurs hors bornes (largeur 280-520, police 12-24, hauteur
0,2-0,85, opacité 0,35-1, décalages bornés à l'écran) ; ce script écrit ce qu'on lui donne.
`side` (v1.3) reste le levier de compatibilité : `positionSide` (v1.4) l'emporte quand il est écrit.

    tools/hud-set.py                                  # affiche les deux blocs
    tools/hud-set.py quality=loupe restOpacity=0.8
    tools/hud-set.py theme=ironclad fontFamily="Fira Sans" fontSize=16
    tools/hud-set.py anchor=bottom width=420 offsetY=-20 railEdge=false
    tools/hud-set.py positionSide=left
    tools/hud-set.py --reset                          # retire le bloc hud
    tools/hud-set.py --reset-appearance               # retire le bloc appearance.hud

Le HUD (extension) relit le fichier sur changement ; le daemon aussi, et republie state.settings.hud.
Ne lit ni n'écrit rien du jeu.
"""
import json
import os
import sys
import tempfile

HUD_FIELDS = {
    "quality": str, "heightFraction": float, "restOpacity": float, "side": str,
    "deckScoreInCombat": bool, "gradesOnly": bool,
}
# clé -> (chemin dans appearance.hud, type)
APP_FIELDS = {
    "theme": (("theme",), str),
    "fontFamily": (("font", "family"), str),
    "fontSize": (("font", "size"), int),
    "anchor": (("position", "anchor"), str),
    "positionSide": (("position", "side"), str),
    "width": (("position", "width"), int),
    "offsetX": (("position", "offsetX"), int),
    "offsetY": (("position", "offsetY"), int),
    "railEdge": (("railEdge",), bool),
}


def path():
    base = os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config")
    return os.path.join(base, "ultima", "settings.json")


def literal(raw, kind):
    if kind is bool:
        if raw not in ("true", "false"):
            sys.exit(f"true ou false attendu, reçu {raw!r}")
        return raw == "true"
    if kind is int:
        try:
            return int(raw)
        except ValueError:
            sys.exit(f"entier attendu, reçu {raw!r}")
    if kind is float:
        try:
            return float(raw)
        except ValueError:
            sys.exit(f"nombre attendu, reçu {raw!r}")
    return raw


def parse(arg):
    key, sep, raw = arg.partition("=")
    if not sep:
        sys.exit(f"argument invalide : {arg!r} (clé=valeur)")
    if key in HUD_FIELDS:
        return "hud", key, literal(raw, HUD_FIELDS[key])
    if key in APP_FIELDS:
        where, kind = APP_FIELDS[key]
        return "appearance", where, literal(raw, kind)
    sys.exit(f"argument inconnu : {key!r} (hud : {', '.join(HUD_FIELDS)} ; "
             f"apparence : {', '.join(APP_FIELDS)})")


def set_path(root, where, value):
    node = root
    for k in where[:-1]:
        child = node.get(k)
        if not isinstance(child, dict):
            child = {}
            node[k] = child
        node = child
    node[where[-1]] = value


def main(argv):
    p = path()
    data: dict = {"modules": []}
    if os.path.exists(p):
        with open(p, encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            sys.exit(f"{p} : racine non objet, rien modifié")
    if not argv:
        print(json.dumps({"hud": data.get("hud"), "appearance": data.get("appearance")},
                         ensure_ascii=False, indent=2))
        return
    if argv == ["--reset"]:
        data.pop("hud", None)
    elif argv == ["--reset-appearance"]:
        data.pop("appearance", None)
    else:
        for arg in argv:
            scope, key, value = parse(arg)
            if scope == "hud":
                hud = data.get("hud") if isinstance(data.get("hud"), dict) else {}
                hud[key] = value
                data["hud"] = hud
            else:
                app = data.get("appearance") if isinstance(data.get("appearance"), dict) else {}
                block = app.get("hud") if isinstance(app.get("hud"), dict) else {}
                set_path(block, key, value)
                app["hud"] = block
                data["appearance"] = app
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
    print(f"{p} : appearance.hud = {json.dumps((data.get('appearance') or {}).get('hud'), ensure_ascii=False)}")


if __name__ == "__main__":
    main(sys.argv[1:])
