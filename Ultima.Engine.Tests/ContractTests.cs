using Xunit;
using Ultima.Engine.Contracts;

namespace Ultima.Engine.Tests;

public class ContractTests
{
    [Fact]
    public void Default_settings_follow_Mio_order_deck_first()
    {
        var m = OverlaySettings.Default.Modules;
        Assert.Equal(new[] { ModuleIds.Deck, ModuleIds.Enchanted, ModuleIds.Curses, ModuleIds.Relics, ModuleIds.Potions },
            m.OrderBy(x => x.Order).Take(5).Select(x => x.Id).ToArray());
        Assert.All(m, x => Assert.True(x.Enabled));
        Assert.Equal(12, m.Select(x => x.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(100, "S"), InlineData(85, "S"), InlineData(84, "A"), InlineData(70, "A"), InlineData(55, "B"),
     InlineData(40, "C"), InlineData(25, "D"), InlineData(24, "F"), InlineData(0, "F")]
    public void Grade_scale_S_to_F(int total, string grade)
        => Assert.Equal(grade, new Score(total, Array.Empty<ScoreTerm>(), "high").Grade);
}
