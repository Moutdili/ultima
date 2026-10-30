using System.Globalization;
using System.Text.Json;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Status;

/// <summary>Règle d'un pouvoir qui modifie les dégâts ou l'armure (docs/47).</summary>
public sealed record StatusRule(
    string Id,
    string? Hook,
    double Value,
    bool PerStack,
    string? Direction,
    bool Modelled,
    string? Source,
    string? Reason)
{
    /// <summary>Direction : <c>dealt_mult</c>, <c>dealt_add</c>, <c>taken_mult</c>, <c>taken_cap</c>,
    /// <c>block_mult</c>, <c>block_add</c>.</summary>
    public bool IsDealt => Direction is "dealt_mult" or "dealt_add";
}

/// <summary>Effet combiné d'un ensemble de statuts sur un coup.</summary>
public sealed record StatusEffect(
    double DealtMultiplier,
    int DealtFlat,
    double TakenMultiplier,
    int TakenFlat,
    int? TakenCap,
    double BlockMultiplier,
    int BlockFlat,
    IReadOnlyList<string> Notes)
{
    public static StatusEffect Neutral { get; } =
        new(1, 0, 1, 0, null, 1, 0, Array.Empty<string>());
}

/// <summary>
/// Règles de statuts chargées depuis <c>data/catalog/status_rules.json</c>. Les valeurs viennent
/// des hooks extraits du code du jeu ; un pouvoir dont la direction n'est pas qualifiée est
/// <b>ignoré en le disant</b> (note), jamais appliqué au hasard.
/// </summary>
public sealed class StatusRules
{
    public const string FileName = "status_rules.json";
    public const string Schema = "ultima.status-rules/1";

    private readonly Dictionary<string, StatusRule> _byId;

    private StatusRules(IReadOnlyList<StatusRule> rules, bool loaded, string? problem)
    {
        Rules = rules;
        IsLoaded = loaded;
        Problem = problem;
        _byId = rules.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<StatusRule> Rules { get; }
    public bool IsLoaded { get; }
    public string? Problem { get; }
    public int ModelledCount => Rules.Count(r => r.Modelled);

    public static StatusRules Empty { get; } = new(Array.Empty<StatusRule>(), false, "aucune règle chargée");

    public StatusRule? Get(string? id) =>
        id is not null && _byId.TryGetValue(id, out var rule) ? rule : null;

    /// <summary>Statuts dont la direction n'est pas qualifiée : listés, jamais appliqués.</summary>
    public IReadOnlyList<StatusRule> Unqualified =>
        Rules.Where(r => !r.Modelled).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();

    public static StatusRules Load(string? catalogDir = null)
    {
        string dir;
        try
        {
            dir = ScoringData.ResolveCatalogDir(catalogDir);
        }
        catch (IOException)
        {
            return Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return Empty;
        }

        var path = Path.Combine(dir, FileName);
        if (!File.Exists(path))
            return new StatusRules(Array.Empty<StatusRule>(), false, $"fichier absent : {path}");

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (!doc.RootElement.TryGetProperty("rules", out var array) || array.ValueKind != JsonValueKind.Array)
                return new StatusRules(Array.Empty<StatusRule>(), false, "clé « rules » absente");

            var rules = new List<StatusRule>(array.GetArrayLength());
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var id = Str(element, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                var raw = Double(element, "value");
                var perStack = element.TryGetProperty("per_stack", out var ps) && ps.ValueKind == JsonValueKind.True;
                rules.Add(new StatusRule(
                    id!,
                    Str(element, "hook"),
                    raw ?? 1,
                    perStack,
                    Str(element, "direction"),
                    element.TryGetProperty("modelled", out var m) && m.ValueKind == JsonValueKind.True,
                    Str(element, "source"),
                    Str(element, "reason")));
            }

            return new StatusRules(rules, true, null);
        }
        catch (JsonException ex)
        {
            return new StatusRules(Array.Empty<StatusRule>(), false, $"JSON invalide : {ex.Message}");
        }
        catch (IOException ex)
        {
            return new StatusRules(Array.Empty<StatusRule>(), false, $"lecture impossible : {ex.Message}");
        }
    }

    public static StatusRules Default { get; } = Load();

    /// <summary>
    /// Effet combiné d'un ensemble de statuts (id → stacks). Les statuts non modélisés sont
    /// signalés dans <see cref="StatusEffect.Notes"/>, sans influencer le calcul.
    /// </summary>
    public StatusEffect Evaluate(IReadOnlyDictionary<string, int>? statuses)
    {
        if (statuses is null || statuses.Count == 0) return StatusEffect.Neutral;

        var dealt = 1.0;
        var taken = 1.0;
        var block = 1.0;
        var dealtFlat = 0;
        var takenFlat = 0;
        var blockFlat = 0;
        int? cap = null;
        List<string>? notes = null;

        foreach (var (id, stacks) in statuses)
        {
            if (stacks == 0) continue;
            var rule = Get(id);
            if (rule is null)
            {
                (notes ??= new List<string>()).Add($"{id} : aucune règle (pouvoir absent du catalogue)");
                continue;
            }

            if (!rule.Modelled || rule.Direction is null)
            {
                (notes ??= new List<string>()).Add(
                    $"{id} : non modélisé" + (rule.Reason is { Length: > 0 } why ? $" ({why})" : ""));
                continue;
            }

            var value = rule.PerStack ? rule.Value * stacks : rule.Value;
            switch (rule.Direction)
            {
                case "dealt_mult":
                    dealt *= value;
                    break;
                case "taken_mult":
                    taken *= value;
                    break;
                case "taken_add":
                    takenFlat += (int)Math.Round(rule.PerStack ? rule.Value * stacks : rule.Value);
                    break;
                case "block_mult":
                    block *= value;
                    break;
                case "dealt_add":
                    dealtFlat += (int)Math.Round(rule.PerStack ? rule.Value * stacks : rule.Value);
                    break;
                case "block_add":
                    blockFlat += (int)Math.Round(rule.PerStack ? rule.Value * stacks : rule.Value);
                    break;
                case "taken_cap":
                    var limit = (int)Math.Round(value);
                    cap = cap is null ? limit : Math.Min(cap.Value, limit);
                    break;
                default:
                    (notes ??= new List<string>()).Add($"{id} : direction inconnue « {rule.Direction} »");
                    break;
            }
        }

        return new StatusEffect(dealt, dealtFlat, taken, takenFlat, cap, block, blockFlat,
            notes ?? (IReadOnlyList<string>)Array.Empty<string>());
    }

    /// <summary>
    /// Dégâts réellement subis par une cible pour un coup de base donné, avec les statuts de
    /// l'attaquant et de la cible. Ordre : additif, puis multiplicatifs, puis plafond.
    /// </summary>
    public int Incoming(int baseDamage, IReadOnlyDictionary<string, int>? attackerStatuses,
        IReadOnlyDictionary<string, int>? defenderStatuses, out StatusEffect attacker, out StatusEffect defender)
    {
        attacker = Evaluate(attackerStatuses);
        defender = Evaluate(defenderStatuses);

        if (baseDamage <= 0) return 0;

        // Ordre du pipeline : plats (attaquant + cible), puis multiplicatifs, puis plafond (docs/47).
        var damage = (baseDamage + attacker.DealtFlat + defender.TakenFlat)
                     * attacker.DealtMultiplier * defender.TakenMultiplier;
        var rounded = (int)Math.Round(damage, MidpointRounding.AwayFromZero);
        if (defender.TakenCap is { } cap) rounded = Math.Min(rounded, cap);
        return Math.Max(0, rounded);
    }

    public int Incoming(int baseDamage, IReadOnlyDictionary<string, int>? attackerStatuses,
        IReadOnlyDictionary<string, int>? defenderStatuses) =>
        Incoming(baseDamage, attackerStatuses, defenderStatuses, out _, out _);

    /// <summary>Armure gagnée par un bloc de base, avec les statuts du porteur.</summary>
    public int BlockGained(int baseBlock, IReadOnlyDictionary<string, int>? statuses)
    {
        var effect = Evaluate(statuses);
        if (baseBlock <= 0) return 0;
        return Math.Max(0, (int)Math.Round((baseBlock + effect.BlockFlat) * effect.BlockMultiplier,
            MidpointRounding.AwayFromZero));
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Double(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;
        return value.TryGetDouble(out var parsed) ? parsed : null;
    }

    /// <summary>Format invariant, pour les libellés de termes.</summary>
    public static string Format(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
