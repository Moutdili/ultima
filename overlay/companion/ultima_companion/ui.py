"""Interface GTK4 + libadwaita 1.9 — phase design ouverte (Mio, 2026-10-29), skill sts2-design-system §8-§12.

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

from . import appearance as appearance_mod
from . import fonts, ipc, settings as settings_mod, textview, theme, themes, tones, views
from .state import OK, StateResult
from .watch import StateWatcher

APP_ID = "io.github.mio.UltimaCompanion"

SHORTCUTS = (
    ("Super+Maj+I", "épingler / déplier"),
    ("Super+Maj+O", "masquer / afficher"),
)
SAVE_DELAY_MS = 350   # curseurs : une seule écriture quand Mio relâche
# Détail (niveau 2) : dévoilement bref, sans rebond, jamais en boucle (skill §4 : 150-220 ms, §12).
DETAIL_REVEAL_MS = 160


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
                                          transition_duration=DETAIL_REVEAL_MS)
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


def _custom_block(block: views.Group) -> Gtk.Box:
    """Un module perso/plugin dessiné comme les autres blocs ; santé ≠ ok = un mot, jamais la couleur seule."""
    box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
    head = Gtk.Box(spacing=8)
    head.append(_label(block.title, "heading-group", ellipsize=True))
    if block.source == "plugin":
        head.append(_label("plugin", "secondary"))
    if block.note:
        head.append(_label(block.note, "secondary", ellipsize=True))
    box.append(head)
    if block.items:
        box.append(item_list(block.items))
    else:
        box.append(_label(block.empty or "Aucune ligne publiée", "empty"))
    if block.dim:
        box.add_css_class("dim")
    return box


def _module_block(block: views.Group) -> Gtk.Box:
    """Bloc d'un module intégré (même grammaire que le HUD) ; note discrète si désactivé pour le HUD."""
    box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
    head = Gtk.Box(spacing=8)
    title = _label(block.title.upper(), "heading-group", ellipsize=True)
    tone = {"curses": "curse", "enchanted": "enchant"}.get(block.key)   # §9 : la couleur double le mot
    if tone:
        title.add_css_class(theme.tone_class(tone))
    head.append(title)
    if block.note:
        head.append(_label(block.note, "secondary", ellipsize=True))
    box.append(head)
    if block.items:
        box.append(item_list(block.items))
    else:
        box.append(_label(block.empty, "empty"))
    if block.dim:
        box.add_css_class("dim")
    return box


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

    def update(self, result: StateResult, daemon: str, order: list[str] | None = None,
               custom: list[views.Group] | None = None) -> None:
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

        # Modules perso/plugins publiés par le daemon (EngineState.Custom) : mêmes blocs que la
        # page Run, mais seulement sur les écrans qu'ils déclarent (`screens`).
        for block in custom or []:
            self._col.append(_custom_block(block))

        if st is not None and st.run is not None:
            note = views.deck_note(st)
            row = Gtk.Box(spacing=12)
            row.append(grade_badge(note.grade))
            row.append(_label(views.deck_summary(st, order), "secondary", ellipsize=True, xalign=0.0))
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

    def update(self, result: StateResult, order: list[str] | None = None,
               enabled: dict[str, bool] | None = None,
               labels: dict[str, str] | None = None) -> None:
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
        if note is None:
            return
        for block in views.run_blocks(st, order, enabled, labels):
            if block.kind == "deck_score":
                self._col.append(self._note_panel(note))
            elif block.kind == "custom":
                self._col.append(_custom_block(block))
            else:
                self._col.append(_module_block(block))

    @staticmethod
    def _note_panel(note: views.DeckNote) -> Gtk.Box:
        """La note du deck : bloc normal, à SA place dans l'ordre des réglages (décision de Mio)."""
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
        return panel


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
        self._custom_labels: dict[str, str] = {}

        # -- Apparence
        look = Adw.PreferencesGroup(title="Apparence")
        reset_hud = Gtk.Button(label="Par défaut", valign=Gtk.Align.CENTER)
        reset_hud.add_css_class("flat")
        reset_hud.set_tooltip_text(settings_mod.HudSettings.default().summary())
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
            row = Adw.SwitchRow(use_markup=False,
                                title=settings_mod.module_label(m.id, self._custom_labels),
                                subtitle=self._module_subtitle(m))
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

    def _module_subtitle(self, m: settings_mod.ModuleSetting) -> str:
        parts = [settings_mod.module_hint(m.id)]
        if m.screens:
            parts.append("écrans : " + ", ".join(m.screens))
        return " · ".join(p for p in parts if p)

    def set_custom_labels(self, labels: dict[str, str]) -> None:
        """Libellés publiés par le daemon pour les modules `x.*` (les réglages n'ont que l'id)."""
        if labels == self._custom_labels:
            return
        self._custom_labels = dict(labels)
        GLib.idle_add(self._rebuild_once)

    def reload_if_external(self, result: StateResult) -> bool:
        """Réglages modifiés ailleurs (TUI `ultima`) : le daemon republie des réglages différents des nôtres
        → on relit settings.json et on resynchronise les contrôles. Une écriture en attente est prioritaire."""
        st = result.state if result.kind == OK else None
        if self._save_timer is not None or st is None or st.settings is None \
                or st.settings.same_applied(self.settings):
            return False
        fresh = settings_mod.reload_if_changed(self.settings)
        if fresh is None:
            return False
        self.settings = fresh
        self._sync_hud_widgets()
        GLib.idle_add(self._rebuild_once)
        return True

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


# ---- page Apparence (éditeur minimal, docs/44 §11) ---------------------------------------------------------

TONE_LABELS = {
    "text": "Texte", "muted": "Texte secondaire", "accent": "Accent", "danger": "Danger",
    "ok": "Réussi", "warn": "Avertissement", "curse": "Malédiction", "enchant": "Enchantement",
    "info": "Info",
    "grade.S": "Note S", "grade.A": "Note A", "grade.B": "Note B", "grade.C": "Note C",
    "grade.D": "Note D", "grade.F": "Note F",
    "background": "Fond", "border": "Liseré",
}


class AppearancePage(Adw.PreferencesPage):
    """Éditeur minimal du compagnon : thème + aperçu, tons, police/taille.

    Une seule configuration par surface (docs/44 §1) : le compagnon n'écrit que
    `appearance.companion` ; le HUD et la console sont en LECTURE avec la commande `ultima`
    équivalente. Chaque changement est écrit atomiquement, sans toucher aux autres clés.
    """

    def __init__(self, on_message, on_changed, state_provider=None) -> None:
        super().__init__()
        self._on_message = on_message
        self._on_changed = on_changed
        self._state_provider = state_provider
        self._save_timer: int | None = None
        self._syncing = False
        self._catalog = themes.load_catalog()
        self._resolved: appearance_mod.ResolvedAppearance | None = None
        self.appearance = appearance_mod.read_companion()
        self._sig = self._signature()
        self._theme_ids: list[str] = []

        # -- Thème
        group = Adw.PreferencesGroup(
            title="Thème",
            description="« auto-perso » suit le personnage de la run (repli « sts » hors run). "
                        "Un thème cassé retombe sur « sts » avec un message, jamais une fenêtre vide.")
        self._theme_row = Adw.ComboRow(title="Thème du compagnon")
        self._theme_row.connect("notify::selected", self._on_theme)
        group.add(self._theme_row)
        self._theme_status = Adw.ActionRow(title="Thème appliqué")
        self._theme_status.set_subtitle_selectable(True)
        group.add(self._theme_status)
        self.add(group)

        # -- Aperçu (recoloré par le CSS du thème, aucun widget à reconstruire)
        preview = Adw.PreferencesGroup(title="Aperçu")
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        box.add_css_class("glass")
        box.set_margin_top(6)
        box.set_margin_bottom(6)
        box.set_margin_start(6)
        box.set_margin_end(6)
        box.append(_label("Exemple de bloc", "heading-page"))
        grades = Gtk.Box(spacing=8)
        for grade in ("S", "A", "B", "C", "D", "F", views.DASH):
            grades.append(grade_badge(grade))
        box.append(grades)
        words = Gtk.Box(spacing=12)
        for tone in ("accent", "danger", "ok", "warn", "curse", "enchant", "info", "muted"):
            words.append(_label(TONE_LABELS[tone], theme.tone_class(tone) or ""))
        box.append(words)
        box.append(_label("Texte secondaire · coût 1 · 12 or", "secondary"))
        preview.add(box)
        self.add(preview)

        # -- Tons
        tones_group = Adw.PreferencesGroup(
            title="Tons",
            description="Surcharges au-dessus du thème (#RRGGBB ou #RRGGBBAA). Vide = couleur du thème.")
        reset = Gtk.Button(label="Réinitialiser")
        reset.add_css_class("flat")
        reset.set_tooltip_text("Retirer toutes les surcharges de tons du compagnon")
        reset.connect("clicked", self._on_reset_tones)
        tones_group.set_header_suffix(reset)
        self._tone_rows: dict[str, tuple[Adw.ActionRow, Gtk.Entry]] = {}
        for tone in tones.ALL:
            row = Adw.ActionRow(title=TONE_LABELS.get(tone, tone))
            swatch = Gtk.Box(valign=Gtk.Align.CENTER)
            swatch.add_css_class("swatch")
            swatch.add_css_class(theme.tone_class(tone) or "")
            entry = Gtk.Entry(placeholder_text="#RRGGBB", width_chars=10, valign=Gtk.Align.CENTER)
            entry.connect("activate", self._on_tone, tone)
            row.add_suffix(swatch)
            row.add_suffix(entry)
            tones_group.add(row)
            self._tone_rows[tone] = (row, entry)
        self.add(tones_group)

        # -- Police
        font_group = Adw.PreferencesGroup(
            title="Police",
            description="Cherchée dans fonts/{perso,jeu,base,systeme} puis fontconfig ; introuvable : "
                        "police du thème, puis police système.")
        self._font_row = Adw.EntryRow(title="Famille")
        self._font_row.set_show_apply_button(True)
        self._font_row.connect("apply", self._on_font)
        font_group.add(self._font_row)
        size_row = Adw.ActionRow(title="Taille", subtitle="12 à 24 px")
        self._size = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL,
                                              appearance_mod.MIN_FONT_SIZE,
                                              appearance_mod.MAX_FONT_SIZE, 1)
        self._size.set_draw_value(False)
        self._size.set_size_request(200, -1)
        self._size.set_valign(Gtk.Align.CENTER)
        self._size_label = _label("", "value", xalign=1.0)
        self._size_label.set_width_chars(5)
        self._size.connect("value-changed", self._on_size)
        size_row.add_suffix(self._size)
        size_row.add_suffix(self._size_label)
        font_group.add(size_row)
        self._font_info = Adw.ActionRow(title="Police retenue")
        self._font_info.set_subtitle_selectable(True)
        font_group.add(self._font_info)
        self.add(font_group)

        # -- Autres surfaces : LECTURE + commande `ultima` équivalente
        self._surfaces_group = Adw.PreferencesGroup(
            title="HUD et console (lecture seule)",
            description="Chaque surface a son apparence (docs/44 §1) ; ces deux-là se règlent depuis "
                        "le TUI. La commande équivalente est affichée.")
        self._surface_rows: list[Adw.ActionRow] = []
        for _info in [i for i in appearance_mod.surfaces() if not i.editable]:
            row = Adw.ActionRow(title="")
            row.set_subtitle_selectable(True)
            self._surfaces_group.add(row)
            self._surface_rows.append(row)
        self.add(self._surfaces_group)

        self._rebuild_theme_model()
        self._sync_widgets()
        self._refresh_surfaces()

    # ---- état / signature du fichier ----------------------------------------------------------------
    @staticmethod
    def _signature():
        try:
            st = settings_mod.paths.settings_path().stat()
            return (st.st_mtime_ns, st.st_size)
        except OSError:
            return None

    def _theme_text(self, theme_id: str) -> str:
        if theme_id == themes.AUTO_ID:
            return "auto-perso — thème du personnage"
        theme = self._catalog.themes.get(theme_id)
        return f"{theme_id} — {theme.label}" if theme is not None else f"{theme_id} — introuvable"

    def _rebuild_theme_model(self) -> None:
        previous = self._syncing
        self._syncing = True
        try:
            ids = [themes.AUTO_ID] + [t.id for t in themes.available(self._catalog)]
            if self.appearance.theme not in ids:
                ids.append(self.appearance.theme)
            self._theme_ids = ids
            self._theme_row.set_model(Gtk.StringList.new([self._theme_text(t) for t in ids]))
            self._select_theme()
        finally:
            self._syncing = previous

    def _select_theme(self) -> None:
        try:
            index = self._theme_ids.index(self.appearance.theme)
        except ValueError:
            index = 0
        self._theme_row.set_selected(index)

    def _sync_widgets(self) -> None:
        self._syncing = True
        try:
            self._select_theme()
            self._font_row.set_text(self.appearance.font_family or "")
            size = self.appearance.font_size or appearance_mod.DEFAULT_FONT_SIZE
            self._size.set_value(size)
            self._size_label.set_label(f"{size} px")
            for tone, (_row, entry) in self._tone_rows.items():
                entry.set_text(self.appearance.tones.get(tone, ""))
            self._update_status()
        finally:
            self._syncing = False

    def _update_status(self) -> None:
        resolved = self._resolved
        if resolved is None:
            self._theme_status.set_subtitle("—")
            return
        line = f"{resolved.theme_id} — {resolved.theme_label}"
        if resolved.fallback:
            line += " (repli : le thème demandé n'a pas pu être chargé)"
        if resolved.messages:
            line += " · " + " · ".join(resolved.messages)
        elif resolved.warnings:
            line += " · " + " · ".join(resolved.warnings[:4])
        self._theme_status.set_subtitle(line)
        font = resolved.font_summary
        if resolved.font_message:
            font += f" · {resolved.font_message}"
        self._font_info.set_subtitle(font)

    def show_resolution(self, resolved: appearance_mod.ResolvedAppearance) -> None:
        self._resolved = resolved
        self._update_status()

    def _refresh_surfaces(self) -> None:
        infos = [i for i in appearance_mod.surfaces() if not i.editable]
        for row, info in zip(self._surface_rows, infos):
            row.set_title(info.title)
            row.set_subtitle(f"{info.theme} · {info.font} — commande : {info.theme_command}")

    def reload_if_external(self) -> bool:
        """`settings.json` modifié par le TUI : on relit `appearance.companion` sans rien écraser."""
        sig = self._signature()
        if sig == self._sig:
            return False
        self._sig = sig
        fresh = appearance_mod.read_companion()
        if fresh.to_json() == self.appearance.to_json():
            return False
        self.appearance = fresh
        self._rebuild_theme_model()
        self._sync_widgets()
        self._refresh_surfaces()
        return True

    # ---- écriture -----------------------------------------------------------------------------------
    def _save(self) -> None:
        if self._save_timer is not None:
            GLib.source_remove(self._save_timer)
            self._save_timer = None
        try:
            path = appearance_mod.save_companion(self.appearance)
        except appearance_mod.AppearanceWriteError as e:
            self._on_message(str(e))
            return
        self._sig = self._signature()
        self._on_message(f"Apparence enregistrée ({path.name})")
        if self._on_changed is not None:
            self._on_changed()

    def _save_later(self) -> None:
        if self._save_timer is not None:
            GLib.source_remove(self._save_timer)
        self._save_timer = GLib.timeout_add(SAVE_DELAY_MS, self._save_timeout)

    def _save_timeout(self) -> bool:
        self._save_timer = None
        self._save()
        return GLib.SOURCE_REMOVE

    def flush(self) -> None:
        if self._save_timer is not None:
            self._save()

    # ---- actions --------------------------------------------------------------------------------------
    def _on_theme(self, row: Adw.ComboRow, _pspec) -> None:
        if self._syncing:
            return
        index = row.get_selected()
        if 0 <= index < len(self._theme_ids):
            self.appearance.set_theme(self._theme_ids[index])
            self._save()

    def _on_tone(self, entry: Gtk.Entry, tone: str) -> None:
        text = entry.get_text().strip()
        if not text:
            self.appearance.clear_tone(tone)
            self._save()
            return
        if not self.appearance.set_tone(tone, text):
            self._on_message(f"« {text} » n'est pas une couleur #RRGGBB ou #RRGGBBAA")
            return
        self._save()

    def _on_reset_tones(self, _button) -> None:
        self.appearance.tones = {}
        self._sync_widgets()
        self._save()

    def _on_font(self, row: Adw.EntryRow) -> None:
        self.appearance.set_font_family(row.get_text())
        self._save()

    def _on_size(self, scale: Gtk.Scale) -> None:
        value = int(round(scale.get_value()))
        self._size_label.set_label(f"{value} px")
        if self._syncing:
            return
        self.appearance.set_font_size(value)
        self._save_later()


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

def _install_css(display: Gdk.Display, resolved, provider: Gtk.CssProvider | None = None) -> Gtk.CssProvider:
    """Installe (ou met à jour) la feuille de style du thème sur l'affichage."""
    translucent = bool(display.is_rgba() and display.is_composited())
    if provider is None:
        provider = Gtk.CssProvider()
        Gtk.StyleContext.add_provider_for_display(display, provider,
                                                  Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
    assert provider is not None
    provider.load_from_string(theme.css(resolved, translucent))
    return provider


class CompanionWindow(Adw.ApplicationWindow):
    def __init__(self, app: Adw.Application) -> None:
        super().__init__(application=app, title="ULTIMA")
        self.set_default_size(720, 820)
        self.set_size_request(360, 480)
        self.add_css_class("ultima")

        self._provider: Gtk.CssProvider | None = None
        self._resolved = None
        self._character: str | None = None

        self._toasts = Adw.ToastOverlay()
        self._banner = Adw.Banner(use_markup=False, revealed=False)

        self._stack = Adw.ViewStack()
        self._dashboard = DashboardPage(lambda: self._stack.set_visible_child_name("run"))
        self._run_page = RunPage()
        self._hud_page = HudPage(self._toast, self._rerender)
        self._appearance_page = AppearancePage(self._toast, self._apply_appearance,
                                               self._current_state)
        self._status_page = SectionsPage()
        self._stack.add_titled_with_icon(self._dashboard, "tableau", "Tableau", "go-home-symbolic")
        self._stack.add_titled_with_icon(self._run_page, "run", "Run", "view-list-symbolic")
        self._stack.add_titled_with_icon(self._hud_page, "hud", "HUD", "emblem-system-symbolic")
        self._stack.add_titled_with_icon(self._appearance_page, "apparence", "Apparence",
                                         "applications-graphics-symbolic")
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
        # Thème et police dès l'ouverture (avant le premier état publié).
        self._apply_appearance()
        self._watcher.start()

    def _toast(self, text: str) -> None:
        toast = Adw.Toast(title=text, timeout=2)
        toast.set_use_markup(False)
        self._toasts.add_toast(toast)

    def _current_state(self):
        last = self._watcher.last if getattr(self, "_watcher", None) is not None else None
        return last.state if last is not None and last.kind == OK else None

    def _apply_appearance(self) -> None:
        """Résout `appearance.companion`, réinstalle le CSS et met à jour le bandeau."""
        page = getattr(self, "_appearance_page", None)
        if page is None:                      # signaux émis pendant la construction de la page
            return
        state = self._current_state()
        self._character = state.run.character_id if state is not None and state.run is not None else None
        resolved = appearance_mod.resolve(page.appearance, self._character)
        self._resolved = resolved
        display = Gdk.Display.get_default()
        if display is not None:
            self._provider = _install_css(display, resolved, self._provider)
        fonts.register([resolved.font_file] if resolved.font_file else [])
        self._appearance_page.show_resolution(resolved)
        self._refresh_banner()

    def _refresh_banner(self) -> None:
        parts = []
        watcher = getattr(self, "_watcher", None)
        if watcher is not None and watcher.last is not None:
            message = textview.banner_message(watcher.last, watcher.daemon)
            if message:
                parts.append(message)
        if self._resolved is not None:
            message = textview.appearance_message(self._resolved)
            if message:
                parts.append(message)
        text = " · ".join(parts)
        self._banner.set_title(text)
        self._banner.set_revealed(bool(text))

    def on_state(self, result: StateResult, daemon: str) -> None:
        self._hud_page.reload_if_external(result)
        if self._appearance_page.reload_if_external():
            self._apply_appearance()           # le TUI a changé l'apparence du compagnon
        state = result.state if result.kind == OK else None
        order = self._hud_page.settings.ids()   # même ordre que le HUD (§11), modifié en direct
        enabled = {m.id: m.enabled for m in self._hud_page.settings.modules}
        labels: dict[str, str] = {}
        blocks: list[views.Group] = []
        if state is not None:
            labels = views.custom_labels(state)
            self._hud_page.set_custom_labels(labels)
            blocks = views.custom_groups(state, order, screen=state.screen)
        self._dashboard.update(result, daemon, order, blocks)
        self._run_page.update(result, order, enabled, labels)
        self._status_page.set_sections(
            textview.status_sections(result, daemon, self._hud_page.settings))
        # `auto-perso` suit le personnage : on ne réapplique que s'il a changé.
        character = state.run.character_id if state is not None and state.run is not None else None
        if (self._appearance_page.appearance.theme == themes.AUTO_ID
                and character != self._character):
            self._apply_appearance()
        self._refresh_banner()

    def _rerender(self) -> None:
        last = self._watcher.last
        if last is not None:
            self.on_state(last, self._watcher.daemon)

    def _on_close(self, _win) -> bool:
        self._hud_page.flush()
        self._appearance_page.flush()
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

    def do_activate(self) -> None:
        if self._window is None:
            self._window = CompanionWindow(self)
        # present() uniquement sur action explicite de Mio (lancement / relancement).
        self._window.present()


__all__ = ["AppearancePage", "CompanionApp", "CompanionWindow", "DashboardPage", "HudPage",
           "ItemRow", "OverlayPage", "RunPage", "SectionsPage", "ipc"]
