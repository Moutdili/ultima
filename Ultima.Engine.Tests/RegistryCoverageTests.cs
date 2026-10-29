using System.Text.Json;
using Ultima.Engine.Scoring;
using Xunit;

namespace Ultima.Engine.Tests;

/// <summary>
/// Invariant de la base : **aucune entité inconnue**. Le registre est la source de vérité ;
/// ces tests échouent si une carte perd ses effets, si une malédiction n'a plus d'entrée
/// d'impact (mesurée ou explicitement « no_data »), ou si un comptage dérive.
/// </summary>
public sealed class RegistryCoverageTests
{
    private static readonly string[] KnownCardTypes =
    {
        "attack", "skill", "power", "curse", "status", "quest",
    };

    private static JsonElement Registry()
    {
        var path = Path.Combine(ScoringData.ResolveCatalogDir(), "registry.json");
        Assert.True(File.Exists(path), $"registry.json introuvable : {path}");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Registry_covers_every_entity_kind()
    {
        var root = Registry();
        var counts = root.GetProperty("counts");

        Assert.True(counts.GetProperty("cards").GetInt32() >= 570);
        Assert.True(counts.GetProperty("relics").GetInt32() >= 300);
        Assert.True(counts.GetProperty("potions").GetInt32() >= 60);
        Assert.True(counts.GetProperty("power_models").GetInt32() >= 250);
        Assert.True(counts.GetProperty("monsters").GetInt32() >= 110);
        Assert.True(counts.GetProperty("encounters").GetInt32() >= 80);
    }

    [Fact]
    public void No_card_is_missing_its_effects()
    {
        var gaps = Registry().GetProperty("gaps").GetProperty("cards_without_effects");
        Assert.Equal(0, gaps.GetArrayLength());
    }

    [Fact]
    public void Every_curse_and_status_has_an_explicit_impact_status()
    {
        var cards = Registry().GetProperty("entities").GetProperty("card");
        var checkedCount = 0;
        foreach (var card in cards.EnumerateArray())
        {
            var type = card.GetProperty("type").GetString();
            if (type is not ("curse" or "status")) continue;
            checkedCount++;
            Assert.True(card.TryGetProperty("impact", out var impact),
                $"{card.GetProperty("id").GetString()} n'a pas de statut d'impact");
            var value = impact.GetString();
            // « effect » = classé par l'effet décrit (docs/37), « no_data » = aucune donnée.
            Assert.True(value is "measured" or "effect" or "no_data",
                $"{card.GetProperty("id").GetString()} : impact inattendu « {value} »");
        }

        Assert.True(checkedCount >= 30, $"seulement {checkedCount} malédictions/statuts vus");
    }

    [Fact]
    public void Card_types_are_known_and_ids_are_unique()
    {
        var cards = Registry().GetProperty("entities").GetProperty("card");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cards.EnumerateArray())
        {
            var id = card.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.True(ids.Add(id!), $"identifiant dupliqué : {id}");
            Assert.Contains(card.GetProperty("type").GetString(), KnownCardTypes);
        }
    }

    [Fact]
    public void Relics_and_potions_have_a_stable_identity()
    {
        var entities = Registry().GetProperty("entities");
        foreach (var kind in new[] { "relic", "potion" })
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in entities.GetProperty(kind).EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(id));
                Assert.True(ids.Add(id!), $"{kind} dupliqué : {id}");
            }
        }
    }
}
