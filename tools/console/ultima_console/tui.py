"""TUI plein écran de `ultima` (Textual).

Choix de la pile : Python + Textual (venv ~/.local/share/ultima/venv) — même langage que le compagnon
(overlay/companion, Python), aucune dépendance système, pilote de test sans TTY (`App.run_test`) qui
permet des captures texte reproductibles. La CLI reste en bibliothèque standard : `ultima status` marche
même sans le venv.

Onglets : 1 État · 2 Deck · 3 Reliques · 4 HUD · 5 Journal · 6 Contrôles ; « ? » aide ; « q » quitter.
Grammaire visuelle commune au HUD et au compagnon (textview.py) ; un accent coloré par ligne.
"""

from __future__ import annotations

import io
import socket
import threading
import time
from collections import deque
from pathlib import Path

from rich.console import Console
from rich.text import Text
from textual import on, work
from textual.app import App, ComposeResult
from textual.binding import Binding
from textual.containers import Horizontal, Vertical, VerticalScroll
from textual.screen import ModalScreen
from textual.theme import Theme
from textual.widgets import (DataTable, Footer, Input, OptionList, RichLog, Static, TabbedContent,
                             TabPane)
from textual.widgets.option_list import Option

from . import banner, consolecfg, doctor, logs, paths, procs, settings, textview
from .palette import CURSE_MARK, DASH, PALETTE, SEP, TIMES, grade_tone
from .settings import SIDE_LABELS, OverlaySettings
from .state import ALIVE, ABSENT, OK, REFUSED, StateResult, get, load as load_state, parse as parse_state

ULTIMA_THEME = Theme(
    name="ultima",
    primary=PALETTE["gold"], secondary=PALETTE["aqua"], accent=PALETTE["gold"],
    warning=PALETTE["orange"], error=PALETTE["red"], success=PALETTE["green"],
    foreground=PALETTE["cream"], background=PALETTE["glass"], dark=True,
)


# ---------- rendu des lignes ----------

def to_text(lines: list[textview.Line]) -> Text:
    out = Text()
    for i, line in enumerate(lines):
        if i:
            out.append("\n")
        for t, tone, bold in line:
            if tone == "dim":
                out.append(t, style="dim")
            elif tone is None:
                out.append(t, style="bold" if bold else "")
            else:
                out.append(t, style=f"{'bold ' if bold else ''}{PALETTE.get(tone, tone)}")
    return out


def grade_cell(score) -> Text:
    g = textview.grade(score)
    return Text(g, style=f"{'bold ' if g != DASH else ''}{PALETTE[grade_tone(g)]}", justify="center")


def screen_text(app: App) -> str:
    """Capture TEXTE de l'écran courant (mêmes données que App.export_screenshot, sans SVG)."""
    width, height = app.size
    console = Console(width=width, height=height, file=io.StringIO(), force_terminal=True,
                      color_system="truecolor", record=True, legacy_windows=False, safe_box=False)
    console.print(app.screen._compositor.render_update(full=True, screen_stack=app._background_screens))
    return "\n".join(line.rstrip() for line in console.export_text().splitlines())


# ---------- tables éditables ----------

class KeyTable(DataTable):
    """DataTable en mode ligne dont ←/→/espace modifient la valeur (au lieu de déplacer le curseur)."""
    BINDINGS = [
        Binding("left", "change(-1)", "−", show=False),
        Binding("right", "change(1)", "+", show=False),
        Binding("space", "change(1)", "basculer", show=False),
    ]

    def __init__(self, on_change, **kw):
        super().__init__(cursor_type="row", zebra_stripes=False, **kw)
        self._on_change = on_change

    def action_change(self, step: int) -> None:
        if self.row_count:
            self._on_change(self, self.cursor_row, step)


class ModuleTable(KeyTable):
    BINDINGS = [
        Binding("left_square_bracket,shift+up", "move(-1)", "monter", show=False),
        Binding("right_square_bracket,shift+down", "move(1)", "descendre", show=False),
    ]

    def __init__(self, on_change, on_move, **kw):
        super().__init__(on_change, **kw)
        self._on_move = on_move

    def action_move(self, step: int) -> None:
        if self.row_count:
            self._on_move(self, self.cursor_row, step)


# ---------- fenêtres modales ----------

HELP = """\
[b]Navigation[/b]  1-6 onglets · Tab champ suivant · ↑/↓ sélection · q quitter · ? cette aide

[b]1 État[/b]       la décision en cours d'abord (lettre S-F + raison), puis malédictions et résumé
[b]2 Deck[/b]       ↑/↓ carte → description FR et détail du score en bas
[b]3 Reliques[/b]   reliques puis potions (Tab) ; détail en bas
[b]4 HUD[/b]        ←/→ changer · espace activer/désactiver · [ ] ou Maj+↑/↓ déplacer un module
             d valeurs par défaut · p aperçu — enregistré aussitôt (écriture atomique)
[b]5 Journal[/b]    a tout · i info+ · w alertes · / chercher · Échap effacer · c couleurs · espace pause
[b]6 Contrôles[/b]  Entrée exécute l'action (démarrer/arrêter le daemon, HUD, vérifier)
[b]Partout[/b]      s démarrer le daemon

Lettres : [b #EFC851]S[/] [b #7FFF00]A[/] [b #2AEBBE]B[/] [b #87CEEB]C[/] [b #FFA518]D[/] [b #FF5555]F[/] · [#808080]—[/] non calculé (jamais 0)
"""


class HelpScreen(ModalScreen):
    BINDINGS = [Binding("escape,q,question_mark", "dismiss", "fermer")]
    DEFAULT_CSS = """
    HelpScreen { align: center middle; }
    HelpScreen > Static { width: 96; max-width: 100%; height: auto; padding: 1 2;
                          background: $surface; border: round $primary 40%; }
    """

    def compose(self) -> ComposeResult:
        yield Static(HELP + "\n[dim]Échap pour fermer[/dim]")


class ColorsScreen(ModalScreen):
    """Couleurs du journal + thème (console.json, enregistré aussitôt)."""
    BINDINGS = [Binding("escape,q,c", "dismiss", "fermer")]
    DEFAULT_CSS = """
    ColorsScreen { align: center middle; }
    ColorsScreen > Vertical { width: 56; height: auto; padding: 1 2; background: $surface;
                              border: round $primary 40%; }
    ColorsScreen KeyTable { height: auto; }
    """
    ROWS = (("theme", "Thème"), ("timestamp", "Heure"), ("debug", "debug"), ("info", "info"),
            ("warn", "warn"), ("error", "error"))

    def __init__(self, cfg: consolecfg.ConsoleConfig, on_saved):
        super().__init__()
        self.cfg = cfg
        self.on_saved = on_saved

    def compose(self) -> ComposeResult:
        with Vertical():
            yield Static("[b]Couleurs du journal[/b]  [dim]←/→ changer · Échap fermer[/dim]")
            t = KeyTable(self._change, id="colors")
            yield t

    def on_mount(self) -> None:
        t = self.query_one("#colors", KeyTable)
        t.add_columns("Élément", "Valeur")
        self._fill()
        t.focus()

    def _fill(self) -> None:
        t = self.query_one("#colors", KeyTable)
        row = t.cursor_row
        t.clear()
        for key, label in self.ROWS:
            if key == "theme":
                t.add_row(label, self.cfg.theme)
            else:
                tok = self.cfg.log_colors.get(key, "")
                t.add_row(label, Text(f"■ {tok}", style=self.cfg.color(key)))
        t.move_cursor(row=min(row, t.row_count - 1))

    def _change(self, table, row: int, step: int) -> None:
        key = self.ROWS[row][0]
        if key == "theme":
            names = list(consolecfg.THEMES)
            i = names.index(self.cfg.theme) if self.cfg.theme in names else -1
            self.cfg.theme = names[(i + step) % len(names)]
            self.app.theme = self.cfg.theme
        else:
            self.cfg.cycle_color(key, step)
        consolecfg.save(self.cfg)
        self._fill()
        self.on_saved()


# ---------- application ----------

class UltimaApp(App):
    TITLE = "ULTIMA"
    CSS = """
    Screen { background: $background; }
    TabbedContent { height: 1fr; }
    TabPane { padding: 0 1; }
    #statusbar { height: 1; padding: 0 1; color: $text-muted; }
    #dash-body { padding: 1 0 0 0; }
    .detail { height: auto; max-height: 8; border-top: solid $primary 20%; padding: 0 0; }
    DataTable { height: 1fr; background: $background; }
    DataTable > .datatable--header { background: $background; color: $text-muted; text-style: none; }
    .title { height: 1; color: $text-muted; }
    #hud-row { height: 1fr; }
    #hud-left { width: 50; }
    #hud-set { height: 8; }
    #modules { height: 1fr; }
    #hud-preview { width: 1fr; padding: 0 0 0 2; }
    #hud-hint, #log-head { height: 1; color: $text-muted; }
    #log { height: 1fr; background: $background; }
    #log-search { display: none; height: 3; }
    #log-search.visible { display: block; }
    #actions { width: 36; height: 1fr; background: $background; }
    #ctl-right { padding: 0 0 0 2; }
    #ctl-info { height: auto; }
    #ctl-out { height: 1fr; background: $background; }
    """
    BINDINGS = [
        Binding("1", "tab('dash')", "État", show=False),
        Binding("2", "tab('deck')", "Deck", show=False),
        Binding("3", "tab('items')", "Reliques", show=False),
        Binding("4", "tab('hud')", "HUD", show=False),
        Binding("5", "tab('journal')", "Journal", show=False),
        Binding("6", "tab('controls')", "Contrôles", show=False),
        Binding("s", "start_daemon", "démarrer", show=False),
        Binding("question_mark", "help", "aide"),
        Binding("q", "quit", "quitter"),
    ]

    def __init__(self, live: bool = True):
        super().__init__()
        self.live = live                       # False en test : ni socket, ni minuteurs de suivi
        self.cfg, self.cfg_error = consolecfg.load()
        self.settings, self.settings_error = settings.load()
        self._settings_mtime = self._mtime(paths.settings_path())
        self.result: StateResult = StateResult("missing")
        self.daemon = ABSENT
        self.pids: list[int] = []
        self._state_mtime: float | None = None
        self._stop = threading.Event()
        self._log_buf: deque[logs.LogLine] = deque(maxlen=5000)
        self._log_level = "debug"
        self._log_needle = ""
        self._log_paused = False
        self._follower: logs.Follower | None = None
        self._deck_cards: list[dict] = []
        self._items: dict[str, list[dict]] = {"relics": [], "potions": []}
        self._preview_visible = True
        self._hud_msg = ""

    # ----- composition -----
    def compose(self) -> ComposeResult:
        with TabbedContent(initial="dash"):
            with TabPane("1 État", id="dash"):
                with VerticalScroll():
                    yield Static(id="dash-body")
            with TabPane("2 Deck", id="deck"):
                yield Static(id="deck-title", classes="title")
                yield DataTable(id="deck-table", cursor_type="row")
                yield Static(id="deck-detail", classes="detail")
            with TabPane("3 Reliques", id="items"):
                yield Static(id="relics-title", classes="title")
                yield DataTable(id="relics", cursor_type="row")
                yield Static(id="potions-title", classes="title")
                yield DataTable(id="potions", cursor_type="row")
                yield Static(id="items-detail", classes="detail")
            with TabPane("4 HUD", id="hud"):
                with Horizontal(id="hud-row"):
                    with Vertical(id="hud-left"):
                        yield KeyTable(self._hud_change, id="hud-set")
                        yield ModuleTable(self._module_change, self._module_move, id="modules")
                    yield Static(id="hud-preview")
                yield Static(id="hud-hint")
            with TabPane("5 Journal", id="journal"):
                yield Static(id="log-head")
                yield RichLog(id="log", max_lines=5000, wrap=False, markup=False, highlight=False)
                yield Input(placeholder="chercher dans le journal (Entrée, Échap pour effacer)", id="log-search")
            with TabPane("6 Contrôles", id="controls"):
                with Horizontal():
                    yield OptionList(id="actions")
                    with Vertical(id="ctl-right"):
                        yield Static(id="ctl-info")
                        yield RichLog(id="ctl-out", wrap=True, markup=False)
        yield Static(id="statusbar")
        yield Footer()

    def on_mount(self) -> None:
        self.register_theme(ULTIMA_THEME)
        try:
            self.theme = self.cfg.theme
        except Exception:  # thème inconnu dans console.json
            self.theme = "ultima"
        self.query_one("#deck-table", DataTable).add_columns("Note", "Carte", TIMES, "Coût", "Type", "Enchantement")
        self.query_one("#relics", DataTable).add_columns("Note", "Relique")
        self.query_one("#potions", DataTable).add_columns("Note", "Potion")
        self.query_one("#hud-set", KeyTable).add_columns("HUD", "Valeur")
        self.query_one("#modules", ModuleTable).add_columns("", "Module", "Affiché")
        self._fill_actions()
        self.reload_state()
        self.refresh_hud()
        self.load_log()
        if self.live:
            self.set_interval(1.0, self.tick)
            self.set_interval(0.5, self.poll_log)
            self.watch_socket()

    def on_unmount(self) -> None:
        self._stop.set()

    @staticmethod
    def _mtime(p: Path) -> float | None:
        try:
            return p.stat().st_mtime
        except OSError:
            return None

    # ----- actions globales -----
    def action_tab(self, tab: str) -> None:
        self.query_one(TabbedContent).active = tab

    def action_help(self) -> None:
        self.push_screen(HelpScreen())

    def action_start_daemon(self) -> None:
        self.run_action_item("start")

    # ----- état : socket (sans sondage du daemon) + state.json -----
    @work(thread=True, exclusive=True, group="socket")
    def watch_socket(self) -> None:
        sock_path = str(paths.socket_path())
        while not self._stop.is_set():
            s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            try:
                s.connect(sock_path)
                s.settimeout(1.0)
                self.call_from_thread(self._set_daemon, ALIVE)
                buf = b""
                while not self._stop.is_set():
                    try:
                        chunk = s.recv(65536)
                    except socket.timeout:
                        continue
                    if not chunk:
                        break
                    buf += chunk
                    while b"\n" in buf:
                        line, buf = buf.split(b"\n", 1)
                        if line.strip():
                            self.call_from_thread(self._on_state_line, line.decode("utf-8", "replace"))
            except OSError:
                pass
            finally:
                s.close()
            if self._stop.is_set():
                break
            self.call_from_thread(self._set_daemon, REFUSED if Path(sock_path).exists() else ABSENT)
            self._stop.wait(2.0)

    def _set_daemon(self, status: str) -> None:
        if status != self.daemon:
            self.daemon = status
            self.pids = procs.owned_pids()
            self.reload_state() if status != ALIVE else self.render_status()
            self.render_controls_info()

    def _on_state_line(self, line: str) -> None:
        self.result = parse_state(line, paths.state_path(), time.time())
        self._state_mtime = self._mtime(paths.state_path())
        self.render_all()

    def tick(self) -> None:
        """1 s : âge de l'état, state.json si le daemon est arrêté, settings.json modifié ailleurs."""
        if self.daemon != ALIVE:
            m = self._mtime(paths.state_path())
            if m != self._state_mtime:
                self.reload_state()
        m = self._mtime(paths.settings_path())
        if m != self._settings_mtime:
            self._settings_mtime = m
            self.settings, self.settings_error = settings.load()
            self.refresh_hud()
        self.render_status()

    def reload_state(self) -> None:
        self.result = load_state()
        self._state_mtime = self.result.mtime
        if not self.live:
            from .state import probe_socket
            self.daemon = probe_socket()
        self.pids = procs.owned_pids()
        self.render_all()

    @property
    def state(self):
        return self.result.state if self.result.kind == OK else None

    def health(self) -> textview.Health:
        return textview.Health(self.daemon, self.pids, self.result.kind, self.result.age_s, self.result.error)

    def render_all(self) -> None:
        self.render_status()
        self.render_dashboard()
        self.render_deck()
        self.render_items()
        self.render_preview()
        self.render_controls_info()

    def render_status(self) -> None:
        self.query_one("#statusbar", Static).update(to_text([textview.health_line(self.state, self.health())]))

    # ----- 1 État -----
    def render_dashboard(self) -> None:
        width = max(40, self.size.width - 4)
        head = banner.segments(self.size.width, self.size.height) + [[]]
        self.query_one("#dash-body", Static).update(
            to_text(head + textview.dashboard(self.state, self.health(), width)))

    # ----- 2 Deck -----
    def render_deck(self) -> None:
        t = self.query_one("#deck-table", DataTable)
        row = t.cursor_row
        t.clear()
        groups = textview.deck_rows(self.state)
        self._deck_cards = [c for c, _ in groups]
        n = sum(k for _, k in groups)
        curses = sum(k for c, k in groups if c.get("isCurse"))
        ench = sum(k for c, k in groups if c.get("enchantment"))
        title = f"DECK{SEP}{n} cartes"
        if ench:
            title += f"{SEP}{ench} enchantée{'s' if ench > 1 else ''}"
        if curses:
            title += f"{SEP}{curses} malédiction{'s' if curses > 1 else ''}"
        self.query_one("#deck-title", Static).update(title if self.state else f"DECK{SEP}{DASH}")
        for c, k in groups:
            if c.get("isCurse"):
                name = Text(f"{CURSE_MARK} {textview.card_label(c)}", style=f"bold {PALETTE['purple']}")
            else:
                name = Text(textview.card_label(c))
            ench_t = Text(textview.enchant_text(c.get("enchantment")), style=PALETTE["aqua"])
            t.add_row(grade_cell(c.get("score")), name, str(k), textview.cost_text(c),
                      textview.TYPE_LABELS.get(c.get("type"), c.get("type") or ""), ench_t)
        if t.row_count:
            t.move_cursor(row=min(row, t.row_count - 1))
        self._deck_detail(t.cursor_row)

    def _deck_detail(self, row: int) -> None:
        card = self._deck_cards[row] if 0 <= row < len(self._deck_cards) else None
        self.query_one("#deck-detail", Static).update(to_text(textview.card_detail(card)))

    @on(DataTable.RowHighlighted, "#deck-table")
    def _deck_row(self, ev: DataTable.RowHighlighted) -> None:
        self._deck_detail(ev.cursor_row)

    # ----- 3 Reliques / potions -----
    def render_items(self) -> None:
        run = get(self.state, "run", default={})
        for key, label in (("relics", "RELIQUES"), ("potions", "POTIONS")):
            items = [i for i in (run.get(key) or []) if isinstance(i, dict)]
            self._items[key] = items
            t = self.query_one(f"#{key}", DataTable)
            row = t.cursor_row
            t.clear()
            for it in items:
                t.add_row(grade_cell(it.get("score")), textview.txt(it.get("name")))
            if t.row_count:
                t.move_cursor(row=min(row, t.row_count - 1))
            self.query_one(f"#{key}-title", Static).update(f"{label}{SEP}{len(items)}")
        self._items_detail("relics", self.query_one("#relics", DataTable).cursor_row)

    def _items_detail(self, key: str, row: int) -> None:
        items = self._items.get(key, [])
        it = items[row] if 0 <= row < len(items) else None
        self.query_one("#items-detail", Static).update(to_text(textview.item_detail(it)))

    @on(DataTable.RowHighlighted, "#relics, #potions")
    def _item_row(self, ev: DataTable.RowHighlighted) -> None:
        self._items_detail(ev.data_table.id, ev.cursor_row)

    # ----- 4 HUD -----
    HUD_ROWS = ("quality", "height", "opacity", "side", "combat", "grades")

    def refresh_hud(self) -> None:
        h = self.settings.hud
        t = self.query_one("#hud-set", KeyTable)
        row = t.cursor_row
        t.clear()

        def yn(v: bool) -> str:
            return "oui" if v else "non"

        quality = Text()
        for i, q in enumerate(settings.QUALITIES):
            if i:
                quality.append(" ")
            quality.append(q, style=f"bold {PALETTE['gold']}" if q == h.quality else "dim")
        t.add_row("Qualité du verre", quality)
        bounds = "dim"
        t.add_row("Hauteur", Text(f"{round(h.height_fraction * 100)} % de l'écran")
                  + Text(f"  {round(settings.HEIGHT_MIN * 100)}–{round(settings.HEIGHT_MAX * 100)}", style=bounds))
        t.add_row("Opacité au repos", Text(f"{round(h.rest_opacity * 100)} %")
                  + Text(f"  {round(settings.OPACITY_MIN * 100)}–{round(settings.OPACITY_MAX * 100)}", style=bounds))
        t.add_row("Côté", SIDE_LABELS.get(h.side, h.side))
        t.add_row("Note du deck en combat", yn(h.deck_score_in_combat))
        t.add_row("Lettres seules", yn(h.grades_only))
        t.move_cursor(row=min(row, t.row_count - 1))

        m = self.query_one("#modules", ModuleTable)
        mrow = m.cursor_row
        m.clear()
        for mod in self.settings.modules:
            mark = Text("●", style=PALETTE["gold"]) if mod.enabled else Text("○", style="dim")
            ctx = settings.CONTEXT_SCREEN.get(mod.id)
            where = f"sur {textview.SCREEN_LABELS.get(ctx, ctx)}" if ctx else "toujours"
            m.add_row(mark, Text(settings.module_label(mod.id), style="" if mod.enabled else "dim"),
                      Text(where if mod.enabled else "masqué", style="dim"))
        if m.row_count:
            m.move_cursor(row=min(mrow, m.row_count - 1))
        hint = "←/→ changer · espace activer · [ ] déplacer · d défauts · p aperçu"
        msg = self.settings_error or self._hud_msg
        self.query_one("#hud-hint", Static).update(
            Text(hint) + (Text(f"   {msg}", style=PALETTE["orange"] if self.settings_error else "dim") if msg else Text()))
        self.render_preview()

    def render_preview(self) -> None:
        pv = self.query_one("#hud-preview", Static)
        pv.display = self._preview_visible and self.size.width >= 84
        if not pv.display:
            return
        h = self.settings.hud
        head = Text(f"Aperçu{SEP}{textview.screen_label(self.state)}{SEP}{h.quality}{SEP}"
                    f"{SIDE_LABELS.get(h.side)}", style="dim")
        body = to_text(textview.hud_preview(self.state, self.settings, width=34))
        pv.update(head + Text("\n\n") + body)

    def on_resize(self, _ev) -> None:
        self.render_preview()
        self.render_dashboard()

    def _save_settings(self, what: str) -> None:
        try:
            p = settings.save(self.settings)
            self._settings_mtime = self._mtime(p)
            self.settings_error = None
            self._hud_msg = f"enregistré{SEP}{what}"
        except OSError as e:
            self._hud_msg = f"échec d'écriture : {e}"
        self.refresh_hud()

    def _hud_change(self, table, row: int, step: int) -> None:
        h = self.settings.hud
        key = self.HUD_ROWS[row]
        if key == "quality":
            h.cycle_quality(step)
            what = f"qualité {h.quality}"
        elif key == "height":
            h.step_height(step)
            what = f"hauteur {round(h.height_fraction * 100)} %"
        elif key == "opacity":
            h.step_opacity(step)
            what = f"opacité {round(h.rest_opacity * 100)} %"
        elif key == "side":
            h.cycle_side()
            what = f"côté {SIDE_LABELS[h.side]}"
        elif key == "combat":
            h.deck_score_in_combat = not h.deck_score_in_combat
            what = "note du deck en combat"
        else:
            h.grades_only = not h.grades_only
            what = "lettres seules"
        self._save_settings(what)

    def _module_change(self, table, row: int, step: int) -> None:
        mod = self.settings.modules[row]
        self.settings.toggle(mod.id)
        self._save_settings(f"{settings.module_label(mod.id)} {'affiché' if mod.enabled else 'masqué'}")

    def _module_move(self, table, row: int, step: int) -> None:
        mod = self.settings.modules[row]
        j = self.settings.move(mod.id, step)
        self._save_settings(f"{settings.module_label(mod.id)} en position {j + 1}")
        table.move_cursor(row=j)

    def key_d(self) -> None:
        if self.query_one(TabbedContent).active == "hud":
            extra = self.settings.extra
            self.settings = OverlaySettings.default()
            self.settings.extra = extra
            self._save_settings("valeurs par défaut")

    def key_p(self) -> None:
        if self.query_one(TabbedContent).active == "hud":
            self._preview_visible = not self._preview_visible
            self.render_preview()

    # ----- 5 Journal -----
    def load_log(self) -> None:
        self._log_buf.clear()
        self._log_buf.extend(logs.parse_lines(logs.tail(paths.log_path(), 2000)))
        self._follower = logs.Follower(paths.log_path(), from_end=True)
        self._follower.poll()
        self.render_log()

    def poll_log(self) -> None:
        if self._follower is None:
            return
        new = self._follower.poll()
        if not new:
            return
        prev = self._log_buf[-1].level if self._log_buf else "info"
        log = self.query_one("#log", RichLog)
        for raw in new:
            ll = logs.parse_line(raw, prev)
            prev = ll.level
            self._log_buf.append(ll)
            if not self._log_paused and logs.accept(ll, self._log_level, self._log_needle):
                log.write(self._log_text(ll))

    def _log_text(self, ll: logs.LogLine) -> Text:
        c = self.cfg
        if ll.ts is None:
            return Text(ll.raw, style=c.color(ll.level))
        t = Text(ll.ts, style=c.color("timestamp"))
        t.append(f" {ll.level.upper():<5} ", style=("bold " if ll.level in ("warn", "error") else "") + c.color(ll.level))
        t.append(ll.msg, style=c.color(ll.level))
        return t

    def render_log(self) -> None:
        log = self.query_one("#log", RichLog)
        log.clear()
        shown = 0
        for ll in self._log_buf:
            if logs.accept(ll, self._log_level, self._log_needle):
                log.write(self._log_text(ll))
                shown += 1
        names = {"debug": "tout", "info": "info et plus", "warn": "alertes"}
        head = Text(f"{paths.log_path()}", style="dim")
        head.append(f"{SEP}{names.get(self._log_level, self._log_level)}")
        if self._log_needle:
            head.append(f"{SEP}« {self._log_needle} »", style=PALETTE["gold"])
        if self._log_paused:
            head.append(f"{SEP}PAUSE", style=PALETTE["orange"])
        if not self._log_buf:
            head.append(f"{SEP}journal vide", style="dim")
        head.append(f"{SEP}{shown} lignes", style="dim")
        self.query_one("#log-head", Static).update(head)

    def _in_journal(self) -> bool:
        return self.query_one(TabbedContent).active == "journal" and not isinstance(self.focused, Input)

    def key_a(self) -> None:
        if self._in_journal():
            self._log_level = "debug"
            self.render_log()

    def key_i(self) -> None:
        if self._in_journal():
            self._log_level = "info"
            self.render_log()

    def key_w(self) -> None:
        if self._in_journal():
            self._log_level = "warn"
            self.render_log()

    def key_slash(self) -> None:
        if self._in_journal():
            inp = self.query_one("#log-search", Input)
            inp.add_class("visible")
            inp.focus()

    def key_c(self) -> None:
        if self._in_journal():
            self.push_screen(ColorsScreen(self.cfg, self.render_log))

    def key_space(self) -> None:
        if self._in_journal():
            self._log_paused = not self._log_paused
            self.render_log()

    @on(Input.Changed, "#log-search")
    def _search(self, ev: Input.Changed) -> None:
        self._log_needle = ev.value.strip()
        self.render_log()

    @on(Input.Submitted, "#log-search")
    def _search_done(self, ev: Input.Submitted) -> None:
        self.query_one("#log", RichLog).focus()

    def key_escape(self) -> None:
        inp = self.query_one("#log-search", Input)
        if inp.has_class("visible"):
            inp.value = ""
            inp.remove_class("visible")
            self._log_needle = ""
            self.render_log()
            self.query_one("#log", RichLog).focus()

    # ----- 6 Contrôles -----
    def _fill_actions(self) -> None:
        ol = self.query_one("#actions", OptionList)
        hl = ol.highlighted
        ol.clear_options()
        ol.add_options([
            Option("Démarrer le daemon", id="start"),
            Option("Arrêter le daemon", id="stop"),
            Option("Redémarrer le daemon", id="restart"),
            Option(f"Miroir mémoire au démarrage : {'oui' if self.cfg.mirror else 'non'}", id="mirror"),
            Option("HUD : activer", id="hud-on"),
            Option("HUD : désactiver", id="hud-off"),
            Option("Vérifier l'installation", id="doctor"),
        ])
        ol.highlighted = hl if hl is not None else 0

    @on(OptionList.OptionSelected, "#actions")
    def _action_selected(self, ev: OptionList.OptionSelected) -> None:
        self.run_action_item(ev.option.id)

    def run_action_item(self, action: str) -> None:
        if action == "mirror":
            self.cfg.mirror = not self.cfg.mirror
            consolecfg.save(self.cfg)
            self._fill_actions()
            self._ctl_write(f"miroir mémoire au prochain démarrage : {'oui' if self.cfg.mirror else 'non'}"
                            f" (console.json)")
            return
        self._ctl_write(f"… {action}")
        self._run_action_thread(action)

    @work(thread=True, group="actions")
    def _run_action_thread(self, action: str) -> None:
        lines: list[str] = []
        if action in ("start", "restart"):
            if action == "restart":
                lines.append(procs.stop().message)
            lines.append(procs.start(self.cfg).message)
        elif action == "stop":
            lines.append(procs.stop().message)
        elif action in ("hud-on", "hud-off"):
            lines.append(procs.hud_set(action == "hud-on").message)
        elif action == "doctor":
            marks = {doctor.OK: "✓", doctor.WARN: "!", doctor.FAIL: "✗", doctor.INFO: "·"}
            for c in doctor.run_checks():
                lines.append(f"{marks[c.status]} {c.label} : {c.detail}")
        for ln in lines:
            self.call_from_thread(self._ctl_write, ln)
        self.call_from_thread(self._after_action)

    def _after_action(self) -> None:
        self.pids = procs.owned_pids()
        if not self.live:
            self.reload_state()
        self.render_status()
        self.render_controls_info()

    def _ctl_write(self, line: str) -> None:
        self.query_one("#ctl-out", RichLog).write(line)

    def render_controls_info(self) -> None:
        hl = get(self.state, "health", default={})
        lines: list[textview.Line] = []
        d_txt = {"alive": "en marche", "refused": "socket fermée (arrêté ?)", "absent": "arrêté"}[self.daemon]
        pid = f"{SEP}pid {' '.join(map(str, self.pids))}" if self.pids else ""
        mode = f"{SEP}systemd" if procs.uses_systemd() else ""
        lines.append([textview.seg("Daemon   ", "dim"),
                      textview.seg(d_txt + pid + mode, None if self.daemon == ALIVE else "orange")])
        if self.state:
            lines.append([textview.seg("Jeu      ", "dim"),
                          textview.seg("lancé" if hl.get("gameRunning") else "fermé")])
            lat = hl.get("mirrorLatencyMs")
            lines.append([textview.seg("Miroir   ", "dim"),
                          textview.seg(("ok" + (f"{SEP}{lat} ms" if lat is not None else "")) if hl.get("mirrorOk") else DASH)])
            lines.append([textview.seg("Save     ", "dim"), textview.seg("ok" if hl.get("saveOk") else "illisible")])
            lines.append([textview.seg("Contrat  ", "dim"),
                          textview.seg(f"v{self.state.get('contractVersion')}.{self.state.get('contractMinor', 0)}")])
        lines.append([textview.seg("Journal  ", "dim"), textview.seg(str(paths.log_path()), "dim")])
        lines.append([textview.seg("Réglages ", "dim"), textview.seg(str(paths.settings_path()), "dim")])
        lines.append([])
        self.query_one("#ctl-info", Static).update(to_text(lines))


def run_tui() -> int:
    UltimaApp().run()
    return 0
