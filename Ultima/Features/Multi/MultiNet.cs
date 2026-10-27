using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Ultima.Config;
using Ultima.Core.Model;
using Ultima.Core.Priority;

namespace Ultima.Features.Multi;

/// <summary>
/// F4 voie B (messages custom), active UNIQUEMENT si multi ET <see cref="UltimaConfig.CustomNetMessages"/> (R8).
/// - hello envoyé une fois au début du combat, re-hello une fois en réponse à un nouveau pair ;
/// - pair « capable » = a envoyé un hello de même version (<see cref="PriorityProtocol.Version"/>) ;
/// - priorité émise seulement si tous les pairs distants connus sont capables (≥ 1), et seulement
///   quand l'ensemble des alertes locales change (<see cref="PriorityRules.Signature"/>) ;
/// - réception : version différente → jeté (le contrôle de tour périmé est fait par MultiFeature).
/// Handlers enregistrés par instance de service, désenregistrés en fin de combat
/// (<c>INetGameService.UnregisterMessageHandler</c>, Multiplayer/Game/INetGameService.cs:75).
/// </summary>
internal sealed class MultiNet
{
    private static readonly string ModVersion =
        typeof(MultiNet).Assembly.GetName().Version?.ToString() ?? "?";

    /// <summary>(joueur émetteur, classe ("" = effacer), nom, tour).</summary>
    private readonly Action<ulong, string, string, int> _onPriority;
    private readonly MessageHandlerDelegate<UltimaHelloMessage> _helloHandler;
    private readonly MessageHandlerDelegate<UltimaPriorityMessage> _priorityHandler;

    private readonly HashSet<ulong> _capable = new();
    private readonly HashSet<ulong> _helloReplied = new();
    private INetGameService? _service;   // service sur lequel les handlers sont enregistrés
    private string? _lastSentSignature = "";

    public MultiNet(Action<ulong, string, string, int> onPriority)
    {
        _onPriority = onPriority;
        _helloHandler = OnHello;
        _priorityHandler = OnPriority;
    }

    public bool IsActive => _service != null;

    public void Start()
    {
        try
        {
            if (!UltimaConfig.CustomNetMessages)
            {
                ModEntry.Write("multi: voie B désactivée (config)");
                return;
            }
            var net = RunManager.Instance?.NetService;
            if (net == null || !net.Type.IsMultiplayer()) return;

            if (!ReferenceEquals(_service, net))
            {
                Stop();
                net.RegisterMessageHandler(_helloHandler);
                net.RegisterMessageHandler(_priorityHandler);
                _service = net;
                ModEntry.Write($"multi: voie B active (type={net.Type}, v{PriorityProtocol.Version})");
            }

            _capable.Clear();
            _helloReplied.Clear();
            _lastSentSignature = "";   // rien d'annoncé : pas d'« effacer » inutile au 1er snapshot
            SendHello("start");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: voie B start failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Stop()
    {
        var net = _service;
        _service = null;
        _capable.Clear();
        _helloReplied.Clear();
        _lastSentSignature = "";
        if (net == null) return;
        try
        {
            net.UnregisterMessageHandler(_helloHandler);
            net.UnregisterMessageHandler(_priorityHandler);
            ModEntry.Write("multi: voie B arretee (handlers desenregistres)");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: voie B stop failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Émet l'ensemble local s'il a changé et si tous les pairs connus sont capables.</summary>
    public void MaybeSendPriorities(CombatSnapshot s, IReadOnlyList<PriorityAlert> local)
    {
        try
        {
            var net = _service;
            if (net == null || s == null) return;

            var signature = PriorityRules.Signature(local, s.Turn);
            if (signature == _lastSentSignature) return;

            var remotes = (s.Players ?? Array.Empty<PlayerSnapshot>()).Where(p => p != null && !p.IsLocal).Select(p => p.NetId);
            if (!PriorityProtocol.CanSendPriority(remotes, _capable)) return;   // jamais vers un pair sans hello

            var me = SafeNetId(net) ?? s.LocalNetId;
            net.SendMessage(new UltimaPriorityMessage   // « effacer » puis l'ensemble courant (canal Reliable, ordonné)
            {
                PlayerNetId = me, CardClass = "", CardName = "",
                TurnNumber = s.Turn, ProtocolVersion = PriorityProtocol.Version,
            });
            foreach (var a in local ?? Array.Empty<PriorityAlert>())
            {
                net.SendMessage(new UltimaPriorityMessage
                {
                    PlayerNetId = me, CardClass = a.CardClass ?? "", CardName = a.CardName ?? "",
                    TurnNumber = s.Turn, ProtocolVersion = PriorityProtocol.Version,
                });
            }
            _lastSentSignature = signature;
            var list = local is { Count: > 0 } ? string.Join(",", local.Select(a => a.CardClass)) : "-";
            ModEntry.Write($"multi: priority sent [{list}] turn={s.Turn} peers={_capable.Count}");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: priority send failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SendHello(string reason)
    {
        try
        {
            var net = _service;
            if (net == null) return;
            var me = SafeNetId(net) ?? 0;
            net.SendMessage(new UltimaHelloMessage
            {
                ProtocolVersion = PriorityProtocol.Version,
                ModVersion = ModVersion,
                SenderNetId = me,
            });
            ModEntry.Write($"multi: hello sent {me} ({reason})");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: hello send failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnHello(UltimaHelloMessage message, ulong senderId)
    {
        try
        {
            if (message == null || _service == null) return;
            var peer = message.SenderNetId != 0 ? message.SenderNetId : senderId;
            if (peer == SafeNetId(_service)) return;   // écho de notre propre hello

            ModEntry.Write($"multi: hello received {peer} (v{message.ProtocolVersion}, mod {message.ModVersion}, via {senderId})");
            if (!PriorityProtocol.IsCompatible(message.ProtocolVersion))
            {
                ModEntry.Write($"multi: hello {peer} ignore (version {message.ProtocolVersion} != {PriorityProtocol.Version})");
                return;
            }
            if (!_capable.Add(peer)) return;           // déjà connu : pas de re-hello (pas de ping-pong)

            _lastSentSignature = null;                 // le nouveau pair recevra l'ensemble courant au prochain snapshot
            if (_helloReplied.Add(peer)) SendHello($"reply {peer}");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: hello handler failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnPriority(UltimaPriorityMessage message, ulong senderId)
    {
        try
        {
            if (message == null || _service == null) return;
            if (!PriorityProtocol.IsCompatible(message.ProtocolVersion))
            {
                ModEntry.Write($"multi: priority received from {senderId} ignore (version {message.ProtocolVersion})");
                return;
            }
            var player = message.PlayerNetId != 0 ? message.PlayerNetId : senderId;
            if (player == SafeNetId(_service)) return;
            _onPriority(player, message.CardClass ?? "", message.CardName ?? "", message.TurnNumber);
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: priority handler failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ulong? SafeNetId(INetGameService net)
    {
        try { return net.NetId; }
        catch { return null; }
    }
}
