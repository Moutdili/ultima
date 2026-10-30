"""Polices du compagnon (docs/44 §5) — aucun fichier de police dans le dépôt.

Quatre dossiers séparés sous `~/.local/share/ultima/fonts/` : `perso`, `jeu`, `base`, `systeme`
(ordre de recherche). Une famille demandée est cherchée par son **nom interne** (table `name` du
fichier) ou, à défaut, par son nom de fichier, puis dans **fontconfig** (`fc-list`). Introuvable →
police du thème, puis police système. Les polices du jeu ne sont plus lues dans le dossier extrait du
jeu (v1.3) : `install.sh` les copie localement dans `jeu/` (docs/44 §5).

`font_files`, `find_family`, `resolve_family` sont purs (testés sans affichage) ; `register` importe
Pango et n'ajoute les fichiers qu'à la FontMap du processus (rien n'est installé).
"""

from __future__ import annotations

import re
import subprocess
from dataclasses import dataclass, field
from pathlib import Path

from . import paths

FOLDERS: tuple[str, ...] = ("perso", "jeu", "base", "systeme")
SUFFIXES: tuple[str, ...] = (".ttf", ".otf", ".ttc", ".otc")
DEFAULT_FAMILY = "Cantarell"
FAMILY_MAX = 80
_STYLE_WORDS = {
    "regular", "bold", "italic", "oblique", "light", "medium", "semibold", "demibold", "black",
    "thin", "extralight", "extrabold", "book", "roman", "condensed", "expanded", "narrow", "wide",
    "variable", "vf", "display", "text", "ui", "mono",
}


@dataclass
class FontFile:
    path: Path
    folder: str
    families: set[str] = field(default_factory=set)

    def matches(self, family: str) -> bool:
        wanted = _norm(family)
        return bool(wanted) and wanted in {_norm(f) for f in self.families}


@dataclass
class FontResolution:
    family: str | None           # famille retenue (None = police système par défaut)
    source: str                  # perso | jeu | base | systeme | fontconfig | theme | système
    file: Path | None = None
    requested: str | None = None
    message: str | None = None   # lisible : « Police « X » introuvable : repli … »


def root() -> Path:
    return paths.installed_fonts_dir()


def _norm(text: str) -> str:
    return re.sub(r"[^a-z0-9]", "", text.casefold())


def clean_family(value: object) -> str | None:
    """Nom de famille sûr pour CSS (pas de guillemet, d'accolade, de point-virgule)."""
    if not isinstance(value, str):
        return None
    text = value.strip()
    if not text or len(text) > FAMILY_MAX:
        return None
    return text if re.fullmatch(r"[^\"\\{};\n\r]{1,%d}" % FAMILY_MAX, text) else None


# ---- table `name` des fichiers de police (aucune dépendance) ------------------------------------

def _sfnt_families(path: Path) -> set[str]:
    """Familles (nameID 1 et 16) lues dans la table `name` ; ensemble vide si illisible."""
    try:
        data = path.read_bytes()
    except OSError:
        return set()
    if len(data) < 12:
        return set()
    offset = 0
    if data[:4] == b"ttcf":                     # collection : première police
        if len(data) < 16:
            return set()
        offset = int.from_bytes(data[12:16], "big")
    if offset + 12 > len(data):
        return set()
    count = int.from_bytes(data[offset + 4:offset + 6], "big")
    tables: dict[bytes, tuple[int, int]] = {}
    for index in range(count):
        record = offset + 12 + 16 * index
        if record + 16 > len(data):
            break
        tag = data[record:record + 4]
        start = int.from_bytes(data[record + 8:record + 12], "big")
        length = int.from_bytes(data[record + 12:record + 16], "big")
        tables[tag] = (start, length)
    if b"name" not in tables:
        return set()
    start, length = tables[b"name"]
    blob = data[start:start + length]
    if len(blob) < 6:
        return set()
    records = int.from_bytes(blob[2:4], "big")
    strings = int.from_bytes(blob[4:6], "big")
    out: set[str] = set()
    for index in range(records):
        record = 6 + 12 * index
        if record + 12 > len(blob):
            break
        platform = int.from_bytes(blob[record:record + 2], "big")
        name_id = int.from_bytes(blob[record + 6:record + 8], "big")
        size = int.from_bytes(blob[record + 8:record + 10], "big")
        at = int.from_bytes(blob[record + 10:record + 12], "big")
        if name_id not in (1, 16):
            continue
        raw = blob[strings + at:strings + at + size]
        try:
            text = raw.decode("utf-16-be") if platform in (0, 3) else raw.decode("latin-1")
        except (UnicodeDecodeError, ValueError):
            continue
        text = text.strip().strip("\x00").strip()
        if text:
            out.add(text)
    return out


def _filename_families(path: Path) -> set[str]:
    """Repli : « FiraSans-Regular.ttf » → « FiraSans » (nom de fichier, style retiré)."""
    parts = [p for p in re.split(r"[-_.]", path.stem) if p]
    while len(parts) > 1 and parts[-1].casefold() in _STYLE_WORDS:
        parts.pop()
    return {parts[0]} if parts else set()


# ---- découverte -----------------------------------------------------------------------------------

def font_files(root_dir: Path | None = None) -> list[FontFile]:
    """Tous les fichiers de police des quatre dossiers, dans l'ordre de recherche."""
    base = root_dir or root()
    found: list[FontFile] = []
    for name in FOLDERS:
        directory = base / name
        try:
            entries = sorted(directory.iterdir())
        except OSError:
            continue
        for path in entries:
            if not path.is_file() or path.suffix.lower() not in SUFFIXES:
                continue
            families = _sfnt_families(path) or _filename_families(path)
            found.append(FontFile(path=path, folder=name, families=families))
    return found


def families_by_folder(root_dir: Path | None = None) -> dict[str, list[str]]:
    """Familles connues par dossier (suggestions de l'éditeur, `ultima font list`)."""
    out: dict[str, list[str]] = {name: [] for name in FOLDERS}
    for item in font_files(root_dir):
        for family in item.families:
            if family not in out[item.folder]:
                out[item.folder].append(family)
    for name in out:
        out[name].sort(key=str.casefold)
    return out


def find_family(family: str, root_dir: Path | None = None) -> FontFile | None:
    """Premier fichier des quatre dossiers dont une famille correspond (ordre perso→systeme)."""
    if not family:
        return None
    for item in font_files(root_dir):
        if item.matches(family):
            return item
    return None


def _fc_list_families() -> set[str]:
    """Familles installées sur le système (`fc-list`). Vide si fontconfig est absent."""
    try:
        out = subprocess.run(["fc-list", "--format", "%{family[0]}\n"], capture_output=True,
                             text=True, timeout=5)
    except (OSError, subprocess.SubprocessError):
        return set()
    if out.returncode != 0:
        return set()
    return {line.strip() for line in out.stdout.splitlines() if line.strip()}


def fontconfig_has(family: str, families: set[str] | None = None) -> bool:
    if not family:
        return False
    known = _fc_list_families() if families is None else families
    wanted = _norm(family)
    return any(_norm(name) == wanted for name in known)


# ---- résolution -------------------------------------------------------------------------------------

def resolve_family(requested: object = None, theme_family: object = None,
                   root_dir: Path | None = None, use_fontconfig: bool = True,
                   fc_families: set[str] | None = None) -> FontResolution:
    """`appearance.font` → dossiers ULTIMA → fontconfig → police du thème → police système."""
    wanted = clean_family(requested)
    theme = clean_family(theme_family)
    missing: str | None = None
    if wanted:
        hit = find_family(wanted, root_dir)
        if hit is not None:
            return FontResolution(wanted, hit.folder, hit.path, wanted)
        if use_fontconfig and fontconfig_has(wanted, fc_families):
            return FontResolution(wanted, "fontconfig", None, wanted)
        missing = f"Police « {wanted} » introuvable"
    if theme:
        note = f"{missing} : police du thème « {theme} »." if missing else None
        hit = find_family(theme, root_dir)
        if hit is not None:
            return FontResolution(theme, hit.folder, hit.path, wanted, note)
        if use_fontconfig and fontconfig_has(theme, fc_families):
            return FontResolution(theme, "fontconfig", None, wanted, note)
        return FontResolution(theme, "theme", None, wanted, note)
    if missing:
        return FontResolution(None, "système", None, wanted, f"{missing} : police système.")
    return FontResolution(None, "système")


def css_family(family: str | None) -> str:
    """Pile CSS : famille retenue puis replis sûrs (jamais de famille injectée brute)."""
    clean = clean_family(family)
    fallback = f'"{DEFAULT_FAMILY}", "Adwaita Sans", sans-serif'
    return f'"{clean}", {fallback}' if clean else fallback


def register(files: list[Path] | None = None) -> list[str]:
    """Ajoute des fichiers à la FontMap Pango du processus. Retourne les noms chargés."""
    files = [] if files is None else list(files)
    if not files:
        return []
    import gi
    gi.require_version("PangoCairo", "1.0")
    from gi.repository import PangoCairo
    fmap = PangoCairo.FontMap.get_default()
    if not hasattr(fmap, "add_font_file"):       # Pango < 1.56
        return []
    loaded = []
    for f in files:
        try:
            if fmap.add_font_file(str(f)):
                loaded.append(Path(f).name)
        except Exception:  # noqa: BLE001 — une police illisible ne doit jamais empêcher l'ouverture
            continue
    return loaded
