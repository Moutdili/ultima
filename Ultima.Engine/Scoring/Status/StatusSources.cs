using System.Text.Json;

namespace Ultima.Engine.Scoring.Status;

/// <summary>Une application de pouvoir trouvée dans le code (carte ou monstre).</summary>
public sealed record StatusApplication(
    string? Power,
    string Target,
    string? Amount,
    string? Method,
    int Line,
    bool Dynamic)
{
    /// <summary>Vrai quand la cible du code est un ennemi (donc le joueur, côté monstre).</summary>
    public bool OnEnemy => Target == "enemy";
    public bool OnSelf => Target == "self";
}

/// <summary>Carte ou monstre qui applique des pouvoirs, avec ses lignes de code.</summary>
public sealed record StatusSourceEntry(
    string Id,
    string Class,
    string File,
    IReadOnlyList<StatusApplication> Applies)
{
    public IReadOnlyList<StatusApplication> DebuffsOnPlayer =>
        Applies.Where(a => a.OnEnemy).ToArray();

    public IReadOnlyList<StatusApplication> SelfBuffs => Applies.Where(a => a.OnSelf).ToArray();
}

/// <summary>
/// Qui applique quoi : chargé depuis <c>data/catalog/status_sources.json</c> (index du code du jeu).
/// Sert à répondre « cette rencontre va-t-elle me poser Vulnerable avant de frapper ? » sans
/// deviner la rotation — on dit quelles applications existent, avec leur ligne.
/// </summary>
public sealed class StatusSources
{
    public const string FileName = "status_sources.json";
    public const string Schema = "ultima.status-sources/1";

    private readonly Dictionary<string, StatusSourceEntry> _cards;
    private readonly Dictionary<string, StatusSourceEntry> _monsters;

    private StatusSources(
        IReadOnlyList<StatusSourceEntry> cards,
        IReadOnlyList<StatusSourceEntry> monsters,
        bool loaded,
        string? problem)
    {
        Cards = cards;
        Monsters = monsters;
        IsLoaded = loaded;
        Problem = problem;
        _cards = Index(cards);
        _monsters = Index(monsters);
    }

    public IReadOnlyList<StatusSourceEntry> Cards { get; }
    public IReadOnlyList<StatusSourceEntry> Monsters { get; }
    public bool IsLoaded { get; }
    public string? Problem { get; }
    public int ApplicationCount => Cards.Sum(c => c.Applies.Count) + Monsters.Sum(m => m.Applies.Count);

    public static StatusSources Empty { get; } = new(Array.Empty<StatusSourceEntry>(),
        Array.Empty<StatusSourceEntry>(), false, "aucune source chargée");

    public StatusSourceEntry? Card(string? id) => Lookup(_cards, id);
    public StatusSourceEntry? Monster(string? id) => Lookup(_monsters, id);

    private static StatusSourceEntry? Lookup(Dictionary<string, StatusSourceEntry> table, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var key = Bare(id!);
        return table.TryGetValue(key, out var entry) ? entry : null;
    }

    private static Dictionary<string, StatusSourceEntry> Index(IEnumerable<StatusSourceEntry> entries) =>
        entries.GroupBy(e => Bare(e.Id).ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private static string Bare(string id)
    {
        var text = id.Trim();
        return text.Contains('.') ? text[(text.LastIndexOf('.') + 1)..] : text;
    }

    public static StatusSources Load(string? catalogDir = null)
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
            return new StatusSources(Array.Empty<StatusSourceEntry>(), Array.Empty<StatusSourceEntry>(),
                false, $"fichier absent : {path}");

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new StatusSources(Array.Empty<StatusSourceEntry>(), Array.Empty<StatusSourceEntry>(),
                    false, "racine JSON invalide");

            var cards = ReadFamily(doc.RootElement, "cards");
            var monsters = ReadFamily(doc.RootElement, "monsters");
            return new StatusSources(cards, monsters, true, null);
        }
        catch (JsonException ex)
        {
            return new StatusSources(Array.Empty<StatusSourceEntry>(), Array.Empty<StatusSourceEntry>(),
                false, $"JSON invalide : {ex.Message}");
        }
        catch (IOException ex)
        {
            return new StatusSources(Array.Empty<StatusSourceEntry>(), Array.Empty<StatusSourceEntry>(),
                false, $"lecture impossible : {ex.Message}");
        }
    }

    public static StatusSources Default { get; } = Load();

    private static List<StatusSourceEntry> ReadFamily(JsonElement root, string family)
    {
        var result = new List<StatusSourceEntry>();
        if (!root.TryGetProperty(family, out var array) || array.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var id = Str(element, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var applies = new List<StatusApplication>();
            // Format compact : "ap": [[pouvoir, cible, montant, méthode, ligne], ...]
            if (element.TryGetProperty("ap", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 5) continue;
                    applies.Add(new StatusApplication(
                        item[0].ValueKind == JsonValueKind.String ? item[0].GetString() : null,
                        item[1].ValueKind == JsonValueKind.String ? item[1].GetString() ?? "unknown" : "unknown",
                        item[2].ValueKind == JsonValueKind.String ? item[2].GetString() : null,
                        item[3].ValueKind == JsonValueKind.String ? item[3].GetString() : null,
                        item[4].ValueKind == JsonValueKind.Number && item[4].TryGetInt32(out var line) ? line : 0,
                        false));
                }
            }

            result.Add(new StatusSourceEntry(
                id!, Str(element, "class") ?? id!, Str(element, "file") ?? "", applies));
        }

        return result;
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
}
