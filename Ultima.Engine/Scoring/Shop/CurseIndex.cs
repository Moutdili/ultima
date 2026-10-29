using System.Text.Json;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Shop;

/// <summary>
/// Reconnaît une malédiction dans un deck, sans se fier au nom de l'id.
///
/// Deux sources, dans cet ordre :
/// 1. <see cref="IMetaTable.Card"/> — une entrée méta dont <c>Type</c> ou <c>Rarity</c> contient
///    « Curse » est une malédiction (source de vérité, cf. docs/24-hermes-arch.md §2) ;
/// 2. repli : <c>data/catalog/cards.json</c> (champ <c>type</c> / <c>rarity</c> = « Curse »).
///    Nécessaire parce que la méta publique ne note pas les malédictions
///    (0 entrée Curse dans <c>data/meta/card_tiers.json</c> au 2026-10-28) alors que les saves
///    réelles en contiennent (ex. <c>CARD.DECAY</c> dans les fixtures).
///
/// Aucune liste d'ids en dur : le repli est chargé une fois depuis le catalogue du jeu, et un
/// catalogue absent laisse simplement le repli vide (jamais d'exception).
/// </summary>
public static class CurseIndex
{
    private const string CatalogRelative = "data/catalog/cards.json";
    private const string Curse = "Curse";

    private static readonly Lazy<HashSet<string>> CatalogCurses =
        new(LoadCatalogCurses, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Vrai si <paramref name="cardId"/> est une malédiction. <paramref name="meta"/> peut être
    /// null : seul le catalogue du jeu est alors consulté.
    /// </summary>
    public static bool IsCurse(IMetaTable? meta, string cardId)
    {
        if (IsCurseEntry(meta?.Card(cardId))) return true;

        var key = Canonical(cardId);
        return key.Length > 0 && CatalogCurses.Value.Contains(key);
    }

    /// <summary>Nom affichable d'une malédiction (méta FR/EN si connue, sinon l'id).</summary>
    public static string Display(IMetaTable? meta, string cardId) =>
        meta?.Card(cardId)?.DisplayName ?? Ids.Bare(cardId);

    /// <summary>Vrai si l'entrée méta décrit une malédiction (Type/Rarity « Curse »).</summary>
    internal static bool IsCurseEntry(MetaEntry? entry) =>
        entry != null && (Contains(entry.Type) || Contains(entry.Rarity));

    private static bool Contains(string? value) =>
        value != null && value.Contains(Curse, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Forme canonique d'un id : sans préfixe CARD./RELIC., sans séparateur, en majuscules.
    /// « CARD.CURSE_OF_THE_BELL », « CurseOfTheBell » et « curseofthebell » se comparent égaux.
    /// </summary>
    private static string Canonical(string? id)
    {
        var bare = Ids.Bare(id);
        if (bare.Length == 0) return string.Empty;
        Span<char> buffer = stackalloc char[bare.Length];
        int n = 0;
        foreach (var c in bare)
            if (char.IsLetterOrDigit(c)) buffer[n++] = char.ToUpperInvariant(c);
        return new string(buffer[..n]);
    }

    private static HashSet<string> LoadCatalogCurses()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var path = ResolveCatalogPath();
        if (path == null) return set;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return set;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var type = Str(el, "type");
                var rarity = Str(el, "rarity");
                if (!Contains(type) && !Contains(rarity)) continue;

                var cls = Str(el, "class") ?? Str(el, "id");
                var key = Canonical(cls);
                if (key.Length > 0) set.Add(key);
            }
        }
        catch (Exception)
        {
            // Catalogue absent/illisible : repli vide. Le scoring reste déterministe et borné.
        }

        return set;
    }

    private static string? ResolveCatalogPath()
    {
        var env = Environment.GetEnvironmentVariable("ULTIMA_DATA");
        if (!string.IsNullOrWhiteSpace(env))
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(env!, "data", "catalog", "cards.json"),
                         Path.Combine(env!, "catalog", "cards.json")
                     })
                if (File.Exists(candidate)) return candidate;
        }

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "data", "catalog", "cards.json");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        var local = Path.Combine(Directory.GetCurrentDirectory(), CatalogRelative);
        return File.Exists(local) ? local : null;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
