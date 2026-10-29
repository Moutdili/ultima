using System.Text;

namespace Ultima.Daemon;

/// <summary>
/// Journal du daemon. Fichier : <c>$XDG_STATE_HOME/ultima/daemon.log</c> (<c>~/.local/state/ultima/daemon.log</c>,
/// jamais à la racine de ~), une ligne par événement utile, jamais par sondage (il n'y a pas de sondage).
/// Format stable pour le TUI `ultima` (couleur par niveau) :
/// <code>2026-10-29 17:54:04.243 INFO  message</code> — niveau ∈ INFO | WARN | ERROR | DEBUG, aligné sur 5.
/// Copie sur stderr (journald sous systemd). Rotation simple au démarrage (&gt; 5 Mio → daemon.log.1).
/// </summary>
public static class Diag
{
    private static readonly object Gate = new();
    private static StreamWriter? _file;

    public static bool Verbose { get; set; }

    /// <summary>Copie sur stderr (désactivée par les tests ou si le journal fichier suffit).</summary>
    public static bool Stderr { get; set; } = true;

    /// <summary>Remplaçable par les tests (capture). Reçoit la ligne formatée.</summary>
    public static Action<string>? Sink { get; set; }

    public static string? FilePath { get; private set; }

    public const long RotateBytes = 5 * 1024 * 1024;

    public static string DefaultLogPath()
    {
        var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state))
            state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(state, "ultima", "daemon.log");
    }

    /// <summary>Ouvre le journal fichier (ajout). Erreur d'E/S : on continue sur stderr seulement.</summary>
    public static void OpenFile(string path)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > RotateBytes) File.Move(path, path + ".1", overwrite: true);
                var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                _file = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
                FilePath = path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"journal fichier indisponible ({path}) : {e.Message}");
            }
        }
    }

    public static void CloseFile()
    {
        lock (Gate) { _file?.Dispose(); _file = null; }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);
    public static void Debug(string msg) { if (Verbose) Write("DEBUG", msg); }

    public static string Format(DateTimeOffset at, string level, string msg) =>
        $"{at:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {msg.Replace('\n', ' ')}";

    private static void Write(string level, string msg)
    {
        var line = Format(DateTimeOffset.Now, level, msg);
        if (Sink is { } s) { s(line); return; }
        lock (Gate)
        {
            _file?.WriteLine(line);
            if (Stderr) Console.Error.WriteLine(line);
        }
    }
}
