using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Powers;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// S21 — catalogue déclaratif des pouvoirs. Les valeurs attendues viennent du code du jeu
/// (<c>Models/Powers/*.cs</c>, extrait par <c>tools/index_powers.py</c>) : chaque test nomme
/// la classe source dont il vérifie l'expression.
///
/// Ce qui est prouvé ici : le moteur peut appliquer un pouvoir sans classe C# dédiée, et
/// une expression non réductible rend <c>null</c> — jamais une valeur par défaut.
/// </summary>
public sealed class PowerCatalogTests
{
    private static readonly PowerCatalog Catalog = PowerCatalog.Load();

    [Fact]
    public void Catalog_loads_the_260_powers_of_the_game()
    {
        Assert.Equal(260, Catalog.Count);
        Assert.Equal("tools/index_powers.py", Catalog.GeneratedBy);
        Assert.Empty(Catalog.Warnings);
    }

    [Fact]
    public void VulnerablePower_multiplier_is_1_5()
    {
        // VulnerablePower.ModifyDamageMultiplicative : return num (DynamicVars["DamageIncrease"] = 1.5m)
        var expression = Catalog.Expression("vulnerable", "ModifyDamageMultiplicative");
        Assert.Equal("1.5", expression);

        Assert.True(ExpressionEval.TryEval(expression, 3m, out var multiplier));
        Assert.Equal(1.5m, multiplier);

        // Les modificateurs externes sont nommés, pas absorbés dans la valeur.
        Assert.Contains(
            "PaperPhrog.ModifyVulnerableMultiplier",
            Catalog.Get("vulnerable")!.ModifiersOf("ModifyDamageMultiplicative"));
    }

    [Fact]
    public void StrengthPower_additive_is_the_stack_amount()
    {
        // StrengthPower.ModifyDamageAdditive : return base.Amount
        var expression = Catalog.Expression("strength", "ModifyDamageAdditive");
        Assert.Equal("Amount", expression);

        Assert.True(ExpressionEval.TryEval(expression, 7m, out var bonus));
        Assert.Equal(7m, bonus);
    }

    [Fact]
    public void IntangiblePower_caps_damage_at_1()
    {
        // IntangiblePower.ModifyDamageCap : return 1m (la garde `target != Owner` rend decimal.MaxValue)
        var expression = Catalog.Expression("intangible", "ModifyDamageCap");
        Assert.Equal("1", expression);

        Assert.True(ExpressionEval.TryEval(expression, 40m, out var cap));
        Assert.Equal(1m, cap);
    }

    [Fact]
    public void HardToKillPower_caps_damage_at_the_stack_amount()
    {
        // HardToKillPower.ModifyDamageCap : return base.Amount
        var expression = Catalog.Expression("hard_to_kill", "ModifyDamageCap");
        Assert.Equal("Amount", expression);

        Assert.True(ExpressionEval.TryEval(expression, 4m, out var cap));
        Assert.Equal(4m, cap);
    }

    [Fact]
    public void Unknown_expression_is_refused_instead_of_defaulted()
    {
        // ShadowmeldPower.ModifyBlockMultiplicative : (decimal)Math.Pow(2.0, base.Amount) — hors langage
        var shadowmeld = Catalog.Get("shadowmeld");
        Assert.NotNull(shadowmeld);
        Assert.True(shadowmeld!.Has("ModifyBlockMultiplicative"));
        Assert.Null(shadowmeld.Expression("ModifyBlockMultiplicative"));
        Assert.Equal("call", shadowmeld.Problem("ModifyBlockMultiplicative"));

        Assert.False(ExpressionEval.TryEval("Math.Pow(2.0, Amount)", 2m, out var value));
        Assert.Equal(0m, value); // valeur de sortie non significative : le booléen fait foi
        Assert.Null(ExpressionEval.Eval("Math.Pow(2.0, Amount)", 2m));
        Assert.False(ExpressionEval.TryEval(null, 2m, out _));
        Assert.False(ExpressionEval.TryEval("", 2m, out _));
    }

    [Fact]
    public void Missing_catalog_falls_back_to_Empty_without_throwing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ultima-catalog-absent-" + Guid.NewGuid().ToString("N"));
        var catalog = PowerCatalog.Load(missing);

        Assert.Same(PowerCatalog.Empty, catalog);
        Assert.Equal(0, catalog.Count);
        Assert.Null(catalog.Get("vulnerable"));
        Assert.Null(catalog.Expression("vulnerable", "ModifyDamageMultiplicative"));
        Assert.Empty(catalog.Powers);
    }

    [Fact]
    public void Unknown_power_or_hook_returns_null()
    {
        Assert.Null(Catalog.Get("pas_un_pouvoir"));
        Assert.Null(Catalog.Expression("pas_un_pouvoir", "ModifyDamageMultiplicative"));
        Assert.Null(Catalog.Expression("vulnerable", "ModifyMerchantPrice")); // aucun pouvoir ne surcharge ce hook
    }

    [Fact]
    public void Lookup_accepts_the_class_name_as_well_as_the_snake_id()
    {
        Assert.Equal("VulnerablePower", Catalog.Get("vulnerable")!.Class);
        Assert.Equal("vulnerable", Catalog.Get("VulnerablePower")!.Id);
        Assert.Equal("vulnerable", Catalog.Get("Vulnerable")!.Id);
        Assert.Equal("vulnerable", Catalog.Get("POWER.VULNERABLE")!.Id);
    }

    [Fact]
    public void Hand_draw_hooks_use_In_for_the_incoming_count()
    {
        // ClarityPower.ModifyHandDraw : return count + 1m
        var expression = Catalog.Expression("clarity", "ModifyHandDraw");
        Assert.Equal("In + 1", expression);

        Assert.True(ExpressionEval.TryEval(expression, 0m, 5m, out var drawn));
        Assert.Equal(6m, drawn);
    }

    [Fact]
    public void Every_stored_expression_is_evaluable_by_the_engine()
    {
        var checkedExpressions = 0;
        foreach (var power in Catalog.Powers)
        {
            foreach (var hook in power.EvaluableHooks)
            {
                var expression = power.Expression(hook);
                Assert.NotNull(expression);

                // Toute expression du catalogue doit passer l'évaluateur, sinon elle n'a rien
                // à y faire : `tools/index_powers.py` valide déjà la grammaire, ce test le scelle.
                Assert.True(
                    ExpressionEval.TryEval(expression, 1m, 1m, out _),
                    $"{power.Id}.{hook} = « {expression} » n'est pas évaluable");
                checkedExpressions++;
            }
        }

        Assert.True(checkedExpressions >= 50, $"expressions évaluables: {checkedExpressions}");
    }

    [Fact]
    public void Unresolved_hooks_are_declared_not_guessed()
    {
        var unresolved = Catalog.Powers
            .SelectMany(p => p.Hooks.Keys.Select(h => (power: p, hook: h)))
            .Where(x => x.power.Expression(x.hook) == null)
            .ToList();

        // Chaque hook surchargé non évaluable doit dire pourquoi (call/state/local/dynamicvar…)
        Assert.All(unresolved, x => Assert.False(string.IsNullOrWhiteSpace(x.power.Problem(x.hook))));

        var slow = Catalog.Get("slow")!;
        Assert.Null(slow.Expression("ModifyDamageMultiplicative"));
        Assert.Equal("dynamicvar_runtime", slow.Problem("ModifyDamageMultiplicative"));
    }

    [Fact]
    public void Evaluator_never_throws_on_hostile_input()
    {
        foreach (var expression in new[]
                 {
                     "1/0", "((1)", "Amount +", "1e9", "1,5", "System.Math.Pow(2, Amount)",
                     "Amount; DROP TABLE", new string('9', ExpressionEval.MaxLength + 1),
                 })
        {
            Assert.False(ExpressionEval.TryEval(expression, 1m, out _), expression);
        }

        Assert.True(ExpressionEval.TryEval("1.5m", 0m, out var decimalSuffix));
        Assert.Equal(1.5m, decimalSuffix);
        Assert.True(ExpressionEval.TryEval("(1 + 2) * 3 - 4 / 2", 0m, out var arithmetic));
        Assert.Equal(7m, arithmetic);
    }
}
