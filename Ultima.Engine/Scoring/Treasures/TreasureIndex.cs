using System.Text.Json;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Relics;

namespace Ultima.Engine.Scoring.Treasures;

/// <summary>Pools réels du jeu (rareté déclarée par chaque relique dans son code).</summary>
public enum TreasurePool
{
    Common,
    Uncommon,
    Rare,
    Shop,
    Event,
    Ancient,
    Starter,
}

/// <summary>Une relique candidate, avec sa note pour le deck courant.</summary>
public sealed record TreasureCandidate(
    string RelicId,
    string Name,
    TreasurePool Pool,
    int Score,
    string Confidence,
    IReadOnlyList<string> Tags);

/// <summary>
/// Trésors potentiels : quelles reliques peuvent apparaître, par pool, et lesquelles sont les plus
/// utiles pour la run en cours (docs/48). La rareté vient du code du jeu, la note du scorer.
/// </summary>
public sealed class TreasureIndex
{
    public const string FileName = "relic_rarity.json";
    public const string Schema = "ultima.relic-rarity/1";

    /// <summary>Pools qu'un joueur peut réellement obtenir en cours de run.</summary>
    public static readonly IReadOnlyList<TreasurePool> Obtainable = new[]
    {
        TreasurePool.Common, TreasurePool.Uncommon, TreasurePool.Rare,
        TreasurePool.Shop, TreasurePool.Event, TreasurePool.Ancient,
    };

    private readonly Dictionary<TreasurePool, List<string>> _pools;
    private readonly Dictionary<string, (TreasurePool Pool, int Line)> _byRelic;
    private readonly Scorer _scorer;
    private readonly RelicCatalog _catalog;

    private TreasureIndex(
        Dictionary<TreasurePool, List<string>> pools,
        Dictionary<string, (TreasurePool, int)> byRelic,
        bool loaded,
        string? problem,
        Scorer scorer,
        RelicCatalog catalog)
    {
        _pools = pools;
        _byRelic = byRelic;
        IsLoaded = loaded;
        Problem = problem;
        _scorer = scorer;
        _catalog = catalog;
    }

    public bool IsLoaded { get; }
    public string? Problem { get; }
    public int RelicCount => _byRelic.Count;

    public static TreasureIndex Empty { get; } = new(
        new Dictionary<TreasurePool, List<string>>(),
        new Dictionary<string, (TreasurePool, int)>(StringComparer.OrdinalIgnoreCase),
        false, "aucun index chargé", new Scorer(), RelicCatalog.Load());

    public static TreasureIndex Load(string? catalogDir = null, Scorer? scorer = null)
    {
        string dir;
        try
        {
            dir = ScoringData.ResolveCatalogDir(catalogDir);
        }
        catch (IOException)
        {
            return Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return Empty;
        }

        var path = Path.Combine(dir, FileName);
        if (!File.Exists(path))
            return new TreasureIndex(new Dictionary<TreasurePool, List<string>>(),
                new Dictionary<string, (TreasurePool, int)>(StringComparer.OrdinalIgnoreCase),
                false, $"fichier absent : {path}", scorer ?? new Scorer(), RelicCatalog.Load(dir));

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (!doc.RootElement.TryGetProperty("relics", out var map) || map.ValueKind != JsonValueKind.Object)
                return Empty;

            var pools = new Dictionary<TreasurePool, List<string>>();
            var byRelic = new Dictionary<string, (TreasurePool, int)>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in map.EnumerateObject())
            {
                if (element.Value.ValueKind != JsonValueKind.String) continue;
                var rarity = element.Value.GetString();
                if (string.IsNullOrWhiteSpace(rarity)) continue;
                if (!Enum.TryParse<TreasurePool>(rarity, ignoreCase: true, out var pool)) continue;

                if (!pools.TryGetValue(pool, out var list))
                    pools[pool] = list = new List<string>();
                list.Add(element.Name);
                byRelic[element.Name] = (pool, 0);
            }

            return new TreasureIndex(pools, byRelic, true, null, scorer ?? new Scorer(),
                RelicCatalog.Load(dir));
        }
        catch (JsonException ex)
        {
            return new TreasureIndex(new Dictionary<TreasurePool, List<string>>(),
                new Dictionary<string, (TreasurePool, int)>(StringComparer.OrdinalIgnoreCase),
                false, $"JSON invalide : {ex.Message}", scorer ?? new Scorer(), RelicCatalog.Load(dir));
        }
        catch (IOException ex)
        {
            return new TreasureIndex(new Dictionary<TreasurePool, List<string>>(),
                new Dictionary<string, (TreasurePool, int)>(StringComparer.OrdinalIgnoreCase),
                false, $"lecture impossible : {ex.Message}", scorer ?? new Scorer(), RelicCatalog.Load(dir));
        }
    }

    public static TreasureIndex Default { get; } = Load();

    /// <summary>Reliques d'un pool, triées par identifiant (ordre stable, pas de hasard).</summary>
    public IReadOnlyList<string> Ids(TreasurePool pool) =>
        _pools.TryGetValue(pool, out var list)
            ? list.OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

    /// <summary>Pool d'une relique ; accepte <c>RELIC.X</c> comme <c>X</c>.</summary>
    public TreasurePool? PoolOf(string relicId) =>
        _byRelic.TryGetValue(Bare(relicId), out var entry) ? entry.Pool : null;

    private static string Bare(string id)
    {
        var text = id.Trim();
        return text.Contains('.') ? text[(text.LastIndexOf('.') + 1)..] : text;
    }

    /// <summary>
    /// Les reliques les plus utiles d'un pool pour le deck courant. Aucune relique non classée
    /// n'entre ici : seules les raretés déclarées par le jeu.
    /// </summary>
    public IReadOnlyList<TreasureCandidate> Rank(ScoringContext ctx, TreasurePool pool, int limit = 5)
    {
        if (limit <= 0) return Array.Empty<TreasureCandidate>();

        var candidates = new List<TreasureCandidate>();
        foreach (var id in Ids(pool))
        {
            var score = _scorer.ScoreRelic(id, ctx);
            var entry = _catalog.Get(id);
            candidates.Add(new TreasureCandidate(
                id,
                entry?.NameFr ?? id,
                pool,
                score.Total,
                score.Confidence,
                entry?.Tags ?? Array.Empty<string>()));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.RelicId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    /// <summary>Meilleures reliques toutes provenances confondues (hors set de départ).</summary>
    public IReadOnlyList<TreasureCandidate> RankAll(ScoringContext ctx, int limit = 10)
    {
        if (limit <= 0) return Array.Empty<TreasureCandidate>();

        var all = new List<TreasureCandidate>();
        foreach (var pool in Obtainable)
        {
            foreach (var id in Ids(pool))
            {
                var score = _scorer.ScoreRelic(id, ctx);
                var entry = _catalog.Get(id);
                all.Add(new TreasureCandidate(id, entry?.NameFr ?? id, pool,
                    score.Total, score.Confidence, entry?.Tags ?? Array.Empty<string>()));
            }
        }

        return all
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.RelicId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
