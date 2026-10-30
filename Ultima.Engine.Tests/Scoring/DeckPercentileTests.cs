using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Personal;
using Ultima.Engine.Scoring.Strength;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Note relative du deck (revue du chef, point 5) : le total devient le percentile de G dans le
/// corpus, au lieu d'une moyenne de notes de cartes qui donnait « F » à tout deck de départ.
/// </summary>
public sealed class DeckPercentileTests
{
    private const string Fixture = """
    {"schema":"ultima.deck-percentiles/1",
     "source":{"file":"test","method":"test","caveat":"test","runs":1000,"skipped":0,"median_deck":12},
     "min_runs":200,
     "buckets":[{"key":"SILENT|1","character":"SILENT","act":1,"n":1000,
                 "p10":10,"p25":20,"p50":30,"p75":40,"p90":50,"median_deck":12}]}
    """;

    private static string WriteFixture(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ultima-deckpct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, DeckPercentiles.FileName), json);
        return dir;
    }

    private static ScoringContext Ctx(string character = "CHARACTER.SILENT") => new(
        character, 0, null, 0, 2,
        new[]
        {
            new CardInstance("CARD.STRIKE_SILENT", 0, null, null),
            new CardInstance("CARD.STRIKE_SILENT", 0, null, null),
            new CardInstance("CARD.STRIKE_SILENT", 0, null, null),
            new CardInstance("CARD.DEFEND_SILENT", 0, null, null),
            new CardInstance("CARD.DEFEND_SILENT", 0, null, null),
            new CardInstance("CARD.DEFEND_SILENT", 0, null, null),
        },
        Array.Empty<string>(), 99, 70, 70);

    [Fact]
    public void Percentile_interpolates_between_published_points()
    {
        var dir = WriteFixture(Fixture);
        try
        {
            var pct = DeckPercentiles.Load(dir);
            Assert.True(pct.HasData);
            Assert.Null(pct.Problem);

            Assert.Equal(25, pct.Percentile("CHARACTER.SILENT", 1, 20));
            Assert.Equal(50, pct.Percentile("CHARACTER.SILENT", 1, 30));
            Assert.Equal(75, pct.Percentile("CHARACTER.SILENT", 1, 40));

            // Sous P10 et au-dessus de P90 : borné, jamais hors 0..100.
            Assert.InRange(pct.Percentile("CHARACTER.SILENT", 1, 0)!.Value, 0, 10);
            Assert.InRange(pct.Percentile("CHARACTER.SILENT", 1, 999)!.Value, 90, 100);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Unknown_character_falls_back_to_all_characters_bucket()
    {
        var json = Fixture.Replace("\"character\":\"SILENT\"", "\"character\":\"*\"")
                           .Replace("\"key\":\"SILENT|1\"", "\"key\":\"*|1\"");
        var dir = WriteFixture(json);
        try
        {
            var pct = DeckPercentiles.Load(dir);
            Assert.Equal(50, pct.Percentile("CHARACTER.DEFECT", 1, 30));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Committed_reference_loads_and_covers_each_act()
    {
        var pct = DeckPercentiles.Load();

        Assert.True(pct.HasData, pct.Problem);
        Assert.True(pct.Count >= 6, $"seulement {pct.Count} buckets");
        Assert.NotNull(pct.Find("*", 1));
        Assert.NotNull(pct.Find("*", 2));
        Assert.NotNull(pct.Find("CHARACTER.SILENT", 1));
        Assert.InRange(pct.Percentile("CHARACTER.SILENT", 1, 999)!.Value, 90, 100);
        Assert.InRange(pct.Percentile("CHARACTER.SILENT", 1, -50)!.Value, 0, 10);
    }

    [Fact]
    public void Missing_file_is_reported_not_guessed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ultima-deckpct-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var pct = DeckPercentiles.Load(dir);
            Assert.False(pct.HasData);
            Assert.NotNull(pct.Problem);
            Assert.Null(pct.Percentile("CHARACTER.SILENT", 1, 30));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Deck_note_becomes_the_corpus_percentile()
    {
        var dir = WriteFixture(Fixture);
        try
        {
            var meta = MetaTable.Load(includeLocal: false);
            var scorer = new CardScorer(meta, PersonalStats.Empty(), new SynergyEngine(meta));
            var pct = DeckPercentiles.Load(dir);
            var evaluator = new DeckEvaluator(meta, scorer, pct);

            var ctx = Ctx();
            var evaluation = evaluator.Evaluate(ctx);

            var g = new PlayerStrengthCalculator().Compute(ctx).Total;
            var expected = pct.Percentile(ctx.CharacterId, ctx.ActIndex + 1, g);

            Assert.NotNull(expected);

            // Total = 75 % percentile + 25 % note absolue (les termes hors percentile donnent l'absolu).
            var absolute = evaluation.Score.Terms
                .Where(t => !t.Label.StartsWith("Percentile", StringComparison.Ordinal))
                .Sum(t => t.Points);
            var blended = (int)Math.Round(DeckEvaluator.PercentileWeight * expected!.Value
                                          + (1 - DeckEvaluator.PercentileWeight) * absolute);

            Assert.Equal(blended, evaluation.Score.Total);
            Assert.Contains(evaluation.Score.Terms,
                t => t.Label.StartsWith("Percentile des decks", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
