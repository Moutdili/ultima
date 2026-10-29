using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Deck;

/// <summary>
/// Note du deck (0-100) face aux reliques et au boss de l'acte, archétype probable et potentiels,
/// malédictions listées. Formule figée (docs/24-hermes-arch.md §2) :
/// <code>
/// note = moyenne des ScoreCard × 0.8
///      + (−8 par malédiction)
///      + adéquation boss (±10)
///      + reliques (0..5)
///      + correspondance d'archétype (0..8)
/// </code>
/// Chaque terme est nommé et sourcé ; un terme à 0 point n'est pas émis. Aucun nombre inventé :
/// quand la donnée manque (boss non résolu, relique non notée, aucun archétype reconnaissable),
/// le terme est omis plutôt que remplacé par une valeur arbitraire.
/// </summary>
public sealed class DeckEvaluator
{
    // --- Pondérations figées (docs/24 §2). Ne pas ajouter d'autres nombres sans mise à jour du doc.
    /// <summary>Part de la moyenne des notes de cartes dans la note du deck.</summary>
    public const double CardAverageWeight = 0.8;

    /// <summary>Pénalité par malédiction.</summary>
    public const int CursePenalty = -8;

    /// <summary>Bornes de l'adéquation boss.</summary>
    public const int BossFitMin = -10, BossFitMax = 10;

    /// <summary>Plafond du terme reliques.</summary>
    public const int RelicFitMax = 5;

    /// <summary>Plafond du terme de correspondance d'archétype.</summary>
    public const int ArchetypeFitMax = 8;

    // --- Règle interne d'adéquation boss (toujours bornée par BossFitMin/BossFitMax).
    /// <summary>Points par unité d'écart entre marge réelle et besoin du boss.</summary>
    public const double BossFitScale = 20;

    /// <summary>Poids de la marge de PV dans l'adéquation boss.</summary>
    public const double HpMarginWeight = 0.5;

    /// <summary>Poids de la marge de garde (part Skill/Power) dans l'adéquation boss.</summary>
    public const double GuardMarginWeight = 0.5;

    private readonly IMetaTable _meta;
    private readonly CardScorer _cards;

    public DeckEvaluator(IMetaTable meta, CardScorer cards)
    {
        _meta = meta ?? throw new ArgumentNullException(nameof(meta));
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
    }

    public DeckEvaluation Evaluate(ScoringContext ctx)
    {
        var b = new ScoreBuilder();
        var deck = ctx.Deck ?? Array.Empty<CardInstance>();

        // Deck vide : note basse (0) et confiance explicite, jamais d'exception.
        if (deck.Count == 0)
            return new DeckEvaluation(b.Build(Confidence.Low), null, Array.Empty<string>(), Array.Empty<CardView>());

        var catalog = CardCatalog.Default;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in deck)
        {
            var id = Ids.Bare(card.Id);
            if (id.Length > 0) counts[id] = (counts.TryGetValue(id, out var n) ? n : 0) + 1;
        }

        double sum = 0;
        int rated = 0, guard = 0;
        var curses = new List<CardView>();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var card in deck)
        {
            sum += _cards.ScoreCard(card, ctx).Total;

            var meta = _meta.Card(card.Id);
            if (meta?.Consensus is not null)
            {
                rated++;
                foreach (var rating in meta.Ratings) sources.Add(rating.SourceId);
            }

            var entry = catalog.Get(card.Id);
            if (IsGuard(entry, meta)) guard++;

            if (IsCurse(meta, entry)) curses.Add(ToCurseView(card, meta, entry));
        }

        // 1) Socle : moyenne des notes de cartes (0..100) ramenée à 0..80.
        int average = ScoreMath.Round(sum / deck.Count * CardAverageWeight);
        b.Add("Note moyenne des cartes", ScoreMath.Clamp(average, 0, 100),
            $"{deck.Count} cartes, dont {rated} notées par la méta");

        // 2) Malédictions : terme négatif nommé, toujours émis s'il y en a (docs/22 : très visibles).
        if (curses.Count > 0)
        {
            b.Add($"Malédictions ({curses.Count})", CursePenalty * curses.Count,
                string.Join(", ", curses.Select(c => c.Name)));
        }

        // 3) Adéquation boss (±10) — seulement si le boss se résout dans le catalogue du jeu.
        var boss = BossCatalog.Default.Resolve(ctx.BossId);
        if (boss is not null && BossFit(boss, guard, deck.Count, ctx) is { } fit)
        {
            b.Add($"Adéquation boss ({boss.DisplayName})", fit,
                $"data/catalog : plus gros coup {boss.BiggestHit} dgt" +
                (boss.Hp is { } hp ? $", {hp} PV" : "") +
                $", garde {Pct(guard, deck.Count)}, besoin {Pct(boss.BiggestHit, ctx.MaxHp)} des PV max");
        }

        // 4) Reliques possédées (0..5) : moyenne de leur note communautaire.
        if (RelicFit(ctx.RelicIds) is { } relicPoints)
            b.Add("Reliques possédées", relicPoints,
                $"moyenne des notes communautaires ({ctx.RelicIds!.Count} reliques)");

        // 5) Correspondance d'archétype (0..8) + archétype probable et potentiels.
        var ranked = ArchetypeMatcher.Rank(_meta.Archetypes(ctx.CharacterId), counts, ctx.RelicIds ?? Array.Empty<string>());
        var chosen = ArchetypeMatcher.Choose(ranked);
        var potential = ArchetypeMatcher.Potential(ranked);

        if (chosen is not null)
        {
            int points = ScoreMath.Clamp(ScoreMath.Round(chosen.Match * ArchetypeFitMax), 0, ArchetypeFitMax);
            if (points > 0)
                b.Add($"Correspondance d'archétype ({chosen.Name})", points,
                    $"data/meta/archetypes.json : {chosen.Describe()}");
        }

        return new DeckEvaluation(b.Build(Confidence.Of(sources.Count, 0)), chosen?.Name, potential, curses);
    }

    // --- Adéquation boss -----------------------------------------------------------------------

    /// <summary>
    /// Adéquation du deck au boss de l'acte, bornée à ±10, **indépendante des PV courants** :
    /// une note de deck qui bouge quand on prend des dégâts n'évalue plus le deck. On compare
    /// seulement la part de garde (Skill/Power) au besoin créé par le plus gros coup du boss
    /// rapporté aux PV max. Null si le boss n'a pas de coup connu — aucune valeur inventée.
    /// </summary>
    private static int? BossFit(BossProfile boss, int guard, int deckCount, ScoringContext ctx)
    {
        if (boss.BiggestHit <= 0 || ctx.MaxHp <= 0 || deckCount <= 0) return null;

        double need = Math.Min(1.0, (double)boss.BiggestHit / ctx.MaxHp);
        double guardShare = (double)guard / deckCount;

        return ScoreMath.Clamp(
            ScoreMath.Round((guardShare - need) * BossFitScale), BossFitMin, BossFitMax);
    }

    // --- Reliques ------------------------------------------------------------------------------

    /// <summary>0..5 depuis la moyenne des notes communautaires ; null si aucune relique n'est notée.</summary>
    private int? RelicFit(IReadOnlyList<string>? relicIds)
    {
        if (relicIds is null || relicIds.Count == 0) return null;

        double sum = 0;
        int n = 0;
        foreach (var id in relicIds)
        {
            var consensus = _meta.Relic(id)?.Consensus;
            if (consensus is not > 0) continue;
            sum += consensus.Value;
            n++;
        }

        if (n == 0) return null;
        int points = ScoreMath.Clamp(ScoreMath.Round(sum / n / 100.0 * RelicFitMax), 0, RelicFitMax);
        return points > 0 ? points : null;
    }

    // --- Cartes --------------------------------------------------------------------------------

    /// <summary>Carte de garde : Skill ou Power (type du catalogue, sinon de la méta).</summary>
    private static bool IsGuard(CatalogCard? entry, MetaEntry? meta)
    {
        var type = entry?.Type ?? meta?.Type;
        return type is not null &&
               (type.Equals("Skill", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("Power", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Malédiction : type/rareté « Curse » de la méta <b>ou</b> du catalogue du jeu — la méta
    /// communautaire ne note pas les malédictions, le catalogue les connaît toutes.
    /// </summary>
    public static bool IsCurse(MetaEntry? meta, CatalogCard? catalogEntry) =>
        (meta is not null &&
         ((meta.Type ?? "").Contains("Curse", StringComparison.OrdinalIgnoreCase) ||
          (meta.Rarity ?? "").Contains("Curse", StringComparison.OrdinalIgnoreCase)))
        || catalogEntry?.IsCurse == true;

    /// <summary>Vue de deck d'une malédiction (module « malédictions très visibles », docs/22).</summary>
    private static CardView ToCurseView(CardInstance card, MetaEntry? meta, CatalogCard? entry) =>
        new(card.Id,
            meta?.DisplayName ?? (string.IsNullOrWhiteSpace(entry?.DisplayName) ? Ids.Bare(card.Id) : entry!.DisplayName),
            meta?.Type ?? entry?.Type ?? "Curse",
            meta?.Rarity ?? entry?.Rarity ?? "Curse",
            meta?.Cost ?? entry?.Cost ?? 0,
            card.UpgradeLevel,
            null,
            true,
            null);

    private static string Pct(int part, int total) =>
        total <= 0 ? "0 %" : $"{Math.Round(100.0 * part / total)} %";
}
