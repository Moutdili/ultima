"""Polices du jeu (Kreon, Source Code Pro, Spectral — docs/17 §3), chargées SANS les copier dans le dépôt.

On les cherche dans le pck extrait (`<dépôt>/game/pck/fonts/`, dossier ignoré par git), y compris depuis
un worktree (le `.git` du worktree pointe vers le dépôt principal), ou dans `$ULTIMA_GAME_FONTS`.
Enregistrement pour le processus seulement (Pango ≥ 1.56 : `FontMap.add_font_file`) : rien n'est
installé dans `~/.local/share/fonts`. Absentes → repli Cantarell / monospace, sans erreur.

`candidate_dirs` / `find_fonts` sont purs (testés sans affichage) ; `register` importe Pango.
"""

from __future__ import annotations

import os
from pathlib import Path

FONT_FILES: tuple[str, ...] = (
    "kreon_regular.ttf", "kreon_bold.ttf",
    "source_code_pro_semibold.ttf", "source_code_pro_medium.ttf",
    "spectral_bold.ttf",
)
REL = Path("game") / "pck" / "fonts"


def _main_checkout(repo: Path) -> Path | None:
    """Worktree : `.git` est un fichier « gitdir: <dépôt>/.git/worktrees/<nom> » → `<dépôt>`."""
    dotgit = repo / ".git"
    if not dotgit.is_file():
        return None
    try:
        line = dotgit.read_text(encoding="utf-8").strip()
    except OSError:
        return None
    if not line.startswith("gitdir:"):
        return None
    gitdir = Path(line.split(":", 1)[1].strip())
    if not gitdir.is_absolute():
        gitdir = (repo / gitdir).resolve()
    common = gitdir
    try:
        rel = (gitdir / "commondir").read_text(encoding="utf-8").strip()
        common = (gitdir / rel).resolve()
    except OSError:
        pass
    return common.parent if common.name == ".git" else None


def candidate_dirs(start: Path | None = None, env: dict | None = None) -> list[Path]:
    env = os.environ if env is None else env
    out: list[Path] = []
    explicit = env.get("ULTIMA_GAME_FONTS")
    if explicit:
        out.append(Path(explicit))
    here = (start or Path(__file__)).resolve()
    for parent in [here, *here.parents]:
        out.append(parent / REL)
        if (parent / ".git").exists():
            main = _main_checkout(parent)
            if main is not None:
                out.append(main / REL)
            break
    seen: set[Path] = set()
    unique = []
    for d in out:
        if d not in seen:
            seen.add(d)
            unique.append(d)
    return unique


def find_fonts(start: Path | None = None, env: dict | None = None) -> list[Path]:
    """Premier dossier candidat qui contient au moins une police connue → ses fichiers présents."""
    for d in candidate_dirs(start, env):
        files = [d / f for f in FONT_FILES if (d / f).is_file()]
        if files:
            return files
    return []


def register(files: list[Path] | None = None) -> list[str]:
    """Ajoute les polices à la FontMap Pango du processus. Retourne les noms chargés."""
    files = find_fonts() if files is None else files
    if not files:
        return []
    import gi
    gi.require_version("PangoCairo", "1.0")
    from gi.repository import PangoCairo
    fmap = PangoCairo.FontMap.get_default()
    if not hasattr(fmap, "add_font_file"):   # Pango < 1.56
        return []
    loaded = []
    for f in files:
        try:
            if fmap.add_font_file(str(f)):
                loaded.append(f.name)
        except Exception:  # noqa: BLE001 — une police illisible ne doit jamais empêcher l'ouverture
            continue
    return loaded
