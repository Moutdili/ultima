using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Cards;

/// <summary>
/// Score d'une carte en récompense (0-100, explicable) et score de l'option « passer ».
/// Version de base : méta communautaire + enchantement + malédiction + perso.
/// TODO agent S3 : synergie, adéquation boss, courbe, confiance affinée + tests.
/// </summary>
public sealed class CardScorer
{
    private readonly IMetaTable _meta;
    private readonly IPersonalStats _personal;
    private readonly SynergyEngine _synergy;

    public CardScorer(IMetaTable meta, IPersonalStats personal, SynergyEngine synergy)
    {
        _meta = meta;
        _personal = personal;
        _synergy = synergy;
    }

    public Score ScoreCard(CardInstance card, ScoringContext ctx)
    {
        var b = new ScoreBuilder();
        var meta = _meta.Card(card.Id);

        if (meta?.Consensus is double consensus)
        {
            b.Add("Méta communautaire", (int)Math.Round(consensus * 0.60, MidpointRounding.AwayFromZero),
                SourceLabel(meta));
        }
        else
        {
            b.Add("Pas de note communautaire", 0, "data/meta");
        }

        if (card.Enchantment != null)
            b.Add($"Enchantement {card.Enchantment.Id}", 3 + Math.Min(2, card.Enchantment.Amount), "enchantement");

        if (IsCurse(meta))
            b.Add("Malédiction", -30, meta!.DisplayName);

        var stats = _personal.Card(card.Id);
        if (stats?.WinRateDelta is double delta)
        {
            b.Add("Ton historique", ScoreMath.Clamp(ScoreMath.Round(delta * 40), -8, 8),
                $"tes {stats.RunsWithCard + stats.RunsWithoutCard} runs");
        }

        b.AddRange(_synergy.Terms(card, ctx));

        return b.Build(Confidence.Of(meta?.SourceCount ?? 0, _personal.RunCount));
    }

    public Score ScoreSkip(IReadOnlyList<CardInstance> offered, ScoringContext ctx)
    {
        var b = new ScoreBuilder();
        double? skip = _meta.SkipRate;
        if (skip.HasValue)
            b.Add("Taux de passage communautaire", (int)Math.Round(skip.Value * 60, MidpointRounding.AwayFromZero),
                "Spire Codex (toutes versions)");

        int best = 0;
        foreach (var card in offered)
            best = Math.Max(best, ScoreCard(card, ctx).Total);

        b.Add("Meilleure carte offerte", -(int)Math.Round(best * 0.35, MidpointRounding.AwayFromZero),
            "comparaison des cartes proposées");

        return b.Build(Confidence.Of(_meta.SkipRate.HasValue ? 1 : 0, _personal.RunCount));
    }

    internal static bool IsCurse(MetaEntry? meta) =>
        meta != null && ((meta.Type ?? "").Contains("Curse", StringComparison.OrdinalIgnoreCase)
                         || (meta.Rarity ?? "").Contains("Curse", StringComparison.OrdinalIgnoreCase));

    internal static string SourceLabel(MetaEntry meta)
    {
        var parts = meta.Ratings.Select(r => r.SourceId).Distinct().Take(2).ToArray();
        var who = parts.Length > 0 ? string.Join(" + ", parts) : "méta";
        return $"{who} (n={meta.SourceCount} source(s), écart {meta.Spread:0.#})";
    }
}
