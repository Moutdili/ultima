#!/usr/bin/env python3
"""Classe les 17 statuts par EFFET DÉCRIT et fixe leur bande de pénalité.

Pourquoi un fichier à part : les statuts sont des cartes **temporaires** (elles ne restent
pas dans le deck final), donc le corpus de runs ne peut pas les classer (n = 0 à 3). Le
moteur ne doit pourtant pas leur appliquer un forfait aveugle : « subissez 5 dégâts à la
fin du tour » et « injouable, rien d'autre » n'ont pas le même coût.

Sources (aucune valeur inventée) :
- `data/catalog/curse_texts.json` : texte FR, extrait de `pck/localization/fra/cards.json`
  (même source que `curse_effects.json`, la couche effet de main) ;
- `data/catalog/cards_effects.json` : type, mots-clés et `vars` (Damage, HpLoss, Energy,
  MindRotPower, SlothPower, WasteAwayPower…) — c'est là que se résout un placeholder
  `{Damage:diff()}` ; la valeur reste `null` si aucune donnée chiffrée n'existe ;
- `data/catalog/registry.json` : la liste **exhaustive** des statuts (17, dont le
  placeholder `DEPRECATED_CARD`, absent de `cards_effects.json`) ;
- `data/catalog/curse_effects.json` : familles/sévérités de la couche effet, réutilisées
  quand le texte ne permet pas de trancher (et comme garde-fou de cohérence).

Familles demandées (grossières, orientées décision) :
  unplayable | damage_when_drawn | hp_loss | energy_loss | draw_block |
  exhaust_on_draw | other

`effect_family` conserve la famille fine de la couche effet
(`play_limit`, `end_turn_damage`, `draw_reduction`, `energy_loss`, `hp_loss`,
 `escape`, `self_removing`, `unplayable_only`, `unclassified`).

Barème (documenté ici et dans `status/hermes-s27.md`) :
  sévérité ≥ 70  → severe        → −12
  35 ≤ sev < 70  → moderate      → −6
  1 ≤ sev < 35   → benign        → −3
  sévérité nulle / famille `unclassified` → unclassified → −6 explicite

Sortie : `data/catalog/status_effects.json` (commité).
Usage : python3 -I tools/index_status_effects.py
"""
import json
import os
import sys

CARDS = "data/catalog/cards_effects.json"
REGISTRY = "data/catalog/registry.json"
TEXTS = "data/catalog/curse_texts.json"
EFFECTS = "data/catalog/curse_effects.json"
OUT = "data/catalog/status_effects.json"

# Barème unique, partagé avec CurseScorer.StatusSeverePenalty / StatusPenalty / StatusBenignPenalty.
BANDS = {
    "severe": -12,
    "moderate": -6,
    "benign": -3,
    "unclassified": -6,
}
SEVERE_MIN = 70
MODERATE_MIN = 35


def band_of(severity):
    if severity is None or severity <= 0:
        return "unclassified"
    if severity >= SEVERE_MIN:
        return "severe"
    if severity >= MODERATE_MIN:
        return "moderate"
    return "benign"


def var(card, *names):
    """Première `vars` numérique strictement positive, sinon None (jamais deviné)."""
    vars_ = card.get("vars") or {}
    for name in names:
        value = vars_.get(name)
        if isinstance(value, bool):
            continue
        if isinstance(value, (int, float)) and value > 0:
            return int(value), f"vars.{name}"
    return None, None


def classify(card, text, effect_entry):
    """(famille grossière, famille fine, valeur, source de la valeur, degré de preuve)."""
    t = (text or "").lower()
    keywords = {k.lower() for k in (card.get("keywords") or [])}
    digit = None
    digit_src = None

    # 1. Dégâts à la fin du tour / à la pioche. Le texte FR porte le placeholder
    #    `{Damage:diff()}` : la valeur se résout dans `vars` (ou damage_base), pas dans le texte.
    damage, damage_src = var(card, "Damage")
    if damage is None and isinstance(card.get("damage_base"), int) and card["damage_base"] > 0:
        damage, damage_src = card["damage_base"], "damage_base"
    disintegration, disintegration_src = var(card, "DisintegrationPower")
    if ("subissez" in t and "dégâts" in t) or damage is not None or disintegration is not None:
        digit, digit_src = (damage, damage_src) if damage is not None else (disintegration, disintegration_src)
        effect = (effect_entry or {}).get("family")
        return "damage_when_drawn", effect if effect == "end_turn_damage" else "end_turn_damage", digit, digit_src, "text"

    # 2. Perte de PV (Beckon : « perdez 6 PV »).
    hp, hp_src = var(card, "HpLoss")
    if ("perdez" in t and "pv" in t) or hp is not None:
        return "hp_loss", "hp_loss", hp, hp_src, "text" if "pv" in t else "vars"

    # 3. Perte d'énergie (Void : à la pioche ; Waste Away : chaque tour).
    energy, energy_src = var(card, "Energy", "WasteAwayPower")
    if ("énergie" in t or "energy" in t) or energy is not None:
        if ("perdez" in t or "en moins" in t) and energy is not None:
            return "energy_loss", "energy_loss", energy, energy_src, "text"

    # 4. Pioche réduite (Mind Rot).
    draw, draw_src = var(card, "MindRotPower")
    if ("piochez" in t and "en moins" in t) or draw is not None:
        return "draw_block", "draw_reduction", draw, draw_src, "text"

    # 5. Injouable sans autre effet : la carte morte qui prend une place en main.
    #    (Dazed est aussi Éthéré : il disparaît en fin de tour — la modulation par mots-clés
    #    de CurseScorer s'en charge, pas ce classement.)
    if "unplayable" in keywords:
        return "unplayable", "unplayable_only", None, None, "keyword"

    # 6. Aucun statut du jeu n'est « épuisé à la pioche ». Le mot-clé Exhaust de Debris/Slimed
    #    s'applique quand la carte est JOUÉE : elle se retire d'elle-même, effet bénin.
    if "exhaust" in keywords:
        drawn, drawn_src = var(card, "Cards")
        return "other", "self_removing", drawn, drawn_src, "keyword"

    # 7. Effet décrit mais hors des familles précédentes (limite de cartes, fuite, placeholder).
    effect = (effect_entry or {}).get("family")
    if effect in ("play_limit", "escape"):
        value, value_src = (None, None)
        if effect == "play_limit":
            value, value_src = var(card, "SlothPower")
        return "other", effect, value, value_src, "text"

    return "other", "unclassified", None, None, "none"


def main():
    cards_doc = json.load(open(CARDS, encoding="utf-8"))
    cards = {c["id"]: c for c in (cards_doc["cards"] if isinstance(cards_doc, dict) else cards_doc)}
    registry = json.load(open(REGISTRY, encoding="utf-8"))
    status_ids = [e["id"] for e in registry["entities"]["card"] if e.get("type") == "status"]
    texts = json.load(open(TEXTS, encoding="utf-8")).get("texts", {})
    effects = {e["id"]: e for e in json.load(open(EFFECTS, encoding="utf-8"))["entries"] if e.get("type") == "status"}

    entries = []
    for cid in sorted(status_ids):
        card = cards.get(cid, {})
        # `cards_effects.json` ne porte pas de description aujourd'hui ; si une version
        # ultérieure en ajoute une (`description_fr`), elle prime. Sinon le texte FR vient de
        # `curse_texts.json` (même extraction de la localisation du jeu).
        text = card.get("description_fr") or texts.get(cid) or ""
        text_source = "cards_effects.json (description_fr)" if card.get("description_fr") else "curse_texts.json (texte FR)"
        effect_entry = effects.get(cid)
        family, effect_family, value, value_src, proof = classify(card, text, effect_entry)

        # Sévérité : celle de la couche effet si elle est classée ET cohérente avec notre
        # famille fine ; sinon le barème par défaut de la famille fine. `unclassified`
        # reste sans sévérité → −6 explicite.
        entry_family = (effect_entry or {}).get("family")
        severity = (effect_entry or {}).get("severity")
        if effect_family in ("unclassified", None):
            severity = None
        elif entry_family in (None, "unclassified") or entry_family != effect_family:
            severity = {"end_turn_damage": 75, "hp_loss": 75, "energy_loss": 80,
                        "draw_reduction": 85, "play_limit": 95, "escape": 40,
                        "self_removing": 10, "unplayable_only": 35}.get(effect_family)

        band = band_of(severity)
        source_bits = []
        if text:
            source_bits.append(text_source)
        else:
            source_bits.append("aucune description FR")
        if proof == "none":
            source_bits.append("aucune donnée de carte (placeholder)")
        if value is not None:
            source_bits.append(f"cards_effects.json {value_src} = {value}")
        if proof == "keyword":
            source_bits.append("mot-clé " + ", ".join(card.get("keywords") or []))

        entries.append({
            "id": cid,
            "description_fr": text or None,
            "family": family,
            "effect_family": effect_family,
            "value": value,
            "severity": severity,
            "band": band,
            "penalty": BANDS[band],
            "source": " ; ".join(source_bits),
        })

    payload = {
        "schema": "ultima.status_effects/1",
        "source": "curse_texts.json (texte FR, extrait de pck/localization/fra/cards.json) + "
                  "cards_effects.json (valeurs) + registry.json (liste exhaustive) ; "
                  "familles/sévérités alignées sur curse_effects.json",
        "count": len(entries),
        "bands": {
            "severe": {"min_severity": SEVERE_MIN, "penalty": BANDS["severe"]},
            "moderate": {"min_severity": MODERATE_MIN, "penalty": BANDS["moderate"]},
            "benign": {"min_severity": 1, "penalty": BANDS["benign"]},
            "unclassified": {"min_severity": None, "penalty": BANDS["unclassified"],
                             "note": "aucune sévérité décrite → forfait modéré explicite, jamais 0"},
        },
        "entries": entries,
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)
        f.write("\n")

    print(f"{len(entries)} statuts -> {OUT}")
    print(f"{'id':16s} {'famille':18s} {'effet':16s} {'val':>4s} {'sev':>4s} {'bande':12s} pen.")
    for e in entries:
        print(f"{e['id']:16s} {e['family']:18s} {e['effect_family']:16s} "
              f"{str(e['value']):>4s} {str(e['severity']):>4s} {e['band']:12s} {e['penalty']}")
    unknown = [e["id"] for e in entries if e["band"] == "unclassified"]
    print(f"non classés : {unknown or 'aucun'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
