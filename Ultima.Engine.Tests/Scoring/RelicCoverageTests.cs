using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Relics;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Invariant de couverture des reliques : après le barème de tags, une seule relique peut rester
/// à 0 — celle qui n'a réellement aucun effet. Avant, 163 reliques taguées sortaient muettes.
/// </summary>
public sealed class RelicCoverageTests
{
    private static ScoringContext Ctx() => new(
        "CHARACTER.SILENT", 0, null, 0, 8,
        new[] { new CardInstance("CARD.STRIKE_SILENT", 0, null, null) },
        Array.Empty<string>(), 99, 70, 70);

    private static string[] CatalogIds()
    {
        var file = Path.Combine(ScoringData.ResolveCatalogDir(), "relics.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
        return doc.RootElement.GetProperty("relics").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString() ?? "")
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [Fact]
    public void Every_relic_with_an_effect_gets_a_note()
    {
        var scorer = new Scorer();
        var ctx = Ctx();
        var ids = CatalogIds();
        var notScored = ids.Where(id => scorer.ScoreRelic(id, ctx).Total == 0).ToArray();

        Assert.True(ids.Length >= 300, $"{ids.Length} reliques au catalogue");
        // Une seule exception admise : « C'est un diadème. » (tag no_effect).
        // Seule exception mesurée : DEPRECATED_RELIC, un artefact de développement sans effet.
        Assert.True(notScored.Length <= 1,
            $"sans note ({notScored.Length}) : {string.Join(", ", notScored.Take(10))}");
        if (notScored.Length == 1)
            Assert.EndsWith("DEPRECATED_RELIC", notScored[0].ToUpperInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_fallback_never_reaches_S()
    {
        var scorer = new Scorer();
        var ctx = Ctx();
        var capped = CatalogIds()
            .Select(id => scorer.ScoreRelic(id, ctx))
            .Where(s => s.Terms.Any(t => t.Label.StartsWith("Effet :", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(capped);
        Assert.All(capped, s => Assert.InRange(s.Total, 1, RelicTagValues.MaxTagScore));
    }
}
