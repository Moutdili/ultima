using System.Text.Json.Nodes;
using Ultima.Daemon.Custom;
using Ultima.Daemon.Knowledge;
using Ultima.Daemon.Mirror;
using Ultima.Daemon.Publish;
using Ultima.Daemon.Settings;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;
using Ultima.Saves;
using Ultima.Saves.Log;
using Ultima.Saves.Model;

namespace Ultima.Daemon.Tests;

/// <summary>
/// Contrat v1.4 côté daemon : échantillon, modules déclaratifs (évaluateur borné), réglages (enabled/screens/
/// bornes HUD), événement (options inconnues, fini), enchantements, revue branchée. Aucun test ne lit ni
/// n'écrit les vrais dossiers XDG : tout se passe dans un dossier temporaire.
/// </summary>
public sealed class V14Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ultima-v14-" + Guid.NewGuid().ToString("N")[..8]);

    public V14Tests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private string Dir(string name)
    {
        var d = Path.Combine(_tmp, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static RunState FixtureRun() =>
        new RunSaveReader().ReadCurrent(ProfileLocator.Locate(Path.Combine(Fixtures.RepoRoot(), "fixtures", "saves", "root")))!;

    /// <summary>État minimal avec une run (pour les chemins des modules).</summary>
    private static EngineState State(int gold = 134, int floor = 5, Screen screen = Screen.Map, int hp = 70) =>
        new(EngineState.CurrentVersion, DateTimeOffset.Now, new SourceHealth(false, true, false, null, null), screen,
            new RunView("CHARACTER.SILENT", "Silent", 0, null, null, null, 0, hp, 73, gold, floor,
                new[] { new CardView("CARD.STRIKE_SILENT", "Frappe", "Attack", "Basic", 1, 0, null, false, null) },
                Array.Empty<RelicView>(), Array.Empty<PotionView>()) { Party = Array.Empty<AllyView>() },
            null, null, null, null, OverlaySettings.Default);

    // ---------------- échantillon v1.4 (point 8) ----------------

    private static JsonNode Sample14() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.RepoRoot(), "docs", "ipc", "state.v14.sample.json")))!;

    [Fact]
    public void V14_sample_is_reproduced_identically_by_the_daemon_serializer()
    {
        var sample = Sample14();
        var state = StateJson.Deserialize(sample.ToJsonString())!;
        var again = JsonNode.Parse(StateJson.Serialize(state, indented: true))!;

        var errors = new List<string>();
        Same(sample, again, "$", errors);
        Assert.Empty(errors);

        Assert.Equal(ContractInfo.CurrentMinor, again["contractMinor"]!.GetValue<int>());
        var custom = again["custom"]!.AsArray();
        Assert.Equal(2, custom.Count);
        Assert.Equal(new[] { "module", "plugin" }, custom.Select(c => c!["source"]!.GetValue<string>()));
        Assert.Equal("x.or-par-etage", custom[0]!["id"]!.GetValue<string>());
        Assert.All(custom.Select(c => c!["rows"]!.AsArray()), rows => Assert.NotEmpty(rows));
        // « screens » (v1.4) traverse la sérialisation, null explicite compris
        Assert.Null(again["settings"]!["modules"]![0]!["screens"]);
        Assert.Equal(new[] { "reward" },
            again["settings"]!["modules"]![5]!["screens"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    /// <summary>Mêmes clés aux mêmes endroits, mêmes valeurs (null compatible avec null).</summary>
    private static void Same(JsonNode? expected, JsonNode? actual, string path, List<string> errors)
    {
        if (expected is null || actual is null) return;
        switch (expected)
        {
            case JsonObject eo when actual is JsonObject ao:
                foreach (var (k, v) in eo)
                {
                    if (!ao.ContainsKey(k)) { errors.Add($"{path}.{k} absent"); continue; }
                    Same(v, ao[k], $"{path}.{k}", errors);
                }
                foreach (var (k, _) in ao)
                    if (!eo.ContainsKey(k)) errors.Add($"{path}.{k} en trop");
                break;
            case JsonArray ea when actual is JsonArray aa:
                if (ea.Count != aa.Count) { errors.Add($"{path} : {ea.Count}≠{aa.Count} éléments"); break; }
                for (int i = 0; i < ea.Count; i++) Same(ea[i], aa[i], $"{path}[{i}]", errors);
                break;
            case JsonValue ev when actual is JsonValue av:
                if (ev.GetValueKind() != av.GetValueKind()) errors.Add($"{path} : type {ev.GetValueKind()}≠{av.GetValueKind()}");
                else if (path != "$.at" && ev.ToJsonString() != av.ToJsonString())
                    errors.Add($"{path} : {ev.ToJsonString()}≠{av.ToJsonString()}");
                break;
            default:
                errors.Add($"{path} : {expected.GetValueKind()}≠{actual.GetValueKind()}");
                break;
        }
    }

    // ---------------- modules déclaratifs (point 5) ----------------

    private static string FixtureModule() => Path.Combine(Fixtures.Dir, "modules", "x.or-par-etage.json");

    private ModuleStore Store(params (string Name, string Json)[] extra)
    {
        var dir = Dir("modules");
        File.Copy(FixtureModule(), Path.Combine(dir, "x.or-par-etage.json"), overwrite: true);
        foreach (var (name, json) in extra) File.WriteAllText(Path.Combine(dir, name), json);
        var set = SchemaSet.Load();
        Assert.NotNull(set.Module);
        var store = new ModuleStore(dir, set.Module, () => { });
        store.Reload();
        return store;
    }

    [Fact]
    public void Valid_module_renders_paths_filters_and_when()
    {
        using var store = Store();
        var entry = Assert.Single(store.Entries);
        Assert.Null(entry.Problem);

        var views = store.Views(State(gold: 134));
        var view = Assert.Single(views);
        Assert.Equal(CustomHealth.Ok, view.Health);
        Assert.Null(view.Problem);
        Assert.Equal("x.or-par-etage", view.Id);
        Assert.Equal(new[] { "map", "shop" }, view.Screens);
        Assert.Equal(new[] { "134 or · étage 5", "PV 70 % · deck 1" }, view.Rows.Select(r => r.Text));
        Assert.Equal("accent", view.Rows[0].Tone);

        // 150 or : la ligne conditionnelle apparaît
        var shop = store.Views(State(gold: 150, screen: Screen.Shop)).Single();
        Assert.Equal(new[] { "150 or · étage 5", "Shop rentable", "PV 70 % · deck 1" }, shop.Rows.Select(r => r.Text));

        // run absente : « — », jamais 0 ni un chemin affiché
        var noRun = store.Views(State() with { Run = null }).Single();
        Assert.Equal("— or · étage —", noRun.Rows[0].Text);
    }

    [Fact]
    public void Module_evaluator_is_bounded_and_never_executes_code()
    {
        var state = State();
        Assert.Equal("—", ModuleParser.RenderTemplate(state, ModuleParser.ParseTemplate("{run.nope}")));
        Assert.Equal("—", ModuleParser.RenderTemplate(state, ModuleParser.ParseTemplate("{run.deck[99].name}")));
        Assert.Equal("—", ModuleParser.RenderTemplate(state, ModuleParser.ParseTemplate("{a.b.c.d.e.f.g.h.i}")));
        Assert.Equal("$134", ModuleParser.RenderTemplate(state, ModuleParser.ParseTemplate("${run.gold}")));
        Assert.Equal("134", ModuleParser.RenderTemplate(state, ModuleParser.ParseTemplate("{run.gold}")));
        Assert.Equal("CHARACTER.SILENT", ModuleParser.RenderTemplate(state, ModuleParser.ParseTemplate("{run.characterId|upper}")));
        Assert.Equal("A", ModuleParser.RenderTemplate(state, ModuleParser.ParseTemplate("{run.hp|grade}")));
        Assert.Throws<ModuleFormatException>(() => ModuleParser.ParseTemplate("{run.gold|exec}"));
        Assert.Throws<ModuleFormatException>(() => ModuleParser.ParseTemplate("{run.gold"));
        Assert.Throws<ModuleFormatException>(() => ModuleParser.ParseCondition("({run.gold}) > 3"));
        Assert.Throws<ModuleFormatException>(() => ModuleParser.ParseCondition("{run.gold}"));
        Assert.Throws<ModuleFormatException>(() => ModuleParser.ParseCondition("3 > run.gold"));
        Assert.Throws<ModuleFormatException>(() => ModuleParser.ParseCondition("{run.gold|pct} > 3"));

        // « and » plus fort que « or » : (A et B) ou C
        var c = ModuleParser.ParseCondition("{run.gold} > 1000 and {run.floor} > 0 or screen == Map");
        Assert.True(ModuleParser.Holds(state, c));
        Assert.True(ModuleParser.Holds(state, ModuleParser.ParseCondition("{run.gold} >= 134 and {run.gold} <= 134")));
        // chemin absent ⇒ comparaison fausse, même avec « != »
        Assert.False(ModuleParser.Holds(state, ModuleParser.ParseCondition("{run.nope} != 3")));
    }

    [Fact]
    public void Invalid_modules_are_published_as_error_with_a_reason_never_a_crash()
    {
        using var store = Store(
            ("x.broken.json", "{ pas du json"),
            ("x.schema.json", """{"id":"x.schema","label":"B","api":1,"rows":[{"text":"a","extra":1}]}"""),
            ("x.filter.json", """{"id":"x.filter","label":"F","api":1,"rows":[{"text":"{run.gold|banana}"}]}"""),
            ("x.when.json", """{"id":"x.when","label":"W","api":1,"rows":[{"text":"a","when":"{run.gold} =! 3"}]}"""),
            ("x.api.json", """{"id":"x.api","label":"A","api":2,"rows":[{"text":"a"}]}"""));

        var views = store.Views(State());
        Assert.Equal(6, views.Count);
        Assert.Equal(CustomHealth.Ok, views.Single(v => v.Id == "x.or-par-etage").Health);
        foreach (var bad in views.Where(v => v.Id != "x.or-par-etage"))
        {
            Assert.Equal(CustomHealth.Error, bad.Health);
            Assert.False(string.IsNullOrWhiteSpace(bad.Problem));
            Assert.Empty(bad.Rows);
            Assert.Equal("module", bad.Source);
        }
        Assert.Contains(views, v => v.Problem?.Contains("champ inconnu", StringComparison.Ordinal) == true);
        Assert.Contains(views, v => v.Problem?.Contains("filtre inconnu", StringComparison.Ordinal) == true);
        Assert.Contains(views, v => v.Problem?.Contains("valeur attendue 1", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Modules_reload_when_the_folder_changes()
    {
        var dir = Dir("modules-watch");
        var set = SchemaSet.Load();
        using var changed = new ManualResetEventSlim();
        using var store = new ModuleStore(dir, set.Module, () => changed.Set());
        store.Start();
        Assert.Empty(store.Entries);

        File.WriteAllText(Path.Combine(dir, "x.late.json"), """{"id":"x.late","label":"Tard","api":1,"rows":[{"text":"ok"}]}""");
        Assert.True(changed.Wait(TimeSpan.FromSeconds(5)), "rechargement à chaud non déclenché");
        Assert.Single(store.Entries);
        Assert.Equal("x.late", store.Views(State()).Single().Id);
    }

    // ---------------- réglages (point 3) ----------------

    [Fact]
    public void Settings_enabled_absent_is_true_and_screens_are_read_and_validated()
    {
        var path = Path.Combine(_tmp, "settings.json");
        File.WriteAllText(path, """
            {"modules":[{"id":"deck"},{"id":"curses","enabled":false,"screens":["reward","bogus","reward"]},
                        {"id":"relics","enabled":true,"order":2,"screens":[]}],
             "pluginsEnabled":["x.echo","x.echo","pas-un-id"]}
            """);
        var snap = SettingsStore.TryLoadSnapshot(path, out var err)!;
        Assert.Null(err);
        Assert.True(snap.Settings.Modules.Single(m => m.Id == "deck").Enabled); // absent ⇒ true (v1.4)
        Assert.Null(snap.Settings.Modules.Single(m => m.Id == "deck").Screens);
        var curses = snap.Settings.Modules.Single(m => m.Id == "curses");
        Assert.False(curses.Enabled);
        Assert.Equal(new[] { "reward" }, curses.Screens);                       // inconnu et doublon retirés
        Assert.Null(snap.Settings.Modules.Single(m => m.Id == "relics").Screens); // vide ⇒ null
        Assert.Equal(new[] { "x.echo", "pas-un-id" }, snap.PluginsEnabled);     // dédoublonné, ordre conservé
    }

    [Fact]
    public void Hud_settings_out_of_bounds_are_clamped_to_the_bounds()
    {
        var path = Path.Combine(_tmp, "settings-hud.json");
        File.WriteAllText(path, """{"modules":[],"hud":{"quality":"ULTRA","heightFraction":3,"restOpacity":-1,"side":"top"}}""");
        var s = SettingsStore.TryLoad(path, out _)!;
        Assert.Equal(HudSettings.HeightMax, s.Hud.HeightFraction, 6);
        Assert.Equal(HudSettings.OpacityMin, s.Hud.RestOpacity, 6);
        Assert.Equal("verre", s.Hud.Quality);   // valeur inconnue ⇒ défaut du champ
        Assert.Equal("right", s.Hud.Side);

        File.WriteAllText(path, """{"modules":[],"hud":{"heightFraction":0.05,"restOpacity":4,"quality":"eco"}}""");
        var t = SettingsStore.TryLoad(path, out _)!;
        Assert.Equal(HudSettings.HeightMin, t.Hud.HeightFraction, 6);
        Assert.Equal(HudSettings.OpacityMax, t.Hud.RestOpacity, 6);
        Assert.Equal("eco", t.Hud.Quality);

        File.WriteAllText(path, """{"modules":[],"hud":{"heightFraction":0.6,"restOpacity":0.7}}""");
        var ok = SettingsStore.TryLoad(path, out _)!.Hud;   // dans les bornes : inchangé
        Assert.Equal(0.6, ok.HeightFraction, 6);
        Assert.Equal(0.7, ok.RestOpacity, 6);
    }

    [Fact]
    public void Daemon_never_writes_settings_json()
    {
        var path = Path.Combine(_tmp, "settings-ro.json");
        const string json = """{"modules":[{"id":"deck"}],"pluginsEnabled":["x.echo"],"inconnu":{"gardé":true}}""";
        File.WriteAllText(path, json);
        var before = File.GetLastWriteTimeUtc(path);

        var snap = SettingsStore.TryLoadSnapshot(path, out _)!;
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), null,
            new ManualTimers(), a => a(), _ => { });
        core.OnSettings(snap.Settings);
        core.Publish();

        Assert.Equal(json, File.ReadAllText(path));            // contenu intact, clé inconnue conservée
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));  // pas même une écriture
    }

    // ---------------- événement et enchantement (points 1-2) ----------------

    [Fact]
    public void Finished_event_publishes_a_single_continue_option()
    {
        var asm = new StateAssembler(NullKnowledge.Instance, null, null, null);
        var ev = asm.BuildEvent(new MEvent("EVENT.ABYSSAL_BATHS", null, null) { IsFinished = true }, null)!;
        var option = Assert.Single(ev.Options);
        Assert.Equal(StateAssembler.ContinueLabel, option.Label);
        Assert.Equal(StateAssembler.ContinueOptionId, option.Id);
        Assert.True(option.Available);
        Assert.Null(option.Score);
    }

    [Fact]
    public void Unknown_event_options_publish_nothing_and_trigger_exactly_one_bounded_reread()
    {
        var timers = new ManualTimers();
        var mirror = new FakeMirror
        {
            Respond = n => FakeMirror.Default(n) with
            {
                Event = new MEvent("EVENT.ABYSSAL_BATHS", null, null) { OptionsKnown = false, InEvent = true },
            },
        };
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), mirror, timers,
            a => a(), _ => { });
        core.OnGame(true);
        core.OnRun(FixtureRun());
        core.OnLog(new RoomEntered(default, "", RoomKind.Event, "Event Room"));

        // 1re lecture, puis UNE relecture bornée (anti-rebond 250 ms) — pas davantage
        timers.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, mirror.Probes.Count);
        Assert.Null(core.LastState!.Event);           // options inconnues : rien de publié

        timers.Advance(TimeSpan.FromSeconds(60));     // jamais de boucle
        Assert.Equal(2, mirror.Probes.Count);
        Assert.Null(core.LastState.Event);
    }

    [Fact]
    public void Enchantments_are_filled_in_reward_and_shop_cards()
    {
        var asm = new StateAssembler(NullKnowledge.Instance, null, null, null);
        var run = asm.BuildRun(FixtureRun());
        var rewards = new MRewards(1, new[]
        {
            new MCardReward(false, new[] { new MCard("CARD.SCOURGE", 1, new MEnchant("ENCHANTMENT.SOWN", 3)) }),
        });
        var reward = asm.BuildReward(rewards, run)!;
        var ench = Assert.Single(reward.Offered).Enchantment!;
        Assert.Equal("ENCHANTMENT.SOWN", ench.Id);
        Assert.Equal(3, ench.Amount);
        Assert.False(string.IsNullOrEmpty(ench.Name));
        Assert.NotNull(ench.Description); // texte FR (vide si la base ne le connaît pas)

        var shop = new MShop(true,
            new[] { new MShopCard(new MCard("CARD.SCOURGE", 0, new MEnchant("ENCHANTMENT.SWIFT", 1)), 48, 24, true, false) },
            Array.Empty<MShopItem>(), Array.Empty<MShopItem>(), 75, 75, false, Array.Empty<string>());
        var view = asm.BuildShop(shop, run, 99)!;
        Assert.Equal("ENCHANTMENT.SWIFT", view.Items[0].Card!.Enchantment!.Id);
    }

    // ---------------- revue branchée (point 4) ----------------

    [Fact]
    public async Task Review_builder_is_wired_on_review_open()
    {
        var paths = ProfileLocator.Locate(Path.Combine(Fixtures.RepoRoot(), "fixtures", "saves", "root"));
        var engine = DaemonHost.BuildEngine(paths, scoring: true, dataDir: Path.Combine(Fixtures.RepoRoot(), "data"));
        Assert.NotNull(engine.Review); // v1.4 : IReviewBuilder (S31) branché, plus « review indisponible »
        var dir = Dir("review");
        var svc = new ReviewService(dir, () => paths.HistoryDir, engine.Review, () => false);
        var reply = (JsonObject)JsonNode.Parse((await svc.HandleAsync("""{"op":"review.open","runId":"win_solo"}"""))!)!;
        Assert.True(reply["ok"]!.GetValue<bool>(), reply.ToJsonString());
        var timeline = JsonNode.Parse((await File.ReadAllTextAsync(reply["path"]!.GetValue<string>()))!)!;
        Assert.Equal("win_solo.run", timeline["run"]!["runId"]!.GetValue<string>());
        Assert.NotNull(timeline["summary"]);
    }

    // ---------------- état et modules ensemble ----------------

    [Fact]
    public void Core_publishes_custom_and_a_broken_provider_never_breaks_the_state()
    {
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), null,
            new ManualTimers(), a => a(), _ => { });
        core.Publish();
        Assert.Null(core.LastState!.Custom); // aucun module : état v1.3 inchangé

        core.CustomProvider = _ => new[]
        {
            new CustomModuleView("x.test", "Test", CustomIds.SourceModule, null,
                new[] { new CustomRowView("ok", null, null) }, CustomHealth.Ok, null),
        };
        core.Publish();
        Assert.Equal("x.test", Assert.Single(core.LastState!.Custom!).Id);

        core.CustomProvider = _ => throw new InvalidOperationException("boom");
        core.Publish();
        Assert.Null(core.LastState.Custom);
    }
}
