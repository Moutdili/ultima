using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ultima.Daemon.Mirror;

// Copie des formes publiées par `ultima-mirror probe --json` (Ultima.Mirror/Readers.cs, camelCase).
// Le daemon ne référence PAS la bibliothèque du miroir (ClrMD) : un plantage du lecteur reste dans l'enfant.

public sealed record MCard(string? Id, int Upgrade);

public sealed record MPlayer(ulong NetId, string? Character, int Hp, int MaxHp, int Block, int Gold, int MaxEnergy,
    IReadOnlyList<MCard> Deck, IReadOnlyList<string?> Relics, IReadOnlyList<string?> PotionSlots);

public sealed record MRun(bool InRun, ulong? LocalNetId, int AscensionLevel, int ActIndex, string? CurrentRoomType,
    IReadOnlyList<MPlayer> Players)
{
    public MPlayer? Local =>
        Players.Count == 1 ? Players[0] : Players.FirstOrDefault(p => LocalNetId is not null && p.NetId == LocalNetId);
}

public sealed record MCardReward(bool AlreadyTaken, IReadOnlyList<MCard> Offered);

public sealed record MRewards(int OpenRewardSets, IReadOnlyList<MCardReward> CardRewards);

public sealed record MShopCard(MCard Card, int BaseCost, int? EstimatedDisplayedCost, bool OnSale, bool Colorless);

public sealed record MShopItem(string? Id, int BaseCost, int? EstimatedDisplayedCost);

public sealed record MShop(bool InShop, IReadOnlyList<MShopCard> Cards, IReadOnlyList<MShopItem> Relics,
    IReadOnlyList<MShopItem> Potions, int? RemovalBaseCost, int? RemovalEstimatedCost, bool? RemovalUsed,
    IReadOnlyList<string> PriceModifiers);

public sealed record ProbeHealth(bool GameRunning, bool Attached, int? PtraceScope, int? Pid, long? AttachMs, string? Runtime);

public sealed record ProbeSection(bool Stable, int Attempts, long Ms, string? Error, JsonElement? Value);

/// <summary>Ligne unique de `ultima-mirror … --json`.</summary>
public sealed record ProbeOutput(int V, string? Cmd, int Exit, long Ms, string? Error, ProbeHealth? Health,
    Dictionary<string, ProbeSection>? Sections, int? Ok, int? Total);

/// <summary>Codes de sortie du probe (Ultima.Mirror.Probe/Program.cs).</summary>
public static class ProbeExit
{
    public const int Ok = 0, Usage = 1, NotRunning = 2, AttachFailed = 3, LayoutDrift = 4, Watchdog = 5,
        SaveMismatch = 6, Unstable = 7;

    /// <summary>Codes propres au daemon (le probe n'a pas répondu).</summary>
    public const int KilledByDaemon = 100, Missing = 101, SpawnFailed = 102, BadOutput = 103;
}

/// <summary>Résultat d'un appel au miroir, vu du daemon.</summary>
public sealed record MirrorResult(
    int Exit,
    long ElapsedMs,
    string? Error,
    ProbeHealth? Health,
    MRun? Run,
    MRewards? Reward,
    MShop? Shop,
    IReadOnlyList<string> UnstableSections)
{
    public bool Ok => Exit == ProbeExit.Ok;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static MirrorResult Failed(int exit, long ms, string error) =>
        new(exit, ms, error, null, null, null, null, Array.Empty<string>());

    /// <summary>Analyse la sortie standard (on prend la dernière ligne qui est un objet JSON).</summary>
    public static MirrorResult Parse(string stdout, int exitCode, long elapsedMs)
    {
        ProbeOutput? o = null;
        foreach (var line in Enumerable.Reverse(stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!line.StartsWith('{')) continue;
            try { o = JsonSerializer.Deserialize<ProbeOutput>(line, Json); break; }
            catch (JsonException) { }
        }
        if (o is null) return Failed(ProbeExit.BadOutput, elapsedMs, $"sortie du miroir illisible (code {exitCode})");

        T? Section<T>(string name) where T : class
        {
            if (o.Sections is null || !o.Sections.TryGetValue(name, out var s) || !s.Stable || s.Value is null) return null;
            try { return s.Value.Value.Deserialize<T>(Json); }
            catch (JsonException) { return null; }
        }
        var unstable = o.Sections?.Where(kv => !kv.Value.Stable).Select(kv => kv.Key).ToArray() ?? Array.Empty<string>();
        // le code du processus fait foi ; celui de la ligne JSON sert si le processus a été tué après l'émission
        return new MirrorResult(exitCode, elapsedMs, o.Error, o.Health,
            Section<MRun>("run"), Section<MRewards>("reward"), Section<MShop>("shop"), unstable);
    }
}
