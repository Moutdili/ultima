using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Meta;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Revue du chef, points 1 et 6 (docs/42) : jamais des F 0 silencieux quand la méta manque,
/// et <c>Score.DeltaG</c> réellement rempli par le calcul de fit.
/// </summary>
public sealed class DataStatusTests
{
    private static ScoringContext Ctx(params string[] deck) => new(
        "CHARACTER.REGENT", 0, null, 0, 5,
        deck.Select(id => new CardInstance(id, 0, null, null)).ToArray(),
        Array.Empty<string>(), 99, 70, 70);

    [Fact]
    public void Empty_dir_is_reported_instead_of_scored_zero()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ultima-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);

        try
        {
            var meta = MetaTable.Load(tmp, includeLocal: false);

            Assert.False(meta.IsLoaded);
            Assert.NotNull(meta.Problem);
            Assert.Contains("introuvable", meta.Problem!);

            var scorer = new Scorer(meta);
            Assert.False(scorer.DataStatus.IsLoaded);
            Assert.NotNull(scorer.DataStatus.Degraded);
            Assert.Contains("introuvable", scorer.DataStatus.Degraded!);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* nettoyage best effort */ }
        }
    }

    [Fact]
    public void Committed_meta_loads_without_problem()
    {
        var meta = MetaTable.Load(includeLocal: false);

        Assert.True(meta.IsLoaded);
        Assert.Null(meta.Problem);
        Assert.True(meta.CardCount > 400);
        Assert.False(string.IsNullOrWhiteSpace(meta.DataPath));
    }

    [Fact]
    public void Signed_DeltaG_comes_from_the_fit_calculation()
    {
        // Deck saturé de BIG_BANG : CRASH_LANDING apporte autre chose, son ΔG doit être non nul.
        var ctx = Ctx(Enumerable.Repeat("CARD.BIG_BANG", 12).ToArray());

        var score = new Scorer().ScoreCard(new CardInstance("CARD.CRASH_LANDING", 0, null, null), ctx);

        Assert.NotNull(score.DeltaG);
        Assert.NotEqual(0.0, score.DeltaG!.Value);
    }

    [Fact]
    public void Curse_DeltaG_is_negative_and_letter_stays_bounded()
    {
        var score = new Scorer().ScoreCard(new CardInstance("CARD.INJURY", 0, null, null), Ctx("CARD.STRIKE"));

        Assert.NotNull(score.DeltaG);
        Assert.True(score.DeltaG < 0);
        Assert.InRange(score.Total, 0, 100);
    }
}
