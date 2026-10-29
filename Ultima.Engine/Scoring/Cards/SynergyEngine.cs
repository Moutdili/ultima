using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Cards;

/// <summary>
/// Règles de synergie explicites (deck + reliques). Aucune boîte noire : chaque terme est nommé.
/// TODO agent S3 : tags par carte (poison, force, orbe, multi-coup…), seuils et plafonds.
/// </summary>
public sealed class SynergyEngine
{
    private readonly IMetaTable _meta;

    public SynergyEngine(IMetaTable meta) => _meta = meta;

    /// <summary>Termes de synergie deck/reliques pour une carte candidate. Vide par défaut.</summary>
    public IReadOnlyList<ScoreTerm> Terms(CardInstance card, ScoringContext ctx) =>
        Array.Empty<ScoreTerm>();

    /// <summary>Tags mécaniques d'une carte, utilisés par le scoring et l'archétype.</summary>
    public IReadOnlyList<string> Tags(CardInstance card) => Array.Empty<string>();
}
