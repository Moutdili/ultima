using System.Net.Sockets;
using System.Text.Json;
using Ultima.Daemon.Publish;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Tests;

public sealed class PublishTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ultima-test-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static EngineState State(int gold, DateTimeOffset? at = null) => new(EngineState.CurrentVersion,
        at ?? DateTimeOffset.Now, new SourceHealth(false, true, false, null, null), Screen.Unknown,
        new RunView("CHARACTER.X", "X", 0, null, null, null, 0, 10, 10, gold,
            1, Array.Empty<CardView>(), Array.Empty<RelicView>(), Array.Empty<PotionView>()),
        null, null, null, null, OverlaySettings.Default);

    [Fact]
    public void Publishes_only_when_the_state_changes_ignoring_timestamp()
    {
        using var p = new StatePublisher(_dir);
        Assert.True(p.Publish(State(1)));
        Assert.False(p.Publish(State(1, DateTimeOffset.Now.AddMinutes(5)))); // seul « at » change
        Assert.True(p.Publish(State(2)));
        Assert.Equal(2, p.Published);
        Assert.Equal(1, p.Skipped);
        Assert.False(File.Exists(p.TempPath));
        if (OperatingSystem.IsLinux())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_dir));
    }

    [Fact]
    public async Task Readers_never_see_a_partial_file()
    {
        using var p = new StatePublisher(_dir);
        p.Publish(State(0));
        int reads = 0, bad = 0;
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                string txt;
                try { txt = File.ReadAllText(p.StatePath); }
                catch (FileNotFoundException) { bad++; continue; }
                try { JsonDocument.Parse(txt).Dispose(); }
                catch (JsonException) { bad++; }
                reads++;
            }
        });
        // gros état (deck de 400 cartes) pour élargir la fenêtre d'écriture
        var big = Enumerable.Range(0, 400).Select(i =>
            new CardView($"CARD.C{i}", new string('x', 64), "Attack", "Common", 1, 0, null, false, null)).ToArray();
        for (int i = 1; i <= 300; i++)
            p.Publish(State(i) with { Run = State(i).Run! with { Deck = big } });
        stop.Cancel();
        await reader;
        Assert.True(reads > 50, $"{reads} lectures");
        Assert.Equal(0, bad);
        Assert.Equal(301, p.Published);
    }

    [Fact]
    public async Task Socket_serves_several_clients_one_line_per_state()
    {
        using var p = new StatePublisher(_dir);
        var sockPath = Path.Combine(_dir, "state.sock");
        using var server = new SocketServer(sockPath);
        p.AddSink(server);
        p.Publish(State(1));

        async Task<StreamReader> Connect()
        {
            var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await s.ConnectAsync(new UnixDomainSocketEndPoint(sockPath));
            return new StreamReader(new NetworkStream(s, ownsSocket: true));
        }
        using var a = await Connect();
        using var b = await Connect();
        // à la connexion : le dernier état
        Assert.Equal(1, StateJson.Deserialize((await a.ReadLineAsync())!)!.Run!.Gold);
        Assert.Equal(1, StateJson.Deserialize((await b.ReadLineAsync())!)!.Run!.Gold);
        p.Publish(State(2));
        p.Publish(State(3));
        foreach (var r in new[] { a, b })
        {
            Assert.Equal(2, StateJson.Deserialize((await r.ReadLineAsync())!)!.Run!.Gold);
            Assert.Equal(3, StateJson.Deserialize((await r.ReadLineAsync())!)!.Run!.Gold);
        }
        Assert.Equal(2, server.ClientCount);
    }

    [Fact]
    public async Task Ephemeral_liveness_probes_do_not_leak_clients()
    {
        // Le compagnon se connecte puis ferme aussitôt toutes les ~4 s (overlay/companion/…/ipc.py).
        using var p = new StatePublisher(_dir);
        var sockPath = Path.Combine(_dir, "state.sock");
        using var server = new SocketServer(sockPath);
        p.AddSink(server);
        p.Publish(State(1));
        for (int i = 0; i < 50; i++)
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await s.ConnectAsync(new UnixDomainSocketEndPoint(sockPath));
            s.Close();
            await Task.Delay(5);
        }
        // aucun état publié entre-temps : la fin de connexion seule doit libérer les clients
        for (int i = 0; i < 100 && server.ClientCount > 0; i++) await Task.Delay(20);
        Assert.Equal(0, server.ClientCount);
    }
}
