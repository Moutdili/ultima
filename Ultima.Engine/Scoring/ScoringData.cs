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
            var shallow = Path.Combine(env!, "catalog");
            if (Directory.Exists(shallow)) return shallow;
            // Override périmé (worktree supprimé, chemin déplacé) : on l'ignore et on résout par
            // défaut. Sinon le moteur charge zéro catalogue en silence et les tests deviennent
            // dépendants du shell qui a lancé le process.
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

    /// <summary>Même résolution que <see cref="ResolveCatalogDir"/>, pour <c>data/calibration/</c>.</summary>
    public static string ResolveCalibrationDir(string? explicitDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;

        var env = Environment.GetEnvironmentVariable("ULTIMA_DATA");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var candidate = Path.Combine(env!, "data", "calibration");
            if (Directory.Exists(candidate)) return candidate;
            var shallow = Path.Combine(env!, "calibration");
            if (Directory.Exists(shallow)) return shallow;
        }

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "data", "calibration");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return Path.Combine("data", "calibration");
    }

    public static string ResolveMetaDir(string? explicitDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;

        var env = Environment.GetEnvironmentVariable("ULTIMA_DATA");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var candidate = Path.Combine(env!, "data", "meta");
            if (Directory.Exists(candidate)) return candidate;
            var shallow = Path.Combine(env!, "meta");
            if (Directory.Exists(shallow)) return shallow;
            // Idem catalog : un override qui ne mène nulle part ne doit pas masquer le dépôt.
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
