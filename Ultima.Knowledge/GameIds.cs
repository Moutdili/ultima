using System.Text.RegularExpressions;

namespace Ultima.Knowledge;

/// <summary>
/// Identifiants du jeu tels que vus dans les saves et le miroir : <c>CATEGORY.ENTRY</c>
/// (ex. <c>CARD.ASCENDERS_BANE</c>). L'entrée est le nom de classe C# « slugifié »
/// (reproduction de <c>StringHelper.Slugify</c>, <c>Helpers/StringHelper.cs:74</c>, v0.107.1).
/// </summary>
public static partial class GameIds
{
    public const string Card = "CARD";
    public const string Relic = "RELIC";
    public const string Potion = "POTION";
    public const string Enchantment = "ENCHANTMENT";
    public const string Encounter = "ENCOUNTER";
    public const string Character = "CHARACTER";

    [GeneratedRegex(@"([A-Za-z0-9]|\G(?!^))([A-Z])")]
    private static partial Regex CamelCase();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^A-Z0-9_]")]
    private static partial Regex SpecialChar();

    /// <summary><c>AscendersBane</c> → <c>ASCENDERS_BANE</c> (même algorithme que le jeu).</summary>
    public static string Slugify(string className)
    {
        var text = CamelCase().Replace(className.Trim(), "$1_$2");
        var upper = Whitespace().Replace(text.ToUpperInvariant(), "_");
        return SpecialChar().Replace(upper, "");
    }

    public static string Make(string category, string className) => category + "." + Slugify(className);

    /// <summary>Partie après le point (clé de localisation) ; l'id brut sans point est accepté tel quel.</summary>
    public static string Entry(string id)
    {
        var dot = id.IndexOf('.');
        return dot < 0 ? id : id[(dot + 1)..];
    }

    /// <summary>Normalise un id (casse, catégorie manquante) : <c>ascenders_bane</c> → <c>CARD.ASCENDERS_BANE</c>.</summary>
    public static string Normalize(string category, string id)
    {
        var trimmed = id.Trim().ToUpperInvariant();
        return trimmed.Contains('.') ? trimmed : category + "." + trimmed;
    }
}
