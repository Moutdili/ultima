using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ultima.Mirror;

// DTOs restricted to ALLOWED fields (docs/20 §8.2): unknown/forbidden keys (rng, odds, grab bags,
// pre-rolled ids) are skipped by the reader and never materialised.
public sealed record SaveCard([property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("current_upgrade_level")] int? CurrentUpgradeLevel);
public sealed record SaveRelic([property: JsonPropertyName("id")] string? Id);
public sealed record SavePlayer(
    [property: JsonPropertyName("net_id")] ulong? NetId,
    [property: JsonPropertyName("current_hp")] int? CurrentHp,
    [property: JsonPropertyName("max_hp")] int? MaxHp,
    [property: JsonPropertyName("gold")] int? Gold,
    [property: JsonPropertyName("deck")] List<SaveCard>? Deck,
    [property: JsonPropertyName("relics")] List<SaveRelic>? Relics);
public sealed record SaveRun([property: JsonPropertyName("players")] List<SavePlayer>? Players,
    [property: JsonPropertyName("ascension")] int? Ascension,
    [property: JsonPropertyName("current_act_index")] int? CurrentActIndex);

public sealed record CompareLine(string Field, string Memory, string Save, bool Match);

/// <summary>Cross-check memory vs current_run.save (read-only copy, never locked).</summary>
public static class SaveCompare
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2/steam");

    /// <summary>Active vanilla profile's current_run.save (never the modded/ tree).</summary>
    public static string? FindCurrentRunSave()
    {
        if (!Directory.Exists(Root)) return null;
        foreach (var steamDir in Directory.EnumerateDirectories(Root))
        {
            var profileSave = Path.Combine(steamDir, "profile.save");
            if (!File.Exists(profileSave)) continue;
            using var doc = JsonDocument.Parse(ReadShared(profileSave));
            var id = doc.RootElement.GetProperty("last_profile_id").GetInt32();
            var p = Path.Combine(steamDir, $"profile{id}", "saves", "current_run.save");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    public static SaveRun Load(string path) =>
        JsonSerializer.Deserialize<SaveRun>(ReadShared(path)) ?? throw new InvalidDataException("save vide");

    public static IReadOnlyList<CompareLine> Compare(RunView mem, SaveRun save)
    {
        var lines = new List<CompareLine>();
        void Add(string f, object? m, object? s) => lines.Add(new(f, $"{m}", $"{s}", Equals($"{m}", $"{s}")));
        Add("ascension", mem.AscensionLevel, save.Ascension);
        Add("act_index", mem.ActIndex, save.CurrentActIndex);
        var sp = save.Players ?? new();
        Add("players.count", mem.Players.Count, sp.Count);
        foreach (var mp in mem.Players)
        {
            var s = sp.FirstOrDefault(p => p.NetId == mp.NetId) ?? (sp.Count == 1 && mem.Players.Count == 1 ? sp[0] : null);
            var tag = $"player[{mp.NetId}]";
            if (s is null) { lines.Add(new(tag, "présent", "absent", false)); continue; }
            Add($"{tag}.hp", mp.Hp, s.CurrentHp);
            Add($"{tag}.max_hp", mp.MaxHp, s.MaxHp);
            Add($"{tag}.gold", mp.Gold, s.Gold);
            static string Deck(IEnumerable<(string? id, int up)> c) =>
                string.Join(",", c.Select(x => x.up > 0 ? $"{x.id}+{x.up}" : $"{x.id}").OrderBy(x => x, StringComparer.Ordinal));
            Add($"{tag}.deck(multiset)", Deck(mp.Deck.Select(c => (c.Id, c.Upgrade))),
                Deck((s.Deck ?? new()).Select(c => (c.Id, c.CurrentUpgradeLevel ?? 0))));
            Add($"{tag}.relics(ordre)", string.Join(",", mp.Relics), string.Join(",", (s.Relics ?? new()).Select(r => r.Id)));
        }
        return lines;
    }
}
