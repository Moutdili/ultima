using System.Runtime.InteropServices;

namespace Ultima.Daemon.Sources;

/// <summary>
/// Sait si le jeu tourne, SANS sondage :
/// - lancement : <see cref="Rescan"/> est appelé par les événements inotify des dossiers du jeu (au
///   démarrage, le jeu fait tourner <c>logs/godot.log</c>) et au démarrage du daemon ;
/// - arrêt : <c>pidfd_open(2)</c> + <c>poll(2)</c> bloquant sur un fil dédié (0 % CPU), notifié à la mort.
/// Lecture seule : uniquement <c>/proc/PID/comm</c>. Aucun signal, aucun ptrace.
/// </summary>
public sealed class GameProcessMonitor : IDisposable
{
    private readonly string _comm;
    private readonly object _gate = new();
    private int? _pid;
    private bool _disposed;

    public event Action<bool, int?>? Changed;

    public GameProcessMonitor(string comm) => _comm = comm;

    public int? Pid { get { lock (_gate) return _pid; } }
    public bool Running => Pid is not null;

    /// <summary>Correspondance exacte sur /proc/PID/comm (pas « pgrep -f »).</summary>
    public static int? FindByComm(string comm)
    {
        var self = Environment.ProcessId;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(System.IO.Path.GetFileName(dir), out var pid) || pid == self) continue;
                try { if (File.ReadAllText(System.IO.Path.Combine(dir, "comm")).TrimEnd('\n') == comm) return pid; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        return null;
    }

    /// <summary>Cherche le jeu s'il n'est pas déjà suivi. Appelé sur événement seulement.</summary>
    public void Rescan()
    {
        lock (_gate) if (_disposed || _pid is not null) return;
        var pid = FindByComm(_comm);
        if (pid is null) return;
        lock (_gate)
        {
            if (_pid is not null) return;
            _pid = pid;
        }
        Diag.Info($"jeu détecté : pid {pid}");
        Changed?.Invoke(true, pid);
        WatchExit(pid.Value);
    }

    private void WatchExit(int pid)
    {
        var t = new Thread(() =>
        {
            WaitForExit(pid);
            lock (_gate)
            {
                if (_pid != pid) return;
                _pid = null;
            }
            Diag.Info($"jeu arrêté : pid {pid}");
            Changed?.Invoke(false, null);
            Rescan(); // une autre instance ?
        })
        { IsBackground = true, Name = "ultima-game-exit" };
        t.Start();
    }

    private const long SYS_pidfd_open = 434; // x86_64 et aarch64
    private const short POLLIN = 0x001;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd { public int Fd; public short Events; public short Revents; }

    [DllImport("libc", SetLastError = true)] private static extern int syscall(long number, int pid, uint flags);
    [DllImport("libc", SetLastError = true)] private static extern unsafe int poll(PollFd* fds, ulong nfds, int timeout);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);

    /// <summary>Bloque jusqu'à la mort du processus (pidfd lisible). Repli : attente sur /proc/PID, rare.</summary>
    internal static unsafe void WaitForExit(int pid)
    {
        int fd = syscall(SYS_pidfd_open, pid, 0);
        if (fd < 0)
        {
            Diag.Warn($"pidfd_open indisponible (errno {Marshal.GetLastPInvokeError()}) : surveillance par inotify de /proc impossible, repli 5 s");
            while (Directory.Exists($"/proc/{pid}")) Thread.Sleep(5000);
            return;
        }
        try
        {
            var p = new PollFd { Fd = fd, Events = POLLIN };
            while (true)
            {
                int r = poll(&p, 1, -1);
                if (r > 0) return;
                if (r < 0 && Marshal.GetLastPInvokeError() != 4 /* EINTR */) return;
            }
        }
        finally { close(fd); }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
    }
}
