using Xunit;
using Ultima.Engine.Contracts;

namespace Ultima.Engine.Tests;

public class ContractTests
{
    [Fact]
    public void Default_settings_show_curses_first_and_all_modules()
    {
        var m = OverlaySettings.Default.Modules;
        Assert.Equal(ModuleIds.Curses, m.OrderBy(x => x.Order).First().Id);
        Assert.All(m, x => Assert.True(x.Enabled));
        Assert.Equal(8, m.Select(x => x.Id).Distinct().Count());
    }
}
