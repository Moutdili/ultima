using System.Net.Sockets;
using System.Text.Json.Nodes;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Tests;

/// <summary>
/// Hôte RÉEL (inotify, boucle, fichier, socket) sur une COPIE de la fixture : jeu « fermé » (nom de
/// processus inexistant), miroir désactivé. Prouve le chemin fichiers → état publié et le rechargement
/// des réglages, sans jamais toucher aux données du jeu.
/// </summary>
public sealed class HostIntegrationTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ultima-host-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    private static async Task<EngineState> NextMatching(StreamReader r, Func<EngineState, bool> pred)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var line = await r.ReadLineAsync(cts.Token) ?? throw new EndOfStreamException();
            var s = StateJson.Deserialize(line)!;
            if (pred(s)) return s;
        }
    }

    [Fact]
    public async Task Game_closed_files_only_state_is_published_and_follows_changes()
    {
        var root = Path.Combine(_tmp, "root");
        CopyDir(Path.Combine(Fixtures.RepoRoot(), "fixtures", "saves", "root"), root);
        var runtime = Path.Combine(_tmp, "run");
        var settings = Path.Combine(_tmp, "cfg", "ultima", "settings.json");
        Directory.CreateDirectory(Path.Combine(_tmp, "cfg"));

        using var host = new DaemonHost(new DaemonOptions
        {
            Root = root, RuntimeDir = runtime, SettingsPath = settings,
            GameComm = "NoSuchGame-xyz", MirrorEnabled = false, Scoring = false,
        });
        host.Start();

        var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(runtime, "state.json")))!;
        Assert.False(json["health"]!["gameRunning"]!.GetValue<bool>());
        Assert.True(json["health"]!["saveOk"]!.GetValue<bool>());
        Assert.Equal("Unknown", json["screen"]!.GetValue<string>());
        Assert.Equal("CHARACTER.SILENT", json["run"]!["characterId"]!.GetValue<string>());
        Assert.Equal(0, host.Core!.MirrorProbes);

        using var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await sock.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(runtime, "state.sock")));
        using var reader = new StreamReader(new NetworkStream(sock));
        var first = await NextMatching(reader, _ => true);
        int gold = first.Run!.Gold;

        // réglages : le dossier ~/.config/ultima n'existe pas encore → créé puis fichier écrit (tmp + rename)
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        await File.WriteAllTextAsync(settings + ".tmp", """{"modules":[{"id":"curses","enabled":true,"order":0}]}""");
        File.Move(settings + ".tmp", settings);
        var withSettings = await NextMatching(reader, s => s.Settings.Modules[0].Id == "curses");
        Assert.Equal(OverlaySettings.Default.Modules.Count, withSettings.Settings.Modules.Count);

        // save réécrite comme le fait le jeu (tmp puis rename) : l'or change
        var save = Directory.GetFiles(root, "current_run.save", SearchOption.AllDirectories)
            .Single(p => !p.Contains("modded"));
        var text = await File.ReadAllTextAsync(save);
        var node = JsonNode.Parse(text)!;
        node["players"]![0]!["gold"] = gold + 42;
        await File.WriteAllTextAsync(save + ".tmp", node.ToJsonString());
        File.Move(save + ".tmp", save, overwrite: true);
        var after = await NextMatching(reader, s => s.Run?.Gold == gold + 42);
        Assert.False(after.Health.GameRunning);
        Assert.Equal(0, host.Core.MirrorProbes);
    }
}
