using System.Text.Json;
using System.Text.Json.Nodes;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Custom;

/// <summary>
/// Manifeste d'un plugin (<c>~/.local/share/ultima/plugins/&lt;id&gt;/plugin.json</c>, docs/44 §7), validé par
/// <c>schemas/plugin.schema.json</c>. Un plugin d'une autre API est refusé, pas deviné.
/// </summary>
public sealed record PluginManifest(
    string Id, string Label, IReadOnlyList<string> Exec, IReadOnlyList<string>? Screens,
    int TimeoutMs, int MinIntervalMs, string Dir)
{
    public const int DefaultTimeoutMs = 200;
    public const int DefaultMinIntervalMs = 1000;
    public const int MaxTimeoutMs = 500;
    public const int MinIntervalFloorMs = 250;
    public const string ManifestFile = "plugin.json";

    /// <summary>Charge et valide un dossier de plugin. null + <paramref name="problem"/> si refusé.</summary>
    public static PluginManifest? TryLoad(string dir, JsonSchema? schema, out string? problem)
    {
        problem = null;
        var file = Path.Combine(dir, ManifestFile);
        if (!File.Exists(file)) { problem = $"plugin.json absent ({dir})"; return null; }
        string json;
        try { json = File.ReadAllText(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"plugin.json illisible : {e.Message}";
            return null;
        }
        if (schema is null) { problem = "schémas du contrat indisponibles (schemas/plugin.schema.json)"; return null; }
        var errors = schema.ValidateText(json, out var node);
        if (errors.Count > 0) { problem = "schéma : " + string.Join(" ; ", errors); return null; }
        if (node is not JsonObject o) { problem = "JSON invalide : objet attendu"; return null; }

        var id = Str(o, "id")!;
        var label = Str(o, "label") ?? id;
        var exec = (o["exec"] as JsonArray)!.Select(v => v!.GetValue<string>()).ToArray();
        var screens = (o["screens"] as JsonArray)?.Select(v => v!.GetValue<string>()).ToArray();
        int timeout = Int(o, "timeoutMs") ?? DefaultTimeoutMs;
        int interval = Int(o, "minIntervalMs") ?? DefaultMinIntervalMs;
        timeout = Math.Clamp(timeout, 10, MaxTimeoutMs);
        interval = Math.Clamp(interval, MinIntervalFloorMs, 60_000);
        if (!id.StartsWith(CustomIds.Prefix, StringComparison.Ordinal))
        {
            problem = $"id « {id} » : doit commencer par « {CustomIds.Prefix} »";
            return null;
        }
        return new PluginManifest(id, label, exec, screens, timeout, interval, dir);
    }

    /// <summary>Chemin de l'exécutable : absolu, ou relatif au dossier du plugin (jamais le cwd du daemon).</summary>
    public string ResolveProgram() => Resolve(Exec[0], Dir);

    public IReadOnlyList<string> Arguments => Exec.Skip(1).ToArray();

    private static string Resolve(string program, string dir) =>
        Path.IsPathRooted(program) ? program
        : program.Contains('/') ? Path.GetFullPath(Path.Combine(dir, program))
        : program; // nom simple : recherche dans PATH

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    internal static string Short(string s) => s.Length <= 160 ? s : s[..160] + "…";

    internal static JsonSerializerOptions JsonOptions => new(JsonSerializerDefaults.Web);
}
