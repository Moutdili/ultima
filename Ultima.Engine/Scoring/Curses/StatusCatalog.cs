using System.Text.Json;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Curses;

/// <summary>
/// Un statut classé par son <b>effet décrit</b> (docs/35, section statuts). Les statuts sont
/// temporaires : le corpus ne peut pas les classer (n = 0 à 3), mais un statut qui inflige
/// 5 dégâts par tour n'est pas une simple carte morte.
/// </summary>
/// <param name="Family">Famille grossière : unplayable, damage_when_drawn, hp_loss,
/// energy_loss, draw_block, exhaust_on_draw, other.</param>
/// <param name="EffectFamily">Famille fine de la couche effet : play_limit, end_turn_damage,
/// draw_reduction, energy_loss, hp_loss, escape, self_removing, unplayable_only, unclassified.</param>
/// <param name="Value">Magnitude chiffrée de l'effet (2 dégâts, 6 PV, 1 énergie…), null si le
/// texte ou les données de la carte ne la donnent pas.</param>
/// <param name="Severity">Sévérité 0..100 de la famille ; null = non classé.</param>
/// <param name="Band">severe | moderate | benign | unclassified.</param>
/// <param name="Penalty">Points de score (négatif) associés à la bande.</param>
public sealed record StatusEffect(
    string Id,
    string? DescriptionFr,
    string Family,
    string EffectFamily,
    int? Value,
    int? Severity,
    string Band,
    int Penalty,
    string Source)
{
    public bool IsClassified => Band != StatusCatalog.UnclassifiedBand;
}

/// <summary>
/// Charge <c>data/catalog/status_effects.json</c> : les 17 statuts du jeu, classés par effet
/// décrit (famille grossière + famille fine), avec la valeur chiffrée quand elle existe et la
/// bande de pénalité. Généré par <c>tools/index_status_effects.py</c>.
///
/// Le fichier est la source du terme de score des statuts ; <see cref="StatusCatalog.Empty"/>
/// (fichier absent) fait retomber <see cref="CurseScorer"/> sur le forfait modéré explicite,
/// jamais sur zéro.
/// </summary>
public sealed class StatusCatalog
{
    public const string FileName = "status_effects.json";
    public const string Schema = "ultima.status_effects/1";

    public const string SevereBand = "severe";
    public const string ModerateBand = "moderate";
    public const string BenignBand = "benign";
    public const string UnclassifiedBand = "unclassified";

    private readonly Dictionary<string, StatusEffect> _byKey;

    private StatusCatalog(Dictionary<string, StatusEffect> byKey) => _byKey = byKey;

    public static StatusCatalog Empty { get; } =
        new(new Dictionary<string, StatusEffect>(StringComparer.Ordinal));

    private static readonly Lazy<StatusCatalog> LazyDefault =
        new(() => Load(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static StatusCatalog Default => LazyDefault.Value;

    public int Count => _byKey.Count;

    /// <summary>Raison d'un chargement vide ou partiel ; null quand tout a été lu.</summary>
    public string? Problem { get; private init; }

    public bool HasData => Count > 0;

    public IReadOnlyList<StatusEffect> Entries =>
        _byKey.Values.OrderByDescending(e => e.Severity ?? -1)
            .ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();

    /// <summary>« CARD.BURN », « Burn » ou « BURN » → entrée, sinon null.</summary>
    public StatusEffect? Get(string? cardId)
    {
        var key = CurseCatalog.Key(cardId);
        return key.Length > 0 && _byKey.TryGetValue(key, out var e) ? e : null;
    }

    public static StatusCatalog Load(string? catalogDir = null)
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

    private static StatusCatalog Fail(string problem) =>
        new(new Dictionary<string, StatusEffect>(StringComparer.Ordinal)) { Problem = problem };

    private static StatusCatalog Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return Fail($"{FileName} : racine JSON inattendue ({root.ValueKind})");

        var schema = Str(root, "schema");
        var problem = schema is null || schema == Schema
            ? null
            : $"{FileName} : schéma « {schema} » au lieu de « {Schema} »";

        var entries = new Dictionary<string, StatusEffect>(StringComparer.Ordinal);
        if (root.TryGetProperty("entries", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var id = Str(el, "id");
                var key = CurseCatalog.Key(id);
                if (key.Length == 0) continue;

                entries[key] = new StatusEffect(
                    Ids.Bare(id),
                    Str(el, "description_fr"),
                    Str(el, "family") ?? "other",
                    Str(el, "effect_family") ?? "unclassified",
                    Int(el, "value"),
                    Int(el, "severity"),
                    Str(el, "band") ?? UnclassifiedBand,
                    Int(el, "penalty") ?? CurseScorer.StatusPenalty,
                    Str(el, "source") ?? string.Empty);
            }
        }

        return new StatusCatalog(entries) { Problem = problem };
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;
}
