using Ultima.Engine.Scoring.Events;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Classement des options d'une salle « ? ». Les profils sont mesurés (80 000 runs solo) ;
/// le score valorise gains et coûts, et le coût des PV dépend de la vie restante.
/// </summary>
public sealed class EventScorerTests
{
    private static readonly EventScorer Scorer = new();

    [Fact]
    public void Catalog_covers_hundreds_of_measured_options()
    {
        Assert.True(Scorer.Catalog.Count >= 150, $"options mesurées : {Scorer.Catalog.Count}");
        Assert.Null(Scorer.Catalog.Problem);
    }

    [Fact]
    public void Gold_option_is_ranked_high()
    {
        // Sunken Treasury / Second Chest : +334 or en moyenne sur 2 502 choix.
        var choice = Scorer.Score("SUNKEN_TREASURY", "SECOND_CHEST", new EventContext(70, 70, 100));

        Assert.True(choice.Measured);
        Assert.True(choice.Score >= 60, $"score {choice.Score} — {choice.Explanation}");
        Assert.Contains(choice.Terms, t => t.Label == "Or" && t.Points > 0);
    }

    [Fact]
    public void Relic_option_is_valued()
    {
        // This or That / Ornate : une relique dans 100 % des cas.
        var choice = Scorer.Score("THIS_OR_THAT", "ORNATE", new EventContext(70, 70, 100));

        Assert.True(choice.Score >= 55, $"score {choice.Score} — {choice.Explanation}");
        Assert.Contains(choice.Terms, t => t.Label == "Relique");
    }

    [Fact]
    public void Losing_hp_costs_more_when_low()
    {
        // Room Full of Cheese / Search : une relique contre ~14 PV.
        var healthy = Scorer.Score("ROOM_FULL_OF_CHEESE", "SEARCH", new EventContext(65, 70, 50));
        var wounded = Scorer.Score("ROOM_FULL_OF_CHEESE", "SEARCH", new EventContext(10, 70, 50));

        Assert.True(wounded.Score < healthy.Score,
            $"à 14 % de vie {wounded.Score} devrait être < à 93 % {healthy.Score}");
        Assert.Contains(wounded.Terms, t => t.Label == "Dégâts" && t.Points < 0);
    }

    [Fact]
    public void Unknown_option_is_explicit_and_neutral()
    {
        var choice = Scorer.Score("PAS_UN_EVENEMENT", "PAS_UNE_OPTION", new EventContext(70, 70, 0));

        Assert.False(choice.Measured);
        Assert.Equal(50, choice.Score);
        Assert.Contains("aucune mesure", choice.Terms[0].Source);
    }

    [Fact]
    public void Ranking_is_sorted_and_complete()
    {
        var options = new[] { "ABSTAIN", "IMMERGE", "LINGER", "EXIT_BATHS" };
        var ranked = Scorer.Rank("ABYSSAL_BATHS", options, new EventContext(70, 70, 100));

        Assert.Equal(options.Length, ranked.Count);
        Assert.Equal(ranked.OrderByDescending(c => c.Score).Select(c => c.Score), ranked.Select(c => c.Score));
    }
}
