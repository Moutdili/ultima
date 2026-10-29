using System.Security.Cryptography;
using System.Text;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Publish;

/// <summary>Destination d'un état publié (fichier, socket, tests).</summary>
public interface IStateSink
{
    void Publish(EngineState state, string line);
}

/// <summary>
/// Publication du contrat : <c>$XDG_RUNTIME_DIR/ultima/state.json</c> réécrit ATOMIQUEMENT (fichier
/// temporaire dans le même dossier puis rename(2)) + diffusion d'une ligne JSON sur <c>state.sock</c>.
/// Seulement si l'état a CHANGÉ : empreinte SHA-256 de l'état sans son horodatage <c>at</c>.
/// </summary>
public sealed class StatePublisher : IDisposable
{
    private readonly string _dir;
    private readonly List<IStateSink> _sinks = new();
    private byte[]? _lastHash;

    public StatePublisher(string dir)
    {
        _dir = dir;
        EnsurePrivateDir(dir);
    }

    public string StatePath => Path.Combine(_dir, "state.json");
    public string TempPath => Path.Combine(_dir, ".state.json.tmp");
    public int Published { get; private set; }
    public int Skipped { get; private set; }
    public EngineState? Last { get; private set; }
    public string? LastLine { get; private set; }

    public void AddSink(IStateSink sink) => _sinks.Add(sink);

    /// <summary>Dossier d'exécution : <c>$XDG_RUNTIME_DIR/ultima</c> (repli /tmp/ultima-UID), droits 0700.</summary>
    public static string DefaultDir()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(xdg) || !Directory.Exists(xdg))
            return Path.Combine(Path.GetTempPath(), $"ultima-{Environment.UserName}");
        return Path.Combine(xdg, "ultima");
    }

    internal static void EnsurePrivateDir(string dir)
    {
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Empreinte de changement : tout l'état sauf <c>at</c>.</summary>
    public static byte[] Fingerprint(EngineState s) =>
        SHA256.HashData(StateJson.SerializeUtf8(s with { At = DateTimeOffset.UnixEpoch }));

    /// <returns>true si publié, false si identique au précédent.</returns>
    public bool Publish(EngineState state)
    {
        var hash = Fingerprint(state);
        if (_lastHash is not null && hash.AsSpan().SequenceEqual(_lastHash)) { Skipped++; return false; }
        _lastHash = hash;
        var line = StateJson.Serialize(state);
        WriteAtomic(StatePath, TempPath, line + "\n");
        Last = state;
        LastLine = line;
        Published++;
        foreach (var s in _sinks)
        {
            try { s.Publish(state, line); }
            catch (Exception e) { Diag.Warn($"diffusion : {e.Message}"); }
        }
        return true;
    }

    /// <summary>Écriture atomique : tmp (même système de fichiers) + flush + rename(2).</summary>
    internal static void WriteAtomic(string path, string tmp, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(fs.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true); // rename(2) : les lecteurs voient l'ancien OU le nouveau fichier entier
    }

    public void Dispose()
    {
        foreach (var s in _sinks) (s as IDisposable)?.Dispose();
    }
}
