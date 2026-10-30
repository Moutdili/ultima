"""Processus du daemon et HUD (extension GNOME Shell).

Sécurité : `ultima kill` n'arrête QUE le daemon de CE dossier IPC (PID du verrou `daemon.lock` du
daemon + PID noté par `ultima start`, chacun vérifié dans /proc/PID/cmdline). `ultima kill --all`
reprend l'ancien comportement (tous les `ultima-daemon run` de l'utilisateur).
"""

from __future__ import annotations

import os
import shutil
import signal
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path

from . import logs, paths
from .consolecfg import ConsoleConfig
from .state import ALIVE, probe_socket

EXT_UUID = "ultima@mio"
UNIT = "ultima-daemon.service"


# ---------- /proc ----------

def cmdline(pid: int) -> list[str]:
    try:
        raw = Path(f"/proc/{pid}/cmdline").read_bytes()
    except OSError:
        return []
    return [a.decode("utf-8", "replace") for a in raw.split(b"\0") if a]


def is_daemon_argv(argv: list[str]) -> bool:
    if not argv:
        return False
    exe = os.path.basename(argv[0])
    rest = argv[1:]
    if exe == "dotnet" and rest and os.path.basename(rest[0]) == "ultima-daemon.dll":
        rest = rest[1:]
    elif exe != "ultima-daemon":
        return False
    return bool(rest) and rest[0] == "run"


def all_daemon_pids() -> list[int]:
    uid = os.getuid()
    out = []
    for name in os.listdir("/proc"):
        if not name.isdigit():
            continue
        try:
            if os.stat(f"/proc/{name}").st_uid != uid:
                continue
        except OSError:
            continue
        if is_daemon_argv(cmdline(int(name))):
            out.append(int(name))
    return sorted(out)


def _pid_from(path: Path) -> int | None:
    try:
        v = path.read_text().strip()
    except OSError:
        return None
    return int(v) if v.isdigit() else None


def owned_pids() -> list[int]:
    """Daemon(s) de ce dossier IPC : verrou du daemon + PID noté par `ultima start`."""
    out = set()
    for p in (paths.lock_path(), paths.pid_path()):
        pid = _pid_from(p)
        if pid and is_daemon_argv(cmdline(pid)):
            out.add(pid)
    return sorted(out)


@dataclass
class ProcInfo:
    pid: int
    etime_s: float
    cpu_s: float
    rss_kb: int
    args: str

    def to_json(self) -> dict:
        return {"pid": self.pid, "elapsedS": round(self.etime_s), "cpuS": round(self.cpu_s, 2),
                "rssKb": self.rss_kb, "args": self.args}


def proc_info(pid: int) -> ProcInfo | None:
    try:
        stat = Path(f"/proc/{pid}/stat").read_text()
        uptime = float(Path("/proc/uptime").read_text().split()[0])
        status = Path(f"/proc/{pid}/status").read_text()
    except OSError:
        return None
    fields = stat[stat.rfind(")") + 2:].split()
    hz = os.sysconf("SC_CLK_TCK")
    utime, stime, start = int(fields[11]), int(fields[12]), int(fields[19])
    rss = 0
    for line in status.splitlines():
        if line.startswith("VmRSS:"):
            rss = int(line.split()[1])
    return ProcInfo(pid, uptime - start / hz, (utime + stime) / hz, rss, " ".join(cmdline(pid)))


def fmt_elapsed(s: float) -> str:
    s = int(s)
    d, s = divmod(s, 86400)
    h, s = divmod(s, 3600)
    m, s = divmod(s, 60)
    return (f"{d}j " if d else "") + (f"{h:02d}:{m:02d}:{s:02d}" if h or d else f"{m:02d}:{s:02d}")


# ---------- systemd (optionnel) ----------

def systemctl() -> str | None:
    if os.environ.get("ULTIMA_NO_SYSTEMCTL"):
        return None
    return shutil.which("systemctl")


def uses_systemd() -> bool:
    """L'unité utilisateur est installée (tools/install.sh --systemd) et systemctl est utilisable."""
    return paths.systemd_unit_path().is_file() and systemctl() is not None


def unit_active() -> bool:
    sc = systemctl()
    if not sc:
        return False
    r = subprocess.run([sc, "--user", "is-active", "--quiet", UNIT], capture_output=True)
    return r.returncode == 0


# ---------- start / stop ----------

@dataclass
class Result:
    ok: bool
    message: str
    pids: list[int]


def start(cfg: ConsoleConfig, extra: list[str] | None = None, wait_s: float = 2.0) -> Result:
    running = owned_pids()
    if running or probe_socket() == ALIVE:
        return Result(True, "ULTIMA tourne déjà", running)
    if uses_systemd() and not extra:
        sc = systemctl()
        r = subprocess.run([sc, "--user", "start", UNIT], capture_output=True, text=True)
        if r.returncode != 0:
            return Result(False, f"systemctl --user start {UNIT} : {r.stderr.strip()}", [])
        return _wait_up(wait_s, via="systemd")
    daemon = paths.find_daemon()
    if daemon is None:
        return Result(False, "binaire du daemon introuvable (tools/install.sh, ou ULTIMA_DAEMON=/chemin)", [])
    log = paths.log_path()
    paths.private_dir(log.parent)
    # Le daemon écrit lui-même daemon.log (et le fait tourner) ET en copie chaque ligne sur stderr :
    # stdout/stderr vont donc dans un fichier de capture à part (sinon chaque ligne serait en double),
    # utile seulement si le daemon meurt avant d'avoir ouvert son journal (runtime .NET absent…).
    capture = paths.daemon_capture_path()
    argv = daemon_argv(daemon, cfg, extra or [])
    with open(capture, "wb", buffering=0) as lf:
        lf.write(f"--- ultima start : {' '.join(argv)}\n".encode())
        proc = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=lf, stderr=lf,
                                start_new_session=True, env=paths.daemon_env(), close_fds=True)
    paths.private_dir(paths.runtime_dir())
    try:
        paths.pid_path().write_text(f"{proc.pid}\n")
    except OSError:
        pass
    deadline = time.monotonic() + wait_s
    while time.monotonic() < deadline:
        if proc.poll() is not None:
            tail = " ".join(ln.strip() for ln in logs.tail(capture, 4)[1:] if ln.strip())
            _cleanup_pidfile()
            return Result(False, f"le daemon s'est arrêté (code {proc.returncode}) : {tail or '?'}", [])
        if probe_socket() == ALIVE:
            return Result(True, f"ULTIMA lancé (pid {proc.pid})", [proc.pid])
        time.sleep(0.1)
    return Result(True, f"ULTIMA lancé (pid {proc.pid}, socket pas encore prête)", [proc.pid])


def daemon_argv(daemon: Path, cfg: ConsoleConfig, extra: list[str]) -> list[str]:
    """`ultima-daemon run` + options de console.json + `--log` (même journal que `ultima logs`)."""
    args = [*cfg.daemon_args(), *extra]
    if "--log" not in args and not any(a.startswith("--log=") for a in args):
        args += ["--log", str(paths.log_path())]
    return [str(daemon), "run", *args]


def _wait_up(wait_s: float, via: str) -> Result:
    deadline = time.monotonic() + wait_s
    while time.monotonic() < deadline:
        if probe_socket() == ALIVE:
            return Result(True, f"ULTIMA lancé ({via})", owned_pids())
        time.sleep(0.1)
    return Result(True, f"ULTIMA démarré ({via}), socket pas encore prête", owned_pids())


def stop(all_users_daemons: bool = False, grace_s: float = 5.0) -> Result:
    if uses_systemd() and unit_active():
        subprocess.run([systemctl(), "--user", "stop", UNIT], capture_output=True)
    pids = all_daemon_pids() if all_users_daemons else owned_pids()
    return stop_pids(pids, grace_s)


def stop_pids(pids: list[int], grace_s: float = 5.0) -> Result:
    """SIGTERM puis, après `grace_s`, SIGKILL — uniquement des PID dont /proc/PID/cmdline est un
    `ultima-daemon run` (jamais un PID réutilisé par un autre programme)."""
    pids = [p for p in pids if is_daemon_argv(cmdline(p))]
    if not pids:
        _cleanup_pidfile()
        return Result(True, "ULTIMA ne tourne pas", [])
    for p in pids:
        try:
            os.kill(p, signal.SIGTERM)
        except ProcessLookupError:
            pass
    deadline = time.monotonic() + grace_s
    while time.monotonic() < deadline:
        if not [p for p in pids if _alive(p)]:
            _cleanup_pidfile()
            return Result(True, f"ULTIMA arrêté (pid {' '.join(map(str, pids))})", pids)
        time.sleep(0.1)
    for p in pids:
        try:
            os.kill(p, signal.SIGKILL)
        except ProcessLookupError:
            pass
    _cleanup_pidfile()
    return Result(True, "ULTIMA tué (SIGKILL : ne répondait pas au SIGTERM)", pids)


def _alive(pid: int) -> bool:
    if not is_daemon_argv(cmdline(pid)):
        return False
    try:
        st = Path(f"/proc/{pid}/stat").read_text()
        return st[st.rfind(")") + 2] != "Z"
    except OSError:
        return False


def _cleanup_pidfile() -> None:
    try:
        paths.pid_path().unlink()
    except OSError:
        pass


def exec_daemon(cfg: ConsoleConfig, extra: list[str]) -> None:
    """Remplace le processus par le daemon (unité systemd : ExecStart=… ultima daemon-exec)."""
    daemon = paths.find_daemon()
    if daemon is None:
        raise SystemExit("ultima : binaire du daemon introuvable")
    argv = daemon_argv(daemon, cfg, extra)
    os.execve(str(daemon), argv, paths.daemon_env())


# ---------- HUD (extension GNOME Shell) ----------

def gnome_extensions() -> str | None:
    env = os.environ.get("ULTIMA_GNOME_EXTENSIONS")
    if env:
        return env
    return shutil.which("gnome-extensions")


def hud_status() -> dict:
    tool = gnome_extensions()
    installed_dir = paths.extension_dir(EXT_UUID)
    out = {"uuid": EXT_UUID, "tool": bool(tool), "installed": installed_dir.is_dir(), "path": str(installed_dir),
           "enabled": None, "state": None, "version": None}
    if not tool:
        return out
    try:
        r = subprocess.run([tool, "info", EXT_UUID], capture_output=True, text=True, timeout=5)
    except (OSError, subprocess.TimeoutExpired):
        return out
    if r.returncode != 0:
        return out
    for line in r.stdout.splitlines():
        k, _, v = line.strip().partition(": ")
        if k == "Enabled":
            out["enabled"] = v.strip().lower() in ("yes", "oui", "true")
        elif k == "State":
            out["state"] = v.strip()
        elif k == "Version":
            out["version"] = v.strip()
        elif k == "Path":
            out["path"] = v.strip()
            out["installed"] = True
    return out


def hud_set(on: bool) -> Result:
    tool = gnome_extensions()
    if not tool:
        return Result(False, "gnome-extensions introuvable (le HUD est une extension GNOME Shell)", [])
    r = subprocess.run([tool, "enable" if on else "disable", EXT_UUID], capture_output=True, text=True)
    if r.returncode != 0:
        return Result(False, (r.stderr or r.stdout).strip() or f"échec (code {r.returncode})", [])
    return Result(True, f"HUD {'activé' if on else 'désactivé'} ({EXT_UUID})", [])
