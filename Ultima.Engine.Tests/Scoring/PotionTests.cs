using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Potions;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Contrat v1.1 / S6bis : score des potions possédées. Aucune méta publique → score dérivé de
/// l'effet décrit dans le jeu et contextualisé (PV manquants, boss, acte).
/// </summary>
public sealed class PotionTests
{
    private static ScoringContext Ctx(int hp, int maxHp, string? boss = null, int act = 0) => new(
        "CHARACTER.SILENT", act, boss, 0, 5,
        Array.Empty<CardInstance>(), Array.Empty<string>(), 99, hp, maxHp);

    [Fact]
    public void Catalog_loads_the_generated_potions()
    {
        var catalog = PotionCatalog.Load();
        Assert.True(catalog.Count >= 60, $"potions chargées: {catalog.Count}");
        var blood = catalog.Get("BLOOD_POTION");
        Assert.NotNull(blood);
        Assert.Contains("heal", blood!.Tags);
        Assert.False(string.IsNullOrWhiteSpace(blood.NameFr));
    }

    [Fact]
    public void Heal_potion_is_worth_more_when_hurt()
    {
        var scorer = new PotionScorer(PotionCatalog.Load());
        var low = scorer.Score("BLOOD_POTION", Ctx(15, 70));
        var full = scorer.Score("BLOOD_POTION", Ctx(70, 70));

        Assert.NotNull(low);
        Assert.NotNull(full);
        Assert.True(low!.Total > full!.Total, $"blessé {low.Total} vs plein {full.Total}");
    }

    [Fact]
    public void Damage_potion_is_worth_more_against_a_known_boss()
    {
        var scorer = new PotionScorer(PotionCatalog.Load());
        var withBoss = scorer.Score("FIRE_POTION", Ctx(70, 70, "ENCOUNTER.WATERFALL_GIANT_BOSS", 1));
        var without = scorer.Score("FIRE_POTION", Ctx(70, 70, null, 1));

        Assert.NotNull(withBoss);
        Assert.NotNull(without);
        Assert.True(withBoss!.Total > without!.Total);
    }

    [Fact]
    public void Revive_potion_dominates_at_low_hp()
    {
        var scorer = new PotionScorer(PotionCatalog.Load());
        var low = scorer.Score("FAIRY_IN_A_BOTTLE", Ctx(10, 70));
        var full = scorer.Score("FAIRY_IN_A_BOTTLE", Ctx(70, 70));
        Assert.NotNull(low);
        Assert.NotNull(full);
        Assert.True(low!.Total > full!.Total);
    }

    [Fact]
    public void Unknown_potion_returns_null_never_a_made_up_score()
    {
        var scorer = new PotionScorer(PotionCatalog.Load());
        Assert.Null(scorer.Score("NOT_A_POTION", Ctx(70, 70)));
        Assert.Null(scorer.Score("", Ctx(70, 70)));
    }

    [Fact]
    public void Every_catalogued_potion_scores_in_range_with_terms()
    {
        var catalog = PotionCatalog.Load();
        var scorer = new PotionScorer(catalog);
        var ctx = Ctx(30, 70, "ENCOUNTER.WATERFALL_GIANT_BOSS", 1);

        int scored = 0;
        foreach (var id in new[] { "ASHWATER", "ATTACK_POTION", "BLOCK_POTION", "FIRE_POTION", "POISON_POTION",
                                   "STRENGTH_POTION", "SWIFT_POTION", "ENERGY_POTION", "FRUIT_JUICE", "FAIRY_IN_A_BOTTLE" })
        {
            var score = scorer.Score(id, ctx);
            Assert.NotNull(score);
            Assert.InRange(score!.Total, 0, 100);
            Assert.NotEmpty(score.Terms);
            Assert.All(score.Terms, t => Assert.False(string.IsNullOrWhiteSpace(t.Source)));
            scored++;
        }
        Assert.Equal(10, scored);
    }

    [Fact]
    public void Facade_exposes_potion_scoring()
    {
        var scorer = new Scorer();
        var viaFacade = scorer.ScorePotion("FIRE_POTION", Ctx(70, 70, "ENCOUNTER.X_BOSS", 1));
        var direct = new PotionScorer(PotionCatalog.Load()).Score("FIRE_POTION", Ctx(70, 70, "ENCOUNTER.X_BOSS", 1));

        Assert.NotNull(viaFacade);
        Assert.NotNull(direct);
        Assert.Equal(direct!.Total, viaFacade!.Total);
    }
}
