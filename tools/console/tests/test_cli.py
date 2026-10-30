"""Sous-commandes : codes de sortie, --json, couleur (--color, NO_COLOR, FORCE_COLOR), journal filtré."""

from ultima_console import CONTRACT_MAJOR, CONTRACT_MINOR
import json
import os
import subprocess
import sys

import pytest
from conftest import CONSOLE, load_fixture, publish

from ultima_console import cli, paths

ESC = "\x1b["
GOLD_BOLD = "\x1b[1;38;2;239;200;81m"


def run(capsys, *args):
    code = cli.main(list(args))
    out = capsys.readouterr()
    return code, out.out, out.err


def test_help_and_unknown(capsys, monkeypatch):
    code, out, _ = run(capsys, "--help")
    assert code == 0 and "ultima status" in out and "NO_COLOR" in out and ESC not in out
    code, out, _ = run(capsys, "help", "--color", "always")
    assert f"{GOLD_BOLD}  ultima status\x1b[0m" in out           # commande en or
    assert "\x1b[38;2;191;191;191m" in out                       # description en secondaire
    monkeypatch.setenv("FORCE_COLOR", "1")
    monkeypatch.setenv("NO_COLOR", "1")
    assert ESC not in run(capsys, "-h")[1]
    code, _, err = run(capsys, "nimporte")
    assert code == 2 and "commande inconnue" in err


def test_status_exit_3_when_daemon_unreachable(capsys):
    code, out, _ = run(capsys, "status")
    assert code == 3 and "daemon arrêté" in out and "Aucun état publié." in out
    publish(load_fixture("reward"))
    code, out, _ = run(capsys, "status")
    assert code == 3 and "RÉCOMPENSE" in out          # état lisible mais daemon injoignable → 3
    assert ESC not in out                               # stdout non TTY → pas de couleur


def test_status_exit_0_with_daemon(capsys, fake_socket):
    publish(load_fixture("reward"))
    fake_socket()
    code, out, _ = run(capsys, "status")
    assert code == 0 and "daemon ✓" in out


def test_no_argument_without_tty_is_status(capsys):
    code, out, _ = run(capsys)
    assert code == 3 and "daemon arrêté" in out


def test_status_json(capsys, fake_socket):
    code, out, _ = run(capsys, "status", "--json")
    d = json.loads(out)
    assert code == 3 and d["daemon"]["reachable"] is False and d["state"]["kind"] == "missing" and d["engine"] is None
    publish(load_fixture("v13"))
    fake_socket()
    code, out, _ = run(capsys, "--json", "status")
    d = json.loads(out)
    assert code == 0 and d["daemon"]["reachable"] is True and d["engine"]["contractMinor"] == 3


def test_color_always_never_and_no_color(capsys, monkeypatch):
    publish(load_fixture("reward"))
    _, out, _ = run(capsys, "status", "--color", "always")
    assert f"{GOLD_BOLD}S\x1b[0m" in out                     # S en or, gras
    _, out, _ = run(capsys, "status", "--color=never")
    assert ESC not in out
    monkeypatch.setenv("FORCE_COLOR", "1")
    _, out, _ = run(capsys, "status")
    assert ESC in out
    monkeypatch.setenv("NO_COLOR", "1")
    _, out, _ = run(capsys, "status")
    assert ESC not in out                                    # NO_COLOR prime sur FORCE_COLOR


def write_log():
    p = paths.log_path()
    p.parent.mkdir(parents=True)
    p.write_text("2026-10-29 17:54:04.243 INFO  démarré\n2026-10-29 17:54:04.250 DEBUG mirror ms=12\n"
                 "2026-10-29 17:54:05.001 WARN  ptrace_scope=1\n2026-10-29 17:54:06.002 ERROR save illisible\n")


def test_logs_filtered_and_colored(capsys):
    code, _, err = run(capsys, "logs")
    assert code == 1 and "aucun journal" in err
    write_log()
    code, out, _ = run(capsys, "logs", "--level", "warn")
    assert code == 0 and out.splitlines() == ["2026-10-29 17:54:05.001 WARN  ptrace_scope=1",
                                              "2026-10-29 17:54:06.002 ERROR save illisible"]
    _, out, _ = run(capsys, "logs", "--grep", "mirror", "-n", "10")
    assert out.splitlines() == ["2026-10-29 17:54:04.250 DEBUG mirror ms=12"]
    _, out, _ = run(capsys, "logs", "-n", "1", "--color", "always")
    assert "\x1b[1;38;2;255;85;85mERROR\x1b[0m" in out       # error = red (défaut)
    paths.config_dir().mkdir(parents=True)
    paths.console_config_path().write_text('{"logColors": {"error": "aqua"}}')
    _, out, _ = run(capsys, "logs", "-n", "1", "--color", "always")
    assert "\x1b[1;38;2;42;235;190mERROR\x1b[0m" in out      # couleur personnalisée (console.json)


def test_logs_json(capsys):
    write_log()
    code, out, _ = run(capsys, "logs", "--json", "--level", "info")
    rows = [json.loads(ln) for ln in out.splitlines()]
    assert code == 0 and [r["level"] for r in rows] == ["info", "warn", "error"]
    assert rows[0] == {"ts": "2026-10-29 17:54:04.243", "level": "info", "msg": "démarré"}


def test_hud_status_and_toggle_use_fake_tool(capsys, env):
    code, out, _ = run(capsys, "hud", "status", "--json")
    d = json.loads(out)
    assert d["enabled"] is True and d["state"] == "ACTIVE" and d["settings"]["quality"] == "verre"
    assert code == 1 and d["installed"] is False             # dossier de l'extension absent
    paths.extension_dir().mkdir(parents=True)
    code, out, _ = run(capsys, "hud", "status", "--color", "never")
    assert code == 0 and "activé · ACTIVE" in out and "verre · hauteur 55 %" in out
    code, out, _ = run(capsys, "hud", "off")
    assert code == 0 and "HUD désactivé" in out
    calls = (env / "fake-bin" / "gnome-extensions.calls").read_text().splitlines()
    assert calls[-1] == "disable ultima@mio"
    code, _, err = run(capsys, "hud", "bascule")
    assert code == 2


def test_version_and_doctor_json(capsys):
    code, out, _ = run(capsys, "version", "--json")
    d = json.loads(out)
    assert code == 0 and d["contract"] == f"{CONTRACT_MAJOR}.{CONTRACT_MINOR}" and d["commit"] is None
    code, out, _ = run(capsys, "doctor", "--quick", "--json")
    d = json.loads(out)
    labels = {c["label"]: c["status"] for c in d["checks"]}
    assert labels["installation"] == "warn" and labels["commande"] == "fail" and code == 1


def test_ps_json_is_a_list(capsys):
    code, out, _ = run(capsys, "ps", "--json")
    assert code == 0 and isinstance(json.loads(out), list)       # lecture seule de /proc


def test_uninstall_without_install(capsys):
    code, _, err = run(capsys, "uninstall", "-y")
    assert code == 1 and "aucune installation" in err


@pytest.mark.parametrize("extra_env,colored", [({}, False), ({"FORCE_COLOR": "1"}, True),
                                               ({"FORCE_COLOR": "1", "NO_COLOR": "1"}, False)])
def test_launcher_subprocess(extra_env, colored):
    publish(load_fixture("shop"))
    e = dict(os.environ, **extra_env, ULTIMA_PYTHON=sys.executable)
    r = subprocess.run([str(CONSOLE / "ultima"), "status"], capture_output=True, text=True, env=e)
    assert r.returncode == 3 and "SHOP" in r.stdout
    assert (ESC in r.stdout) is colored
