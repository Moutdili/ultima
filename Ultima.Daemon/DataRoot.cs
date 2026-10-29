namespace Ultima.Daemon;

/// <summary>
/// Convention de données fixée par le daemon (demandée par Hermes, docs/26 §6.3) :
/// <c>ULTIMA_DATA</c> = dossier RACINE des données, celui qui contient <c>meta/</c>, <c>catalog/</c>,
/// <c>calibration/</c>… (dans le dépôt : <c>&lt;repo&gt;/data</c>). Par compatibilité, un dossier qui contient
/// <c>data/meta</c> (racine du dépôt) est accepté et ramené à <c>&lt;dossier&gt;/data</c>.
///
/// Ordre : option <c>--data</c>, puis variable <c>ULTIMA_DATA</c>, puis <c>data/</c> trouvé en remontant
/// depuis l'exécutable. La valeur retenue est RÉÉCRITE dans l'environnement du processus avant de
/// construire le moteur : tous ses résolveurs (ScoringData, DataPaths, CurseIndex…) lisent alors la même
/// racine. Une valeur qui ne mène à aucun <c>meta/</c> est ignorée (et signalée).
/// </summary>
public static class DataRoot
{
    public const string EnvVar = "ULTIMA_DATA";

    /// <summary>Forme normalisée (contient meta/) ou null.</summary>
    public static string? Normalize(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        var full = Path.GetFullPath(dir);
        if (Directory.Exists(Path.Combine(full, "meta"))) return full;
        if (Directory.Exists(Path.Combine(full, "data", "meta"))) return Path.Combine(full, "data");
        return null;
    }

    public static string? Resolve(string? explicitDir, out string source)
    {
        if (explicitDir is not null)
        {
            source = "--data";
            var n = Normalize(explicitDir);
            if (n is null) Diag.Warn($"--data {explicitDir} : aucun meta/ ; ignoré");
            else return n;
        }
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            source = EnvVar;
            var n = Normalize(env);
            if (n is null) Diag.Warn($"{EnvVar}={env} : aucun meta/ ; ignoré");
            else return n;
        }
        var d = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(d); i++)
        {
            var candidate = Path.Combine(d, "data");
            if (Directory.Exists(Path.Combine(candidate, "meta"))) { source = "à côté de l'exécutable"; return candidate; }
            d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
        }
        source = "introuvable";
        return null;
    }

    /// <summary>Résout et fixe <c>ULTIMA_DATA</c> pour ce processus (et ses enfants).</summary>
    public static string? Apply(string? explicitDir)
    {
        var root = Resolve(explicitDir, out var source);
        if (root is null)
        {
            Diag.Warn("données d'ULTIMA introuvables (ULTIMA_DATA) : scores et chemins indisponibles");
            return null;
        }
        Environment.SetEnvironmentVariable(EnvVar, root);
        Diag.Info($"données : {EnvVar}={root} ({source})");
        return root;
    }
}
