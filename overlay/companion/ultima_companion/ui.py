"""Interface GTK4 + libadwaita 1.9 — phase design ouverte (Mio, 2026-10-29). Voir DESIGN.md.

Quatre pages (Adw.ViewStack + Adw.ViewSwitcher, barre du bas sur fenêtre étroite) :
Tableau (la décision du moment), Run (deck, reliques, potions, équipe), HUD (réglages v1.3 + modules),
Sources (santé des données). Les données viennent des modules purs `views` / `textview` ; ce fichier
ne fait que les disposer.

L'import de ce module n'ouvre aucune fenêtre : seules des classes sont définies. La fenêtre n'est
créée que dans `CompanionApp.do_activate`, c'est-à-dire quand Mio lance `ultima-companion`.
Elle ne se met jamais au premier plan d'elle-même (aucun present() sur mise à jour d'état).
"""

from __future__ import annotations

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Gdk", "4.0")
gi.require_version("Adw", "1")
gi.require_version("Pango", "1.0")
from gi.repository import Adw, Gdk, Gio, GLib, Gtk, Pango  # noqa: E402

from . import fonts, ipc, settings as settings_mod, textview, theme, views  # noqa: E402
from .state import OK, StateResult  # noqa: E402
from .watch import StateWatcher  # noqa: E402

APP_ID = "io.github.mio.UltimaCompanion"

SHORTCUTS = (
    ("Super+Maj+I", "épingler / déplier"),
    ("Super+Maj+O", "masquer / afficher"),
)
SAVE_DELAY_MS = 350   # curseurs : une seule écriture quand Mio relâche


# ---- briques visuelles --------------------------------------------------------------------------------

def _label(text: str = "", *classes: str, xalign: float = 0.0, wrap: bool = False,
           ellipsize: bool = False) -> Gtk.Label:
    lbl = Gtk.Label(label=text, xalign=xalign, use_markup=False)
    if wrap:
        lbl.set_wrap(True)
        lbl.set_natural_wrap_mode(Gtk.NaturalWrapMode.WORD)
    if ellipsize:
        lbl.set_ellipsize(Pango.EllipsizeMode.END)
    for c in classes:
        if c:
            lbl.add_css_class(c)
    return lbl


def grade_badge(grade: str, big: bool = False) -> Gtk.Label:
    """Lettre S-F : toujours écrite ; la couleur ne fait que la doubler (§9)."""
    badge = _label(grade, "grade", theme.grade_class(grade), xalign=0.5)
    if big:
        badge.add_css_class("big")
    badge.set_valign(Gtk.Align.CENTER)
    badge.set_tooltip_text("Non calculé" if grade == views.DASH else f"Note {grade}")
    return badge


def _clear(box: Gtk.Box) -> None:
    child = box.get_first_child()
    while child is not None:
        nxt = child.get_next_sibling()
        box.remove(child)
        child = nxt


class ItemRow(Gtk.ListBoxRow):
    """Une ligne : lettre · nom (+ mot coloré, raison) · valeur ; détail révélé au clic (niveau 2)."""

    def __init__(self, item: views.Item) -> None:
        super().__init__()
        self.item = item
        outer = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        line = Gtk.Box(spacing=14, margin_top=10, margin_bottom=10, margin_start=14, margin_end=14)
        line.append(grade_badge(item.grade))

        text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2, hexpand=True,
                       valign=Gtk.Align.CENTER)
        text.append(_label(item.label, theme.tone_class(item.tone) or "", ellipsize=True))
        second = Gtk.Box(spacing=8)
        if item.tag:
            second.append(_label(item.tag, "word", theme.tone_class(item.tag_tone) or ""))
        if item.reason:
            second.append(_label(item.reason, "secondary", ellipsize=True))
        if second.get_first_child() is not None:
            text.append(second)
        line.append(text)

        if item.value:
            v = _label(item.value, "value", xalign=1.0)
            v.set_valign(Gtk.Align.CENTER)
            line.append(v)
        self._chevron = None
        if item.detail:
            self._chevron = Gtk.Image.new_from_icon_name("pan-end-symbolic")
            self._chevron.add_css_class("secondary")
            line.append(self._chevron)
        outer.append(line)

        self._revealer = None
        if item.detail:
            self._revealer = Gtk.Revealer(transition_type=Gtk.RevealerTransitionType.SLIDE_DOWN,
                                          transition_duration=160)
            detail = _label("\n".join(item.detail), "secondary", wrap=True)
            detail.set_selectable(True)
            detail.set_margin_start(14 + 30 + 14)
            detail.set_margin_end(14)
            detail.set_margin_bottom(12)
            self._revealer.set_child(detail)
            outer.append(self._revealer)
        self.set_activatable(bool(item.detail))
        self.set_child(outer)
        if item.dim:
            line.add_css_class("dim")
        if item.best:
            self.add_css_class("best")
            self.set_tooltip_text("Meilleur choix selon la note")

    def toggle(self) -> None:
        if self._revealer is None:
            return
        open_ = not self._revealer.get_reveal_child()
        self._revealer.set_reveal_child(open_)
        self._chevron.set_from_icon_name("pan-down-symbolic" if open_ else "pan-end-symbolic")


def item_list(items: list[views.Item]) -> Gtk.ListBox:
    lb = Gtk.ListBox(selection_mode=Gtk.SelectionMode.NONE)
    lb.add_css_class("boxed-list")
    for it in items:
        lb.append(ItemRow(it))
    lb.connect("row-activated", lambda _lb, row: row.toggle() if isinstance(row, ItemRow) else None)
    return lb


def _page_scroller(content: Gtk.Widget, max_width: int = 720) -> Gtk.ScrolledWindow:
    clamp = Adw.Clamp(maximum_size=max_width, tightening_threshold=560, child=content)
    sw = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER, vexpand=True)
    sw.set_child(clamp)
    return sw


def _column() -> Gtk.Box:
    return Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=24, margin_top=28, margin_bottom=36,
                   margin_start=20, margin_end=20)


# ---- page Tableau de bord ------------------------------------------------------------------------------

class DashboardPage(Gtk.Box):
    """Une seule chose importante : la décision du moment ; en dessous, une ligne deck, une ligne santé."""

    def __init__(self, on_open_run) -> None:
        super().__init__(orientation=Gtk.Orientation.VERTICAL)
        self._col = _column()
        self._on_open_run = on_open_run
        self.append(_page_scroller(self._col))

    def update(self, result: StateResult, daemon: str) -> None:
        _clear(self._col)
        st = result.state if result.kind == OK else None
        if st is not None and st.run is not None:
            self._col.append(_label(views.run_title(st), "secondary"))

        d = views.decision(result)
        panel = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
        panel.add_css_class("glass")
        head = Gtk.Box(spacing=12)
        head.append(_label(d.title, "heading-page"))
        if d.subtitle:
            sub = _label(d.subtitle, "secondary", ellipsize=True)
            sub.set_valign(Gtk.Align.END)
            head.append(sub)
        panel.append(head)
        if d.alert:
            panel.append(_label(d.alert, "alert"))
        if d.items:
            panel.append(item_list(d.items))
        if d.empty:
            panel.append(_label(d.empty, "empty", wrap=True))
        self._col.append(panel)

        if st is not None and st.run is not None:
            note = views.deck_note(st)
            row = Gtk.Box(spacing=12)
            row.append(grade_badge(note.grade))
            row.append(_label(views.deck_summary(st), "secondary", ellipsize=True, xalign=0.0))
            row.get_last_child().set_hexpand(True)
            btn = Gtk.Button(label="Voir la run", valign=Gtk.Align.CENTER)
            btn.add_css_class("flat")
            btn.connect("clicked", lambda *_: self._on_open_run())
            row.append(btn)
            self._col.append(row)

        h = views.health_line(result, daemon)
        hrow = Gtk.Box(spacing=8)
        dot = Gtk.Box(valign=Gtk.Align.CENTER)
        dot.add_css_class("dot")
        dot.add_css_class(theme.tone_class(h.tone))
        hrow.append(dot)
        hrow.append(_label(h.text, "secondary", ellipsize=True))
        self._col.append(hrow)


# ---- page Run --------------------------------------------------------------------------------------------

class RunPage(Gtk.Box):
    def __init__(self) -> None:
        super().__init__(orientation=Gtk.Orientation.VERTICAL)
        self._col = _column()
        self.append(_page_scroller(self._col))

    def update(self, result: StateResult) -> None:
        _clear(self._col)
        st = result.state if result.kind == OK else None
        if st is None or st.run is None:
            status = Adw.StatusPage(icon_name="applications-games-symbolic",
                                    title="Pas de run",
                                    description="Le deck, les reliques et les potions apparaîtront ici.")
            status.add_css_class("compact")
            self._col.append(status)
            return

        note = views.deck_note(st)
        panel = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
        panel.add_css_class("glass")
        top = Gtk.Box(spacing=16)
        top.append(grade_badge(note.grade, big=True))
        txt = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4, valign=Gtk.Align.CENTER,
                      hexpand=True)
        txt.append(_label(settings_mod.module_label("deck_score"), "heading-page"))
        txt.append(_label(note.reason, "secondary", ellipsize=True))
        top.append(txt)
        panel.append(top)
        panel.append(item_list([views.Item("Détail de la note", views.DASH,
                                           reason=f"Archétype {note.archetype} · boss {note.boss}",
                                           detail=note.detail)]))
        self._col.append(panel)

        for g in views.run_groups(st):
            box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
            title = _label(g.title.upper(), "heading-group")
            if g.key == "curses":
                title.add_css_class(theme.tone_class("purple"))
            box.append(title)
            if g.items:
                box.append(item_list(g.items))
            else:
                box.append(_label(g.empty, "empty"))
            self._col.append(box)


# ---- page Réglages du HUD ---------------------------------------------------------------------------------

class HudPage(Adw.PreferencesPage):
    """HudSettings v1.3 + modules. Chaque modification est écrite atomiquement dans settings.json ;
    le daemon surveille ce fichier et republie l'état (state.settings) ; l'extension applique."""

    def __init__(self, on_message, on_saved=None) -> None:
        super().__init__()
        self._on_message = on_message
        self._on_saved = on_saved
        self._save_timer: int | None = None
        self._syncing = False
        loaded = settings_mod.load()
        self.settings = loaded.settings
        self._module_rows: list[Adw.SwitchRow] = []

        # -- Apparence
        look = Adw.PreferencesGroup(title="Apparence")
        reset_hud = Gtk.Button(label="Par défaut", valign=Gtk.Align.CENTER)
        reset_hud.add_css_class("flat")
        reset_hud.set_tooltip_text("Verre · 55 % · 62 % · droite")
        reset_hud.connect("clicked", self._on_reset_hud)
        look.set_header_suffix(reset_hud)

        self._quality_row = Adw.ActionRow(title="Qualité du verre", use_markup=False)
        self._quality = Adw.ToggleGroup(valign=Gtk.Align.CENTER)
        for q in settings_mod.QUALITIES:
            self._quality.add(Adw.Toggle(name=q, label=settings_mod.QUALITY_LABELS[q],
                                         tooltip=settings_mod.QUALITY_HINTS[q]))
        self._quality.connect("notify::active-name", self._on_quality)
        self._quality_row.add_suffix(self._quality)
        look.add(self._quality_row)

        side_row = Adw.ActionRow(title="Côté de l'écran", use_markup=False)
        self._side = Adw.ToggleGroup(valign=Gtk.Align.CENTER)
        for s in ("left", "right"):
            self._side.add(Adw.Toggle(name=s, label=settings_mod.SIDE_LABELS[s]))
        self._side.connect("notify::active-name", self._on_side)
        side_row.add_suffix(self._side)
        look.add(side_row)

        self._height, self._height_lbl = self._scale_row(
            look, "Hauteur du panneau", "part de la hauteur de l'écran", settings_mod.HEIGHT_RANGE,
            self._on_height)
        self._opacity, self._opacity_lbl = self._scale_row(
            look, "Opacité au repos", "rail replié, quand la souris est ailleurs", settings_mod.OPACITY_RANGE,
            self._on_opacity)
        self.add(look)

        # -- Contenu
        content = Adw.PreferencesGroup(title="Contenu")
        self._dsic = Adw.SwitchRow(title="Note du deck en combat", use_markup=False,
                                   subtitle="sinon seulement aux moments de décision")
        self._dsic.connect("notify::active", self._on_dsic)
        content.add(self._dsic)
        self._grades_only = Adw.SwitchRow(title="Lettres seules", use_markup=False,
                                          subtitle="masque les raisons, garde S à F")
        self._grades_only.connect("notify::active", self._on_grades_only)
        content.add(self._grades_only)
        self.add(content)

        # -- Modules
        self._modules_group = Adw.PreferencesGroup(
            title="Modules", description="Ordre d'affichage dans le HUD. Les modules contextuels "
                                         "n'apparaissent que sur leur écran.")
        reset = Gtk.Button(label="Par défaut", valign=Gtk.Align.CENTER)
        reset.add_css_class("flat")
        reset.set_tooltip_text("Ordre de Mio : deck, enchantées, malédictions, reliques, potions…")
        reset.connect("clicked", self._on_reset)
        self._modules_group.set_header_suffix(reset)
        self.add(self._modules_group)
        self._build_module_rows()

        # -- Pied : raccourcis + fichier (une ligne chacun)
        foot = Adw.PreferencesGroup()
        keys = " · ".join(f"{k} {what}" for k, what in SHORTCUTS)
        foot.add(self._info_row("Raccourcis", keys))
        status = "lu" if loaded.exists and not loaded.error else (
            loaded.error or "absent : créé à la première modification")
        foot.add(self._info_row("Fichier", f"{settings_mod.paths.settings_path()} · {status}"))
        self.add(foot)

        self._sync_hud_widgets()

    # ---- construction -------------------------------------------------------------------------------
    @staticmethod
    def _info_row(title: str, subtitle: str) -> Adw.ActionRow:
        row = Adw.ActionRow(title=title, subtitle=subtitle, use_markup=False)
        row.set_subtitle_selectable(True)
        return row

    def _scale_row(self, group, title, subtitle, rng, handler):
        row = Adw.ActionRow(title=title, subtitle=subtitle, use_markup=False)
        scale = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, rng[0], rng[1], 0.01)
        scale.set_draw_value(False)
        scale.set_size_request(200, -1)
        scale.set_valign(Gtk.Align.CENTER)
        lbl = _label("", "value", xalign=1.0)
        lbl.set_width_chars(5)
        scale.connect("value-changed", handler)
        row.add_suffix(scale)
        row.add_suffix(lbl)
        group.add(row)
        return scale, lbl

    def _sync_hud_widgets(self) -> None:
        h = self.settings.hud
        self._syncing = True
        try:
            self._quality.set_active_name(h.quality)
            self._quality_row.set_subtitle(settings_mod.QUALITY_HINTS[h.quality])
            self._side.set_active_name(h.side)
            self._height.set_value(h.height_fraction)
            self._height_lbl.set_label(f"{round(h.height_fraction * 100)} %")
            self._opacity.set_value(h.rest_opacity)
            self._opacity_lbl.set_label(f"{round(h.rest_opacity * 100)} %")
            self._dsic.set_active(h.deck_score_in_combat)
            self._grades_only.set_active(h.grades_only)
        finally:
            self._syncing = False

    def _build_module_rows(self) -> None:
        for row in self._module_rows:
            self._modules_group.remove(row)
        self._module_rows = []
        count = len(self.settings.modules)
        for i, m in enumerate(self.settings.modules):
            row = Adw.SwitchRow(use_markup=False, title=settings_mod.module_label(m.id),
                                subtitle=settings_mod.module_hint(m.id))
            row.set_active(m.enabled)
            row.connect("notify::active", self._on_toggled, m.id)

            up = Gtk.Button(icon_name="go-up-symbolic", valign=Gtk.Align.CENTER)
            up.set_tooltip_text("Monter")
            up.add_css_class("flat")
            up.set_sensitive(i > 0)
            up.connect("clicked", self._on_move, m.id, -1)

            down = Gtk.Button(icon_name="go-down-symbolic", valign=Gtk.Align.CENTER)
            down.set_tooltip_text("Descendre")
            down.add_css_class("flat")
            down.set_sensitive(i < count - 1)
            down.connect("clicked", self._on_move, m.id, +1)

            row.add_prefix(up)
            row.add_prefix(down)
            self._modules_group.add(row)
            self._module_rows.append(row)

    # ---- écriture ---------------------------------------------------------------------------------------
    def _persist(self) -> None:
        if self._save_timer is not None:
            GLib.source_remove(self._save_timer)
            self._save_timer = None
        try:
            path = settings_mod.save(self.settings)
        except OSError as e:
            self._on_message(f"Échec de l'écriture des réglages : {e}")
            return
        self._on_message(f"Enregistré ({path.name})")
        if self._on_saved is not None:
            self._on_saved()

    def _persist_later(self) -> None:
        if self._save_timer is not None:
            GLib.source_remove(self._save_timer)
        self._save_timer = GLib.timeout_add(SAVE_DELAY_MS, self._persist_timeout)

    def _persist_timeout(self) -> bool:
        self._save_timer = None
        self._persist()
        return GLib.SOURCE_REMOVE

    def flush(self) -> None:
        """Fermeture de la fenêtre : écrire un réglage de curseur encore en attente."""
        if self._save_timer is not None:
            self._persist()

    # ---- actions HUD -------------------------------------------------------------------------------------
    def _on_quality(self, group, _pspec) -> None:
        name = group.get_active_name()
        if self._syncing or name is None or name == self.settings.hud.quality:
            return
        self.settings.hud.set_quality(name)
        self._quality_row.set_subtitle(settings_mod.QUALITY_HINTS[name])
        self._persist()

    def _on_side(self, group, _pspec) -> None:
        name = group.get_active_name()
        if self._syncing or name is None or name == self.settings.hud.side:
            return
        self.settings.hud.set_side(name)
        self._persist()

    def _on_height(self, scale) -> None:
        self._height_lbl.set_label(f"{round(scale.get_value() * 100)} %")
        if self._syncing:
            return
        self.settings.hud.set_height(scale.get_value())
        self._persist_later()

    def _on_opacity(self, scale) -> None:
        self._opacity_lbl.set_label(f"{round(scale.get_value() * 100)} %")
        if self._syncing:
            return
        self.settings.hud.set_opacity(scale.get_value())
        self._persist_later()

    def _on_dsic(self, row, _pspec) -> None:
        if self._syncing:
            return
        self.settings.hud.deck_score_in_combat = row.get_active()
        self._persist()

    def _on_grades_only(self, row, _pspec) -> None:
        if self._syncing:
            return
        self.settings.hud.grades_only = row.get_active()
        self._persist()

    def _on_reset_hud(self, _button) -> None:
        self.settings.reset_hud()
        self._sync_hud_widgets()
        self._persist()

    # ---- actions modules ---------------------------------------------------------------------------------
    def _on_toggled(self, row: Adw.SwitchRow, _pspec, module_id: str) -> None:
        self.settings.set_enabled(module_id, row.get_active())
        self._persist()

    def _on_move(self, _button, module_id: str, delta: int) -> None:
        if self.settings.move(module_id, delta):
            self._persist()
            # Reconstruction différée : on ne détruit pas le bouton pendant son propre signal.
            GLib.idle_add(self._rebuild_once)

    def _on_reset(self, _button) -> None:
        self.settings.reset()
        self._persist()
        GLib.idle_add(self._rebuild_once)

    def _rebuild_once(self) -> bool:
        self._build_module_rows()
        return GLib.SOURCE_REMOVE


# Nom historique (v0.1) conservé pour les scripts et tests existants.
OverlayPage = HudPage


# ---- page Sources -----------------------------------------------------------------------------------------

class SectionsPage(Adw.PreferencesPage):
    """Page en lecture seule qui affiche une liste de textview.Section."""

    def __init__(self) -> None:
        super().__init__()
        self._groups: list[Adw.PreferencesGroup] = []

    def set_sections(self, sections: list[textview.Section]) -> None:
        for g in self._groups:
            self.remove(g)
        self._groups = []
        for sec in sections:
            group = Adw.PreferencesGroup(title=sec.title, description=sec.description)
            for r in sec.rows:
                row = Adw.ActionRow(use_markup=False, title=r.title, subtitle=r.subtitle)
                row.set_subtitle_selectable(True)
                group.add(row)
            self.add(group)
            self._groups.append(group)


# ---- fenêtre ------------------------------------------------------------------------------------------------

def _install_css(display: Gdk.Display) -> bool:
    translucent = bool(display.is_rgba() and display.is_composited())
    provider = Gtk.CssProvider()
    provider.load_from_string(theme.css(translucent))
    Gtk.StyleContext.add_provider_for_display(display, provider,
                                              Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
    return translucent


class CompanionWindow(Adw.ApplicationWindow):
    def __init__(self, app: Adw.Application) -> None:
        super().__init__(application=app, title="ULTIMA")
        self.set_default_size(720, 820)
        self.set_size_request(360, 480)
        self.add_css_class("ultima")

        self._toasts = Adw.ToastOverlay()
        self._banner = Adw.Banner(use_markup=False, revealed=False)

        self._stack = Adw.ViewStack()
        self._dashboard = DashboardPage(lambda: self._stack.set_visible_child_name("run"))
        self._run_page = RunPage()
        self._hud_page = HudPage(self._toast, self._rerender)
        self._status_page = SectionsPage()
        self._stack.add_titled_with_icon(self._dashboard, "tableau", "Tableau", "go-home-symbolic")
        self._stack.add_titled_with_icon(self._run_page, "run", "Run", "view-list-symbolic")
        self._stack.add_titled_with_icon(self._hud_page, "hud", "HUD", "emblem-system-symbolic")
        self._stack.add_titled_with_icon(self._status_page, "sources", "Sources",
                                         "network-transmit-receive-symbolic")

        header = Adw.HeaderBar()
        switcher = Adw.ViewSwitcher(stack=self._stack, policy=Adw.ViewSwitcherPolicy.WIDE)
        header.set_title_widget(switcher)
        bottom = Adw.ViewSwitcherBar(stack=self._stack)

        toolbar = Adw.ToolbarView()
        toolbar.add_top_bar(header)
        toolbar.add_top_bar(self._banner)
        toolbar.set_content(self._stack)
        toolbar.add_bottom_bar(bottom)
        self._toasts.set_child(toolbar)
        self.set_content(self._toasts)

        # Fenêtre étroite : onglets en bas, titre simple en haut.
        bp = Adw.Breakpoint.new(Adw.BreakpointCondition.parse("max-width: 560sp"))
        bp.add_setter(header, "title-widget", Adw.WindowTitle(title="ULTIMA"))
        bp.add_setter(bottom, "reveal", True)
        self.add_breakpoint(bp)

        self._watcher = StateWatcher(self.on_state)
        self.connect("close-request", self._on_close)
        self._watcher.start()

    def _toast(self, text: str) -> None:
        toast = Adw.Toast(title=text, timeout=2)
        toast.set_use_markup(False)
        self._toasts.add_toast(toast)

    def on_state(self, result: StateResult, daemon: str) -> None:
        msg = textview.banner_message(result, daemon)
        self._banner.set_title(msg or "")
        self._banner.set_revealed(msg is not None)
        self._dashboard.update(result, daemon)
        self._run_page.update(result)
        self._status_page.set_sections(
            textview.status_sections(result, daemon, self._hud_page.settings))

    def _rerender(self) -> None:
        last = self._watcher.last
        if last is not None:
            self.on_state(last, self._watcher.daemon)

    def _on_close(self, _win) -> bool:
        self._hud_page.flush()
        self._watcher.stop()
        return False


class CompanionApp(Adw.Application):
    def __init__(self) -> None:
        super().__init__(application_id=APP_ID, flags=Gio.ApplicationFlags.DEFAULT_FLAGS)
        self._window: CompanionWindow | None = None
        self.fonts_loaded: list[str] = []

    def do_startup(self) -> None:
        Adw.Application.do_startup(self)
        # Thème sombre imposé : l'identité est celle du jeu, pas celle du bureau.
        Adw.StyleManager.get_default().set_color_scheme(Adw.ColorScheme.FORCE_DARK)
        self.fonts_loaded = fonts.register()
        display = Gdk.Display.get_default()
        if display is not None:
            _install_css(display)

    def do_activate(self) -> None:
        if self._window is None:
            self._window = CompanionWindow(self)
        # present() uniquement sur action explicite de Mio (lancement / relancement).
        self._window.present()


__all__ = ["CompanionApp", "CompanionWindow", "DashboardPage", "HudPage", "ItemRow", "OverlayPage",
           "RunPage", "SectionsPage", "ipc"]
