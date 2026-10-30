"""TUI plein écran de `ultima` (Textual).

Choix de la pile : Python + Textual (venv ~/.local/share/ultima/venv) — même langage que le compagnon
(overlay/companion, Python), aucune dépendance système, pilote de test sans TTY (`App.run_test`) qui
permet des captures texte reproductibles. La CLI reste en bibliothèque standard : `ultima status` marche
même sans le venv.

Onglets : 1 État · 2 Deck · 3 Reliques · 4 HUD · 5 Journal · 6 Contrôles · 7 Apparence ; « ? » aide ;
« q » quitter. Depuis la v1.4, TOUT l'habillage vient du thème (`theme.resolve` : ton → couleur) :
le cadre ET le contenu, y compris le journal et le bandeau. Aucune couleur en dur ici.
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

from . import appearance, banner, consolecfg, custom, doctor, fonts, logs, paths, procs, settings, textview
from . import theme as themes
from .palette import ANSI16, CURSE_MARK, DASH, SEP, TIMES
from .settings import SIDE_LABELS, ModuleSetting, OverlaySettings
from .state import ALIVE, ABSENT, OK, REFUSED, StateResult, get, load as load_state, parse as parse_state

SURFACE_LABELS = {"tui": "TUI", "hud": "HUD", "companion": "Compagnon"}


def tone_style(res: themes.Resolution, tone: str | None, bold: bool = False) -> str:
    return res.style(tone, bold)


# ---------- rendu des lignes ----------

def to_text(lines: list[textview.Line], res: themes.Resolution) -> Text:
    """Segments (texte, ton, gras) → Rich Text, couleurs du thème résolu (jamais un hex en dur)."""
    out = Text()
    for i, line in enumerate(lines):
        if i:
            out.append("\n")
        for t, tone, bold in line:
            style = res.style(tone, bold)
            out.append(t, style=style)
    return out


def grade_cell(score, res: themes.Resolution) -> Text:
    g = textview.grade(score)
    return Text(g, style=res.style(textview.grade_tone(g), g != DASH), justify="center")


def screen_text(app: App) -> str:
    """Capture TEXTE de l'écran courant (mêmes données que App.export_screenshot, sans SVG)."""
    width, height = app.size
    console = Console(width=width, height=height, file=io.StringIO(), force_terminal=True,
                      color_system="truecolor", record=True, legacy_windows=False, safe_box=False)
    console.print(app.screen._compositor.render_update(full=True, screen_stack=app._background_screens))
    return "\n".join(line.rstrip() for line in console.export_text().splitlines())


# ---------- thème Textual ----------

def textual_color(value: str) -> str:
    """Nom Rich (16 couleurs) → nom Textual (`ansi_…`) ; un hex reste un hex."""
    return f"ansi_{value}" if value in ANSI16 else value


def textual_theme(tid: str, res: themes.Resolution) -> Theme:
    t = res.tones
    bg = t.get("background") or ""
    return Theme(name=tid, primary=textual_color(t.get("accent", "")),
                 secondary=textual_color(t.get("enchant", "")), accent=textual_color(t.get("accent", "")),
                 warning=textual_color(t.get("warn", "")), error=textual_color(t.get("danger", "")),
                 success=textual_color(t.get("ok", "")), foreground=textual_color(t.get("text", "")),
                 background=textual_color(bg) if bg else "ansi_black", dark=True)


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
        Binding("e", "screens(1)", "écrans", show=False),
    ]

    def __init__(self, on_change, on_move, on_screens=None, **kw):
        super().__init__(on_change, **kw)
        self._on_move = on_move
        self._on_screens = on_screens

    def action_move(self, step: int) -> None:
        if self.row_count:
            self._on_move(self, self.cursor_row, step)

    def action_screens(self, step: int) -> None:
        if self.row_count and self._on_screens:
            self._on_screens(self, self.cursor_row, step)


# ---------- fenêtres modales ----------

HELP = """\
[b]Navigation[/b]  1-7 onglets · Tab champ suivant · ↑/↓ sélection · q quitter · ? cette aide

[b]1 État[/b]       la décision en cours d'abord (lettre S-F + raison), puis malédictions et résumé
[b]2 Deck[/b]       ↑/↓ carte → description FR et détail du score en bas
[b]3 Reliques[/b]   reliques puis potions (Tab) ; détail en bas
[b]4 HUD[/b]        ←/→ changer · espace activer/désactiver · [ ] ou Maj+↑/↓ déplacer un module · e écrans
             d valeurs par défaut · p aperçu — enregistré aussitôt (écriture atomique)
[b]5 Journal[/b]    a tout · i info+ · w alertes · / chercher · Échap effacer · c couleurs (onglet Apparence)
[b]6 Contrôles[/b]  Entrée exécute l'action (démarrer/arrêter le daemon, HUD, vérifier)
[b]7 Apparence[/b]  sous-onglets TUI/HUD/Compagnon/Modules : thème + aperçu, tons, police et taille,
             position et liseré du HUD, blocs (écrans), modules x.*, plugins (activés après test)
[b]Partout[/b]      s démarrer le daemon

Lettres : S A B C D F colorées par le thème (grade.S … grade.F), « — » en muted
« — » = non calculé (jamais 0)
"""


class HelpScreen(ModalScreen):
    BINDINGS = [Binding("escape,q,question_mark", "dismiss", "fermer")]
    DEFAULT_CSS = """
    HelpScreen { align: center middle; }
    HelpScreen > Static { width: 100; max-width: 100%; height: auto; padding: 1 2;
                          background: $surface; border: round $primary 40%; }
    """

    def compose(self) -> ComposeResult:
        yield Static(HELP + "\n[dim]Échap pour fermer[/dim]")


# ---------- application ----------

class UltimaApp(App):
    TITLE = "ULTIMA"
    CSS = """
    Screen { background: $background; }
    Screen.ansi16 { background: transparent; }
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
    #appearance { height: 1fr; }
    #appearance ContentSwitcher { height: 1fr; }
    .app-table { height: 1fr; }
    .app-preview { width: 46; padding: 0 0 0 2; }
    .app-head { height: 1; color: $text-muted; }
    """
    BINDINGS = [
        Binding("1", "tab('dash')", "État", show=False),
        Binding("2", "tab('deck')", "Deck", show=False),
        Binding("3", "tab('items')", "Reliques", show=False),
        Binding("4", "tab('hud')", "HUD", show=False),
        Binding("5", "tab('journal')", "Journal", show=False),
        Binding("6", "tab('controls')", "Contrôles", show=False),
        Binding("7", "tab('appearance')", "Apparence", show=False),
        Binding("s", "start_daemon", "démarrer", show=False),
        Binding("question_mark", "help", "aide"),
        Binding("q", "quit", "quitter"),
    ]

    # Lignes du tableau Apparence : (clé, libellé)
    TONE_ROWS = tuple((f"tone:{t}", f"Ton {t}") for t in themes.TONES if t not in ("background", "border"))
    LOG_ROWS = tuple((f"log:{k}", f"Journal {k}") for k in themes.LOG_KEYS)
    TUI_ROWS = (("theme", "Thème"),) + TONE_ROWS + LOG_ROWS
    FONT_ROWS = (("theme", "Thème"), ("font", "Police"), ("size", "Taille"))
    HUD_ROWS = FONT_ROWS + (
        ("side", "Côté"), ("anchor", "Ancrage"), ("offsetX", "Décalage X"), ("offsetY", "Décalage Y"),
        ("width", "Largeur"), ("railEdge", "Liseré du bord"),
    ) + TONE_ROWS
    COMP_ROWS = FONT_ROWS + TONE_ROWS

    def __init__(self, live: bool = True):
        super().__init__()
        self.live = live                       # False en test : ni socket, ni minuteurs de suivi
        self.cfg, self.cfg_error = consolecfg.load()
        self.settings, self.settings_error = settings.load()
        self.appearance, self.appearance_error = appearance.load()
        try:                                   # migration UNIQUE console.json → appearance.tui
            if appearance.migrate_console():
                self.appearance, self.appearance_error = appearance.load()
        except (settings.SettingsError, OSError) as e:
            self.appearance_error = str(e)
        self.res = self.appearance.resolve("tui", character=None)
        self._settings_mtime = self._mtime(paths.settings_path())
        self._appearance_mtime = self._mtime(paths.settings_path())
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
        self._app_msg = ""
        self._terms = themes.load_all()
        self._module_findings: list[custom.Finding] = []
        self._plugin_findings: list[custom.Finding] = []

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
                        yield ModuleTable(self._module_change, self._module_move, self._module_screens,
                                          id="modules")
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
            with TabPane("7 Apparence", id="appearance"):
                with TabbedContent(initial="app-tui", id="appearance-tabs"):
                    with TabPane("TUI", id="app-tui"):
                        with Horizontal():
                            yield KeyTable(self._app_change, id="app-tui-table", classes="app-table")
                            yield Static(id="app-tui-preview", classes="app-preview")
                    with TabPane("HUD", id="app-hud"):
                        with Horizontal():
                            yield KeyTable(self._app_change, id="app-hud-table", classes="app-table")
                            yield Static(id="app-hud-preview", classes="app-preview")
                    with TabPane("Compagnon", id="app-comp"):
                        with Horizontal():
                            yield KeyTable(self._app_change, id="app-comp-table", classes="app-table")
                            yield Static(id="app-comp-preview", classes="app-preview")
                    with TabPane("Modules", id="app-mod"):
                        with Horizontal():
                            with Vertical(classes="app-table"):
                                yield Static("Blocs (ordre, écrans)", classes="app-head")
                                yield ModuleTable(self._module_change, self._module_move,
                                                  self._module_screens, id="app-blocks")
                                yield Static("Plugins (activés après un test réussi)", classes="app-head")
                                yield KeyTable(self._plugin_change, id="app-plugins")
                            yield Static(id="app-mod-preview", classes="app-preview")
        yield Static(id="statusbar")
        yield Footer()

    def on_mount(self) -> None:
        self._register_themes()
        self.query_one("#deck-table", DataTable).add_columns("Note", "Carte", TIMES, "Coût", "Type", "Enchantement")
        self.query_one("#relics", DataTable).add_columns("Note", "Relique")
        self.query_one("#potions", DataTable).add_columns("Note", "Potion")
        self.query_one("#hud-set", KeyTable).add_columns("HUD", "Valeur")
        self.query_one("#modules", ModuleTable).add_columns("", "Module", "Affiché")
        self.query_one("#app-blocks", ModuleTable).add_columns("", "Bloc", "Écrans")
        self.query_one("#app-plugins", KeyTable).add_columns("", "Plugin", "État")
        for tid, widget in (("app-tui", "#app-tui-table"), ("app-hud", "#app-hud-table"),
                            ("app-comp", "#app-comp-table")):
            self.query_one(widget, KeyTable).add_columns("Réglage", "Valeur")
        self._fill_actions()
        self.reload_state()
        self.refresh_hud()
        self.load_log()
        self.render_appearance()
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

    def character(self) -> str | None:
        """`run.character` (contrat), repli `run.characterId` : sert à résoudre `auto-perso`."""
        run = get(self.state, "run", default=None)
        if not isinstance(run, dict):
            return None
        for key in ("character", "characterId"):
            v = run.get(key)
            if isinstance(v, str) and v:
                return v
        return None

    # ----- thème -----
    def _register_themes(self) -> None:
        for tid in self._terms:
            res = themes.resolve(tid, surface="tui", character=self.character())
            self.register_theme(textual_theme(tid, res))
        self.apply_theme()

    def apply_theme(self) -> None:
        """Résout le thème de la surface TUI (auto-perso inclus) et l'applique au cadre ET au contenu."""
        self.res = self.appearance.resolve("tui", character=self.character())
        if self.res.id in self._terms:
            try:
                self.theme = self.res.id
                self.refresh_css()
            except Exception:                   # thème retiré entre-temps : on garde le cadre actuel
                pass
        screen = self.screen
        screen.set_class(self.res.ansi16, "ansi16")
        screen.styles.background = "transparent" if self.res.ansi16 or not self.res.background \
            else self.res.background

    def reload_appearance(self) -> None:
        self.appearance, err = appearance.load()
        if err and not self.appearance_error:
            self.appearance_error = err
        self._terms = themes.load_all()
        self._register_themes()
        self.render_all()
        self.refresh_hud()
        self.render_log()
        self.render_appearance()

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
            self.reload_appearance()
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
        self.query_one("#statusbar", Static).update(
            to_text([textview.health_line(self.state, self.health())], self.res))

    # ----- 1 État -----
    def render_dashboard(self) -> None:
        width = max(40, self.size.width - 4)
        head = banner.segments(self.size.width, self.size.height) + [[]]
        self.query_one("#dash-body", Static).update(
            to_text(head + textview.dashboard(self.state, self.health(), width), self.res))

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
                name = Text(f"{CURSE_MARK} {textview.card_label(c)}", style=self.res.style("curse", True))
            else:
                name = Text(textview.card_label(c))
            ench_t = Text(textview.enchant_text(c.get("enchantment")), style=self.res.style("enchant"))
            t.add_row(grade_cell(c.get("score"), self.res), name, str(k), textview.cost_text(c),
                      textview.TYPE_LABELS.get(c.get("type"), c.get("type") or ""), ench_t)
        if t.row_count:
            t.move_cursor(row=min(row, t.row_count - 1))
        self._deck_detail(t.cursor_row)

    def _deck_detail(self, row: int) -> None:
        card = self._deck_cards[row] if 0 <= row < len(self._deck_cards) else None
        self.query_one("#deck-detail", Static).update(to_text(textview.card_detail(card), self.res))

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
                t.add_row(grade_cell(it.get("score"), self.res), textview.txt(it.get("name")))
            if t.row_count:
                t.move_cursor(row=min(row, t.row_count - 1))
            self.query_one(f"#{key}-title", Static).update(f"{label}{SEP}{len(items)}")
        self._items_detail("relics", self.query_one("#relics", DataTable).cursor_row)

    def _items_detail(self, key: str, row: int) -> None:
        items = self._items.get(key, [])
        it = items[row] if 0 <= row < len(items) else None
        self.query_one("#items-detail", Static).update(to_text(textview.item_detail(it), self.res))

    @on(DataTable.RowHighlighted, "#relics, #potions")
    def _item_row(self, ev: DataTable.RowHighlighted) -> None:
        self._items_detail(ev.data_table.id, ev.cursor_row)

    # ----- 4 HUD -----
    HUD_FIELDS = ("quality", "height", "opacity", "side", "combat", "grades")

    def refresh_hud(self) -> None:
        self._merge_custom_blocks()
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
            quality.append(q, style=self.res.style("accent", True) if q == h.quality else "dim")
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

        self._refresh_blocks()
        hint = "←/→ changer · espace activer · [ ] déplacer · e écrans · d défauts · p aperçu"
        msg = self.settings_error or self._hud_msg
        self.query_one("#hud-hint", Static).update(
            Text(hint) + (Text(f"   {msg}", style=self.res.style("warn")) if msg else Text()))
        self.render_preview()

    def _refresh_blocks(self) -> None:
        for widget in ("#modules", "#app-blocks"):
            try:
                table = self.query_one(widget, ModuleTable)
            except Exception:
                continue
            mrow = table.cursor_row
            table.clear()
            for mod in self.settings.modules:
                mark = Text("●", style=self.res.style("accent")) if mod.enabled else Text("○", style="dim")
                ctx = settings.CONTEXT_SCREEN.get(mod.id)
                if mod.screens:
                    where = ", ".join(textview.SCREEN_LABELS.get(s, s) for s in mod.screens)
                elif ctx:
                    where = f"sur {textview.SCREEN_LABELS.get(ctx, ctx)}"
                else:
                    where = "toujours"
                table.add_row(mark, Text(settings.module_label(mod.id), style="" if mod.enabled else "dim"),
                              Text(where if mod.enabled else "masqué", style="dim"))
            if table.row_count:
                table.move_cursor(row=min(mrow, table.row_count - 1))

    def _merge_custom_blocks(self) -> None:
        """Les modules perso et plugins (`x.…`) figurent dans les blocs, comme le demande docs/44 §3."""
        if not hasattr(self, "_module_findings") or not self._module_findings and not self._plugin_findings:
            self._module_findings = custom.load_modules()
            self._plugin_findings = custom.load_plugins()
        known = {m.id for m in self.settings.modules}
        for f in [*self._module_findings, *self._plugin_findings]:
            if f.id.startswith("x.") and f.id not in known:
                extra = {"screens": list(f.screens)} if f.screens else {}
                self.settings.modules.append(ModuleSetting(f.id, f.enabled if f.kind == "plugin" else True,
                                                           len(self.settings.modules), extra))

    def render_preview(self) -> None:
        pv = self.query_one("#hud-preview", Static)
        pv.display = self._preview_visible and self.size.width >= 84
        if not pv.display:
            return
        h = self.settings.hud
        res = self.appearance.resolve("hud", character=self.character())
        pos = self.appearance.hud.position
        head = Text(f"Aperçu{SEP}{textview.screen_label(self.state)}{SEP}{h.quality}{SEP}"
                    f"{SIDE_LABELS.get(h.side)}{SEP}{pos.width} px", style="dim")
        body = to_text(textview.hud_preview(self.state, self.settings, width=34), res)
        pv.update(head + Text("\n\n") + body)

    def on_resize(self, _ev) -> None:
        self.render_preview()
        self.render_dashboard()

    def _save_settings(self, what: str) -> None:
        try:
            p = settings.save(self.settings)     # relit le fichier : conserve appearance, pluginsEnabled…
            self._settings_mtime = self._mtime(p)
            self.settings_error = None
            self._hud_msg = f"enregistré{SEP}{what}"
        except (settings.SettingsError, OSError) as e:
            self._hud_msg = f"échec d'écriture : {e}"
        self.refresh_hud()

    def _hud_change(self, table, row: int, step: int) -> None:
        h = self.settings.hud
        key = self.HUD_FIELDS[row]
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
            try:                                   # même valeur dans appearance.hud.position (v1.4)
                appearance.set_position("hud", side=h.side)
                self.appearance.hud.position.side = h.side
            except (appearance.AppearanceError, settings.SettingsError, OSError):
                pass
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

    def _module_screens(self, table, row: int, step: int) -> None:
        """Cycle les écrans d'un bloc : défaut (∅) → chaque écran seul → map+shop → défaut."""
        mod = self.settings.modules[row]
        presets: list[list[str] | None] = [None] + [[s] for s in custom.SCREENS] + [["map", "shop"]]
        cur = mod.screens
        idx = presets.index(cur) if cur in presets else 0
        value = presets[(idx + step) % len(presets)]
        try:
            try:
                appearance.set_module_screens(mod.id, value)
            except appearance.AppearanceError:
                # le bloc vient d'un module perso : il n'est pas encore dans settings.json
                self._save_settings("blocs")
                appearance.set_module_screens(mod.id, value)
        except (settings.SettingsError, appearance.AppearanceError, OSError) as e:
            self._hud_msg = str(e)
            return
        if value is None:
            mod.extra.pop("screens", None)
        else:
            mod.extra["screens"] = value
        self._save_settings(f"{settings.module_label(mod.id)} : "
                            + ("écrans par défaut" if value is None else ", ".join(value)))

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
        res = self.res
        if ll.ts is None:
            return Text(ll.raw, style=res.log_style(ll.level))
        t = Text(ll.ts, style=res.log_style("timestamp"))
        t.append(f" {ll.level.upper():<5} ", style=res.log_style(ll.level, bold=ll.level in ("warn", "error")))
        t.append(ll.msg, style=res.log_style(ll.level))
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
            head.append(f"{SEP}« {self._log_needle} »", style=self.res.style("accent"))
        if self._log_paused:
            head.append(f"{SEP}PAUSE", style=self.res.style("warn"))
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
        """Les couleurs du journal vivent dans l'onglet Apparence (surface TUI)."""
        if self._in_journal():
            self.action_tab("appearance")
            self.query_one("#appearance-tabs", TabbedContent).active = "app-tui"

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

    # ----- 7 Apparence -----
    def render_appearance(self) -> None:
        self._render_surface_table("tui", "#app-tui-table", "#app-tui-preview")
        self._render_surface_table("hud", "#app-hud-table", "#app-hud-preview")
        self._render_surface_table("companion", "#app-comp-table", "#app-comp-preview")
        self._render_modules_pane()

    def _rows_for(self, surface: str) -> tuple:
        return {"tui": self.TUI_ROWS, "hud": self.HUD_ROWS, "companion": self.COMP_ROWS}[surface]

    def _theme_candidates(self) -> list[str]:
        return sorted(self._terms)

    def _tone_candidates(self, surface: str) -> list[str | None]:
        """Valeurs proposées pour une surcharge de ton : aucune, puis les couleurs du thème."""
        res = self.appearance.resolve(surface, character=self.character())
        colors: list[str | None] = [None]
        for v in res.tones.values():
            if themes.is_hex(v) and v.upper() not in colors:
                colors.append(v.upper())
        return colors

    def _font_candidates(self) -> list[str | None]:
        out: list[str | None] = [None]
        try:
            for name in ("base", "systeme", "jeu", "perso"):
                for entry in fonts.list_fonts().get(name, []):
                    fam = entry.get("family")
                    if fam and fam not in out:
                        out.append(fam)
        except Exception:
            pass
        return out

    def _render_surface_table(self, surface: str, table_id: str, preview_id: str) -> None:
        table = self.query_one(table_id, KeyTable)
        row = table.cursor_row
        table.clear()
        s = self.appearance.surface(surface)
        res = self.appearance.resolve(surface, character=self.character())
        for key, label in self._rows_for(surface):
            if key == "theme":
                shown = s.theme if s.theme != themes.AUTO else f"{s.theme} → {res.id}"
                table.add_row(label, Text(shown, style=self.res.style("accent")))
            elif key.startswith("tone:"):
                tone = key.split(":", 1)[1]
                value = s.tones.get(tone)
                txt = Text("■ ", style=res.style(tone))
                txt.append(value or "thème", style="dim" if not value else "")
                table.add_row(label, txt)
            elif key.startswith("log:"):
                log_key = key.split(":", 1)[1]
                value = s.log_colors.get(log_key)
                txt = Text("■ ", style=res.log_style(log_key))
                txt.append(value or "thème", style="dim" if not value else "")
                table.add_row(label, txt)
            elif key == "font":
                table.add_row(label, s.font.family or "thème")
            elif key == "size":
                table.add_row(label, str(s.font.size) if s.font.size else "thème")
            elif key == "side":
                table.add_row(label, s.position.side)
            elif key == "anchor":
                table.add_row(label, s.position.anchor)
            elif key == "offsetX":
                table.add_row(label, str(s.position.offset_x))
            elif key == "offsetY":
                table.add_row(label, str(s.position.offset_y))
            elif key == "width":
                table.add_row(label, f"{s.position.width} px")
            elif key == "railEdge":
                table.add_row(label, "oui" if s.rail_edge else "non")
        if table.row_count:
            table.move_cursor(row=min(row, table.row_count - 1))
        self.query_one(preview_id, Static).update(self._surface_preview(surface))

    def _surface_preview(self, surface: str) -> Text:
        """Aperçu vivant : mêmes tons que la surface, donc la preuve que le contenu suit le thème."""
        res = self.appearance.resolve(surface, character=self.character())
        head = Text(f"{SURFACE_LABELS[surface]}{SEP}thème {res.id}"
                    + (f"{SEP}police {res.font_family}" if res.font_family else "")
                    + (f"{SEP}{res.font_size} px" if res.font_size else ""), style="dim")
        sample = Text("\n\n")
        sample.append("Décision  ", style="dim")
        sample.append("S", style=res.style("grade.S", True))
        sample.append("  ")
        sample.append("A", style=res.style("grade.A", True))
        sample.append("  ")
        sample.append("—", style=res.style("muted"))
        sample.append("\n")
        sample.append("!! Malédiction", style=res.style("curse", True))
        sample.append("  ·  ")
        sample.append("Célérité 1", style=res.style("enchant"))
        sample.append("\n")
        sample.append("LÉTAL possible", style=res.style("danger", True))
        sample.append("  ·  ")
        sample.append("Shop rentable", style=res.style("ok"))
        sample.append("\n")
        sample.append("2026-10-30 12:00:00.000 ", style=res.log_style("timestamp"))
        sample.append("WARN  ", style=res.log_style("warn", True))
        sample.append("ptrace_scope=1", style=res.log_style("warn"))
        if surface == "hud":
            p = self.appearance.hud.position
            sample.append(f"\n\nPosition {p.side}/{p.anchor} · {p.width} px · décalages "
                          f"{p.offset_x},{p.offset_y} · liseré {'oui' if self.appearance.hud.rail_edge else 'non'}",
                          style="dim")
        return head + sample

    def _render_modules_pane(self) -> None:
        findings = [*custom.load_modules(), *custom.load_plugins()]
        self._module_findings = custom.load_modules()
        self._plugin_findings = custom.load_plugins()
        plugins = self.query_one("#app-plugins", KeyTable)
        row = plugins.cursor_row
        plugins.clear()
        enabled = set(appearance.plugins_enabled())
        self._plugin_rows = self._plugin_findings
        for f in self._plugin_findings:
            mark = Text("●", style=self.res.style("ok")) if f.id in enabled else Text("○", style="dim")
            state = "actif" if f.id in enabled else ("invalide" if not f.valid else "inactif")
            plugins.add_row(mark, Text(f.id), Text(state, style="dim" if f.id not in enabled else ""))
        if plugins.row_count:
            plugins.move_cursor(row=min(row, plugins.row_count - 1))
        invalid = [f for f in findings if not f.valid]
        lines = [Text("Modules et plugins", style="bold"),
                 Text(f"{len(self._module_findings)} module(s) · {len(self._plugin_findings)} plugin(s)"
                      f" · {len(enabled)} activé(s)", style="dim")]
        for f in invalid:
            lines.append(Text(f"\n{f.id} : " + " ; ".join(f.problems[:3]), style=self.res.style("danger")))
        for f in self._module_findings:
            lines.append(Text(f"\n{f.id} — ", style=self.res.style("accent")) +
                         Text("valide" if f.valid else "invalide", style="dim"))
        self.query_one("#app-mod-preview", Static).update(
            Text("\n").join(lines) + Text("\n\n" + self._app_msg if self._app_msg else "", style="dim"))

    def _app_change(self, table, row: int, step: int) -> None:
        surface = {"app-tui-table": "tui", "app-hud-table": "hud", "app-comp-table": "companion"}.get(
            table.id or "")
        if surface is None:
            return
        key = self._rows_for(surface)[row][0]
        s = self.appearance.surface(surface)
        try:
            if key == "theme":
                ids = self._theme_candidates()
                i = ids.index(s.theme) if s.theme in ids else -1
                tid = ids[(i + step) % len(ids)]
                appearance.set_theme(surface, tid)
                self._app_msg = f"{SURFACE_LABELS[surface]} : thème {tid}"
            elif key.startswith("tone:"):
                tone = key.split(":", 1)[1]
                values = self._tone_candidates(surface)
                cur = s.tones.get(tone)
                i = values.index(cur) if cur in values else 0
                value = values[(i + step) % len(values)]
                appearance.set_tone(surface, tone, value)
                self._app_msg = f"{SURFACE_LABELS[surface]} : ton {tone} " + (value or "du thème")
            elif key.startswith("log:"):
                log_key = key.split(":", 1)[1]
                values: list[str | None] = [None] + list(themes.TONES)
                cur = s.log_colors.get(log_key)
                i = values.index(cur) if cur in values else 0
                value = values[(i + step) % len(values)]
                appearance.set_log_color(log_key, value)
                self._app_msg = f"Journal {log_key} : " + (value or "du thème")
            elif key == "font":
                values = self._font_candidates()
                cur = s.font.family
                i = values.index(cur) if cur in values else 0
                value = values[(i + step) % len(values)]
                if value is None:
                    appearance.unset_font(surface, "family")
                else:
                    appearance.set_font(surface, family=value)
                self._app_msg = f"{SURFACE_LABELS[surface]} : police " + (value or "du thème")
            elif key == "size":
                sizes = [None] + list(range(appearance.SIZE_MIN, appearance.SIZE_MAX + 1, 2))
                cur = s.font.size
                i = sizes.index(cur) if cur in sizes else 0
                value = sizes[(i + step) % len(sizes)]
                if value is None:
                    appearance.unset_font(surface, "size")
                else:
                    appearance.set_font(surface, size=value)
                self._app_msg = f"{SURFACE_LABELS[surface]} : taille " + (str(value) if value else "du thème")
            elif key in ("side", "anchor", "offsetX", "offsetY", "width"):
                s.position.cycle(key, step)
                appearance.set_position(surface, **{key: getattr(s.position, {
                    "side": "side", "anchor": "anchor", "offsetX": "offset_x", "offsetY": "offset_y",
                    "width": "width"}[key])})
                if key == "side":                  # HudSettings.Side (v1.3) reste cohérent
                    self.settings.hud.side = s.position.side
                    settings.save(self.settings)
                self._app_msg = f"HUD : {key} = {s.position.to_json()[key]}"
            elif key == "railEdge":
                appearance.set_rail_edge(not s.rail_edge)
                self._app_msg = f"HUD : liseré {'oui' if not s.rail_edge else 'non'}"
            self.appearance_error = None
        except (appearance.AppearanceError, settings.SettingsError, OSError) as e:
            self.appearance_error = str(e)
            self._app_msg = f"échec : {e}"
        self.reload_appearance()

    def _plugin_change(self, table, row: int, step: int) -> None:
        findings = getattr(self, "_plugin_rows", [])
        if not (0 <= row < len(findings)):
            return
        pid = findings[row].id
        try:
            if pid in appearance.plugins_enabled():
                custom.disable_plugin(pid)
                self._app_msg = f"{pid} désactivé"
            else:
                ok, result, msg = custom.enable_plugin(pid)
                self._app_msg = msg if ok else f"{pid} : test échoué ({result.mode}) — {result.detail}"
        except (appearance.AppearanceError, settings.SettingsError, custom.CustomError, OSError) as e:
            self._app_msg = f"{pid} : {e}"
        self._settings_mtime = self._mtime(paths.settings_path())
        self._render_modules_pane()
        self.refresh_hud()

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
                      textview.seg(d_txt + pid + mode, None if self.daemon == ALIVE else "warn")])
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
        lines.append([textview.seg("Thème    ", "dim"), textview.seg(f"{self.res.id}{SEP}{self.res.requested}")])
        lines.append([])
        self.query_one("#ctl-info", Static).update(to_text(lines, self.res))


def run_tui() -> int:
    UltimaApp().run()
    return 0
