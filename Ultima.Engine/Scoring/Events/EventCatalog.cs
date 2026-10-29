using System.Text.Json;
using Ultima.Engine.Scoring;

namespace Ultima.Engine.Scoring.Events;

/// <summary>Profil mesuré d'une option d'événement (salle « ? »).</summary>
public sealed record EventProfile(
    string Event,
    string Option,
    int N,
    double WinRate,
    double HpDelta,
    double GoldDelta,
    double RelicRate,
    double CardGainRate,
    double CardRemoveRate,
    double CurseRate);

/// <summary>
/// Charge <c>data/calibration/events.json</c> (profils mesurés sur 80 000 runs solo) et
/// <c>data/catalog/event_options.json</c> (libellés FR). Une option absente n'est jamais devinée :
/// elle est signalée « aucune mesure ».
/// </summary>
public sealed class EventCatalog
{
    public const string ProfilesFile = "events.json";
    public const string TitlesFile = "event_options.json";

    private readonly Dictionary<string, EventProfile> _profiles;
    private readonly Dictionary<string, string> _titles;
    private readonly HashSet<string> _unknown = new(StringComparer.OrdinalIgnoreCase);

    private EventCatalog(Dictionary<string, EventProfile> profiles, Dictionary<string, string> titles, string? problem)
    {
        _profiles = profiles;
        _titles = titles;
        Problem = problem;
    }

    public static EventCatalog Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), "catalogue vide");

    public static EventCatalog Default => LazyDefault.Value;

    private static readonly Lazy<EventCatalog> LazyDefault =
        new(() => Load(), LazyThreadSafetyMode.ExecutionAndPublication);

    public string? Problem { get; }
    public int Count => _profiles.Count;
    public bool HasData => Count > 0;

    /// <summary>Options rencontrées sans profil (jamais silencieux).</summary>
    public IReadOnlyCollection<string> Unknown => _unknown;

    public EventProfile? Get(string? eventId, string? option)
    {
        var key = Key(eventId, option);
        if (key.Length == 0) return null;
        if (_profiles.TryGetValue(key, out var profile)) return profile;
        lock (_unknown) _unknown.Add(key);
        return null;
    }

    public string? Title(string? eventId, string? option)
    {
        var key = Key(eventId, option);
        return _titles.TryGetValue(key, out var title) ? title : null;
    }

    public static string Key(string? eventId, string? option)
    {
        var e = (eventId ?? "").Trim().ToUpperInvariant();
        var o = (option ?? "").Trim().ToUpperInvariant();
        return e.Length == 0 || o.Length == 0 ? string.Empty : $"{e}|{o}";
    }

    public static EventCatalog Load(string? explicitDir = null)
    {
        try
        {
            var dir = explicitDir ?? ScoringData.ResolveCatalogDir();
            var profiles = new Dictionary<string, EventProfile>(StringComparer.OrdinalIgnoreCase);
            var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var calibration = Path.Combine(ScoringData.ResolveCalibrationDir(), ProfilesFile);
            if (File.Exists(calibration))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(calibration));
                if (doc.RootElement.TryGetProperty("options", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        var ev = Str(item, "event");
                        var opt = Str(item, "option");
                        var key = Key(ev, opt);
                        if (key.Length == 0) continue;
                        profiles[key] = new EventProfile(
                            ev, opt,
                            Int(item, "n"),
                            Num(item, "win_rate"),
                            Num(item, "avg_hp_delta"),
                            Num(item, "avg_gold_delta"),
                            Num(item, "relic_rate"),
                            Num(item, "card_gain_rate"),
                            Num(item, "card_remove_rate"),
                            Num(item, "curse_rate"));
                    }
                }
            }

            var titlesPath = Path.Combine(dir, TitlesFile);
            if (File.Exists(titlesPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(titlesPath));
                if (doc.RootElement.TryGetProperty("options", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        var key = Key(Str(item, "event"), Str(item, "option"));
                        var title = Str(item, "title");
                        if (key.Length > 0 && title.Length > 0) titles[key] = title;
                    }
                }
            }

            var problem = profiles.Count == 0 ? $"{ProfilesFile} vide ou absent" : null;
            return new EventCatalog(profiles, titles, problem);
        }
        catch (Exception ex)
        {
            return new EventCatalog(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), ex.Message);
        }
    }

    private static string Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    private static int Int(JsonElement item, string name) =>
        item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? (int)el.GetDouble() : 0;

    private static double Num(JsonElement item, string name) =>
        item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDouble() : 0;
}
