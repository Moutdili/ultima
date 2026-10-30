"""ULTIMA — fenêtre compagnon (modèle HDT).

Lit l'état publié par le daemon ($XDG_RUNTIME_DIR/ultima/state.json + state.sock) et écrit
UNIQUEMENT ~/.config/ultima/settings.json. Ne lit jamais le jeu, ses saves ou sa mémoire.

Les modules `paths`, `settings`, `state`, `textview` sont purs (aucun import GTK) et testés sans
affichage ; `watch` n'importe que Gio/GLib ; `ui`/`app` importent GTK4 + libadwaita.
"""

__version__ = "0.3.0"
