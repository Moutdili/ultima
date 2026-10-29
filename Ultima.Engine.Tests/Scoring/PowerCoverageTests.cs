using System.Text.Json;
using System.Text.RegularExpressions;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Powers;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// S28 — couverture des pouvoirs qui changent un classement.
///
/// Ce qui est prouvé ici :
///  1. chaque hook surchargé a un statut de résolution explicite (<c>code</c>, <c>manual</c>,
///     <c>unresolved</c>) et un statut <c>code</c> implique une expression évaluable ;
///  2. la table manuelle <c>power_constants.json</c> ne cite que des pouvoirs et des hooks
///     qui existent, et chaque ligne cite une ligne de code (<c>fichier.cs:ligne</c>) ;
///  3. les 21 multiplicateurs de dégâts portés par un pouvoir sont tous <c>code</c> ou
///     <c>manual</c>, et le total annoncé par docs/33 (25 = 21 pouvoirs + 4 reliques) est
///     vérifiable dans le fichier ;
///  4. un pouvoir inconnu reste <c>unresolved</c> : jamais évalué à 1 par défaut.
/// </summary>
public sealed class PowerCoverageTests
{
    private static readonly PowerCatalog Catalog = PowerCatalog.Load();

    private static readonly Regex SourcePattern = new(@"^[A-Za-z0-9_.]+\.cs:\d+$", RegexOptions.Compiled);

    private static JsonElement Manual()
    {
        var path = Path.Combine(ScoringData.ResolveCatalogDir(), "power_constants.json");
        Assert.True(File.Exists(path), $"power_constants.json introuvable : {path}");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.Clone();
    }

    private static JsonElement CatalogJson()
    {
        var path = Path.Combine(ScoringData.ResolveCatalogDir(), "powers.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Every_overridden_hook_has_an_explicit_resolution()
    {
        var checkedHooks = 0;
        foreach (var power in Catalog.Powers)
        {
            foreach (var hook in power.Hooks.Keys)
            {
                var resolution = power.ResolutionOf(hook);
                Assert.Contains(resolution, new[] { "code", "manual", "unresolved" });

                switch (resolution)
                {
                    case "code":
                        Assert.True(power.IsEvaluable(hook), $"{power.Id}.{hook} : code sans expression");
                        Assert.NotNull(power.Expression(hook));
                        break;
                    case "manual":
                        Assert.False(string.IsNullOrWhiteSpace(power.ManualSourceOf(hook)),
                            $"{power.Id}.{hook} : manual sans source");
                        Assert.Matches(SourcePattern, power.ManualSourceOf(hook)!);
                        break;
                    default:
                        Assert.Null(power.Expression(hook));
                        Assert.False(string.IsNullOrWhiteSpace(power.Problem(hook)),
                            $"{power.Id}.{hook} : unresolved sans raison");
                        break;
                }
                checkedHooks++;
            }
        }

        Assert.Equal(58, checkedHooks);
    }

    [Fact]
    public void Manual_table_cites_only_existing_powers_and_hooks()
    {
        var manual = Manual();
        Assert.Equal("manual", manual.GetProperty("kind").GetString());

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = 0;
        foreach (var row in manual.GetProperty("constants").EnumerateArray())
        {
            var powerId = row.GetProperty("power").GetString();
            var hook = row.GetProperty("hook").GetString();
            Assert.False(string.IsNullOrWhiteSpace(powerId));
            Assert.False(string.IsNullOrWhiteSpace(hook));

            var power = Catalog.Get(powerId);
            Assert.NotNull(power);
            Assert.True(power!.Has(hook!), $"{powerId} ne surcharge pas {hook}");

            var source = row.GetProperty("source").GetString();
            Assert.False(string.IsNullOrWhiteSpace(source));
            Assert.Matches(SourcePattern, source!);

            Assert.Equal("manual", power.ResolutionOf(hook!));
            Assert.Equal(row.GetProperty("evaluable").GetBoolean(), power.IsEvaluable(hook!));

            Assert.True(seen.Add($"{powerId}.{hook}"), $"ligne manuelle en double : {powerId}.{hook}");
            rows++;
        }

        Assert.Equal(4, rows);
    }

    [Fact]
    public void Damage_multipliers_are_all_code_or_manual()
    {
        var manual = Manual();
        var set = manual.GetProperty("sets").GetProperty("damage_multipliers");

        var powers = set.GetProperty("powers").EnumerateArray().Select(p => p.GetString()!).ToList();
        var relics = set.GetProperty("relics").EnumerateArray().ToList();
        var documentedTotal = set.GetProperty("documented_total").GetInt32();

        Assert.Equal(21, powers.Count);
        Assert.Equal(documentedTotal, powers.Count + relics.Count);
        Assert.Equal(25, documentedTotal);

        foreach (var powerId in powers)
        {
            var power = Catalog.Get(powerId);
            Assert.NotNull(power);
            Assert.True(power!.Has("ModifyDamageMultiplicative"),
                $"{powerId} ne surcharge pas ModifyDamageMultiplicative");
            Assert.Contains(power.ResolutionOf("ModifyDamageMultiplicative"), new[] { "code", "manual" });
        }

        foreach (var relic in relics)
        {
            Assert.False(string.IsNullOrWhiteSpace(relic.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(relic.GetProperty("hook").GetString()));
            Assert.Matches(SourcePattern, relic.GetProperty("source").GetString()!);
        }

        // Les 2 multiplicateurs documentés à la main sont exactement ceux que le code ne
        // réduit pas : Slow (compteur de combat) et Intercept (nombre d'alliés couverts).
        var manualMultipliers = powers
            .Where(id => Catalog.Resolution(id, "ModifyDamageMultiplicative") == "manual")
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "intercept", "slow" }, manualMultipliers);
    }

    [Fact]
    public void Indirect_damage_multipliers_are_extracted()
    {
        // Cruelty et Debilitate passent par ModifyVulnerableMultiplier / ModifyWeakMultiplier :
        // sans ces hooks, deux multiplicateurs de dégâts restaient invisibles.
        Assert.Equal("In + Amount / 100", Catalog.Expression("cruelty", "ModifyVulnerableMultiplier"));
        Assert.Equal("In + (In - 1)", Catalog.Expression("debilitate", "ModifyVulnerableMultiplier"));
        Assert.Equal("In - (1 - In)", Catalog.Expression("debilitate", "ModifyWeakMultiplier"));

        Assert.True(ExpressionEval.TryEval("In + Amount / 100", 25m, 1.5m, out var cruelty));
        Assert.Equal(1.75m, cruelty);

        Assert.True(ExpressionEval.TryEval("In + (In - 1)", 0m, 1.5m, out var debilitate));
        Assert.Equal(2.0m, debilitate);
    }

    [Fact]
    public void Unknown_power_is_unresolved_and_never_evaluated_as_one()
    {
        Assert.Null(Catalog.Get("pas_un_pouvoir"));
        Assert.Equal("unresolved", Catalog.Resolution("pas_un_pouvoir", "ModifyDamageMultiplicative"));
        Assert.Null(Catalog.Expression("pas_un_pouvoir", "ModifyDamageMultiplicative"));

        // Un hook non résolu ne produit jamais 1 : ExpressionEval refuse l'absence d'expression.
        Assert.Null(Catalog.Expression("slow", "ModifyDamageMultiplicative"));
        Assert.False(ExpressionEval.TryEval(Catalog.Expression("slow", "ModifyDamageMultiplicative"), 3m, out _));
        Assert.Null(ExpressionEval.Eval(null, 3m));

        // Le hook que le pouvoir ne surcharge pas n'est pas inventé non plus.
        Assert.Equal("unresolved", Catalog.Resolution("vulnerable", "ModifyMerchantPrice"));
    }

    [Fact]
    public void Hook_surface_is_declared_in_the_catalog()
    {
        var catalog = CatalogJson();
        var engine = catalog.GetProperty("hooks_engine").EnumerateArray().Select(h => h.GetString()).ToArray();
        var extended = catalog.GetProperty("hooks_extended").EnumerateArray().Select(h => h.GetString()).ToArray();

        Assert.Equal(12, engine.Length);
        Assert.Equal(3, extended.Length);
        Assert.Contains("ModifyDamageMultiplicative", engine);
        Assert.Contains("ModifyVulnerableMultiplier", extended);
        Assert.Contains("ModifyEnergyGain", extended);
    }
}
