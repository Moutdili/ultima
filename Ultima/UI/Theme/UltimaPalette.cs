using Godot;

namespace Ultima.UI.Theme;

/// <summary>
/// SEULE source de couleurs de l'UI (docs/17 §1.1, §1.4, §3) : aucun <c>new Color(</c> ailleurs.
/// Valeurs copiées de StsColors / Models/Characters (v0.107.1). Aucune autre couleur.
/// </summary>
public static class UltimaPalette
{
    // §1.1 — StsColors
    public static readonly Color Cream = Color.FromHtml("#FFF6E2");
    public static readonly Color HalfTransparentCream = Color.FromHtml("#FFF6E280");
    public static readonly Color Gold = Color.FromHtml("#EFC851");
    public static readonly Color Red = Color.FromHtml("#FF5555");
    public static readonly Color Green = Color.FromHtml("#7FFF00");
    public static readonly Color Blue = Color.FromHtml("#87CEEB");
    public static readonly Color DarkBlue = Color.FromHtml("#67AEEB");
    public static readonly Color Orange = Color.FromHtml("#FFA518");
    public static readonly Color Pink = Color.FromHtml("#FF78A0");
    public static readonly Color Purple = Color.FromHtml("#EE82EE");
    public static readonly Color Aqua = Color.FromHtml("#2AEBBE");
    public static readonly Color Gray = Color.FromHtml("#808080");
    public static readonly Color LightGray = Color.FromHtml("#BFBFBF");

    // §3 — verre
    /// <summary>Teinte verre #192930 (opaque ; le shader porte sa propre alpha 0.78).</summary>
    public static readonly Color GlassTint = Color.FromHtml("#192930");
    /// <summary>Fond du repli StyleBoxFlat : #192930 @ 0.72.</summary>
    public static readonly Color GlassFill = new Color(GlassTint, 0.72f);
    /// <summary>Liseré « métal » : gold @ 0.18, 1 px.</summary>
    public static readonly Color GlassBorder = new Color(Gold, 0.18f);
    /// <summary>Ombre portée : noir @ 0.35.</summary>
    public static readonly Color GlassShadow = new Color(0f, 0f, 0f, 0.35f);
    /// <summary>Highlight interne : blanc @ 0.06.</summary>
    public static readonly Color GlassHighlight = new Color(1f, 1f, 1f, 0.06f);

    // §1.4 — personnages (clé = nom de classe C#)
    private static readonly Dictionary<string, (Color Name, Color? EnergyOutline)> Characters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ironclad"] = (Color.FromHtml("#FF5555"), Color.FromHtml("#801212")),
            ["Silent"] = (Color.FromHtml("#7FFF00"), Color.FromHtml("#004F04")),
            ["Defect"] = (Color.FromHtml("#87CEEB"), Color.FromHtml("#163E64")),
            ["Regent"] = (Color.FromHtml("#FFA518"), Color.FromHtml("#784000")),
            ["Necrobinder"] = (Color.FromHtml("#EE82EE"), Color.FromHtml("#702D6F")),
            ["Deprived"] = (Color.FromHtml("#EFC851"), null),
        };

    /// <summary><c>NameColor</c> du personnage ; inconnu → <see cref="Cream"/>.</summary>
    public static Color ForCharacter(string characterId) =>
        characterId != null && Characters.TryGetValue(characterId, out var c) ? c.Name : Cream;

    /// <summary><c>EnergyLabelOutlineColor</c> du personnage ; null si absent (Deprived, inconnu).</summary>
    public static Color? OutlineForCharacter(string characterId) =>
        characterId != null && Characters.TryGetValue(characterId, out var c) ? c.EnergyOutline : null;
}
