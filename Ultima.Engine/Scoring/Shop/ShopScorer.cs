using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Relics;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Shop;

/// <summary>
/// Score d'un article de shop : valeur intrinsèque vs prix vs or disponible.
/// Le retrait de carte est scoré séparément (<see cref="ScoreRemoval"/>).
/// TODO agent S4 : courbe prix/valeur, réductions, comparaison au retrait, tests sur le shop réel.
/// </summary>
public sealed class ShopScorer
{
    private readonly CardScorer _cards;
    private readonly RelicScorer _relics;

    public ShopScorer(CardScorer cards, RelicScorer relics)
    {
        _cards = cards;
        _relics = relics;
    }

    public Score Score(string kind, string id, int price, ScoringContext ctx)
    {
        var b = new ScoreBuilder();

        int value = kind.ToLowerInvariant() switch
        {
            "card" => _cards.ScoreCard(new CardInstance(id, 0, null, null), ctx).Total,
            "relic" => _relics.Score(id, ctx).Total,
            _ => 50 // potion : neutre tant que l'agent S4 n'a pas posé de règle
        };
        b.Add($"Valeur ({kind})", (int)Math.Round(value * 0.7, MidpointRounding.AwayFromZero), "moteur de score");

        // Économie : plus l'article mange l'or disponible, plus le terme est négatif.
        if (price > 0)
        {
            double share = ctx.Gold <= 0 ? 2.0 : (double)price / ctx.Gold;
            int economy = (int)Math.Round(ScoreMath.Clamp(1.0 - share, -1.0, 1.0) * 15, MidpointRounding.AwayFromZero);
            b.Add("Économie (prix / or)", economy, $"{price} or sur {ctx.Gold}");
        }

        if (price > ctx.Gold) b.Add("Non abordable", -20, $"{price} > {ctx.Gold} or");

        return b.Build(Confidence.Of(0, 0));
    }

    public Score ScoreRemoval(int price, ScoringContext ctx)
    {
        var b = new ScoreBuilder();
        b.Add("Base retrait", 25, "décision de deck");

        var curse = ctx.Deck.FirstOrDefault(c => IsCurse(c.Id));
        if (curse != null) b.Add("Retirer une malédiction", 30, curse.Id);

        double share = ctx.Gold <= 0 ? 2.0 : (double)price / ctx.Gold;
        b.Add("Coût", -(int)Math.Round(ScoreMath.Clamp(share, 0, 1.5) * 20, MidpointRounding.AwayFromZero),
            $"{price} or sur {ctx.Gold}");

        return b.Build(Confidence.Low);
    }

    private static bool IsCurse(string id) => id.Contains("CURSE", StringComparison.OrdinalIgnoreCase);
}
