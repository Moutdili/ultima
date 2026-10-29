namespace Ultima.Daemon.Sources;

/// <summary>
/// Une seule instance par utilisateur : verrou exclusif (flock via FileShare.None) sur
/// <c>$XDG_RUNTIME_DIR/ultima/daemon.lock</c>, qui contient le PID. Libéré à la mort du processus.
/// </summary>
public sealed class DaemonLock : IDisposable
{
    private readonly FileStream _fs;

    private DaemonLock(FileStream fs) => _fs = fs;

    public static DaemonLock? TryAcquire(string dir, out string? holder)
    {
        holder = null;
        var path = Path.Combine(dir, "daemon.lock");
        try
        {
            var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            fs.SetLength(0);
            var pid = System.Text.Encoding.ASCII.GetBytes(Environment.ProcessId + "\n");
            fs.Write(pid);
            fs.Flush();
            return new DaemonLock(fs);
        }
        catch (IOException)
        {
            try { holder = File.ReadAllText(path).Trim(); } catch { }
            return null;
        }
    }

    public void Dispose() => _fs.Dispose();
}
