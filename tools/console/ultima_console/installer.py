"""Installation et désinstallation d'ULTIMA sous ~/.local, pilotées par un MANIFESTE.

`tools/install.sh` vérifie les prérequis, construit (dotnet publish Release) dans un dossier de staging,
puis appelle `python3 -m ultima_console.installer install …` ; `ultima uninstall` et
`tools/install.sh --uninstall` appellent `uninstall()`.

Règles :
- tout ce qui est écrit est noté dans `$ULTIMA_HOME/manifest.json` (fichier + sha256, lien + cible,
  arbre pour le venv, dossiers créés, sauvegardes) AVANT la copie, puis mis à jour à la fin ;
- un chemin déjà présent qui n'appartient pas à l'installation (ex. un ancien `~/.local/bin/ultima`
  écrit à la main) est DÉPLACÉ dans `$ULTIMA_HOME/backup/` puis remis en place par la désinstallation ;
- la désinstallation retire exactement les entrées du manifeste, remet les sauvegardes, supprime les
  dossiers créés s'ils sont vides ; `--purge` retire en plus réglages (~/.config/ultima), journal
  (~/.local/state/ultima) et dossier IPC, après confirmation ;
- réinstaller est idempotent : mêmes fichiers → mêmes octets, entrées disparues retirées, sauvegardes
  et dossiers créés de la première installation conservés, venv gardé si ses dépendances n'ont pas changé ;
- aucun appel à gnome-extensions (activation laissée à Mio) ; systemctl seulement pour désactiver une
  unité que l'installation a écrite (jamais si ULTIMA_NO_SYSTEMCTL est défini).
"""

from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import json
import os
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

from . import VERSION, paths, procs
from .settings import atomic_write_text

MARKER = "ultima-install"
MANIFEST_VERSION = 1
EXT_UUID = procs.EXT_UUID


# ---------- utilitaires ----------

def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _exists(p: Path) -> bool:
    return p.exists() or p.is_symlink()


def _remove(p: Path) -> None:
    if p.is_symlink() or p.is_file():
        p.unlink()
    elif p.is_dir():
        shutil.rmtree(p)


def _under(p: Path, root: Path) -> bool:
    try:
        Path(os.path.abspath(p)).relative_to(os.path.abspath(root))
        return True
    except ValueError:
        return False


def read_manifest(home: Path | None = None) -> dict | None:
    path = (home or paths.ultima_home()) / "manifest.json"
    try:
        m = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return m if isinstance(m, dict) and m.get("marker") == MARKER else None


def _write_manifest(home: Path, man: dict) -> None:
    atomic_write_text(home / "manifest.json", json.dumps(man, ensure_ascii=False, indent=2) + "\n")


# ---------- plan ----------

@dataclass
class Item:
    dest: Path
    kind: str                 # file | symlink
    src: Path | None = None   # file
    target: str | None = None  # symlink
    mode: int = 0o644


def _tree(src: Path, dest: Path, items: list[Item], exec_names: tuple[str, ...] = ()) -> None:
    for root, dirs, files in os.walk(src):
        dirs.sort()
        for name in sorted(files):
            s = Path(root) / name
            rel = s.relative_to(src)
            if "__pycache__" in rel.parts or name.endswith(".pyc"):
                continue
            mode = 0o755 if (os.access(s, os.X_OK) or name in exec_names) else 0o644
            items.append(Item(dest / rel, "file", src=s, mode=mode))


def _data_files(repo: Path) -> list[Path]:
    """Fichiers SUIVIS de data/ (git ls-files) ; sans git : meta, catalog, calibration, explosivity."""
    try:
        r = subprocess.run(["git", "-C", str(repo), "ls-files", "-z", "data"], capture_output=True, check=True)
        files = [repo / f for f in r.stdout.decode().split("\0") if f]
        if files:
            return sorted(f for f in files if f.is_file())
    except (OSError, subprocess.CalledProcessError):
        pass
    out: list[Path] = []
    for d in ("meta", "catalog", "calibration", "explosivity"):
        for root, _, fs in os.walk(repo / "data" / d):
            out.extend(Path(root) / f for f in fs)
    return sorted(out)


def plan(repo: Path, stage: Path, home: Path, bin_dir: Path, *, extension: bool, systemd: bool,
         kb_stage: Path | None) -> list[Item]:
    items: list[Item] = []
    console = repo / "tools" / "console"
    items.append(Item(home / "bin" / "ultima", "file", src=console / "ultima", mode=0o755))
    _tree(console / "ultima_console", home / "lib" / "console" / "ultima_console", items)
    items.append(Item(home / "lib" / "console" / "requirements.txt", "file", src=console / "requirements.txt"))
    for name in ("daemon", "mirror"):
        if (stage / name).is_dir():
            _tree(stage / name, home / "lib" / name, items)
    if (home / "lib" / "daemon" / "ultima-daemon") in {i.dest for i in items}:
        items.append(Item(home / "bin" / "ultima-daemon", "symlink", target="../lib/daemon/ultima-daemon"))
    for f in _data_files(repo):
        items.append(Item(home / f.relative_to(repo), "file", src=f))
    if kb_stage is not None and (kb_stage / "kb.json").is_file():
        _tree(kb_stage, paths.kb_path().parent, items)
    if extension:
        src = repo / "overlay" / "gnome" / EXT_UUID
        _tree(src, paths.extension_dir(EXT_UUID), items)
        compiled = stage / "extension-schemas" / "gschemas.compiled"
        if compiled.is_file():
            items.append(Item(paths.extension_dir(EXT_UUID) / "schemas" / "gschemas.compiled", "file", src=compiled))
    if systemd:
        unit = stage / "ultima-daemon.service"
        unit.write_text(systemd_unit(home), encoding="utf-8")
        items.append(Item(paths.systemd_unit_path(), "file", src=unit))
    items.append(Item(bin_dir / "ultima", "symlink", target=str(home / "bin" / "ultima")))
    return items


def systemd_unit(home: Path) -> str:
    return f"""# ULTIMA — daemon (unité systemd UTILISATEUR), écrite par tools/install.sh --systemd.
# Activation laissée à l'utilisateur : systemctl --user daemon-reload && systemctl --user enable --now ultima-daemon.service
[Unit]
Description=ULTIMA daemon (tracker Slay the Spire 2, lecture seule)
After=graphical-session.target
PartOf=graphical-session.target

[Service]
Type=simple
ExecStart={home}/bin/ultima daemon-exec
Environment=DOTNET_ROOT=%h/.dotnet
RuntimeDirectory=ultima
RuntimeDirectoryMode=0700
RuntimeDirectoryPreserve=yes
Nice=10
IOSchedulingClass=idle
MemoryHigh=256M
NoNewPrivileges=yes
Restart=on-failure
RestartSec=5
TimeoutStopSec=5

[Install]
WantedBy=graphical-session.target
"""


# ---------- installation ----------

class InstallError(Exception):
    pass


def _owned_by(entry_paths: set[str], p: Path) -> bool:
    return str(p) in entry_paths


def _backup_path(home: Path, original: Path) -> Path:
    """backup/HOME/.local/bin/ultima (sous $HOME), sinon backup/ROOT/<chemin absolu>."""
    try:
        return home / "backup" / "HOME" / Path(os.path.abspath(original)).relative_to(os.path.abspath(paths.home()))
    except ValueError:
        return home / "backup" / "ROOT" / str(original).lstrip("/")


def _mkdirs(d: Path, created: list[str]) -> None:
    missing = []
    cur = d
    while not cur.exists():
        missing.append(cur)
        cur = cur.parent
    for m in reversed(missing):
        m.mkdir()
        created.append(str(m))


def _same_file(dest: Path, src: Path, mode: int) -> bool:
    try:
        st = dest.lstat()
    except OSError:
        return False
    if dest.is_symlink() or not dest.is_file() or st.st_size != src.stat().st_size:
        return False
    return (st.st_mode & 0o777) == mode and sha256(dest) == sha256(src)


def _copy(src: Path, dest: Path, mode: int) -> None:
    tmp = dest.with_name(f".{dest.name}.ultima-tmp")
    shutil.copyfile(src, tmp)
    os.chmod(tmp, mode)
    os.replace(tmp, dest)


def install(repo: Path, stage: Path, *, extension: bool = True, systemd: bool = False,
            venv: bool = True, wheels: Path | None = None, kb_stage: Path | None = None,
            log=print) -> dict:
    home = paths.ultima_home()
    bin_dir = paths.bin_dir()
    old = read_manifest(home)
    if old is None and (home / "manifest.json").exists():
        raise InstallError(f"{home}/manifest.json n'est pas un manifeste ULTIMA : refus d'écraser")
    old_entries = {e["path"]: e for e in (old or {}).get("entries", [])}
    created: list[str] = list((old or {}).get("createdDirs", []))
    backups: list[dict] = list((old or {}).get("backups", []))
    backed = {b["original"] for b in backups}

    items = plan(repo, stage, home, bin_dir, extension=extension, systemd=systemd, kb_stage=kb_stage)
    venv_dir = paths.venv_dir()
    req = repo / "tools" / "console" / "requirements.txt"
    req_hash = sha256(req) if req.is_file() else None

    # 1. Conflits : un chemin présent qui n'est pas à nous → sauvegarde (déplacé, remis à la désinstallation).
    roots: list[Path] = []
    for it in items:
        # Un arbre étranger (extension installée à la main…) est sauvegardé en bloc, pas fichier par fichier.
        if extension and _under(it.dest, paths.extension_dir(EXT_UUID)):
            roots.append(paths.extension_dir(EXT_UUID))
        else:
            roots.append(it.dest)
    if venv:
        roots.append(venv_dir)
    seen: set[str] = set()
    _mkdirs(home, created)
    for r in roots:
        key = str(r)
        if key in seen:
            continue
        seen.add(key)
        if not _exists(r) or key in old_entries or key in backed:
            continue
        if r.is_dir() and not r.is_symlink() and any(e.startswith(key + os.sep) for e in old_entries):
            continue  # dossier qui contient nos fichiers (installation précédente)
        dst = _backup_path(home, r)
        _mkdirs(dst.parent, created)
        if _exists(dst):
            raise InstallError(f"sauvegarde déjà présente : {dst}")
        os.replace(r, dst)
        backups.append({"original": key, "backup": str(dst)})
        backed.add(key)
        log(f"sauvegardé : {r} → {dst}")

    # 2. Manifeste écrit AVANT la copie (une installation interrompue reste désinstallable).
    entries: list[dict] = []
    for it in items:
        if it.kind == "file":
            entries.append({"path": str(it.dest), "type": "file", "mode": oct(it.mode),
                            "sha256": sha256(it.src)})
        else:
            entries.append({"path": str(it.dest), "type": "symlink", "target": it.target})
    if venv:
        entries.append({"path": str(venv_dir), "type": "tree"})
    elif str(venv_dir) in old_entries:
        entries.append(old_entries[str(venv_dir)])
    if kb_stage is None:
        # Base FR générée par une installation précédente (--kb auto ne la reconstruit pas) : conservée.
        kb_dir = paths.kb_path().parent
        entries.extend(e for p, e in old_entries.items() if _under(Path(p), kb_dir) and _exists(Path(p)))
    entries.sort(key=lambda e: e["path"])     # manifeste déterministe (réinstallation = mêmes octets)
    man = {
        "marker": MARKER, "manifestVersion": MANIFEST_VERSION, "version": VERSION,
        "commit": _git_commit(repo), "source": str(repo),
        "installedAt": (old or {}).get("installedAt") or _now(),
        "home": str(home), "binDir": str(bin_dir), "status": "installing",
        "extension": str(paths.extension_dir(EXT_UUID)) if extension else None,
        "systemdUnit": str(paths.systemd_unit_path()) if systemd else None,
        "kb": str(paths.kb_path()) if any(e["path"] == str(paths.kb_path()) for e in entries) else None,
        "venv": {"path": str(venv_dir), "requirementsSha256": req_hash} if venv else (old or {}).get("venv"),
        "createdDirs": created, "backups": backups, "entries": entries,
    }
    _write_manifest(home, man)

    # 3. Copie (idempotente : fichier identique → rien).
    changed = 0
    for it in items:
        _mkdirs(it.dest.parent, created)
        if it.kind == "file":
            if _same_file(it.dest, it.src, it.mode):
                continue
            if it.dest.is_symlink() or it.dest.is_dir():
                _remove(it.dest)
            _copy(it.src, it.dest, it.mode)
        else:
            if it.dest.is_symlink() and os.readlink(it.dest) == it.target:
                continue
            if _exists(it.dest):
                _remove(it.dest)
            os.symlink(it.target, it.dest)
        changed += 1

    # 4. Entrées de l'ancienne installation qui ne font plus partie du plan.
    new_paths = {e["path"] for e in entries}
    for p, e in old_entries.items():
        if p not in new_paths and _exists(Path(p)):
            _remove(Path(p))
            log(f"retiré (plus installé) : {p}")

    # 5. venv (Textual) : recréé seulement si absent ou si requirements.txt a changé.
    if venv:
        old_hash = ((old or {}).get("venv") or {}).get("requirementsSha256")
        if not (venv_dir / "bin" / "python3").exists() or old_hash != req_hash:
            if _exists(venv_dir):
                shutil.rmtree(venv_dir)
            _make_venv(venv_dir, req, wheels, log)
            changed += 1
        else:
            log(f"venv inchangé : {venv_dir}")

    # Dossiers vides laissés par l'étape 4 : retirés s'ils ont été créés par l'installation.
    _prune(created, keep=new_paths | {str(home / "manifest.json")})
    man["createdDirs"] = created
    man["status"] = "complete"
    _write_manifest(home, man)
    log(f"{changed} élément(s) écrit(s), {len(entries)} entrée(s) au manifeste")
    return man


def _make_venv(venv_dir: Path, req: Path, wheels: Path | None, log) -> None:
    log(f"venv : {venv_dir}")
    subprocess.run([sys.executable, "-m", "venv", str(venv_dir)], check=True)
    pip = [str(venv_dir / "bin" / "python3"), "-m", "pip", "install", "--quiet", "--disable-pip-version-check",
           "--no-input", "--no-cache-dir", "-r", str(req)]
    if wheels is not None:
        pip += ["--no-index", "--find-links", str(wheels)]
    r = subprocess.run(pip)
    if r.returncode != 0:
        raise InstallError("pip install a échoué (réseau ? sinon --wheels DIR ou --no-venv)")


def _prune(created: list[str], keep: set[str]) -> None:
    for d in sorted(created, key=lambda s: s.count(os.sep), reverse=True):
        p = Path(d)
        if p.is_dir() and not p.is_symlink() and not any(k.startswith(d + os.sep) for k in keep):
            try:
                p.rmdir()
            except OSError:
                pass


def _git_commit(repo: Path) -> str | None:
    try:
        r = subprocess.run(["git", "-C", str(repo), "rev-parse", "--short", "HEAD"], capture_output=True, text=True)
    except OSError:
        return None
    return (r.stdout.strip() or None) if r.returncode == 0 else None


def _now() -> str:
    return _dt.datetime.now().astimezone().isoformat(timespec="seconds")


# ---------- désinstallation ----------

def uninstall_plan(man: dict, purge: bool) -> dict:
    """Ce qui sera retiré / remis en place (affichage + tests)."""
    remove = [e["path"] for e in man.get("entries", []) if _exists(Path(e["path"]))]
    restore = [b for b in man.get("backups", []) if _exists(Path(b["backup"]))]
    purged: list[str] = []
    if purge:
        for p in (paths.config_dir(), paths.log_dir(), paths.runtime_dir()):
            if _exists(p):
                purged.append(str(p))
    return {"remove": remove, "restore": restore, "purge": purged}


def _stop_installed_daemon(home: Path) -> list[int]:
    """Arrête SEULEMENT un daemon lancé depuis cette installation (exécutable sous $ULTIMA_HOME)."""
    mine = []
    for pid in procs.owned_pids():
        argv = procs.cmdline(pid)
        exe = argv[1] if argv and os.path.basename(argv[0]) == "dotnet" and len(argv) > 1 else (argv[0] if argv else "")
        if exe and _under(Path(os.path.realpath(exe)), Path(os.path.realpath(home))):
            mine.append(pid)
    if mine:
        procs.stop_pids(mine)
    return mine


def uninstall(purge: bool = False, log=print) -> int:
    home = paths.ultima_home()
    man = read_manifest(home)
    if man is None:
        raise InstallError(f"aucune installation trouvée dans {home} (manifest.json absent)")
    stopped = _stop_installed_daemon(home)
    if stopped:
        log(f"daemon arrêté (pid {' '.join(map(str, stopped))})")
    unit = man.get("systemdUnit")
    sc = procs.systemctl()
    if unit and sc and Path(unit).is_file():
        subprocess.run([sc, "--user", "disable", "--now", procs.UNIT], capture_output=True)
    if purge and procs.probe_socket() == "alive":
        raise InstallError("un daemon répond encore sur la socket : l'arrêter avant --purge (ultima kill)")
    removed = 0
    for e in man.get("entries", []):
        p = Path(e["path"])
        if not _exists(p):
            continue
        if e["type"] == "symlink" and not p.is_symlink():
            log(f"conservé (n'est plus notre lien) : {p}")
            continue
        if e["type"] == "file" and p.is_file() and e.get("sha256") and sha256(p) != e["sha256"]:
            log(f"retiré (modifié depuis l'installation) : {p}")
        _remove(p)
        removed += 1
    (home / "manifest.json").unlink(missing_ok=True)
    created = list(man.get("createdDirs", []))
    backup_root = str(home / "backup")
    # Dossiers vides créés par l'installation retirés AVANT de remettre les sauvegardes (un dossier
    # d'extension vide occuperait sinon la place de l'extension sauvegardée).
    _prune([d for d in created if not (d == backup_root or d.startswith(backup_root + os.sep))], keep=set())
    restored = 0
    for b in man.get("backups", []):
        src, dst = Path(b["backup"]), Path(b["original"])
        if not _exists(src):
            continue
        if _exists(dst):
            log(f"sauvegarde non remise (chemin occupé) : {src}")
            continue
        dst.parent.mkdir(parents=True, exist_ok=True)
        os.replace(src, dst)
        restored += 1
        log(f"remis en place : {dst}")
    _prune(created + [backup_root], keep=set())
    if unit and sc:
        subprocess.run([sc, "--user", "daemon-reload"], capture_output=True)
    if purge:
        for p in (paths.config_dir(), paths.log_dir(), paths.runtime_dir()):
            if _exists(p):
                _remove(p)
                log(f"purgé : {p}")
    log(f"{removed} élément(s) retiré(s), {restored} sauvegarde(s) remise(s)")
    return 0


# ---------- point d'entrée (tools/install.sh) ----------

def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="ultima-installer")
    sub = ap.add_subparsers(dest="cmd", required=True)
    i = sub.add_parser("install")
    i.add_argument("--repo", required=True, type=Path)
    i.add_argument("--stage", required=True, type=Path)
    i.add_argument("--no-extension", action="store_true")
    i.add_argument("--systemd", action="store_true")
    i.add_argument("--no-venv", action="store_true")
    i.add_argument("--wheels", type=Path)
    i.add_argument("--kb-stage", type=Path)
    u = sub.add_parser("uninstall")
    u.add_argument("--purge", action="store_true")
    sub.add_parser("manifest")
    a = ap.parse_args(argv)
    try:
        if a.cmd == "install":
            install(a.repo.resolve(), a.stage.resolve(), extension=not a.no_extension, systemd=a.systemd,
                    venv=not a.no_venv, wheels=a.wheels, kb_stage=a.kb_stage)
            return 0
        if a.cmd == "uninstall":
            return uninstall(purge=a.purge)
        man = read_manifest()
        print(json.dumps(man, ensure_ascii=False, indent=2) if man else "aucun manifeste")
        return 0 if man else 1
    except (InstallError, subprocess.CalledProcessError, OSError) as e:
        print(f"ultima install : {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
