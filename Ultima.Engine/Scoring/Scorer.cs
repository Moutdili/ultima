using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Engine.Scoring.Relics;
using Ultima.Engine.Scoring.Shop;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring;

/// <summary>
/// Façade IScorer : assemble les composants (méta, perso, cartes, reliques, shop, deck).
/// Le daemon ne connaît que cette classe.
/// </summary>
public sealed class Scorer : IScorer
{
    private readonly CardScorer _cards;
    private readonly RelicScorer _relics;
    private readonly ShopScorer _shop;
    private readonly DeckEvaluator _deck;

    public IMetaTable Meta { get; }
    public IPersonalStats Personal { get; }

    public Scorer(IMetaTable? meta = null, IPersonalStats? personal = null)
    {
        Meta = meta ?? MetaTable.Load();
        Personal = personal ?? PersonalStats.Empty();

        var synergy = new SynergyEngine(Meta);
        _cards = new CardScorer(Meta, Personal, synergy);
        _relics = new RelicScorer(Meta, Personal);
        _shop = new ShopScorer(_cards, _relics);
        _deck = new DeckEvaluator(Meta, _cards);
    }

    public Score ScoreCard(CardInstance card, ScoringContext ctx) => _cards.ScoreCard(card, ctx);

    public Score ScoreSkip(IReadOnlyList<CardInstance> offered, ScoringContext ctx) =>
        _cards.ScoreSkip(offered, ctx);

    public Score ScoreRelic(string relicId, ScoringContext ctx) => _relics.Score(relicId, ctx);

    public Score ScoreShopItem(string kind, string id, int price, ScoringContext ctx) =>
        _shop.Score(kind, id, price, ctx);

    public Score ScoreRemoval(int price, ScoringContext ctx) => _shop.ScoreRemoval(price, ctx);

    public DeckEvaluation EvaluateDeck(ScoringContext ctx) => _deck.Evaluate(ctx);
}
