#!/usr/bin/env python3
"""Substitut du jeu pour le spike S1 (moteur A) : une fenetre X11 (XWayland) PLEIN ECRAN,
comme Godot 4.5 en X11. Elle journalise chaque clic et chaque entree/sortie du pointeur avec
ses coordonnees : si un clic dans la zone de l'overlay ULTIMA apparait ici, le click-through
est prouve (l'overlay n'a rien avale).

Usage : GDK_BACKEND=x11 python3 -I fake-fullscreen-game.py [--seconds N]
Couleurs : fond #241F1A et grille #2B3152 (StsColors, docs/17 §1.3) ; c'est un banc d'essai, pas l'UI.
"""
import os
import sys
import time

os.environ["GDK_BACKEND"] = "x11"  # force XWayland, comme le jeu
import gi  # noqa: E402

gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
gi.require_version("GdkX11", "3.0")
from gi.repository import Gdk, GdkX11, GLib, Gtk  # noqa: E402,F401

T0 = time.monotonic()
clicks = []


def log(msg):
    print(f"{time.strftime('%H:%M:%S')} [substitut] {msg}", flush=True)


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def on_draw(widget, cr):
    w, h = widget.get_allocated_width(), widget.get_allocated_height()
    cr.set_source_rgb(*hexrgb("#241F1A"))
    cr.paint()
    cr.set_source_rgb(*hexrgb("#2B3152"))
    cr.set_line_width(1)
    for x in range(0, w, 80):
        cr.move_to(x + 0.5, 0)
        cr.line_to(x + 0.5, h)
    for y in range(0, h, 80):
        cr.move_to(0, y + 0.5)
        cr.line_to(w, y + 0.5)
    cr.stroke()
    cr.set_source_rgb(*hexrgb("#FFF6E2"))
    cr.select_font_face("Sans")
    cr.set_font_size(42)
    cr.move_to(80, 120)
    cr.show_text("SUBSTITUT DU JEU - X11 PLEIN ECRAN")
    cr.set_font_size(22)
    cr.move_to(80, 170)
    cr.show_text(f"{w}x{h}  -  cliquez sur le panneau ULTIMA a droite : le clic doit s'afficher ici")
    cr.move_to(80, 210)
    cr.show_text(f"clics recus : {len(clicks)}" + (f"  dernier : {clicks[-1]}" if clicks else ""))
    for (x, y) in clicks[-10:]:
        cr.set_source_rgb(*hexrgb("#FF5555"))
        cr.arc(x, y, 8, 0, 6.2832)
        cr.fill()
    return False


def on_press(widget, ev):
    clicks.append((int(ev.x_root), int(ev.y_root)))
    log(f"CLIC bouton={ev.button} x={int(ev.x_root)} y={int(ev.y_root)}")
    widget.queue_draw()
    return True


def on_cross(widget, ev):
    kind = "ENTREE" if ev.type == Gdk.EventType.ENTER_NOTIFY else "SORTIE"
    log(f"pointeur {kind} x={int(ev.x_root)} y={int(ev.y_root)} detail={ev.detail.value_nick}")
    return False


def on_state(widget, ev):
    full = bool(ev.new_window_state & Gdk.WindowState.FULLSCREEN)
    focus = bool(ev.new_window_state & Gdk.WindowState.FOCUSED)
    log(f"etat fenetre : fullscreen={full} focused={focus}")
    return False


def on_focus(widget, ev, inside):
    log(f"focus {'GAGNE' if inside else 'PERDU'}")
    return False


def main():
    secs = None
    if "--seconds" in sys.argv:
        secs = float(sys.argv[sys.argv.index("--seconds") + 1])
    win = Gtk.Window(title="ULTIMA substitut jeu (X11 plein ecran)")
    win.set_wmclass("ultima-substitut", "UltimaSubstitut")
    area = Gtk.DrawingArea()
    area.connect("draw", on_draw)
    win.add(area)
    win.add_events(Gdk.EventMask.BUTTON_PRESS_MASK | Gdk.EventMask.ENTER_NOTIFY_MASK
                   | Gdk.EventMask.LEAVE_NOTIFY_MASK)
    win.connect("button-press-event", on_press)
    win.connect("enter-notify-event", on_cross)
    win.connect("leave-notify-event", on_cross)
    win.connect("window-state-event", on_state)
    win.connect("focus-in-event", on_focus, True)
    win.connect("focus-out-event", on_focus, False)
    win.connect("destroy", Gtk.main_quit)
    win.connect("key-press-event", lambda w, e: Gtk.main_quit() if e.keyval == Gdk.KEY_Escape else False)
    win.fullscreen()  # _NET_WM_STATE_FULLSCREEN, comme Godot en plein ecran
    win.show_all()
    xid = win.get_window().get_xid()
    log(f"xid=0x{xid:x} pid={os.getpid()} DISPLAY={os.environ.get('DISPLAY')}")
    if secs:
        GLib.timeout_add(int(secs * 1000), Gtk.main_quit)
    Gtk.main()
    log(f"fin ({len(clicks)} clics)")


if __name__ == "__main__":
    main()
