"""Point d'entrée. `--paths` et `--check` n'importent pas GTK et n'ouvrent aucune fenêtre."""

from __future__ import annotations

import sys

from . import __version__


def main(argv: list[str] | None = None) -> int:
    argv = list(sys.argv if argv is None else argv)
    args = argv[1:]
    if "--version" in args:
        print(f"ultima-companion {__version__}")
        return 0
    if "--paths" in args:
        from .watch import debug_env
        print(debug_env())
        return 0
    if "--check" in args:
        # Lecture seule, texte : état + réglages + apparence tels que le compagnon les comprend.
        from . import appearance, ipc, paths, settings, state, textview
        res = state.load()
        daemon = ipc.probe_socket(paths.socket_path())
        banner = textview.banner_message(res, daemon)
        if banner:
            print(f"!! {banner}")
        loaded = settings.load()
        character = (res.state.run.character_id
                     if res.kind == state.OK and res.state is not None and res.state.run is not None
                     else None)
        resolved = appearance.resolve(appearance.read_companion(loaded.settings), character)
        theme_message = textview.appearance_message(resolved)
        if theme_message:
            print(f"!! {theme_message}")
        for sec in textview.status_sections(res, daemon, loaded.settings):
            print(f"== {sec.title}")
            for r in sec.rows:
                print(f"  {r.title} : {r.subtitle}")
        look = textview.appearance_section(resolved, appearance.surfaces(loaded.settings))
        print(f"== {look.title}")
        for r in look.rows:
            print(f"  {r.title} : {r.subtitle}")
        print("== HUD")
        print(f"  {loaded.settings.hud.summary()}")
        print("== Modules")
        for m in loaded.settings.modules:
            line = f"  {m.order}. {m.id} {'activé' if m.enabled else 'désactivé'}"
            if m.screens:
                line += f" · écrans {', '.join(m.screens)}"
            print(line)
        return 0

    from .ui import CompanionApp
    return CompanionApp().run([argv[0]])
