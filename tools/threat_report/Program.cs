using System.Globalization;
using Ultima.Engine.Scoring.Threat;

namespace Ultima.Tools.ThreatReport;

/// <summary>
/// Confronte la menace prédite (MaxHit, majorant hors rotation) aux dégâts réellement observés
/// sur le corpus. Sert de porte de sortie au modèle de statuts : si l'écart de base est déjà
/// énorme, modéliser ±25-50 % de plus ne change rien au classement (docs/50).
/// Usage : dotnet run --project tools/threat_report -- [ascension]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var ascension = args.Length > 0 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ? a : 0;
        var model = ThreatModel.Default;

        var rows = new List<(string Id, int MaxHit, double Observed, int Runs, double? Turns, double Ratio)>();

        foreach (var encounter in model.Encounters)
        {
            if (model.AverageDamage(encounter.Id, null) is not { } observed || observed <= 0) continue;

            var maxHit = model.MaxHit(encounter.Id, ascension);
            if (maxHit <= 0) continue;

            var stats = model.Encounter(encounter.Id)?.Stats;
            var turns = model.AverageTurns(encounter.Id);
            // MaxHit est un majorant PAR TOUR : on le compare aux dégâts observés PAR TOUR.
            var perTurn = turns is > 0 ? observed / turns.Value : observed;
            rows.Add((encounter.Id, maxHit, perTurn, (int)(stats?.Total ?? 0), turns, maxHit / perTurn));
        }

        if (rows.Count == 0)
        {
            Console.Error.WriteLine("aucune rencontre avec dégâts observés");
            return 1;
        }

        rows.Sort((left, right) => right.Ratio.CompareTo(left.Ratio));

        Console.WriteLine($"Menace prédite (par tour) vs dégâts observés par tour — ascension {ascension}, {rows.Count} rencontres");
        Console.WriteLine($"{"rencontre",-34}{"MaxHit",7}{"obs/tour",9}{"ratio",7}{"runs",8}{"tours",7}");
        foreach (var row in rows.Take(15))
            Console.WriteLine($"{row.Id,-34}{row.MaxHit,7}{row.Observed,9:0.0}{row.Ratio,7:0.00}{row.Runs,8}{(row.Turns is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) : "—"),7}");

        var ratios = rows.Select(r => r.Ratio).OrderBy(x => x).ToArray();
        Console.WriteLine();
        Console.WriteLine($"ratio médian {Percentile(ratios, 0.50):0.00} · p10 {Percentile(ratios, 0.10):0.00} · p90 {Percentile(ratios, 0.90):0.00}");
        Console.WriteLine($"rencontres où MaxHit > 2x l'observé : {rows.Count(r => r.Ratio > 2)} / {rows.Count}");
        Console.WriteLine($"rencontres où MaxHit < l'observé : {rows.Count(r => r.Ratio < 1)} / {rows.Count}");

        // Sensibilité : un statut qui change les dégâts de ±25 % déplace-t-il le ratio ? Oui, mais
        // la dispersion entre rencontres est déjà bien plus grande que cet effet.
        var spread = Percentile(ratios, 0.90) / Math.Max(0.01, Percentile(ratios, 0.10));
        Console.WriteLine($"dispersion p90/p10 entre rencontres : {spread:0.0}x " +
                          $"(un modificateur de statut vaut 0,75-1,5x, soit x2 au plus)");
        return 0;
    }

    private static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Round(q * (sorted.Length - 1), MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
