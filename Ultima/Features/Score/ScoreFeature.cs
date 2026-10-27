using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using Ultima.Core.Score;

namespace Ultima.Features.Score;

/// <summary>
/// F5 côté mod : attribution des dégâts reçus par les ennemis, export CSV par combat.
/// Read-only : abonnement à CombatHistory.Changed et lecture des entrées, rien d'autre.
///
/// Piège (v0.107.1) : History.Clear() est appelé AVANT CombatWon/CombatEnded
/// (CombatManager.cs:915 et :986) → on accumule au fil de l'eau avec un curseur
/// (entrées déjà ingérées) remis à 0 quand le nombre d'entrées diminue (Clear),
/// et on ne lit jamais l'historique à la fin. Changed est synchrone après chaque Add
/// (CombatHistory.cs:123-128) et après Clear (:32-36).
/// </summary>
public static class ScoreFeature
{
    private const int MaxErrorLogsPerCombat = 3;

    private static readonly object Gate = new();
    private static readonly DamageLedger Ledger = new();
    private static CombatHistory? _history;
    private static int _cursor;
    private static int _errors;
    private static bool _seeded;
    private static bool _open;
    private static readonly Dictionary<ulong, string> Names = new();

    /// <summary>mods/Ultima/runscore.csv (même dossier que le log). Jamais de .json (R7).</summary>
    public static string CsvPath =>
        Path.Combine(Path.GetDirectoryName(ModEntry.LogPath) ?? ".", "runscore.csv");

    /// <summary>Abonne History.Changed et remet le ledger à zéro (nouveau combat_index).</summary>
    public static void OnCombatStart()
    {
        try
        {
            lock (Gate)
            {
                Detach();
                if (_open)
                {
                    // Jamais de perte silencieuse ; un échec d'écriture n'empêche pas le nouveau combat.
                    try { Flush("previous combat not closed"); }
                    catch (Exception ex) { ModEntry.Write($"score: flush of unclosed combat failed {ex.GetType().Name}: {ex.Message}"); }
                }
                SeedOnce();
                Ledger.BeginCombat();
                Names.Clear();
                _cursor = 0;
                _errors = 0;
                _open = true;

                var history = CombatManager.Instance?.History;
                if (history == null)
                {
                    ModEntry.Write($"score: combat {Ledger.CombatIndex} start — no history, nothing recorded");
                    return;
                }
                _history = history;
                history.Changed += OnHistoryChanged;
                // Dégâts déjà présents (ex. reliques de début de combat avant l'injection de l'UI).
                Ingest();
                ModEntry.Write($"score: combat {Ledger.CombatIndex} start (pre-existing hits={Ledger.ObservedHits})");
            }
        }
        catch (Exception ex)
        {
            ModEntry.Write($"score: start failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Désabonne, ajoute le combat à runscore.csv (en-tête si fichier neuf), logge le résumé.</summary>
    public static void OnCombatEnd()
    {
        try
        {
            lock (Gate)
            {
                Detach();
                if (_open) Flush("combat end"); // sinon : pas de OnCombatStart, ou déjà flushé
            }
        }
        catch (Exception ex)
        {
            ModEntry.Write($"score: end failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Ajoute le combat courant au CSV (en-tête si fichier neuf) et logge le résumé.</summary>
    private static void Flush(string why)
    {
        _open = false;
        var path = CsvPath;
        var lines = CsvWriter.Lines(Ledger);
        if (lines.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var fresh = !File.Exists(path) || new FileInfo(path).Length == 0;
            File.AppendAllText(path, CsvWriter.Render(Ledger, includeHeader: fresh));
        }

        ModEntry.Write(
            $"score: combat {Ledger.CombatIndex} total={Ledger.ObservedUnblocked} " +
            $"attributed={Ledger.AttributedUnblocked} unattributed={Ledger.UnattributedUnblocked} " +
            $"rows={lines.Count} csv={path}");

        var byReason = Ledger.UnattributedByReason();
        if (byReason.Count > 0)
            ModEntry.Write("score: unattributed by reason " +
                           string.Join(" ", byReason.Select(kv => $"{kv.Key}={kv.Value}")));
        if (!Ledger.Reconciles)
            ModEntry.Write($"score: RECONCILIATION FAILED combat {Ledger.CombatIndex} (hits={Ledger.ObservedHits})");
        if (_errors > 0)
            ModEntry.Write($"score: {_errors} history entries skipped on read errors");
        if (why != "combat end")
            ModEntry.Write($"score: flushed combat {Ledger.CombatIndex} ({why})");
    }

    private static void OnHistoryChanged()
    {
        try
        {
            lock (Gate)
            {
                if (_open) Ingest();
            }
        }
        catch (Exception ex)
        {
            ModEntry.Write($"score: history changed failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Detach()
    {
        var h = _history;
        _history = null;
        if (h != null) h.Changed -= OnHistoryChanged;
    }

    /// <summary>Continue la numérotation du CSV existant (une fois par session de jeu).</summary>
    private static void SeedOnce()
    {
        if (_seeded) return;
        _seeded = true;
        try
        {
            var path = CsvPath;
            if (File.Exists(path)) Ledger.SeedCombatIndex(CsvWriter.LastCombatIndex(File.ReadLines(path)));
        }
        catch (Exception ex)
        {
            ModEntry.Write($"score: csv seed failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Ingère les entrées après le curseur ; un Clear (moins d'entrées) remet le curseur à 0.</summary>
    private static void Ingest()
    {
        var h = _history;
        if (h == null) return;
        var entries = h.Entries as IReadOnlyList<CombatHistoryEntry> ?? h.Entries.ToList();
        if (entries.Count < _cursor) _cursor = 0;
        for (; _cursor < entries.Count; _cursor++)
        {
            if (entries[_cursor] is not DamageReceivedEntry e) continue;
            try
            {
                var row = ToRow(e);
                if (row != null) Ledger.Record(row);
            }
            catch (Exception ex)
            {
                _errors++;
                if (_errors <= MaxErrorLogsPerCombat)
                    ModEntry.Write($"score: entry skipped {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Traduction d'une entrée. Receveur non ennemi → ignoré. Dealer joueur + CardSource → attribué
    /// à la carte ; tout le reste → non attribué avec raison explicite (jamais inventé).
    /// </summary>
    private static DamageRow? ToRow(DamageReceivedEntry e)
    {
        var receiver = e.Receiver;
        var result = e.Result;
        if (receiver == null || result == null || !receiver.IsEnemy) return null;

        var round = DamageLedger.ParseRound(SafeText(() => e.HumanReadableString));
        var card = e.CardSource;
        var cls = card?.GetType().Name ?? "";
        var title = card != null ? SafeText(() => card.Title) : "";
        int unblocked = result.UnblockedDamage, blocked = result.BlockedDamage, overkill = result.OverkillDamage;
        var killed = result.WasTargetKilled;

        var dealer = e.Dealer;
        if (dealer == null)
            return new DamageRow(round, cls, title, false, UnattributedReason.NoDealer, 0, "",
                unblocked, blocked, overkill, killed);

        var player = dealer.Player;
        if (player != null)
        {
            var name = DealerName(dealer, player.NetId);
            return card != null
                ? new DamageRow(round, cls, title, true, "", player.NetId, name, unblocked, blocked, overkill, killed)
                : new DamageRow(round, "", "", false, UnattributedReason.NoCardSource, player.NetId, name,
                    unblocked, blocked, overkill, killed);
        }

        var owner = dealer.PetOwner;
        if (owner != null)
            return new DamageRow(round, cls, title, false, UnattributedReason.PetDealer, owner.NetId,
                SafeText(() => dealer.Name), unblocked, blocked, overkill, killed);

        return new DamageRow(round, cls, title, false, UnattributedReason.NonPlayerDealer, 0,
            SafeText(() => dealer.Name), unblocked, blocked, overkill, killed);
    }

    /// <summary>Nom stable par NetId sur le combat (une lecture ratée ne doit pas scinder les lignes).</summary>
    private static string DealerName(Creature dealer, ulong netId)
    {
        if (Names.TryGetValue(netId, out var cached)) return cached;
        var n = SafeText(() => dealer.Name);
        if (n.Length == 0) return netId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Names[netId] = n;
        return n;
    }

    private static string SafeText(Func<string?> read)
    {
        try
        {
            return read() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
