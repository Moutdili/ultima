using System.Text.Json;
using Ultima.Engine.Scoring;

namespace Ultima.Engine.Scoring.Keywords;

/// <summary>Sémantique d'un mot-clé de carte, telle que le jeu la définit.</summary>
public sealed record KeywordSemantics(
    string Id,
    string Fr,
    string Semantics,
    bool Transient,
    bool Unplayable,
    bool Retained,
    bool OpeningHand,
    bool ReturnsOnDiscard,
    bool Unremovable);

/// <summary>Drapeaux agrégés d'un ensemble de mots-clés (une carte peut en porter plusieurs).</summary>
public readonly record struct KeywordFlags(
    bool Transient,
    bool Unplayable,
    bool Retained,
    bool OpeningHand,
    bool ReturnsOnDiscard,
    bool Unremovable)
{
    public static KeywordFlags None => default;
    public bool Any => Transient || Unplayable || Retained || OpeningHand || ReturnsOnDiscard || Unremovable;
}

/// <summary>
/// Catalogue des 7 mots-clés du jeu (<c>Entities/Cards/CardKeyword.cs</c> : Exhaust, Ethereal,
/// Innate, Unplayable, Retain, Sly, Eternal), chargé depuis
/// <c>data/catalog/keyword_semantics.json</c>. Un mot-clé inconnu n'est jamais deviné : il est
/// ignoré et signalé par <see cref="Unknown"/>.
/// </summary>
public sealed class KeywordCatalog
{
    public const string FileName = "keyword_semantics.json";

    private readonly Dictionary<string, KeywordSemantics> _byId;
    private readonly HashSet<string> _unknown = new(StringComparer.OrdinalIgnoreCase);

    private KeywordCatalog(Dictionary<string, KeywordSemantics> byId, string? problem)
    {
        _byId = byId;
        Problem = problem;
    }

    public static KeywordCatalog Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase), "catalogue vide");

    public static KeywordCatalog Default => LazyDefault.Value;

    private static readonly Lazy<KeywordCatalog> LazyDefault =
        new(() => Load(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Raison du chargement vide, null si le fichier a été lu.</summary>
    public string? Problem { get; }

    public int Count => _byId.Count;

    public IReadOnlyCollection<KeywordSemantics> All => _byId.Values;

    /// <summary>Mots-clés rencontrés mais absents du catalogue (jamais silencieux).</summary>
    public IReadOnlyCollection<string> Unknown => _unknown;

    public KeywordSemantics? Get(string? id) =>
        id is not null && _byId.TryGetValue(Normalize(id), out var value) ? value : null;

    /// <summary>Drapeaux agrégés ; les mots-clés inconnus sont mémorisés pour audit.</summary>
    public KeywordFlags Flags(IEnumerable<string>? keywords)
    {
        if (keywords is null) return KeywordFlags.None;

        var transient = false;
        var unplayable = false;
        var retained = false;
        var opening = false;
        var returns = false;
        var unremovable = false;
        var any = false;

        foreach (var raw in keywords)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var semantics = Get(raw);
            if (semantics is null)
            {
                lock (_unknown) _unknown.Add(raw.Trim());
                continue;
            }

            any = true;
            transient |= semantics.Transient;
            unplayable |= semantics.Unplayable;
            retained |= semantics.Retained;
            opening |= semantics.OpeningHand;
            returns |= semantics.ReturnsOnDiscard;
            unremovable |= semantics.Unremovable;
        }

        return any
            ? new KeywordFlags(transient, unplayable, retained, opening, returns, unremovable)
            : KeywordFlags.None;
    }

    public static KeywordCatalog Load(string? explicitDir = null)
    {
        try
        {
            var path = Path.Combine(explicitDir ?? ScoringData.ResolveCatalogDir(), FileName);
            if (!File.Exists(path)) return new KeywordCatalog(new(StringComparer.OrdinalIgnoreCase), $"{FileName} absent");

            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var byId = new Dictionary<string, KeywordSemantics>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("keywords", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    byId[Normalize(id!)] = new KeywordSemantics(
                        id!,
                        Str(item, "fr"),
                        Str(item, "semantics"),
                        Bool(item, "transient"),
                        Bool(item, "unplayable"),
                        Bool(item, "retained"),
                        Bool(item, "opening_hand"),
                        Bool(item, "returns_on_discard"),
                        Bool(item, "unremovable"));
                }
            }

            return new KeywordCatalog(byId, byId.Count == 0 ? "aucun mot-clé lu" : null);
        }
        catch (Exception ex)
        {
            return new KeywordCatalog(new(StringComparer.OrdinalIgnoreCase), ex.Message);
        }
    }

    private static string Normalize(string id) => id.Trim();

    private static string Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    private static bool Bool(JsonElement item, string name) =>
        item.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True;
}
