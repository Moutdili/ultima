using System.Text.Json;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Curses;

/// <summary>
/// Une ligne de <c>data/catalog/curse_impact.json</c> : impact <b>mesuré</b> d'une malédiction ou
/// d'un statut sur le taux de victoire (docs/35-curses-statuts.md).
/// <see cref="DeltaPp"/> est un écart en points de pourcentage vs la base des runs entrées en
/// acte 3 ; <see cref="Toxicity"/> le même écart ramené sur 0..100 (50 = neutre, 100 = −10 pp,
/// 0 = +10 pp, formule de <c>tools/index_curse_impact.py</c>).
/// </summary>
public sealed record CurseImpact(
    string Id,
    string Type,
    string? Description,
    int N,
    double WinRate,
    double DeltaPp,
    bool Reliable,
    int Toxicity)
{
    /// <summary>Statut de combat (Brûlure, Plaie…) : temporaire, jamais dans le deck final.</summary>
    public bool IsStatus => string.Equals(Type, "status", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Charge <c>data/catalog/curse_impact.json</c> (commité, 63 634 runs entrées en acte 3).
///
/// Aucun id n'est codé en dur : la correspondance se fait sur une forme canonique
/// (sans <c>CARD.</c>, sans séparateur, en majuscules), donc <c>CARD.POOR_SLEEP</c>,
/// <c>PoorSleep</c> et <c>POOR_SLEEP</c> désignent la même entrée.
///
/// Un fichier absent ou illisible ne lève pas : le catalogue est vide et <see cref="Problem"/>
/// dit pourquoi, pour que l'appelant produise un terme explicite (« aucune donnée mesurée »)
/// au lieu d'un zéro silencieux.
/// </summary>
public sealed class CurseCatalog
{
    /// <summary>Nom du fichier dans <c>data/catalog</c>.</summary>
    public const string FileName = "curse_impact.json";

    /// <summary>Schéma attendu ; un autre schéma est signalé dans <see cref="Problem"/>.</summary>
    public const string Schema = "ultima.curse_impact/1";

    private readonly Dictionary<string, CurseImpact> _byKey;

    private CurseCatalog(Dictionary<string, CurseImpact> byKey) => _byKey = byKey;

    /// <summary>Catalogue vide (fichier absent) : toute malédiction est alors « non mesurée ».</summary>
    public static CurseCatalog Empty { get; } =
        new(new Dictionary<string, CurseImpact>(StringComparer.Ordinal));

    public int Count => _byKey.Count;

    /// <summary>Nombre de runs entrées en acte 3 ayant servi de base (0 si fichier absent).</summary>
    public int SampleRuns { get; private init; }

    /// <summary>Taux de victoire de référence de ces runs, en % (0 si fichier absent).</summary>
    public double BaselineWinRate { get; private init; }

    /// <summary>Seuil d'échantillon en dessous duquel une entrée n'est pas classable.</summary>
    public int MinSample { get; private init; } = 300;

    /// <summary>Mise en garde méthodologique du fichier (deltas positifs = bundle, pas la carte).</summary>
    public string? Caveat { get; private init; }

    /// <summary>Pourquoi le catalogue est vide/incomplet ; null quand tout a été lu.</summary>
    public string? Problem { get; private init; }

    /// <summary>Méthode de mesure, telle que publiée dans le fichier.</summary>
    public string? Method { get; private init; }

    public bool HasData => Count > 0;

    /// <summary>Entrées triées par toxicité décroissante (lecture humaine).</summary>
    public IReadOnlyList<CurseImpact> Entries =>
        _byKey.Values.OrderByDescending(e => e.Toxicity).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();

    /// <summary>« CARD.POOR_SLEEP », « PoorSleep » ou « POOR_SLEEP » → entrée mesurée, sinon null.</summary>
    public CurseImpact? Get(string? cardId)
    {
        var key = Key(cardId);
        return key.Length > 0 && _byKey.TryGetValue(key, out var e) ? e : null;
    }

    /// <summary>
    /// Forme canonique d'un id : sans préfixe <c>CARD.</c>/<c>RELIC.</c>, sans séparateur,
    /// en majuscules. « CARD.POOR_SLEEP » → « POORSLEEP ».
    /// </summary>
    public static string Key(string? cardId)
    {
        var bare = Ids.Bare(cardId);
        if (bare.Length == 0) return string.Empty;

        Span<char> buffer = stackalloc char[bare.Length];
        int n = 0;
        foreach (var c in bare)
            if (char.IsLetterOrDigit(c)) buffer[n++] = char.ToUpperInvariant(c);
        return new string(buffer[..n]);
    }

    /// <summary>
    /// Charge le catalogue. <paramref name="catalogDir"/> null ⇒ dossier résolu comme le reste du
    /// moteur (<see cref="ScoringData.ResolveCatalogDir(string?)"/>, donc <c>ULTIMA_DATA</c> puis
    /// remontée depuis l'assembly).
    /// </summary>
    public static CurseCatalog Load(string? catalogDir = null)
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

    /// <summary>Catalogue vide portant la raison : jamais d'exception, jamais de silence.</summary>
    private static CurseCatalog Fail(string problem) => new(new Dictionary<string, CurseImpact>(StringComparer.Ordinal))
    {
        Problem = problem,
    };

    private static CurseCatalog Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return Fail($"{FileName} : racine JSON inattendue ({root.ValueKind})");

        var schema = Str(root, "schema");
        var problem = schema is null || schema == Schema
            ? null
            : $"{FileName} : schéma « {schema} » au lieu de « {Schema} »";

        int minSample = Int(root, "min_sample") ?? 300;
        var entries = new Dictionary<string, CurseImpact>(StringComparer.Ordinal);

        if (root.TryGetProperty("entries", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var id = Str(el, "id");
                var key = Key(id);
                if (key.Length == 0) continue;

                int n = Int(el, "n") ?? 0;
                entries[key] = new CurseImpact(
                    Ids.Bare(id),
                    (Str(el, "type") ?? "").ToLowerInvariant(),
                    Str(el, "description"),
                    n,
                    Num(el, "win_rate") ?? 0,
                    Num(el, "delta_pp") ?? 0,
                    Bool(el, "reliable") ?? n >= minSample,
                    Int(el, "toxicity") ?? 50);
            }
        }

        return new CurseCatalog(entries)
        {
            SampleRuns = Int(root, "sample_runs") ?? 0,
            BaselineWinRate = Num(root, "baseline_win_rate") ?? 0,
            MinSample = minSample,
            Caveat = Str(root, "caveat"),
            Method = Str(root, "method"),
            Problem = problem,
        };
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;

    private static double? Num(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
            ? d
            : null;

    private static bool? Bool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
