"""Installation/désinstallation pilotées par le manifeste, dans un HOME temporaire (dépôt et build factices).

Preuve centrale : arbre du HOME identique avant install et après uninstall ; réinstallation sans écriture.
Le venv (réseau) et le build dotnet sont prouvés par le scénario réel de status/console.md.
"""

import hashlib
import json
import os
import shutil
import subprocess

import pytest
from conftest import CONSOLE, REPO

from ultima_console import installer, paths


def snapshot(root):
    out = {}
    for dirpath, dirs, files in os.walk(root):
        for name in dirs + files:
            p = os.path.join(dirpath, name)
            rel = os.path.relpath(p, root)
            st = os.lstat(p)
            if os.path.islink(p):
                out[rel] = ("l", os.readlink(p))
            elif os.path.isdir(p):
                out[rel] = ("d", oct(st.st_mode & 0o777))
            else:
                out[rel] = ("f", oct(st.st_mode & 0o777), hashlib.sha256(open(p, "rb").read()).hexdigest())
    return out


@pytest.fixture
def fake_repo(tmp_path):
    repo = tmp_path / "repo"
    c = repo / "tools" / "console"
    c.mkdir(parents=True)
    shutil.copy2(CONSOLE / "ultima", c / "ultima")
    shutil.copy2(CONSOLE / "requirements.txt", c / "requirements.txt")
    shutil.copytree(CONSOLE / "ultima_console", c / "ultima_console", ignore=shutil.ignore_patterns("__pycache__"))
    (repo / "data" / "meta").mkdir(parents=True)
    (repo / "data" / "meta" / "card_tiers.json").write_text('{"x": 1}')
    (repo / "data" / "catalog").mkdir()
    (repo / "data" / "catalog" / "cards.json").write_text("[]")
    ext = repo / "overlay" / "gnome" / "ultima@mio"
    (ext / "schemas").mkdir(parents=True)
    (ext / "metadata.json").write_text('{"uuid": "ultima@mio"}')
    (ext / "extension.js").write_text("// ext")
    stage = tmp_path / "stage"
    for name, exe in (("daemon", "ultima-daemon"), ("mirror", "ultima-mirror")):
        (stage / name).mkdir(parents=True)
        (stage / name / exe).write_text("#!/bin/sh\necho fake\n")
        (stage / name / exe).chmod(0o755)
        (stage / name / f"{exe}.dll").write_text("dll")
    return repo, stage


def realistic_home():
    home = paths.home()
    (home / ".config" / "autre").mkdir(parents=True)
    (home / ".local" / "state").mkdir(parents=True)
    (home / ".bashrc").write_text("# perso\n")


def test_install_uninstall_tree_identical_with_backup(fake_repo):
    repo, stage = fake_repo
    realistic_home()
    old = paths.bin_dir() / "ultima"
    old.parent.mkdir(parents=True)
    old.write_text("#!/bin/bash\nexec /home/autre-agent/worktree/ultima \"$@\"\n")
    old.chmod(0o755)
    kb = paths.kb_path()                                   # base FR non gérée déjà présente
    kb.parent.mkdir(parents=True)
    kb.write_text("{}")
    before = snapshot(paths.home())

    man = installer.install(repo, stage, venv=False, log=lambda m: None)
    link = paths.bin_dir() / "ultima"
    assert link.is_symlink() and os.readlink(link) == str(paths.ultima_home() / "bin" / "ultima")
    assert (paths.ultima_home() / "bin" / "ultima-daemon").resolve() == paths.ultima_home() / "lib/daemon/ultima-daemon"
    assert (paths.ultima_home() / "data" / "meta" / "card_tiers.json").is_file()
    assert (paths.extension_dir() / "extension.js").is_file()
    assert man["status"] == "complete" and man["backups"][0]["original"] == str(old)
    assert kb.read_text() == "{}" and man["kb"] is None
    r = subprocess.run([str(link), "version", "--json"], capture_output=True, text=True)
    assert r.returncode == 0 and json.loads(r.stdout)["home"] == str(paths.ultima_home())
    assert not list(paths.ultima_home().rglob("__pycache__"))    # lanceur -B

    installer.uninstall(log=lambda m: None)
    assert snapshot(paths.home()) == before
    assert old.read_text().startswith("#!/bin/bash\nexec /home/autre-agent")


def test_reinstall_is_idempotent(fake_repo):
    repo, stage = fake_repo
    logs = []
    installer.install(repo, stage, venv=False, log=logs.append)
    first = snapshot(paths.home())
    logs.clear()
    installer.install(repo, stage, venv=False, log=logs.append)
    assert snapshot(paths.home()) == first
    assert logs[-1].startswith("0 élément(s) écrit(s)")


def test_reinstall_removes_stale_entries(fake_repo):
    repo, stage = fake_repo
    installer.install(repo, stage, venv=False, log=lambda m: None)
    shutil.rmtree(repo / "data" / "catalog")
    installer.install(repo, stage, venv=False, extension=False, log=lambda m: None)
    assert not (paths.ultima_home() / "data" / "catalog").exists()
    assert not paths.extension_dir().exists()
    installer.uninstall(log=lambda m: None)
    assert not paths.ultima_home().exists() and not paths.bin_dir().exists()


def test_generated_kb_survives_reinstall_without_kb(fake_repo, tmp_path):
    repo, stage = fake_repo
    kb_stage = tmp_path / "kb-stage"
    kb_stage.mkdir()
    (kb_stage / "kb.json").write_text('{"cards": {}}')
    man = installer.install(repo, stage, venv=False, kb_stage=kb_stage, log=lambda m: None)
    assert man["kb"] == str(paths.kb_path()) and paths.kb_path().is_file()
    first = snapshot(paths.home())
    man = installer.install(repo, stage, venv=False, log=lambda m: None)   # --kb auto : déjà présente
    assert snapshot(paths.home()) == first and man["kb"] == str(paths.kb_path())
    installer.uninstall(log=lambda m: None)
    assert not paths.kb_path().exists() and not paths.home().joinpath(".local").exists()


def test_foreign_extension_is_backed_up_and_restored(fake_repo):
    repo, stage = fake_repo
    ext = paths.extension_dir()
    ext.mkdir(parents=True)
    (ext / "extension.js").write_text("// version installée à la main")
    before = snapshot(paths.home())
    installer.install(repo, stage, venv=False, log=lambda m: None)
    assert (ext / "extension.js").read_text() == "// ext"
    installer.uninstall(log=lambda m: None)
    assert snapshot(paths.home()) == before


def test_modified_file_removed_and_foreign_manifest_refused(fake_repo):
    repo, stage = fake_repo
    installer.install(repo, stage, venv=False, log=lambda m: None)
    target = paths.ultima_home() / "data" / "meta" / "card_tiers.json"
    target.write_text("modifié")
    logs = []
    installer.uninstall(log=logs.append)
    assert any("modifié depuis l'installation" in m for m in logs) and not target.exists()
    paths.ultima_home().mkdir(parents=True)
    (paths.ultima_home() / "manifest.json").write_text('{"marker": "autre"}')
    with pytest.raises(installer.InstallError):
        installer.install(repo, stage, venv=False, log=lambda m: None)
    with pytest.raises(installer.InstallError):
        installer.uninstall(log=lambda m: None)


def test_purge_removes_settings_state_and_ipc(fake_repo, fake_socket):
    repo, stage = fake_repo
    realistic_home()
    before = snapshot(paths.home())
    installer.install(repo, stage, venv=False, log=lambda m: None)
    from ultima_console import settings
    settings.save(settings.OverlaySettings.default())
    paths.log_path().parent.mkdir(parents=True)
    paths.log_path().write_text("2026-10-30 12:00:00.000 INFO  x\n")
    sock = fake_socket()
    with pytest.raises(installer.InstallError):           # un daemon répond encore
        installer.uninstall(purge=True, log=lambda m: None)
    sock.close()
    plan = installer.uninstall_plan(installer.read_manifest(), purge=True)
    assert str(paths.config_dir()) in plan["purge"] and str(paths.log_dir()) in plan["purge"]
    installer.uninstall(purge=True, log=lambda m: None)
    assert snapshot(paths.home()) == before and not paths.runtime_dir().exists()


def test_systemd_unit_written_never_enabled(fake_repo, monkeypatch):
    repo, stage = fake_repo
    called = []
    real_run = subprocess.run
    # context() et non undo() : undo() annulerait aussi l'isolation du conftest (HOME, XDG_*).
    with monkeypatch.context() as m:
        m.setattr(installer.subprocess, "run", lambda *a, **k: called.append(a) or real_run(*a, **k))
        installer.install(repo, stage, venv=False, systemd=True, log=lambda m: None)
    unit = paths.systemd_unit_path().read_text()
    assert f"ExecStart={paths.ultima_home()}/bin/ultima daemon-exec" in unit
    assert not any("systemctl" in str(a) for a in called)
    installer.uninstall(log=lambda m: None)               # ULTIMA_NO_SYSTEMCTL : aucun appel réel
    assert not paths.systemd_unit_path().exists()


def test_cli_uninstall_needs_confirmation(fake_repo, capsys, monkeypatch):
    from ultima_console import cli
    repo, stage = fake_repo
    installer.install(repo, stage, venv=False, log=lambda m: None)
    monkeypatch.setattr("sys.stdin", open(os.devnull))
    assert cli.main(["uninstall", "--purge"]) == 2
    assert paths.manifest_path().exists()
    assert cli.main(["uninstall", "-y", "--color", "never"]) == 0
    assert "ULTIMA désinstallé" in capsys.readouterr().out and not paths.ultima_home().exists()


def test_install_sh_check_writes_nothing(env):
    home = paths.home()
    before = snapshot(home)
    r = subprocess.run(["bash", str(REPO / "tools" / "install.sh"), "--check", "--no-build", "--build-dir",
                        str(env / "nulle-part")], capture_output=True, text=True, env=dict(os.environ, NO_COLOR="1"))
    assert r.returncode == 1 and "prérequis manquants" in r.stderr and "\x1b[" not in r.stdout
    assert snapshot(home) == before
    r = subprocess.run(["bash", str(REPO / "tools" / "install.sh"), "--kb", "peut-être"], capture_output=True, text=True)
    assert r.returncode == 1 and "--kb auto|yes|no" in r.stderr
    r = subprocess.run(["bash", str(REPO / "tools" / "uninstall.sh"), "-y"], capture_output=True, text=True)
    assert r.returncode == 1 and "aucune installation" in r.stderr
