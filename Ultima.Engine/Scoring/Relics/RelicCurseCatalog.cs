using System.Text.Json;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Relics;

/// <summary>
/// Relique → malédiction qu'elle impose. <see cref="Curse"/> null signifie « malédictions
/// aléatoires, nombre non mesuré » : la relique est alors signalée, jamais chiffrée.
/// <see cref="Evidence"/> porte la preuve (co-occurrence corpus ou description du jeu) pour que
/// l'UI puisse répondre à « d'où sort ce chiffre ? ».
/// </summary>
public sealed record RelicCurseEntry(
    string Relic,
    string? Curse,
    int Count,
    string? Evidence)
{
    /// <summary>Malédiction dont la carte n'est pas fixe : aucun prix possible sans inventer.</summary>
    public bool IsRandom => string.IsNullOrWhiteSpace(Curse);
}

/// <summary>
/// Charge <c>data/catalog/relic_curses.json</c> — les reliques qui imposent une malédiction,
/// avec la preuve de la correspondance.
///
/// Le fichier est volontairement minuscule : une seule correspondance est prouvée
/// (Cloche d'appel → Curse of the Bell, 1 623/1 623 runs). Les reliques à malédictions
/// aléatoires sont listées à part (<c>rejected</c>) avec leur raison, pour qu'on ne les
/// « re-découvre » pas comme si elles étaient chiffrables. Fichier absent ou illisible :
/// catalogue vide + <see cref="Problem"/>, jamais d'exception.
/// </summary>
public sealed class RelicCurseCatalog
{
    public const string FileName = "relic_curses.json";
    public const string Schema = "ultima.relic_curses/1";

    private readonly Dictionary<string, RelicCurseEntry> _byRelic;

    private RelicCurseCatalog(Dictionary<string, RelicCurseEntry> byRelic) => _byRelic = byRelic;

    public static RelicCurseCatalog Empty { get; } =
        new(new Dictionary<string, RelicCurseEntry>(StringComparer.OrdinalIgnoreCase));

    public int Count => _byRelic.Values.Count(e => !e.IsRandom);

    /// <summary>Pourquoi le catalogue est vide/incomplet ; null quand tout a été lu.</summary>
    public string? Problem { get; private init; }

    /// <summary>Correspondances chiffrées (malédiction identifiée).</summary>
    public IReadOnlyList<RelicCurseEntry> Entries =>
        _byRelic.Values.Where(e => !e.IsRandom).OrderBy(e => e.Relic, StringComparer.Ordinal).ToArray();

    /// <summary>Reliques examinées et écartées : leur imposition existe mais n'est pas chiffrable.</summary>
    public IReadOnlyList<string> Rejected { get; private init; } = Array.Empty<string>();

    public RelicCurseEntry? Get(string? relicId)
    {
        if (string.IsNullOrWhiteSpace(relicId)) return null;
        return _byRelic.TryGetValue(Ids.Bare(relicId), out var e) ? e : null;
    }

    public static RelicCurseCatalog Load(string? catalogDir = null)
    {
        var dir = ScoringData.ResolveCatalogDir(catalogDir);
        var path = Path.Combine(dir, FileName);
        if (!File.Exists(path)) return Fail($"{FileName} absent de {dir}");

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return Parse(doc.RootElement);
        }
        catch (JsonException ex)
        {
            return Fail($"{FileName} illisible : {ex.Message}");
        }
        catch (IOException ex)
        {
            return Fail($"{FileName} inaccessible : {ex.Message}");
        }
    }

    private static RelicCurseCatalog Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return Fail($"{FileName} : racine JSON inattendue ({root.ValueKind})");

        var schema = Str(root, "schema");
        var problem = schema is null || schema == Schema
            ? null
            : $"{FileName} : schéma « {schema} » au lieu de « {Schema} »";

        var byRelic = new Dictionary<string, RelicCurseEntry>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in entries.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var relic = Str(el, "relic");
                if (string.IsNullOrWhiteSpace(relic)) continue;

                var curse = Str(el, "curse");
                int count = el.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number &&
                            c.TryGetInt32(out var n)
                    ? n
                    : 1;

                byRelic[Ids.Bare(relic)] = new RelicCurseEntry(
                    Ids.Bare(relic), string.IsNullOrWhiteSpace(curse) ? null : Ids.Bare(curse),
                    Math.Max(1, count), Str(el, "evidence"));
            }
        }

        var rejected = new List<string>();
        if (root.TryGetProperty("rejected", out var rej) && rej.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in rej.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var relic = Str(el, "relic");
                if (!string.IsNullOrWhiteSpace(relic)) rejected.Add(Ids.Bare(relic));

                // Une relique « rejetée » ne doit pas être chiffrée par mégarde : elle est
                // seulement signalée, jamais prise pour une malédiction aléatoire chiffrable.
                var reason = Str(el, "reason") ?? "correspondance non prouvée";
                byRelic[Ids.Bare(relic)] = new RelicCurseEntry(Ids.Bare(relic), null, 0, reason);
            }
        }

        return new RelicCurseCatalog(byRelic)
        {
            Rejected = rejected,
            Problem = problem,
        };
    }

    private static RelicCurseCatalog Fail(string problem) =>
        new(new Dictionary<string, RelicCurseEntry>(StringComparer.OrdinalIgnoreCase)) { Problem = problem };

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
