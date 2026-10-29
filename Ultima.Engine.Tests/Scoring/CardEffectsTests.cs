using Ultima.Engine.Scoring.Cards;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Catalogue des effets numériques (S7) : les valeurs que les descriptions ne donnent pas.
/// Sources : data/catalog/cards_effects.json, généré par tools/index_card_effects.py.
/// </summary>
public sealed class CardEffectsTests
{
    private static readonly CardEffects Catalog = CardEffects.Load();

    [Fact]
    public void Catalog_loads_more_than_500_cards()
    {
        Assert.True(Catalog.Count >= 500, $"cartes chargées: {Catalog.Count}");
        Assert.False(string.IsNullOrWhiteSpace(Catalog.GeneratedBy));
    }

    [Fact]
    public void Strike_deals_6_and_9_upgraded()
    {
        var strike = Catalog.Get("STRIKE_IRONCLAD");

        Assert.NotNull(strike);
        Assert.Equal(6, strike!.Damage(0));
        Assert.Equal(9, strike.Damage(1));
        Assert.Equal(1, strike.Hits);
        Assert.Equal(1, strike.Cost);
        Assert.Null(strike.Block(0));
    }

    [Fact]
    public void Defend_gives_5_block_and_8_upgraded()
    {
        var defend = Catalog.Get("DEFEND_IRONCLAD");

        Assert.NotNull(defend);
        Assert.Equal(5, defend!.Block(0));
        Assert.Equal(8, defend.Block(1));
        Assert.Null(defend.Damage(0));
    }

    [Fact]
    public void Multi_hit_card_keeps_its_hit_count()
    {
        var twin = Catalog.Get("TWIN_STRIKE");

        Assert.NotNull(twin);
        Assert.Equal(2, twin!.Hits);
        Assert.Equal(5, twin.Damage(0));
        Assert.Equal(7, twin.Damage(1));
        Assert.Equal(10, twin.TotalDamage(0));
        Assert.Equal(14, twin.TotalDamage(1));
    }

    [Fact]
    public void At_least_one_multi_hit_card_is_catalogued()
    {
        Assert.Contains(Catalog.All, e => e.Hits > 1);
    }

    [Fact]
    public void X_cost_card_reports_its_flag_and_per_hit_damage()
    {
        var whirlwind = Catalog.Get("CARD.WHIRLWIND");

        Assert.NotNull(whirlwind);
        Assert.True(whirlwind!.IsXCost);
        Assert.Equal(5, whirlwind.Damage(0));
        Assert.Equal(8, whirlwind.Damage(1));
        Assert.Equal(0, whirlwind.Cost);
    }

    [Fact]
    public void Unknown_card_returns_null_never_an_invented_effect()
    {
        Assert.Null(Catalog.Get("NOT_A_CARD"));
        Assert.Null(Catalog.Get("CARD.NOT_A_CARD"));
        Assert.Null(Catalog.Get(""));
        Assert.Null(Catalog.Get(null));
    }

    [Fact]
    public void Draw_and_energy_are_catalogued_with_their_upgrade()
    {
        var acrobatics = Catalog.Get("ACROBATICS");
        Assert.NotNull(acrobatics);
        Assert.Equal(3, acrobatics!.Draw(0));
        Assert.Equal(4, acrobatics.Draw(1));
        Assert.Equal(0, acrobatics.Energy(0));

        var adrenaline = Catalog.Get("ADRENALINE");
        Assert.NotNull(adrenaline);
        Assert.Equal(2, adrenaline!.Draw(0));
        Assert.Equal(2, adrenaline.Draw(1));      // l'amélioration ne touche que l'énergie
        Assert.Equal(1, adrenaline.Energy(0));
        Assert.Equal(2, adrenaline.Energy(1));
    }

    [Fact]
    public void Powers_and_their_amounts_come_from_vars()
    {
        var bash = Catalog.Get("BASH");

        Assert.NotNull(bash);
        Assert.Contains("Vulnerable", bash!.Powers);
        Assert.True(bash.HasPower("vulnerable"));
        Assert.Equal(2, bash.PowerAmount("Vulnerable"));
        Assert.Equal(8, bash.Vars["Damage"]);
    }

    [Fact]
    public void Absent_catalog_yields_empty_and_no_exception()
    {
        var empty = CardEffects.Load(Path.Combine(Path.GetTempPath(), "ultima-no-catalog-here"));

        Assert.Equal(0, empty.Count);
        Assert.Null(empty.Get("STRIKE_IRONCLAD"));
        Assert.Same(CardEffects.Empty, empty);
    }

    [Fact]
    public void Every_catalogued_card_has_an_id_and_a_name()
    {
        Assert.All(Catalog.All, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Id));
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
        });
    }

    [Fact]
    public void Damaging_cards_have_a_positive_base_damage()
    {
        // Les cartes à dégâts calculés (Body Slam) portent 0 : elles sont hors de ce test.
        var damaging = Catalog.All.Where(e => e.DamageBase is > 0).ToList();

        Assert.True(damaging.Count >= 150, $"cartes avec dégâts: {damaging.Count}");
        Assert.All(damaging, e => Assert.NotNull(e.Damage(0)));
    }
}
