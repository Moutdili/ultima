#!/usr/bin/env python3
"""Capture COMPOSEE par Mutter via xdg-desktop-portal (org.freedesktop.portal.Screenshot).
C'est la seule capture qui montre ce que voit Mio : `import -window root` echoue sous Xwayland
rootless (pas de framebuffer racine) et `import -window <id>` ne montre qu'UNE fenetre, sans composition.

Usage : python3 -I portal-screenshot.py <sortie.png> [--interactive]
GNOME peut afficher une boite de dialogue d'autorisation la premiere fois.
"""
import shutil
import sys
import urllib.parse

import gi

gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib  # noqa: E402

out = sys.argv[1]
interactive = "--interactive" in sys.argv
bus = Gio.bus_get_sync(Gio.BusType.SESSION)
token = "ultima_x11_%d" % GLib.random_int_range(0, 1 << 30)
sender = bus.get_unique_name()[1:].replace(".", "_")
handle = f"/org/freedesktop/portal/desktop/request/{sender}/{token}"
loop = GLib.MainLoop()
result = {}


def on_response(_c, _s, _p, _i, _sig, params, *_):
    code, res = params.unpack()
    result["code"] = code
    result["uri"] = res.get("uri")
    loop.quit()


bus.signal_subscribe("org.freedesktop.portal.Desktop", "org.freedesktop.portal.Request", "Response",
                     handle, None, Gio.DBusSignalFlags.NO_MATCH_RULE, on_response)
bus.call_sync("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
              "org.freedesktop.portal.Screenshot", "Screenshot",
              GLib.Variant("(sa{sv})", ("", {"handle_token": GLib.Variant("s", token),
                                             "interactive": GLib.Variant("b", interactive)})),
              None, Gio.DBusCallFlags.NONE, -1, None)
GLib.timeout_add_seconds(60, loop.quit)
loop.run()
if result.get("code") != 0 or not result.get("uri"):
    print(f"echec portail : code={result.get('code')} (1=refuse, 2=annule/erreur, None=timeout)")
    sys.exit(1)
src = urllib.parse.unquote(urllib.parse.urlparse(result["uri"]).path)
shutil.move(src, out)
print(f"ok {out} (source portail {src})")
