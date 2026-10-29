using System.Text.Json;

namespace Ultima.Engine.Scoring.Powers;

/// <summary>
/// Un pouvoir tel que <c>tools/index_powers.py</c> l'a extrait du code du jeu : identité,
/// hooks surchargés et expression de chacun. Les expressions sont dans le mini-langage de
/// <see cref="ExpressionEval"/> (<c>Amount</c> = piles du pouvoir, <c>In</c> = valeur reçue).
/// </summary>
public sealed record PowerEntry(
    string Id,
    string Class,
    string? Base,
    string Source,
    IReadOnlyDictionary<string, string?> Hooks,
    IReadOnlyDictionary<string, string> HookSources,
    IReadOnlyDictionary<string, string> Problems,
    IReadOnlyDictionary<string, string[]> DynamicInputs)
{
    /// <summary>Le pouvoir surcharge-t-il ce hook ? (même si l'expression n'est pas évaluable)</summary>
    public bool Has(string hook) => Hooks.ContainsKey(hook);

    /// <summary>Expression du hook, ou <c>null</c> quand elle n'est pas évaluable / absente.</summary>
    public string? Expression(string hook) =>
        Hooks.TryGetValue(hook, out var expr) ? expr : null;

    /// <summary>« fichier:ligne » de la déclaration du hook (source du code du jeu).</summary>
    public string? SourceOf(string hook) =>
        HookSources.TryGetValue(hook, out var src) ? src : null;

    /// <summary>Raison de non-évaluabilité : <c>call</c>, <c>state</c>, <c>local</c>, <c>dynamicvar</c>…</summary>
    public string? Problem(string hook) =>
        Problems.TryGetValue(hook, out var reason) ? reason : null;

    /// <summary>Modificateurs externes qui peuvent changer la valeur (PaperPhrog, Cruelty…).</summary>
    public IReadOnlyList<string> ModifiersOf(string hook) =>
        DynamicInputs.TryGetValue(hook, out var list) ? list : Array.Empty<string>();

    /// <summary>Hooks dont l'expression est évaluable (les seuls applicables par le moteur).</summary>
    public IEnumerable<string> EvaluableHooks => Hooks.Where(kv => kv.Value != null).Select(kv => kv.Key);
}

/// <summary>
/// Charge <c>data/catalog/powers.json</c> (généré par <c>tools/index_powers.py</c>, qui lit
/// les 260 <c>Models/Powers/*.cs</c> en lecture seule). Aucun pouvoir n'est codé ici : le
/// catalogue est la seule source. Un fichier absent donne <see cref="Empty"/> — le moteur
/// fonctionne alors sans les hooks, jamais avec des valeurs inventées.
/// </summary>
public sealed class PowerCatalog
{
    private readonly Dictionary<string, PowerEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PowerEntry> _byClass = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _warnings = new();

    public int Count => _byId.Count;
    public string? Source { get; }
    public string? GeneratedBy { get; }
    public IReadOnlyList<string> Warnings => _warnings;

    private PowerCatalog(string? source, string? generatedBy)
    {
        Source = source;
        GeneratedBy = generatedBy;
    }

    public static PowerCatalog Empty { get; } = new(null, null);

    /// <summary>Catalogue par défaut (chargé une fois depuis data/catalog).</summary>
    public static PowerCatalog Default { get; } = Load();

    /// <summary>Tous les pouvoirs chargés (lecture seule).</summary>
    public IReadOnlyCollection<PowerEntry> Powers => _byId.Values;

    /// <summary>
    /// Pouvoir par id snake_case (<c>hard_to_kill</c>) ou par nom de classe (<c>HardToKillPower</c>,
    /// <c>HardToKill</c>). <c>null</c> si inconnu — jamais d'entrée fabriquée.
    /// </summary>
    public PowerEntry? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var key = id.Trim();
        if (key.StartsWith("POWER.", StringComparison.OrdinalIgnoreCase)) key = key[6..];
        if (_byId.TryGetValue(key, out var entry)) return entry;
        if (_byClass.TryGetValue(key, out entry)) return entry;
        if (_byClass.TryGetValue(key + "Power", out entry)) return entry;
        return null;
    }

    /// <summary>Expression du hook pour ce pouvoir, ou <c>null</c> si absente / non évaluable.</summary>
    public string? Expression(string? id, string hook) => Get(id)?.Expression(hook);

    public static PowerCatalog Load(string? catalogDir = null)
    {
        var dir = ScoringData.ResolveCatalogDir(catalogDir);
        var path = Path.Combine(dir, "powers.json");
        if (!File.Exists(path)) return Empty;

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return Empty;

        var catalog = new PowerCatalog(
            root.TryGetProperty("source", out var s) ? s.GetString() : null,
            root.TryGetProperty("generated_by", out var g) ? g.GetString() : null);

        if (!root.TryGetProperty("powers", out var array) || array.ValueKind != JsonValueKind.Array)
            return catalog;

        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var id = el.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                catalog._warnings.Add("pouvoir sans id ignoré");
                continue;
            }

            var hooks = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (el.TryGetProperty("hooks", out var h) && h.ValueKind == JsonValueKind.Object)
            {
                foreach (var hook in h.EnumerateObject())
                    hooks[hook.Name] = hook.Value.ValueKind == JsonValueKind.String
                        ? hook.Value.GetString()
                        : null;
            }

            var entry = new PowerEntry(
                id!,
                Str(el, "class") ?? id!,
                Str(el, "base"),
                Str(el, "source") ?? "",
                hooks,
                ReadMap(el, "hook_sources"),
                ReadMap(el, "unresolved"),
                ReadLists(el, "dynamic_inputs"));

            catalog._byId[entry.Id] = entry;
            catalog._byClass[entry.Class] = entry;
        }

        return catalog;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IReadOnlyDictionary<string, string> ReadMap(JsonElement el, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!el.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object) return map;
        foreach (var prop in obj.EnumerateObject())
            if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is { Length: > 0 } value)
                map[prop.Name] = value;
        return map;
    }

    private static IReadOnlyDictionary<string, string[]> ReadLists(JsonElement el, string name)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (!el.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object) return map;
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            var items = prop.Value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!)
                .Where(v => v.Length > 0)
                .ToArray();
            if (items.Length > 0) map[prop.Name] = items;
        }
        return map;
    }
}
