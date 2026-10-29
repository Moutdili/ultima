using System.Text.Json;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Meta;

/// <summary>
/// Charge data/meta (variante local/ si présente, sinon publique) et indexe cartes, reliques,
/// archétypes et la référence « passer ». Lecture seule, aucun accès réseau.
/// </summary>
public sealed class MetaTable : IMetaTable
{
    private readonly Dictionary<string, MetaEntry> _cards;
    private readonly Dictionary<string, MetaEntry> _relics;
    private readonly Dictionary<string, List<MetaArchetype>> _archetypes;

    public string Variant { get; }
    public string? Generated { get; }
    public string? PatchTarget { get; }
    public double? SkipRate { get; }
    public double? SkipElo { get; }
    public int CardCount => _cards.Count;
    public int RelicCount => _relics.Count;

    /// <summary>Toutes les cartes chargées (lecture seule) — utilisé par les tests de calibrage.</summary>
    public IReadOnlyCollection<MetaEntry> Cards => _cards.Values;

    /// <summary>Toutes les reliques chargées (lecture seule).</summary>
    public IReadOnlyCollection<MetaEntry> Relics => _relics.Values;

    private MetaTable(
        Dictionary<string, MetaEntry> cards,
        Dictionary<string, MetaEntry> relics,
        Dictionary<string, List<MetaArchetype>> archetypes,
        string variant, string? generated, string? patchTarget, double? skipRate, double? skipElo,
        string dataPath = "", string? problem = null)
    {
        _cards = cards;
        _relics = relics;
        _archetypes = archetypes;
        Variant = variant;
        Generated = generated;
        PatchTarget = patchTarget;
        SkipRate = skipRate;
        SkipElo = skipElo;
        DataPath = dataPath;
        Problem = problem;
    }

    /// <summary>Vrai si au moins une carte a été chargée. Faux = les scores n'ont aucune base.</summary>
    public bool IsLoaded => _cards.Count > 0;

    /// <summary>Répertoire effectivement lu (utile dans un rapport d'erreur).</summary>
    public string DataPath { get; }

    /// <summary>
    /// Raison explicite quand la table est vide (fichier absent ou illisible).
    /// Le daemon la traduit en <c>SourceHealth.Degraded</c> : jamais des F 0 silencieux.
    /// </summary>
    public string? Problem { get; }

    public static MetaTable Empty { get; } = new(
        new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase),
        new(StringComparer.OrdinalIgnoreCase), "empty", null, null, null, null,
        "", "table vide : aucune donnée méta chargée");

    public MetaEntry? Card(string id) =>
        _cards.TryGetValue(Ids.Bare(id), out var e) ? e : null;

    public MetaEntry? Relic(string id) =>
        _relics.TryGetValue(Ids.Bare(id), out var e) ? e : null;

    public IReadOnlyList<MetaArchetype> Archetypes(string characterId) =>
        _archetypes.TryGetValue(Ids.CharacterKey(characterId), out var list)
            ? list
            : Array.Empty<MetaArchetype>();

    /// <summary>
    /// Charge la méta. Préfère &lt;metaDir&gt;/local/ (variante complète, non commitée) puis &lt;metaDir&gt;.
    /// Les fichiers absents sont tolérés : la table reste utilisable (scores sans base).
    /// </summary>
    /// <param name="includeLocal">
    /// false = ignore &lt;metaDir&gt;/local/ et ne lit que la variante commitée. Les tests passent false
    /// (ou posent <c>ULTIMA_META_NO_LOCAL=1</c>) : la variante locale de la machine ne doit jamais
    /// décider si un test passe.
    /// </param>
    public static MetaTable Load(string? metaDir = null, bool? includeLocal = null)
    {
        var dir = ScoringData.ResolveMetaDir(metaDir);
        var localDir = Path.Combine(dir, "local");
        var useLocal = includeLocal
            ?? Environment.GetEnvironmentVariable("ULTIMA_META_NO_LOCAL") != "1";

        // Repli FICHIER PAR FICHIER : Mio a un local/ partiel (card_tiers + relic_tiers seulement,
        // notes sous licence). Choisir local/ en bloc laissait archetypes.json introuvable.
        string Pick(string name)
        {
            var local = Path.Combine(localDir, name);
            return useLocal && File.Exists(local) ? local : Path.Combine(dir, name);
        }

        string source = useLocal && File.Exists(Path.Combine(localDir, "card_tiers.json")) ? localDir : dir;

        var cards = new Dictionary<string, MetaEntry>(StringComparer.OrdinalIgnoreCase);
        var relics = new Dictionary<string, MetaEntry>(StringComparer.OrdinalIgnoreCase);
        var archetypes = new Dictionary<string, List<MetaArchetype>>(StringComparer.OrdinalIgnoreCase);

        string variant = "empty";
        string? generated = null, patch = null;
        double? skipRate = null, skipElo = null;
        string? problem = null;

        var cardPath = Pick("card_tiers.json");
        if (!File.Exists(cardPath))
            problem = $"aucune méta de cartes : {cardPath} introuvable";
        if (File.Exists(cardPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(cardPath));
            var root = doc.RootElement;
            variant = Str(root, "variant") ?? (source == localDir ? "full" : "public");
            generated = Str(root, "generated");
            patch = Str(root, "patch_target");

            if (root.TryGetProperty("skip_reference", out var skip) && skip.ValueKind == JsonValueKind.Object)
            {
                var picked = Num(skip, "pick_rate_percent");
                if (picked.HasValue) skipRate = ScoreMath.Clamp(1.0 - picked.Value / 100.0, 0, 1);
                skipElo = Num(skip, "elo");
            }

            ReadGroups(root, "characters", cards);
        }

        var relicPath = Pick("relic_tiers.json");
        if (File.Exists(relicPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(relicPath));
            var root = doc.RootElement;
            generated ??= Str(root, "generated");
            patch ??= Str(root, "patch_target");
            ReadGroups(root, "groups", relics);
        }

        var archPath = Pick("archetypes.json");
        if (File.Exists(archPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(archPath));
            if (doc.RootElement.TryGetProperty("characters", out var chars) &&
                chars.ValueKind == JsonValueKind.Object)
            {
                foreach (var ch in chars.EnumerateObject())
                {
                    if (ch.Value.ValueKind != JsonValueKind.Array) continue;
                    var list = new List<MetaArchetype>();
                    foreach (var item in ch.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        list.Add(new MetaArchetype(
                            Str(item, "name") ?? "(sans nom)",
                            ch.Name.ToLowerInvariant(),
                            Str(item, "source_id") ?? "?",
                            Str(item, "kind") ?? "?",
                            IdsIn(item, "key_cards"),
                            IdsIn(item, "key_relics")));
                    }
                    archetypes[ch.Name] = list;
                }
            }
        }

        ApplyPercentiles(cards);
        ApplyPercentiles(relics);

        if (problem is null && cards.Count == 0)
            problem = $"aucune carte lue dans {cardPath}";

        return new MetaTable(cards, relics, archetypes, variant, generated, patch, skipRate, skipElo,
            dir, problem);
    }

    /// <summary>
    /// Ajoute à chaque entrée son rang de consensus (0..1) dans la table. Une note locale peut être
    /// sur une autre échelle (ou comprimée) : le rang, lui, garde le même sens. Les ex æquo partagent
    /// le rang le plus bas, ce qui évite de fabriquer un ordre inexistant.
    /// </summary>
    private static void ApplyPercentiles(Dictionary<string, MetaEntry> entries)
    {
        var rated = entries.Values
            .Where(e => e.Consensus is not null)
            .Select(e => e.Consensus!.Value)
            .OrderBy(v => v)
            .ToArray();

        if (rated.Length < 2) return;

        foreach (var (key, entry) in entries.ToArray())
        {
            if (entry.Consensus is not double value) continue;

            int index = Array.BinarySearch(rated, value);
            if (index < 0) index = ~index;
            while (index > 0 && rated[index - 1] == value) index--;   // premier ex æquo

            entries[key] = entry with { ConsensusPercentile = (double)index / (rated.Length - 1) };
        }
    }

    private static void ReadGroups(JsonElement root, string groupsKey, Dictionary<string, MetaEntry> into)
    {
        if (!root.TryGetProperty(groupsKey, out var groups) || groups.ValueKind != JsonValueKind.Object) return;

        foreach (var group in groups.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var item in group.Value.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.Object) continue;
                var e = ToEntry(item.Value, item.Name, group.Name);
                if (e != null) into[e.Id] = e;
            }
        }
    }

    private static MetaEntry? ToEntry(JsonElement el, string className, string group)
    {
        var rawId = Str(el, "id");
        var id = Ids.Bare(rawId ?? className);
        if (string.IsNullOrEmpty(id)) return null;

        var ratings = new List<SourceRating>();
        if (el.TryGetProperty("ratings", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in arr.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                var n = Num(r, "n");
                ratings.Add(new SourceRating(
                    Str(r, "source_id") ?? "?",
                    Str(r, "tier"),
                    Num(r, "score"),
                    Str(r, "patch"),
                    Str(r, "date"),
                    Str(r, "note"),
                    n.HasValue ? (int)n.Value : null));
            }
        }

        double? consensus = null;
        int sourceCount = ratings.Count;
        double spread = 0;
        if (el.TryGetProperty("consensus", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            consensus = Num(c, "score");
            var ns = Num(c, "n_sources");
            if (ns.HasValue) sourceCount = (int)ns.Value;
            spread = Num(c, "spread") ?? 0;
        }

        var cost = Num(el, "cost");

        return new MetaEntry(
            id,
            Str(el, "class") ?? className,
            Str(el, "name_eng") ?? className,
            Str(el, "name_fra"),
            Str(el, "type"),
            Str(el, "rarity"),
            cost.HasValue ? (int)cost.Value : null,
            Str(el, "owner") ?? group,
            ratings,
            consensus,
            sourceCount,
            spread);
    }

    private static IReadOnlyList<string> IdsIn(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var it in arr.EnumerateArray())
        {
            if (it.ValueKind != JsonValueKind.Object) continue;
            var id = Str(it, "id") ?? Str(it, "class");
            if (!string.IsNullOrWhiteSpace(id)) list.Add(Ids.Bare(id));
        }
        return list;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
            ? d
            : null;
}
