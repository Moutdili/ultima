namespace Ultima.Engine.Scoring.Model;

/// <summary>Une note d'une source pour une carte/relique (data/meta/*.json).</summary>
public sealed record SourceRating(
    string SourceId, string? Tier, double? Score, string? Patch, string? Date, string? Note, int? N);

/// <summary>
/// Entrée méta normalisée. <see cref="Id"/> est l'id du jeu sans préfixe ("ALCHEMIZE"),
/// les accesseurs acceptent aussi "CARD.ALCHEMIZE".
/// </summary>
public sealed record MetaEntry(
    string Id,
    string ClassName,
    string NameEng,
    string? NameFra,
    string? Type,
    string? Rarity,
    int? Cost,
    string? Owner,
    IReadOnlyList<SourceRating> Ratings,
    double? Consensus,
    int SourceCount,
    double Spread)
{
    public string DisplayName => string.IsNullOrWhiteSpace(NameFra) ? NameEng : NameFra!;
}

/// <summary>Un archétype/cluster de data/meta/archetypes.json.</summary>
public sealed record MetaArchetype(
    string Name,
    string CharacterId,
    string SourceId,
    string Kind,
    IReadOnlyList<string> KeyCardIds,
    IReadOnlyList<string> KeyRelicIds);

/// <summary>Accès en lecture à la méta communautaire. Jamais null : une absence est explicite.</summary>
public interface IMetaTable
{
    /// <summary>"CARD.X" ou "X".</summary>
    MetaEntry? Card(string id);

    /// <summary>"RELIC.X" ou "X".</summary>
    MetaEntry? Relic(string id);

    /// <summary>Part des récompenses où la communauté PASSE (0..1). Null si la source manque.</summary>
    double? SkipRate { get; }

    /// <summary>Elo communautaire de l'option « passer », si publié.</summary>
    double? SkipElo { get; }

    /// <summary>"CHARACTER.DEFECT" ou "defect".</summary>
    IReadOnlyList<MetaArchetype> Archetypes(string characterId);

    /// <summary>"public" (commitée) ou "full" (data/meta/local/).</summary>
    string Variant { get; }

    /// <summary>Date de génération des données (ISO), pour l'affichage des sources.</summary>
    string? Generated { get; }

    /// <summary>Patch cible des données (ex. "v0.107.1").</summary>
    string? PatchTarget { get; }
}

/// <summary>Normalisation des identifiants entre le jeu et les données méta.</summary>
public static class Ids
{
    public static string Bare(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        var s = id.Trim().ToUpperInvariant();
        if (s.StartsWith("CARD.")) s = s[5..];
        else if (s.StartsWith("RELIC.")) s = s[6..];
        else if (s.StartsWith("CHARACTER.")) s = s[10..];
        return s;
    }

    /// <summary>"CHARACTER.DEFECT" → "defect" (clé de data/meta/archetypes.json).</summary>
    public static string CharacterKey(string? id) => Bare(id).ToLowerInvariant();
}
