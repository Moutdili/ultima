#!/usr/bin/env python3
"""fake-fullscreen.py — substitut du jeu : fenêtre X11 (XWayland) plein écran avec une mire
colorée et un compteur de clics/touches reçus.

But : permettre aux Verriers (spike S1) de prouver, SANS lancer le jeu, qu'un overlay
  1. est visible au-dessus d'une fenêtre X11 plein écran (capture-composed.py),
  2. laisse passer les clics (le compteur de cette fenêtre augmente quand on clique
     « à travers » l'overlay ; il ne doit PAS augmenter quand on clique sur une zone
     volontairement non click-through),
  3. ne vole pas le focus (la ligne « focus » reste OUI ; les touches arrivent ici).

Forcé en X11 (GDK_BACKEND=x11) comme le jeu (Godot via XWayland).

État machine-lisible : --state FILE (JSON réécrit à chaque événement) et une ligne par
événement sur stdout (`CLICK n=3 x=812 y=440 button=1 t=...`), pour les scripts de test.

Usage : fake-fullscreen.py [--windowed WxH] [--timeout S] [--state FILE]
                           [--mimic] [--title T] [--wm-class C]
  --mimic : WM_CLASS = ("Godot_Engine", "Slay the Spire 2") et titre « Slay the Spire 2 »,
            pour tester la détection par défaut de capture-game-x11.sh. Sans --mimic,
            classe « ULTIMA-FakeGame » (jamais confondue avec le vrai jeu).
"""
import argparse
import datetime as dt
import json
import os
import sys

os.environ["GDK_BACKEND"] = "x11"  # comme le jeu ; doit précéder l'import de Gtk

import gi  # noqa: E402

gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
from gi.repository import Gdk, GLib, Gtk  # noqa: E402

BARS = [(0.75, 0.75, 0.75), (0.75, 0.75, 0), (0, 0.75, 0.75), (0, 0.75, 0),
        (0.75, 0, 0.75), (0.75, 0, 0), (0, 0, 0.75)]  # barres SMPTE 75 %


class Fake(Gtk.Window):
    def __init__(self, a):
        super().__init__(title=a.title)
        self.a = a
        self.clicks = 0
        self.keys = 0
        self.last = None
        self.focus = False
        self.t0 = dt.datetime.now()
        self.area = Gtk.DrawingArea()
        self.area.connect("draw", self.on_draw)
        self.add(self.area)
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK | Gdk.EventMask.KEY_PRESS_MASK)
        self.connect("button-press-event", self.on_click)
        self.connect("key-press-event", self.on_key)
        self.connect("focus-in-event", lambda *_: self.set_focus_flag(True))
        self.connect("focus-out-event", lambda *_: self.set_focus_flag(False))
        self.connect("destroy", Gtk.main_quit)
        if a.windowed:
            w, h = (int(v) for v in a.windowed.lower().split("x"))
            self.set_default_size(w, h)
        else:
            self.fullscreen()
        if a.timeout:
            GLib.timeout_add(int(a.timeout * 1000), self.quit_timeout)
        GLib.timeout_add(1000, self.tick)
        self.emit_event("START", pid=os.getpid())

    # --- état ---
    def emit_event(self, kind, **kw):
        stamp = dt.datetime.now().isoformat(timespec="milliseconds")
        extra = " ".join(f"{k}={v}" for k, v in kw.items())
        print(f"{kind} n_clicks={self.clicks} n_keys={self.keys} {extra} t={stamp}", flush=True)
        if self.a.state:
            gw = self.get_window()
            st = {"pid": os.getpid(), "clicks": self.clicks, "keys": self.keys,
                  "last_click": self.last, "focus": self.focus,
                  "xid": hex(gw.get_xid()) if gw else None,
                  "title": self.a.title, "wm_class": self.a.wm_class, "updated": stamp}
            tmp = self.a.state + ".tmp"
            with open(tmp, "w") as f:
                json.dump(st, f)
            os.replace(tmp, self.a.state)

    def set_focus_flag(self, v):
        self.focus = v
        self.emit_event("FOCUS", focus=v)
        self.area.queue_draw()

    def on_click(self, _w, ev):
        self.clicks += 1
        self.last = {"x": int(ev.x), "y": int(ev.y), "button": ev.button,
                     "t": dt.datetime.now().isoformat(timespec="milliseconds")}
        self.emit_event("CLICK", x=int(ev.x), y=int(ev.y), button=ev.button)
        self.area.queue_draw()
        return True

    def on_key(self, _w, ev):
        self.keys += 1
        name = Gdk.keyval_name(ev.keyval)
        self.emit_event("KEY", key=name)
        if name == "Escape" and ev.state & Gdk.ModifierType.SHIFT_MASK:
            Gtk.main_quit()  # Maj+Échap = sortie manuelle
        self.area.queue_draw()
        return True

    def tick(self):
        self.area.queue_draw()
        return True

    def quit_timeout(self):
        self.emit_event("TIMEOUT")
        Gtk.main_quit()
        return False

    # --- dessin ---
    def on_draw(self, widget, cr):
        w, h = widget.get_allocated_width(), widget.get_allocated_height()
        # barres de couleur sur les 2/3 supérieurs
        bw = w / len(BARS)
        for i, (r, g, b) in enumerate(BARS):
            cr.set_source_rgb(r, g, b)
            cr.rectangle(i * bw, 0, bw + 1, h * 2 / 3)
            cr.fill()
        # bande inférieure : dégradé de gris (repère de transparence d'overlay)
        steps = 16
        for i in range(steps):
            v = i / (steps - 1)
            cr.set_source_rgb(v, v, v)
            cr.rectangle(i * w / steps, h * 2 / 3, w / steps + 1, h / 3)
            cr.fill()
        # grille 10 % + coins repérables
        cr.set_source_rgba(0, 0, 0, 0.5)
        cr.set_line_width(1)
        for k in range(1, 10):
            cr.move_to(w * k / 10, 0); cr.line_to(w * k / 10, h)
            cr.move_to(0, h * k / 10); cr.line_to(w, h * k / 10)
        cr.stroke()
        for (x, y) in [(0, 0), (w - 40, 0), (0, h - 40), (w - 40, h - 40)]:
            cr.set_source_rgb(1, 0, 1)
            cr.rectangle(x, y, 40, 40)
            cr.fill()
        # panneau central : compteurs
        pw, ph = min(760, w - 40), 230
        px, py = (w - pw) / 2, (h - ph) / 2
        cr.set_source_rgba(0, 0, 0, 0.85)
        cr.rectangle(px, py, pw, ph)
        cr.fill()
        cr.set_source_rgb(1, 1, 1)
        cr.select_font_face("monospace")
        lines = [
            ("FAUX JEU — substitut ULTIMA (pas Slay the Spire 2)", 22),
            (f"CLICS REÇUS : {self.clicks}", 44),
            (f"touches : {self.keys}   focus : {'OUI' if self.focus else 'non'}", 22),
            (f"dernier clic : {self.last['x']},{self.last['y']} b{self.last['button']}"
             if self.last else "dernier clic : —", 20),
            (f"{w}x{h}  pid {os.getpid()}  {dt.datetime.now():%H:%M:%S}  (Maj+Échap = quitter)", 18),
        ]
        y = py + 40
        for text, size in lines:
            cr.set_font_size(size)
            cr.move_to(px + 20, y)
            cr.show_text(text)
            y += size + 18
        return False


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--windowed", metavar="WxH", help="fenêtre normale au lieu du plein écran")
    ap.add_argument("--timeout", type=float, default=0, help="quitte après S secondes")
    ap.add_argument("--state", help="fichier JSON d'état (réécrit à chaque événement)")
    ap.add_argument("--mimic", action="store_true", help="imite WM_CLASS/titre du jeu")
    ap.add_argument("--title", default=None)
    ap.add_argument("--wm-class", default=None)
    a = ap.parse_args()
    if a.mimic:
        a.title = a.title or "Slay the Spire 2"
        a.wm_class = a.wm_class or "Slay the Spire 2"
        GLib.set_prgname("Godot_Engine")  # res_name
    else:
        a.title = a.title or "ULTIMA Fake Game"
        a.wm_class = a.wm_class or "ULTIMA-FakeGame"
        GLib.set_prgname("ultima-fake-game")
    Gdk.set_program_class(a.wm_class)  # res_class
    Gtk.init(sys.argv[:1])
    if Gdk.Display.get_default() is None:
        print("ERREUR : pas d'affichage X11 (DISPLAY ?)", file=sys.stderr)
        return 1
    win = Fake(a)
    win.show_all()
    Gtk.main()
    win.emit_event("EXIT")
    return 0


if __name__ == "__main__":
    sys.exit(main())
