using Ultima.Knowledge.Text;
using Xunit;

namespace Ultima.Knowledge.Tests;

public class RendererTests
{
    private static readonly GameTextRenderer R = new();

    private static string Fr(string tpl, params (string, TextValue)[] vars) =>
        R.Render(tpl, vars.ToDictionary(v => v.Item1, v => v.Item2), "fr").Text;

    private static string En(string tpl, params (string, TextValue)[] vars) =>
        R.Render(tpl, vars.ToDictionary(v => v.Item1, v => v.Item2), "en").Text;

    [Fact]
    public void Diff_writes_integer_value_and_bbcode_is_stripped() =>
        Assert.Equal("Cognez 7 fois le Gardien.", Fr("Cognez {Damage:diff()} fois le [gold]Gardien[/gold].", ("Damage", TextValue.Of(7m))));

    [Theory]
    [InlineData(0, "fr", "pomme")]
    [InlineData(1, "fr", "pomme")]
    [InlineData(2, "fr", "pommes")]
    [InlineData(0, "en", "pommes")]
    [InlineData(1, "en", "pomme")]
    [InlineData(2, "en", "pommes")]
    public void Plural_follows_smartformat_rules(int n, string lang, string expected) =>
        Assert.Equal(expected, R.Render("{N:plural:pomme|pommes}", new Dictionary<string, TextValue> { ["N"] = TextValue.Of(n) }, lang).Text);

    [Fact]
    public void Implicit_plural_with_empty_selector_uses_current_value()
    {
        const string tpl = "{Repeat:plural:la prochaine lune|pendant [blue]{}[/blue] lunes}";
        Assert.Equal("la prochaine lune", Fr(tpl, ("Repeat", TextValue.Of(1m))));
        Assert.Equal("pendant 4 lunes", Fr(tpl, ("Repeat", TextValue.Of(4m))));
    }

    [Fact]
    public void Complex_condition()
    {
        const string tpl = "{A:cond:>1?Les {A:diff()} corbeaux|Le corbeau}";
        Assert.Equal("Le corbeau", Fr(tpl, ("A", TextValue.Of(1m))));
        Assert.Equal("Les 3 corbeaux", Fr(tpl, ("A", TextValue.Of(3m))));
    }

    [Fact]
    public void Implicit_conditional_on_bool_and_string()
    {
        Assert.Equal("Base", Fr("Base{InCombat:\n(en combat)|}", ("InCombat", TextValue.Of(false))));
        Assert.Equal("Base\n(en combat)", Fr("Base{InCombat:\n(en combat)|}", ("InCombat", TextValue.Of(true))));
        Assert.Equal("générique", Fr("{S.StringValue:cond:[gold]{S}[/gold]|générique}", ("S", TextValue.Of(""))));
        Assert.Equal("Lame", Fr("{S.StringValue:cond:[gold]{S}[/gold]|générique}", ("S", TextValue.Of("Lame"))));
    }

    [Fact]
    public void Show_depends_on_upgrade()
    {
        const string tpl = "Polissez {IfUpgraded:show:TOUS les cailloux|un caillou}.";
        Assert.Equal("Polissez un caillou.", Fr(tpl, ("IfUpgraded", TextValue.IfUpgraded(false))));
        Assert.Equal("Polissez TOUS les cailloux.", Fr(tpl, ("IfUpgraded", TextValue.IfUpgraded(true))));
        Assert.Equal("Rangez un caillou au Sac.", Fr("Rangez un caillou {IfUpgraded:show:[gold]Poli[/gold]|} au [gold]Sac[/gold].", ("IfUpgraded", TextValue.IfUpgraded(false))));
    }

    [Theory]
    [InlineData(1, "⚡")]
    [InlineData(3, "⚡⚡⚡")]
    [InlineData(4, "4⚡")]
    [InlineData(0, "0⚡")]
    public void Energy_icons_like_the_game(int n, string expected) =>
        Assert.Equal(expected, Fr("{Energy:energyIcons()}", ("Energy", TextValue.Of(n))));

    [Fact]
    public void Energy_icon_from_prefix_string_uses_option_count() =>
        Assert.Equal("valant ⚡⚡", Fr("valant {energyPrefix:energyIcons(2)}", ("energyPrefix", TextValue.Of("ironclad"))));

    [Fact]
    public void Stars_abs_percent_and_choose()
    {
        Assert.Equal("★★", Fr("{Stars:starIcons()}", ("Stars", TextValue.Of(2m))));
        Assert.Equal("3", Fr("{D:abs()}", ("D", TextValue.Of(-3m))));
        Assert.Equal("25", Fr("{G:percentMore()}", ("G", TextValue.Of(1.25m))));
        Assert.Equal("25", Fr("{G:percentLess()}", ("G", TextValue.Of(0.75m))));
        Assert.Equal("défense", Fr("{T:choose(Attack|Skill):attaque|défense|autre}", ("T", TextValue.Of("Skill"))));
        Assert.Equal("autre", Fr("{T:choose(Attack|Skill):attaque|défense|autre}"));
        Assert.Equal("brisé", Fr("brisé{g:choose(feminine):e|}"));
    }

    [Fact]
    public void Unknown_variable_renders_X_and_is_listed()
    {
        var r = R.Render("Cueillez {Amount} {Amount:plural:pomme|pommes} et {Other:diff()}.", new Dictionary<string, TextValue>(), "fr");
        Assert.Equal("Cueillez X pommes et X.", r.Text);
        Assert.Equal(["Amount", "Other"], r.UnresolvedVars);
    }

    [Theory]
    [InlineData("Texte {non fermé")]
    [InlineData("Texte } orphelin")]
    [InlineData("{A:cond:>1?{B}|}}")]
    [InlineData("[sine][blue]1100[/blue][/sine] [img]res://x.png[/img]fin")]
    public void Never_outputs_braces_or_bbcode(string tpl)
    {
        var text = R.Render(tpl, new Dictionary<string, TextValue>(), "fr").Text;
        Assert.DoesNotContain('{', text);
        Assert.DoesNotContain('}', text);
        Assert.DoesNotContain('[', text);
        Assert.DoesNotContain(']', text);
    }

    [Fact]
    public void Nested_implicit_conditional_with_formatter_like_word()
    {
        // « Sautez » suivi d'une espace n'est pas un nom de formateur : tout est format implicite.
        Assert.Equal("", Fr("{Flag:Sautez {N:diff()} fois|}", ("Flag", TextValue.Of(false)), ("N", TextValue.Of(2m))));
        Assert.Equal("Sautez 2 fois", Fr("{Flag:Sautez {N:diff()} fois|}", ("Flag", TextValue.Of(true)), ("N", TextValue.Of(2m))));
    }

    [Fact]
    public void English_rules_are_used_for_english_templates() =>
        Assert.Equal("Pick 1 apple.", En("Pick {C:diff()} {C:plural:apple|apples}.", ("C", TextValue.Of(1m))));
}
