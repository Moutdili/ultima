using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Deck;

/// <summary>
/// Note du deck (0-100) vs reliques et boss de l'acte, archétype probable et potentiels,
/// malédictions listées. TODO agent S5 : pondération par rareté, archétypes, boss, tests.
/// </summary>
public sealed class DeckEvaluator
{
    private readonly IMetaTable _meta;
    private readonly CardScorer _cards;

    public DeckEvaluator(IMetaTable meta, CardScorer cards)
    {
        _meta = meta;
        _cards = cards;
    }

    public DeckEvaluation Evaluate(ScoringContext ctx)
    {
        var b = new ScoreBuilder();

        if (ctx.Deck.Count == 0)
            return new DeckEvaluation(b.Build(Confidence.Low), null, Array.Empty<string>(), Array.Empty<CardView>());

        double sum = 0;
        var curses = new List<CardView>();
        foreach (var card in ctx.Deck)
        {
            var score = _cards.ScoreCard(card, ctx);
            sum += score.Total;

            var meta = _meta.Card(card.Id);
            if (CardScorer.IsCurse(meta))
            {
                curses.Add(new CardView(card.Id, meta?.DisplayName ?? card.Id, meta?.Type ?? "Curse",
                    meta?.Rarity ?? "Curse", meta?.Cost ?? 0, card.UpgradeLevel, null, true, null));
            }
        }

        var avg = sum / ctx.Deck.Count;
        b.Add("Note moyenne des cartes", (int)Math.Round(avg * 0.8, MidpointRounding.AwayFromZero),
            $"{ctx.Deck.Count} cartes");
        b.Add("Malédictions", -8 * curses.Count, curses.Count > 0 ? string.Join(", ", curses.Select(c => c.Name)) : "aucune");

        return new DeckEvaluation(b.Build(Confidence.Of(0, 0)), null,
            Array.Empty<string>(), curses);
    }
}
