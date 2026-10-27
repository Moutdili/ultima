using Godot;
using Ultima.Core.Model;
using Ultima.UI.Theme;

namespace Ultima.UI.Widgets;

/// <summary>Niveau 2 co-op : une ligne par joueur (pire cas), nom coloré par personnage.</summary>
public partial class ThreatList : VBoxContainer
{
    private readonly List<(HBoxContainer Box, Label Name, Label Info)> _lines = new();

    public ThreatList()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 0);
        Visible = false;
    }

    public void Bind(IReadOnlyList<PlayerThreat> players)
    {
        while (_lines.Count < players.Count) _lines.Add(CreateLine());
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            line.Box.Visible = i < players.Count;
            if (i >= players.Count) continue;
            var p = players[i];
            line.Name.Text = p.Name;
            UltimaFonts.SetColor(line.Name, UltimaPalette.ForCharacter(p.CharacterId));
            UltimaFonts.SetOutline(line.Name, UltimaPalette.OutlineForCharacter(p.CharacterId));
            line.Info.Text = OverlayText.Threat(p);
            UltimaFonts.SetColor(line.Info, p.LethalHitIndex != null ? UltimaPalette.Red : UltimaPalette.Cream);
        }
        Visible = players.Count > 0;
    }

    private (HBoxContainer, Label, Label) CreateLine()
    {
        var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 6);
        var name = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.Cream);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var info = UltimaFonts.NewLabel(FontRole.Mono, 14, UltimaPalette.Cream, trim: false);
        box.AddChild(name);
        box.AddChild(info);
        AddChild(box);
        return (box, name, info);
    }
}
