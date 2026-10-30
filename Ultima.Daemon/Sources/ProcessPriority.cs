using System.Runtime.InteropServices;

namespace Ultima.Daemon.Sources;

/// <summary>
/// Sobriété (skill sts2-performance : « daemon nice 10 ») : nice 10 + classe d'E/S « idle » sur TOUS
/// les fils existants (sous Linux la priorité est par fil ; les nouveaux fils héritent de leur créateur).
/// L'unité systemd fixe aussi Nice=10 / IOSchedulingClass=idle dès l'exec : ceci couvre les lancements manuels.
/// </summary>
public static class ProcessPriority
{
    private const int PRIO_PROCESS = 0;
    private const long SYS_ioprio_set = 251; // x86_64
    private const int IOPRIO_WHO_PROCESS = 1;
    private const int IOPRIO_CLASS_IDLE = 3;
    private const int IOPRIO_CLASS_SHIFT = 13;

    [DllImport("libc", SetLastError = true)] private static extern int setpriority(int which, uint who, int prio);
    [DllImport("libc", SetLastError = true)] private static extern int getpriority(int which, uint who);
    [DllImport("libc", SetLastError = true)] private static extern long syscall(long number, int which, int who, int ioprio);

    /// <summary>
    /// Fixe le nice d'un processus enfant (plugin : « nice 19 », docs/44 §7). Ne lève jamais : un échec
    /// (processus déjà mort, droits) est seulement journalisé par l'appelant.
    /// </summary>
    /// <returns>true si le nice a bien été appliqué.</returns>
    public static bool SetNice(int pid, int nice)
    {
        if (pid <= 0 || !OperatingSystem.IsLinux()) return false;
        try
        {
            if (setpriority(PRIO_PROCESS, (uint)pid, nice) != 0) return false;
            syscall(SYS_ioprio_set, IOPRIO_WHO_PROCESS, pid, IOPRIO_CLASS_IDLE << IOPRIO_CLASS_SHIFT);
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <returns>nombre de fils ajustés.</returns>
    public static int Lower(int nice = 10)
    {
        if (!OperatingSystem.IsLinux()) return 0;
        int n = 0;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc/self/task"))
            {
                if (!uint.TryParse(Path.GetFileName(dir), out var tid)) continue;
                // ne jamais AUGMENTER la priorité (un nice plus élevé imposé par systemd est conservé)
                Marshal.SetLastPInvokeError(0);
                int cur = getpriority(PRIO_PROCESS, tid);
                if (Marshal.GetLastPInvokeError() == 0 && cur >= nice) { n++; }
                else if (setpriority(PRIO_PROCESS, tid, nice) == 0) n++;
                syscall(SYS_ioprio_set, IOPRIO_WHO_PROCESS, (int)tid, IOPRIO_CLASS_IDLE << IOPRIO_CLASS_SHIFT);
            }
        }
        catch (IOException) { }
        return n;
    }
}
