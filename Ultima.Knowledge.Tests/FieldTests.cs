using System.Diagnostics;
using System.Text.Json;
using Ultima.Knowledge.Build;
using Xunit;

namespace Ultima.Knowledge.Tests;

/// <summary>Test « de terrain » : ignoré automatiquement si le jeu n'est pas installé (CI).</summary>
public sealed class GameFactAttribute : FactAttribute
{
    // Aucun chemin utilisateur en dur : ULTIMA_GAME_DIR, sinon « game » sous la racine du dépôt.
    public static string GameDir =>
        Environment.GetEnvironmentVariable("ULTIMA_GAME_DIR") ?? Path.Combine("..", "..", "game");

    public GameFactAttribute()
    {
        if (!Directory.Exists(Path.Combine(GameDir, "pck", "localization")))
            Skip = $"jeu absent ({GameDir}) : test de terrain ignoré";
    }
}

public class FieldTests
{
    private static readonly Lazy<(KbDocument Doc, string Path, long LoadMs, KnowledgeBase Kb)> Real = new(() =>
    {
        var doc = KbBuilder.Build(new KbBuildInput(GameFactAttribute.GameDir, Path.Combine(RepoRoot(), "data", "catalog"), "v0.107.1"));
        // Écrit hors dépôt (dossier temporaire) : textes du jeu.
        var dir = Directory.CreateTempSubdirectory("ultima-kb-field");
        var path = Path.Combine(dir.FullName, "kb.json");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(doc, KbJson.Options));
        var sw = Stopwatch.StartNew();
        var kb = KnowledgeBase.Load(path);
        return (doc, path, sw.ElapsedMilliseconds, kb);
    });

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Ultima.sln"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("Ultima.sln introuvable");
    }

    // Vérifications ponctuelles de la mission. Les noms attendus (textes MegaCrit) ne sont pas commités :
    // on compare leur empreinte SHA-256 (UTF-8) ; le rapport status/encyclopediste.md cite le résultat.
    [GameFact]
    public void Spot_checks_from_the_mission()
    {
        var kb = Real.Value.Kb;
        var bane = kb.Card("CARD.ASCENDERS_BANE")!;
        Assert.Equal("4023c741a242bf91707f19be612a874326dd02d544c9e34d4d9992bb58c0ef7f", Sha(bane.Name));
        Assert.True(bane.IsCurse);
        Assert.Equal("ff01b6258e151b93fad4fab2c129b0e531f7521457631a85608245a004f1430c", Sha(kb.Enchantment("ENCHANTMENT.SOWN")!.Name));
        Assert.Equal("6c8b8712382f63357e33018e2f63883cddc0a802e2d01f4c29a037e1fa2f8535", Sha(kb.Enchantment("ENCHANTMENT.SWIFT")!.Name));
    }

    private static string Sha(string s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    [GameFact]
    public void Every_model_has_a_french_name()
    {
        var cov = Real.Value.Doc.Coverage;
        foreach (var cat in new[] { "cards", "relics", "enchantments", "potions", "encounters", "characters" })
        {
            var c = cov.Categories[cat];
            Assert.True(c.Total > 0, cat);
            Assert.True(c.NameFr == c.Total, $"{cat} : {c.NameFr}/{c.Total} ; non résolus : {string.Join(", ", cov.UnresolvedFr[cat])}");
        }
    }

    [GameFact]
    public void No_residual_brace_or_tag_in_any_generated_text()
    {
        var bad = BuilderTests.AllTexts(Real.Value.Doc).Where(t => t.IndexOfAny(['{', '}', '[', ']']) >= 0).ToList();
        Assert.Empty(bad);
    }

    [GameFact]
    public void Enchantments_render_with_real_amounts_and_no_brace()
    {
        var kb = Real.Value.Kb;
        foreach (var e in Real.Value.Doc.Enchantments)
            for (int amount = 0; amount <= 5; amount++)
            {
                var d = kb.Enchantment(e.Id, amount)!.Description;
                Assert.True(d.IndexOfAny(['{', '}', '[', ']']) < 0, $"{e.Id}/{amount} : {d}");
                var x = kb.EnchantmentCardText(e.Id, amount);
                Assert.True(x == null || x.IndexOfAny(['{', '}', '[', ']']) < 0, $"{e.Id}/{amount} : {x}");
            }
        Assert.DoesNotContain("X", kb.Enchantment("ENCHANTMENT.SWIFT", 2)!.Description);
    }

    /// <summary>Chaque identifiant des catalogues du calcul (data/catalog/relics.json, potions.json) est résolu.</summary>
    [GameFact]
    public void Every_catalog_relic_and_potion_id_resolves()
    {
        var kb = Real.Value.Kb;
        foreach (var (file, prop, cat) in new[] { ("relics.json", "relics", "RELIC"), ("potions.json", "potions", "POTION") })
        {
            var path = Path.Combine(RepoRoot(), "data", "catalog", file);
            if (!File.Exists(path)) continue;
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var missing = json.RootElement.GetProperty(prop).EnumerateArray()
                .Select(e => $"{cat}.{e.GetProperty("id").GetString()}")
                .Where(id => cat == "RELIC" ? kb.Relic(id) == null : kb.Potion(id) == null).ToList();
            Assert.True(missing.Count == 0, $"{file} : {string.Join(", ", missing)}");
        }
    }

    [GameFact]
    public void Load_under_300_ms() =>
        Assert.True(Real.Value.LoadMs < 300, $"chargement {Real.Value.LoadMs} ms");
}
