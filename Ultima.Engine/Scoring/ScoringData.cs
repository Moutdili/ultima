namespace Ultima.Engine.Scoring;

/// <summary>
/// Localise le dossier de données. Priorité : variable d'environnement ULTIMA_DATA,
/// puis remontée depuis l'assembly vers data/ (repo), puis "data/".
/// </summary>
public static class ScoringData
{
    public static string ResolveCatalogDir(string? explicitDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;

        var env = Environment.GetEnvironmentVariable("ULTIMA_DATA");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var candidate = Path.Combine(env!, "data", "catalog");
            if (Directory.Exists(candidate)) return candidate;
            return Path.Combine(env!, "catalog");
        }

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "data", "catalog");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return Path.Combine("data", "catalog");
    }

    public static string ResolveMetaDir(string? explicitDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;

        var env = Environment.GetEnvironmentVariable("ULTIMA_DATA");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var candidate = Path.Combine(env!, "data", "meta");
            if (Directory.Exists(candidate)) return candidate;
            if (Directory.Exists(Path.Combine(env!, "meta"))) return Path.Combine(env!, "meta");
            return candidate;
        }

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "data", "meta");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return Path.Combine("data", "meta");
    }
}
