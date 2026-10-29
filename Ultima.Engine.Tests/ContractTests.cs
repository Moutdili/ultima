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
        Assert.Equal(9, m.Select(x => x.Id).Distinct().Count());
    }
}
