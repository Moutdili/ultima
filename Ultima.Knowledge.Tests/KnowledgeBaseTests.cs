using System.Diagnostics;
using System.Text.Json;
using Ultima.Engine.Contracts;
using Xunit;

namespace Ultima.Knowledge.Tests;

public class KnowledgeBaseTests
{
    private static readonly KnowledgeBase Kb = new(BuilderTests.BuildFixture());

    [Fact]
    public void Implements_the_engine_contract()
    {
        IKnowledge k = Kb;
        var c = k.Card("CARD.HOLLOW_HEX")!;
        Assert.Equal(("Maléfice creux", true, "Curse", -1), (c.Name, c.IsCurse, c.Type, c.Cost));
        Assert.Equal(["Eternal", "Unplayable", "Ethereal"], c.Keywords);
        Assert.Equal("Cloche de cuivre", k.Relic("RELIC.COPPER_BELL")!.Name);
        Assert.Equal("Lueur", k.Enchantment("ENCHANTMENT.GLEAM")!.Name);
        Assert.Equal("Rare", k.Potion("POTION.FOG_TONIC")!.Rarity);
        Assert.Equal("Golem de verre", k.EncounterName("ENCOUNTER.GLASS_GOLEM_BOSS"));
        Assert.Equal("La Vagabonde", k.CharacterName("CHARACTER.WANDERER"));
    }

    [Fact]
    public void Lookup_tolerates_case_missing_category_and_class_names()
    {
        Assert.Equal("CARD.EMBER_SLASH", Kb.Card("card.ember_slash")!.Id);
        Assert.Equal("CARD.EMBER_SLASH", Kb.Card("EMBER_SLASH")!.Id);
        Assert.Equal("CARD.EMBER_SLASH", Kb.Card("EmberSlash")!.Id);
    }

    [Fact]
    public void Unknown_ids_return_null_and_are_reported()
    {
        var kb = new KnowledgeBase(BuilderTests.BuildFixture());
        Assert.Null(kb.Card("CARD.DOES_NOT_EXIST"));
        Assert.Null(kb.EncounterName("ENCOUNTER.NOPE"));
        Assert.Equal(["CARD.DOES_NOT_EXIST", "ENCOUNTER.NOPE"], kb.MissingIds);
    }

    [Fact]
    public void Upgraded_card_name_cost_text_and_keywords()
    {
        var c = Kb.Card("CARD.QUICK_STUDY", 1)!;
        Assert.Equal("Étude rapide+", c.Name);
        Assert.Equal(0, c.Cost);
        Assert.Equal(["Retain"], c.Keywords);
        Assert.StartsWith("Gardé.", c.Description);
        // Une carte non améliorable garde son nom.
        Assert.Equal("Maléfice creux", Kb.Card("CARD.HOLLOW_HEX", 1)!.Name);
    }

    [Theory]
    [InlineData(1, "Au premier usage de ce jeton, tirez 1 jeton et obtenez ⚡.", "Tirez 1 jeton.")]
    [InlineData(2, "Au premier usage de ce jeton, tirez 2 jetons et obtenez ⚡⚡.", "Tirez 2 jetons.")]
    public void Enchantment_with_real_amount(int amount, string description, string cardText)
    {
        Assert.Equal(description, Kb.Enchantment("ENCHANTMENT.GLEAM", amount)!.Description);
        Assert.Equal(cardText, Kb.EnchantmentCardText("ENCHANTMENT.GLEAM", amount));
        Assert.Null(Kb.EnchantmentCardText("ENCHANTMENT.BRACED", amount));
        Assert.Equal($"Dressez {amount} Remparts.", Kb.Enchantment("ENCHANTMENT.BRACED", amount)!.Description);
    }

    [Fact]
    public void Labels_and_fallback_listing()
    {
        Assert.Equal("Consumé", Kb.KeywordName("Exhaust"));
        Assert.Equal("Assaut", Kb.CardTypeLabel("Attack"));
        Assert.Equal("Fléau", Kb.RarityLabel("card", "Curse"));
        Assert.Equal("Relique de boutique", Kb.RarityLabel("relic", "Shop"));
        Assert.Equal("Skill", Kb.CardTypeLabel("Skill")); // libellé absent → valeur canonique
        Assert.Equal(["CARD.ENGLISH_ONLY", "CARD.NO_TEXT", "ENCOUNTER.MOSS_RATS_WEAK"],
            Kb.Fallbacks.Select(f => f.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Load_from_file_is_fast_and_rejects_future_schema()
    {
        var dir = Directory.CreateTempSubdirectory("ultima-kb-test");
        try
        {
            var doc = BuilderTests.BuildFixture();
            var path = Path.Combine(dir.FullName, "kb.json");
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(doc, KbJson.Options));
            var sw = Stopwatch.StartNew();
            var kb = KnowledgeBase.Load(path);
            Assert.True(sw.ElapsedMilliseconds < 300, $"chargement {sw.ElapsedMilliseconds} ms");
            Assert.Equal("Taillade de braise", kb.Card("CARD.EMBER_SLASH")!.Name);

            doc.Schema = KbDocument.CurrentSchema + 1;
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(doc, KbJson.Options));
            Assert.Throws<InvalidDataException>(() => KnowledgeBase.Load(path));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Default_path_follows_xdg() =>
        Assert.EndsWith(Path.Combine("ultima", "kb", "kb.json"), KnowledgeBase.DefaultPath);
}
