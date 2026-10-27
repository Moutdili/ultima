using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Ultima.Core.Model;
using Ultima.Core.Priority;
using Ultima.Features.Priority;

namespace Ultima.Features.Multi;

/// <summary>
/// F4 : alertes de priorité des AUTRES joueurs.
/// Voie A (toujours, multi) : survol distant via <c>HoveredModelTracker.HoverChanged</c> → <see cref="PrioritySource.RemoteHover"/>.
/// Voie B (si <c>UltimaConfig.CustomNetMessages</c>) : messages <see cref="UltimaPriorityMessage"/> → <see cref="PrioritySource.RemoteMessage"/>.
/// Read-only ; aucune méthode publique ne lève d'exception. Appels attendus sur le thread principal.
/// </summary>
public static class MultiFeature
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ulong, PriorityAlert> Hovers = new();            // voie A : 1 alerte max / joueur
    private static readonly Dictionary<ulong, List<PriorityAlert>> Messages = new();    // voie B : ensemble annoncé / joueur
    private static volatile IReadOnlyList<PriorityAlert> _remote = Array.Empty<PriorityAlert>();
    private static volatile CombatSnapshot? _last;
    private static HoverWatcher? _hover;
    private static MultiNet? _net;

    /// <summary>Alertes distantes modifiées → l'UI re-rend (lire <see cref="RemoteAlerts"/>).</summary>
    public static event Action? Changed;

    public static IReadOnlyList<PriorityAlert> RemoteAlerts => _remote;

    /// <summary>Abonne HoverChanged (+ voie B si config). Sans effet en solo.</summary>
    public static void OnCombatStart()
    {
        try
        {
            Reset();
            if (!IsMultiplayer())
            {
                ModEntry.Write("multi: solo, F4 inactive");
                return;
            }
            (_hover ??= new HoverWatcher(OnHover)).Attach();
            (_net ??= new MultiNet(OnRemotePriority)).Start();
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: combat start failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Désabonne, vide les alertes.</summary>
    public static void OnCombatEnd()
    {
        try
        {
            _hover?.Detach();
            _net?.Stop();
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: combat end failed {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Reset();
        }
    }

    /// <summary>Résout noms/personnages, expire les messages périmés ; voie B : émet si l'ensemble local a changé.</summary>
    public static void OnSnapshot(CombatSnapshot snapshot, IReadOnlyList<PriorityAlert> localAlerts)
    {
        try
        {
            if (snapshot == null) return;
            _last = snapshot;

            bool changed = false;
            lock (Gate)
            {
                foreach (var id in Hovers.Keys.ToList())
                {
                    var resolved = Resolve(snapshot, Hovers[id]);
                    if (!ReferenceEquals(resolved, Hovers[id])) { Hovers[id] = resolved; changed = true; }
                }
                foreach (var id in Messages.Keys.ToList())
                {
                    var list = Messages[id];
                    var kept = list.Where(a => !PriorityProtocol.IsStale(a.Turn, snapshot.Turn)).Select(a => Resolve(snapshot, a)).ToList();
                    if (kept.Count != list.Count || !kept.SequenceEqual(list, ReferenceEqualityComparer.Instance))
                    {
                        if (kept.Count == 0) Messages.Remove(id); else Messages[id] = kept;
                        changed = true;
                    }
                }
                if (changed) Rebuild();
            }
            if (changed) RaiseChanged();

            _net?.MaybeSendPriorities(snapshot, localAlerts ?? Array.Empty<PriorityAlert>());
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: snapshot failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---------- voie A ----------

    private static void OnHover(ulong playerId, string? cardClass, string? cardName)
    {
        try
        {
            var local = LocalNetId();
            if (local == null || playerId == local) return;   // local inconnu → on s'abstient (jamais d'auto-alerte)

            bool changed;
            lock (Gate)
            {
                if (cardClass != null && PriorityFeature.Catalog.Contains(cardClass))
                {
                    if (Hovers.TryGetValue(playerId, out var cur) && cur.CardClass == cardClass) return;
                    var s = _last;
                    Hovers[playerId] = PriorityRules.Remote(s, playerId, cardClass, cardName ?? cardClass, PrioritySource.RemoteHover, s?.Turn ?? 0);
                    ModEntry.Write($"multi: hover {playerId} -> {cardClass} (prio)");
                    changed = true;
                }
                else
                {
                    changed = Hovers.Remove(playerId);
                    if (changed) ModEntry.Write($"multi: hover {playerId} -> fin (alerte retiree)");
                }
                if (changed) Rebuild();
            }
            if (changed) RaiseChanged();
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: hover failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---------- voie B ----------

    private static void OnRemotePriority(ulong playerId, string cardClass, string cardName, int turn)
    {
        try
        {
            var s = _last;
            var current = s?.Turn ?? 0;
            if (PriorityProtocol.IsStale(turn, current))
            {
                ModEntry.Write($"multi: priority received {playerId} -> {cardClass} turn={turn} perime (tour {current}), jete");
                return;
            }

            lock (Gate)
            {
                if (cardClass.Length == 0)
                {
                    Messages.Remove(playerId);
                    ModEntry.Write($"multi: priority received {playerId} -> effacer turn={turn}");
                }
                else
                {
                    if (!Messages.TryGetValue(playerId, out var list)) Messages[playerId] = list = new List<PriorityAlert>();
                    list.Add(PriorityRules.Remote(s, playerId, cardClass, cardName, PrioritySource.RemoteMessage, turn));
                    ModEntry.Write($"multi: priority received {playerId} -> {cardClass} turn={turn}");
                }
                Rebuild();
            }
            RaiseChanged();
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: priority receive failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---------- interne ----------

    /// <summary>Messages (plus précis) puis survols, sans doublon (joueur, classe). Appelé sous Gate.</summary>
    private static void Rebuild()
    {
        var result = new List<PriorityAlert>();
        var seen = new HashSet<(ulong, string)>();
        foreach (var kv in Messages.OrderBy(k => k.Key))
            foreach (var a in kv.Value)
            {
                seen.Add((a.PlayerNetId, a.CardClass));
                result.Add(a);
            }
        foreach (var kv in Hovers.OrderBy(k => k.Key))
            if (!seen.Contains((kv.Value.PlayerNetId, kv.Value.CardClass)))
                result.Add(kv.Value);
        _remote = result;
    }

    private static PriorityAlert Resolve(CombatSnapshot s, PriorityAlert a)
    {
        var (name, characterId) = PriorityRules.ResolvePlayer(s, a.PlayerNetId);
        return name == a.PlayerName && characterId == a.CharacterId
            ? a
            : a with { PlayerName = name, CharacterId = characterId };
    }

    private static void Reset()
    {
        bool had;
        lock (Gate)
        {
            had = Hovers.Count > 0 || Messages.Count > 0 || _remote.Count > 0;
            Hovers.Clear();
            Messages.Clear();
            _remote = Array.Empty<PriorityAlert>();
        }
        _last = null;
        if (had) RaiseChanged();
    }

    private static void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { ModEntry.Write($"multi: Changed subscriber failed {ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool IsMultiplayer()
    {
        try
        {
            var net = RunManager.Instance?.NetService;
            return net != null && net.Type.IsMultiplayer();
        }
        catch { return false; }
    }

    private static ulong? LocalNetId()
    {
        try
        {
            var net = RunManager.Instance?.NetService;
            if (net != null) return net.NetId;
        }
        catch { /* repli snapshot */ }
        return _last?.LocalNetId;
    }
}
