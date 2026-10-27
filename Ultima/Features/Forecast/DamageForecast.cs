using Ultima.Core.Forecast;
using Ultima.Core.Model;

namespace Ultima.Features.Forecast;

/// <summary>
/// Adapter côté mod de F1/F2 : <see cref="ForecastEngine.Build"/> + contrôle oracle contre le label du jeu.
/// Appelé depuis le jeu → ne lève jamais : toute exception est loggée et renvoie null.
/// </summary>
public static class DamageForecast
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> ReportedMismatches = new();
    private static int _oracleTurn = int.MinValue;
    private static string? _lastSummary;

    /// ForecastEngine.Build + contrôle oracle ; null si exception (loggée).
    public static TurnForecast? Build(CombatSnapshot snapshot)
    {
        try
        {
            if (snapshot is null) return null;

            var forecast = ForecastEngine.Build(snapshot);
            lock (Gate)
            {
                CheckOracle(snapshot);
                LogSummary(snapshot, forecast);
            }
            return forecast;
        }
        catch (Exception ex)
        {
            ModEntry.Write($"forecast: Build failed: {ex}");
            return null;
        }
    }

    /// <summary>Mismatch loggé une seule fois par (CombatId, Turn, label) ; mémoire purgée à chaque changement de tour.</summary>
    private static void CheckOracle(CombatSnapshot snapshot)
    {
        if (snapshot.Turn != _oracleTurn)
        {
            ReportedMismatches.Clear();
            _oracleTurn = snapshot.Turn;
        }

        foreach (var enemy in snapshot.Enemies ?? Array.Empty<EnemySnapshot>())
        {
            if (enemy is null) continue;
            foreach (var intent in enemy.Intents ?? Array.Empty<IntentSnapshot>())
            {
                if (!ForecastEngine.IsAttack(intent)) continue;
                if (IntentOracle.Matches(intent, out _)) continue;

                var key = $"{enemy.CombatId}|{snapshot.Turn}|{intent.Label}";
                if (!ReportedMismatches.Add(key)) continue;

                ModEntry.Write($"forecast: oracle mismatch {enemy.Name} label='{OneLine(intent.Label)}' " +
                               $"perHit={intent.PerHit} repeats={intent.Repeats}");
            }
        }
    }

    /// <summary>Résumé loggé seulement quand il change (pas de spam à chaque frame).</summary>
    private static void LogSummary(CombatSnapshot snapshot, TurnForecast forecast)
    {
        var lethal = forecast.LethalHitIndex?.ToString() ?? "none";
        var summary = $"forecast: T{snapshot.Turn} incoming={forecast.TotalIncoming} lethal={lethal}";
        if (summary == _lastSummary) return;
        _lastSummary = summary;
        ModEntry.Write(summary);
    }

    private static string OneLine(string? s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ');
}
