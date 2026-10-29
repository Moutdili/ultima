using Ultima.Daemon.Mirror;
using Ultima.Daemon.Screens;
using Ultima.Saves.Log;

namespace Ultima.Daemon.Tests;

/// <summary>Minuteurs à horloge virtuelle : rien ne se déclenche sans <see cref="AdvanceTo"/>.</summary>
public sealed class ManualTimers : IOneShotTimers
{
    private readonly List<Entry> _entries = new();
    public TimeSpan Now { get; private set; }
    public int Armed => _entries.Count(e => !e.Disposed);

    private sealed class Entry(TimeSpan due, Action cb) : IDisposable
    {
        public TimeSpan Due { get; } = due;
        public Action Callback { get; } = cb;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    public IDisposable Once(TimeSpan due, Action callback)
    {
        var e = new Entry(Now + due, callback);
        _entries.Add(e);
        return e;
    }

    public void AdvanceTo(TimeSpan t)
    {
        while (true)
        {
            var next = _entries.Where(e => !e.Disposed && e.Due <= t).OrderBy(e => e.Due).FirstOrDefault();
            if (next is null) break;
            Now = next.Due;
            next.Dispose();
            next.Callback();
        }
        if (t > Now) Now = t;
    }

    public void Advance(TimeSpan d) => AdvanceTo(Now + d);
}

/// <summary>Faux miroir : répond immédiatement, journalise les appels.</summary>
public sealed class FakeMirror : IMirrorRunner
{
    public List<MirrorNeed> Probes { get; } = new();
    public int Layouts { get; private set; }
    public Func<MirrorNeed, MirrorResult> Respond { get; set; } = Default;
    public int LayoutExit { get; set; }

    public static MRewards Rewards(params string[] ids) =>
        new(1, new[] { new MCardReward(false, ids.Select(i => new MCard(i, 0)).ToArray()) });

    public static MShop Shop() => new(true,
        new[] { new MShopCard(new MCard("CARD.SCOURGE", 0), 48, 24, true, false) },
        new[] { new MShopItem("RELIC.PENDULUM", 181, 181) },
        new[] { new MShopItem("POTION.WEAK_POTION", 49, 49) },
        75, 75, false, Array.Empty<string>());

    public static MirrorResult Default(MirrorNeed need) => new(0, 120, null, null, null,
        need.HasFlag(MirrorNeed.Reward) ? Rewards("CARD.A", "CARD.B", "CARD.C") : null,
        need.HasFlag(MirrorNeed.Shop) ? Shop() : null, Array.Empty<string>());

    public Task<MirrorResult> ProbeAsync(MirrorNeed need, CancellationToken ct = default)
    {
        Probes.Add(need);
        return Task.FromResult(Respond(need));
    }

    public Task<MirrorResult> LayoutAsync(CancellationToken ct = default)
    {
        Layouts++;
        return Task.FromResult(MirrorResult.Failed(LayoutExit, 5, LayoutExit == 0 ? null! : "drift"));
    }
}

public static class Fixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string RepoRoot()
    {
        var d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "Ultima.sln"))) d = Path.GetDirectoryName(d);
        return d ?? throw new DirectoryNotFoundException("Ultima.sln introuvable");
    }

    /// <summary>Événements analysés d'un log réel, dans l'ordre, avec leur numéro de ligne (1-based).</summary>
    public static List<(int Line, GodotLogEvent Event)> Events(string file)
    {
        var list = new List<(int, GodotLogEvent)>();
        int n = 0;
        foreach (var line in File.ReadLines(Path.Combine(Dir, file)))
        {
            n++;
            if (GodotLogParser.ParseLine(line, DateTimeOffset.UnixEpoch) is { } e) list.Add((n, e));
        }
        return list;
    }
}
