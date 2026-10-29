namespace Ultima.Knowledge.Text;

public enum TextValueKind { Number, Bool, String, IfUpgraded }

/// <summary>Valeur d'une variable de description (équivalent statique d'un <c>DynamicVar</c> du jeu).</summary>
public readonly record struct TextValue(TextValueKind Kind, decimal Number = 0m, bool Bool = false, string? Str = null)
{
    public static TextValue Of(decimal n) => new(TextValueKind.Number, Number: n);
    public static TextValue Of(bool b) => new(TextValueKind.Bool, Bool: b);
    public static TextValue Of(string s) => new(TextValueKind.String, Str: s);

    /// <summary><c>IfUpgradedVar</c> : <c>show</c> prend la 1re option si amélioré, la 2e sinon.</summary>
    public static TextValue IfUpgraded(bool upgraded) => new(TextValueKind.IfUpgraded, Number: upgraded ? 1 : 0, Bool: upgraded);

    /// <summary>Rendu brut (comme <c>DynamicVar.ToString()</c> : valeur entière).</summary>
    public override string ToString() => Kind switch
    {
        TextValueKind.Number => FormatNumber(Number),
        TextValueKind.Bool => Bool ? "True" : "False",
        TextValueKind.String => Str ?? "",
        TextValueKind.IfUpgraded => Bool ? "1" : "0",
        _ => "",
    };

    internal static string FormatNumber(decimal n) =>
        ((int)n).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
