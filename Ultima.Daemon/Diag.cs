namespace Ultima.Daemon;

/// <summary>
/// Journal minimal sur stderr (capté par journald sous systemd). Une ligne par événement utile ;
/// jamais de journal par sondage (il n'y a pas de sondage).
/// </summary>
public static class Diag
{
    private static readonly object Gate = new();
    public static bool Verbose { get; set; }

    /// <summary>Remplaçable par les tests (capture).</summary>
    public static Action<string>? Sink { get; set; }

    public static void Info(string msg) => Write("info", msg);
    public static void Warn(string msg) => Write("warn", msg);
    public static void Debug(string msg) { if (Verbose) Write("debug", msg); }

    private static void Write(string level, string msg)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss.fff} {level} {msg}";
        if (Sink is { } s) { s(line); return; }
        lock (Gate) Console.Error.WriteLine(line);
    }
}
