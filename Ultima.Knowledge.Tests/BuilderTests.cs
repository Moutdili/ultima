using System.Text.Json;
using Ultima.Knowledge.Build;
using Xunit;

namespace Ultima.Knowledge.Tests;

/// <summary>Construction de la base sur les fixtures synthétiques (même format que le jeu).</summary>
public class BuilderTests
{
    internal static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    internal static KbDocument BuildFixture() => KbBuilder.Build(new KbBuildInput(
        Path.Combine(FixtureDir, "fake-game"), Path.Combine(FixtureDir, "catalog"), "v0.0.0-test",
        new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero)));

    private static readonly KbDocument Doc = BuildFixture();

    private static KbCard Card(string id) => Doc.Cards.Single(c => c.Id == id);

    [Fact]
    public void Metadata_and_traceability()
    {
        Assert.Equal("v0.0.0-test", Doc.GameVersion);
        Assert.Equal("2026-10-29T00:00:00Z", Doc.GeneratedAt);
        Assert.Contains("localization/fra/cards.json", Doc.Source.Hashes.Keys);
        Assert.Contains("catalog/cards.json", Doc.Source.Hashes.Keys);
    }

    [Fact]
    public void Abstract_mock_and_non_model_classes_are_excluded()
    {
        Assert.Equal(["CARD.EMBER_SLASH", "CARD.ENGLISH_ONLY", "CARD.HOLLOW_HEX", "CARD.NO_TEXT", "CARD.QUICK_STUDY", "CARD.STAR_TOSS"],
            Doc.Cards.Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["RELIC.COPPER_BELL", "RELIC.MIRROR_SHARD"],
            Doc.Relics.Where(r => r.LocalizationOnly != true).Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Doc.Cards, c => c.Id.Contains("MOCK"));
        Assert.Equal(["ENCHANTMENT.BRACED", "ENCHANTMENT.GLEAM"], Doc.Enchantments.Select(c => c.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Card_values_from_source_vars_and_upgrade()
    {
        var c = Card("CARD.EMBER_SLASH");
        Assert.Equal("Taillade de braise", c.Name);
        Assert.Equal("fra", c.NameSource);
        Assert.Equal("Ember Slash", c.NameEn);
        Assert.Equal("Brûlez pour 7.\nTirez 1 jeton.", c.Description);
        Assert.Equal("Brûlez pour 10.\nTirez 2 jetons.", c.DescriptionUpgraded);
        Assert.Equal(("Attack", "Common", 1, "AnyEnemy"), (c.Type, c.Rarity, c.Cost, c.Target));
        Assert.Equal("Wanderer", c.Owner); // pool de personnage prioritaire sur Colorless
        Assert.False(c.IsCurse);
    }

    [Fact]
    public void Curse_without_description_gets_keyword_lines_in_game_order()
    {
        var c = Card("CARD.HOLLOW_HEX");
        Assert.True(c.IsCurse);
        Assert.Equal(-1, c.Cost); // la source fait foi (le catalogue écrit 0)
        Assert.Equal("Curse", c.Owner);
        Assert.Equal("none", c.DescriptionSource);
        Assert.Equal("Injouable test.\nFugace.\nPerpétuel.", c.Description);
        Assert.Null(c.DescriptionUpgraded);
        Assert.Equal(0, c.MaxUpgradeLevel);
    }

    [Fact]
    public void Upgrade_changes_cost_keywords_energy_and_show()
    {
        var c = Card("CARD.QUICK_STUDY");
        Assert.Equal("Obtenez ⚡.\nPolissez un caillou.\nConsumé.", c.Description);
        Assert.Equal("Gardé.\nObtenez ⚡⚡.\nPolissez TOUS les cailloux.", c.DescriptionUpgraded);
        Assert.Equal(0, c.CostUpgraded);
        Assert.Equal(["Retain"], c.KeywordsUpgraded);
        Assert.Equal("calculated", c.Vars.Single(v => v.Name == "CalculatedCards").Kind);
    }

    [Fact]
    public void Const_keys_stars_x_cost_and_conditions()
    {
        var c = Card("CARD.STAR_TOSS");
        Assert.True(c.CostX);
        Assert.Equal("Obtenez ★★.\nVaut 0 ⚡.\nTapez 3 fois.", c.Description);
    }

    [Fact]
    public void Fallbacks_en_then_id_are_signalled()
    {
        var en = Card("CARD.ENGLISH_ONLY");
        Assert.Equal(("English Only", "eng", "Raise 5 Shield.", "eng"), (en.Name, en.NameSource, en.Description, en.DescriptionSource));
        var id = Card("CARD.NO_TEXT");
        Assert.Equal(("NO_TEXT", "id", "", "none"), (id.Name, id.NameSource, id.Description, id.DescriptionSource));
        Assert.Contains(Doc.Coverage.Notes, n => n.StartsWith("CARD.NO_TEXT : absent du catalogue"));
        Assert.Equal(["CARD.ENGLISH_ONLY (eng)", "CARD.NO_TEXT (id)"], Doc.Coverage.UnresolvedFr["cards"]);
        Assert.Equal(["ENCOUNTER.MOSS_RATS_WEAK (eng)"], Doc.Coverage.UnresolvedFr["encounters"]);
    }

    [Fact]
    public void Localization_only_titles_are_added_and_flagged()
    {
        var l = Doc.Relics.Single(r => r.Id == "RELIC.LOST_LANTERN");
        Assert.True(l.LocalizationOnly);
        Assert.Equal(("Lanterne égarée", "fra", "Ramassez X Écus.", "None"), (l.Name, l.NameSource, l.Description, l.Rarity));
        Assert.Equal(["Gold"], l.UnresolvedVars);
        Assert.Contains(Doc.Relics, r => r.Id == "RELIC.SEA_GLASS_LIKE");
        Assert.DoesNotContain(Doc.Relics, r => r.Id.Contains("WANDERER"));
        Assert.Equal(2, Doc.Coverage.Categories["relics"].Total); // modèles seulement
        Assert.Equal(2, Doc.Coverage.Categories["relics"].LocalizationOnly);
        Assert.Equal(["RELIC.LOST_LANTERN (fra)", "RELIC.SEA_GLASS_LIKE (fra)"], Doc.Coverage.LocalizationOnly["relics"]);
        Assert.Equal(["ENCOUNTER.MOSS_RATS_WEAK (eng)"], Doc.Coverage.UnresolvedFr["encounters"]);
    }

    [Fact]
    public void Coverage_is_counted_per_category()
    {
        var c = Doc.Coverage.Categories["cards"];
        Assert.Equal((6, 4, 1, 1), (c.Total, c.NameFr, c.NameEnFallback, c.NameIdFallback));
        Assert.Equal((3, 1, 2), (c.DescriptionFr, c.DescriptionEnFallback, c.DescriptionNone)); // HOLLOW_HEX et NO_TEXT sans texte
        Assert.Equal(2, Doc.Coverage.Categories["enchantments"].NameFr);
        var md = CoverageReport.ToMarkdown(Doc);
        Assert.Contains("| cards | 6 | 4 (66,7 %)", md.Replace('.', ','));
    }

    [Fact]
    public void Relic_field_backed_values_string_vars_and_model_title_refs()
    {
        var bell = Doc.Relics.Single(r => r.Id == "RELIC.COPPER_BELL");
        Assert.Equal("Au lever des 3 prochaines aubes, ramassez 15 Écus.", bell.Description);
        Assert.Equal(("Shop", "Shared"), (bell.Rarity, bell.Owner));
        var shard = Doc.Relics.Single(r => r.Id == "RELIC.MIRROR_SHARD");
        Assert.Equal("Reflétez un jeton de départ. Ornez de Lueur. Touchez 25 % d'écus en plus.", shard.Description);
        Assert.Equal("None", shard.Owner);
        Assert.Null(shard.UnresolvedVars);
    }

    [Fact]
    public void Potion_owner_through_epoch_and_formatters()
    {
        var p = Doc.Potions.Single();
        Assert.Equal(("POTION.FOG_TONIC", "Tonique de brume", "Rare", "Wanderer", "CombatOnly"), (p.Id, p.Name, p.Rarity, p.Owner, p.Usage));
        Assert.Equal("Les brumes masquent 30% du paysage pendant 4 jours. Opacité 25%, écart 3.", p.Description);
    }

    [Fact]
    public void Enchantment_amount_is_left_symbolic_without_a_card()
    {
        var g = Doc.Enchantments.Single(e => e.Id == "ENCHANTMENT.GLEAM");
        Assert.Equal("Lueur", g.Name);
        Assert.True(g.ShowAmount);
        Assert.Equal("Au premier usage de ce jeton, tirez X jetons et obtenez X⚡.", g.Description);
        Assert.Equal("Tirez X jetons.", g.ExtraCardText);
        Assert.Null(g.UnresolvedVars); // Amount est attendu, pas une lacune
        var b = Doc.Enchantments.Single(e => e.Id == "ENCHANTMENT.BRACED");
        Assert.Equal("amount", b.Vars.Single().Kind);
        Assert.Equal("Dressez X Remparts.", b.Description);
    }

    [Fact]
    public void Encounters_characters_keywords_and_labels()
    {
        Assert.Equal("Golem de verre", Doc.Encounters.Single(e => e.Id == "ENCOUNTER.GLASS_GOLEM_BOSS").Name);
        Assert.Equal("La Vagabonde", Doc.Characters.Single().Name);
        Assert.Equal("Consumé", Doc.Keywords["Exhaust"].Fr);
        Assert.Equal("Assaut", Doc.Labels["CARD_TYPE.ATTACK"].Fr);
    }

    [Fact]
    public void No_brace_or_bbcode_in_any_generated_text()
    {
        foreach (var t in AllTexts(Doc))
        {
            Assert.DoesNotContain('{', t);
            Assert.DoesNotContain('}', t);
            Assert.DoesNotContain('[', t);
            Assert.DoesNotContain(']', t);
        }
    }

    [Fact]
    public void Json_round_trip()
    {
        var json = JsonSerializer.Serialize(Doc, KbJson.Options);
        var back = JsonSerializer.Deserialize<KbDocument>(json, KbJson.Options)!;
        Assert.Equal(Doc.Cards.Count, back.Cards.Count);
        Assert.Equal(Card("CARD.QUICK_STUDY").DescriptionUpgraded, back.Cards.Single(c => c.Id == "CARD.QUICK_STUDY").DescriptionUpgraded);
    }

    internal static IEnumerable<string> AllTexts(KbDocument d)
    {
        foreach (var e in d.Cards.Cast<KbEntry>().Concat(d.Relics).Concat(d.Potions).Concat(d.Enchantments))
        {
            yield return e.Name;
            yield return e.Description;
            if (e is KbCard { DescriptionUpgraded: { } u }) yield return u;
            if (e is KbEnchantment { ExtraCardText: { } x }) yield return x;
        }
        foreach (var n in d.Encounters.Concat(d.Characters)) yield return n.Name;
    }
}
