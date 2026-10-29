using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Potions;
using Ultima.Engine.Scoring.Relics;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Shop;

/// <summary>
/// Score d'un article de shop et du retrait de carte.
/// <list type="bullet">
/// <item>article : la note de la récompense (carte, relique) — <b>le prix ne change pas la lettre</b> ;</item>
/// <item>potion : note plafonnée à <see cref="PotionCap"/>, marquée non comparable ;</item>
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
    /// <summary>Ancienne valeur neutre : conservée pour compatibilité, plus utilisée pour noter.</summary>
    public const int PotionValue = 50;

    /// <summary>Plafond d'une potion : aucune source publique ne la compare aux cartes.</summary>
    public const int PotionCap = 45;

    private const int RemovalBase = 25;
    private const int RemovalCurseBonus = 30;
    private const int RemovalCostMax = 20; // clamp(prix/or, 0, 1.5) × 20

    /// <summary>
    /// Préfixe des termes de malédiction posés par <c>CardScorer</c> via <c>CurseScorer</c> :
    /// « Malédiction (impact mesuré) », « … (impact non classable) », « … (aucune donnée mesurée) ».
    /// </summary>
    private const string CurseTermPrefix = "Malédiction";

    private readonly CardScorer _cards;
    private readonly RelicScorer _relics;
    private readonly PotionScorer? _potions;

    public ShopScorer(CardScorer cards, RelicScorer relics, PotionScorer? potions = null)
    {
        _cards = cards;
        _relics = relics;
        _potions = potions;
    }

    /// <summary>
    /// Score d'un article. La <b>lettre est celle de la récompense</b> : une carte vaut au shop
    /// exactement ce qu'elle vaut à la récompense, une relique ce qu'elle vaut en butin. Le prix
    /// n'entre plus dans la note — l'abordabilité est déjà un booléen du contrat
    /// (<c>ShopItem.Affordable</c>) et l'économie reste un terme informatif à 0 point.
    /// </summary>
    public Score Score(string kind, string id, int price, ScoringContext ctx)
    {
        var k = Normalize(kind);
        int p = Math.Max(0, price);
        int gold = Math.Max(0, ctx.Gold);

        var basis = k switch
        {
            "card" => _cards.ScoreCard(new CardInstance(id, 0, null, null), ctx),
            "relic" => _relics.Score(id, ctx),
            "potion" => PotionScore(id, ctx),
            _ => new Score(0, Array.Empty<ScoreTerm>(), Confidence.Unknown),
        };

        var terms = new List<ScoreTerm>(basis.Terms)
        {
            new("Économie (prix / or)", 0, PriceBasis(p, gold)),
        };

        if (k is not ("card" or "relic" or "potion"))
            terms.Add(new ScoreTerm($"Type « {kind} » inconnu", 0, "type non reconnu : aucune valeur"));

        if (p > gold)
            terms.Add(new ScoreTerm("Non abordable", 0, $"{p} or > {gold} or — la lettre ne change pas"));

        return basis with { Terms = terms };
    }

    /// <summary>
    /// Note d'une potion, volontairement <b>plafonnée</b> : aucune source publique ne la compare
    /// aux cartes. Un placeholder ne doit jamais battre une mesure.
    /// </summary>
    private Score PotionScore(string id, ScoringContext ctx)
    {
        var measured = _potions?.Score(id, ctx);
        if (measured is null)
            return new Score(0,
                new[] { new ScoreTerm($"Potion {Ids.Bare(id)} inconnue", 0, "aucune description au catalogue") },
                Confidence.Unknown);

        var terms = new List<ScoreTerm>(measured.Terms)
        {
            new("Non comparable aux cartes", 0,
                $"aucune méta publique de potions — note plafonnée à {PotionCap}"),
        };

        return new Score(Math.Min(measured.Total, PotionCap), terms, Confidence.Unknown);
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
    /// « Malédiction … » du score de carte — posé par <c>CurseScorer</c> depuis le catalogue
    /// du jeu et l'impact mesuré (docs/35).
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

            var term = _cards.ScoreCard(card, ctx).Terms
                .FirstOrDefault(t => t.Label.StartsWith(CurseTermPrefix, StringComparison.Ordinal));
            if (term != null) found.Add(term.Source);
        }
        return found;
    }

    private static string DescribeCurse(List<string> curses) =>
        curses.Count == 1 ? curses[0] : $"{curses[0]} (+{curses.Count - 1} autre(s))";
}
