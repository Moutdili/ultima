namespace Ultima.Mirror;

public sealed record ProcessTarget(int Pid, string Comm, string? CoreClrPath, string? DacPath, string DacSource);

/// <summary>
/// Finds the game process from the HOST and the DAC to use. Read-only: only /proc text files
/// and file existence checks. Never signals, never ptraces.
/// </summary>
public static class GameProcess
{
    public const string GameComm = "SlayTheSpire2";

    public static readonly string DefaultGameDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2");

    public static string DefaultDac => Path.Combine(DefaultGameDir, "data_sts2_linuxbsd_x86_64", "libmscordaccore.so");
    public static string DefaultSts2Dll => Path.Combine(DefaultGameDir, "data_sts2_linuxbsd_x86_64", "sts2.dll");

    /// <summary>
    /// Exact match on /proc/PID/comm (NOT `pgrep -f`, which also matches any shell whose command
    /// line merely contains the word — observed 2026-10-28).
    /// </summary>
    public static IReadOnlyList<int> FindByComm(string comm)
    {
        var self = Environment.ProcessId;
        var list = new List<int>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid) || pid == self) continue;
            try
            {
                if (File.ReadAllText(Path.Combine(dir, "comm")).Trim() == comm) list.Add(pid);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return list;
    }

    /// <summary>Path of libcoreclr.so as seen in the target's OWN mount namespace (/proc/PID/maps).</summary>
    public static string? CoreClrPathFromMaps(int pid)
    {
        try
        {
            foreach (var line in File.ReadLines($"/proc/{pid}/maps"))
            {
                var idx = line.IndexOf('/');
                if (idx < 0) continue;
                var path = line[idx..];
                if (path.EndsWith("/libcoreclr.so", StringComparison.Ordinal)) return path;
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>
    /// DAC resolution order: explicit --dac; sibling of libcoreclr.so through /proc/PID/root (the
    /// target's own mount namespace — exact even under bubblewrap/Flatpak); same path on the host; default game dir.
    /// </summary>
    public static ProcessTarget Describe(int pid, string? explicitDac)
    {
        string comm = "?";
        try { comm = File.ReadAllText($"/proc/{pid}/comm").Trim(); } catch { }
        var coreclr = CoreClrPathFromMaps(pid);
        if (explicitDac is not null) return new(pid, comm, coreclr, explicitDac, "--dac");
        if (coreclr is not null)
        {
            var sibling = Path.Combine(Path.GetDirectoryName(coreclr)!, "libmscordaccore.so");
            var viaRoot = $"/proc/{pid}/root{sibling}";
            if (File.Exists(viaRoot)) return new(pid, comm, coreclr, viaRoot, "à côté de libcoreclr.so via /proc/PID/root (espace de noms de la cible)");
            if (File.Exists(sibling)) return new(pid, comm, coreclr, sibling, "à côté de libcoreclr.so (chemin hôte)");
        }
        if (File.Exists(DefaultDac)) return new(pid, comm, coreclr, DefaultDac, "dossier du jeu par défaut");
        return new(pid, comm, coreclr, null, "introuvable");
    }
}
