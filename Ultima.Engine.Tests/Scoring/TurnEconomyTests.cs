using Ultima.Engine.Scoring.Powers;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Budget de tour (T5 §2) : la couche traite les pouvoirs comportementaux « d'un bloc » via les
/// expressions extraites du code. Ce qui n'a pas d'expression n'est pas inventé : il sort en null.
/// </summary>
public sealed class TurnEconomyTests
{
    private static Dictionary<string, int> P(params (string Id, int Stacks)[] powers) =>
        powers.ToDictionary(p => p.Id, p => p.Stacks, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Sans_pouvoir_le_tour_est_le_tour_de_base()
    {
        var budget = new TurnEconomy().Compute(null);

        Assert.Equal(TurnEconomy.BaseDraw, budget.Draw);
        Assert.Equal(TurnEconomy.BaseEnergy, budget.Energy);
        Assert.Equal(TurnEconomy.BasePlays, budget.CardPlays);
        Assert.Equal(1.5, budget.VulnerableMultiplier);
        Assert.Equal(0.75, budget.WeakMultiplier);
        Assert.Empty(budget.Terms);
        Assert.Empty(budget.Unresolved);
    }

    [Fact]
    public void Un_pouvoir_de_pioche_deplace_la_pioche_par_ses_piles()
    {
        // demesne : `In + Amount` sur ModifyHandDraw (et ModifyMaxEnergy) — expression du code.
        var budget = new TurnEconomy().Compute(P(("demesne", 2)));

        Assert.Equal(TurnEconomy.BaseDraw + 2, budget.Draw);
        Assert.All(budget.Terms, t => Assert.Equal("code", t.Resolution));
        Assert.Contains(budget.Terms, t => t.Hook == "ModifyHandDraw" && t.Value == 7);
    }

    [Fact]
    public void Un_pouvoir_sans_expression_ne_deplace_rien_et_le_dit()
    {
        // mind_rot : ModifyHandDraw sans expression évaluable (expr null dans le catalogue).
        var budget = new TurnEconomy().Compute(P(("mind_rot", 3)));

        Assert.Equal(TurnEconomy.BaseDraw, budget.Draw);
        var term = Assert.Single(budget.Terms, t => t.PowerId == "mind_rot");
        Assert.Null(term.Value);
        Assert.Equal("unresolved", term.Resolution);
        Assert.Contains("mind_rot", budget.Unresolved);
    }

    [Fact]
    public void Les_multiplicateurs_de_statuts_sont_chaines()
    {
        // debilitate : Vulnerable `In + (In - 1)` et Weak `In - (1 - In)` — à 1 pile :
        // Vulnerable 1,5 + 0,5 = 2,0 ; Weak 0,75 - 0,25 = 0,5.
        var budget = new TurnEconomy().Compute(P(("debilitate", 1)));

        Assert.Equal(2.0, budget.VulnerableMultiplier, 3);
        Assert.Equal(0.5, budget.WeakMultiplier, 3);
    }

    [Fact]
    public void Wreath_of_energy_negatif_est_borne_a_zero()
    {
        // waste_away : `In - Amount` sur ModifyMaxEnergy ; 10 piles ne peuvent pas donner -7 énergie.
        var budget = new TurnEconomy().Compute(P(("waste_away", 10)));

        Assert.Equal(0, budget.Energy);
        Assert.Contains(budget.Terms, t => t.Hook == "ModifyMaxEnergy" && t.Value == -7);
    }
}
