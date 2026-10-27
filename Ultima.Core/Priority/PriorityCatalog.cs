using System.Text.Json;

namespace Ultima.Core.Priority;

/// <summary>Une carte à payoff conditionnel (entrée de data/catalog/priority_cards.json).</summary>
/// <param name="Class">Nom de classe C# du modèle (= <c>CardSnapshot.ClassName</c>).</param>
/// <param name="GlowGold">La carte override <c>ShouldGlowGoldInternal</c> : l'oracle du jeu est disponible.</param>
/// <param name="TurnConditional">Le payoff dépend d'une condition de tour (souvent non observable sans glow).</param>
public sealed record PriorityEntry(string Class, bool GlowGold, bool TurnConditional);

/// <summary>
/// Catalogue des cartes prioritaires. Pur (aucune dépendance au jeu) : parse le JSON généré,
/// tolère tout champ supplémentaire et ne garde que <c>priority_candidate == true</c>.
/// </summary>
public sealed class PriorityCatalog
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<string, PriorityEntry> _byClass;

    private PriorityCatalog(Dictionary<string, PriorityEntry> byClass) => _byClass = byClass;

    public static PriorityCatalog Empty { get; } = new(new Dictionary<string, PriorityEntry>(StringComparer.Ordinal));

    public int Count => _byClass.Count;

    public IReadOnlyCollection<PriorityEntry> Entries => _byClass.Values;

    public bool Contains(string? className) => !string.IsNullOrEmpty(className) && _byClass.ContainsKey(className);

    public bool TryGet(string? className, out PriorityEntry entry)
    {
        if (!string.IsNullOrEmpty(className) && _byClass.TryGetValue(className, out var found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    /// <summary>
    /// Parse le catalogue. Racine = tableau d'objets ; clés lues : <c>class</c>, <c>priority_candidate</c>,
    /// <c>glow_gold</c>, <c>turn_conditional</c> (booléens absents = false). Les autres clés sont ignorées,
    /// les éléments non-objets ou sans <c>class</c> sont sautés, un doublon garde la première entrée.
    /// </summary>
    /// <exception cref="JsonException">JSON invalide ou racine qui n'est pas un tableau.</exception>
    public static PriorityCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json ?? "", Options);
        return FromRoot(doc.RootElement);
    }

    /// <inheritdoc cref="Parse(string)"/>
    public static PriorityCatalog Load(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream, Options);
        return FromRoot(doc.RootElement);
    }

    private static PriorityCatalog FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new JsonException($"priority catalog: root must be an array, got {root.ValueKind}");

        var byClass = new Dictionary<string, PriorityEntry>(StringComparer.Ordinal);
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!IsTrue(item, "priority_candidate")) continue;
            if (!item.TryGetProperty("class", out var cls) || cls.ValueKind != JsonValueKind.String) continue;

            var name = cls.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            byClass.TryAdd(name, new PriorityEntry(name, IsTrue(item, "glow_gold"), IsTrue(item, "turn_conditional")));
        }
        return new PriorityCatalog(byClass);
    }

    private static bool IsTrue(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
}
