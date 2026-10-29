using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;

namespace Ultima.Engine.Scoring.Relics;

/// <summary>
/// Score d'une relique (récompense, shop) : méta + synergie avec le deck et le personnage.
/// TODO agent S4 : synergie relique/deck, reliques d'Ancien (catégories), tests.
/// </summary>
public sealed class RelicScorer
{
    private readonly IMetaTable _meta;
    private readonly IPersonalStats _personal;

    public RelicScorer(IMetaTable meta, IPersonalStats personal)
    {
        _meta = meta;
        _personal = personal;
    }

    public Score Score(string relicId, ScoringContext ctx)
    {
        var b = new ScoreBuilder();
        var meta = _meta.Relic(relicId);

        if (meta?.Consensus is double consensus)
            b.Add("Méta communautaire", (int)Math.Round(consensus * 0.75, MidpointRounding.AwayFromZero),
                CardScorer.SourceLabel(meta));
        else
            b.Add("Pas de note communautaire", 0, "data/meta");

        return b.Build(Confidence.Of(meta?.SourceCount ?? 0, _personal.RunCount));
    }
}
