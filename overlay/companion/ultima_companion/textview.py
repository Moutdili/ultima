"""Mise en texte (pure, sans GTK) des pages État et Run. L'UI ne fait qu'afficher ces sections.

Sert à `ultima-companion --check` et à la page Sources (texte). Score absent → « — », jamais 0.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone

from . import ipc
from .settings import OverlaySettings
from .state import (INVALID, MISSING, OK, SUPPORTED_CONTRACT_VERSION, TOO_NEW, Card, Score,
                    StateResult)

DASH = "—"

SCREEN_LABELS = {
    "Unknown": "Inconnu", "Menu": "Menu", "Map": "Carte", "Combat": "Combat",
    "Reward": "Récompense", "Shop": "Boutique", "Event": "Événement",
    "RestSite": "Feu de camp", "Treasure": "Trésor",
}
TYPE_LABELS = {
    "Attack": "Attaque", "Skill": "Compétence", "Power": "Pouvoir",
    "Curse": "Malédiction", "Status": "Statut",
}
RARITY_LABELS = {
    "Basic": "Basique", "Common": "Commune", "Uncommon": "Peu commune", "Rare": "Rare",
    "Curse": "Malédiction", "Special": "Spéciale", "Status": "Statut",
}


@dataclass
class Row:
    title: str
    subtitle: str = ""


@dataclass
class Section:
    title: str
    rows: list[Row] = field(default_factory=list)
    description: str = ""


# ---- briques -------------------------------------------------------------------------------

def fmt_score(score: Score | None) -> str:
    if score is None:
        return DASH
    # Lettre en premier (décision Mio), total et confiance en détail.
    return f"{score.grade} ({score.total}/100, confiance {score.confidence})"


def fmt_terms(score: Score | None) -> str:
    if score is None or not score.terms:
        return ""
    parts = []
    for t in score.terms:
        sign = "+" if t.points >= 0 else ""
        src = f" [{t.source}]" if t.source else ""
        parts.append(f"{t.label} {sign}{t.points}{src}")
    return " ; ".join(parts)


def yes_no(v: bool) -> str:
    return "oui" if v else "non"


def ok_ko(v: bool) -> str:
    return "OK" if v else "KO"


def screen_label(screen: str) -> str:
    label = SCREEN_LABELS.get(screen)
    return f"{label} ({screen})" if label else f"{screen} (écran inconnu de cette version)"


def fmt_at(at: datetime | None, raw: str | None) -> str:
    """Horodatage du dernier état, en heure locale (brut si non analysable)."""
    if at is None:
        return raw or DASH
    if at.tzinfo is None:
        at = at.replace(tzinfo=timezone.utc)
    return at.astimezone().strftime("%d/%m/%Y %H:%M:%S")


def grade_prefix(score: Score | None) -> str:
    """Lettre en tête de ligne : « B · Pendule » ; score absent → « — · Pendule »."""
    return f"{score.grade if score is not None else DASH} · "


def card_title(c: Card) -> str:
    name = c.name
    if c.upgrade == 1:
        name += "+"
    elif c.upgrade > 1:
        name += f"+{c.upgrade}"
    if c.is_curse:
        name = f"[MALÉDICTION] {name}"
    return name


def card_subtitle(c: Card) -> str:
    cost = "injouable" if (c.cost < 0 and c.is_curse) else f"coût {c.cost}"
    parts = [TYPE_LABELS.get(c.type, c.type), RARITY_LABELS.get(c.rarity, c.rarity), cost,
             f"score {fmt_score(c.score)}"]
    line = " · ".join(parts)
    if c.enchantment is not None:
        e = c.enchantment
        amount = f" {e.amount}" if e.amount else ""
        line += f"\nEnchantement : {e.name}{amount} — {e.description or DASH}"
    terms = fmt_terms(c.score)
    if terms:
        line += f"\n{terms}"
    return line


def _card_key(c: Card) -> tuple:
    e = c.enchantment
    return (c.id, c.upgrade, e.id if e else None, e.amount if e else None,
            c.score.total if c.score else None)


def group_cards(cards: list[Card]) -> list[tuple[Card, int]]:
    """Regroupe les copies identiques (ordre de première apparition conservé)."""
    order: list[tuple] = []
    groups: dict[tuple, list] = {}
    for c in cards:
        k = _card_key(c)
        if k not in groups:
            groups[k] = [c, 0]
            order.append(k)
        groups[k][1] += 1
    return [(groups[k][0], groups[k][1]) for k in order]


# ---- bandeau ---------------------------------------------------------------------------------

def banner_message(result: StateResult, daemon: str) -> str | None:
    """Message prioritaire affiché en tête de fenêtre (None = rien à signaler)."""
    if result.kind == MISSING:
        return "Daemon non démarré : aucun état publié."
    if result.kind == TOO_NEW:
        return result.message
    if result.kind == INVALID:
        return f"État du daemon illisible : {result.message}"
    if daemon == ipc.REFUSED or daemon == ipc.NO_SOCKET:
        return "Daemon injoignable (socket absente ou fermée) : l'état affiché est peut-être périmé."
    st = result.state
    if st is not None and st.health.degraded:
        return f"Mode dégradé : {st.health.degraded}"
    return None


# ---- page État ---------------------------------------------------------------------------------

DAEMON_LABELS = {
    ipc.ALIVE: "en marche (socket joignable)",
    ipc.REFUSED: "socket présente mais fermée (daemon arrêté ?)",
    ipc.NO_SOCKET: "pas de socket",
}


def status_sections(result: StateResult, daemon: str, local: OverlaySettings | None) -> list[Section]:
    daemon_rows = [Row("Daemon", DAEMON_LABELS.get(daemon, daemon))]
    if result.kind == MISSING:
        daemon_rows.append(Row("État publié", "Daemon non démarré (aucun state.json)."))
        return [Section("Daemon", daemon_rows)]
    if result.kind == TOO_NEW:
        daemon_rows.append(Row("Version du contrat",
                               f"v{result.contract_version} — non supportée (supportée : "
                               f"v{SUPPORTED_CONTRACT_VERSION}). Mettre à jour ULTIMA."))
        return [Section("Daemon", daemon_rows)]
    if result.kind == INVALID or result.state is None:
        daemon_rows.append(Row("État publié", result.message or "illisible"))
        return [Section("Daemon", daemon_rows)]

    st = result.state
    daemon_rows += [
        Row("Version du contrat", f"v{st.contract_version}.{st.contract_minor} "
                                  f"(supportée : v{SUPPORTED_CONTRACT_VERSION}.x)"),
        Row("Dernier état publié", fmt_at(st.at, st.at_raw)),
    ]
    if local is not None and st.settings is not None:
        same = local.same_applied(st.settings)
        daemon_rows.append(Row("Réglages appliqués par le daemon",
                               "identiques à settings.json" if same
                               else "différents de settings.json (rechargement en attente)"))
    h = st.health
    latency = f"{h.mirror_latency_ms} ms" if h.mirror_latency_ms is not None else DASH
    sources = Section("Sources", [
        Row("Mode dégradé", h.degraded if h.degraded else "aucun"),
        Row("Jeu lancé", yes_no(h.game_running)),
        Row("Save", ok_ko(h.save_ok)),
        Row("Miroir mémoire", ok_ko(h.mirror_ok)),
        Row("Latence du miroir", latency),
    ])
    game = Section("Jeu", [Row("Écran courant", screen_label(st.screen))])
    if st.run is not None:
        r = st.run
        game.rows.append(Row("Run", f"{r.character_name} · acte {r.act_index + 1} · étage {r.floor}"))
    else:
        game.rows.append(Row("Run", "aucune run en cours"))
    return [Section("Daemon", daemon_rows), sources, game]


# ---- page Run ----------------------------------------------------------------------------------

def run_sections(result: StateResult) -> list[Section]:
    if result.kind != OK or result.state is None:
        msg = {MISSING: "Daemon non démarré."}.get(result.kind, result.message or "État indisponible.")
        return [Section("Run", [Row(msg)])]
    st = result.state
    r = st.run
    if r is None:
        return [Section("Run", [Row("Aucune run en cours.")])]

    act = f"{r.act_index + 1}" + (f" ({r.act_id})" if r.act_id else "")
    summary = Section("Résumé", [
        Row("Personnage", r.character_name),
        Row("Acte", act),
        Row("Boss", r.boss_name or r.boss_id or DASH),
        Row("Ascension", str(r.ascension)),
        Row("PV", f"{r.hp}/{r.max_hp}"),
        Row("Or", str(r.gold)),
        Row("Étage", str(r.floor)),
    ])
    sections = [summary]

    ev = st.deck
    note = Section("Note du deck")
    if ev is None:
        note.rows.append(Row("Score", DASH))
    else:
        note.rows.append(Row("Score", fmt_score(ev.score) + (f"\n{fmt_terms(ev.score)}"
                                                           if fmt_terms(ev.score) else "")))
        note.rows.append(Row("Archétype", ev.archetype or DASH))
        note.rows.append(Row("Archétypes potentiels",
                             ", ".join(ev.potential_archetypes) if ev.potential_archetypes else DASH))
    sections.append(note)

    curses = ev.curses if (ev is not None and ev.curses) else [c for c in r.deck if c.is_curse]
    if curses:
        sections.append(Section(f"Malédictions ({len(curses)})",
                                [Row(grade_prefix(c.score) + card_title(c), card_subtitle(c)) for c in curses]))

    deck_rows = []
    for c, n in group_cards(r.deck):
        title = grade_prefix(c.score) + card_title(c) + (f" ×{n}" if n > 1 else "")
        deck_rows.append(Row(title, card_subtitle(c)))
    sections.append(Section(f"Deck ({len(r.deck)} cartes)", deck_rows or [Row("Deck vide")],
                            "Descriptions de cartes : absentes du contrat v1 (seuls les enchantements "
                            "en ont)."))

    relic_rows = []
    for rel in r.relics:
        sub = f"{rel.description or DASH}\nscore {fmt_score(rel.score)}"
        terms = fmt_terms(rel.score)
        if terms:
            sub += f"\n{terms}"
        relic_rows.append(Row(grade_prefix(rel.score) + rel.name, sub))
    sections.append(Section(f"Reliques ({len(r.relics)})", relic_rows or [Row("Aucune relique")]))

    potion_rows = [Row(grade_prefix(p.score) + p.name, f"{p.description or DASH}\nscore {fmt_score(p.score)}") for p in r.potions]
    sections.append(Section(f"Potions ({len(r.potions)})", potion_rows or [Row("Aucune potion")]))
    return sections
