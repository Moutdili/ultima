using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Model;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>Tests de fumée du socle : la méta se charge et un score sort avec ses termes.</summary>
public sealed class SmokeTests
{
    private static readonly ScoringContext Ctx = new(
        "CHARACTER.SILENT", 0, "ENCOUNTER.WATERFALL_GIANT_BOSS", 0, 5,
        new[] { new CardInstance("CARD.STRIKE_SILENT", 0, null, 1) }, Array.Empty<string>(), 99, 70, 70);

    [Fact]
    public void MetaTable_loads_public_data()
    {
        var meta = MetaTable.Load();
        Assert.True(meta.CardCount > 400, $"cartes chargées: {meta.CardCount}");
        Assert.True(meta.RelicCount > 200, $"reliques chargées: {meta.RelicCount}");
        Assert.NotNull(meta.PatchTarget);
        Assert.True(meta.SkipRate is > 0.5 and < 0.9, $"skip rate: {meta.SkipRate}");
    }

    [Fact]
    public void Known_card_has_consensus_and_source()
    {
        var meta = MetaTable.Load();
        var card = meta.Card("CARD.ALCHEMIZE");
        Assert.NotNull(card);
        Assert.NotNull(card!.Consensus);
        Assert.NotEmpty(card.Ratings);
        Assert.Equal("ALCHEMIZE", card.Id);
    }

    [Fact]
    public void ScoreCard_returns_explainable_terms()
    {
        var scorer = new Scorer();
        var s = scorer.ScoreCard(new CardInstance("CARD.ALCHEMIZE", 0, null, null), Ctx);
        Assert.InRange(s.Total, 0, 100);
        Assert.NotEmpty(s.Terms);
        Assert.All(s.Terms, t => Assert.False(string.IsNullOrWhiteSpace(t.Source)));
    }

    [Fact]
    public void Archetypes_are_indexed_by_character()
    {
        var meta = MetaTable.Load();
        Assert.NotEmpty(meta.Archetypes("CHARACTER.DEFECT"));
    }
}
