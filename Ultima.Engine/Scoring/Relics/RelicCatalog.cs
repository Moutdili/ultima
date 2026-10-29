using System.Text.Json;

namespace Ultima.Engine.Scoring.Relics;

/// <summary>Relique telle que le joueur la lit : id, nom FR, description FR, tags mécaniques.</summary>
public sealed record RelicEntry(string Id, string NameFr, string DescriptionFr, IReadOnlyList<string> Tags)
{
    public bool Has(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Charge data/catalog/relics.json (généré par tools/index_relics.py). Sert au scoring
/// contextuel : la valeur d'une relique de PV max dépend des PV du joueur, pas seulement de sa méta.
/// </summary>
public sealed class RelicCatalog
{
    private readonly Dictionary<string, RelicEntry> _byId = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byId.Count;

    private RelicCatalog() { }

    public static RelicCatalog Empty { get; } = new();

    public RelicEntry? Get(string? relicId)
    {
        if (string.IsNullOrWhiteSpace(relicId)) return null;
        var id = relicId.Trim().ToUpperInvariant();
        if (id.StartsWith("RELIC.")) id = id[7..];
        return _byId.TryGetValue(id, out var e) ? e : null;
    }

    public static RelicCatalog Load(string? catalogDir = null)
    {
        var dir = ScoringData.ResolveCatalogDir(catalogDir);
        var path = Path.Combine(dir, "relics.json");
        if (!File.Exists(path)) return Empty;

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var catalog = new RelicCatalog();

        if (!doc.RootElement.TryGetProperty("relics", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return catalog;

        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var id = el.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var tags = new List<string>();
            if (el.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
                foreach (var tag in t.EnumerateArray())
                    if (tag.ValueKind == JsonValueKind.String && tag.GetString() is { Length: > 0 } v)
                        tags.Add(v);

            catalog._byId[id!] = new RelicEntry(
                id!.ToUpperInvariant(),
                el.TryGetProperty("name_fr", out var n) ? n.GetString() ?? id : id,
                el.TryGetProperty("description_fr", out var d) ? d.GetString() ?? "" : "",
                tags);
        }

        return catalog;
    }
}
