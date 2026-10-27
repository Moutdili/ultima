using Ultima.Core.Damage;
using Xunit;

namespace Ultima.Tests;

public class DamageMathTests
{
    [Fact]
    public void Weak_Multiplier_Truncates_Like_The_Game()
    {
        // 17 * 0.75 = 12.75 -> le jeu tronque a 12
        Assert.Equal(12, DamageMath.ApplyMultiplier(17, 0.75m));
        Assert.Equal(12, DamageMath.ApplyMultiplier(16, 0.75m));
    }

    [Fact]
    public void Multipliers_Combine()
    {
        // 16 * 0.75 * 1.5 = 18
        Assert.Equal(18, DamageMath.ApplyAll(16, 0.75m, 1.5m));
    }

    [Fact]
    public void Sequence_Total_Never_Negative()
    {
        Assert.Equal(0, DamageMath.SequenceTotal(6, 2, 99));
        Assert.Equal(12, DamageMath.SequenceTotal(6, 2, 0));
        Assert.Equal(2, DamageMath.SequenceTotal(6, 2, 10));
    }
}
