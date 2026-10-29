using Ultima.Knowledge.Build;
using Xunit;

namespace Ultima.Knowledge.Tests;

public class ScannerAndIdsTests
{
    [Theory]
    [InlineData("AscendersBane", "ASCENDERS_BANE")]
    [InlineData("StrikeIronclad", "STRIKE_IRONCLAD")]
    [InlineData("TheScythe", "THE_SCYTHE")]
    [InlineData("GlassGolemBoss", "GLASS_GOLEM_BOSS")]
    [InlineData("Ironclad4Epoch", "IRONCLAD4_EPOCH")]
    [InlineData("ABTest", "A_B_TEST")]
    public void Slugify_matches_game_algorithm(string cls, string expected) =>
        Assert.Equal(expected, GameIds.Slugify(cls));

    [Fact]
    public void Ids_helpers()
    {
        Assert.Equal("ASCENDERS_BANE", GameIds.Entry("CARD.ASCENDERS_BANE"));
        Assert.Equal("CARD.ASCENDERS_BANE", GameIds.Normalize("CARD", "ascenders_bane"));
        Assert.Equal("RELIC.X", GameIds.Normalize("CARD", "relic.x"));
    }

    [Fact]
    public void Scanner_reads_typed_named_power_and_const_vars()
    {
        var src = """
            namespace MegaCrit.Sts2.Core.Models.Cards;

            public sealed class Sample : CardModel
            {
            	private const string _key = "Custom";

            	protected override IEnumerable<DynamicVar> CanonicalVars => new global::_003C_003Ez__ReadOnlyArray<DynamicVar>(new DynamicVar[5]
            	{
            		new DamageVar(12m, ValueProp.Move),
            		new BlockVar("ExtraBlock", 4m, ValueProp.Move),
            		new PowerVar<ThornsPower>(3m),
            		new DynamicVar(_key, 0.5m),
            		new BoolVar("Flag", true)
            	});

            	public Sample()
            		: base(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
            	{
            	}

            	protected override void OnUpgrade()
            	{
            		base.DynamicVars.Damage.UpgradeValueBy(4m);
            		base.DynamicVars["ThornsPower"].UpgradeValueBy(2m);
            		base.DynamicVars["Missing"].UpgradeValueBy(1m);
            	}
            }
            """;
        var f = ModelSourceScanner.Scan(src, "Sample.cs", "CardModel")!;
        Assert.Equal(["Damage", "ExtraBlock", "ThornsPower", "Custom", "Flag"], f.Vars.Select(v => v.Name));
        Assert.Equal([12m, 4m, 3m, 0.5m, 1m], f.Vars.Select(v => v.Base));
        Assert.Equal(4m, f.Vars[0].Upgrade);
        Assert.Equal(2m, f.Vars[2].Upgrade);
        Assert.Equal("bool", f.Vars[4].Kind);
        Assert.Equal((2, "Attack", "Rare", "AnyEnemy"), (f.Cost!.Value, f.CardType, f.CardRarity, f.Target));
        Assert.Contains(f.Issues, i => i.Contains("Missing"));
    }

    [Fact]
    public void Scanner_ignores_wrong_base_and_abstract()
    {
        Assert.Null(ModelSourceScanner.Scan("public sealed class A : RelicModel {}", "A.cs", "CardModel"));
        Assert.Null(ModelSourceScanner.Scan("public abstract class A : CardModel {}", "A.cs", "CardModel"));
    }
}
