"""Interface GTK4 + libadwaita (fonctionnelle, texte brut — design en dernier, décision Mio).

L'import de ce module n'ouvre aucune fenêtre : seules des classes sont définies. La fenêtre n'est
créée que dans `CompanionApp.do_activate`, c'est-à-dire quand Mio lance `ultima-companion`.
Elle ne se met jamais au premier plan d'elle-même (aucun present() sur mise à jour d'état).
"""

from __future__ import annotations

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, Gio, GLib, Gtk  # noqa: E402

from . import ipc, settings as settings_mod, textview  # noqa: E402
from .state import StateResult  # noqa: E402
from .watch import StateWatcher  # noqa: E402

APP_ID = "io.github.mio.UltimaCompanion"

SHORTCUTS = (
    ("Super+Maj+I", "Épingler / déplier l'overlay"),
    ("Super+Maj+O", "Masquer / afficher l'overlay"),
)


def _row(title: str, subtitle: str = "") -> Adw.ActionRow:
    row = Adw.ActionRow(use_markup=False, title=title, subtitle=subtitle)
    row.set_subtitle_selectable(True)
    return row


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
                group.add(_row(r.title, r.subtitle))
            self.add(group)
            self._groups.append(group)


class OverlayPage(Adw.PreferencesPage):
    """Modules de l'overlay : activer/désactiver, monter/descendre, valeurs par défaut.

    Chaque modification est écrite immédiatement (écriture atomique) dans settings.json ;
    le daemon surveille ce fichier et republie l'état avec les nouveaux réglages.
    """

    def __init__(self, on_message, on_saved=None) -> None:
        super().__init__()
        self._on_message = on_message
        self._on_saved = on_saved
        loaded = settings_mod.load()
        self.settings = loaded.settings
        self._module_rows: list[Adw.SwitchRow] = []

        self._modules_group = Adw.PreferencesGroup(
            title="Modules (ordre d'affichage)",
            description="Les modules récompense, boutique et chemins n'apparaissent que sur l'écran "
                        "correspondant. Enregistrement immédiat.")
        reset = Gtk.Button(label="Valeurs par défaut", valign=Gtk.Align.CENTER)
        reset.set_tooltip_text("Ordre de Mio : deck, enchantées, malédictions, reliques, potions…")
        reset.connect("clicked", self._on_reset)
        self._modules_group.set_header_suffix(reset)
        self.add(self._modules_group)
        self._build_module_rows()

        shortcuts = Adw.PreferencesGroup(
            title="Raccourcis de l'overlay",
            description="Définis par l'extension GNOME (non modifiables ici).")
        for keys, what in SHORTCUTS:
            shortcuts.add(_row(keys, what))
        self.add(shortcuts)

        file_group = Adw.PreferencesGroup(title="Fichier de réglages")
        file_group.add(_row("Chemin", str(settings_mod.paths.settings_path())))
        if not loaded.exists:
            file_group.add(_row("État", "absent : valeurs par défaut (créé à la première modification)"))
        elif loaded.error:
            file_group.add(_row("État", loaded.error))
        else:
            file_group.add(_row("État", "lu"))
        self.add(file_group)

    # ---- lignes des modules (le groupe reste en place, seules ses lignes sont recréées) -------
    def _build_module_rows(self) -> None:
        for row in self._module_rows:
            self._modules_group.remove(row)
        self._module_rows = []
        count = len(self.settings.modules)
        for i, m in enumerate(self.settings.modules):
            row = Adw.SwitchRow(use_markup=False, title=f"{i + 1}. {settings_mod.module_label(m.id)}",
                                subtitle=f"id : {m.id}")
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

            row.add_suffix(up)
            row.add_suffix(down)
            self._modules_group.add(row)
            self._module_rows.append(row)

    # ---- actions --------------------------------------------------------------------------
    def _persist(self) -> None:
        try:
            path = settings_mod.save(self.settings)
        except OSError as e:
            self._on_message(f"Échec de l'écriture des réglages : {e}")
            return
        self._on_message(f"Réglages enregistrés ({path.name})")
        if self._on_saved is not None:
            self._on_saved()

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


class CompanionWindow(Adw.ApplicationWindow):
    def __init__(self, app: Adw.Application) -> None:
        super().__init__(application=app, title="ULTIMA — Compagnon")
        self.set_default_size(560, 760)

        self._toasts = Adw.ToastOverlay()
        self._banner = Adw.Banner(use_markup=False, revealed=False)

        self._stack = Adw.ViewStack()
        self._status_page = SectionsPage()
        self._overlay_page = OverlayPage(self._toast, self._rerender)
        self._run_page = SectionsPage()
        self._stack.add_titled_with_icon(self._status_page, "etat", "État", "dialog-information-symbolic")
        self._stack.add_titled_with_icon(self._overlay_page, "overlay", "Overlay", "view-list-symbolic")
        self._stack.add_titled_with_icon(self._run_page, "run", "Run", "view-grid-symbolic")

        header = Adw.HeaderBar()
        header.set_title_widget(Adw.ViewSwitcher(stack=self._stack, policy=Adw.ViewSwitcherPolicy.WIDE))

        toolbar = Adw.ToolbarView()
        toolbar.add_top_bar(header)
        toolbar.add_top_bar(self._banner)
        toolbar.set_content(self._stack)
        self._toasts.set_child(toolbar)
        self.set_content(self._toasts)

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
        self._status_page.set_sections(
            textview.status_sections(result, daemon, self._overlay_page.settings))
        self._run_page.set_sections(textview.run_sections(result))

    def _rerender(self) -> None:
        last = self._watcher.last
        if last is not None:
            self.on_state(last, self._watcher.daemon)

    def _on_close(self, _win) -> bool:
        self._watcher.stop()
        return False


class CompanionApp(Adw.Application):
    def __init__(self) -> None:
        super().__init__(application_id=APP_ID, flags=Gio.ApplicationFlags.DEFAULT_FLAGS)
        self._window: CompanionWindow | None = None

    def do_activate(self) -> None:
        if self._window is None:
            self._window = CompanionWindow(self)
        # present() uniquement sur action explicite de Mio (lancement / relancement).
        self._window.present()


__all__ = ["CompanionApp", "CompanionWindow", "OverlayPage", "SectionsPage", "ipc"]
