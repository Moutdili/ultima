using System.Text.Json;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Deck;

/// <summary>Un bucket de référence : les percentiles de G des decks finaux à un acte donné.</summary>
public sealed record DeckPercentileBucket(
    string Character, int Act, int N, int P10, int P25, int P50, int P75, int P90, int MedianDeck)
{
    public string Key => $"{Character.ToUpperInvariant()}|{Act}";
}

/// <summary>
/// Référence de comparaison pour la note du deck : « ce deck est au 63e percentile des decks
/// arrêtés à l'acte 2 » au lieu d'une moyenne de notes de cartes qui ne dit rien.
/// </summary>
/// <remarks>
/// Source : <c>data/calibration/deck_percentiles.json</c>, produite par
/// <c>tools/deck_percentiles/</c> sur les decks finaux du corpus (runs solo).
/// Réserve documentée dans le fichier : un deck en cours est comparé à des decks finaux, donc la
/// référence est légèrement haute (les cartes retirées ne sont pas reconstituables).
/// </remarks>
public sealed class DeckPercentiles
{
    public const string FileName = "deck_percentiles.json";
    public const string Schema = "ultima.deck-percentiles/1";

    private readonly Dictionary<string, DeckPercentileBucket> _buckets;

    private DeckPercentiles(Dictionary<string, DeckPercentileBucket> buckets, string? problem, string? path)
    {
        _buckets = buckets;
        Problem = problem;
        Path = path;
    }

    public static DeckPercentiles Empty { get; } =
        new(new(StringComparer.OrdinalIgnoreCase), "aucune référence de deck chargée", null);

    private static readonly Lazy<DeckPercentiles> LazyDefault = new(() => Load());

    public static DeckPercentiles Default => LazyDefault.Value;

    /// <summary>Vrai si au moins un bucket est disponible.</summary>
    public bool HasData => _buckets.Count > 0;

    public int Count => _buckets.Count;

    /// <summary>Chemin lu (null si vide).</summary>
    public string? Path { get; }

    /// <summary>Raison explicite quand aucune référence n'est disponible.</summary>
    public string? Problem { get; }

    public static DeckPercentiles Load(string? calibrationDir = null)
    {
        var dir = ScoringData.ResolveCalibrationDir(calibrationDir);
        var file = System.IO.Path.Combine(dir, FileName);
        if (!System.IO.File.Exists(file))
            return new DeckPercentiles(new(StringComparer.OrdinalIgnoreCase), $"{file} introuvable", file);

        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(file));
            var root = doc.RootElement;
            var schema = root.TryGetProperty("schema", out var s) ? s.GetString() : null;
            if (!string.Equals(schema, Schema, StringComparison.Ordinal))
                return new DeckPercentiles(new(StringComparer.OrdinalIgnoreCase),
                    $"schéma inattendu « {schema} » (attendu {Schema})", file);

            var buckets = new Dictionary<string, DeckPercentileBucket>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("buckets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var bucket = ReadBucket(item);
                    if (bucket is null) continue;
                    buckets[bucket.Key] = bucket;
                }
            }

            return buckets.Count == 0
                ? new DeckPercentiles(buckets, "fichier présent mais aucun bucket exploitable", file)
                : new DeckPercentiles(buckets, null, file);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new DeckPercentiles(new(StringComparer.OrdinalIgnoreCase), $"lecture impossible : {ex.Message}", file);
        }
    }

    /// <summary>Bucket d'un personnage à un acte (repli sur la référence tous personnages).</summary>
    public DeckPercentileBucket? Find(string? characterId, int act)
    {
        var key = $"{Ids.Bare(characterId).ToUpperInvariant()}|{act}";
        if (_buckets.TryGetValue(key, out var exact)) return exact;
        return _buckets.TryGetValue($"*|{act}", out var any) ? any : null;
    }

    /// <summary>
    /// Percentile (0..100) d'un score G dans la référence, par interpolation linéaire entre les
    /// points publiés. Null si aucune référence pour cet acte — jamais une valeur inventée.
    /// </summary>
    public int? Percentile(string? characterId, int act, int g)
    {
        var bucket = Find(characterId, act);
        if (bucket is null) return null;

        var points = new (int G, int Pct)[]
        {
            (bucket.P10, 10), (bucket.P25, 25), (bucket.P50, 50), (bucket.P75, 75), (bucket.P90, 90),
        };

        if (g <= points[0].G)
            return points[0].G <= 0 ? 0 : ScoreMath.Clamp(ScoreMath.Round(10.0 * g / points[0].G), 0, 10);

        for (var i = 1; i < points.Length; i++)
        {
            if (g > points[i].G) continue;
            var (g0, p0) = points[i - 1];
            var (g1, p1) = points[i];
            if (g1 == g0) return p1;
            return ScoreMath.Clamp(p0 + ScoreMath.Round((double)(g - g0) / (g1 - g0) * (p1 - p0)), p0, p1);
        }

        var last = points[^1];
        var span = Math.Max(1, last.G - points[^2].G);
        return ScoreMath.Clamp(90 + ScoreMath.Round(10.0 * (g - last.G) / span), 90, 100);
    }

    private static DeckPercentileBucket? ReadBucket(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;

        var character = Str(item, "character") ?? "";
        if (character.Length == 0) return null;
        var act = Num(item, "act");
        var n = Num(item, "n");
        if (act is null or <= 0 || n is null or <= 0) return null;

        return new DeckPercentileBucket(
            character.ToUpperInvariant(), act.Value, n.Value,
            Num(item, "p10") ?? 0, Num(item, "p25") ?? 0, Num(item, "p50") ?? 0,
            Num(item, "p75") ?? 0, Num(item, "p90") ?? 0, Num(item, "median_deck") ?? 0);
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Num(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : null;
}
