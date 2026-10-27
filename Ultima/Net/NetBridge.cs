using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace Ultima.Net;

/// <summary>
/// Pont réseau minimal et défensif.
/// - ne fait rien hors multi ;
/// - ne lève jamais d'exception vers le jeu ;
/// - s'attache une seule fois par service réseau.
/// </summary>
public static class NetBridge
{
    private static INetGameService? _attached;
    private static bool _registered;

    /// <summary>Dernier ping reçu (texte déjà formaté) + compteur, pour l'UI.</summary>
    public static string LastPing { get; private set; } = "";
    public static int PingCount { get; private set; }
    public static event Action? PingReceived;

    public static bool IsMultiplayer
    {
        get
        {
            try
            {
                var net = RunManager.Instance?.NetService;
                return net != null && net.Type.IsMultiplayer();
            }
            catch { return false; }
        }
    }

    public static void Attach()
    {
        try
        {
            var net = RunManager.Instance?.NetService;
            if (net == null) return;
            if (!net.Type.IsMultiplayer()) return;
            if (ReferenceEquals(_attached, net) && _registered) return;

            _attached = net;
            net.RegisterMessageHandler<UltimaPingMessage>(OnPing);
            _registered = true;
            ModEntry.Write($"net: attached (type={net.Type})");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"net: attach failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void SendPing(string text)
    {
        try
        {
            var net = RunManager.Instance?.NetService;
            if (net == null || !net.Type.IsMultiplayer()) return;

            var msg = new UltimaPingMessage
            {
                Turn = 0,
                SenderId = 0,
                Text = text ?? "",
            };
            net.SendMessage(msg);
            ModEntry.Write($"net: ping sent '{msg.Text}'");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"net: send failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void OnPing(UltimaPingMessage message, ulong senderId)
    {
        try
        {
            PingCount++;
            LastPing = $"PING from {senderId}: {message.Text}";
            ModEntry.Write($"net: ping received from {senderId} '{message.Text}'");
            PingReceived?.Invoke();
        }
        catch (Exception ex)
        {
            ModEntry.Write($"net: receive handler failed: {ex.Message}");
        }
    }
}
