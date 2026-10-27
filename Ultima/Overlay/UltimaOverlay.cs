using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Ultima.Overlay;

/// <summary>
/// Overlay V0 : panneau discret, click-through, hotkey F8 pour masquer.
/// Le contenu réel (forecast, létalité, priorité) se branche ici en V1.
/// </summary>
public partial class UltimaOverlay : PanelContainer
{
    private Label? _label;
    private bool _hidden;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Modulate = new Color(1, 1, 1, 0.32f);

        _label = new Label
        {
            Text = "ULTIMA — chargement du combat…",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_label);

        SetProcess(true);
        ModEntry.Write("overlay ready");
    }

    public override void _Process(double delta)
    {
        // V0 : rien à recalculer. V1 : DamageForecast + LethalIndex + TurnPriority.
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F8)
        {
            _hidden = !_hidden;
            Visible = !_hidden;
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetStatus(string text)
    {
        if (_label != null) _label.Text = text;
    }
}
