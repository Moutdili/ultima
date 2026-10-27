using Godot;
using Ultima.Core.Model;
using Ultima.UI.Theme;

namespace Ultima.UI.Widgets;

/// <summary>
/// Alerte de priorité : <c>PRIO: Backflip (Mio)</c>. Joueur coloré par son personnage
/// (NameColor + outline énergie). Distant = « · distant » écrit (jamais la couleur seule).
/// </summary>
public partial class PriorityStrip : VBoxContainer
{
    public const int MaxLinesCompact = 1;   // niveau 1 : une seule ligne PRIO
    public const int MaxLinesDetail = 3;    // niveau 2

    private readonly List<(HBoxContainer Box, Label Card, Label Player)> _lines = new();

    public PriorityStrip()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 2);
        Visible = false;
    }

    public void Bind(IReadOnlyList<PriorityAlert> alerts, bool detail)
    {
        int n = Math.Min(alerts.Count, detail ? MaxLinesDetail : MaxLinesCompact);
        while (_lines.Count < n) _lines.Add(CreateLine());
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            line.Box.Visible = i < n;
            if (i >= n) continue;
            var a = alerts[i];
            line.Card.Text = a.CardName;
            line.Player.Text = OverlayText.PriorityPlayer(a);
            UltimaFonts.SetColor(line.Player, UltimaPalette.ForCharacter(a.CharacterId));
            UltimaFonts.SetOutline(line.Player, UltimaPalette.OutlineForCharacter(a.CharacterId));
        }
        Visible = n > 0;
    }

    private (HBoxContainer, Label, Label) CreateLine()
    {
        var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        var tag = UltimaFonts.NewLabel(FontRole.Title, 14, UltimaPalette.Gold, trim: false);
        tag.Text = "PRIO:";
        var card = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.Cream);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var player = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.Cream, trim: false);
        box.AddChild(tag);
        box.AddChild(card);
        box.AddChild(player);
        AddChild(box);
        return (box, card, player);
    }
}
