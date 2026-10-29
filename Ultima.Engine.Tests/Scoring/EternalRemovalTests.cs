using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Deck;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Les malédictions <c>Eternal</c> ne peuvent pas être retirées (<c>CardModel.IsRemovable</c> :
/// « ne peuvent être retirées de votre Deck ou transformées »). Le conseiller ne doit donc pas
/// recommander une action impossible, même si la carte est la plus toxique du deck.
/// </summary>
public sealed class EternalRemovalTests
{
    private const string BossAct1 = "ENCOUNTER.WATERFALL_GIANT_BOSS";

    private static CardInstance Card(string id) => new(id, 0, null, null);

    private static ScoringContext Ctx(IReadOnlyList<CardInstance> deck) =>
        new("CHARACTER.IRONCLAD", 0, BossAct1, 0, 10, deck, Array.Empty<string>(), 999, 70, 70);

    [Fact]
    public void Eternal_curse_is_not_recommended_for_removal()
    {
        var deck = new[] { Card("CARD.BAD_LUCK"), Card("CARD.STRIKE_IRONCLAD") };
        var advisor = new RemovalAdvisor();

        var candidates = advisor.Recommend(Ctx(deck), 75, 999);
        var badLuck = candidates.Single(c => c.CardId.Contains("BAD_LUCK", StringComparison.OrdinalIgnoreCase));

        Assert.False(badLuck.WorthIt);
        Assert.Contains("Éternel", badLuck.Reason);
    }

    [Fact]
    public void Removable_curse_stays_actionable()
    {
        var deck = new[] { Card("CARD.POOR_SLEEP"), Card("CARD.STRIKE_IRONCLAD") };
        var advisor = new RemovalAdvisor();

        var candidates = advisor.Recommend(Ctx(deck), 75, 999);
        var poorSleep = candidates.Single(c => c.CardId.Contains("POOR_SLEEP", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("Éternel", poorSleep.Reason);
    }
}
