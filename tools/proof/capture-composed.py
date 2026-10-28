#!/usr/bin/env python3
"""capture-composed.py — capture d'écran COMPOSÉE (ce que Mio voit, overlay compris).

Passe par le portail xdg-desktop-portal `org.freedesktop.portal.Screenshot`
(D-Bus session, python3-gobject/Gio), mode NON interactif (`interactive=false`).
C'est la seule voie qui voit l'image finale de Mutter (jeu XWayland + overlay
Wayland/extension Shell) : `import -window root` ne voit que le serveur XWayland.

Sortie : PNG horodaté dans ~/sts2-proofs/ (hors dépôt) — chemin imprimé sur stdout.
Codes retour : 0 OK, 1 refus/annulation (réponse portail 1), 2 autre erreur portail,
               3 timeout (aucune réponse : boîte de dialogue en attente ?), 4 D-Bus.

Première exécution : selon la version de GNOME, une boîte « Autoriser la capture
d'écran ? » peut apparaître (voir tools/proof/README.md). Le choix est mémorisé
dans le permission store (`flatpak permissions screenshot`).

Usage : capture-composed.py [--out-dir DIR] [--label TXT] [--timeout S] [--keep-original]
"""
import argparse
import datetime as dt
import os
import shutil
import sys
import uuid

import gi

gi.require_version("Gio", "2.0")
gi.require_version("GLib", "2.0")
from gi.repository import Gio, GLib  # noqa: E402

BUS = "org.freedesktop.portal.Desktop"
PATH = "/org/freedesktop/portal/desktop"
IFACE = "org.freedesktop.portal.Screenshot"
REQ_IFACE = "org.freedesktop.portal.Request"
RESPONSES = {0: "succès", 1: "annulé/refusé par l'utilisateur", 2: "autre erreur (backend)"}


def log(msg):
    print(f"[capture-composed {dt.datetime.now():%H:%M:%S}] {msg}", file=sys.stderr)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--out-dir", default=os.path.expanduser("~/sts2-proofs"))
    ap.add_argument("--label", default="", help="suffixe ajouté au nom de fichier")
    ap.add_argument("--timeout", type=float, default=30.0,
                    help="secondes d'attente de la réponse du portail (défaut 30)")
    ap.add_argument("--interactive", action="store_true",
                    help="mode interactif (sélecteur GNOME) — pour l'autorisation initiale")
    ap.add_argument("--keep-original", action="store_true",
                    help="copier au lieu de déplacer le fichier écrit par le portail")
    a = ap.parse_args()

    try:
        conn = Gio.bus_get_sync(Gio.BusType.SESSION, None)
    except GLib.Error as e:
        log(f"ERREUR D-Bus session : {e.message}")
        return 4

    # Réponse = signal Response sur /org/freedesktop/portal/desktop/request/<sender>/<token>.
    # On s'abonne AVANT l'appel pour éviter la course (doc xdg-desktop-portal).
    sender = conn.get_unique_name()[1:].replace(".", "_")
    token = "ultima_" + uuid.uuid4().hex[:12]
    req_path = f"{PATH}/request/{sender}/{token}"
    loop = GLib.MainLoop()
    result = {"code": None, "results": None}

    def on_response(_c, _s, _p, _i, _sig, params):
        code, res = params.unpack()
        result["code"], result["results"] = code, res
        loop.quit()

    sub = conn.signal_subscribe(BUS, REQ_IFACE, "Response", req_path, None,
                                Gio.DBusSignalFlags.NONE,
                                on_response)
    opts = {"handle_token": GLib.Variant("s", token),
            "interactive": GLib.Variant("b", bool(a.interactive)),
            "modal": GLib.Variant("b", False)}
    t0 = dt.datetime.now()
    try:
        ret = conn.call_sync(BUS, PATH, IFACE, "Screenshot",
                             GLib.Variant("(sa{sv})", ("", opts)),
                             GLib.VariantType("(o)"), Gio.DBusCallFlags.NONE, 10000, None)
    except GLib.Error as e:
        log(f"ERREUR appel Screenshot : {e.message}")
        return 4
    handle = ret.unpack()[0]
    if handle != req_path:  # anciens portails : chemin différent, on se réabonne
        log(f"handle inattendu {handle}, réabonnement")
        conn.signal_unsubscribe(sub)
        sub = conn.signal_subscribe(BUS, REQ_IFACE, "Response", handle, None,
                                    Gio.DBusSignalFlags.NONE, on_response)
    log(f"requête envoyée (handle {handle}), attente ≤ {a.timeout:.0f}s "
        "(si une boîte d'autorisation GNOME s'affiche : répondre « Autoriser »)")

    timed_out = {"v": False}

    def on_timeout():
        timed_out["v"] = True
        loop.quit()
        return False

    GLib.timeout_add(int(a.timeout * 1000), on_timeout)
    loop.run()
    conn.signal_unsubscribe(sub)
    elapsed = (dt.datetime.now() - t0).total_seconds()

    if timed_out["v"] and result["code"] is None:
        log(f"TIMEOUT après {elapsed:.1f}s : aucune réponse du portail "
            "(boîte de dialogue restée ouverte ? backend absent ?)")
        try:  # ferme la requête pour ne pas laisser de dialogue orphelin
            conn.call_sync(BUS, handle, REQ_IFACE, "Close", None, None,
                           Gio.DBusCallFlags.NONE, 2000, None)
        except GLib.Error:
            pass
        return 3

    code, res = result["code"], result["results"] or {}
    log(f"réponse portail = {code} ({RESPONSES.get(code, '?')}) en {elapsed:.1f}s ; "
        f"clés={sorted(res.keys())}")
    if code != 0:
        return 1 if code == 1 else 2
    uri = res.get("uri")
    if not uri:
        log("ERREUR : réponse 0 sans 'uri'")
        return 2
    src = Gio.File.new_for_uri(uri).get_path()
    os.makedirs(a.out_dir, exist_ok=True)
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S-%f")[:-3]
    name = f"composed-{stamp}{('-' + a.label) if a.label else ''}.png"
    dst = os.path.join(a.out_dir, name)
    if a.keep_original:
        shutil.copy2(src, dst)
    else:
        shutil.move(src, dst)  # le portail écrit dans ~/Pictures : on ne laisse pas de déchets
    log(f"source portail : {src}")
    print(dst)
    return 0


if __name__ == "__main__":
    sys.exit(main())
