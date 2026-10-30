using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Treasures;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Trésors potentiels (docs/48) : pools réels du jeu (rareté déclarée dans le code de chaque
/// relique) et classement par utilité pour le deck courant.
/// </summary>
public sealed class TreasureIndexTests
{
    private static ScoringContext Ctx() => new(
        "CHARACTER.SILENT", 0, null, 0, 6,
        new[]
        {
            new CardInstance("CARD.STRIKE_SILENT", 0, null, null),
            new CardInstance("CARD.STRIKE_SILENT", 0, null, null),
            new CardInstance("CARD.DEFEND_SILENT", 0, null, null),
            new CardInstance("CARD.DEFEND_SILENT", 0, null, null),
            new CardInstance("CARD.BIG_BANG", 0, null, null),
        },
        Array.Empty<string>(), 99, 70, 70);

    [Fact]
    public void Pools_come_from_declared_rarity()
    {
        var index = TreasureIndex.Default;

        Assert.True(index.IsLoaded, index.Problem);
        Assert.True(index.Ids(TreasurePool.Common).Count >= 25, $"Common : {index.Ids(TreasurePool.Common).Count}");
        Assert.True(index.Ids(TreasurePool.Rare).Count >= 40, $"Rare : {index.Ids(TreasurePool.Rare).Count}");
        Assert.True(index.Ids(TreasurePool.Shop).Count >= 20, $"Shop : {index.Ids(TreasurePool.Shop).Count}");
        Assert.True(index.Ids(TreasurePool.Ancient).Count >= 50, $"Ancient : {index.Ids(TreasurePool.Ancient).Count}");

        // Aucune relique sans rareté déclarée n'entre dans un pool.
        Assert.Empty(index.Ids(TreasurePool.Starter).Where(id => id.Contains("VAKUU")));
    }

    [Fact]
    public void Known_relics_land_in_the_right_pool()
    {
        var index = TreasureIndex.Default;

        Assert.Equal(TreasurePool.Uncommon, index.PoolOf("RELIC.AKABEKO"));
        Assert.Equal(TreasurePool.Common, index.PoolOf("RELIC.BLOOD_VIAL"));
    }

    [Fact]
    public void Ranking_is_sorted_and_bounded()
    {
        var index = TreasureIndex.Default;

        var top = index.Rank(Ctx(), TreasurePool.Common, 5);

        Assert.Equal(5, top.Count);
        Assert.All(top, c => Assert.Equal(TreasurePool.Common, c.Pool));
        Assert.All(top, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
        for (var i = 1; i < top.Count; i++)
            Assert.True(top[i - 1].Score >= top[i].Score, "le classement doit être décroissant");
    }

    [Fact]
    public void Rank_all_excludes_the_starting_set()
    {
        var index = TreasureIndex.Default;

        var top = index.RankAll(Ctx(), 12);

        Assert.Equal(12, top.Count);
        Assert.DoesNotContain(top, c => c.Pool == TreasurePool.Starter);
        for (var i = 1; i < top.Count; i++)
            Assert.True(top[i - 1].Score >= top[i].Score);
    }

    [Fact]
    public void Same_context_gives_the_same_order()
    {
        var index = TreasureIndex.Default;

        var first = index.Rank(Ctx(), TreasurePool.Rare, 8).Select(c => c.RelicId).ToArray();
        var second = index.Rank(Ctx(), TreasurePool.Rare, 8).Select(c => c.RelicId).ToArray();

        Assert.Equal(first, second);
    }
}
