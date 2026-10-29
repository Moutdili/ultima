using System.Text.Json;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Cards;

/// <summary>
/// Effets numériques d'une carte, tels que <c>data/catalog/cards_effects.json</c> les porte.
/// <para>
/// Les valeurs viennent de l'API spire-codex (dégâts, bloc, coups, pioche, énergie, pouvoirs) :
/// c'est ce que les descriptions ne disent pas. Un champ que la source ne donne pas reste
/// <c>null</c> — on ne le remplace jamais par un 0 inventé.
/// </para>
/// <para>
/// <see cref="Hits"/> vaut 1 quand la source ne précise pas de nombre de coups : une carte qui
/// frappe une fois n'a pas besoin de le dire. Pour une carte X (<see cref="IsXCost"/>), le nombre
/// de répétitions vaut l'énergie dépensée, donc <see cref="Damage"/> est le montant PAR répétition.
/// </para>
/// </summary>
public sealed record CardEffect(
    string Id,
    string Name,
    int? Cost,
    int? CostUp,
    bool IsXCost,
    string? Type,
    string? Rarity,
    string? Color,
    string? Target,
    int? DamageBase,
    int? DamageUp,
    int? BlockBase,
    int? BlockUp,
    int? HitCount,
    int? DrawBase,
    int? DrawUp,
    int? EnergyBase,
    int? EnergyUp,
    IReadOnlyList<string> Powers,
    IReadOnlyDictionary<string, int> Vars,
    IReadOnlyList<string> Keywords)
{
    /// <summary>
    /// Nombre de coups de base (1 si la source ne le précise pas). L'amélioration n'est pas
    /// repliée ici : <c>upgrade.repeat</c> (+1 sur 10 cartes) compte des répétitions d'effet, pas
    /// toujours des coups — le JSON brut le porte, ce record non.
    /// </summary>
    public int Hits => HitCount ?? 1;

    /// <summary>
    /// Dégâts par coup, améliorés si <paramref name="upgrade"/> &gt; 0. Null quand la carte
    /// n'inflige pas de dégâts fixes (bloc, poison, dégâts calculés…).
    /// </summary>
    public int? Damage(int upgrade) => upgrade > 0 ? DamageUp : DamageBase;

    /// <summary>Bloc gagné, amélioré si <paramref name="upgrade"/> &gt; 0. Null si la carte n'en donne pas.</summary>
    public int? Block(int upgrade) => upgrade > 0 ? BlockUp : BlockBase;

    /// <summary>Cartes piochées (0 si la carte ne pioche pas).</summary>
    public int Draw(int upgrade) => (upgrade > 0 ? DrawUp : DrawBase) ?? 0;

    /// <summary>Énergie rendue (0 si la carte n'en rend pas).</summary>
    public int Energy(int upgrade) => (upgrade > 0 ? EnergyUp : EnergyBase) ?? 0;

    /// <summary>Coût en énergie après amélioration (un coût X vaut -1 en base, comme dans le jeu).</summary>
    public int? CostAfter(int upgrade) => upgrade > 0 ? CostUp ?? Cost : Cost;

    /// <summary>Vrai si la carte applique ce pouvoir (insensible à la casse).</summary>
    public bool HasPower(string power) =>
        Powers.Contains(power, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Montant d'un pouvoir appliqué, lu dans <see cref="Vars"/> (« Vulnerable » ou
    /// « VulnerablePower »). Null si la source ne chiffre pas ce pouvoir pour cette carte.
    /// </summary>
    public int? PowerAmount(string power)
    {
        if (Vars.TryGetValue(power + "Power", out var withSuffix)) return withSuffix;
        return Vars.TryGetValue(power, out var bare) ? bare : null;
    }

    /// <summary>Vrai si la carte porte ce mot-clé (Exhaust, Ethereal, Retain, Sly…).</summary>
    public bool HasKeyword(string keyword) =>
        Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);

    /// <summary>Dégâts totaux d'une carte non-X (dégâts par coup × coups). 0 si la carte n'en inflige pas.</summary>
    public int TotalDamage(int upgrade) => (Damage(upgrade) ?? 0) * Hits;
}

/// <summary>
/// Charge <c>data/catalog/cards_effects.json</c> (généré par <c>tools/index_card_effects.py</c>)
/// et sert les effets numériques par id de carte, sans jamais lever sur une carte inconnue.
/// Lecture seule, aucun accès réseau ; fichier absent → <see cref="Empty"/>.
/// </summary>
public sealed class CardEffects
{
    private static readonly IReadOnlyDictionary<string, int> NoVars =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, CardEffect> _byId = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byId.Count;

    /// <summary>Source déclarée par le fichier (chemin codex, non commité). Null si absent.</summary>
    public string? Source { get; private init; }

    /// <summary>Script générateur déclaré par le fichier. Null si absent.</summary>
    public string? GeneratedBy { get; private init; }

    /// <summary>Toutes les cartes chargées (lecture seule).</summary>
    public IReadOnlyCollection<CardEffect> All => _byId.Values;

    private CardEffects()
    {
    }

    /// <summary>Catalogue vide : aucune donnée, aucune exception. Le scoring doit alors s'abstenir.</summary>
    public static CardEffects Empty { get; } = new();

    private static readonly Lazy<CardEffects> LazyDefault = new(() => Load());

    /// <summary>Catalogue du dépôt (lazy). Jamais null : <see cref="Empty"/> si le fichier manque.</summary>
    public static CardEffects Default => LazyDefault.Value;

    /// <summary>« CARD.STRIKE_IRONCLAD » ou « STRIKE_IRONCLAD ». Null si la carte est inconnue.</summary>
    public CardEffect? Get(string? cardId)
    {
        var bare = Ids.Bare(cardId);
        return bare.Length > 0 && _byId.TryGetValue(bare, out var effect) ? effect : null;
    }

    /// <summary>
    /// Charge le catalogue. <paramref name="catalogDir"/> explicite sinon
    /// <see cref="ScoringData.ResolveCatalogDir"/>. Fichier absent ou illisible au sens JSON :
    /// catalogue vide (le scoring continue sans les chiffres, il n'invente rien).
    /// </summary>
    public static CardEffects Load(string? catalogDir = null)
    {
        var path = Path.Combine(ScoringData.ResolveCatalogDir(catalogDir), "cards_effects.json");
        if (!File.Exists(path)) return Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (JsonException)
        {
            return Empty;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var catalog = new CardEffects
            {
                Source = Str(root, "source"),
                GeneratedBy = Str(root, "generated_by"),
            };

            if (!root.TryGetProperty("cards", out var cards) || cards.ValueKind != JsonValueKind.Array)
                return catalog;

            foreach (var el in cards.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var id = Ids.Bare(Str(el, "id"));
                if (id.Length == 0) continue;

                catalog._byId[id] = new CardEffect(
                    id,
                    Str(el, "name") ?? id,
                    Int(el, "cost"),
                    Int(el, "cost_up"),
                    Bool(el, "x_cost"),
                    Str(el, "type"),
                    Str(el, "rarity"),
                    Str(el, "color"),
                    Str(el, "target"),
                    Int(el, "damage_base"),
                    Int(el, "damage_up"),
                    Int(el, "block_base"),
                    Int(el, "block_up"),
                    Int(el, "hits"),
                    Int(el, "draw_base"),
                    Int(el, "draw_up"),
                    Int(el, "energy_base"),
                    Int(el, "energy_up"),
                    Strings(el, "powers"),
                    IntMap(el, "vars"),
                    Strings(el, "keywords"));
            }

            return catalog;
        }
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        if (v.TryGetInt32(out var i)) return i;
        return v.TryGetDouble(out var d) ? (int)Math.Round(d) : null;
    }

    private static bool Bool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> Strings(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                list.Add(s);
        return list;
    }

    private static IReadOnlyDictionary<string, int> IntMap(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return NoVars;

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in obj.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n))
                map[p.Name] = n;
        return map;
    }
}
