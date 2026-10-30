"""Mise en texte pure (sans Textual) de l'EngineState : utilisée par le TUI, `ultima status` et les tests.

Grammaire partagée avec le HUD (overlay/gnome/ultima@mio/render.js) et la charte (skill
sts2-design-system §9, §11) :
- lettre S-F TOUJOURS écrite, colorée (S gold, A green, B aqua, C blue, D orange, F red, « — » gray) ;
- score null → « — », jamais 0 ; malédiction = « !! » + mot « Malédiction » en violet ;
- enchantement = nom + valeur (« Célérité 1 ») en aqua ; séparateur « · », quantité « 2× », amélioration « + » ;
- au plus UN accent coloré par ligne, texte secondaire en gris clair ;
- l'actionnable d'abord : la décision (récompense, shop, événement, carte) prend la place.

Une ligne = liste de segments (texte, ton, gras). Ton : token de palette, "dim" (secondaire) ou None (crème).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Iterable

from .palette import CURSE_MARK, DASH, SEP, TIMES, grade_tone
from .settings import CONTEXT_SCREEN, MODULE_LABELS, OverlaySettings
from .state import arr, get, num

Seg = tuple[str, "str | None", bool]
Line = list[Seg]

SCREEN_LABELS = {
    "Unknown": "Inconnu", "Menu": "Menu", "Map": "Carte", "Combat": "Combat", "Reward": "Récompense",
    "Shop": "Shop", "Event": "Événement", "RestSite": "Feu de camp", "Treasure": "Trésor",
}
TYPE_LABELS = {"Attack": "Attaque", "Skill": "Compétence", "Power": "Pouvoir", "Curse": "Malédiction",
               "Status": "Statut", "Quest": "Quête"}
KIND_LABELS = {"card": "carte", "relic": "relique", "potion": "potion"}
CONFIDENCE_LABELS = {"high": "haute", "medium": "moyenne", "low": "basse"}
ROOM_LABELS = {"monster": "combat", "elite": "élite", "unknown": "?", "rest_site": "feu", "shop": "shop",
               "treasure": "trésor", "boss": "boss", "ancient": "ancien"}
GRADE_RANK = {"S": 0, "A": 1, "B": 2, "C": 3, "D": 4, "F": 5}
_SCALE = (("S", 85), ("A", 70), ("B", 55), ("C", 40), ("D", 25))


# ---------- briques ----------

def seg(text: str, tone: str | None = None, bold: bool = False) -> Seg:
    return (text, tone, bold)


def plain(line: Line) -> str:
    return "".join(t for t, _, _ in line)


def txt(v: Any, fallback: str = "?", limit: int = 60) -> str:
    if v is None or v == "":
        return fallback
    return " ".join(str(v).split())[:limit]


def grade_from_total(total: Any) -> str | None:
    t = num(total)
    if t is None:
        return None
    for g, lo in _SCALE:
        if t >= lo:
            return g
    return "F"


def grade(score: Any) -> str:
    """Lettre publiée (`score.grade`), sinon dérivée de `total` ; score null → « — »."""
    if not isinstance(score, dict):
        return DASH
    g = score.get("grade")
    if g in GRADE_RANK:
        return g
    return grade_from_total(score.get("total")) or DASH


def grade_seg(score: Any, width: int = 1) -> Seg:
    g = grade(score)
    return seg(g.ljust(width), grade_tone(g), g != DASH)


def rank(score: Any) -> int:
    return GRADE_RANK.get(grade(score), 9)


def top_reason(score: Any) -> str:
    """Raison principale : le terme qui pèse le plus (en valeur absolue)."""
    terms = [t for t in arr(get(score, "terms")) if isinstance(t, dict)]
    if not terms:
        return ""
    best = max(terms, key=lambda t: abs(num(t.get("points")) or 0))
    ev = best.get("evidence")
    return txt(best.get("label"), "") + (f" ({txt(ev, '')})" if ev else "")


def terms_text(score: Any) -> str:
    terms = [t for t in arr(get(score, "terms")) if isinstance(t, dict)]
    parts = []
    for t in terms:
        p = num(t.get("points"))
        sign = DASH if p is None else (f"+{p}" if p > 0 else str(p))
        parts.append(f"{txt(t.get('label'))} {sign}")
    conf = CONFIDENCE_LABELS.get(get(score, "confidence"), get(score, "confidence"))
    tail = f" (total {num(get(score, 'total'))}/100, confiance {conf})" if isinstance(score, dict) else ""
    return SEP.join(parts) + tail


def upgrade_mark(card: Any) -> str:
    u = num(get(card, "upgrade")) or 0
    return "" if u <= 0 else ("+" if u == 1 else f"+{u}")


def card_label(card: Any) -> str:
    return f"{txt(get(card, 'name'))}{upgrade_mark(card)}"


def enchant_text(e: Any) -> str:
    if not isinstance(e, dict):
        return ""
    a = num(e.get("amount"))
    return txt(e.get("name")) if a is None else f"{txt(e.get('name'))} {a}"


def group_cards(cards: Iterable[Any]) -> list[tuple[dict, int]]:
    """Regroupe les cartes identiques (id + amélioration + enchantement) ; malédictions en dernier,
    puis coût croissant (injouable en dernier), nom (même tri que le HUD)."""
    groups: dict[tuple, list] = {}
    for c in cards:
        if not isinstance(c, dict):
            continue
        e = c.get("enchantment") if isinstance(c.get("enchantment"), dict) else {}
        key = (c.get("id"), num(c.get("upgrade")) or 0, e.get("id", ""), num(e.get("amount")))
        if key in groups:
            groups[key][1] += 1
        else:
            groups[key] = [c, 1]

    def cost_key(c: dict) -> int:
        v = num(c.get("cost"))
        return 99 if v is None or v < 0 else v

    return sorted(((c, n) for c, n in groups.values()),
                  key=lambda cn: (cn[0].get("isCurse") is True, cost_key(cn[0]), txt(cn[0].get("name")).lower(),
                                  num(cn[0].get("upgrade")) or 0, 1 if cn[0].get("enchantment") else 0))


def fit(text: str, width: int) -> str:
    if width <= 0:
        return ""
    return text if len(text) <= width else text[: max(0, width - 1)] + "…"


def pad(text: str, width: int) -> str:
    return fit(text, width).ljust(width)


def screen_label(state: Any) -> str:
    s = get(state, "screen", default="Unknown")
    return SCREEN_LABELS.get(s, txt(s))


# ---------- santé ----------

@dataclass
class Health:
    daemon: str            # alive | refused | absent (sonde de la socket)
    pids: list[int]
    state_kind: str        # ok | missing | invalid | too_new
    age_s: float | None
    error: str | None = None


def health_line(state: Any, h: Health) -> Line:
    """Une ligne : jeu · daemon · miroir. Un seul accent : le premier problème (sinon aucun)."""
    hl = get(state, "health", default={})
    parts: list[tuple[str, str | None]] = []
    if h.daemon == "alive":
        parts.append(("daemon ✓", None))
    elif h.pids:
        parts.append((f"daemon démarre (pid {h.pids[0]})", "orange"))
    else:
        parts.append(("daemon arrêté", "red"))
    if state is not None and h.state_kind == "ok":
        parts.append(("jeu ✓" if hl.get("gameRunning") else "jeu fermé", None if hl.get("gameRunning") else "dim"))
        if hl.get("mirrorOk"):
            lat = num(hl.get("mirrorLatencyMs"))
            parts.append((f"miroir ✓ {lat} ms" if lat is not None else "miroir ✓", None))
        else:
            parts.append((f"miroir {DASH}", "dim"))
        if h.age_s is not None:
            parts.append((f"état il y a {_age(h.age_s)}", "dim"))
    line: Line = []
    accent_used = False
    for i, (t, tone) in enumerate(parts):
        if i:
            line.append(seg(SEP, "dim"))
        if tone in ("red", "orange"):
            if accent_used:
                tone = None
            accent_used = True
        line.append(seg(t, "dim" if tone is None else tone))
    return line


def _age(s: float) -> str:
    s = int(s)
    if s < 60:
        return f"{s} s"
    if s < 3600:
        return f"{s // 60} min"
    return f"{s // 3600} h"


# ---------- tableau de bord ----------

def run_line(state: Any) -> Line:
    run = get(state, "run")
    if not isinstance(run, dict):
        return [seg(f"Pas de run en cours{SEP}{screen_label(state)}", "dim")]
    act = num(run.get("actIndex"))
    t = (f"{txt(run.get('characterName'))} A{num(run.get('ascension'))}{SEP}acte {'?' if act is None else act + 1}"
         f"{SEP}étage {num(run.get('floor'))}{SEP}{screen_label(state)}")
    return [seg(t, "dim")]


def degraded_line(state: Any) -> Line | None:
    d = get(state, "health", "degraded")
    return [seg(f"DÉGRADÉ{SEP}{txt(d, limit=200)}", "orange", True)] if d else None


def reward_lines(state: Any, width: int) -> list[Line]:
    r = get(state, "reward")
    if not isinstance(r, dict):
        return [[seg("RÉCOMPENSE", None, True), seg(f"{SEP}non lue", "dim")]]
    head: Line = [seg("RÉCOMPENSE", None, True)]
    if r.get("alreadyTaken"):
        head.append(seg(f"{SEP}déjà prise", "dim"))
    out = [head]
    offered = [c for c in arr(r.get("offered")) if isinstance(c, dict)]
    offered.sort(key=lambda c: rank(c.get("score")))
    name_w = max([len(card_label(c)) + (len(enchant_text(c.get("enchantment"))) + 3 if c.get("enchantment") else 0)
                  for c in offered] + [6])
    name_w = min(name_w, 28)
    for c in offered:
        name = card_label(c) + (f" ({enchant_text(c.get('enchantment'))})" if c.get("enchantment") else "")
        reason = top_reason(c.get("score"))
        out.append([seg("  "), grade_seg(c.get("score")), seg("  " + pad(name, name_w)),
                    seg(fit("  " + reason, max(0, width - name_w - 5)), "dim")])
    out.append([seg("  "), grade_seg(r.get("skipScore")), seg("  " + pad("passer", name_w)),
                seg(fit("  " + top_reason(r.get("skipScore")), max(0, width - name_w - 5)), "dim")])
    return out


def shop_lines(state: Any, width: int) -> list[Line]:
    shop = get(state, "shop")
    gold = num(get(state, "run", "gold"))
    head: Line = [seg("SHOP", None, True)]
    if gold is not None:
        head.append(seg(f"{SEP}{gold} or", "dim"))
    if not isinstance(shop, dict):
        return [head + [seg(f"{SEP}non lu", "dim")]]
    out = [head]
    items = [i for i in arr(shop.get("items")) if isinstance(i, dict)]
    items.sort(key=lambda i: (rank(i.get("score")), not i.get("affordable", True), num(i.get("price")) or 0))
    rows: list[tuple[Any, str, str, str, str]] = []
    for it in items:
        name = card_label(it.get("card")) if isinstance(it.get("card"), dict) else txt(it.get("name"))
        kind = KIND_LABELS.get(it.get("kind"), txt(it.get("kind")))
        price = f"{num(it.get('price'))} or"
        notes = []
        if it.get("onSale"):
            notes.append("soldé")
        if it.get("affordable") is False:
            notes.append("trop cher")
        reason = top_reason(it.get("score"))
        if reason:
            notes.append(reason)
        rows.append((it.get("score"), name, kind, price, SEP.join(notes)))
    if shop.get("removalUsed"):
        rows.append((None, "retrait", "", "utilisé", ""))
    else:
        rows.append((shop.get("removalScore"), "retrait", "", f"{num(shop.get('removalPrice'))} or",
                     top_reason(shop.get("removalScore"))))
    name_w = min(max([len(r[1]) for r in rows] + [6]), 24)
    for score, name, kind, price, notes in rows:
        left = f"  {pad(name, name_w)}  {kind:<7} {price:>7}"
        out.append([seg("  "), grade_seg(score), seg(left),
                    seg(fit(("  " + notes) if notes else "", max(0, width - len(left) - 3)), "dim")])
    return out


def event_lines(state: Any, width: int) -> list[Line]:
    ev = get(state, "event")
    if not isinstance(ev, dict):
        return []
    out: list[Line] = [[seg("ÉVÉNEMENT", None, True), seg(f"{SEP}{txt(ev.get('name'))}", "dim")]]
    for o in arr(ev.get("options")):
        if not isinstance(o, dict):
            continue
        label = txt(o.get("label"))
        note = "indisponible" if o.get("available") is False else top_reason(o.get("score"))
        out.append([seg("  "), grade_seg(o.get("score")), seg("  " + pad(label, 28)),
                    seg(fit("  " + note, max(0, width - 33)), "dim")])
    return out


def map_lines(state: Any, width: int) -> list[Line]:
    out: list[Line] = []
    route = get(state, "route")
    if isinstance(route, dict):
        hp = num(route.get("expectedHpAtBoss"))
        line: Line = [seg("ROUTE", None, True), seg(f"{SEP}PV au boss ~{DASH if hp is None else hp}", "dim")]
        if route.get("mayBeLethal"):
            line.append(seg(f"{SEP}LÉTAL possible", "red", True))
        out.append(line)
    p = get(state, "paths")
    if isinstance(p, dict):
        out.append([seg("CHEMINS", None, True), seg(f"{SEP}{num(p.get('totalPaths'))} jusqu'au boss", "dim")])
        for c in arr(p.get("nextChoices"))[:4]:
            if not isinstance(c, dict):
                continue
            n = num(c.get("pathCount"))
            dmg = c.get("expectedDamage")
            room = ROOM_LABELS.get(str(c.get("roomType")), txt(c.get("roomType")))
            t = f"  col {num(c.get('col'))} {room}{SEP}{n} chemin{'s' if n != 1 else ''}"
            if isinstance(dmg, (int, float)) and not isinstance(dmg, bool):
                t += f"{SEP}dégâts ~{round(dmg)}"
            out.append([seg(fit(t, width))])
    return out


def decision_lines(state: Any, width: int) -> list[Line]:
    s = get(state, "screen")
    if s == "Reward":
        return reward_lines(state, width)
    if s == "Shop":
        return shop_lines(state, width)
    if s == "Event":
        return event_lines(state, width)
    if s == "Map":
        return map_lines(state, width)
    return []


def curse_lines(state: Any, width: int) -> list[Line]:
    deck = arr(get(state, "run", "deck"))
    curses = [c for c in deck if isinstance(c, dict) and c.get("isCurse")]
    out = []
    for c, n in group_cards(curses):
        count = f"{n}{TIMES} " if n > 1 else ""
        out.append([seg(fit(f"{CURSE_MARK} Malédiction{SEP}{count}{card_label(c)}", width), "purple", True)])
    return out


def enchanted_line(state: Any, width: int) -> Line | None:
    deck = arr(get(state, "run", "deck"))
    ench = [c for c in deck if isinstance(c, dict) and isinstance(c.get("enchantment"), dict)]
    if not ench:
        return None
    parts = [f"{card_label(c)} ({enchant_text(c.get('enchantment'))})" + (f" {TIMES}{n}" if n > 1 else "")
             for c, n in group_cards(ench)]
    return [seg("Enchantées  ", "dim"), seg(fit(SEP.join(parts), width - 12), "aqua")]


def summary_line(state: Any) -> Line:
    run = get(state, "run", default={})
    deck = arr(run.get("deck"))
    ench = sum(1 for c in deck if isinstance(c, dict) and c.get("enchantment"))
    curses = sum(1 for c in deck if isinstance(c, dict) and c.get("isCurse"))
    parts = [f"Deck {len(deck)}"]
    if ench:
        parts.append(f"{ench} enchantée{'s' if ench > 1 else ''}")
    if curses:
        parts.append(f"{curses} malédiction{'s' if curses > 1 else ''}")
    parts.append(f"reliques {len(arr(run.get('relics')))}")
    parts.append(f"potions {len(arr(run.get('potions')))}")
    line: Line = [seg(SEP.join(parts), "dim")]
    deck_eval = get(state, "deck")
    line.append(seg(f"{SEP}note du deck ", "dim"))
    line.append(grade_seg(get(deck_eval, "score")))
    arch = get(deck_eval, "archetype")
    if arch:
        line.append(seg(f"{SEP}{txt(arch)}", "dim"))
    return line


def dashboard(state: Any, h: Health, width: int = 80) -> list[Line]:
    """Tableau de bord épuré : dégradé → contexte (1 ligne) → décision → malédictions → enchantées →
    résumé. La santé est dans la barre d'état (health_line), pas ici."""
    if h.state_kind == "missing" or state is None and h.state_kind != "too_new":
        out: list[Line] = [[seg("Aucun état publié.", None, True)]]
        if h.daemon != "alive":
            out.append([seg("Démarrer le daemon : ", "dim"), seg("s", "gold", True),
                        seg(" ici (onglet Contrôles) ou « ultima start ».", "dim")])
        if h.error:
            out.append([seg(h.error, "orange")])
        return out
    if h.state_kind == "too_new":
        return [[seg(f"{h.error or 'Contrat plus récent'} — mettre à jour ULTIMA.", "orange", True)]]
    if h.state_kind == "invalid":
        return [[seg(f"État illisible : {h.error}", "orange")]]
    out = []
    d = degraded_line(state)
    if d:
        out.append(d)
    out.append(run_line(state))
    dec = decision_lines(state, width)
    if dec:
        out.append([])
        out.extend(dec)
    if isinstance(get(state, "run"), dict):
        out.append([])
        out.extend(curse_lines(state, width))
        e = enchanted_line(state, width)
        if e:
            out.append(e)
        out.append(summary_line(state))
    return out


# ---------- listes détaillées (Deck, Reliques, Potions) ----------

def deck_rows(state: Any) -> list[tuple[dict, int]]:
    return group_cards(arr(get(state, "run", "deck")))


def card_detail(card: Any) -> list[Line]:
    if not isinstance(card, dict):
        return []
    out: list[Line] = []
    head: Line = [grade_seg(card.get("score")), seg(f"  {card_label(card)}", None, True)]
    meta = [TYPE_LABELS.get(card.get("type"), txt(card.get("type"), "")), _cost(card)]
    head.append(seg(f"{SEP}{SEP.join(m for m in meta if m)}", "dim"))
    out.append(head)
    if card.get("isCurse"):
        out.append([seg(f"{CURSE_MARK} Malédiction", "purple", True)])
    e = card.get("enchantment")
    if isinstance(e, dict):
        out.append([seg(enchant_text(e), "aqua"), seg(f"{SEP}{txt(e.get('description'), '', 300)}", "dim")])
    if card.get("description"):
        out.append([seg(txt(card.get("description"), "", 400))])
    if isinstance(card.get("score"), dict):
        out.append([seg(terms_text(card.get("score")), "dim")])
    return out


def item_detail(item: Any) -> list[Line]:
    if not isinstance(item, dict):
        return []
    out: list[Line] = [[grade_seg(item.get("score")), seg(f"  {txt(item.get('name'))}", None, True)]]
    if item.get("description"):
        out.append([seg(txt(item.get("description"), "", 400))])
    if isinstance(item.get("score"), dict):
        out.append([seg(terms_text(item.get("score")), "dim")])
    return out


def _cost(card: dict) -> str:
    c = num(card.get("cost"))
    if c is None:
        return ""
    return "injouable" if c < 0 else f"coût {c}"


def cost_text(card: dict) -> str:
    c = num(card.get("cost"))
    return "" if c is None else ("—" if c < 0 else str(c))


# ---------- aperçu texte du HUD (onglet HUD) ----------

def hud_preview(state: Any, settings: OverlaySettings, width: int = 34) -> list[Line]:
    """Aperçu de ce que le panneau affichera : modules activés dans l'ordre réglé, contextuels
    seulement sur leur écran, une ligne de résumé par module (§11)."""
    hud = settings.hud
    screen = get(state, "screen", default="Unknown")
    out: list[Line] = []
    d = degraded_line(state)
    if d:
        out.append([seg(fit(plain(d), width), "orange", True)])
    in_decision = screen in ("Reward", "Shop", "Event")
    for mid in settings.enabled_ids():
        ctx = CONTEXT_SCREEN.get(mid)
        if ctx is not None and ctx != screen:
            continue
        if in_decision and ctx is None and mid != "curses":
            continue  # le moment de décision prend la place (§11)
        if mid == "deck_score" and screen == "Combat" and not hud.deck_score_in_combat:
            continue
        line = _module_summary(state, mid, hud.grades_only, width)
        if line:
            out.append(line)
    if not out:
        out.append([seg("(rien à afficher sur cet écran)", "dim")])
    return out


def _module_summary(state: Any, mid: str, grades_only: bool, width: int) -> Line | None:
    run = get(state, "run", default={})
    deck = arr(run.get("deck"))
    label = MODULE_LABELS.get(mid, mid).upper()
    if mid == "deck":
        return [seg(fit(f"{label}{SEP}{len(deck)} cartes", width))]
    if mid == "enchanted":
        n = sum(1 for c in deck if isinstance(c, dict) and c.get("enchantment"))
        return [seg(fit(f"{label}{SEP}{n}", width), "aqua" if n else "dim")]
    if mid == "curses":
        n = sum(1 for c in deck if isinstance(c, dict) and c.get("isCurse"))
        return [seg(fit(f"{CURSE_MARK} {label}{SEP}{n}", width), "purple", True)] if n else None
    if mid == "relics":
        return [seg(fit(f"{label}{SEP}{len(arr(run.get('relics')))}", width))]
    if mid == "potions":
        return [seg(fit(f"{label}{SEP}{len(arr(run.get('potions')))}", width))]
    if mid in ("reward", "shop", "event"):
        src = {"reward": get(state, "reward", "offered"), "shop": get(state, "shop", "items"),
               "event": get(state, "event", "options")}[mid]
        items = [i for i in arr(src) if isinstance(i, dict)]
        best = min(items, key=lambda i: rank(i.get("score")), default=None)
        if best is None:
            return [seg(f"{label}{SEP}non lu", "dim")]
        name = txt(best.get("name") or best.get("label"))
        line: Line = [seg(f"{label}  "), grade_seg(best.get("score"))]
        if not grades_only:
            line.append(seg(fit(f"  {name}", width - len(label) - 3), "dim"))
        return line
    if mid == "paths":
        t = num(get(state, "paths", "totalPaths"))
        return [seg(f"{label}{SEP}{DASH if t is None else t}")]
    if mid == "route":
        hp = num(get(state, "route", "expectedHpAtBoss"))
        return [seg(f"{label}{SEP}PV boss ~{DASH if hp is None else hp}")] if get(state, "route") else None
    if mid == "party":
        party = arr(run.get("party"))
        return [seg(f"{label}{SEP}{len(party)} joueurs")] if len(party) > 1 else None
    if mid == "deck_score":
        return [seg(f"{label}  "), grade_seg(get(state, "deck", "score"))]
    return None
