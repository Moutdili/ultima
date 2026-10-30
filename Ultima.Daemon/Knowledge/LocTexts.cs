using System.Text.Json;
using Ultima.Knowledge.Text;

namespace Ultima.Daemon.Knowledge;

/// <summary>
/// Textes du jeu par clé de localisation (<c>table</c>, <c>key</c>) — ce que le miroir transmet pour les
/// événements (il ne rend aucun texte). Texte absent ⇒ <c>null</c>, jamais la clé brute.
/// </summary>
public interface ILocTexts
{
    string? Text(string? table, string? key);
}

/// <summary>Aucun texte disponible (pas de localisation extraite) : tout est <c>null</c>.</summary>
public sealed class NullLocTexts : ILocTexts
{
    public static NullLocTexts Instance { get; } = new();
    public string? Text(string? table, string? key) => null;
}

/// <summary>
/// Lit les tables de localisation extraites LOCALEMENT du jeu (<c>&lt;dir&gt;/fra/events.json</c>…, le dossier
/// <c>Source.LocalizationDir</c> de la base de l'Encyclopédiste, ou <c>ULTIMA_LOC_DIR</c>) et rend le texte
/// avec le moteur de rendu d'<c>Ultima.Knowledge</c> (BBCode retiré, variables inconnues = « ? », jamais
/// d'accolade). Français, repli anglais. Chargement paresseux, une table à la fois, au premier événement :
/// rien n'est lu au démarrage. Seules les tables utiles aux événements sont permises (pas de chemin arbitraire).
/// </summary>
public sealed class LocTexts : ILocTexts
{
    public static readonly IReadOnlySet<string> AllowedTables =
        new HashSet<string>(StringComparer.Ordinal) { "events", "ancients", "relics", "gameplay_ui" };

    private const long MaxTableBytes = 16 * 1024 * 1024;
    private readonly string _dir;
    private readonly string[] _langs;
    private readonly Dictionary<string, Dictionary<string, string>?> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), string?> _rendered = new();
    private readonly GameTextRenderer _renderer = new(new RenderOptions(UnknownValue: "?"));
    private readonly object _gate = new();

    public LocTexts(string dir, params string[] langs)
    {
        _dir = dir;
        _langs = langs.Length > 0 ? langs : new[] { "fra", "eng" };
    }

    public string Directory => _dir;

    /// <summary><c>ULTIMA_LOC_DIR</c>, sinon le dossier noté dans la base, s'il existe.</summary>
    public static ILocTexts FromEnvironment(string? kbLocalizationDir)
    {
        var env = Environment.GetEnvironmentVariable("ULTIMA_LOC_DIR");
        foreach (var d in new[] { env, kbLocalizationDir })
            if (!string.IsNullOrWhiteSpace(d) && System.IO.Directory.Exists(d)) return new LocTexts(d);
        return NullLocTexts.Instance;
    }

    public string? Text(string? table, string? key)
    {
        if (table is null || key is null || !AllowedTables.Contains(table)) return null;
        lock (_gate)
        {
            if (_rendered.TryGetValue((table, key), out var done)) return done;
            string? text = null;
            foreach (var lang in _langs)
            {
                if (Table(lang, table) is { } t && t.TryGetValue(key, out var tpl) && !string.IsNullOrWhiteSpace(tpl))
                {
                    try { text = _renderer.Render(tpl, new Dictionary<string, TextValue>(), lang).Text; }
                    catch (Exception e) { Diag.Debug($"texte {table}/{key} : {e.Message}"); text = null; }
                    if (string.IsNullOrWhiteSpace(text)) text = null;
                    break;
                }
            }
            if (_rendered.Count < 4096) _rendered[(table, key)] = text;
            return text;
        }
    }

    private Dictionary<string, string>? Table(string lang, string table)
    {
        var id = lang + "/" + table;
        if (_tables.TryGetValue(id, out var t)) return t;
        t = null;
        var path = Path.Combine(_dir, lang, table + ".json");
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length <= MaxTableBytes)
            {
                using var fs = fi.OpenRead();
                var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fs);
                t = raw?.Where(kv => kv.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(kv => kv.Key, kv => kv.Value.GetString()!, StringComparer.Ordinal);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Diag.Warn($"localisation illisible : {path} : {e.Message}");
        }
        _tables[id] = t;
        return t;
    }
}
