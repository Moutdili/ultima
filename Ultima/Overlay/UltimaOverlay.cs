using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Ultima.Net;

namespace Ultima.Overlay;

/// <summary>
/// Overlay V0.1 : panneau discret + test de broadcast (F7 = ping, F8 = cacher).
/// Le contenu analytique (F1/F2) se branche ici à l'étape suivante.
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
            Text = "ULTIMA — chargement…",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_label);

        NetBridge.Attach();
        NetBridge.PingReceived += Refresh;
        Refresh();
        SetProcess(true);
        ModEntry.Write("overlay ready");
    }

    public override void _ExitTree()
    {
        NetBridge.PingReceived -= Refresh;
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        // V0.1 : rien à recalculer par frame. V1 : forecast + létalité sur événement.
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        switch (key.Keycode)
        {
            case Key.F8:
                _hidden = !_hidden;
                Visible = !_hidden;
                GetViewport().SetInputAsHandled();
                break;
            case Key.F7:
                NetBridge.SendPing($"hello from {System.Environment.MachineName}");
                Refresh();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void Refresh()
    {
        if (_label == null) return;
        var net = NetBridge.IsMultiplayer ? "MULTI" : "SOLO";
        var last = string.IsNullOrEmpty(NetBridge.LastPing) ? "aucun ping" : NetBridge.LastPing;
        _label.Text = $"ULTIMA [{net}] · pings: {NetBridge.PingCount}\n{last}\nF7 = ping · F8 = hide";
    }
}
