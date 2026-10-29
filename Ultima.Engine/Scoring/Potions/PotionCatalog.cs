using System.Text.Json;

namespace Ultima.Engine.Scoring.Potions;

/// <summary>Une potion telle que le joueur la lit : id, nom FR, description FR, tags mécaniques.</summary>
public sealed record PotionEntry(string Id, string NameFr, string DescriptionFr, IReadOnlyList<string> Tags)
{
    public bool Has(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Charge data/catalog/potions.json (généré par tools/index_potions.py depuis la localisation FR).
/// Aucune méta communautaire n'existe pour les potions : le score vient de l'effet décrit.
/// </summary>
public sealed class PotionCatalog
{
    private readonly Dictionary<string, PotionEntry> _byId = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byId.Count;
    public string? Source { get; }
    public string? GeneratedBy { get; }

    private PotionCatalog(string? source, string? generatedBy) 
    {
        Source = source;
        GeneratedBy = generatedBy;
    }

    public static PotionCatalog Empty { get; } = new(null, null);

    public PotionEntry? Get(string? potionId)
    {
        if (string.IsNullOrWhiteSpace(potionId)) return null;
        var id = potionId.Trim().ToUpperInvariant();
        if (id.StartsWith("POTION.")) id = id[7..];
        return _byId.TryGetValue(id, out var e) ? e : null;
    }

    public static PotionCatalog Load(string? catalogDir = null)
    {
        var dir = ScoringData.ResolveCatalogDir(catalogDir);
        var path = Path.Combine(dir, "potions.json");
        if (!File.Exists(path)) return Empty;

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var catalog = new PotionCatalog(
            root.TryGetProperty("source", out var s) ? s.GetString() : null,
            root.TryGetProperty("generated_by", out var g) ? g.GetString() : null);

        if (!root.TryGetProperty("potions", out var arr) || arr.ValueKind != JsonValueKind.Array)
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

            catalog._byId[id!] = new PotionEntry(
                id!.ToUpperInvariant(),
                el.TryGetProperty("name_fr", out var n) ? n.GetString() ?? id : id,
                el.TryGetProperty("description_fr", out var d) ? d.GetString() ?? "" : "",
                tags);
        }

        return catalog;
    }
}
