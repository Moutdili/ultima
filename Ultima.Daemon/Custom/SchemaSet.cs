namespace Ultima.Daemon.Custom;

/// <summary>
/// Schémas du contrat v1.4 utilisés à l'exécution. Ils sont copiés à côté de l'exécutable depuis
/// <c>schemas/</c> (voir <c>Ultima.Daemon.csproj</c>) : le daemon valide avec le fichier du dépôt, jamais
/// avec une copie écrite en dur. Ordre de recherche : <c>$ULTIMA_SCHEMAS</c>, puis <c>&lt;exe&gt;/schemas</c>,
/// puis remontée depuis l'exécutable jusqu'à un dossier <c>schemas/</c> du dépôt.
/// </summary>
public sealed class SchemaSet
{
    public const string EnvVar = "ULTIMA_SCHEMAS";

    private SchemaSet(JsonSchema? module, JsonSchema? plugin, JsonSchema? pluginOutput, string? error)
    {
        Module = module;
        Plugin = plugin;
        PluginOutput = pluginOutput;
        Error = error;
    }

    public JsonSchema? Module { get; }
    public JsonSchema? Plugin { get; }
    public JsonSchema? PluginOutput { get; }

    /// <summary>Raison lisible quand un schéma manque (les modules/plugins sont alors refusés, jamais devinés).</summary>
    public string? Error { get; }

    public static SchemaSet Load(string? dir = null)
    {
        var root = Find(dir);
        if (root is null) return new SchemaSet(null, null, null, "schémas du contrat introuvables (schemas/*.schema.json)");
        string Path0(string name) => System.IO.Path.Combine(root, name + ".schema.json");
        var problems = new List<string>();
        JsonSchema? Load(string name)
        {
            var s = JsonSchema.TryLoad(Path0(name), out var err);
            if (err is not null) problems.Add(err);
            return s;
        }
        var set = new SchemaSet(Load("module"), Load("plugin"), Load("plugin-output"),
            problems.Count == 0 ? null : string.Join(" ; ", problems));
        if (problems.Count == 0) Diag.Debug($"schémas : {root}");
        return set;
    }

    /// <summary>Dossier contenant <c>module.schema.json</c>, ou null.</summary>
    public static string? Find(string? dir = null)
    {
        bool Ok(string? d) => d is not null && File.Exists(System.IO.Path.Combine(d, "module.schema.json"));

        if (Ok(dir)) return dir;
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (Ok(env)) return env;
        var same = System.IO.Path.Combine(AppContext.BaseDirectory, "schemas");
        if (Ok(same)) return same;
        var d = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(d); i++)
        {
            var candidate = System.IO.Path.Combine(d, "schemas");
            if (Ok(candidate)) return candidate;
            d = System.IO.Path.GetDirectoryName(d.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        }
        return null;
    }
}
