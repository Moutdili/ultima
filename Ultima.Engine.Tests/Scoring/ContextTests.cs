using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Scoring contextuel : la même relique ne vaut pas la même chose selon l'état du joueur.
/// Exemple de Mio : une relique de PV max = B pour un joueur à PV pleins, A pour un joueur à 18/70.
/// </summary>
public sealed class ContextTests
{
    private static ScoringContext Ctx(int hp, int maxHp) => new(
        "CHARACTER.SILENT", 0, "ENCOUNTER.WATERFALL_GIANT_BOSS", 0, 5,
        Array.Empty<CardInstance>(), Array.Empty<string>(), 99, hp, maxHp);

    private static int Rank(string grade) => grade switch
    {
        "S" => 6, "A" => 5, "B" => 4, "C" => 3, "D" => 2, _ => 1
    };

    [Theory]
    [InlineData("RELIC.MANGO")]
    [InlineData("RELIC.MEAT_ON_THE_BONE")]
    public void Relic_that_gives_hp_is_worth_more_when_hurt(string relicId)
    {
        var scorer = new Scorer();
        var full = scorer.ScoreRelic(relicId, Ctx(70, 70));
        var low = scorer.ScoreRelic(relicId, Ctx(18, 70));

        Assert.True(low.Total >= full.Total + 10,
            $"{relicId}: à 18/70 = {low.Total} ({low.Grade}), à 70/70 = {full.Total} ({full.Grade})");
        Assert.True(Rank(low.Grade) >= Rank(full.Grade), $"{low.Grade} devrait être ≥ {full.Grade}");
        Assert.Contains(low.Terms, t => t.Label.Contains("besoin de PV", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(full.Terms, t => t.Label.Contains("besoin de PV", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Explanation_names_the_meta_source()
    {
        var scorer = new Scorer();
        var score = scorer.ScoreCard(new CardInstance("CARD.ALCHEMIZE", 0, null, null), Ctx(70, 70));
        var meta = score.Terms.First(t => t.Label.StartsWith("Méta communautaire", StringComparison.Ordinal));

        Assert.Contains("Spire Codex", meta.Source);
        Assert.Contains("consensus", meta.Source);
    }

    [Fact]
    public void Deck_note_stays_stable_when_hp_changes()
    {
        var scorer = new Scorer();
        var full = scorer.EvaluateDeck(Ctx(70, 70));
        var low = scorer.EvaluateDeck(Ctx(15, 70));

        Assert.Equal(full.Score.Total, low.Score.Total);
    }
}
