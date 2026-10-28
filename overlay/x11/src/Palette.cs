namespace Ultima.Overlay.X11;

/// <summary>
/// Seul endroit du moteur A ou une couleur est definie. Chaque valeur est tracee vers
/// docs/17-charte.md §1 (StsColors.cs livre par MegaCrit) ou vers la teinte de verre #192930
/// de la skill sts2-design-system. Aucune couleur inventee.
/// </summary>
internal readonly record struct Rgba(double R, double G, double B, double A = 1.0)
{
    public static Rgba Hex(string hex, double a = 1.0)
    {
        var h = hex.TrimStart('#');
        return new Rgba(Convert.ToInt32(h[..2], 16) / 255.0, Convert.ToInt32(h[2..4], 16) / 255.0,
            Convert.ToInt32(h[4..6], 16) / 255.0, a);
    }

    public Rgba WithAlpha(double a) => this with { A = a };
}

internal static class UltimaPalette
{
    public static readonly Rgba Glass = Rgba.Hex("#192930");      // teinte de verre (skill §v2)
    public static readonly Rgba Cream = Rgba.Hex("#FFF6E2");      // StsColors.cream
    public static readonly Rgba HalfCream = Rgba.Hex("#FFF6E2", 0x80 / 255.0); // halfTransparentCream
    public static readonly Rgba Gold = Rgba.Hex("#EFC851");       // StsColors.gold : liseré, focus
    public static readonly Rgba Red = Rgba.Hex("#FF5555");        // StsColors.red : PV
    public static readonly Rgba Orange = Rgba.Hex("#FFA518");     // StsColors.orange : avertissement
    public static readonly Rgba Aqua = Rgba.Hex("#2AEBBE");       // StsColors.aqua
    public static readonly Rgba Energy = Rgba.Hex("#40FFFF");     // energie (cout jouable) §1.3
    public static readonly Rgba RarityCommon = Rgba.Hex("#4D4B40");
    public static readonly Rgba RarityUncommon = Rgba.Hex("#005C75");
    public static readonly Rgba RarityRare = Rgba.Hex("#6B4B00");
    public static readonly Rgba Black = Rgba.Hex("#000000");      // ombre (noir, skill §3)

    /// <summary>Opacite du verre : ~0.78 (lecon v1 : 0.30 = invisible).</summary>
    public const double GlassAlpha = 0.78;
}
