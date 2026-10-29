using System.Collections.Concurrent;
using System.Text.Json;
using Ultima.Engine.Contracts;
using Ultima.Knowledge.Build;
using Ultima.Knowledge.Text;

namespace Ultima.Knowledge;

/// <summary>
/// Base de connaissance statique (implémente <see cref="IKnowledge"/>), chargée depuis <c>kb.json</c>
/// généré localement par <c>tools/kb/build-kb</c>. Pure : aucun accès au jeu à l'exécution.
/// Repli des noms : FR → EN → identifiant (signalé par <c>NameSource</c> et <see cref="Fallbacks"/>).
/// Les champs <c>Type</c>, <c>Rarity</c>, <c>Owner</c> et <c>Keywords</c> sont les valeurs canoniques du jeu
/// (« Attack », « Curse », « Ironclad », « Exhaust ») ; libellés FR via <see cref="CardTypeLabel"/>,
/// <see cref="RarityLabel"/>, <see cref="KeywordName"/>.
/// </summary>
public sealed class KnowledgeBase : IKnowledge
{
    private readonly Dictionary<string, KbCard> _cards;
    private readonly Dictionary<string, KbRelic> _relics;
    private readonly Dictionary<string, KbEnchantment> _enchantments;
    private readonly Dictionary<string, KbPotion> _potions;
    private readonly Dictionary<string, KbNamed> _encounters;
    private readonly Dictionary<string, KbNamed> _characters;
    private readonly ConcurrentDictionary<string, byte> _missing = new(StringComparer.Ordinal);
    private readonly GameTextRenderer _renderer = new();

    public KbDocument Document { get; }

    /// <summary><c>$XDG_DATA_HOME/ultima/kb/kb.json</c> (défaut <c>~/.local/share/ultima/kb/kb.json</c>).</summary>
    public static string DefaultPath
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var root = string.IsNullOrEmpty(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                : xdg;
            return Path.Combine(root, "ultima", "kb", "kb.json");
        }
    }

    public KnowledgeBase(KbDocument document)
    {
        if (document.Schema > KbDocument.CurrentSchema)
            throw new InvalidDataException($"kb.json schéma {document.Schema} > {KbDocument.CurrentSchema} : mettre à jour ULTIMA");
        Document = document;
        _cards = Index(document.Cards);
        _relics = Index(document.Relics);
        _enchantments = Index(document.Enchantments);
        _potions = Index(document.Potions);
        _encounters = Index(document.Encounters);
        _characters = Index(document.Characters);
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> items) where T : KbNamed
    {
        var d = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var i in items) d[i.Id] = i;
        return d;
    }

    public static KnowledgeBase Load(string path)
    {
        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<KbDocument>(stream, KbJson.Options)
                  ?? throw new InvalidDataException($"kb.json vide : {path}");
        return new KnowledgeBase(doc);
    }

    /// <summary>Charge la base par défaut ; <c>null</c> + raison si absente (lancer <c>tools/kb/build-kb</c>).</summary>
    public static KnowledgeBase? TryLoadDefault(out string? error)
    {
        var path = DefaultPath;
        if (!File.Exists(path)) { error = $"base absente : {path} (lancer tools/kb/build-kb)"; return null; }
        try { error = null; return Load(path); }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            error = $"base illisible : {path} : {e.Message}";
            return null;
        }
    }

    /// <summary>Identifiants demandés mais inconnus de la base (signalés, jamais devinés).</summary>
    public IReadOnlyCollection<string> MissingIds => _missing.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>Entrées dont le nom n'est pas en français (repli EN ou identifiant).</summary>
    public IReadOnlyList<KbNamed> Fallbacks =>
        Document.Cards.Cast<KbNamed>().Concat(Document.Relics).Concat(Document.Enchantments).Concat(Document.Potions)
            .Concat(Document.Encounters).Concat(Document.Characters).Where(e => e.NameSource != "fra").ToList();

    private T? Find<T>(Dictionary<string, T> map, string category, string id) where T : class
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var key = id.Contains('.') || id.All(c => !char.IsLower(c))
            ? GameIds.Normalize(category, id)
            : GameIds.Make(category, id); // nom de classe C# : "AscendersBane"
        if (map.TryGetValue(key, out var hit)) return hit;
        _missing.TryAdd(key, 0);
        return null;
    }

    // ---------- IKnowledge ----------
    public CardInfo? Card(string id) => Card(id, 0);

    /// <summary>Carte au niveau d'amélioration donné (save : <c>CardInstance.UpgradeLevel</c>) : nom « + », coût, texte.</summary>
    public CardInfo? Card(string id, int upgradeLevel)
    {
        var c = Find(_cards, GameIds.Card, id);
        if (c == null) return null;
        bool up = upgradeLevel > 0 && c.MaxUpgradeLevel > 0;
        var name = !up ? c.Name : c.MaxUpgradeLevel > 1 ? $"{c.Name}+{upgradeLevel}" : c.Name + "+";
        return new CardInfo(c.Id, c.ClassName, name,
            up ? c.DescriptionUpgraded ?? c.Description : c.Description,
            c.Type, c.Rarity, up ? c.CostUpgraded ?? c.Cost : c.Cost, c.Owner, c.IsCurse,
            up ? c.KeywordsUpgraded ?? c.Keywords : c.Keywords);
    }

    public RelicInfo? Relic(string id)
    {
        var r = Find(_relics, GameIds.Relic, id);
        return r == null ? null : new RelicInfo(r.Id, r.ClassName, r.Name, r.Description, r.Rarity, r.Owner);
    }

    public EnchantmentInfo? Enchantment(string id)
    {
        var e = Find(_enchantments, GameIds.Enchantment, id);
        return e == null ? null : new EnchantmentInfo(e.Id, e.ClassName, e.Name, e.Description);
    }

    /// <summary>Enchantement avec son montant réel (save : <c>enchantment.amount</c>), ex. Célérité 2 → « piochez 2 cartes ».</summary>
    public EnchantmentInfo? Enchantment(string id, int amount)
    {
        var e = Find(_enchantments, GameIds.Enchantment, id);
        if (e == null) return null;
        var tpl = e.TemplateFr ?? e.TemplateEn;
        var desc = tpl == null ? e.Description : RenderWithAmount(e, tpl, amount, e.TemplateFr != null);
        return new EnchantmentInfo(e.Id, e.ClassName, e.Name, desc);
    }

    /// <summary>Ligne ajoutée sur la carte enchantée (extraCardText), avec le montant réel ; <c>null</c> si aucune.</summary>
    public string? EnchantmentCardText(string id, int amount)
    {
        var e = Find(_enchantments, GameIds.Enchantment, id);
        var tpl = e?.ExtraTemplateFr ?? e?.ExtraTemplateEn;
        return e == null || tpl == null ? null : RenderWithAmount(e, tpl, amount, e.ExtraTemplateFr != null);
    }

    private string RenderWithAmount(KbEnchantment e, string tpl, int amount, bool fr)
    {
        var ctx = new Dictionary<string, TextValue>(StringComparer.Ordinal)
        {
            ["Amount"] = TextValue.Of(amount),
            ["energyPrefix"] = TextValue.Of("colorless"),
            ["singleStarIcon"] = TextValue.Of(_renderer.Options.StarGlyph),
        };
        return _renderer.Render(tpl, KbBuilder.StaticVars(e.Vars, upgraded: false, ctx), fr ? "fr" : "en").Text;
    }

    public PotionInfo? Potion(string id)
    {
        var p = Find(_potions, GameIds.Potion, id);
        return p == null ? null : new PotionInfo(p.Id, p.ClassName, p.Name, p.Description, p.Rarity);
    }

    public string? EncounterName(string id) => Find(_encounters, GameIds.Encounter, id)?.Name;

    public string? CharacterName(string id) => Find(_characters, GameIds.Character, id)?.Name;

    // ---------- Libellés ----------
    /// <summary>« Exhaust » → « Épuisement ».</summary>
    public string KeywordName(string keyword) =>
        Document.Keywords.TryGetValue(keyword, out var l) ? l.Best(keyword) : keyword;

    /// <summary>« Attack » → « Attaque ».</summary>
    public string CardTypeLabel(string type) => Label("CARD_TYPE." + type.ToUpperInvariant(), type);

    /// <summary><paramref name="kind"/> = card | relic | potion ; « Curse » → « Malédiction ».</summary>
    public string RarityLabel(string kind, string rarity) =>
        Label(kind.ToUpperInvariant() + "_RARITY." + GameIds.Slugify(rarity), rarity);

    private string Label(string key, string fallback) =>
        Document.Labels.TryGetValue(key, out var l) ? l.Best(fallback) : fallback;

    // ---------- Accès bruts ----------
    public KbCard? CardEntry(string id) => Find(_cards, GameIds.Card, id);
    public KbRelic? RelicEntry(string id) => Find(_relics, GameIds.Relic, id);
    public KbEnchantment? EnchantmentEntry(string id) => Find(_enchantments, GameIds.Enchantment, id);
    public KbPotion? PotionEntry(string id) => Find(_potions, GameIds.Potion, id);
}
