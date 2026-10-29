using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Deck;

/// <summary>
/// Localise <c>data/catalog</c> (types de cartes, malédictions, profils de boss).
/// Même logique que <see cref="ScoringData"/> pour <c>data/meta</c> : voisin de la méta, sinon
/// remontée depuis l'assembly. Retourne null si absent (les termes dépendants sont alors omis,
/// jamais remplacés par un chiffre inventé).
/// </summary>
public static class DataPaths
{
    public static string? CatalogDir()
    {
        var metaDir = ScoringData.ResolveMetaDir();
        if (!string.IsNullOrWhiteSpace(metaDir))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(metaDir)) ?? ".", "catalog");
            if (File.Exists(Path.Combine(sibling, "cards.json"))) return sibling;
        }

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "data", "catalog");
            if (File.Exists(Path.Combine(candidate, "cards.json"))) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    /// <summary>
    /// Localise <c>data/calibration/weights.json</c> (poids de G calibrés, écrit par
    /// <c>tools/calibration/fit_weights.py</c>). Priorité : <c>ULTIMA_DATA</c>, voisin de
    /// <c>data/catalog</c>, puis remontée depuis l'assembly. Null si absent — l'appelant retombe
    /// alors sur les constantes du code, jamais sur un chiffre inventé.
    /// </summary>
    public static string? CalibrationFile()
    {
        var env = Environment.GetEnvironmentVariable("ULTIMA_DATA");
        if (!string.IsNullOrWhiteSpace(env))
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(env!, "calibration", "weights.json"),
                         Path.Combine(env!, "data", "calibration", "weights.json"),
                     })
            {
                if (File.Exists(candidate)) return candidate;
            }
        }

        var catalog = CatalogDir();
        if (!string.IsNullOrWhiteSpace(catalog))
        {
            var dataDir = Path.GetDirectoryName(Path.GetFullPath(catalog));
            if (!string.IsNullOrWhiteSpace(dataDir))
            {
                var candidate = Path.Combine(dataDir!, "calibration", "weights.json");
                if (File.Exists(candidate)) return candidate;
            }
        }

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "data", "calibration", "weights.json");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }
}
