using System.Text.Json;
using Ultima.Engine.Scoring;
using Xunit;

namespace Ultima.Engine.Tests;

/// <summary>
/// Invariant des tags de reliques : **aucune relique sans tag**. Le scoring des reliques
/// (RelicScorer : Needs, Fit au deck, synergie) lit ces tags ; une relique sans tag est
/// silencieusement sous-évaluée. Le registre de dérivation <c>relic_tags.json</c> est la
/// source de vérité du vocabulaire et des règles (tools/index_relic_tags.py).
/// </summary>
public sealed class RelicTagCoverageTests
{
    private const int MaxTagsPerRelic = 8;
    private const int MaxNoEffect = 20;

    private static JsonElement Catalog()
    {
        var path = Path.Combine(ScoringData.ResolveCatalogDir(), "relics.json");
        Assert.True(File.Exists(path), $"relics.json introuvable : {path}");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.Clone();
    }

    private static JsonElement Ledger()
    {
        var path = Path.Combine(ScoringData.ResolveCatalogDir(), "relic_tags.json");
        Assert.True(File.Exists(path), $"relic_tags.json introuvable : {path}");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.Clone();
    }

    private static List<string> Tags(JsonElement relic) =>
        relic.GetProperty("tags").EnumerateArray().Select(t => t.GetString() ?? "").ToList();

    private static HashSet<string> Vocabulary()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in Ledger().GetProperty("vocabulary").EnumerateArray())
            set.Add(v.GetProperty("tag").GetString() ?? "");
        return set;
    }

    [Fact]
    public void Every_relic_has_at_least_one_tag()
    {
        var relics = Catalog().GetProperty("relics");
        Assert.True(relics.GetArrayLength() >= 300, $"reliques vues : {relics.GetArrayLength()}");

        var without = new List<string>();
        foreach (var relic in relics.EnumerateArray())
        {
            var id = relic.GetProperty("id").GetString();
            var tags = Tags(relic);
            if (tags.Count == 0) without.Add(id ?? "?");
            Assert.DoesNotContain(tags, string.IsNullOrWhiteSpace);
        }

        Assert.True(without.Count == 0,
            $"{without.Count} reliques sans tag : {string.Join(", ", without.Take(20))}");
    }

    [Fact]
    public void No_relic_is_unknown_to_the_tag_ledger()
    {
        var catalogIds = Catalog().GetProperty("relics").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString() ?? "").ToHashSet(StringComparer.Ordinal);

        var ledgerRelics = Ledger().GetProperty("relics").EnumerateArray().ToList();
        var ledgerIds = ledgerRelics.Select(r => r.GetProperty("id").GetString() ?? "")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(catalogIds.Count, ledgerIds.Count);
        Assert.True(catalogIds.SetEquals(ledgerIds),
            $"écart catalogue/registre : {string.Join(", ", catalogIds.Except(ledgerIds).Take(10))}");

        foreach (var relic in ledgerRelics)
        {
            var id = relic.GetProperty("id").GetString();
            var status = relic.GetProperty("status").GetString();
            Assert.True(status is "tagged" or "no_effect", $"{id} : statut inconnu « {status} »");
        }

        // Un no_effect doit être justifié ET citer la description : pas de tag vide muet.
        var noEffectIds = Ledger().GetProperty("no_effect_relics").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString() ?? "").ToHashSet(StringComparer.Ordinal);
        foreach (var relic in ledgerRelics)
        {
            var id = relic.GetProperty("id").GetString() ?? "";
            if (relic.GetProperty("status").GetString() != "no_effect") continue;
            Assert.Contains(id, noEffectIds);
        }

        foreach (var entry in Ledger().GetProperty("no_effect_relics").EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString();
            var reason = entry.GetProperty("reason").GetString();
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{id} : no_effect sans raison");
            var desc = entry.GetProperty("description_fr").GetString();
            Assert.False(string.IsNullOrWhiteSpace(desc), $"{id} : no_effect sans description citée");
            Assert.True(catalogIds.Contains(id!), $"{id} : no_effect sur une relique inconnue du catalogue");
        }
    }

    [Fact]
    public void Tags_are_declared_and_bounded()
    {
        var vocabulary = Vocabulary();
        Assert.True(vocabulary.Count >= 20, $"vocabulaire : {vocabulary.Count} tags");

        var max = Ledger().GetProperty("max_tags").GetInt32();
        Assert.True(max is > 0 and <= MaxTagsPerRelic, $"max_tags inattendu : {max}");

        foreach (var relic in Catalog().GetProperty("relics").EnumerateArray())
        {
            var id = relic.GetProperty("id").GetString();
            var tags = Tags(relic);
            Assert.True(tags.Count <= max, $"{id} : {tags.Count} tags > {max}");
            Assert.Equal(tags.Count, tags.Distinct(StringComparer.Ordinal).Count());
            foreach (var tag in tags)
                Assert.True(vocabulary.Contains(tag), $"{id} : tag hors vocabulaire « {tag} »");
        }
    }

    [Fact]
    public void Every_rule_has_an_example_and_a_witness()
    {
        var byId = Catalog().GetProperty("relics").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetString() ?? "", Tags, StringComparer.Ordinal);
        var vocabulary = Vocabulary();

        foreach (var rule in Ledger().GetProperty("rules").EnumerateArray())
        {
            var tag = rule.GetProperty("tag").GetString() ?? "";
            Assert.True(vocabulary.Contains(tag), $"règle hors vocabulaire : {tag}");
            Assert.NotEmpty(rule.GetProperty("patterns").EnumerateArray());
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("example").GetString()),
                $"{tag} : règle sans exemple de description");

            var witness = rule.GetProperty("witness_relic").GetString();
            Assert.False(string.IsNullOrWhiteSpace(witness), $"{tag} : règle sans témoin (règle morte)");
            Assert.True(byId.TryGetValue(witness!, out var tags), $"{tag} : témoin inconnu {witness}");
            Assert.Contains(tag, tags!);
        }
    }

    [Fact]
    public void Coverage_is_explicit_and_no_effect_stays_an_exception()
    {
        var coverage = Ledger().GetProperty("coverage");
        var relicCount = Catalog().GetProperty("relics").GetArrayLength();

        var taggedAfter = coverage.GetProperty("tagged_after").GetInt32();
        var noEffect = coverage.GetProperty("no_effect").GetInt32();

        Assert.Equal(relicCount, coverage.GetProperty("relics").GetInt32());
        Assert.Equal(relicCount, taggedAfter + noEffect);
        Assert.True(noEffect <= MaxNoEffect,
            $"{noEffect} reliques no_effect : au-delà de la borne de {MaxNoEffect}, la dérivation FR a régressé");
        Assert.True(coverage.GetProperty("tagged_before").GetInt32() < relicCount,
            "tagged_before doit refléter le catalogue d'origine, pas le résultat complété");

        var noEffectIds = Ledger().GetProperty("no_effect_relics").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString() ?? "").ToHashSet(StringComparer.Ordinal);
        Assert.Equal(noEffect, noEffectIds.Count);

        foreach (var relic in Catalog().GetProperty("relics").EnumerateArray())
        {
            var id = relic.GetProperty("id").GetString() ?? "";
            var isNoEffect = Tags(relic).Contains("no_effect");
            Assert.Equal(noEffectIds.Contains(id), isNoEffect);
        }
    }
}
