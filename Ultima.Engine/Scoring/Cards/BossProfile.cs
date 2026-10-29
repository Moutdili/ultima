using System.Text.Json;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Cards;

/// <summary>
/// Profil d'un boss lu dans <c>data/catalog/monsters.json</c> (extraction locale du jeu, lecture
/// seule, hors réseau). Sert uniquement au terme « adéquation boss » : la seule chose qu'on
/// affirme est ce que les <c>moves</c> du monstre contiennent réellement —
/// <list type="bullet">
///   <item><b>multi-coups</b> : un move <c>MultiAttackIntent</c> (ou <c>repeats ≥ 2</c>) ;</item>
///   <item><b>débuffs</b> : <c>DebuffIntent</c>, <c>StatusIntent</c>, <c>CardDebuffIntent</c> ;</item>
///   <item><b>scaling</b> : un <c>BuffIntent</c> (le boss se renforce) ou une attaque aux dégâts
///   calculés (valeur absente → « Current…Damage ») ;</item>
///   <item><b>soins</b> : <c>HealIntent</c>.</item>
/// </list>
/// Fichier ou boss absent ⇒ <see cref="For"/> renvoie null ⇒ aucun terme (jamais de faux chiffre).
/// </summary>
internal sealed class BossProfile
{
    private static readonly Lazy<Dictionary<string, BossProfile>> Cache =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly string[] TrailingSuffixes = { "Boss", "Elite", "Normal", "Weak", "Event", "Encounter" };

    public string Name { get; }
    public bool MultiHits { get; }
    public bool Debuffs { get; }
    public bool Scaling { get; }
    public bool Sustains { get; }
    public bool HasAttack { get; }
    public int MaxHit { get; }
    public int Hp { get; }
    public int Repeats { get; }

    private BossProfile(string name, bool multiHits, bool debuffs, bool scaling, bool sustains,
        bool hasAttack, int maxHit, int hp, int repeats)
    {
        Name = name;
        MultiHits = multiHits;
        Debuffs = debuffs;
        Scaling = scaling;
        Sustains = sustains;
        HasAttack = hasAttack;
        MaxHit = maxHit;
        Hp = hp;
        Repeats = repeats;
    }

    /// <summary>Profil du boss désigné par un id d'encounter (« ENCOUNTER.QUEEN_BOSS »), sinon null.</summary>
    public static BossProfile? For(string? bossId)
    {
        if (string.IsNullOrWhiteSpace(bossId)) return null;
        var monsters = Cache.Value;
        if (monsters.Count == 0) return null;

        foreach (var candidate in Candidates(bossId!))
            if (monsters.TryGetValue(candidate, out var profile)) return profile;
        return null;
    }

    /// <summary>Résumé lisible pour la source du terme (« PV 240, frappe max 16, scaling »).</summary>
    public string Evidence
    {
        get
        {
            var parts = new List<string> { $"PV {Hp}" };
            if (HasAttack) parts.Add($"frappe max {MaxHit}");
            if (MultiHits) parts.Add($"multi-coups ×{Math.Max(2, Repeats)}");
            if (Debuffs) parts.Add("débuffs");
            if (Scaling) parts.Add("scaling");
            if (Sustains) parts.Add("soins");
            if (!HasAttack) parts.Add("aucune attaque");
            return string.Join(", ", parts);
        }
    }

    private static IEnumerable<string> Candidates(string bossId)
    {
        var bare = bossId.Trim();
        if (bare.StartsWith("ENCOUNTER.", StringComparison.OrdinalIgnoreCase)) bare = bare["ENCOUNTER.".Length..];

        var parts = bare.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) yield break;

        var pascal = string.Concat(parts.Select(p =>
            p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));

        yield return pascal;
        foreach (var suffix in TrailingSuffixes)
        {
            if (pascal.Length <= suffix.Length) continue;
            if (!pascal.EndsWith(suffix, StringComparison.Ordinal)) continue;
            yield return pascal[..^suffix.Length];
        }
    }

    private static Dictionary<string, BossProfile> Load()
    {
        var map = new Dictionary<string, BossProfile>(StringComparer.OrdinalIgnoreCase);
        var path = ResolveMonstersPath();
        if (path == null) return map;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var className = Str(el, "class");
                if (string.IsNullOrEmpty(className)) continue;
                map[className!] = Parse(className!, el);
            }
        }
        catch (IOException)
        {
            // Fichier verrouillé : on rend une table vide, le scoring continue sans terme boss.
        }
        catch (JsonException)
        {
            // Donnée illisible : idem, aucune exception ne remonte au scoring.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return map;
    }

    private static BossProfile Parse(string className, JsonElement el)
    {
        bool multiHits = false, debuffs = false, scaling = false, sustains = false, hasAttack = false;
        int maxHit = 0, repeats = 0;

        if (el.TryGetProperty("moves", out var moves) && moves.ValueKind == JsonValueKind.Array)
        {
            foreach (var move in moves.EnumerateArray())
            {
                if (move.ValueKind != JsonValueKind.Object) continue;
                var intent = Str(move, "intent") ?? string.Empty;
                Observe(intent);

                if (move.TryGetProperty("extra_intents", out var extras) && extras.ValueKind == JsonValueKind.Array)
                {
                    foreach (var extra in extras.EnumerateArray())
                    {
                        if (extra.ValueKind != JsonValueKind.Object) continue;
                        Observe(Str(extra, "intent") ?? string.Empty);
                    }
                }

                if (!intent.Contains("Attack", StringComparison.OrdinalIgnoreCase)) continue;
                hasAttack = true;
                var (damage, repeat) = AttackValues(move);
                if (damage is int d && d > maxHit) maxHit = d;
                if (repeat is int r)
                {
                    if (r >= 2) multiHits = true;
                    if (r > repeats) repeats = r;
                }
                // Attaque sans valeur littérale : dégâts calculés (« Current…Damage ») ⇒ montée en puissance.
                if (damage == null) scaling = true;
            }
        }

        var hp = 0;
        if (el.TryGetProperty("min_hp", out var minHp) && minHp.ValueKind == JsonValueKind.Object)
            hp = FirstNumber(minHp) ?? 0;

        return new BossProfile(className, multiHits, debuffs, scaling, sustains, hasAttack, maxHit, hp, repeats);

        void Observe(string intent)
        {
            if (intent.Length == 0) return;
            if (intent.Contains("Debuff", StringComparison.OrdinalIgnoreCase) ||
                intent.Contains("Status", StringComparison.OrdinalIgnoreCase))
                debuffs = true;
            if (intent.Contains("Buff", StringComparison.OrdinalIgnoreCase)) scaling = true;
            if (intent.Contains("Heal", StringComparison.OrdinalIgnoreCase)) sustains = true;
        }
    }

    private static (int? Damage, int? Repeats) AttackValues(JsonElement move)
    {
        if (!move.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            return (null, null);

        int? damage = null, repeats = null;
        var index = 0;
        foreach (var item in values.EnumerateArray())
        {
            if (index == 0) damage = FirstNumber(item);
            else if (index == 1) repeats = FirstNumber(item);
            index++;
        }

        return (damage, repeats);
    }

    private static int? FirstNumber(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.TryGetInt32(out var i) ? i : (int)Math.Round(el.GetDouble());
            case JsonValueKind.Object:
                if (el.TryGetProperty("values", out var inner)) return FirstNumber(inner);
                return null;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    var found = FirstNumber(item);
                    if (found.HasValue) return found;
                }
                return null;
            default:
                return null;
        }
    }

    private static string? ResolveMonstersPath()
    {
        var metaDir = ScoringData.ResolveMetaDir();
        var dataDir = Path.GetDirectoryName(metaDir.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.IsNullOrEmpty(dataDir))
        {
            var direct = Path.Combine(dataDir, "catalog", "monsters.json");
            if (File.Exists(direct)) return direct;
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "data", "catalog", "monsters.json");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
