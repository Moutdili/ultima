using System.Text;
using System.Text.Json;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Deck;

/// <summary>
/// Une carte telle que le catalogue du jeu la décrit (<c>data/catalog/cards.json</c>).
/// <see cref="Id"/> est l'id du jeu sans préfixe : classe C# « AscendersBane » → « ASCENDERS_BANE »,
/// même normalisation que <see cref="Ids.Bare(string)"/> (vérifiée sur les 474 cartes de la méta).
/// </summary>
public sealed record CatalogCard(string Id, string ClassName, string? Type, string? Rarity, int? Cost)
{
    public string DisplayName => NameFormat.FromClassName(ClassName);

    public bool IsCurse =>
        string.Equals(Type, "Curse", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Rarity, "Curse", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Mise en forme des noms de classes C# quand la méta n'a pas de nom traduit.</summary>
public static class NameFormat
{
    /// <summary>« AscendersBane » → « Ascenders Bane ». Aucune invention : le nom du jeu.</summary>
    public static string FromClassName(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return string.Empty;
        var sb = new StringBuilder(className.Length + 4);
        for (int i = 0; i < className.Length; i++)
        {
            char c = className[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(className[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>« AscendersBane » → « ASCENDERS_BANE » (id du jeu).</summary>
    public static string ToCardId(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return string.Empty;
        var sb = new StringBuilder(className.Length + 4);
        for (int i = 0; i < className.Length; i++)
        {
            char c = className[i];
            bool boundary = i > 0 && char.IsUpper(c) &&
                            (char.IsLower(className[i - 1]) ||
                             (i + 1 < className.Length && char.IsLower(className[i + 1]) && char.IsUpper(className[i - 1])));
            if (boundary) sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}

/// <summary>
/// Catalogue des cartes du jeu (types, raretés, coûts, malédictions). Chargé une fois, en lecture seule.
/// Sert ce que la méta communautaire ne contient pas : les malédictions et les cartes de base
/// (Frappe/Défense) n'ont aucune note mais ont un type.
/// </summary>
public sealed class CardCatalog
{
    private readonly Dictionary<string, CatalogCard> _cards;

    private CardCatalog(Dictionary<string, CatalogCard> cards) => _cards = cards;

    public int Count => _cards.Count;

    public static CardCatalog Empty { get; } = new(new Dictionary<string, CatalogCard>(StringComparer.OrdinalIgnoreCase));

    private static readonly Lazy<CardCatalog> LazyDefault = new(() => LoadFrom(DataPaths.CatalogDir()));

    /// <summary>Catalogue du dépôt (lazy). Jamais null : <see cref="Empty"/> si data/catalog manque.</summary>
    public static CardCatalog Default => LazyDefault.Value;

    /// <summary>« CARD.X » ou « X ». Null si la carte est inconnue du catalogue.</summary>
    public CatalogCard? Get(string? id)
    {
        var bare = Ids.Bare(id);
        return bare.Length > 0 && _cards.TryGetValue(bare, out var c) ? c : null;
    }

    /// <summary>Une malédiction d'après le catalogue (type ou rareté « Curse »).</summary>
    public bool IsCurse(string? id) => Get(id)?.IsCurse == true;

    /// <summary>Charge un dossier <c>data/catalog</c>. Fichier absent ou illisible → catalogue vide.</summary>
    public static CardCatalog LoadFrom(string? catalogDir)
    {
        var cards = new Dictionary<string, CatalogCard>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(catalogDir)) return new CardCatalog(cards);

        var path = Path.Combine(catalogDir!, "cards.json");
        if (!File.Exists(path)) return new CardCatalog(cards);

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new CardCatalog(cards);

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var className = Str(el, "class");
                var id = NameFormat.ToCardId(className);
                if (id.Length == 0) continue;

                var cost = Num(el, "cost");
                cards[id] = new CatalogCard(
                    id, className!, Str(el, "type"), Str(el, "rarity"), cost.HasValue ? (int)cost.Value : null);
            }
        }
        catch (JsonException)
        {
            // Catalogue corrompu : on continue sans lui (les termes dépendants seront omis).
        }
        catch (IOException)
        {
        }

        return new CardCatalog(cards);
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
            ? d
            : null;
}
