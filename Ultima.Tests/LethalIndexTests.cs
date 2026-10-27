using Ultima.Core.Damage;
using Xunit;

namespace Ultima.Tests;

/// <summary>
/// Cas de référence du brief. Ces tests sont le contrat : s'ils cassent, l'UI ment.
/// Arithmétique vérifiée à la main dans chaque commentaire.
/// </summary>
public class LethalIndexTests
{
    [Fact]
    public void ThirdHitKills_16x3_on_35hp()
    {
        // 35 - 16 - 16 - 16 = -13 -> le 3e coup tue, overkill 13
        var r = LethalIndex.Compute(perHit: 16, repeats: 3, block: 0, hp: 35);
        Assert.True(r.IsLethal);
        Assert.Equal(3, r.LethalAt);
        Assert.Equal(-13, r.HpAfter);
        Assert.Equal(13, r.Overkill);
    }

    [Fact]
    public void SecondHitKills_With_20_block_and_12hp()
    {
        // block 20: coup 1 absorbe 16 (reste 4), coup 2 absorbe 4 -> 12 PV perdus -> 0 PV
        var r = LethalIndex.Compute(perHit: 16, repeats: 3, block: 20, hp: 12);
        Assert.Equal(2, r.LethalAt);
        Assert.Equal(16, r.Hits[0].Absorbed);
        Assert.Equal(4, r.Hits[1].Absorbed);
        Assert.Equal(12, r.Hits[1].ToHp);
        Assert.Equal(0, r.Hits[1].HpAfter);   // mort au 2e coup
        Assert.Equal(-16, r.HpAfter);         // la sequence continue: 3e coup = 16 overkill
    }

    [Fact]
    public void NotLethal_When_Block_Absorbs_Enough()
    {
        // 48 dégâts vs 20 block + 35 PV -> 28 sur 35 PV, il reste 7
        var r = LethalIndex.Compute(perHit: 16, repeats: 3, block: 20, hp: 35);
        Assert.False(r.IsLethal);
        Assert.Equal(7, r.HpAfter);
        Assert.Equal(0, r.BlockAfter);
    }

    [Fact]
    public void FullyBlocked_NoLethal()
    {
        var r = LethalIndex.Compute(perHit: 16, repeats: 3, block: 48, hp: 35);
        Assert.False(r.IsLethal);
        Assert.True(r.FullyBlocked);
        Assert.Equal(35, r.HpAfter);
        Assert.Equal(0, r.BlockAfter);
    }

    [Fact]
    public void ZeroDamage_NoLethal()
    {
        var r = LethalIndex.Compute(perHit: 0, repeats: 5, block: 0, hp: 10);
        Assert.False(r.IsLethal);
    }

    [Fact]
    public void NoRepeats_NoLethal()
    {
        var r = LethalIndex.Compute(perHit: 99, repeats: 0, block: 0, hp: 10);
        Assert.False(r.IsLethal);
        Assert.Equal(10, r.HpAfter);
    }

    [Theory]
    [InlineData(16, 3, 0, 35, "16x3 -> 3e coup LETAL (-13 PV)")]
    [InlineData(16, 3, 48, 35, "16x3 -> tout bloque")]
    [InlineData(6, 2, 0, 40, "6x2 -> 28 PV restants")]
    public void Describe_Is_Stable(int perHit, int repeats, int block, int hp, string expected)
        => Assert.Equal(expected, LethalIndex.Describe(perHit, repeats, block, hp));
}
