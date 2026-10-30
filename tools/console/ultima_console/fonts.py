"""Polices (docs/44 §5) : quatre dossiers séparés, jamais mélangés.

    ~/.local/share/ultima/fonts/base/      polices libres livrées par ULTIMA (OFL/Apache), licence à côté,
                                           ≤ 2 Mo au total, sha256 vérifié (`fonts/base/SHA256SUMS`)
    ~/.local/share/ultima/fonts/systeme/   LIENS vers les polices du système (`fc-list`), jamais de copie
    ~/.local/share/ultima/fonts/jeu/       polices du jeu extraites LOCALEMENT, jamais commitées
    ~/.local/share/ultima/fonts/perso/     polices déposées par l'utilisateur (.ttf/.otf), vide au départ

Un fichier fontconfig dédié (`~/.config/fontconfig/conf.d/60-ultima.conf`) rend ces dossiers visibles du
HUD (St/Pango) et du compagnon (GTK/Pango) ; il est installé et retiré par le manifeste.

Rien n'est copié depuis le jeu ni depuis le système : le dépôt ne contient QUE `fonts/base` (≤ 2 Mo,
licences libres). Aucun chemin utilisateur n'est écrit dans le dépôt.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
from pathlib import Path
from typing import Any

from . import paths

FOLDERS: tuple[str, ...] = ("base", "systeme", "jeu", "perso")
FONT_SUFFIXES: tuple[str, ...] = (".ttf", ".otf", ".ttc")
MAX_BASE_BYTES = 2 * 1024 * 1024
LICENSE_NAMES = ("LICENSE", "LICENSE.txt", "OFL.txt", "OFL", "COPYING")
_SHA_LINE = re.compile(r"^([0-9a-f]{64})\s+\*?(.+)$")
_SLUG = re.compile(r"[^a-z0-9]+")


class FontError(Exception):
    pass


def root() -> Path:
    return paths.fonts_dir()


def ensure_dirs(base: Path | None = None) -> dict[str, Path]:
    """Crée les quatre dossiers (idempotent) et rend leurs chemins."""
    root_dir = base or root()
    out = {}
    for name in FOLDERS:
        d = root_dir / name
        d.mkdir(parents=True, exist_ok=True)
        out[name] = d
    return out


def folder(name: str) -> Path:
    if name not in FOLDERS:
        raise FontError(f"dossier inconnu : {name} ({', '.join(FOLDERS)})")
    return root() / name


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def repo_base() -> Path:
    return paths.repo_fonts_dir() / "base"


def base_manifest(base: Path | None = None) -> dict[str, str]:
    """`fonts/base/SHA256SUMS` → {nom: sha256}. Fichier absent = aucune police livrée (valide)."""
    path = (base or repo_base()) / "SHA256SUMS"
    try:
        text = path.read_text(encoding="utf-8")
    except OSError:
        return {}
    out: dict[str, str] = {}
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        m = _SHA_LINE.match(line)
        if m:
            out[m.group(2).strip()] = m.group(1)
    return out


def verify_base(base: Path | None = None) -> list[str]:
    """Contrôle des polices livrées : licence, somme sha256, taille totale ≤ 2 Mo."""
    directory = base or repo_base()
    problems: list[str] = []
    if not directory.is_dir():
        return problems                          # dépôt sans polices livrées : valide
    fonts = sorted(p for p in directory.iterdir() if p.suffix.lower() in FONT_SUFFIXES)
    manifest = base_manifest(directory)
    for name, digest in manifest.items():
        f = directory / name
        if not f.is_file():
            problems.append(f"{name} : listé dans SHA256SUMS mais absent")
        elif sha256(f) != digest:
            problems.append(f"{name} : sha256 ne correspond pas")
    for f in fonts:
        if f.name not in manifest:
            problems.append(f"{f.name} : absent de SHA256SUMS")
    total = sum(f.stat().st_size for f in fonts)
    if total > MAX_BASE_BYTES:
        problems.append(f"polices livrées : {total} octets > {MAX_BASE_BYTES} (2 Mo)")
    if fonts and not any((directory / n).is_file() for n in LICENSE_NAMES):
        problems.append("licence absente à côté des polices (LICENSE/OFL.txt)")
    return problems


def copy_base(dest: Path | None = None) -> list[Path]:
    """Copie `fonts/base` dans `fonts/base/` de l'installation (après vérification)."""
    src = repo_base()
    target = dest or folder("base")
    target.mkdir(parents=True, exist_ok=True)
    problems = verify_base(src)
    if problems:
        raise FontError("polices livrées invalides : " + " ; ".join(problems))
    copied = []
    if not src.is_dir() or src.resolve() == target.resolve():
        return copied                       # installé : la source EST la destination
    for f in sorted(src.iterdir()):
        if f.is_file():
            shutil.copyfile(f, target / f.name)
            os.chmod(target / f.name, 0o644)
            copied.append(target / f.name)
    return copied


def fc_list_command() -> list[str] | None:
    """Commande `fc-list` (ou ULTIMA_FC_LIST pour un faux en test)."""
    override = os.environ.get("ULTIMA_FC_LIST")
    if override:
        return [override]
    exe = shutil.which("fc-list")
    return [exe] if exe else None


def system_fonts() -> list[tuple[str, Path]]:
    """[(famille, fichier)] depuis `fc-list` ; liste vide si fontconfig est absent."""
    cmd = fc_list_command()
    if cmd is None:
        return []
    try:
        r = subprocess.run([*cmd, "--format", "%{file}\t%{family[0]}\n"], capture_output=True, text=True,
                           timeout=20)
    except (OSError, subprocess.TimeoutExpired):
        return []
    if r.returncode != 0:
        return []
    out: list[tuple[str, Path]] = []
    seen: set[str] = set()
    for line in r.stdout.splitlines():
        if "\t" not in line:
            continue
        file, family = line.split("\t", 1)
        p = Path(file.strip())
        family = family.split(",")[0].strip() or p.stem
        if not p.is_file() or str(p) in seen:
            continue
        seen.add(str(p))
        out.append((family, p))
    return sorted(out, key=lambda fp: (fp[0].lower(), str(fp[1])))


def _slug(text: str) -> str:
    return _SLUG.sub("-", text.lower()).strip("-") or "police"


def refresh(limit: int = 500) -> dict[str, Any]:
    """Recrée `systeme/` (liens) et `index.json`, vérifie `base/`, garde `jeu/` et `perso/`.

    Rend {'links': [(chemin, cible)], 'dirs': [chemins], 'index': chemin, 'warnings': [...]} pour que
    l'installateur (et `ultima uninstall`) puissent noter exactement ce qui a été créé.
    """
    dirs = ensure_dirs()
    warnings: list[str] = []
    links: list[tuple[str, str]] = []
    # 1. base : recopier si l'installation n'en a pas encore (install.sh le fait déjà).
    if not any(dirs["base"].iterdir()):
        try:
            copy_base(dirs["base"])
        except FontError as e:
            warnings.append(str(e))
    # 2. systeme : liens seulement, jamais de copie.
    index: list[dict[str, str]] = []
    wanted = system_fonts()
    if not wanted:
        warnings.append("fc-list indisponible : aucun lien système (les polices du système restent visibles "
                        "par fontconfig)")
    for family, source in wanted[:limit]:
        name = f"{_slug(family)}--{source.name}"
        link = dirs["systeme"] / name
        try:
            if link.is_symlink():
                if os.readlink(link) == str(source):
                    index.append({"family": family, "file": str(link), "source": str(source)})
                    continue
                link.unlink()
            elif link.exists():
                link.unlink()
            link.symlink_to(source)
        except OSError as e:
            warnings.append(f"lien impossible {link} : {e}")
            continue
        links.append((str(link), str(source)))
        index.append({"family": family, "file": str(link), "source": str(source)})
    # 3. les liens qui ne correspondent plus à l'index sont retirés (nos liens seulement).
    keep = {Path(i["file"]).name for i in index}
    for link in dirs["systeme"].iterdir():
        if link.is_symlink() and link.name not in keep:
            try:
                link.unlink()
            except OSError:
                pass
    index_path = dirs["systeme"] / "index.json"
    index_path.write_text(json.dumps({"api": 1, "fonts": index}, ensure_ascii=False, indent=2) + "\n",
                          encoding="utf-8")
    # 4. jeu et perso : jamais touchés ici (jeu = extraction locale, perso = utilisateur).
    return {"links": links, "dirs": [str(p) for p in dirs.values()], "index": str(index_path),
            "warnings": warnings}


def extract_game_fonts(source: Path, dest: Path | None = None) -> list[Path]:
    """Copie LOCALE des polices du jeu (dossier déjà extrait par l'utilisateur).

    Rien de ce qui est copié ici n'est versionné : `fonts/jeu/` est ignoré par git et vit hors du dépôt.
    """
    target = dest or folder("jeu")
    target.mkdir(parents=True, exist_ok=True)
    if not source.is_dir():
        raise FontError(f"dossier de polices du jeu introuvable : {source}")
    copied: list[Path] = []
    for p in sorted(source.rglob("*")):
        if p.is_file() and p.suffix.lower() in FONT_SUFFIXES:
            out = target / p.name
            shutil.copyfile(p, out)
            os.chmod(out, 0o644)
            copied.append(out)
    return copied


def fontconfig_xml(base: Path | None = None) -> str:
    root_dir = base or root()
    dirs = "\n".join(f"  <dir>{root_dir / name}</dir>" for name in FOLDERS)
    return f"""<?xml version="1.0"?>
<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
<!-- ULTIMA : polices livrées, du système, du jeu et perso. Écrit par tools/install.sh, retiré par
     tools/uninstall.sh (manifeste). Ne modifie aucune autre règle fontconfig. -->
<fontconfig>
{dirs}
  <cachedir prefix="xdg">fontconfig</cachedir>
</fontconfig>
"""


def write_fontconfig(path: Path | None = None, base: Path | None = None) -> Path:
    """Écrit `~/.config/fontconfig/conf.d/60-ultima.conf` (atomique)."""
    target = path or paths.fontconfig_path()
    target.parent.mkdir(parents=True, exist_ok=True)
    tmp = target.with_name(f".{target.name}.tmp")
    tmp.write_text(fontconfig_xml(base), encoding="utf-8")
    os.replace(tmp, target)
    return target


def family_of(path: Path) -> str:
    exe = shutil.which("fc-scan")
    if exe:
        try:
            r = subprocess.run([exe, "--format", "%{family[0]}", str(path)], capture_output=True, text=True,
                               timeout=5)
            if r.returncode == 0 and r.stdout.strip():
                return r.stdout.strip().split(",")[0]
        except (OSError, subprocess.TimeoutExpired):
            pass
    return path.stem


def list_fonts() -> dict[str, list[dict[str, str]]]:
    """Familles par dossier (base, systeme, jeu, perso) — l'index de `systeme` évite fc-scan."""
    out: dict[str, list[dict[str, str]]] = {}
    root_dir = root()
    system_index: dict[str, str] = {}
    try:
        data = json.loads((root_dir / "systeme" / "index.json").read_text(encoding="utf-8"))
        for entry in data.get("fonts", []):
            if isinstance(entry, dict) and isinstance(entry.get("file"), str):
                system_index[entry["file"]] = str(entry.get("family") or "")
    except (OSError, ValueError, AttributeError):
        pass
    for name in FOLDERS:
        d = root_dir / name
        entries: list[dict[str, str]] = []
        if d.is_dir():
            for p in sorted(d.iterdir()):
                if p.is_file() and p.suffix.lower() in FONT_SUFFIXES:
                    if name == "systeme":
                        family = system_index.get(str(p), "")
                    else:
                        family = family_of(p)
                    entries.append({"family": family or p.stem, "file": str(p),
                                    "link": str(p.resolve()) if p.is_symlink() else ""})
        out[name] = entries
    return out


def verify_installed() -> list[str]:
    """Contrôle à froid de l'installation (utilisé par `ultima doctor`) : dossiers + licence + sha256."""
    root_dir = root()
    problems: list[str] = []
    for name in FOLDERS:
        if not (root_dir / name).is_dir():
            problems.append(f"dossier absent : {root_dir / name}")
    if (root_dir / "base" / "SHA256SUMS").is_file():
        manifest = base_manifest(root_dir / "base")
        for name, digest in manifest.items():
            f = root_dir / "base" / name
            if not f.is_file():
                problems.append(f"{name} : absent de l'installation")
            elif sha256(f) != digest:
                problems.append(f"{name} : sha256 modifié")
        if not any((root_dir / "base" / n).is_file() for n in LICENSE_NAMES):
            problems.append("licence absente dans fonts/base")
    if not paths.fontconfig_path().is_file():
        problems.append(f"fontconfig absent : {paths.fontconfig_path()}")
    return problems
