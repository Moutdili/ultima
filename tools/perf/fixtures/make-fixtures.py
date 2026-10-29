#!/usr/bin/env python3
"""make-fixtures.py — génère des journaux MangoHud SYNTHÉTIQUES (déterministes) pour tester
analyze-frametimes.py. Rien ici ne vient du vrai jeu : les valeurs sont fabriquées pour que
chaque verdict soit connu d'avance (voir EXPECTED en bas et tools/perf/test-analyze.sh).

Modèle : 60 FPS (16,67 ms ± 0,25 ms), une « pioche » toutes les 3 s = rafale de 8 images
plus lentes (durée réglée par condition). 12 s par segment.

Usage : make-fixtures.py [DOSSIER]   (défaut : session-demo/ à côté de ce script)
"""
import os
import random
import sys

COLS = ("fps,frametime,cpu_load,cpu_power,gpu_load,cpu_temp,gpu_temp,gpu_core_clock,"
        "gpu_mem_clock,gpu_vram_used,gpu_power,ram_used,swap_used,process_rss,cpu_mhz,elapsed")
SYSINFO_H = "os,cpu,gpu,ram,kernel,driver,cpuscheduler"
SYSINFO_V = ("Fedora Linux 44 (synthetique),AMD Ryzen 7 250 w/ Radeon 780M Graphics,"
             "AMD Radeon 780M Graphics (RADV PHOENIX),23.1 GB,7.2.11-200.fc44.x86_64,Mesa 26.2.5,")


def frames(seed, burst_ms, seconds=12.0, cap30_from=None):
    rnd = random.Random(seed)
    out, t = [], 0.0
    next_draw = 1.5
    burst_left = 0
    while t < seconds:
        if cap30_from is not None and t >= cap30_from:
            ft = 33.33 + rnd.gauss(0, 0.2)
        elif burst_left:
            ft = burst_ms + rnd.gauss(0, 0.4)
            burst_left -= 1
        else:
            ft = 16.67 + rnd.gauss(0, 0.25)
            if t >= next_draw:
                burst_left = 8
                next_draw += 3.0
        ft = max(ft, 8.0)
        t += ft / 1000.0
        out.append((ft, t))
    return out


def write(path, fr, versioned=False, truncate_last=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        if versioned:
            f.write("v1\n0.8.4\n---------------------SYSTEM INFO---------------------\n")
        f.write(SYSINFO_H + "\n" + SYSINFO_V + "\n")
        if versioned:
            f.write("--------------------FRAME METRICS--------------------\n")
        f.write(COLS + "\n")
        for ft, t in fr:
            f.write(f"{1000.0 / ft:.1f},{ft:.3f},23.0,0.0,71,58,52,1800,2800,812000000,0.0,"
                    f"9.1,0.0,840000000,3100,{int(t * 1e9)}\n")
        if truncate_last:
            f.write("59.9,16.7,22.0,0.0,70")   # ligne coupée (jeu tué pendant l'écriture)


# nom de fichier, condition attendue, rafale (ms), options
PLAN = [
    ("SlayTheSpire2_2026-10-29_14-00-10.csv", "sans-ultima", 24.0, {}),
    ("SlayTheSpire2_2026-10-29_14-01-10.csv", "rail-seul", 24.3, {"truncate_last": True}),
    ("SlayTheSpire2_2026-10-29_14-02-10.csv", "panneau-ouvert", 32.0, {}),
    ("SlayTheSpire2_2026-10-29_14-03-10.csv", "sans-flou", 24.2, {}),
    ("SlayTheSpire2_2026-10-29_14-04-10.csv", "sans-unredirect", 23.9, {}),
    ("SlayTheSpire2_2026-10-29_14-05-10.csv", "x11", 23.0, {}),
    ("SlayTheSpire2_2026-10-29_14-06-10.csv", "sans-ultima", 24.2, {"versioned": True}),
    ("SlayTheSpire2_2026-10-29_14-07-10.csv", "masque", 24.0, {"cap30_from": 5.0}),
    # sans horodatage dans le nom -> attribué par le nom du dossier
    ("x11/repetition-2.csv", "x11", 28.5, {}),
]

MARKS = [
    ("2026-10-29T14:00:05.120", "sans-ultima"),
    ("2026-10-29T14:01:04.800", "rail-seul"),
    ("2026-10-29T14:02:06.000", "panneau-ouvert"),
    ("2026-10-29T14:03:05.500", "sans-flou"),
    ("2026-10-29T14:04:03.000", "sans-unredirect"),
    ("2026-10-29T14:05:04.000", "x11"),
    ("2026-10-29T14:06:02.000", "sans-ultima"),
    ("2026-10-29T14:07:01.000", "masque"),
]

EVENTS = [
    ("2026-10-29T14:03:59.000", "[INFO] Player 1 playing card DEFEND_NECROBINDER (no target)"),
    ("2026-10-29T14:07:15.250", "[INFO] MuteInBackground: FocusOut received [tween=False, windowFocused=False]"),
    ("2026-10-29T14:07:15.251", "[INFO] Limiting background FPS to 30"),
]

EXPECTED = {
    "sans-ultima": "REFERENCE", "rail-seul": "OK", "panneau-ouvert": "DEPASSE",
    "sans-flou": "OK", "sans-unredirect": "OK", "x11": "INDECIS",
}


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "session-demo")
    for i, (name, _, burst, opts) in enumerate(PLAN):
        fr = frames(1000 + i, burst, cap30_from=opts.get("cap30_from"))
        write(os.path.join(out, "csv", name), fr, opts.get("versioned", False), opts.get("truncate_last", False))
    with open(os.path.join(out, "marks.tsv"), "w", encoding="utf-8") as f:
        f.write("# iso_local\tcondition (synthétique)\n")
        for t, lab in MARKS:
            f.write(f"{t}\t{lab}\n")
    with open(os.path.join(out, "godot-events.tsv"), "w", encoding="utf-8") as f:
        f.write("# iso_local\tligne godot.log (synthétique)\n")
        for t, x in EVENTS:
            f.write(f"{t}\t{x}\n")
    print(out)


if __name__ == "__main__":
    main()
