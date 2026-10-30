using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Engine.Scoring.Relics;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Conversion des tags de reliques (revue du chef, point 2) : 304 reliques taguées, mais le scorer
/// n'en lisait que 7 → 163 muettes. Une relique sans note communautaire reçoit désormais une note
/// de repli bornée, explicable, plafonnée à B.
/// </summary>
public sealed class RelicTagTests
{
    private static ScoringContext Ctx() => new(
        "CHARACTER.SILENT", 0, null, 0, 8,
        new[] { new CardInstance("CARD.STRIKE_SILENT", 0, null, null) },
        Array.Empty<string>(), 99, 70, 70);

    /// <summary>Méta qui connaît une relique sans lui donner de note (cas des 163 muettes).</summary>
    private sealed class NoConsensusMeta : IMetaTable
    {
        private readonly HashSet<string> _known;

        public NoConsensusMeta(params string[] known) =>
            _known = new HashSet<string>(known.Select(Ids.Bare), StringComparer.OrdinalIgnoreCase);

        public MetaEntry? Card(string id) => null;

        public MetaEntry? Relic(string id) =>
            _known.Contains(Ids.Bare(id))
                ? new MetaEntry(Ids.Bare(id), Ids.Bare(id), Ids.Bare(id), null, null, null, null, "shared",
                    Array.Empty<SourceRating>(), null, 0, 0)
                : null;

        public double? SkipRate => null;
        public double? SkipElo => null;
        public IReadOnlyList<MetaArchetype> Archetypes(string characterId) => Array.Empty<MetaArchetype>();
        public string Variant => "test";
        public string? Generated => null;
        public string? PatchTarget => "v0.107.1";
    }

    [Fact]
    public void Relic_without_meta_gets_a_bounded_tag_score_instead_of_zero()
    {
        var scorer = new Scorer(new NoConsensusMeta("ANCHOR"), PersonalStats.Empty());

        var score = scorer.ScoreRelic("RELIC.ANCHOR", Ctx());

        Assert.True(score.Total > 0, $"Ancre doit sortir du 0 muet, vu {score.Total}");
        Assert.InRange(score.Total, 1, RelicTagValues.MaxTagScore);
        Assert.Equal(Confidence.Low, score.Confidence);
        var effect = Assert.Single(score.Terms, t => t.Label.StartsWith("Effet :", StringComparison.Ordinal));
        Assert.Equal(3, effect.Points);                       // tag « block »
        Assert.False(string.IsNullOrWhiteSpace(effect.Source));
    }

    [Fact]
    public void No_effect_relic_stays_zero_and_says_why()
    {
        var scorer = new Scorer(new NoConsensusMeta("CIRCLET"), PersonalStats.Empty());

        var score = scorer.ScoreRelic("RELIC.CIRCLET", Ctx());

        Assert.Equal(0, score.Total);
        Assert.Contains(score.Terms, t => t.Label == "Pas de note chiffrée");
    }

    [Fact]
    public void Revive_weighs_more_than_energy()
    {
        var lizard = RelicTagValues.Evaluate(new RelicEntry("LIZARD_TAIL", "Queue de lézard",
            "Lorsque vos PV sont réduits à 0, récupérez des PV.", new[] { "maxhp", "heal", "revive" }));
        var bread = RelicTagValues.Evaluate(new RelicEntry("BREAD", "Pain",
            "Au début de votre premier tour, gagnez de l'énergie.", new[] { "energy" }));

        Assert.True(lizard.Total > bread.Total, $"{lizard.Total} > {bread.Total}");
        Assert.Equal(17, lizard.Total);   // 6 (PV max) + 3 (soin) + 8 (résurrection)
    }

    [Fact]
    public void Tag_score_is_capped_at_B_even_with_many_effects()
    {
        // 58 points de tags avant plafond : 8+6+5+4+4+4+4+4+3+3+3+3+3+3+3.
        var all = RelicTagValues.Evaluate(new RelicEntry("TEST", "Test", "Tout à la fois.",
            new[]
            {
                "revive", "maxhp", "damage", "strength", "energy", "draw", "mitigation", "debuff",
                "block", "heal", "gold", "upgrade", "remove", "transform", "aoe",
            }));

        Assert.Equal(RelicTagValues.MaxTagScore, all.Total);
        Assert.Contains(all.Terms, t => t.Label == "Plafond des tags");
    }

    [Fact]
    public void Negative_tag_cannot_make_the_score_negative()
    {
        var penalty = RelicTagValues.Evaluate(new RelicEntry("TEST", "Test", "Effet négatif.",
            new[] { "penalty" }));

        Assert.Equal(0, penalty.Total);
        Assert.InRange(penalty.Total, 0, 100);
    }

    [Fact]
    public void Measured_relic_keeps_its_community_score()
    {
        // ANCHOR a un consensus réel dans data/meta : le barème de tags ne doit pas s'y substituer.
        var score = new Scorer().ScoreRelic("RELIC.ANCHOR", Ctx());

        Assert.Contains(score.Terms, t => t.Label == "Méta communautaire");
        Assert.DoesNotContain(score.Terms, t => t.Label.StartsWith("Effet :", StringComparison.Ordinal));
    }
}
