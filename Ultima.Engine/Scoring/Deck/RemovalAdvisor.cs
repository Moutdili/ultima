using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Curses;
using Ultima.Engine.Scoring.Keywords;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Strength;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Deck;

/// <summary>
/// Une carte à retirer, classée par ce qu'elle coûte au deck.
/// <see cref="DeckGain"/> = <c>G(deck − carte) − G(deck)</c> en points de G ; <see cref="CurseBonus"/>
/// ajoute la toxicité mesurée pour les malédictions ; <see cref="Score"/> = la somme des deux, sur
/// laquelle se fait le classement. <see cref="WorthIt"/> compare ce gain au prix réel
/// (75 or, ou 100 à A6+ <c>Inflation</c>).
/// </summary>
public sealed record RemovalCandidate(
    string CardId,
    string Name,
    int UpgradeLevel,
    int Copies,
    bool IsCurse,
    int Toxicity,
    int DeckGain,
    int CurseBonus,
    int Score,
    int Price,
    bool Affordable,
    bool WorthIt,
    string Reason)
{
    /// <summary>Gain minimal qui rentabilise le prix : <c>prix / 12</c> (6,25 à 75 or, 8,33 à 100).</summary>
    public double BreakEven => RemovalAdvisor.BreakEvenGain(Price);
}

/// <summary>
/// Conseiller de retrait : classe les cartes du deck par <b>gain réel de G après retrait</b>, et
/// compare ce gain au prix du retrait au marchand.
///
/// <para>
/// <b>Pourquoi ΔG et pas la note de carte.</b> La note d'une carte dit ce qu'elle vaut dans
/// l'abstrait ; ce qui décide d'un retrait, c'est ce que le deck perd quand elle part. Une Frappe
/// dans un deck qui a déjà mieux vaut moins qu'une carte de bloc qui comble un trou. On calcule
/// donc <c>G(deck − carte) − G(deck)</c> avec le même modèle de débit que le « Fit au deck »
/// (docs/27 §3 L1) : un seul G dans tout le dépôt.
/// </para>
///
/// <para>
/// <b>Malédictions.</b> G ne les pénalise que de 6 points de consistance, pondérés par le poids de
/// la dimension (≈1 point de G) : c'est très en dessous de l'effet mesuré (−9,5 pp de victoire pour
/// Poor Sleep, docs/35). On ajoute donc <c>toxicité / 10</c> points — 10 pour Poor Sleep, 5 pour
/// Clumsy — pour les malédictions et les statuts. Sans ce terme, une Frappe et une malédiction
/// mortelle sortiraient à égalité.
/// </para>
///
/// <para>
/// <b>Prix et seuil.</b> 75 or au marchand, <b>100 à A6+</b> (ascension <c>Inflation</c>,
/// docs/34-paliers-ascension.md). Le seuil de rentabilité est explicite :
/// <c>Score ≥ prix / 12</c> — 1 point de G vaut 12 or dans cet arbitrage, donc 6,25 points à 75 or
/// et 8,33 à 100. En dessous, l'or est mieux dépensé ailleurs (une relique à 150 or vaut ~12
/// points de G dans le même barème).
/// </para>
///
/// <para>
/// <b>Périmètre.</b> Ce conseiller classe les cartes <i>du deck</i>. Le score de l'emplacement de
/// retrait lui-même reste celui de <c>ShopScorer.ScoreRemoval</c> (formule figée docs/24 §2).
/// </para>
/// </summary>
public sealed class RemovalAdvisor
{
    /// <summary>Prix du retrait au marchand hors Inflation.</summary>
    public const int BasePrice = 75;

    /// <summary>Prix du retrait à A6+ (ascension « Inflation », docs/34).</summary>
    public const int InflationPrice = 100;

    /// <summary>Palier d'ascension qui active Inflation.</summary>
    public const int InflationAscension = 6;

    /// <summary>
    /// Or par point de G. Seuil de rentabilité : <c>gain ≥ prix / 12</c>. Un retrait de base
    /// (75 or) doit donc rendre 6,25 points de G, un retrait à A6 (100 or) 8,33.
    /// </summary>
    public const double PointsPerGold = 12.0;

    /// <summary>
    /// Points de G ajoutés par point de toxicité divisé par 10 : la toxicité 0..100 devient un
    /// bonus de classement 0..10. Voir la remarque sur les malédictions ci-dessus.
    /// </summary>
    public const double ToxicityScale = 10.0;

    private static readonly Lazy<PlayerStrengthCalculator> LazyStrength =
        new(() => new PlayerStrengthCalculator(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly PlayerStrengthCalculator _strength;
    private readonly CurseScorer _curses;
    private readonly CardCatalog _cards;
    private readonly IMetaTable? _meta;
    private readonly CardEffects _effects;
    private readonly KeywordCatalog _keywords;

    public RemovalAdvisor(PlayerStrengthCalculator? strength = null, CurseScorer? curses = null,
        CardCatalog? cards = null, IMetaTable? meta = null, CardEffects? effects = null,
        KeywordCatalog? keywords = null)
    {
        _strength = strength ?? LazyStrength.Value;
        _curses = curses ?? CurseScorer.Default;
        _cards = cards ?? CardCatalog.Default;
        _meta = meta;
        _effects = effects ?? CardEffects.Default;
        _keywords = keywords ?? KeywordCatalog.Default;
    }

    /// <summary>
    /// Prix du retrait pour un palier d'ascension : 75 or, 100 à partir de A6 (<c>Inflation</c>).
    /// </summary>
    public static int PriceFor(int ascension) =>
        ascension >= InflationAscension ? InflationPrice : BasePrice;

    /// <summary>Gain de G nécessaire pour que le retrait vaille son prix.</summary>
    public static double BreakEvenGain(int price) => Math.Max(0, price) / PointsPerGold;

    /// <summary>
    /// Classe les cartes du deck par intérêt à les retirer, du plus rentable au moins rentable.
    /// Prix et or lus dans le contexte (A6+ ⇒ 100 or).
    /// </summary>
    public IReadOnlyList<RemovalCandidate> Recommend(ScoringContext ctx) =>
        Recommend(ctx, PriceFor(ctx.Ascension), ctx.Gold);

    /// <summary>
    /// Idem avec un prix et un or explicites (le miroir connaît le prix affiché, remises
    /// comprises : on ne réinvente pas la remise ici).
    /// </summary>
    public IReadOnlyList<RemovalCandidate> Recommend(ScoringContext ctx, int price, int gold)
    {
        var deck = ctx.Deck ?? Array.Empty<CardInstance>();
        if (deck.Count == 0) return Array.Empty<RemovalCandidate>();

        int p = Math.Max(0, price);
        int purse = Math.Max(0, gold);
        double breakEven = BreakEvenGain(p);
        int before = _strength.Compute(ctx).Total;

        var candidates = new List<RemovalCandidate>();
        foreach (var group in Copies(deck))
        {
            var without = Without(deck, group.Key);
            int gain = _strength.Compute(ctx with { Deck = without }).Total - before;

            var verdict = _curses.Classify(group.Card.Id, _meta);
            bool curse = verdict.Kind != CurseKind.None;
            int toxicity = verdict.Measured?.Toxicity ?? 0;
            int bonus = curse ? ScoreMath.Round(toxicity / ToxicityScale) : 0;
            int score = gain + bonus;

            // Eternal : le jeu refuse le retrait (CardModel.IsRemovable). On le dit, et on ne
            // recommande pas une action impossible.
            bool eternal = _keywords.Flags(_effects.Get(group.Card.Id)?.Keywords).Unremovable;

            candidates.Add(new RemovalCandidate(
                group.Card.Id,
                Name(group.Card),
                group.Card.UpgradeLevel,
                group.Count,
                curse,
                toxicity,
                gain,
                bonus,
                score,
                p,
                p <= purse,
                score >= breakEven && !eternal,
                Reason(group.Card, group.Count, gain, bonus, toxicity, p, breakEven) +
                    (eternal ? " · Éternel : ne peut pas être retirée du deck (CardModel.IsRemovable)" : "")));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Toxicity)
            .ThenByDescending(c => c.DeckGain)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Phrase chiffrée : ce que le deck gagne, ce que la malédiction ajoute, ce que le retrait coûte
    /// et le seuil à atteindre. Même exigence que les autres termes du moteur : pas de chiffre sans
    /// explication.
    /// </summary>
    private static string Reason(CardInstance card, int copies, int gain, int bonus, int toxicity,
        int price, double breakEven)
    {
        var parts = new List<string>(4)
        {
            $"G(deck − {Ids.Bare(card.Id)}) − G(deck) = {Signed(gain)}",
        };

        if (copies > 1) parts.Add($"{copies} exemplaires identiques (une seule copie retirée)");
        if (bonus != 0) parts.Add($"toxicité mesurée {toxicity}/100 → +{bonus} de classement");
        parts.Add(price == 0 ? "retrait gratuit" : $"prix {price} or");
        parts.Add($"seuil {Num(breakEven)} de gain pour valoir son prix");

        return string.Join(" ; ", parts);
    }

    // --- Groupes de copies identiques -----------------------------------------------------------

    private sealed record CardCopies(GroupKey Key, CardInstance Card, int Count);

    private readonly record struct GroupKey(string Id, int UpgradeLevel, string? Enchantment);

    private static IReadOnlyList<CardCopies> Copies(IReadOnlyList<CardInstance> deck)
    {
        var order = new List<GroupKey>();
        var counts = new Dictionary<GroupKey, int>();
        var first = new Dictionary<GroupKey, CardInstance>();

        foreach (var card in deck)
        {
            var key = Key(card);
            if (!counts.ContainsKey(key)) order.Add(key);
            counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            first.TryAdd(key, card);
        }

        var groups = new List<CardCopies>(order.Count);
        foreach (var key in order) groups.Add(new CardCopies(key, first[key], counts[key]));
        return groups;
    }

    private static GroupKey Key(CardInstance card) =>
        new(Ids.Bare(card.Id).ToUpperInvariant(), card.UpgradeLevel, card.Enchantment?.Id);

    /// <summary>Deck privé d'<b>une</b> copie de la carte décrite par <paramref name="key"/>.</summary>
    private static IReadOnlyList<CardInstance> Without(IReadOnlyList<CardInstance> deck, GroupKey key)
    {
        var result = new List<CardInstance>(deck.Count);
        bool removed = false;

        foreach (var card in deck)
        {
            if (!removed && Key(card) == key)
            {
                removed = true;
                continue;
            }
            result.Add(card);
        }

        return result;
    }

    private string Name(CardInstance card)
    {
        var meta = _meta?.Card(card.Id);
        if (meta is not null && !string.IsNullOrWhiteSpace(meta.DisplayName)) return meta.DisplayName;

        var entry = _cards.Get(card.Id);
        if (entry is not null && !string.IsNullOrWhiteSpace(entry.DisplayName)) return entry.DisplayName;

        return Ids.Bare(card.Id);
    }

    private static string Signed(int value) =>
        value > 0 ? $"+{value}" : value < 0 ? $"−{Math.Abs(value)}" : "0";

    private static string Num(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
}
