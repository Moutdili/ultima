using System.Text.Json;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Settings;

/// <summary>
/// Réglages de l'overlay : <c>~/.config/ultima/settings.json</c> (écrit par le compagnon, LU par le daemon).
/// Format : la sérialisation camelCase d'<see cref="OverlaySettings"/> (<c>{"modules":[{"id","enabled","order"}]}</c>).
/// Absent → <see cref="OverlaySettings.Default"/>. Illisible → dernier réglage valide conservé.
/// Normalisation : ids inconnus ignorés, doublons ignorés, modules manquants ajoutés (activés) après les autres,
/// ordre renuméroté 0..n-1.
/// </summary>
public static class SettingsStore
{
    public static string DefaultPath()
    {
        var cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(cfg))
            cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(cfg, "ultima", "settings.json");
    }

    public static readonly IReadOnlyList<string> KnownModules = OverlaySettings.Default.Modules.Select(m => m.Id).ToArray();

    /// <summary>Lit le fichier. <paramref name="error"/> non nul : fichier présent mais invalide.</summary>
    public static OverlaySettings? TryLoad(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path)) return OverlaySettings.Default;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var raw = JsonSerializer.Deserialize<OverlaySettings>(fs, StateJson.Options);
            if (raw?.Modules is null) { error = "settings.json sans « modules »"; return null; }
            return Normalize(raw);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"settings.json illisible : {e.Message}";
            return null;
        }
    }

    public static OverlaySettings Normalize(OverlaySettings s)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = s.Modules
            .Where(m => m?.Id is not null && KnownModules.Contains(m.Id) && seen.Add(m.Id))
            .OrderBy(m => m.Order)
            .ToList();
        foreach (var d in OverlaySettings.Default.Modules)
            if (seen.Add(d.Id)) list.Add(d with { Order = int.MaxValue });
        return new OverlaySettings(list.Select((m, i) => m with { Order = i }).ToArray());
    }
}

/// <summary>
/// Surveille le fichier de réglages par inotify (FileSystemWatcher), sans sondage. Si
/// <c>~/.config/ultima</c> n'existe pas encore, surveille son parent jusqu'à sa création.
/// Le daemon ne crée ni n'écrit jamais ce fichier.
/// </summary>
public sealed class SettingsWatcher : IDisposable
{
    private readonly string _path;
    private readonly Action _changed;
    private FileSystemWatcher? _dirWatcher;
    private FileSystemWatcher? _parentWatcher;
    private Timer? _coalesce;
    private bool _disposed;

    public SettingsWatcher(string path, Action changed)
    {
        _path = path;
        _changed = changed;
    }

    public void Start()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) WatchDir(dir);
        else
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || !Directory.Exists(parent)) return;
            _parentWatcher = new FileSystemWatcher(parent) { NotifyFilter = NotifyFilters.DirectoryName, IncludeSubdirectories = false };
            void OnParent(string? name)
            {
                if (name != Path.GetFileName(dir) || !Directory.Exists(dir) || _dirWatcher is not null) return;
                WatchDir(dir);
                Kick();
            }
            _parentWatcher.Created += (_, e) => OnParent(e.Name);
            _parentWatcher.Renamed += (_, e) => OnParent(e.Name);
            _parentWatcher.EnableRaisingEvents = true;
        }
    }

    private void WatchDir(string dir)
    {
        var file = Path.GetFileName(_path);
        _dirWatcher = new FileSystemWatcher(dir)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        void On(string? name, string? old = null) { if (name == file || old == file) Kick(); }
        _dirWatcher.Created += (_, e) => On(e.Name);
        _dirWatcher.Changed += (_, e) => On(e.Name);
        _dirWatcher.Deleted += (_, e) => On(e.Name);
        _dirWatcher.Renamed += (_, e) => On(e.Name, e.OldName);
        _dirWatcher.EnableRaisingEvents = true;
    }

    /// <summary>Regroupe une rafale d'écriture (tmp + rename) en un seul rechargement (minuteur ponctuel).</summary>
    private void Kick()
    {
        if (_disposed) return;
        _coalesce ??= new Timer(_ => _changed(), null, Timeout.Infinite, Timeout.Infinite);
        _coalesce.Change(150, Timeout.Infinite);
    }

    public void Dispose()
    {
        _disposed = true;
        _dirWatcher?.Dispose();
        _parentWatcher?.Dispose();
        _coalesce?.Dispose();
    }
}
