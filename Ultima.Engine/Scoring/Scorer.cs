using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Engine.Scoring.Potions;
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
    private readonly PotionScorer _potions;
    private readonly RemovalAdvisor _removal;

    public IMetaTable Meta { get; }
    public IPersonalStats Personal { get; }

    /// <summary>
    /// Conseiller de retrait (docs/35) : classe les cartes du deck par gain de G après retrait,
    /// malédictions comprises, et compare ce gain au prix (75 or, 100 à A6+). Hors contrat
    /// <c>IScorer</c> (figé) : ajout additif pour l'UI et les tests.
    /// </summary>
    public RemovalAdvisor Removal => _removal;

    public Scorer(IMetaTable? meta = null, IPersonalStats? personal = null)
    {
        Meta = meta ?? MetaTable.Load();
        Personal = personal ?? PersonalStats.Empty();

        var synergy = new SynergyEngine(Meta);
        _cards = new CardScorer(Meta, Personal, synergy);
        _relics = new RelicScorer(Meta, Personal, RelicCatalog.Load());
        _shop = new ShopScorer(_cards, _relics);
        _deck = new DeckEvaluator(Meta, _cards);
        _potions = new PotionScorer(PotionCatalog.Load());
        _removal = new RemovalAdvisor(meta: Meta);
    }

    public Score ScoreCard(CardInstance card, ScoringContext ctx) => _cards.ScoreCard(card, ctx);

    public Score ScoreSkip(IReadOnlyList<CardInstance> offered, ScoringContext ctx) =>
        _cards.ScoreSkip(offered, ctx);

    public Score ScoreRelic(string relicId, ScoringContext ctx) => _relics.Score(relicId, ctx);

    /// <summary>
    /// Valeur nette d'une relique qui impose une malédiction (docs/35 §4) — hors contrat figé,
    /// additif. <paramref name="includeRemovalCost"/> ajoute le prix du retrait (75/100 or selon A6).
    /// </summary>
    public RelicNetValue ScoreRelicNet(string relicId, ScoringContext ctx, bool includeRemovalCost = false) =>
        _relics.NetValue(relicId, ctx, includeRemovalCost);

    public Score ScoreShopItem(string kind, string id, int price, ScoringContext ctx) =>
        _shop.Score(kind, id, price, ctx);

    public Score ScoreRemoval(int price, ScoringContext ctx) => _shop.ScoreRemoval(price, ctx);

    public DeckEvaluation EvaluateDeck(ScoringContext ctx) => _deck.Evaluate(ctx);

    /// <summary>Contrat v1.1 (S6bis) : null si la potion est inconnue du catalogue.</summary>
    public Score? ScorePotion(string potionId, ScoringContext ctx) => _potions.Score(potionId, ctx);
}
