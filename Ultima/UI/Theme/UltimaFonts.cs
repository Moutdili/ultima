using Godot;

namespace Ultima.UI.Theme;

public enum FontRole { Body, Title, Mono }

/// <summary>Polices du jeu (pck), chargées une fois, null-safe (repli : police par défaut).</summary>
public static class UltimaFonts
{
    public const string KreonRegular = "res://themes/kreon_regular_shared.tres";
    public const string KreonBold = "res://themes/kreon_bold_shared.tres";
    public const string SourceCodeProSemibold = "res://themes/source_code_pro_semibold_shared.tres";

    /// <summary>Taille minimale partout (skill sts2-overlay-ui : min 14 ; charte §6 : >= 12).</summary>
    public const int MinFontSize = 14;

    private static bool _loaded;
    private static Font? _body, _title, _mono;

    public static Font? Get(FontRole role)
    {
        if (!_loaded)
        {
            _loaded = true;
            _body = TryLoad(KreonRegular);
            _title = TryLoad(KreonBold);
            _mono = TryLoad(SourceCodeProSemibold);
        }
        return role switch { FontRole.Title => _title, FontRole.Mono => _mono, _ => _body };
    }

    private static Font? TryLoad(string path)
    {
        try
        {
            if (!ResourceLoader.Exists(path))
            {
                ModEntry.Write($"ui: police absente {path} -> police par defaut");
                return null;
            }
            return GD.Load<Font>(path);
        }
        catch (Exception ex)
        {
            ModEntry.Write($"ui: police {path} illisible ({ex.Message}) -> police par defaut");
            return null;
        }
    }

    /// <summary>Label non interactif (MouseFilter Ignore), une ligne, tronqué par ellipse si <paramref name="trim"/>.
    /// Taille bornée à <see cref="MinFontSize"/>.</summary>
    public static Label NewLabel(FontRole role, int size, Color color, bool trim = true)
    {
        var l = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (trim) l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var font = Get(role);
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", Math.Max(size, MinFontSize));
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    /// <summary>Change la couleur seulement si elle diffère (évite une notification de thème par Render).</summary>
    public static void SetColor(Label label, Color color)
    {
        if (label.GetThemeColor("font_color") != color) label.AddThemeColorOverride("font_color", color);
    }

    /// <summary>Outline co-op (EnergyLabelOutlineColor) ; null → pas d'outline.</summary>
    public static void SetOutline(Label label, Color? outline, int size = 4)
    {
        if (outline is Color c)
        {
            label.AddThemeColorOverride("font_outline_color", c);
            label.AddThemeConstantOverride("outline_size", size);
        }
        else
        {
            label.AddThemeConstantOverride("outline_size", 0);
        }
    }
}
