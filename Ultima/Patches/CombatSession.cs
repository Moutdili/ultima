using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using Ultima.Config;
using Ultima.Core.Model;
using Ultima.Features.Forecast;
using Ultima.Features.Multi;
using Ultima.Features.Priority;
using Ultima.GameApi;
using Ultima.Net;
using Ultima.UI;

namespace Ultima.Patches;

/// <summary>
/// Point de composition (chef) : événements du jeu → reader → forecast → priorité/multi → UI.
/// Une session par NCombatUi. Recalcul sur événement, au plus une fois par frame (CallDeferred).
/// Read-only : on s'abonne à des événements et on lit des modèles, rien d'autre.
/// </summary>
internal sealed class CombatSession
{
    private static CombatSession? _current;

    private readonly NCombatUi _ui;
    private readonly UltimaOverlay _overlay;
    private CombatState? _lastState;
    private bool _scheduled;
    private bool _stopped;

    // SPEC §5 Performance : coût cible < 1 ms par mise à jour → mesuré et loggé en fin de session.
    private int _refreshCount;
    private double _refreshTotalMs;
    private double _refreshMaxMs;

    private CombatSession(NCombatUi ui, UltimaOverlay overlay)
    {
        _ui = ui;
        _overlay = overlay;
    }

    public static void Start(NCombatUi ui)
    {
        _current?.Stop("new combat ui");

        var overlay = new UltimaOverlay
        {
            Name = "UltimaOverlay",
            BlurAmount = UltimaConfig.BlurAmount,
            CollapseDelaySec = UltimaConfig.CollapseDelaySec,
        };
        ui.AddChild(overlay);

        var session = new CombatSession(ui, overlay);
        _current = session;
        session.Subscribe();
        ModEntry.Write("overlay injected");
        session.Schedule();
    }

    private void Subscribe()
    {
        var cm = CombatManager.Instance;
        cm.CombatSetUp += OnStateChanged;
        cm.StateTracker.CombatStateChanged += OnStateChanged;
        cm.TurnStarted += OnStateChanged;
        cm.CombatEnded += OnCombatEnded;
        MultiFeature.Changed += Schedule;
        _overlay.PingRequested += OnPingRequested;
        _overlay.TreeExiting += OnOverlayExiting;

        // R8 : tout le transport custom (ping V0.1 compris) est soumis à [net] custom_messages.
        if (UltimaConfig.CustomNetMessages) NetBridge.Attach();
        MultiFeature.OnCombatStart();
        ModEntry.Write($"session: started (multi={NetBridge.IsMultiplayer}, custom_net={UltimaConfig.CustomNetMessages})");
    }

    private void OnStateChanged(CombatState state)
    {
        _lastState = state;
        Schedule();
    }

    private void Schedule()
    {
        if (_stopped || _scheduled) return;
        _scheduled = true;
        Callable.From(Refresh).CallDeferred();
    }

    private void Refresh()
    {
        _scheduled = false;
        if (_stopped) return;
        var sw = Stopwatch.StartNew();
        try
        {
            if (!GodotObject.IsInstanceValid(_overlay) || _overlay.IsQueuedForDeletion())
            {
                Stop("overlay freed");
                return;
            }

            var snapshot = _lastState != null ? CombatReader.TryRead(_lastState) : CombatReader.TryRead();
            TurnForecast? forecast = null;
            var alerts = new List<PriorityAlert>();
            if (snapshot != null)
            {
                forecast = DamageForecast.Build(snapshot);
                var local = PriorityFeature.LocalAlerts(snapshot);
                alerts.AddRange(local);
                MultiFeature.OnSnapshot(snapshot, local);
            }
            alerts.AddRange(MultiFeature.RemoteAlerts);

            _overlay.SetAvoidRects(AvoidRects.Collect(_ui));
            _overlay.Render(snapshot, forecast, alerts);
        }
        catch (Exception ex)
        {
            ModEntry.Write($"session: refresh failed {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            var ms = sw.Elapsed.TotalMilliseconds;
            _refreshCount++;
            _refreshTotalMs += ms;
            if (ms > _refreshMaxMs) _refreshMaxMs = ms;
        }
    }

    private void OnPingRequested()
    {
        try
        {
            if (!UltimaConfig.CustomNetMessages)
            {
                ModEntry.Write("net: ping ignored (voie B off: [net] custom_messages=false)");
                return;
            }
            NetBridge.SendPing($"hello from {System.Environment.MachineName}");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"net: ping failed {ex.Message}");
        }
    }

    private void OnCombatEnded(CombatRoom _) => Stop("combat ended");

    private void OnOverlayExiting() => Stop("overlay exiting");

    private void Stop(string reason)
    {
        if (_stopped) return;
        _stopped = true;
        try
        {
            var cm = CombatManager.Instance;
            cm.CombatSetUp -= OnStateChanged;
            cm.StateTracker.CombatStateChanged -= OnStateChanged;
            cm.TurnStarted -= OnStateChanged;
            cm.CombatEnded -= OnCombatEnded;
            MultiFeature.Changed -= Schedule;
            if (GodotObject.IsInstanceValid(_overlay))
            {
                _overlay.PingRequested -= OnPingRequested;
                _overlay.TreeExiting -= OnOverlayExiting;
            }
            MultiFeature.OnCombatEnd();
            if (ReferenceEquals(_current, this)) _current = null;
            var avg = _refreshCount > 0 ? _refreshTotalMs / _refreshCount : 0;
            ModEntry.Write($"session: stopped ({reason}) refreshes={_refreshCount} " +
                           $"avg={avg:0.###}ms max={_refreshMaxMs:0.###}ms (cible < 1 ms)");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"session: stop failed {ex.Message}");
        }
    }
}
