using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Ultima.Saves.Tests;

/// <summary>
/// Decision de Mio (docs/20 §8.2, « probabiliste seulement ») : ces champs ne doivent ni etre lus,
/// ni affiches, ni utilises. Le parseur ne les declare meme pas.
/// </summary>
public class ForbiddenFieldsTests(ITestOutputHelper output)
{
    /// <summary>Noms JSON interdits (save v16).</summary>
    public static readonly string[] ForbiddenJsonNames =
    [
        "normal_encounter_ids", "elite_encounter_ids", "event_ids",
        "normal_encounters_visited", "elite_encounters_visited", "events_visited", "boss_encounters_visited",
        "relic_grab_bag", "shared_relic_grab_bag",
        "rng", "odds",
    ];

    /// <summary>
    /// Formes normalisees (minuscules, sans '_') : attrape aussi bien <c>event_ids</c> que <c>EventIds</c>,
    /// <c>RelicGrabBag</c>, <c>PlayerRng</c>, <c>RunOdds</c>…
    /// </summary>
    private static readonly string[] ForbiddenTokens =
    [
        "normalencounterids", "eliteencounterids", "eventids",
        "encountersvisited", "eventsvisited",
        "relicgrabbag",
        "rng", "odds",
    ];

    private static IEnumerable<string> LibraryFiles() =>
        Directory.EnumerateFiles(TestPaths.LibrarySourceDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".csproj") || f.EndsWith(".json"));

    [Fact]
    public void Aucun_champ_interdit_dans_le_source_de_Ultima_Saves()
    {
        var hits = new List<string>();
        int scanned = 0;
        foreach (var f in LibraryFiles())
        {
            scanned++;
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                var norm = lines[i].ToLowerInvariant().Replace("_", "");
                foreach (var t in ForbiddenTokens)
                    if (norm.Contains(t))
                        hits.Add($"{Path.GetRelativePath(TestPaths.RepoRoot, f)}:{i + 1}: '{t}' <- {lines[i].Trim()}");
                // tout *_visited (sauf visited_map_coords, autorise : position du joueur)
                foreach (Match m in Regex.Matches(lines[i], @"\b\w+_visited\b"))
                    hits.Add($"{Path.GetRelativePath(TestPaths.RepoRoot, f)}:{i + 1}: '{m.Value}'");
            }
        }
        output.WriteLine($"fichiers scannes : {scanned}");
        Assert.True(scanned > 5, "le scan n'a trouve aucun fichier source");
        Assert.True(hits.Count == 0, "Champs interdits trouves :\n" + string.Join("\n", hits));
    }

    private static IEnumerable<Type> LibraryTypes() => typeof(ProfileLocator).Assembly.GetTypes();

    [Fact]
    public void Aucun_DTO_ne_declare_un_nom_JSON_interdit()
    {
        var names = LibraryTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .OfType<string>()
            .ToHashSet();
        output.WriteLine($"noms JSON declares : {names.Count}");
        Assert.True(names.Count > 50);
        Assert.Empty(names.Intersect(ForbiddenJsonNames));
        Assert.DoesNotContain(names, n => n.EndsWith("_visited"));
        Assert.DoesNotContain("seed", names);
    }

    [Fact]
    public void Aucun_DTO_ne_capture_les_champs_inconnus()
    {
        // [JsonExtensionData] ou un JsonElement/JsonNode/dictionnaire capturerait les champs caches.
        var props = LibraryTypes()
            .Where(t => t.Namespace == "Ultima.Saves.Dto")
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .ToList();
        Assert.NotEmpty(props);
        Assert.DoesNotContain(props, p => p.GetCustomAttribute<JsonExtensionDataAttribute>() is not null);
        Assert.DoesNotContain(props, p => p.PropertyType == typeof(JsonElement) || typeof(JsonNode).IsAssignableFrom(p.PropertyType)
            || p.PropertyType == typeof(object) || (p.PropertyType.IsGenericType &&
                p.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>)));
    }

    [Fact]
    public void Une_save_contenant_les_champs_interdits_se_lit_sans_les_exposer()
    {
        // On injecte des valeurs sentinelles dans la fixture et on verifie qu'aucune n'atteint le modele.
        var path = Path.Combine(TestPaths.FixtureSaves, "current_run.save");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        const string sentinel = "SENTINELLE_INTERDITE";
        foreach (var n in ForbiddenJsonNames) root[n] = new JsonArray(sentinel);
        var player = root["players"]![0]!.AsObject();
        player["rng"] = new JsonObject { ["seed"] = sentinel };
        player["odds"] = new JsonObject { ["x"] = sentinel };
        player["relic_grab_bag"] = new JsonObject { ["x"] = sentinel };
        var rooms = root["acts"]![0]!["rooms"]!.AsObject();
        rooms["normal_encounter_ids"] = new JsonArray("ENCOUNTER." + sentinel);
        rooms["event_ids"] = new JsonArray("EVENT." + sentinel);
        rooms["events_visited"] = 42;

        var state = RunSaveReader.Parse(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()), "injected");
        var dumped = JsonSerializer.Serialize(state);
        Assert.DoesNotContain(sentinel, dumped);
        Assert.Equal(99, state.Players[0].Gold);
    }
}
