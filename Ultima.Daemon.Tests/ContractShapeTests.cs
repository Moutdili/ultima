using System.Text.Json.Nodes;
using Ultima.Daemon.Knowledge;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;
using Ultima.Saves;

namespace Ultima.Daemon.Tests;

/// <summary>La sérialisation d'EngineState a la même FORME que docs/22-state.sample.json.</summary>
public class ContractShapeTests
{
    private static JsonNode Sample() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.RepoRoot(), "docs", "22-state.sample.json")))!;

    /// <summary>Compare deux arbres : mêmes clés aux mêmes endroits, mêmes types JSON (null compatible avec tout).</summary>
    private static void SameShape(JsonNode? expected, JsonNode? actual, string path, List<string> errors, bool checkValues)
    {
        if (expected is null || actual is null) return;
        switch (expected)
        {
            case JsonObject eo when actual is JsonObject ao:
                foreach (var (k, v) in eo)
                {
                    if (!ao.ContainsKey(k)) { errors.Add($"{path}.{k} absent"); continue; }
                    SameShape(v, ao[k], $"{path}.{k}", errors, checkValues);
                }
                foreach (var (k, _) in ao)
                    if (!eo.ContainsKey(k) && !AdditiveField(path, k, ao)) errors.Add($"{path}.{k} en trop");
                break;
            case JsonArray ea when actual is JsonArray aa:
                if (checkValues && ea.Count != aa.Count) errors.Add($"{path} : {ea.Count}≠{aa.Count} éléments");
                for (int i = 0; i < Math.Min(ea.Count, aa.Count); i++) SameShape(ea[i], aa[i], $"{path}[{i}]", errors, checkValues);
                break;
            case JsonValue ev when actual is JsonValue av:
                if (ev.GetValueKind() != av.GetValueKind()
                    && !(ev.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                         && av.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                    errors.Add($"{path} : type {ev.GetValueKind()}≠{av.GetValueKind()}");
                else if (checkValues && path != "$.at" && ev.ToJsonString() != av.ToJsonString())
                    errors.Add($"{path} : {ev.ToJsonString()}≠{av.ToJsonString()}");
                break;
            default:
                errors.Add($"{path} : {expected.GetValueKind()}≠{actual.GetValueKind()}");
                break;
        }
    }

    /// <summary>
    /// Champs ADDITIFS des versions mineures que docs/22-state.sample.json ne montre pas encore. Seuls ceux-ci
    /// sont tolérés en plus (signalé au chef dans status/cableur.md) :
    /// v1.1 (8abdeb1) CardView.description, PotionView.score ;
    /// v1.2 (89e376e) contractMinor, map, route, event, RunView.party, Score.deltaG, ScoreTerm.evidence ;
    /// v1.3 (be593ee) OverlaySettings.hud ; v1.4 (docs/44) custom, ModuleSetting.screens.
    /// </summary>
    private static bool AdditiveField(string path, string key, JsonObject obj) => key switch
    {
        "description" => obj.ContainsKey("isCurse"),
        "score" => path.Contains(".potions["),
        "contractMinor" or "map" or "route" or "event" => path == "$",
        "party" => path == "$.run",
        "hud" => path == "$.settings", // v1.3
        "custom" => path == "$", // v1.4
        "screens" => path.StartsWith("$.settings.modules["), // v1.4
        "combat" or "restSite" => path == "$", // v1.5
        "costKind" => obj.ContainsKey("isCurse"), // v1.5
        "slot" => path.Contains(".items["), // v1.5
        "inventoryOpen" => path == "$.shop", // v1.5
        "community" => obj.ContainsKey("confidence"), // v1.5
        "deltaG" => obj.ContainsKey("confidence"),
        "evidence" => obj.ContainsKey("points"),
        _ => false,
    };

    [Fact]
    public void Sample_round_trips_identically()
    {
        var sample = Sample();
        var state = StateJson.Deserialize(sample.ToJsonString())!;
        var again = JsonNode.Parse(StateJson.Serialize(state))!;
        var errors = new List<string>();
        SameShape(sample, again, "$", errors, checkValues: true);
        Assert.Empty(errors);
        Assert.Equal("Shop", again["screen"]!.GetValue<string>());           // enum en chaîne
        Assert.True(again["paths"] is null && ((JsonObject)again).ContainsKey("paths")); // null explicite
    }

    [Fact]
    public void Daemon_state_from_a_real_save_has_the_sample_shape()
    {
        // save anonymisée de la fixture (Silent, Underdocks) → état assemblé par le daemon
        var paths = ProfileLocator.Locate(Path.Combine(Fixtures.RepoRoot(), "fixtures", "saves", "root"));
        var run = new RunSaveReader().ReadCurrent(paths);
        var asm = new StateAssembler(NullKnowledge.Instance, null, null, paths.SteamId64);
        var part = asm.BuildRun(run)!;
        var shop = asm.BuildShop(FakeMirror.Shop(), part, 99);
        var state = StateAssembler.Compose(DateTimeOffset.Now,
            new SourceHealth(true, true, true, 108, null), Screen.Shop, part, null, shop, OverlaySettings.Default);
        var json = JsonNode.Parse(StateJson.Serialize(state))!;

        var errors = new List<string>();
        SameShape(Sample(), json, "$", errors, checkValues: false);
        Assert.Empty(errors);
        Assert.Equal("CHARACTER.SILENT", json["run"]!["characterId"]!.GetValue<string>());
        Assert.Equal(EngineState.CurrentVersion, json["contractVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Score_grade_is_serialized()
    {
        var s = new Score(90, new[] { new ScoreTerm("Méta", 90, "x") }, "high");
        var card = new CardView("CARD.X", "X", "Attack", "Rare", 1, 0, null, false, s);
        var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(card, StateJson.Options))!;
        Assert.Equal("S", json["score"]!["grade"]!.GetValue<string>());
        Assert.True(((JsonObject)json).ContainsKey("description"));
    }
}
