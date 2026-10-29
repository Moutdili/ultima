#!/usr/bin/env python3
"""Classe les malédictions/statuts par EFFET décrit (texte FR), pas seulement par win rate.

Pourquoi : Normality (« pas plus de 3 cartes par tour ») mesure +1,8 pp de win rate parce que
l'événement qui la donne offre une contrepartie forte. Le delta est un **bundle**, pas un effet.
Une limite de cartes est pourtant dévastatrice pour un deck de combo : la sévérité d'effet doit
plancherer la toxicité.

Sortie : data/catalog/curse_effects.json (commité).
"""
import json
import os
import re

TEXTS = "data/catalog/curse_texts.json"
CARDS = "data/catalog/cards_effects.json"
OUT = "data/catalog/curse_effects.json"

# Sévérité par famille d'effet (0..100). Argumentée par l'effet, pas par la mesure.
SEVERITY = {
    "play_limit": 95,        # Normality / Sloth : plafonne le nombre de cartes jouées
    "draw_reduction": 85,    # Mind Rot : moins de cartes chaque tour
    "energy_loss": 80,       # Void / Waste Away : énergie perdue
    "end_turn_damage": 75,   # Burn / Infection / Toxic / Wither / Disintegration
    "hp_loss": 75,           # Bad Luck
    "debuff": 55,            # Doubt (Affaibli) / Shame (Fragilisé)
    "forced_order": 50,      # Enthralled : doit être jouée avant les autres
    "gold_loss": 45,         # Debt
    "escape": 40,            # Frantic Escape
    "self_removing": 10,     # Guilty : disparaît après N combats
    "unplayable_only": 35,   # Clumsy & co : encombre la main, sans autre effet
    "unclassified": 50,
}


def family(text, keywords):
    t = (text or "").lower()
    if "ne pouvez pas jouer plus de" in t or "ne pouvez pas jouer plus" in t:
        return "play_limit"
    if "piochez" in t and "en moins" in t:
        return "draw_reduction"
    if ("perdez" in t and ("énergie" in t or "energy" in t)) or ("gagnez" in t and "en moins chaque tour" in t):
        return "energy_loss"
    if "subissez" in t and "dégâts" in t:
        return "end_turn_damage"
    if "perdez" in t and "pv" in t:
        return "hp_loss"
    if "perdez" in t and ("pièces" in t or "or" in t):
        return "gold_loss"
    if "gagnez" in t and ("affaibli" in t or "fragilisé" in t):
        return "debuff"
    if "doit être jouée avant" in t:
        return "forced_order"
    if "retiré" in t and "deck" in t:
        return "self_removing"
    if "éloignez-vous" in t:
        return "escape"
    kw = {k.lower() for k in (keywords or [])}
    if "unplayable" in kw:
        return "unplayable_only"
    return "unclassified"


def extract_value(text):
    """Valeur chiffrée si le texte en contient une (ex. « plus de 3 cartes »)."""
    m = re.search(r"plus de (\d+)", text or "", re.IGNORECASE)
    return int(m.group(1)) if m else None


def main():
    texts = json.load(open(TEXTS, encoding="utf-8"))["texts"]
    cards = json.load(open(CARDS, encoding="utf-8"))
    cards = cards["cards"] if isinstance(cards, dict) else cards

    entries = []
    for c in cards:
        if (c.get("type") or "").lower() not in ("curse", "status"):
            continue
        cid = c["id"]
        text = texts.get(cid, "")
        fam = family(text, c.get("keywords"))
        entries.append({
            "id": cid,
            "type": (c.get("type") or "").lower(),
            "family": fam,
            "severity": SEVERITY[fam],
            "value": extract_value(text),
            "text_fr": text,
            "keywords": c.get("keywords") or [],
            "has_text": bool(text),
        })

    # Les malédictions sans texte mais avec mots-clés gardent une famille explicite.
    for e in entries:
        if not e["has_text"] and e["family"] == "unclassified":
            e["family"] = "unplayable_only" if "Unplayable" in e["keywords"] else "unclassified"
            e["severity"] = SEVERITY[e["family"]]

    entries.sort(key=lambda e: -e["severity"])
    payload = {
        "schema": "ultima.curse_effects/1",
        "source": "pck/localization/fra/cards.json (description) + keywords de cards_effects.json",
        "severity_scale": SEVERITY,
        "entries": entries,
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)

    print(f"{'carte':22s} {'type':7s} {'famille':16s} {'sev':>4s} {'valeur':>7s}  texte")
    for e in entries:
        print(f"{e['id'][:22]:22s} {e['type']:7s} {e['family']:16s} {e['severity']:4d} "
              f"{str(e['value']):>7s}  {'oui' if e['has_text'] else '-'}")


if __name__ == "__main__":
    main()
