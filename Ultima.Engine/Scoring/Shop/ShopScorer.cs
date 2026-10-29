using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Relics;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Shop;

/// <summary>
/// Score d'un article de shop et du retrait de carte. Formule figée (docs/24-hermes-arch.md §2) :
/// <list type="bullet">
/// <item>article : <c>valeur × 0.7 + clamp(1 − prix/or, −1, 1) × 15</c>, puis <c>−20</c> si non abordable ;</item>
/// <item>retrait : <c>25 + 30 si le deck contient une malédiction − clamp(prix/or, 0, 1.5) × 20</c>.</item>
/// </list>
///
/// <para>
/// <b>Prix et remises.</b> <c>price</c> est le prix <i>affiché</i>, remises déjà comprises :
/// le miroir applique MembershipCard, The Courier et le flag <c>IsOnSale</c> au coût de base
/// (Ultima.Mirror/src/Ultima.Mirror/Readers.cs, <c>ReadShop</c>). Le scorer ne ré-applique donc
/// jamais de réduction — ce serait un double comptage. Un prix à 0 est un article gratuit, pas
/// une erreur : il reçoit le maximum d'économie (+15) et n'est jamais « non abordable ».
/// </para>
/// <para>
/// <b>Potion.</b> Aucune source publique ne note les potions (data/meta n'en contient aucune) :
/// la valeur par défaut est <see cref="PotionValue"/> (50/100, neutre), documentée comme telle et
/// remplaçable dès qu'une source existe. Elle ne prétend pas classer deux potions entre elles.
/// </para>
/// </summary>
public sealed class ShopScorer
{
    /// <summary>Valeur neutre documentée d'une potion (aucune méta potion publiée).</summary>
    public const int PotionValue = 50;

    private const int EconomyMax = 15;   // clamp(1 − prix/or, −1, 1) × 15
    private const int Unaffordable = -20;
    private const int RemovalBase = 25;
    private const int RemovalCurseBonus = 30;
    private const int RemovalCostMax = 20; // clamp(prix/or, 0, 1.5) × 20
    private const string CurseTerm = "Malédiction"; // terme posé par CardScorer (source : IMetaTable.Card)

    private readonly CardScorer _cards;
    private readonly RelicScorer _relics;

    public ShopScorer(CardScorer cards, RelicScorer relics)
    {
        _cards = cards;
        _relics = relics;
    }

    public Score Score(string kind, string id, int price, ScoringContext ctx)
    {
        var k = Normalize(kind);
        int p = Math.Max(0, price);
        int gold = Math.Max(0, ctx.Gold);

        var (value, source, confidence) = Value(k, id, ctx);

        var b = new ScoreBuilder();
        b.Add($"Valeur ({k})", ScoreMath.Round(value * 0.7), source);
        b.Add("Économie (prix / or)", Economy(p, gold), PriceBasis(p, gold));

        if (p > gold) b.Add("Non abordable", Unaffordable, $"{p} or > {gold} or disponible");

        return b.Build(confidence);
    }

    public Score ScoreRemoval(int price, ScoringContext ctx)
    {
        int p = Math.Max(0, price);
        int gold = Math.Max(0, ctx.Gold);

        var b = new ScoreBuilder();
        b.Add("Base retrait", RemovalBase, "une carte de moins dans le deck");

        var curses = CursesInDeck(ctx);
        if (curses.Count > 0)
            b.Add("Retirer une malédiction", RemovalCurseBonus, DescribeCurse(curses));

        b.Add("Coût du retrait", RemovalCost(p, gold), PriceBasis(p, gold, removal: true));

        var score = b.Build(Confidence.Low);
        if (p > gold)
        {
            var terms = new List<ScoreTerm>(score.Terms)
            {
                new("Retrait non abordable", 0, $"{p} or > {gold} or disponible")
            };
            score = score with { Terms = terms };
        }

        return score;
    }

    /// <summary>Valeur intrinsèque de l'article + provenance du chiffre + confiance associée.</summary>
    private (int Value, string Source, string Confidence) Value(string kind, string id, ScoringContext ctx)
    {
        switch (kind)
        {
            case "card":
            {
                var score = _cards.ScoreCard(new CardInstance(id, 0, null, null), ctx);
                return (score.Total, Attribution(score, $"carte {Ids.Bare(id)}"), score.Confidence);
            }
            case "relic":
            {
                var score = _relics.Score(id, ctx);
                return (score.Total, Attribution(score, $"relique {Ids.Bare(id)}"), score.Confidence);
            }
            case "potion":
                return (PotionValue, "valeur neutre documentée : aucune méta potion publiée", Confidence.Low);
            default:
                return (PotionValue, $"type « {kind} » inconnu → valeur neutre {PotionValue}", Confidence.Unknown);
        }
    }

    /// <summary>
    /// Hérite de la source du terme dominant du score de valeur (ex. « spirecodex-scores (n=1…) »)
    /// pour que l'UI puisse répondre à « d'où vient ce chiffre ? ».
    /// </summary>
    private static string Attribution(Score score, string fallback)
    {
        var term = score.Terms
            .Where(t => t.Points != 0 && !string.IsNullOrWhiteSpace(t.Source))
            .OrderByDescending(t => Math.Abs(t.Points))
            .FirstOrDefault();

        return term == null ? $"{fallback} : aucune donnée" : $"{fallback} — {term.Source}";
    }

    private static string Normalize(string? kind) =>
        string.IsNullOrWhiteSpace(kind) ? "potion" : kind.Trim().ToLowerInvariant();

    /// <summary>clamp(1 − prix/or, −1, 1) × 15. Or nul : économie minimale (ou maximale si gratuit).</summary>
    private static int Economy(int price, int gold)
    {
        if (gold <= 0) return price <= 0 ? EconomyMax : -EconomyMax;

        double share = (double)price / gold;
        return ScoreMath.Round(ScoreMath.Clamp(1.0 - share, -1.0, 1.0) * EconomyMax);
    }

    /// <summary>−clamp(prix/or, 0, 1.5) × 20. Or nul : coût maximal (−30) dès que le prix &gt; 0.</summary>
    private static int RemovalCost(int price, int gold)
    {
        if (gold <= 0) return price <= 0 ? 0 : -ScoreMath.Round(1.5 * RemovalCostMax);

        return -ScoreMath.Round(ScoreMath.Clamp((double)price / gold, 0, 1.5) * RemovalCostMax);
    }

    private static string PriceBasis(int price, int gold, bool removal = false)
    {
        string what = removal ? "retrait" : "prix affiché, remises comprises";
        if (price == 0) return $"gratuit (0 or) — {what}";
        if (gold <= 0) return $"{price} or — or disponible inconnu (0)";
        return $"{price} or sur {gold} or — {what}";
    }

    /// <summary>
    /// Malédictions du deck. Détection par la donnée, jamais par le nom de l'id :
    /// le catalogue du jeu (<see cref="CurseIndex"/>, type « Curse ») puis, en repli, le terme
    /// « Malédiction » du score de carte — lui-même calculé depuis <c>IMetaTable.Card</c>
    /// (Type/Rarity « Curse », cf. CardScorer).
    /// </summary>
    private List<string> CursesInDeck(ScoringContext ctx)
    {
        var found = new List<string>();
        foreach (var card in ctx.Deck ?? Array.Empty<CardInstance>())
        {
            if (CurseIndex.IsCurse(null, card.Id))
            {
                found.Add(CurseIndex.Display(null, card.Id));
                continue;
            }

            var term = _cards.ScoreCard(card, ctx).Terms.FirstOrDefault(t => t.Label == CurseTerm);
            if (term != null) found.Add(term.Source);
        }
        return found;
    }

    private static string DescribeCurse(List<string> curses) =>
        curses.Count == 1 ? curses[0] : $"{curses[0]} (+{curses.Count - 1} autre(s))";
}
