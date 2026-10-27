using Godot;

namespace Ultima.UI.Theme;

/// <summary>Fabriques StyleBoxFlat (docs/17 §3). Couleurs : <see cref="UltimaPalette"/> uniquement.</summary>
public static class UltimaStyles
{
    public const int BorderWidth = 1;     // 1 px, jamais 2
    public const int CornerRadius = 10;
    public const int ShadowSize = 6;
    public const float ContentMargin = 10f;
    public const int BadgeRadius = 3;

    /// <summary>Repli « verre » sans shader : #192930 @0.72, liseré gold @0.18, radius 10, ombre noir @0.35.</summary>
    public static StyleBoxFlat Glass()
    {
        var s = new StyleBoxFlat
        {
            BgColor = UltimaPalette.GlassFill,
            BorderColor = UltimaPalette.GlassBorder,
            ShadowColor = UltimaPalette.GlassShadow,
            ShadowSize = ShadowSize,
            AntiAliasing = true,
        };
        s.SetBorderWidthAll(BorderWidth);
        s.SetCornerRadiusAll(CornerRadius);
        s.SetContentMarginAll(ContentMargin);
        return s;
    }

    /// <summary>Liseré seul (sans fond) posé SUR le verre shader : 1 px gold @0.18, radius 10
    /// (même rayon que le masque SDF du shader).</summary>
    public static StyleBoxFlat GlassEdge()
    {
        var s = new StyleBoxFlat
        {
            DrawCenter = false,
            BorderColor = UltimaPalette.GlassBorder,
            AntiAliasing = true,
        };
        s.SetBorderWidthAll(BorderWidth);
        s.SetCornerRadiusAll(CornerRadius);
        return s;
    }

    /// <summary>Ombre seule DERRIÈRE le verre shader : noir @0.35, taille 6, radius 10. Avec
    /// DrawCenter=false, StyleBoxFlat trace l'anneau d'ombre (dégradé vers l'extérieur) mais
    /// pas son centre : rien n'assombrit le verre par-dessous.</summary>
    public static StyleBoxFlat ShadowOnly()
    {
        var s = new StyleBoxFlat
        {
            DrawCenter = false,
            ShadowColor = UltimaPalette.GlassShadow,
            ShadowSize = ShadowSize,
            AntiAliasing = true,
        };
        s.SetCornerRadiusAll(CornerRadius);
        return s;
    }

    /// <summary>Fond du rail replié : verre sans ombre, coins doux.</summary>
    public static StyleBoxFlat Rail()
    {
        var s = new StyleBoxFlat { BgColor = UltimaPalette.GlassFill, BorderColor = UltimaPalette.GlassBorder, AntiAliasing = true };
        s.SetBorderWidthAll(BorderWidth);
        s.SetCornerRadiusAll(BadgeRadius);
        return s;
    }

    /// <summary>Pastille / badge plein (couleur palette).</summary>
    public static StyleBoxFlat Badge(Color color)
    {
        var s = new StyleBoxFlat { BgColor = color, AntiAliasing = true };
        s.SetCornerRadiusAll(BadgeRadius);
        return s;
    }
}
