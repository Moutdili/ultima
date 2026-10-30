using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ultima.Daemon.Knowledge;
using Ultima.Daemon.Mirror;
using Ultima.Daemon.Publish;
using Ultima.Daemon.Screens;
using Ultima.Daemon.Settings;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;
using Ultima.Saves;
using Ultima.Saves.Log;
using Ultima.Saves.Model;

namespace Ultima.Daemon.Tests;

/// <summary>Contrat v1.2 (carte, route, équipe, événement, revue) et v1.3 (HUD), côté daemon.</summary>
public sealed class V12Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ultima-v12-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private static RunState FixtureRun() =>
        new RunSaveReader().ReadCurrent(ProfileLocator.Locate(Path.Combine(Fixtures.RepoRoot(), "fixtures", "saves", "root")))!;

    private sealed class SpyPlanner : IRoutePlanner
    {
        public MapView? Seen;
        public ScoringContext? Ctx;
        public RouteView? Plan(MapView map, ScoringContext ctx)
        {
            Seen = map; Ctx = ctx;
            return new RouteView(new[] { map.Position ?? map.Boss, map.Boss }, 42, false,
                new[] { new ScoreTerm("PV", 0, "test") }, "test");
        }
    }

    private sealed class ThrowingPlanner : IRoutePlanner
    {
        public RouteView? Plan(MapView map, ScoringContext ctx) => throw new InvalidOperationException("boom");
    }

    private sealed class SpyAdvisor : IEventAdvisor
    {
        public string? EventId;
        public IReadOnlyList<string>? Options;
        public IReadOnlyList<Score?> Rank(string eventId, IReadOnlyList<string> optionIds, ScoringContext ctx)
        {
            EventId = eventId; Options = optionIds;
            return optionIds.Select((o, i) => i == 0 ? new Score(80, new[] { new ScoreTerm("PV", 30, "mesure") }, "medium") : null).ToArray();
        }
    }

    private sealed class FakeReview : IReviewBuilder
    {
        public int Calls;
        public ReviewTimeline Build(RunRecord run)
        {
            Calls++;
            return new ReviewTimeline(1,
                new ReviewRun(run.FileName, run.BuildId, run.GameMode, run.Ascension, run.Character(), null,
                    DateTimeOffset.FromUnixTimeSeconds(run.StartTime), (int)run.RunTime, run.Win, run.WasAbandoned,
                    run.KilledByEncounter, "history", "test", null, "test"),
                Array.Empty<ReviewLane>(), Array.Empty<ReviewState>(), Array.Empty<ReviewPoint>(),
                new ReviewSummary(0, 0, 0, 0, 0, Array.Empty<int>(), 0, Array.Empty<ReviewActRegret>(), null),
                new ReviewCoverage(0, 0, Array.Empty<string>(), "test"));
        }
    }

    // ---------------- carte / route ----------------

    [Fact]
    public void Map_is_the_visible_current_act_with_position_boss_and_visited()
    {
        var run = FixtureRun();
        var map = StateAssembler.BuildMap(run)!;
        Assert.Equal(run.CurrentActIndex, map.ActIndex);
        Assert.Equal(7, map.Width);
        Assert.All(map.Nodes, n => Assert.Contains(n.RoomType, RoomTypes.All));
        Assert.Contains(map.Nodes, n => n.RoomType == RoomTypes.Boss && n.Col == map.Boss.Col && n.Row == map.Boss.Row);
        Assert.Equal(run.CurrentCoord!.Value.Col, map.Position!.Col);
        Assert.Equal(run.VisitedMapCoords.Count, map.Nodes.Count(n => n.Visited));
        // les enfants pointent vers des cases publiées (graphe fermé)
        var coords = map.Nodes.Select(n => (n.Col, n.Row)).ToHashSet();
        Assert.All(map.Nodes.SelectMany(n => n.Children), c => Assert.Contains((c.Col, c.Row), coords));
        // acte futur jamais publié
        Assert.Null(StateAssembler.BuildMap(run with { CurrentActIndex = run.Acts.Count }));
    }

    [Fact]
    public void Route_comes_from_the_planner_and_a_failure_is_null()
    {
        var spy = new SpyPlanner();
        var part = new StateAssembler(NullKnowledge.Instance, null, null, null, spy).BuildRun(FixtureRun())!;
        Assert.NotNull(part.Map);
        Assert.Same(part.Map, spy.Seen);
        Assert.Equal(42, part.Route!.ExpectedHpAtBoss);
        var state = StateAssembler.Compose(DateTimeOffset.Now, new SourceHealth(false, true, false, null, null),
            Screen.Map, part, null, null, OverlaySettings.Default);
        Assert.Same(part.Map, state.Map);
        Assert.Same(part.Route, state.Route);

        var broken = new StateAssembler(NullKnowledge.Instance, null, null, null, new ThrowingPlanner()).BuildRun(FixtureRun())!;
        Assert.NotNull(broken.Map);
        Assert.Null(broken.Route);
    }

    // ---------------- équipe ----------------

    [Fact]
    public void Solo_party_is_one_local_entry_and_no_allies()
    {
        var spy = new SpyPlanner();
        var part = new StateAssembler(NullKnowledge.Instance, null, null, null, spy).BuildRun(FixtureRun())!;
        var me = Assert.Single(part.View.Party);
        Assert.True(me.IsLocal);
        Assert.Equal(part.View.Hp, me.Hp);
        Assert.Same(part.View.Deck, me.Deck);
        Assert.Empty(part.Context.Allies);
        Assert.Empty(spy.Ctx!.Allies);
    }

    [Fact]
    public void Coop_party_lists_everyone_and_allies_exclude_the_local_player()
    {
        var run = FixtureRun();
        var me = run.Players[0] with { NetId = 70000000000000001 };
        var ally = me with { NetId = 70000000000000002, CharacterId = "CHARACTER.IRONCLAD", CurrentHp = 33, MaxHp = 80 };
        run = run with { Players = new[] { me, ally } };
        var part = new StateAssembler(NullKnowledge.Instance, null, null, steamId64: 70000000000000001).BuildRun(run)!;
        Assert.Equal(2, part.View.Party.Count);
        Assert.Single(part.View.Party, a => a.IsLocal && a.NetId == 70000000000000001);
        var a2 = part.View.Party.Single(a => !a.IsLocal);
        Assert.Equal(("CHARACTER.IRONCLAD", 33, 80), (a2.CharacterId, a2.Hp, a2.MaxHp));
        var ctxAlly = Assert.Single(part.Context.Allies);
        Assert.Equal(70000000000000002UL, ctxAlly.NetId);
        Assert.Equal(ally.Deck.Count, ctxAlly.Deck.Count);

        // PV de l'équipe pris en mémoire quand la lecture est plus récente
        var mem = new MRun(true, 70000000000000001, 0, 0, "MerchantRoom", new[]
        {
            new MPlayer(70000000000000002, "CHARACTER.IRONCLAD", 20, 80, 0, 0, 3, Array.Empty<MCard>(), Array.Empty<string?>(), Array.Empty<string?>()),
        });
        var over = StateAssembler.OverlayParty(part.View.Party, mem);
        Assert.Equal(20, over.Single(a => !a.IsLocal).Hp);
        Assert.Equal(part.View.Hp, over.Single(a => a.IsLocal).Hp);
    }

    // ---------------- événement ----------------

    [Fact]
    public void Event_view_uses_bare_ids_for_the_advisor_and_null_for_unmeasured_options()
    {
        var advisor = new SpyAdvisor();
        var asm = new StateAssembler(NullKnowledge.Instance, null, null, null, null, advisor);
        var part = asm.BuildRun(FixtureRun());
        var ev = asm.BuildEvent(new MEvent("EVENT.ABYSSAL_BATHS", null, new[]
        {
            new MEventOption("ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE", "Plonger", "Perdez 5 PV.", true),
            new MEventOption("ABYSSAL_BATHS.pages.INITIAL.options.ABSTAIN", null, null, null),
            new MEventOption(null, "sans id", null, true),
        }), part)!;
        Assert.Equal("ABYSSAL_BATHS", advisor.EventId);
        Assert.Equal(new[] { "IMMERSE", "ABSTAIN" }, advisor.Options);
        Assert.Equal(2, ev.Options.Count);
        Assert.Equal("Plonger", ev.Options[0].Label);
        Assert.Equal(80, ev.Options[0].Score!.Total);
        Assert.Null(ev.Options[1].Label); // v1.4 : texte inconnu ⇒ null, jamais l'id nu
        Assert.Null(ev.Name);
        Assert.True(ev.Options[1].Available);
        Assert.Null(ev.Options[1].Score);
        Assert.Null(asm.BuildEvent(null, part));
    }

    [Fact]
    public void Event_room_triggers_one_event_read_and_publishes_the_view()
    {
        var timers = new ManualTimers();
        var mirror = new FakeMirror();
        mirror.Respond = n => FakeMirror.Default(n) with
        {
            Event = new MEvent("EVENT.ABYSSAL_BATHS", null, new[] { new MEventOption("ABSTAIN", null, null, true) }),
        };
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null, null, new SpyAdvisor()),
            mirror, timers, a => a(), _ => { });
        core.OnGame(true);
        core.OnRun(FixtureRun());
        core.OnLog(new RoomEntered(default, "", RoomKind.Event, "Event Room"));
        timers.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { MirrorNeed.Event }, mirror.Probes);
        Assert.Equal("EVENT.ABYSSAL_BATHS", core.LastState!.Event!.EventId);
        core.OnLog(new MapVote(default, "", 1, null, new MapCoord(1, 1), 1));
        Assert.Null(core.LastState.Event);
        Assert.Equal("event", ProcessMirrorRunner.SectionsArg(MirrorNeed.Event));
        var parsed = MirrorResult.Parse("""{"v":1,"cmd":"probe","exit":0,"ms":5,"sections":{},"unsupported":["event","piles"]}""", 0, 5);
        Assert.Equal(new[] { "event", "piles" }, parsed.Unsupported);
    }

    // ---------------- revue ----------------

    private ReviewService Review(IReviewBuilder? builder, bool gameRunning = false) =>
        new(Path.Combine(_tmp, "review"),
            () => Path.Combine(Fixtures.RepoRoot(), "fixtures", "saves", "root", "steam", "70000000000000001", "profile1", "saves", "history"),
            builder, () => gameRunning);

    private static JsonObject Json(string? s) => (JsonObject)JsonNode.Parse(s!)!;

    [Fact]
    public async Task Review_without_builder_answers_unavailable()
    {
        var r = Json(await Review(null).HandleAsync("""{"op":"review.open","runId":"abandon_solo"}"""));
        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Equal("review indisponible", r["error"]!.GetValue<string>());
        Assert.Equal("review.open", r["op"]!.GetValue<string>());
    }

    [Fact]
    public async Task Review_with_builder_writes_the_file_once_and_rejects_bad_requests()
    {
        var b = new FakeReview();
        var svc = Review(b);
        var r = Json(await svc.HandleAsync("""{"op":"review.open","runId":"abandon_solo.run"}"""));
        Assert.True(r["ok"]!.GetValue<bool>(), r.ToJsonString());
        var path = r["path"]!.GetValue<string>();
        Assert.Matches(@"/review/[0-9a-f]{16}\.json$", path);
        var timeline = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal("abandon_solo.run", timeline["run"]!["runId"]!.GetValue<string>());
        var again = Json(await svc.HandleAsync("""{"op":"review.open","runId":"abandon_solo"}"""));
        Assert.Equal(path, again["path"]!.GetValue<string>());
        Assert.Equal(1, b.Calls); // mis en cache

        Assert.Equal("runId invalide", Json(await svc.HandleAsync("""{"op":"review.open","runId":"../../etc/passwd"}"""))["error"]!.GetValue<string>());
        Assert.Equal("run introuvable", Json(await svc.HandleAsync("""{"op":"review.open","runId":"nope"}"""))["error"]!.GetValue<string>());
        Assert.Equal("op inconnue", Json(await svc.HandleAsync("""{"op":"rm"}"""))["error"]!.GetValue<string>());
        Assert.Equal("requête illisible", Json(await svc.HandleAsync("pas du json"))["error"]!.GetValue<string>());
        Assert.Contains("pendant une partie",
            Json(await Review(b, gameRunning: true).HandleAsync("""{"op":"review.open","runId":"abandon_solo"}"""))["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Review_op_over_the_socket_answers_only_the_asking_client()
    {
        Directory.CreateDirectory(_tmp);
        using var p = new StatePublisher(Path.Combine(_tmp, "rt"));
        var sock = Path.Combine(_tmp, "rt", "state.sock");
        using var server = new SocketServer(sock) { OnRequest = Review(null).HandleAsync };
        p.AddSink(server);
        using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await s.ConnectAsync(new UnixDomainSocketEndPoint(sock));
        using var ns = new NetworkStream(s);
        using var rd = new StreamReader(ns);
        await ns.WriteAsync("{\"op\":\"review.open\",\"runId\":\"x\"}\n"u8.ToArray());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var line = await rd.ReadLineAsync(cts.Token);
        Assert.Equal("review indisponible", Json(line)["error"]!.GetValue<string>());
    }

    // ---------------- HUD (v1.3) ----------------

    [Fact]
    public void Hud_settings_are_read_validated_field_by_field_and_published()
    {
        Directory.CreateDirectory(_tmp);
        var path = Path.Combine(_tmp, "settings.json");
        File.WriteAllText(path, """{"modules":[],"hud":{"quality":"LOUPE","heightFraction":0.7,"restOpacity":0.4,"side":"left","deckScoreInCombat":true,"gradesOnly":true}}""");
        var s = SettingsStore.TryLoad(path, out _)!;
        Assert.Equal(new HudSettings("loupe", 0.7, 0.4, "left", true, true), s.Hud);

        File.WriteAllText(path, """{"modules":[],"hud":{"quality":"ultra","heightFraction":3,"restOpacity":-1,"side":"top","deckScoreInCombat":true,"gradesOnly":false}}""");
        var bad = SettingsStore.TryLoad(path, out _)!;
        var d = HudSettings.Default;
        Assert.Equal(new HudSettings(d.Quality, d.HeightFraction, d.RestOpacity, d.Side, true, false), bad.Hud);

        File.WriteAllText(path, """{"modules":[]}""");
        Assert.Equal(HudSettings.Default, SettingsStore.TryLoad(path, out _)!.Hud);
        File.WriteAllText(path, """{"modules":[],"hud":null}""");
        Assert.Equal(HudSettings.Default, SettingsStore.TryLoad(path, out _)!.Hud);

        var state = StateAssembler.Compose(DateTimeOffset.Now, new SourceHealth(false, true, false, null, null),
            Screen.Unknown, null, null, null, s);
        var json = JsonNode.Parse(StateJson.Serialize(state))!;
        Assert.Equal("loupe", json["settings"]!["hud"]!["quality"]!.GetValue<string>());
        Assert.Equal(ContractInfo.CurrentMinor, json["contractMinor"]!.GetValue<int>());
    }

    // ---------------- journal ----------------

    [Fact]
    public void Log_lines_have_timestamp_and_level_for_the_console()
    {
        var line = Diag.Format(new DateTimeOffset(2026, 10, 29, 17, 54, 4, 243, TimeSpan.FromHours(1)), "WARN", "a\nb");
        Assert.Equal("2026-10-29 17:54:04.243 WARN  a b", line);
        Assert.Matches(new Regex(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} (INFO |WARN |ERROR|DEBUG) "), line);
        // ~/.local/state/ultima/daemon.log (ou $XDG_STATE_HOME/ultima/daemon.log) — jamais à la racine de ~
        var log = Diag.DefaultLogPath();
        Assert.EndsWith(Path.Combine("ultima", "daemon.log"), log);
        Assert.NotEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetDirectoryName(log));
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_STATE_HOME")))
            Assert.EndsWith(Path.Combine(".local", "state", "ultima", "daemon.log"), log);
    }
}
