"""L'interface s'importe sans erreur et sans ouvrir de fenêtre (aucun affichage disponible :
DISPLAY retiré, WAYLAND_DISPLAY inexistant, voir conftest). On vérifie aussi statiquement que chaque
classe Gtk/Adw utilisée existe dans les versions installées (GTK 4, libadwaita 1.9)."""

import ast
import os
import subprocess
import sys
from pathlib import Path

from conftest import COMPANION, NO_DISPLAY

UI = COMPANION / "ultima_companion" / "ui.py"


def gi_require_gdk():
    import gi
    gi.require_version("Gdk", "4.0")


def test_no_display_in_tests():
    assert "DISPLAY" not in os.environ and os.environ["WAYLAND_DISPLAY"] == NO_DISPLAY


def test_ui_imports_without_window():
    from ultima_companion import ui
    gi_require_gdk()
    from gi.repository import Adw, Gdk
    assert issubclass(ui.CompanionApp, Adw.Application)
    assert issubclass(ui.CompanionWindow, Adw.ApplicationWindow)
    assert issubclass(ui.OverlayPage, Adw.PreferencesPage)
    assert issubclass(ui.HudPage, Adw.PreferencesPage)
    from gi.repository import Gtk
    assert issubclass(ui.ItemRow, Gtk.ListBoxRow)
    assert issubclass(ui.DashboardPage, Gtk.Box) and issubclass(ui.RunPage, Gtk.Box)
    assert ui.APP_ID.count(".") >= 2
    # Aucun écran ouvert : sans GdkDisplay, aucune fenêtre ne peut exister.
    assert Gdk.Display.get_default() is None


def test_gtk_symbols_exist():
    from ultima_companion import ui  # noqa: F401
    from gi.repository import Adw, Gdk, Gtk, Gio, GLib, Pango
    mods = {"Adw": Adw, "Gdk": Gdk, "Gtk": Gtk, "Gio": Gio, "GLib": GLib, "Pango": Pango}
    tree = ast.parse(UI.read_text(encoding="utf-8"))
    used = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name) and node.value.id in mods:
            used.add((node.value.id, node.attr))
        # Attributs à deux niveaux : Gtk.Align.CENTER, Adw.ViewSwitcherPolicy.WIDE…
        if (isinstance(node, ast.Attribute) and isinstance(node.value, ast.Attribute)
                and isinstance(node.value.value, ast.Name) and node.value.value.id in mods):
            owner = getattr(mods[node.value.value.id], node.value.attr)
            assert hasattr(owner, node.attr), f"{node.value.value.id}.{node.value.attr}.{node.attr}"
    missing = [f"{m}.{a}" for m, a in used if not hasattr(mods[m], a)]
    assert not missing, missing
    # méthodes appelées sur les widgets
    for cls, meth in [(Adw.ActionRow, "set_subtitle_selectable"), (Adw.PreferencesGroup, "set_header_suffix"),
                      (Adw.PreferencesGroup, "remove"), (Adw.PreferencesPage, "remove"),
                      (Adw.SwitchRow, "add_suffix"), (Adw.SwitchRow, "set_active"),
                      (Adw.ViewStack, "add_titled_with_icon"), (Adw.ToolbarView, "add_top_bar"),
                      (Adw.ToolbarView, "set_content"), (Adw.Banner, "set_revealed"),
                      (Adw.Toast, "set_use_markup"), (Adw.ToastOverlay, "add_toast"),
                      (Adw.ToggleGroup, "add"), (Adw.ToggleGroup, "set_active_name"),
                      (Adw.ToggleGroup, "get_active_name"), (Adw.ActionRow, "add_prefix"),
                      (Adw.ToolbarView, "add_bottom_bar"), (Adw.ApplicationWindow, "add_breakpoint"),
                      (Adw.Breakpoint, "add_setter"), (Adw.StyleManager, "set_color_scheme"),
                      (Gtk.CssProvider, "load_from_string"), (Gtk.Label, "set_natural_wrap_mode"),
                      (Gtk.Revealer, "set_reveal_child"), (Gtk.Scale, "set_draw_value"),
                      (Gdk.Display, "is_rgba"), (Gdk.Display, "is_composited")]:
        assert hasattr(cls, meth), f"{cls.__name__}.{meth}"
    # propriétés passées en kwargs
    for cls, prop in [(Adw.ActionRow, "use-markup"), (Adw.SwitchRow, "use-markup"),
                      (Adw.Banner, "use-markup"), (Adw.ViewSwitcher, "policy"),
                      (Adw.PreferencesGroup, "description"), (Adw.Toast, "timeout"),
                      (Adw.Toggle, "name"), (Adw.Toggle, "label"), (Adw.Toggle, "tooltip"),
                      (Adw.ViewSwitcherBar, "reveal"), (Adw.HeaderBar, "title-widget"),
                      (Adw.Clamp, "tightening-threshold"), (Adw.StatusPage, "description"),
                      (Gtk.Revealer, "transition-duration")]:
        assert cls.find_property(prop) is not None, f"{cls.__name__}:{prop}"


def _run(args, env):
    return subprocess.run([sys.executable, str(COMPANION / "ultima-companion"), *args],
                          capture_output=True, text=True, env=env, timeout=20)


def test_launcher_text_modes(isolated_xdg, sample_path):
    env = dict(os.environ)
    r = _run(["--version"], env)
    assert r.returncode == 0 and r.stdout.startswith("ultima-companion ")
    r = _run(["--paths"], env)
    assert str(isolated_xdg["config"]) in r.stdout and str(isolated_xdg["runtime"]) in r.stdout
    r = _run(["--check"], env)
    assert r.returncode == 0 and "Daemon non démarré" in r.stdout
    (isolated_xdg["runtime"] / "ultima").mkdir()
    (isolated_xdg["runtime"] / "ultima" / "state.json").write_bytes(sample_path.read_bytes())
    r = _run(["--check"], env)
    assert r.returncode == 0, r.stderr
    assert "Écran courant : Boutique (Shop)" in r.stdout and "0. deck activé" in r.stdout
    # --check est en lecture seule : aucun settings.json créé
    assert not (isolated_xdg["config"] / "ultima").exists()


def test_desktop_file_not_installed():
    desktop = COMPANION / "ultima-companion.desktop"
    assert desktop.exists()
    assert not (Path.home() / ".local/share/applications/ultima-companion.desktop").exists()
