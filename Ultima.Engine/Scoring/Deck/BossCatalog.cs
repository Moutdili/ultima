using System.Text;
using System.Text.Json;

namespace Ultima.Engine.Scoring.Deck;

/// <summary>
/// Profil d'un boss lu dans le catalogue du jeu : plus gros coup en un tour
/// (<c>dégâts × répétitions</c>, données <c>intents_by_monster.json</c>) et PV de base.
/// </summary>
public sealed record BossProfile(string ClassName, int BiggestHit, int? Hp, int AttackMoves)
{
    public string DisplayName => NameFormat.FromClassName(ClassName);
}

/// <summary>
/// Résolution d'un id d'rencontre (<c>ENCOUNTER.WATERFALL_GIANT_BOSS</c>) vers le monstre du
/// catalogue (<c>WaterfallGiant</c>) et son profil de dégâts. Aucune table inventée : on compare
/// les noms normalisés (majuscules, alphanumériques) et on retire les suffixes de rencontre
/// (<c>BOSS</c>, <c>ELITE</c>…). Un id non résolu ne produit aucun terme de score.
/// </summary>
public sealed class BossCatalog
{
    private static readonly string[] EncounterSuffixes = { "MINIBOSS", "BOSS", "ELITE", "FIGHT", "EASY", "HARD", "1", "2" };

    private readonly Dictionary<string, BossProfile> _byKey;

    private BossCatalog(Dictionary<string, BossProfile> byKey) => _byKey = byKey;

    public int Count => _byKey.Count;

    public static BossCatalog Empty { get; } = new(new Dictionary<string, BossProfile>(StringComparer.Ordinal));

    private static readonly Lazy<BossCatalog> LazyDefault = new(() => LoadFrom(DataPaths.CatalogDir()));

    /// <summary>Catalogue du dépôt (lazy). Jamais null : <see cref="Empty"/> si data/catalog manque.</summary>
    public static BossCatalog Default => LazyDefault.Value;

    /// <summary>Profil du boss désigné par <paramref name="bossId"/> ; null si l'id ne résout pas.</summary>
    public BossProfile? Resolve(string? bossId)
    {
        var key = EncounterKey(bossId);
        if (key.Length == 0) return null;

        if (_byKey.TryGetValue(key, out var exact)) return exact;

        // Repli : préfixe le plus long (ex. ENCOUNTER.THE_INSATIABLE → TheInsatiable).
        BossProfile? best = null;
        int bestLen = 0;
        string bestName = string.Empty;
        foreach (var (k, profile) in _byKey)
        {
            if (k.Length < 6) continue;
            if (!key.StartsWith(k, StringComparison.Ordinal) && !k.StartsWith(key, StringComparison.Ordinal)) continue;

            bool better = k.Length > bestLen ||
                          (k.Length == bestLen && string.CompareOrdinal(k, bestName) < 0);
            if (!better) continue;

            best = profile;
            bestLen = k.Length;
            bestName = k;
        }

        return best;
    }

    /// <summary>Normalise un id d'rencontre : « ENCOUNTER.X_BOSS » → « X ».</summary>
    public static string EncounterKey(string? encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId)) return string.Empty;

        var sb = new StringBuilder(encounterId!.Length);
        foreach (var c in encounterId.ToUpperInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        var key = sb.ToString();

        const string prefix = "ENCOUNTER";
        if (key.StartsWith(prefix, StringComparison.Ordinal)) key = key[prefix.Length..];

        bool trimmed = true;
        while (trimmed)
        {
            trimmed = false;
            foreach (var suffix in EncounterSuffixes)
            {
                if (key.Length <= suffix.Length || !key.EndsWith(suffix, StringComparison.Ordinal)) continue;
                key = key[..^suffix.Length];
                trimmed = true;
                break;
            }
        }

        return key;
    }

    /// <summary>Charge <c>data/catalog/monsters.json</c> + <c>intents_by_monster.json</c>.</summary>
    public static BossCatalog LoadFrom(string? catalogDir)
    {
        var byKey = new Dictionary<string, BossProfile>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(catalogDir)) return new BossCatalog(byKey);

        var monstersPath = Path.Combine(catalogDir!, "monsters.json");
        if (!File.Exists(monstersPath)) return new BossCatalog(byKey);

        var intentsPath = Path.Combine(catalogDir!, "intents_by_monster.json");
        var damage = File.Exists(intentsPath) ? ReadDamage(intentsPath) : new Dictionary<string, (int Hit, int Attacks)>();

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(monstersPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new BossCatalog(byKey);

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var className = el.TryGetProperty("class", out var cn) && cn.ValueKind == JsonValueKind.String
                    ? cn.GetString()
                    : null;
                var key = NameFormat.ToCardId(className).Replace("_", string.Empty, StringComparison.Ordinal);
                if (key.Length == 0) continue;

                var hp = ReadHp(el);
                damage.TryGetValue(className!, out var dmg);
                byKey[key] = new BossProfile(className!, dmg.Hit, hp, dmg.Attacks);
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new BossCatalog(byKey);
    }

    private static int? ReadHp(JsonElement monster)
    {
        if (!monster.TryGetProperty("min_hp", out var hp) || hp.ValueKind != JsonValueKind.Object) return null;
        if (!hp.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) return null;

        int? best = null;
        foreach (var v in values.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n)) continue;
            if (best is null || n > best) best = n;
        }
        return best;
    }

    private static Dictionary<string, (int Hit, int Attacks)> ReadDamage(string path)
    {
        var result = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var monster in doc.RootElement.EnumerateObject())
            {
                if (monster.Value.ValueKind != JsonValueKind.Array) continue;

                int biggest = 0, attacks = 0;
                foreach (var move in monster.Value.EnumerateArray())
                {
                    if (move.ValueKind != JsonValueKind.Object) continue;
                    var intent = move.TryGetProperty("intent", out var it) && it.ValueKind == JsonValueKind.String
                        ? it.GetString() ?? string.Empty
                        : string.Empty;
                    if (!intent.Contains("Attack", StringComparison.OrdinalIgnoreCase)) continue;

                    attacks++;
                    int hit = (MaxOf(move, "damage") ?? 0) * Math.Max(1, MaxOf(move, "repeats") ?? 1);
                    if (hit > biggest) biggest = hit;
                }

                result[monster.Name] = (biggest, attacks);
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return result;
    }

    private static int? MaxOf(JsonElement move, string name)
    {
        if (!move.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;

        int? best = null;
        foreach (var v in arr.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n)) continue;
            if (best is null || n > best) best = n;
        }
        return best;
    }
}
