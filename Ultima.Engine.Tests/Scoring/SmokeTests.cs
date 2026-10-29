using System.Text.Json;
using System.Text.Json.Nodes;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Model;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>Tests de fumée du socle : la méta se charge et un score sort avec ses termes.</summary>
public sealed class SmokeTests
{
    private static readonly ScoringContext Ctx = new(
        "CHARACTER.SILENT", 0, "ENCOUNTER.WATERFALL_GIANT_BOSS", 0, 5,
        new[] { new CardInstance("CARD.STRIKE_SILENT", 0, null, 1) }, Array.Empty<string>(), 99, 70, 70);

    [Fact]
    public void MetaTable_loads_public_data()
    {
        var meta = MetaTable.Load();
        Assert.True(meta.CardCount > 400, $"cartes chargées: {meta.CardCount}");
        Assert.True(meta.RelicCount > 200, $"reliques chargées: {meta.RelicCount}");
        Assert.NotNull(meta.PatchTarget);
        Assert.True(meta.SkipRate is > 0.5 and < 0.9, $"skip rate: {meta.SkipRate}");
    }

    [Fact]
    public void Known_card_has_consensus_and_source()
    {
        var meta = MetaTable.Load();
        var card = meta.Card("CARD.ALCHEMIZE");
        Assert.NotNull(card);
        Assert.NotNull(card!.Consensus);
        Assert.NotEmpty(card.Ratings);
        Assert.Equal("ALCHEMIZE", card.Id);
    }

    [Fact]
    public void ScoreCard_returns_explainable_terms()
    {
        var scorer = new Scorer();
        var s = scorer.ScoreCard(new CardInstance("CARD.ALCHEMIZE", 0, null, null), Ctx);
        Assert.InRange(s.Total, 0, 100);
        Assert.NotEmpty(s.Terms);
        Assert.All(s.Terms, t => Assert.False(string.IsNullOrWhiteSpace(t.Source)));
    }

    [Fact]
    public void Archetypes_are_indexed_by_character()
    {
        var meta = MetaTable.Load();
        Assert.NotEmpty(meta.Archetypes("CHARACTER.DEFECT"));
    }

    // --- Calibrage S→F : doit tenir avec ET sans les notes locales (exigence du chef) ----------

    private static int MetaPoints(Score score) =>
        score.Terms.First(t => t.Label.StartsWith("Méta communautaire", StringComparison.Ordinal)).Points;

    /// <summary>
    /// Construit une variante de data/meta dans un dossier temporaire.
    /// <paramref name="rescale"/> réécrit les consensus (simule une échelle locale différente).
    /// </summary>
    private static string MakeVariant(bool withLocal, Func<double, double>? rescale = null)
    {
        var publicDir = ScoringData.ResolveMetaDir();
        var tmp = Path.Combine(Path.GetTempPath(), "ultima-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);

        foreach (var name in new[] { "card_tiers.json", "relic_tiers.json", "archetypes.json", "sources.json" })
        {
            var src = Path.Combine(publicDir, name);
            if (!File.Exists(src)) continue;
            File.Copy(src, Path.Combine(tmp, name));
        }

        if (!withLocal) return tmp;

        var localDir = Path.Combine(tmp, "local");
        Directory.CreateDirectory(localDir);
        foreach (var name in new[] { "card_tiers.json", "relic_tiers.json", "archetypes.json", "sources.json" })
        {
            var src = Path.Combine(tmp, name);
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(localDir, name);
            File.Copy(src, dst);
            if (rescale is not null && name is "card_tiers.json" or "relic_tiers.json") RescaleConsensus(dst, rescale);
        }

        return tmp;
    }

    private static void RescaleConsensus(string path, Func<double, double> f)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach (var container in new[] { "characters", "groups" })
        {
            if (node[container] is not JsonObject groups) continue;
            foreach (var group in groups)
            {
                if (group.Value is not JsonObject items) continue;
                foreach (var item in items)
                {
                    if (item.Value is not JsonObject entry) continue;
                    if (entry["consensus"] is JsonObject consensus &&
                        consensus["score"] is JsonValue value &&
                        value.TryGetValue<double>(out var score))
                    {
                        consensus["score"] = Math.Round(f(score), 2);
                    }
                }
            }
        }

        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void AssertCalibrated(string metaDir, string label)
    {
        var meta = MetaTable.Load(metaDir);
        Assert.True(meta.CardCount > 400, $"{label}: {meta.CardCount} cartes");

        var scorer = new Scorer(meta);
        var rated = meta.Cards.Where(c => c.Consensus is not null).OrderBy(c => c.Consensus).ToList();
        Assert.True(rated.Count > 400);

        var best = rated[^1];
        var topScore = scorer.ScoreCard(new CardInstance(best.Id, 0, null, null), Ctx);
        Assert.True(MetaPoints(topScore) >= 70,
            $"{label}: meilleure carte ({best.Id}, consensus {best.Consensus}) = {MetaPoints(topScore)} pts");
        Assert.Contains(topScore.Grade, new[] { "A", "S" });

        var median = rated[rated.Count / 2];
        var medianScore = scorer.ScoreCard(new CardInstance(median.Id, 0, null, null), Ctx);
        Assert.InRange(MetaPoints(medianScore), 40, 55);
    }

    [Fact]
    public void Calibration_holds_without_local_notes()
    {
        var dir = MakeVariant(withLocal: false);
        try { AssertCalibrated(dir, "variante publique"); }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// Le cas réel de Mio : ses notes locales sont sur une autre échelle. Réduites à 30 % autour de
    /// 60, la meilleure carte passe de 100 à 72 — l'ancien calibrage la sortait en B. Le rang, lui,
    /// ne bouge pas : elle doit rester A/S.
    /// </summary>
    [Fact]
    public void Calibration_holds_with_a_compressed_local_variant()
    {
        var dir = MakeVariant(withLocal: true, rescale: c => 60 + (c - 60) * 0.3);
        try { AssertCalibrated(dir, "variante locale compressée"); }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// Régression signalée par le chef : un data/meta/local/ PARTIEL (notes sous licence) ne doit
    /// pas masquer archetypes.json, qui n'existe que dans la variante commitée.
    /// </summary>
    [Fact]
    public void Partial_local_dir_falls_back_file_by_file()
    {
        var publicDir = ScoringData.ResolveMetaDir();
        var tmp = Path.Combine(Path.GetTempPath(), "ultima-meta-" + Guid.NewGuid().ToString("N"));
        var localDir = Path.Combine(tmp, "local");
        Directory.CreateDirectory(localDir);

        try
        {
            // local/ ne contient QUE les deux fichiers sous licence.
            File.Copy(Path.Combine(publicDir, "card_tiers.json"), Path.Combine(localDir, "card_tiers.json"));
            File.Copy(Path.Combine(publicDir, "relic_tiers.json"), Path.Combine(localDir, "relic_tiers.json"));
            File.Copy(Path.Combine(publicDir, "archetypes.json"), Path.Combine(tmp, "archetypes.json"));

            var meta = MetaTable.Load(tmp);

            Assert.NotEmpty(meta.Archetypes("CHARACTER.DEFECT"));   // repli sur le fichier commité
            Assert.True(meta.CardCount > 400);
            Assert.NotEqual("empty", meta.Variant);                 // la variante est déclarée par le fichier
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* nettoyage best effort */ }
        }
    }
}
