using System.Text.Json;
using System.Text.Json.Nodes;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Settings;

/// <summary>Réglages lus dans <c>settings.json</c> : contrat (<see cref="OverlaySettings"/>) + liste des plugins.</summary>
public sealed record SettingsSnapshot(OverlaySettings Settings, IReadOnlyList<string> PluginsEnabled);

/// <summary>
/// Réglages de l'overlay : <c>~/.config/ultima/settings.json</c> (écrit par le compagnon et le TUI, LU par le
/// daemon — il ne le réécrit JAMAIS). Format : la sérialisation camelCase d'<see cref="OverlaySettings"/>
/// (<c>{"modules":[{"id","enabled","order","screens"}],"hud":{…},"pluginsEnabled":[…]}</c>, v1.3/v1.4).
/// Absent → <see cref="OverlaySettings.Default"/>. Illisible → dernier réglage valide conservé.
/// <c>enabled</c> ABSENT vaut <c>true</c> (v1.4) ; <c>screens</c> est validé contre <see cref="ScreenIds.All"/> ;
/// les bornes du HUD sont RAMENÉES à la borne (<see cref="HudSettings.HeightMin"/> …) et non remplacées par le défaut.
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
    public static OverlaySettings? TryLoad(string path, out string? error) =>
        TryLoadSnapshot(path, out error)?.Settings;

    /// <summary>Lit le fichier, avec la liste <c>pluginsEnabled</c> (hors contrat : réglage du daemon).</summary>
    public static SettingsSnapshot? TryLoadSnapshot(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path)) return new SettingsSnapshot(OverlaySettings.Default, Array.Empty<string>());
        string text;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            text = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"settings.json illisible : {e.Message}";
            return null;
        }
        try
        {
            if (JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) is not JsonObject root)
            {
                error = "settings.json : objet attendu";
                return null;
            }
            if (root["modules"] is not JsonArray modules)
            {
                error = "settings.json sans « modules »";
                return null;
            }
            var list = new List<ModuleSetting>(modules.Count);
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is not JsonObject m) continue;
                var id = Str(m, "id");
                if (id is null) continue;
                // v1.4 : « enabled » absent ⇒ true (un module cité est activé sauf mention contraire)
                bool enabled = m["enabled"] is JsonValue ev && ev.TryGetValue<bool>(out var b) ? b : true;
                int order = m["order"] is JsonValue ov && ov.TryGetValue<int>(out var o) ? o : i;
                list.Add(new ModuleSetting(id, enabled, order) { Screens = Screens(m) });
            }
            return new SettingsSnapshot(
                new OverlaySettings(Normalize(list)) { Hud = NormalizeHud(ParseHud(root["hud"])) },
                PluginsEnabled(root["pluginsEnabled"]));
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or NotSupportedException)
        {
            error = $"settings.json illisible : {e.Message}";
            return null;
        }
    }

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static HudSettings? ParseHud(JsonNode? node)
    {
        if (node is null) return null;
        try { return node.Deserialize<HudSettings>(StateJson.Options); }
        catch (JsonException) { return null; }
    }

    /// <summary>v1.4 : écrans d'un bloc (ScreenIds.All, doublons retirés, valeurs inconnues ignorées).</summary>
    private static IReadOnlyList<string>? Screens(JsonObject m)
    {
        if (m["screens"] is not JsonArray arr) return null;
        var list = new List<string>(arr.Count);
        foreach (var s in arr)
        {
            if (s is not JsonValue v || !v.TryGetValue<string>(out var name)) continue;
            if (!ScreenIds.All.Contains(name) || list.Contains(name)) continue;
            list.Add(name);
        }
        return list.Count == 0 ? null : list;
    }

    /// <summary><c>pluginsEnabled</c> : ids de plugins (hors contrat ; le daemon seul les lit).</summary>
    private static IReadOnlyList<string> PluginsEnabled(JsonNode? node)
    {
        if (node is not JsonArray arr) return Array.Empty<string>();
        var list = new List<string>(arr.Count);
        foreach (var s in arr)
        {
            if (s is not JsonValue v || !v.TryGetValue<string>(out var id)) continue;
            if (string.IsNullOrWhiteSpace(id) || list.Contains(id)) continue;
            list.Add(id);
        }
        return list;
    }

    public static OverlaySettings Normalize(OverlaySettings s) =>
        new(Normalize(s.Modules)) { Hud = NormalizeHud(s.Hud) };

    private static IReadOnlyList<ModuleSetting> Normalize(IReadOnlyList<ModuleSetting> modules)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = modules
            .Where(m => m?.Id is not null && KnownModules.Contains(m.Id) && seen.Add(m.Id))
            .OrderBy(m => m.Order)
            .ToList();
        foreach (var d in OverlaySettings.Default.Modules)
            if (seen.Add(d.Id)) list.Add(d with { Order = int.MaxValue });
        return list.Select((m, i) => m with { Order = i }).ToArray();
    }

    /// <summary>
    /// v1.3/v1.4 : réglages du HUD, validés champ par champ. Une valeur HORS BORNES est ramenée à la borne
    /// (hauteur 0,30–0,85 ; opacité 0,35–1) ; une valeur non finie ou d'un type inconnu reprend le défaut de
    /// SON champ ; les autres champs sont conservés. « hud » absent ou null → <see cref="HudSettings.Default"/>.
    /// </summary>
    public static HudSettings NormalizeHud(HudSettings? h)
    {
        var d = HudSettings.Default;
        if (h is null) return d;
        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        return new HudSettings(
            h.Quality is { } q && HudSettings.Qualities.Contains(q.ToLowerInvariant()) ? q.ToLowerInvariant() : d.Quality,
            Finite(h.HeightFraction) ? Math.Clamp(h.HeightFraction, HudSettings.HeightMin, HudSettings.HeightMax) : d.HeightFraction,
            Finite(h.RestOpacity) ? Math.Clamp(h.RestOpacity, HudSettings.OpacityMin, HudSettings.OpacityMax) : d.RestOpacity,
            h.Side?.ToLowerInvariant() is "right" or "left" ? h.Side.ToLowerInvariant() : d.Side,
            h.DeckScoreInCombat,
            h.GradesOnly);
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
