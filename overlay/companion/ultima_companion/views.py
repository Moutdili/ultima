"""Modèle de présentation (pur, sans GTK) des pages Tableau de bord et Run.

Règle d'or du design (skill sts2-design-system §10-§11) : « que fait le joueur de cette info dans les
10 prochaines secondes ? ». Chaque ligne = une lettre S-F (toujours écrite) + un nom + UNE raison ;
tout le reste (termes, sources, description, type, coût) va dans `detail`, révélé à la demande.

Même grammaire que le HUD (`overlay/gnome/ultima@mio/render.js`) : « 4× Nom+ », « [Enchantement 1] »,
« — » pour un score absent (jamais 0), regroupement id + amélioration + enchantement, malédictions en
dernier, coût croissant.
"""

from __future__ import annotations

from dataclasses import dataclass, field

from . import ipc
from .palette import character_tone
from .settings import module_label
from .state import (INVALID, MISSING, OK, TOO_NEW, Card, EngineState, Score, ScoreTerm,
                    StateResult)

DASH = "—"

TYPE_LABELS = {
    "Attack": "Attaque", "Skill": "Compétence", "Power": "Pouvoir",
    "Curse": "Malédiction", "Status": "Statut",
}
RARITY_LABELS = {
    "Basic": "Basique", "Common": "Commune", "Uncommon": "Peu commune", "Rare": "Rare",
    "Curse": "Malédiction", "Special": "Spéciale", "Status": "Statut",
}
CONFIDENCE_FR = {"high": "haute", "medium": "moyenne", "low": "basse"}
KIND_FR = {"card": "Carte", "relic": "Relique", "potion": "Potion"}
# Même vocabulaire que le HUD (render.js ROOM_FR).
ROOM_FR = {
    "monster": "combat", "elite": "élite", "unknown": "?", "event": "?", "restsite": "feu",
    "rest": "feu", "shop": "shop", "merchant": "shop", "treasure": "trésor", "boss": "boss",
    "ancient": "ancien",
}


def room_fr(room_type: str | None) -> str:
    key = "".join(ch for ch in (room_type or "").lower() if ch.isalpha())
    return ROOM_FR.get(key, room_type or "?")


# ---- briques -------------------------------------------------------------------------------------

def grade_of(score: Score | None) -> str:
    return score.grade if score is not None else DASH


def signed(n: int | float | None) -> str:
    if n is None:
        return DASH
    v = round(n)
    return f"+{v}" if v > 0 else str(v)


def main_term(score: Score | None) -> ScoreTerm | None:
    """Le terme qui pèse le plus (valeur absolue) : la « raison principale » en une ligne."""
    if score is None or not score.terms:
        return None
    return max(score.terms, key=lambda t: abs(t.points))


def reason_of(score: Score | None) -> str:
    t = main_term(score)
    if t is None:
        return "" if score is not None else "non calculé"
    line = f"{t.label} {signed(t.points)}"
    if t.evidence:
        line += f" · {t.evidence}"
    return line


def score_detail(score: Score | None) -> list[str]:
    """Détail (niveau 2) : total, confiance, ΔG, chaque terme avec sa source et sa preuve."""
    if score is None:
        return ["Note : — (calcul indisponible)"]
    conf = CONFIDENCE_FR.get(score.confidence, score.confidence)
    head = f"Note {score.grade} · {score.total}/100 · confiance {conf}"
    if score.delta_g is not None:
        head += f" · ΔG {score.delta_g:+.1f}"
    lines = [head]
    for t in score.terms:
        line = f"{t.label} {signed(t.points)}"
        if t.source:
            line += f" · {t.source}"
        if t.evidence:
            line += f" · {t.evidence}"
        lines.append(line)
    return lines


def upgrade_mark(upgrade: int) -> str:
    if upgrade <= 0:
        return ""
    return "+" if upgrade == 1 else f"+{upgrade}"


def card_name(c: Card) -> str:
    return f"{c.name}{upgrade_mark(c.upgrade)}"


def enchant_text(c: Card) -> str | None:
    e = c.enchantment
    if e is None:
        return None
    return f"{e.name} {e.amount}" if e.amount else e.name


def card_meta(c: Card) -> str:
    if c.is_curse and c.cost < 0:
        cost = "injouable"
    elif c.cost < 0:
        cost = "coût X"
    else:
        cost = f"coût {c.cost}"
    return " · ".join([TYPE_LABELS.get(c.type, c.type), RARITY_LABELS.get(c.rarity, c.rarity), cost])


# ---- élément affiché -------------------------------------------------------------------------------

@dataclass
class Item:
    """Une ligne : lettre + nom (+ quantité) + une raison ; le reste en détail replié."""
    title: str
    grade: str = DASH
    count: int = 1
    reason: str = ""
    tag: str | None = None          # mot coloré (« Malédiction », « Célérité 1 »)
    tag_tone: str | None = None     # jeton de palette du mot : purple | aqua | red
    value: str | None = None        # valeur à droite, chiffres (« 75 or »)
    detail: list[str] = field(default_factory=list)
    dim: bool = False               # indisponible / trop cher
    best: bool = False              # meilleur choix (focus or)
    tone: str | None = None         # couleur du nom (personnage en co-op), sinon crème

    @property
    def label(self) -> str:
        return f"{self.count}× {self.title}" if self.count > 1 else self.title


def card_item(c: Card, count: int = 1) -> Item:
    detail: list[str] = []
    if c.description:
        detail.append(c.description)
    if c.enchantment is not None and c.enchantment.description:
        detail.append(f"{enchant_text(c)} : {c.enchantment.description}")
    detail.append(card_meta(c))
    detail += score_detail(c.score)
    if c.is_curse:
        tag, tone = "Malédiction", "purple"
    elif c.enchantment is not None:
        tag, tone = enchant_text(c), "aqua"
    else:
        tag, tone = None, None
    return Item(card_name(c), grade_of(c.score), count, reason_of(c.score) if c.score else "",
                tag, tone, None, detail)


def _card_key(c: Card) -> tuple:
    e = c.enchantment
    return (c.id, c.upgrade, e.id if e else "", e.amount if e else "")


def group_cards(cards: list[Card]) -> list[tuple[Card, int]]:
    """Même regroupement et même ordre que le HUD : malédictions en dernier, coût croissant
    (injouable en dernier), nom, amélioration, sans enchantement d'abord."""
    groups: dict[tuple, list] = {}
    for c in cards:
        k = _card_key(c)
        if k in groups:
            groups[k][1] += 1
        else:
            groups[k] = [c, 1]

    def cost_key(c: Card) -> int:
        return 99 if c.cost < 0 else c.cost

    out = [(g[0], g[1]) for g in groups.values()]
    out.sort(key=lambda t: (t[0].is_curse, cost_key(t[0]), t[0].name.casefold(), t[0].upgrade,
                            t[0].enchantment is not None))
    return out


def _mark_best(items: list[Item], scores: list[Score | None]) -> None:
    """Focus or sur le meilleur choix disponible (strictement unique), si au moins un score existe."""
    ranked = [(s.total, i) for i, s in enumerate(scores) if s is not None and not items[i].dim]
    if not ranked:
        return
    top = max(t for t, _ in ranked)
    winners = [i for t, i in ranked if t == top]
    if len(winners) == 1:
        items[winners[0]].best = True


# ---- tableau de bord : la décision en cours ----------------------------------------------------------

@dataclass
class Decision:
    kind: str                 # reward | shop | event | map | idle | offline
    title: str
    subtitle: str = ""
    items: list[Item] = field(default_factory=list)
    empty: str | None = None  # état vide (message calme, une phrase)
    alert: str | None = None  # mot + couleur rouge (« LÉTAL possible »)


SCREEN_FR = {
    "Unknown": "Écran inconnu", "Menu": "Menu", "Map": "Carte", "Combat": "Combat",
    "Reward": "Récompense", "Shop": "Shop", "Event": "Événement", "RestSite": "Feu de camp",
    "Treasure": "Trésor",
}


def screen_fr(screen: str) -> str:
    return SCREEN_FR.get(screen, screen)


def _reward_decision(st: EngineState) -> Decision:
    rw = st.reward
    if rw is None:
        return Decision("reward", module_label("reward"), empty="Récompense non lue par le miroir.")
    items = [card_item(c) for c in rw.offered]
    scores: list[Score | None] = [c.score for c in rw.offered]
    skip = Item("Passer", grade_of(rw.skip_score), reason=reason_of(rw.skip_score),
                detail=score_detail(rw.skip_score))
    items.append(skip)
    scores.append(rw.skip_score)
    _mark_best(items, scores)
    return Decision("reward", module_label("reward"), "déjà prise" if rw.already_taken else "", items)


def _shop_decision(st: EngineState) -> Decision:
    sh = st.shop
    gold = st.run.gold if st.run is not None else None
    title = module_label("shop") + (f" · {gold} or" if gold is not None else "")
    if sh is None:
        return Decision("shop", title, empty="Inventaire non lu par le miroir.")
    rows: list[tuple[Item, Score | None]] = []
    for it in sh.items:
        base = card_item(it.card) if it.card is not None else Item(
            it.name, detail=[KIND_FR.get(it.kind, it.kind)])
        base.grade = grade_of(it.score)
        base.reason = reason_of(it.score) if it.score else ""
        base.detail = [d for d in base.detail if not d.startswith("Note ")] + score_detail(it.score)
        price = DASH if it.price is None else f"{it.price} or"
        base.value = price + (" soldé" if it.on_sale else "")
        base.dim = not it.affordable
        if base.dim:
            base.reason = "trop cher" + (f" · {base.reason}" if base.reason else "")
        rows.append((base, it.score))
    if not sh.removal_used:
        removal = Item("Retrait d'une carte", grade_of(sh.removal_score), reason=reason_of(sh.removal_score),
                       value=DASH if sh.removal_price is None else f"{sh.removal_price} or",
                       detail=score_detail(sh.removal_score))
        if gold is not None and sh.removal_price is not None and gold < sh.removal_price:
            removal.dim = True
            removal.reason = "trop cher" + (f" · {removal.reason}" if removal.reason else "")
        rows.append((removal, sh.removal_score))
    # Ce qui compte dans les 10 s : le meilleur achat possible d'abord, le trop cher en dernier.
    rows.sort(key=lambda r: (r[0].dim, -(r[1].total if r[1] is not None else -1)))
    items = [r[0] for r in rows]
    _mark_best(items, [r[1] for r in rows])
    sub = "retrait déjà utilisé" if sh.removal_used else ""
    return Decision("shop", title, sub, items)


def _event_decision(st: EngineState) -> Decision:
    ev = st.event
    if ev is None:
        return Decision("event", module_label("event"),
                        empty="Options de l'événement non publiées (daemon antérieur à la v1.2).")
    items = []
    for o in ev.options:
        detail = ([o.description] if o.description else []) + score_detail(o.score)
        it = Item(o.label, grade_of(o.score), reason=reason_of(o.score) if o.score else "",
                  detail=detail, dim=not o.available)
        if it.dim:
            it.reason = "indisponible" + (f" · {it.reason}" if it.reason else "")
        items.append(it)
    _mark_best(items, [o.score for o in ev.options])
    return Decision("event", ev.name, "", items,
                    empty=None if items else "Aucune option publiée.")


def route_rooms(st: EngineState) -> list[str]:
    """Route conseillée → suite de salles (« combat › ? › feu › boss »), depuis la carte v1.2."""
    if st.route is None:
        return []
    rooms = []
    for col, row in st.route.path:
        rt = st.map.room_at(col, row) if st.map is not None else None
        if rt is None and st.map is not None and st.map.boss == (col, row):
            rt = "boss"
        rooms.append(room_fr(rt))
    return rooms


def _map_decision(st: EngineState) -> Decision:
    items: list[Item] = []
    alert = None
    sub = ""
    if st.route is not None:
        r = st.route
        rooms = route_rooms(st)
        hp = f"PV au boss ~{r.expected_hp_at_boss}" if r.expected_hp_at_boss is not None else ""
        detail = [f"{t.label} {signed(t.points)}" + (f" · {t.evidence}" if t.evidence else "")
                  for t in r.terms]
        if r.basis:
            detail.append(f"Base : {r.basis}")
        items.append(Item("Route conseillée", DASH, reason=" › ".join(rooms) or DASH,
                          value=hp or None, detail=detail))
        if r.may_be_lethal:
            alert = "LÉTAL possible sur cette route"
    if st.paths is not None:
        p = st.paths
        sub = f"{p.total_paths} chemins jusqu'au boss" if p.total_paths is not None else ""
        for c in sorted(p.next_choices, key=lambda c: c.col):
            n = c.path_count
            dmg = f"dégâts ~{round(c.expected_damage)}" if c.expected_damage is not None else None
            items.append(Item(f"Colonne {c.col} · {room_fr(c.room_type)}", DASH,
                              reason=f"{n} chemin{'s' if n != 1 else ''}", value=dmg))
    if not items:
        return Decision("map", module_label("paths"), empty="Carte non publiée par le daemon.")
    return Decision("map", module_label("route") if st.route is not None else module_label("paths"),
                    sub, items, alert=alert)


def decision(result: StateResult) -> Decision:
    """La seule chose importante maintenant (skill §10) : décision de l'écran courant, sinon calme."""
    if result.kind != OK or result.state is None:
        msg = {MISSING: "Le daemon ne publie rien pour l'instant.",
               TOO_NEW: result.message or "Mettre à jour ULTIMA."}.get(
            result.kind, result.message or "État illisible.")
        return Decision("offline", "En attente", empty=msg)
    st = result.state
    if st.run is None:
        return Decision("idle", "Pas de run", empty="Lance une partie : la décision du moment s'affichera ici.")
    screen = st.screen
    if screen == "Reward":
        return _reward_decision(st)
    if screen == "Shop":
        return _shop_decision(st)
    if screen == "Event":
        return _event_decision(st)
    if screen == "Map":
        return _map_decision(st)
    if screen == "Combat":
        return Decision("idle", "Combat", empty="Rien à décider ici : le HUD suit le combat.")
    return Decision("idle", screen_fr(screen), empty="Aucune décision en cours.")


# ---- lignes de résumé -----------------------------------------------------------------------------------

def deck_summary(st: EngineState) -> str:
    """Une ligne par run (skill §11) : « Deck 14 · 2 enchantées · 1 malédiction · reliques 4 · potions 1 »."""
    r = st.run
    if r is None:
        return ""
    n = len(r.deck)
    ench = sum(1 for c in r.deck if c.enchantment is not None)
    curses = sum(1 for c in r.deck if c.is_curse)
    parts = [f"Deck {n}"]
    if ench:
        parts.append(f"{ench} enchantée{'s' if ench > 1 else ''}")
    if curses:
        parts.append(f"{curses} malédiction{'s' if curses > 1 else ''}")
    parts.append(f"reliques {len(r.relics)}")
    parts.append(f"potions {len(r.potions)}")
    return " · ".join(parts)


def run_title(st: EngineState) -> str:
    r = st.run
    if r is None:
        return "Pas de run"
    parts = [r.character_name, f"Acte {r.act_index + 1}", f"Étage {r.floor}"]
    if r.ascension:
        parts.append(f"A{r.ascension}")
    return " · ".join(parts)


@dataclass
class Health:
    ok: bool
    text: str                 # une ligne
    tone: str                 # jeton : green si tout va bien, orange en mode dégradé, red si rien


def health_line(result: StateResult, daemon: str) -> Health:
    if result.kind == MISSING:
        return Health(False, "Daemon non démarré", "red")
    if result.kind == TOO_NEW:
        return Health(False, "Mettre à jour ULTIMA", "red")
    if result.kind == INVALID or result.state is None:
        return Health(False, "État illisible", "red")
    if daemon != ipc.ALIVE:
        return Health(False, "Daemon injoignable · état peut-être périmé", "orange")
    h = result.state.health
    if h.degraded:
        return Health(False, f"Mode dégradé · {h.degraded}", "orange")
    if not h.game_running:
        return Health(True, "Jeu fermé · daemon prêt", "gray")
    bad = [n for n, ok in (("save", h.save_ok), ("miroir", h.mirror_ok)) if not ok]
    if bad:
        return Health(False, "Source KO : " + ", ".join(bad), "orange")
    lat = f" · miroir {h.mirror_latency_ms} ms" if h.mirror_latency_ms is not None else ""
    return Health(True, "Sources OK" + lat, "green")


# ---- page Run ---------------------------------------------------------------------------------------------

@dataclass
class Group:
    key: str                  # id de module (même ordre/libellés que le HUD)
    title: str
    items: list[Item] = field(default_factory=list)
    empty: str = ""


def run_groups(st: EngineState) -> list[Group]:
    r = st.run
    if r is None:
        return []
    groups: list[Group] = []
    curses = [c for c in r.deck if c.is_curse]
    if curses:
        groups.append(Group("curses", module_label("curses"),
                            [card_item(c, n) for c, n in group_cards(curses)]))
    groups.append(Group("deck", f"{module_label('deck')} · {len(r.deck)}",
                        [card_item(c, n) for c, n in group_cards([c for c in r.deck if not c.is_curse])],
                        "Deck vide"))
    relics = []
    for rel in r.relics:
        detail = ([rel.description] if rel.description else []) + score_detail(rel.score)
        relics.append(Item(rel.name, grade_of(rel.score), reason=reason_of(rel.score) if rel.score else "",
                           detail=detail))
    groups.append(Group("relics", f"{module_label('relics')} · {len(r.relics)}", relics, "Aucune relique"))
    potions = []
    for p in r.potions:
        detail = ([p.description] if p.description else []) + score_detail(p.score)
        potions.append(Item(p.name, grade_of(p.score), reason=reason_of(p.score) if p.score else "",
                            detail=detail))
    groups.append(Group("potions", f"{module_label('potions')} · {len(r.potions)}", potions, "Aucune potion"))
    allies = r.allies
    if allies:
        items = []
        for a in allies:
            detail = [f"Deck {len(a.deck)} · reliques {len(a.relics)}"]
            detail += [f"{n}× {card_name(c)}" if n > 1 else card_name(c) for c, n in group_cards(a.deck)]
            items.append(Item(a.name, DASH, reason=a.character_id.split(".")[-1].capitalize(),
                              value=f"{a.hp}/{a.max_hp} PV", detail=detail,
                              tone=character_tone(a.character_id)))
        groups.append(Group("party", module_label("party"), items))
    return groups


@dataclass
class DeckNote:
    grade: str
    reason: str
    archetype: str
    boss: str
    detail: list[str]


def deck_note(st: EngineState) -> DeckNote | None:
    if st.run is None:
        return None
    ev = st.deck
    score = ev.score if ev is not None else None
    arch = DASH
    if ev is not None:
        arch = ev.archetype or (", ".join(ev.potential_archetypes) if ev.potential_archetypes else DASH)
    boss = st.run.boss_name or st.run.boss_id or DASH
    return DeckNote(grade_of(score), reason_of(score) if score else "non calculé", arch, boss,
                    score_detail(score))
