using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Ultima.Daemon.Custom;
using Ultima.Daemon.Knowledge;
using Ultima.Daemon.Publish;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Tests;

/// <summary>
/// Plugins (docs/44 §7) : processus séparé en nice 19, protocole JSON sur stdin/stdout validé par schéma,
/// minIntervalMs, timeoutMs, 3 échecs ⇒ disabled, arrêt propre — et surtout : un plugin qui dort, crashe,
/// écrit 1 Mio ou répond un JSON invalide ne retarde JAMAIS la publication de plus de 50 ms (chronométré).
/// Tous les dossiers sont temporaires ; les vrais dossiers XDG ne sont jamais touchés.
/// </summary>
[Collection("custom")]
public sealed class V14PluginTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ultima-plug-" + Guid.NewGuid().ToString("N")[..8]);

    public V14PluginTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private static string Python => File.Exists("/usr/bin/python3") ? "/usr/bin/python3" : "python3";

    private string PluginsDir()
    {
        var d = Path.Combine(_tmp, "plugins");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Écrit un plugin minimal (manifeste conforme au schéma + script Python).</summary>
    private string Plugin(string pluginsDir, string id, string python, int timeoutMs = 200, int minIntervalMs = 250)
    {
        var dir = Path.Combine(pluginsDir, id);
        Directory.CreateDirectory(dir);
        var manifest = new JsonObject
        {
            ["id"] = id,
            ["label"] = id,
            ["api"] = 1,
            ["exec"] = new JsonArray(Python, "main.py"),
            ["timeoutMs"] = timeoutMs,
            ["minIntervalMs"] = minIntervalMs,
        };
        File.WriteAllText(Path.Combine(dir, "plugin.json"), manifest.ToJsonString());
        File.WriteAllText(Path.Combine(dir, "main.py"), python);
        return dir;
    }

    private static EngineState State(int gold = 100) =>
        new(EngineState.CurrentVersion, DateTimeOffset.Now, new SourceHealth(false, true, false, null, null), Screen.Map,
            new RunView("CHARACTER.SILENT", "Silent", 0, null, null, null, 0, 70, 73, gold, 5,
                Array.Empty<CardView>(), Array.Empty<RelicView>(), Array.Empty<PotionView>()),
            null, null, null, null, OverlaySettings.Default);

    private static bool WaitUntil(Func<bool> cond, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            Thread.Sleep(20);
        }
        return cond();
    }

    // ---------------- plugin d'exemple : il répond ----------------

    [Fact]
    public void Example_python_plugin_answers_and_its_rows_are_published()
    {
        var set = SchemaSet.Load();
        Assert.NotNull(set.PluginOutput);
        using var host = new PluginHost(Path.Combine(Fixtures.Dir, "plugins"), set.Plugin, set.PluginOutput, () => { });
        host.SetEnabled(new[] { "x.echo" });
        Assert.Equal(1, host.ActiveCount);

        host.Submit(StateJson.Serialize(State(134)));
        Assert.True(WaitUntil(() => host.Views().Single().Rows.Count > 0), "le plugin n'a pas répondu");
        var view = host.Views().Single();
        Assert.Equal(CustomHealth.Ok, view.Health);
        Assert.Null(view.Problem);
        Assert.Equal("plugin", view.Source);
        Assert.Equal("écran Map", view.Rows[0].Text);
        Assert.Equal("134 or", view.Rows[1].Text);
    }

    // ---------------- processus séparé, nice 19 ----------------

    [Fact]
    public void Plugin_runs_in_its_own_process_with_nice_19()
    {
        const string python = """
            import json, os, sys
            def nice(pid):
                with open(f"/proc/{pid}/stat") as f:
                    fields = f.read().rsplit(")", 1)[1].split()
                return fields[16]
            for line in sys.stdin:
                if not line.strip():
                    continue
                sys.stdout.write(json.dumps({"rows": [{"text": f"nice {nice('self')} pid {os.getpid()}"}]}) + "\n")
                sys.stdout.flush()
            """;
        var dir = PluginsDir();
        Plugin(dir, "x.nice", python);
        var set = SchemaSet.Load();
        using var host = new PluginHost(dir, set.Plugin, set.PluginOutput, () => { });
        host.SetEnabled(new[] { "x.nice" });
        host.Submit(StateJson.Serialize(State()));
        Assert.True(WaitUntil(() => host.Views().Single().Rows.Count > 0), "le plugin n'a pas répondu");
        var text = host.Views().Single().Rows[0].Text;
        Assert.StartsWith("nice 19 pid ", text);
        var pid = int.Parse(text.Split(' ')[^1]);
        Assert.NotEqual(Environment.ProcessId, pid);           // processus SÉPARÉ
        Assert.True(Directory.Exists($"/proc/{pid}"));         // il vit pendant que le daemon tourne
    }

    [Fact]
    public void Plugin_process_is_stopped_with_the_daemon()
    {
        const string python = """
            import json, os, sys
            for line in sys.stdin:
                if not line.strip():
                    continue
                sys.stdout.write(json.dumps({"rows": [{"text": f"pid {os.getpid()}"}]}) + "\n")
                sys.stdout.flush()
            """;
        var dir = PluginsDir();
        Plugin(dir, "x.quit", python);
        var set = SchemaSet.Load();
        var host = new PluginHost(dir, set.Plugin, set.PluginOutput, () => { });
        host.SetEnabled(new[] { "x.quit" });
        host.Submit(StateJson.Serialize(State()));
        Assert.True(WaitUntil(() => host.Views().Single().Rows.Count > 0), "pas de réponse");
        var pid = int.Parse(host.Views().Single().Rows[0].Text.Split(' ')[^1]);
        host.Dispose();
        Assert.True(WaitUntil(() => !Alive(pid), 3000), $"le processus {pid} a survécu à l'arrêt du daemon");
    }

    [Fact]
    public void Removing_a_plugin_from_the_list_stops_its_process()
    {
        const string python = """
            import json, os, sys
            for line in sys.stdin:
                if not line.strip():
                    continue
                sys.stdout.write(json.dumps({"rows": [{"text": f"pid {os.getpid()}"}]}) + "\n")
                sys.stdout.flush()
            """;
        var dir = PluginsDir();
        Plugin(dir, "x.unload", python);
        var set = SchemaSet.Load();
        using var host = new PluginHost(dir, set.Plugin, set.PluginOutput, () => { });
        host.SetEnabled(new[] { "x.unload" });
        host.Submit(StateJson.Serialize(State()));
        Assert.True(WaitUntil(() => host.Views().Single().Rows.Count > 0), "pas de réponse");
        var pid = int.Parse(host.Views().Single().Rows[0].Text.Split(' ')[^1]);

        host.SetEnabled(Array.Empty<string>()); // retiré de settings.json → pluginsEnabled
        Assert.Equal(0, host.Count);
        Assert.Empty(host.Views());
        Assert.True(WaitUntil(() => !Alive(pid), 3000), "le plugin retiré tourne encore");
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    // ---------------- le critère des 50 ms ----------------

    [Fact]
    public void Sleeping_crashing_flooding_and_invalid_plugins_never_delay_publication_more_than_50ms()
    {
        var dir = PluginsDir();
        Plugin(dir, "x.sleep", """
            import sys, time
            for line in sys.stdin:
                time.sleep(30)
            """);
        Plugin(dir, "x.crash", """
            import sys
            sys.exit(3)
            """);
        Plugin(dir, "x.big", """
            import sys
            for line in sys.stdin:
                sys.stdout.write("x" * 1000000 + "\n")
                sys.stdout.flush()
            """);
        Plugin(dir, "x.invalid", """
            import sys
            for line in sys.stdin:
                sys.stdout.write("{ceci n'est pas du JSON}\n")
                sys.stdout.flush()
            """);
        var ids = new[] { "x.sleep", "x.crash", "x.big", "x.invalid" };

        var set = SchemaSet.Load();
        using var custom = new CustomHost(Path.Combine(_tmp, "modules"), dir, set, () => { });
        custom.Start();
        custom.SetPluginsEnabled(ids);

        using var publisher = new StatePublisher(Path.Combine(_tmp, "run"));
        publisher.AddSink(custom);
        var published = 0;
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), null,
            new ManualTimers(), a => a(), s => { if (publisher.Publish(s)) published++; });
        core.CustomProvider = custom.Views;
        core.Publish();

        // 12 publications : chacune doit rendre la main en moins de 50 ms, plugins défaillants ou non
        var times = new List<double>();
        for (int i = 0; i < 12; i++)
        {
            var sw = Stopwatch.StartNew();
            core.OnSettings(new OverlaySettings(
                OverlaySettings.Default.Modules.Select((m, k) => m with { Order = (k + i) % 12 }).ToArray()));
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        Assert.True(published >= 6, $"seulement {published} états publiés");
        Assert.All(times, ms => Assert.True(ms < 50, $"publication retardée de {ms:0.0} ms par un plugin"));

        // 3 échecs consécutifs ⇒ disabled (il faut laisser passer minIntervalMs entre deux tours)
        for (int i = 0; i < 8; i++)
        {
            Thread.Sleep(300);
            core.OnSettings(new OverlaySettings(
                OverlaySettings.Default.Modules.Select((m, k) => m with { Order = (k + i + 40) % 12 }).ToArray()));
        }
        Assert.True(WaitUntil(() => custom.Plugins.Views().All(v => v.Health == CustomHealth.Disabled), 10000),
            string.Join(" | ", custom.Plugins.Views().Select(v => $"{v.Id}={v.Health}:{v.Problem}")));
        Assert.All(custom.Plugins.Views(), v =>
        {
            Assert.Equal("plugin", v.Source);
            Assert.Empty(v.Rows);
            Assert.False(string.IsNullOrWhiteSpace(v.Problem));
        });
    }

    [Fact]
    public void Three_consecutive_failures_disable_the_plugin_and_are_logged()
    {
        var dir = PluginsDir();
        Plugin(dir, "x.invalid", """
            import sys
            for line in sys.stdin:
                sys.stdout.write("pas du JSON\n")
                sys.stdout.flush()
            """);
        var set = SchemaSet.Load();
        var log = new List<string>();
        var previous = Diag.Sink;
        Diag.Sink = line => { lock (log) log.Add(line); };
        try
        {
            using var runtime = new PluginRuntime(PluginManifest.TryLoad(Path.Combine(dir, "x.invalid"), set.Plugin, out _)!,
                set.PluginOutput, () => { });
            for (int i = 1; i <= 3; i++)
            {
                runtime.Submit(StateJson.Serialize(State(100 + i)));
                Thread.Sleep(350);
            }
            Assert.True(WaitUntil(() => runtime.Disabled, 5000), "le plugin n'a pas été désactivé après 3 échecs");
            Assert.Equal(3, runtime.Failures);
            Assert.Equal(CustomHealth.Disabled, runtime.Health);
            Assert.Contains("JSON", runtime.Problem!);
            lock (log) Assert.Contains(log, l => l.Contains("DÉSACTIVÉ", StringComparison.Ordinal));
        }
        finally { Diag.Sink = previous; }
    }

    [Fact]
    public void MinInterval_limits_sends_and_identical_states_are_not_resent()
    {
        var dir = PluginsDir();
        Plugin(dir, "x.count", """
            import json, sys
            n = 0
            for line in sys.stdin:
                if not line.strip():
                    continue
                n += 1
                sys.stdout.write(json.dumps({"rows": [{"text": f"tour {n}"}]}) + "\n")
                sys.stdout.flush()
            """, timeoutMs: 300, minIntervalMs: 400);
        var set = SchemaSet.Load();
        using var runtime = new PluginRuntime(
            PluginManifest.TryLoad(Path.Combine(dir, "x.count"), set.Plugin, out _)!, set.PluginOutput, () => { });

        var state = StateJson.Serialize(State());
        for (int i = 0; i < 5; i++) runtime.Submit(state); // même état : un seul envoi
        Assert.True(WaitUntil(() => runtime.Rows.Count > 0), "pas de réponse");
        Assert.Equal(1, runtime.Sends);
        Assert.Equal("tour 1", runtime.Rows.Single().Text);

        runtime.Submit(StateJson.Serialize(State(101)));   // juste après : dans l'intervalle
        Thread.Sleep(80);
        Assert.Equal(1, runtime.Sends);                    // minIntervalMs pas écoulé
        runtime.Submit(StateJson.Serialize(State(102)));   // remplace l'état en attente
        Thread.Sleep(600);
        Assert.Equal(2, runtime.Sends);
        Assert.Equal("tour 2", runtime.Rows.Single().Text);
    }

    // ---------------- erreurs de manifeste ----------------

    [Fact]
    public void Invalid_manifest_or_unknown_api_is_published_as_error_never_started()
    {
        var dir = PluginsDir();
        var bad = Path.Combine(dir, "x.api2");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "plugin.json"), """{"id":"x.api2","label":"A","api":2,"exec":["true"]}""");
        var set = SchemaSet.Load();
        using var host = new PluginHost(dir, set.Plugin, set.PluginOutput, () => { });
        host.SetEnabled(new[] { "x.api2", "x.absent", "pas-un-id" });
        Assert.Equal(0, host.ActiveCount);
        var views = host.Views();
        Assert.Equal(2, views.Count); // « pas-un-id » est refusé par le préfixe, avec un WARN
        Assert.All(views, v => Assert.Equal(CustomHealth.Error, v.Health));
        Assert.All(views, v => Assert.False(string.IsNullOrWhiteSpace(v.Problem)));
    }

    // ---------------- commandes de test (point 7) ----------------

    [Fact]
    public void Module_test_command_returns_clear_exit_codes()
    {
        var ok = new StringWriter();
        Assert.Equal(CustomCli.Ok, CustomCli.ModuleTest(Path.Combine(Fixtures.Dir, "modules", "x.or-par-etage.json"), null, ok));
        var text = ok.ToString();
        Assert.Contains("x.or-par-etage", text);
        Assert.Contains("— or · étage —", text);   // échantillon v1.4 : run absente
        Assert.Contains("OK", text);

        var stateFile = Path.Combine(_tmp, "state-run.json");
        File.WriteAllText(stateFile, StateJson.Serialize(State(134), indented: true));
        var withRun = new StringWriter();
        Assert.Equal(CustomCli.Ok, CustomCli.ModuleTest(Path.Combine(Fixtures.Dir, "modules", "x.or-par-etage.json"), stateFile, withRun));
        Assert.Contains("134 or · étage 5", withRun.ToString());

        var broken = Path.Combine(_tmp, "x.broken.json");
        File.WriteAllText(broken, """{"id":"x.broken","label":"B","api":1,"rows":[]}""");
        var refused = new StringWriter();
        Assert.Equal(CustomCli.Invalid, CustomCli.ModuleTest(broken, null, refused));
        Assert.Contains("REFUSÉ", refused.ToString());
    }

    [Fact]
    public void Plugin_test_command_replays_the_sample_states()
    {
        var ok = new StringWriter();
        Assert.Equal(CustomCli.Ok, CustomCli.PluginTest(Path.Combine(Fixtures.Dir, "plugins", "x.echo"), null, ok));
        var text = ok.ToString();
        Assert.Contains("tour 1", text);
        Assert.Contains("état(s) — OK", text);

        var dir = PluginsDir();
        Plugin(dir, "x.invalid", """
            import sys
            for line in sys.stdin:
                sys.stdout.write("pas du JSON\n")
                sys.stdout.flush()
            """);
        var ko = new StringWriter();
        Assert.Equal(CustomCli.RunFailed, CustomCli.PluginTest(Path.Combine(dir, "x.invalid"), null, ko));
        Assert.Contains("SORTIE INVALIDE", ko.ToString());

        var noManifest = Path.Combine(_tmp, "vide");
        Directory.CreateDirectory(noManifest);
        var missing = new StringWriter();
        Assert.Equal(CustomCli.Invalid, CustomCli.PluginTest(noManifest, null, missing));
        Assert.Contains("REFUSÉ", missing.ToString());
    }

    [Fact]
    public void Cli_commands_are_documented_in_the_ipc_readme()
    {
        var readme = File.ReadAllText(Path.Combine(Fixtures.RepoRoot(), "docs", "ipc", "README.md"));
        Assert.Contains("plugin-test", readme);
        Assert.Contains("module-test", readme);
        Assert.Contains("state.v14.sample.json", readme);
    }
}
