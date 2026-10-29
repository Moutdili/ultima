#!/usr/bin/env python3
"""scanout-probe.py — le jeu plein écran est-il affiché en SCANOUT DIRECT ou COMPOSÉ par Mutter ?

Question tranchée : `global.compositor.disable_unredirect()` (extension ULTIMA) ne coûte quelque
chose que si, SANS ULTIMA, Mutter aurait affiché le tampon du jeu directement (scanout direct).
Ce script lit l'état KMS courant, en LECTURE SEULE, comme client DRM non maître :
  ouvre /dev/dri/cardN (O_RDONLY), active DRM_CLIENT_CAP_UNIVERSAL_PLANES sur SON descripteur,
  puis GETPLANERESOURCES / GETPLANE / GETFB2 (ioctls sans DRM_AUTH ; aucun SET, aucun commit).
Il n'ouvre aucune fenêtre, ne touche ni au jeu ni à Mutter (Mutter reste maître DRM).

Lecture du résultat : la taille du tampon affiché par le plan principal de l'écran interne.
  - égale au mode (1920x1200)        -> image COMPOSÉE par Mutter (overlay visible, bureau…)
  - autre taille (ex. 3072x1920,
    tampon XWayland du jeu à l'échelle 2) -> SCANOUT DIRECT du jeu (aucune composition)
Les petits plans (curseur 64x64 / 256x256) sont signalés à part.

Usage : scanout-probe.py [--card /dev/dri/card1] [--watch S] [--interval 0.5] [--json]
  --watch S : échantillonne pendant S secondes et compte les états (le chef bascule ULTIMA
              pendant ce temps : masqué -> attendu scanout direct si Mutter le permet).
Codes : 0 ok · 2 erreur (pas d'accès au périphérique, ioctl refusé).
"""
import argparse
import ctypes
import fcntl
import glob
import json
import os
import sys
import time
from collections import Counter

# ---- ioctl (asm-generic) : _IOC(dir, type, nr, size)
_IOC_WRITE, _IOC_READ = 1, 2


def _ioc(d, nr, size):
    return (d << 30) | (size << 16) | (ord("d") << 8) | nr


class SetClientCap(ctypes.Structure):
    _fields_ = [("capability", ctypes.c_uint64), ("value", ctypes.c_uint64)]


class GetPlaneRes(ctypes.Structure):
    _fields_ = [("plane_id_ptr", ctypes.c_uint64), ("count_planes", ctypes.c_uint32)]


class GetPlane(ctypes.Structure):
    _fields_ = [("plane_id", ctypes.c_uint32), ("crtc_id", ctypes.c_uint32), ("fb_id", ctypes.c_uint32),
                ("possible_crtcs", ctypes.c_uint32), ("gamma_size", ctypes.c_uint32),
                ("count_format_types", ctypes.c_uint32), ("format_type_ptr", ctypes.c_uint64)]


class FbCmd2(ctypes.Structure):
    _fields_ = [("fb_id", ctypes.c_uint32), ("width", ctypes.c_uint32), ("height", ctypes.c_uint32),
                ("pixel_format", ctypes.c_uint32), ("flags", ctypes.c_uint32),
                ("handles", ctypes.c_uint32 * 4), ("pitches", ctypes.c_uint32 * 4),
                ("offsets", ctypes.c_uint32 * 4), ("modifier", ctypes.c_uint64 * 4)]


SET_CLIENT_CAP = _ioc(_IOC_WRITE, 0x0D, ctypes.sizeof(SetClientCap))
GETPLANERESOURCES = _ioc(_IOC_READ | _IOC_WRITE, 0xB5, ctypes.sizeof(GetPlaneRes))
GETPLANE = _ioc(_IOC_READ | _IOC_WRITE, 0xB6, ctypes.sizeof(GetPlane))
GETFB2 = _ioc(_IOC_READ | _IOC_WRITE, 0xCE, ctypes.sizeof(FbCmd2))
CAP_UNIVERSAL_PLANES = 2


def fourcc(v):
    return "".join(chr((v >> (8 * i)) & 0xFF) for i in range(4)).strip() or "?"


def internal_mode(card):
    """Mode préféré du connecteur interne connecté (sysfs, lecture seule)."""
    base = os.path.basename(card)
    for c in sorted(glob.glob(f"/sys/class/drm/{base}-*")):
        try:
            if open(f"{c}/status").read().strip() == "connected":
                m = open(f"{c}/modes").readline().strip()
                if "x" in m:
                    w, h = m.split("x")
                    return os.path.basename(c), int(w), int(h.rstrip("i"))
        except OSError:
            continue
    return None, None, None


def probe(fd):
    res = GetPlaneRes()
    fcntl.ioctl(fd, GETPLANERESOURCES, res)
    ids = (ctypes.c_uint32 * res.count_planes)()
    res.plane_id_ptr = ctypes.addressof(ids)
    fcntl.ioctl(fd, GETPLANERESOURCES, res)
    planes = []
    for pid in ids:
        p = GetPlane(plane_id=pid)
        fcntl.ioctl(fd, GETPLANE, p)
        if not p.fb_id:
            continue
        fb = FbCmd2(fb_id=p.fb_id)
        try:
            fcntl.ioctl(fd, GETFB2, fb)
            planes.append({"plane": pid, "crtc": p.crtc_id, "fb": p.fb_id, "w": fb.width, "h": fb.height,
                           "format": fourcc(fb.pixel_format), "modifier": hex(fb.modifier[0])})
        except OSError as e:
            planes.append({"plane": pid, "crtc": p.crtc_id, "fb": p.fb_id, "error": str(e)})
    return planes


def classify(planes, mw, mh):
    big = [p for p in planes if p.get("w", 0) > 512 and p.get("h", 0) > 512]
    if not big:
        return "inconnu (aucun grand plan lisible)"
    if any((p["w"], p["h"]) != (mw, mh) for p in big):
        sizes = ", ".join(f"{p['w']}x{p['h']}" for p in big if (p["w"], p["h"]) != (mw, mh))
        return f"SCANOUT DIRECT probable (tampon {sizes} != mode {mw}x{mh})"
    return f"COMPOSÉ (tampon = mode {mw}x{mh})"


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--card", default=None)
    ap.add_argument("--watch", type=float, default=0)
    ap.add_argument("--interval", type=float, default=0.5)
    ap.add_argument("--json", action="store_true")
    a = ap.parse_args()
    card = a.card or next((c for c in sorted(glob.glob("/dev/dri/card*"))), None)
    if not card:
        print("aucun /dev/dri/card*", file=sys.stderr)
        return 2
    conn, mw, mh = internal_mode(card)
    try:
        fd = os.open(card, os.O_RDONLY | os.O_CLOEXEC)
        fcntl.ioctl(fd, SET_CLIENT_CAP, SetClientCap(CAP_UNIVERSAL_PLANES, 1))
    except OSError as e:
        print(f"accès DRM refusé ({card}) : {e}", file=sys.stderr)
        return 2
    try:
        if a.watch <= 0:
            planes = probe(fd)
            verdict = classify(planes, mw, mh)
            if a.json:
                print(json.dumps({"card": card, "connector": conn, "mode": [mw, mh], "planes": planes,
                                  "verdict": verdict}, ensure_ascii=False, indent=2))
            else:
                print(f"{card} · connecteur {conn} · mode {mw}x{mh}")
                for p in planes:
                    if "error" in p:
                        print(f"  plan {p['plane']:>3} crtc {p['crtc']} fb {p['fb']} : GETFB2 refusé ({p['error']})")
                    else:
                        print(f"  plan {p['plane']:>3} crtc {p['crtc']} fb {p['fb']:>4} : {p['w']}x{p['h']} {p['format']} mod {p['modifier']}")
                print(f"verdict : {verdict}")
            return 0
        counts, t_end = Counter(), time.monotonic() + a.watch
        last = None
        while time.monotonic() < t_end:
            v = classify(probe(fd), mw, mh)
            counts[v] += 1
            if v != last:
                print(f"{time.strftime('%H:%M:%S')} {v}", flush=True)
                last = v
            time.sleep(a.interval)
        total = sum(counts.values())
        print("résumé : " + " ; ".join(f"{k} = {n}/{total}" for k, n in counts.most_common()))
        return 0
    except OSError as e:
        print(f"ioctl refusé : {e}", file=sys.stderr)
        return 2
    finally:
        os.close(fd)


if __name__ == "__main__":
    sys.exit(main())
