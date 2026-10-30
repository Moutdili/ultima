using Ultima.Engine.Scoring.Status;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Statuts : les cartes qui en posent (y compris co-op) et les effets que les monstres appliquent
/// au joueur ou à eux-mêmes. Les coefficients viennent du code du jeu (hooks de powers.json).
/// </summary>
public sealed class StatusRulesTests
{
    private static Dictionary<string, int> S(params (string Id, int Stacks)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Stacks, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Weak_reduces_damage_dealt_by_a_quarter()
    {
        var rules = StatusRules.Default;

        Assert.True(rules.IsLoaded, rules.Problem);
        Assert.Equal(8, rules.Incoming(10, S(("weak", 1)), null));      // 7,5 → 8 (arrondi au plus loin)
        Assert.Equal(16, rules.Incoming(21, S(("weak", 1)), null));     // 15,75 → 16
    }

    [Fact]
    public void Vulnerable_increases_damage_taken_by_half()
    {
        var rules = StatusRules.Default;

        Assert.Equal(15, rules.Incoming(10, null, S(("vulnerable", 1))));
    }

    [Fact]
    public void Weak_and_vulnerable_combine_on_the_right_sides()
    {
        var rules = StatusRules.Default;

        // Attaquant affaibli (0,75) ET cible vulnérable (1,5) : 10 → 11,25 → 11.
        Assert.Equal(11, rules.Incoming(10, S(("weak", 1)), S(("vulnerable", 1))));
    }

    [Fact]
    public void Intangible_caps_damage_per_hit()
    {
        var rules = StatusRules.Default;

        Assert.Equal(1, rules.Incoming(40, null, S(("intangible", 1))));
    }

    [Fact]
    public void Strength_adds_flat_damage_per_stack()
    {
        var rules = StatusRules.Default;

        Assert.Equal(13, rules.Incoming(10, S(("strength", 3)), null));
        Assert.Equal(14, rules.Incoming(10, S(("strength", 2), ("vigor", 2)), null));
    }

    [Fact]
    public void Frail_and_dexterity_change_block()
    {
        var rules = StatusRules.Default;

        Assert.Equal(15, rules.BlockGained(20, S(("frail", 1))));
        Assert.Equal(12, rules.BlockGained(10, S(("dexterity", 2))));
        Assert.Equal(9, rules.BlockGained(10, S(("dexterity", 2), ("frail", 1))));   // 12 × 0,75
    }

    [Fact]
    public void Unqualified_power_is_reported_never_applied()
    {
        var rules = StatusRules.Default;
        // « slow » dépend de DynamicVars[SlowAmount] : publié non modélisé, jamais appliqué au hasard.
        var effect = rules.Evaluate(S(("slow", 1)));

        Assert.Equal(1, effect.DealtMultiplier);
        Assert.Equal(1, effect.TakenMultiplier);
        Assert.Contains(effect.Notes, n => n.Contains("slow") && n.Contains("non modélisé"));
    }

    [Fact]
    public void Directions_qualified_from_the_code_are_applied()
    {
        var rules = StatusRules.Default;

        // Colossus (ColossusPower.cs:26, garde target != Owner) : dégâts SUBIS x0,5.
        Assert.Equal(10, rules.Incoming(20, null, S(("colossus", 1))));
        // Double Damage (DoubleDamagePower.cs:19, garde dealer != Owner) : dégâts INFLIGÉS x2.
        Assert.Equal(20, rules.Incoming(10, S(("double_damage", 1)), null));
        // Tainted (TaintedPower.cs:18) : +Amount de dégâts SUBIS.
        Assert.Equal(13, rules.Incoming(10, null, S(("tainted", 3))));
        // Hard to Kill (HardToKillPower.cs:14) : plafond = Amount.
        Assert.Equal(5, rules.Incoming(50, null, S(("hard_to_kill", 5))));
        // Covered : multiplicateur 0 (immunité).
        Assert.Equal(0, rules.Incoming(30, null, S(("covered", 1))));
        // Guarded (x0,5) sur une cible vulnérable (x1,5) : 10 -> 7,5 -> 8.
        Assert.Equal(8, rules.Incoming(10, null, S(("guarded", 1), ("vulnerable", 1))));
        // Unmovable (armure x2).
        Assert.Equal(20, rules.BlockGained(10, S(("unmovable", 1))));
    }

    [Fact]
    public void Unknown_status_is_reported_never_guessed()
    {
        var effect = StatusRules.Default.Evaluate(S(("pas_un_pouvoir", 3)));

        Assert.Equal(1, effect.DealtMultiplier);
        Assert.Contains(effect.Notes, n => n.Contains("aucune règle"));
    }

    [Fact]
    public void Encounter_max_hit_is_corrected_by_statuses()
    {
        var threat = Ultima.Engine.Scoring.Threat.ThreatModel.Default;
        var baseHit = threat.MaxHit("AEONGLASS_BOSS", 0);
        Assert.True(baseHit > 0, $"pire coup de Aeonglass : {baseHit}");

        var vulnerable = S(("vulnerable", 1));
        var weak = S(("weak", 1));

        Assert.Equal(baseHit, threat.PredictedMaxHit("AEONGLASS_BOSS", 0));
        Assert.True(threat.PredictedMaxHit("AEONGLASS_BOSS", 0, null, vulnerable) > baseHit);
        Assert.True(threat.PredictedMaxHit("AEONGLASS_BOSS", 0, weak, null) < baseHit);
    }

    [Fact]
    public void Unqualified_list_is_published_for_transparency()
    {
        var rules = StatusRules.Default;

        Assert.True(rules.ModelledCount >= 28, $"règles modélisées : {rules.ModelledCount}");
        Assert.Contains(rules.Unqualified, r => r.Id == "slow");
        Assert.All(rules.Unqualified, r => Assert.False(string.IsNullOrWhiteSpace(r.Reason)));
    }
}

/// <summary>Qui applique quoi : cartes (dont co-op) et monstres, avec la ligne de code.</summary>
public sealed class StatusSourcesTests
{
    [Fact]
    public void Monsters_expose_their_debuffs_and_self_buffs()
    {
        var sources = StatusSources.Default;

        Assert.True(sources.IsLoaded, sources.Problem);
        Assert.True(sources.ApplicationCount > 400, $"applications indexées : {sources.ApplicationCount}");

        var aeonglass = sources.Monster("AEONGLASS");
        Assert.NotNull(aeonglass);
        Assert.Contains(aeonglass!.SelfBuffs, a => a.Power == "artifact" && a.Amount == "3m");
        Assert.All(aeonglass.SelfBuffs, a => Assert.True(a.Line > 0));

        var axebot = sources.Monster("AXEBOT");
        Assert.NotNull(axebot);
        Assert.Contains(axebot!.DebuffsOnPlayer, a => a.Power == "weak");
        Assert.Contains(axebot.DebuffsOnPlayer, a => a.Power == "frail");
    }

    [Fact]
    public void Cards_expose_the_power_they_apply()
    {
        var sources = StatusSources.Default;

        var abrasive = sources.Card("ABRASIVE");
        Assert.NotNull(abrasive);
        Assert.Contains(abrasive!.Applies, a => a.Power == "dexterity" && a.OnSelf);
        Assert.Contains(abrasive.Applies, a => a.Power == "thorns" && a.OnSelf);
    }
}
