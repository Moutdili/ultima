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
    /// État des données de scoring. Le daemon le traduit en <c>SourceHealth.Degraded</c> :
    /// sans méta, <c>IsLoaded = false</c> et <c>Problem</c> dit pourquoi — jamais des F 0 muets.
    /// </summary>
    public ScoringDataStatus DataStatus => Meta is MetaTable table
        ? new ScoringDataStatus(table.IsLoaded, table.CardCount, table.RelicCount, table.Variant,
            table.DataPath, table.Problem)
        : new ScoringDataStatus(true, -1, -1, "hôte", "", null);   // -1 = comptes non exposés

    /// <summary>
    /// Conseiller de retrait (docs/35) : classe les cartes du deck par gain de G après retrait,
    /// malédictions comprises, et compare ce gain au prix (75 or, 100 à A6+). Hors contrat
    /// <c>IScorer</c> (figé) : ajout additif pour l'UI et les tests.
    /// </summary>
    public RemovalAdvisor Removal => _removal;

    /// <summary>État des sources de données (méta, chemins, raison d'un échec).</summary>
    public sealed record ScoringDataStatus(
        bool IsLoaded, int Cards, int Relics, string Variant, string Path, string? Problem)
    {
        /// <summary>Message prêt pour <c>SourceHealth.Degraded</c> (null = rien à signaler).</summary>
        public string? Degraded => Problem;
    }

    public Scorer(IMetaTable? meta = null, IPersonalStats? personal = null)
    {
        Meta = meta ?? MetaTable.Load();
        Personal = personal ?? PersonalStats.Empty();

        var synergy = new SynergyEngine(Meta);
        _cards = new CardScorer(Meta, Personal, synergy);
        _relics = new RelicScorer(Meta, Personal, RelicCatalog.Load());
        _potions = new PotionScorer(PotionCatalog.Load());
        _shop = new ShopScorer(_cards, _relics, _potions);
        _deck = new DeckEvaluator(Meta, _cards, DeckPercentiles.Default);
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

    /// <summary>
    /// Retrait : le total reste la formule du marchand, mais la <b>carte à retirer</b> vient du
    /// conseiller (RemovalAdvisor) — une seule recommandation, pas deux classements parallèles.
    /// Le terme porte le gain de G et le seuil de rentabilité, à 0 point (il n'invente pas de
    /// points sur une échelle qui n'est pas la sienne).
    /// </summary>
    public Score ScoreRemoval(int price, ScoringContext ctx)
    {
        var score = _shop.ScoreRemoval(price, ctx);

        var best = _removal.Recommend(ctx, price, Math.Max(0, ctx.Gold))
            .OrderByDescending(c => c.Score)
            .FirstOrDefault();

        if (best is null) return score;   // deck vide : rien à recommander

        var terms = new List<ScoreTerm>(score.Terms)
        {
            new($"Meilleure carte à retirer : {best.Name}", 0,
                $"{best.Reason} · gain {best.Score} pts de G, rentable dès {best.BreakEven:0.#}"),
        };

        return score with { Terms = terms };
    }

    public DeckEvaluation EvaluateDeck(ScoringContext ctx) => _deck.Evaluate(ctx);

    /// <summary>Contrat v1.1 (S6bis) : null si la potion est inconnue du catalogue.</summary>
    public Score? ScorePotion(string potionId, ScoringContext ctx) => _potions.Score(potionId, ctx);
}
