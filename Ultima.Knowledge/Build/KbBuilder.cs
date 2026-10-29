using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ultima.Knowledge.Text;

namespace Ultima.Knowledge.Build;

public sealed record KbBuildInput(
    string GameDir,            // …/game (contient pck/localization et pck/src/Core/Models)
    string CatalogDir,         // data/catalog (cards.json)
    string GameVersion,        // ex. "v0.107.1"
    DateTimeOffset? Now = null);

/// <summary>
/// Génère la base de connaissance depuis le jeu installé (lecture seule) : localisation FR + EN,
/// sources des modèles (variables, mots-clés, pools) et catalogue <c>data/catalog/cards.json</c>.
/// </summary>
public static partial class KbBuilder
{
    public const string Generator = "ultima-kb/1";

    private static readonly string[] LocFiles =
        ["cards", "relics", "enchantments", "potions", "encounters", "characters", "card_keywords", "gameplay_ui"];

    // Pools génériques, par priorité décroissante ; tout autre pool (Ironclad, Silent, Defect…) est un
    // personnage et passe devant quand un modèle figure dans plusieurs pools.
    private static readonly string[] GenericPools =
        ["Shared", "Colorless", "Curse", "Status", "Event", "Token", "Quest", "Fallback", "Deprecated", "Mock"];

    // CardKeywordOrder.cs : avant la description (insérés en tête un à un), puis après.
    private static readonly string[] KeywordsBefore = ["Ethereal", "Sly", "Retain", "Innate", "Unplayable"];
    private static readonly string[] KeywordsAfter = ["Exhaust", "Eternal"];

    [GeneratedRegex(@"ModelDb\.(Card|Relic|Potion)<(\w+)>\(\)")]
    private static partial Regex PoolEntry();

    [GeneratedRegex(@"return\s+(\w+)\.(Potions|Relics|Cards)\s*;")]
    private static partial Regex PoolIndirection();

    public static KbDocument Build(KbBuildInput input)
    {
        var pck = Path.Combine(input.GameDir, "pck");
        var locDir = Path.Combine(pck, "localization");
        var modelsDir = Path.Combine(pck, "src", "Core", "Models");
        var doc = new KbDocument
        {
            GameVersion = input.GameVersion,
            GeneratedAt = (input.Now ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Generator = Generator,
            Source = new KbSource
            {
                GameDir = input.GameDir, LocalizationDir = locDir, ModelsDir = modelsDir, CatalogDir = input.CatalogDir,
            },
        };

        var fr = LoadLoc(locDir, "fra", doc);
        var en = LoadLoc(locDir, "eng", doc);
        var loc = new Loc(fr, en);
        var renderer = new GameTextRenderer();
        var notes = doc.Coverage.Notes;

        // Mots-clés et libellés d'interface.
        foreach (var kw in new[] { "Exhaust", "Ethereal", "Innate", "Unplayable", "Retain", "Sly", "Eternal" })
        {
            var key = GameIds.Slugify(kw) + ".title";
            doc.Keywords[kw] = new KbLabel { Fr = loc.Fr("card_keywords", key), En = loc.En("card_keywords", key) };
        }
        foreach (var (k, v) in en.GetValueOrDefault("gameplay_ui") ?? [])
            if (k.StartsWith("CARD_TYPE.") || k.StartsWith("CARD_RARITY.") || k.StartsWith("RELIC_RARITY.") || k.StartsWith("POTION_RARITY."))
                doc.Labels[k] = new KbLabel { Fr = loc.Fr("gameplay_ui", k), En = v };
        var periodFr = loc.Fr("card_keywords", "PERIOD") ?? ".";
        var periodEn = loc.En("card_keywords", "PERIOD") ?? ".";

        var pools = LoadPools(Path.Combine(modelsDir), notes);
        var catalog = LoadCatalog(input.CatalogDir, doc);

        // ---- Cartes ----
        foreach (var f in ScanDir(Path.Combine(modelsDir, "Cards"), "CardModel"))
        {
            var id = GameIds.Make(GameIds.Card, f.ClassName);
            var card = new KbCard { Id = id, ClassName = f.ClassName, Vars = f.Vars };
            catalog.TryGetValue(f.ClassName, out var cat);
            card.Type = cat?.Type ?? f.CardType ?? "";
            card.Rarity = cat?.Rarity ?? f.CardRarity ?? "";
            // Coût : la source fait foi (-1 = injouable ou X, cf. CostX) ; le catalogue écrit 0 pour -1.
            card.Cost = f.Cost ?? cat?.Cost ?? 0;
            card.Target = cat?.Target ?? f.Target ?? "";
            if (cat == null) notes.Add($"{id} : absent du catalogue, type/rareté lus dans la source");
            else if (f.CardType != null && (cat.Type != f.CardType || cat.Rarity != f.CardRarity))
                notes.Add($"{id} : catalogue ({cat.Type},{cat.Rarity}) ≠ source ({f.CardType},{f.CardRarity})");
            else if (f.Cost != null && cat.Cost != f.Cost && !(cat.Cost == 0 && f.Cost == -1))
                notes.Add($"{id} : coût catalogue {cat.Cost} ≠ source {f.Cost}");
            if (f.Cost == null) notes.Add($"{id} : constructeur non reconnu, coût lu dans le catalogue");
            ResolveRefs(f.Vars, loc);
            card.CostX = f.CostX;
            card.GainsBlock = f.GainsBlock;
            card.MaxUpgradeLevel = f.MaxUpgradeLevel ?? 1;
            if (f.CostUpgradeDelta is int cd && card.MaxUpgradeLevel > 0) card.CostUpgraded = card.Cost + cd;
            card.Owner = pools.Owner("Card", f.ClassName) ?? "None";
            card.IsCurse = card.Type == "Curse" || card.Rarity == "Curse";
            card.Keywords = [.. f.Keywords];
            if (card.MaxUpgradeLevel > 0 && (f.KeywordsAddedOnUpgrade.Count > 0 || f.KeywordsRemovedOnUpgrade.Count > 0))
                card.KeywordsUpgraded = [.. f.Keywords.Except(f.KeywordsRemovedOnUpgrade).Union(f.KeywordsAddedOnUpgrade)];
            Name(card, loc, "cards", f.ClassName);
            card.TemplateFr = loc.Fr("cards", GameIds.Entry(id) + ".description");
            card.TemplateEn = loc.En("cards", GameIds.Entry(id) + ".description");

            var lang = card.TemplateFr != null ? "fra" : card.TemplateEn != null ? "eng" : "none";
            card.DescriptionSource = lang;
            var unresolved = new SortedSet<string>(StringComparer.Ordinal);
            card.Description = RenderCard(card, upgraded: false, renderer, doc, unresolved, notes);
            bool upgradeChangesText = card.MaxUpgradeLevel > 0 &&
                (card.Vars.Any(v => v.Upgrade != 0) || card.KeywordsUpgraded != null ||
                 (card.TemplateFr ?? card.TemplateEn ?? "").Contains("IfUpgraded", StringComparison.Ordinal));
            if (upgradeChangesText)
                card.DescriptionUpgraded = RenderCard(card, upgraded: true, renderer, doc, unresolved, notes);
            if (unresolved.Count > 0) card.UnresolvedVars = [.. unresolved];
            foreach (var issue in f.Issues) notes.Add($"{id} : {issue}");
            doc.Cards.Add(card);
        }

        // ---- Reliques ----
        foreach (var f in ScanDir(Path.Combine(modelsDir, "Relics"), "RelicModel"))
        {
            var id = GameIds.Make(GameIds.Relic, f.ClassName);
            var relic = new KbRelic
            {
                Id = id, ClassName = f.ClassName, Vars = f.Vars, Rarity = f.Rarity ?? "None",
                Owner = pools.Owner("Relic", f.ClassName) ?? "None",
            };
            ResolveRefs(f.Vars, loc);
            Name(relic, loc, "relics", f.ClassName);
            relic.TemplateFr = loc.Fr("relics", GameIds.Entry(id) + ".description");
            relic.TemplateEn = loc.En("relics", GameIds.Entry(id) + ".description");
            RenderSimple(relic, renderer, notes, f.Issues, extra: null);
            doc.Relics.Add(relic);
        }

        // ---- Potions ----
        foreach (var f in ScanDir(Path.Combine(modelsDir, "Potions"), "PotionModel"))
        {
            var id = GameIds.Make(GameIds.Potion, f.ClassName);
            var potion = new KbPotion
            {
                Id = id, ClassName = f.ClassName, Vars = f.Vars, Rarity = f.Rarity ?? "None",
                Owner = pools.Owner("Potion", f.ClassName) ?? "None", Usage = f.Usage ?? "",
            };
            ResolveRefs(f.Vars, loc);
            Name(potion, loc, "potions", f.ClassName);
            potion.TemplateFr = loc.Fr("potions", GameIds.Entry(id) + ".description");
            potion.TemplateEn = loc.En("potions", GameIds.Entry(id) + ".description");
            RenderSimple(potion, renderer, notes, f.Issues, extra: null);
            doc.Potions.Add(potion);
        }

        // ---- Enchantements ----
        foreach (var f in ScanDir(Path.Combine(modelsDir, "Enchantments"), "EnchantmentModel"))
        {
            var id = GameIds.Make(GameIds.Enchantment, f.ClassName);
            var ench = new KbEnchantment { Id = id, ClassName = f.ClassName, Vars = f.Vars, ShowAmount = f.ShowAmount };
            ResolveRefs(f.Vars, loc);
            Name(ench, loc, "enchantments", f.ClassName);
            var entry = GameIds.Entry(id);
            ench.TemplateFr = loc.Fr("enchantments", entry + ".description");
            ench.TemplateEn = loc.En("enchantments", entry + ".description");
            if (f.HasExtraCardText)
            {
                ench.ExtraTemplateFr = loc.Fr("enchantments", entry + ".extraCardText");
                ench.ExtraTemplateEn = loc.En("enchantments", entry + ".extraCardText");
            }
            // Le montant (Amount) est propre à chaque carte enchantée (save : enchantment.amount) : non résolu ici.
            RenderSimple(ench, renderer, notes, f.Issues, extra: null,
                ignoreUnresolved: ["Amount", .. ench.Vars.Where(v => v.Kind == "amount").Select(v => v.Name)]);
            var extraTpl = ench.ExtraTemplateFr ?? ench.ExtraTemplateEn;
            if (extraTpl != null)
            {
                var r = renderer.Render(extraTpl, StaticVars(ench.Vars, upgraded: false, extra: null),
                    ench.ExtraTemplateFr != null ? "fr" : "en");
                ench.ExtraCardText = r.Text;
            }
            doc.Enchantments.Add(ench);
        }

        // ---- Rencontres et personnages (noms) ----
        foreach (var f in ScanDir(Path.Combine(modelsDir, "Encounters"), "EncounterModel"))
        {
            var e = new KbNamed { Id = GameIds.Make(GameIds.Encounter, f.ClassName), ClassName = f.ClassName };
            Name(e, loc, "encounters", f.ClassName);
            doc.Encounters.Add(e);
        }
        foreach (var f in ScanDir(Path.Combine(modelsDir, "Characters"), "CharacterModel"))
        {
            var c = new KbNamed { Id = GameIds.Make(GameIds.Character, f.ClassName), ClassName = f.ClassName };
            Name(c, loc, "characters", f.ClassName);
            doc.Characters.Add(c);
        }

        ComputeCoverage(doc);
        return doc;

        // -- rendu d'une carte (description canonique, hors combat, comme CardModel.GetDescriptionForPile) --
        string RenderCard(KbCard card, bool upgraded, GameTextRenderer r, KbDocument d, SortedSet<string> unresolved, List<string> n)
        {
            var useFr = card.TemplateFr != null || card.TemplateEn == null;
            var lines = new List<string>();
            var tpl = card.TemplateFr ?? card.TemplateEn;
            if (tpl != null)
            {
                var ctx = new Dictionary<string, TextValue>(StringComparer.Ordinal)
                {
                    ["IfUpgraded"] = TextValue.IfUpgraded(upgraded),
                    ["OnTable"] = TextValue.Of(false),
                    ["InCombat"] = TextValue.Of(false),
                    ["IsTargeting"] = TextValue.Of(false),
                    ["TargetType"] = TextValue.Of(card.Target),
                    ["GainsBlock"] = TextValue.Of(card.GainsBlock),
                    ["IsOstyAlive"] = TextValue.Of(false),
                    ["energyPrefix"] = TextValue.Of(card.Owner.ToLowerInvariant()),
                    ["singleStarIcon"] = TextValue.Of(r.Options.StarGlyph),
                };
                var res = r.Render(tpl, StaticVars(card.Vars, upgraded, ctx), card.TemplateFr != null ? "fr" : "en");
                foreach (var u in res.UnresolvedVars) unresolved.Add(u);
                foreach (var i in res.Issues) n.Add($"{card.Id} : {i}");
                if (res.Text.Length > 0) lines.Add(res.Text);
            }
            var kws = upgraded && card.KeywordsUpgraded != null ? card.KeywordsUpgraded : card.Keywords;
            string Kw(string k)
            {
                var label = d.Keywords.GetValueOrDefault(k);
                var title = useFr ? label?.Fr ?? label?.En : label?.En ?? label?.Fr;
                return (title ?? k) + (useFr ? periodFr : periodEn);
            }
            foreach (var k in KeywordsBefore) if (kws.Contains(k)) lines.Insert(0, Kw(k));
            foreach (var k in KeywordsAfter) if (kws.Contains(k)) lines.Add(Kw(k));
            return GameTextRenderer.StripBbCode(string.Join('\n', lines.Where(l => l.Length > 0)));
        }
    }

    /// <summary>Valeurs statiques des variables (valeur de base, ou améliorée), plus le contexte fourni.</summary>
    public static Dictionary<string, TextValue> StaticVars(IEnumerable<KbVar> vars, bool upgraded, IReadOnlyDictionary<string, TextValue>? extra)
    {
        var list = vars.ToList();
        var d = new Dictionary<string, TextValue>(StringComparer.Ordinal);
        decimal Val(KbVar v) => v.Base + (upgraded ? v.Upgrade : 0m);
        var calcBase = list.FirstOrDefault(v => v.Name == "CalculationBase");
        foreach (var v in list)
        {
            switch (v.Kind)
            {
                case "number": d[v.Name] = TextValue.Of(Val(v)); break;
                case "bool": d[v.Name] = TextValue.Of(v.Base != 0); break;
                case "string": if (v.Str != null) d[v.Name] = TextValue.Of(v.Str); break;
                // Hors combat, CalculatedVar.Calculate(null) = CalculationBase + CalculationExtra × 0.
                case "calculated": if (calcBase != null) d[v.Name] = TextValue.Of(Val(calcBase)); break;
                // Recopiée du montant de l'enchantement : connue seulement avec la carte enchantée.
                case "amount": if (extra != null && extra.TryGetValue("Amount", out var amount)) d[v.Name] = amount; break;
            }
        }
        if (extra != null) foreach (var (k, v) in extra) d.TryAdd(k, v);
        return d;
    }

    private static void RenderSimple(KbEntry e, GameTextRenderer r, List<string> notes, List<string> issues,
        IReadOnlyDictionary<string, TextValue>? extra, string[]? ignoreUnresolved = null)
    {
        var tpl = e.TemplateFr ?? e.TemplateEn;
        e.DescriptionSource = e.TemplateFr != null ? "fra" : e.TemplateEn != null ? "eng" : "none";
        foreach (var i in issues) notes.Add($"{e.Id} : {i}");
        if (tpl == null) { e.Description = ""; return; }
        var ctx = new Dictionary<string, TextValue>(StringComparer.Ordinal)
        {
            ["energyPrefix"] = TextValue.Of("colorless"),
            ["singleStarIcon"] = TextValue.Of(r.Options.StarGlyph),
        };
        if (extra != null) foreach (var (k, v) in extra) ctx[k] = v;
        var res = r.Render(tpl, StaticVars(e.Vars, upgraded: false, ctx), e.TemplateFr != null ? "fr" : "en");
        e.Description = res.Text;
        var unresolved = res.UnresolvedVars.Where(u => ignoreUnresolved == null || !ignoreUnresolved.Contains(u)).ToList();
        if (unresolved.Count > 0) e.UnresolvedVars = unresolved;
        foreach (var i in res.Issues) notes.Add($"{e.Id} : {i}");
    }

    private static readonly Dictionary<string, string> RefFiles = new(StringComparer.Ordinal)
    {
        [GameIds.Card] = "cards", [GameIds.Relic] = "relics", [GameIds.Enchantment] = "enchantments", [GameIds.Potion] = "potions",
    };

    /// <summary>StringVar = titre d'un autre modèle (ModelDb.Enchantment&lt;X&gt;().Title) : nom FR, sinon EN.</summary>
    private static void ResolveRefs(List<KbVar> vars, Loc loc)
    {
        foreach (var v in vars.Where(v => v.Ref != null))
        {
            var cat = v.Ref![..v.Ref!.IndexOf('.')];
            var key = GameIds.Entry(v.Ref) + ".title";
            var file = RefFiles.GetValueOrDefault(cat);
            var name = file == null ? null : loc.Fr(file, key) ?? loc.En(file, key);
            v.Str = name == null ? null : GameTextRenderer.StripBbCode(name);
        }
    }

    private static void Name(KbNamed e, Loc loc, string file, string className)
    {
        var key = GameIds.Slugify(className) + ".title";
        e.NameFr = loc.Fr(file, key);
        e.NameEn = loc.En(file, key);
        // Les titres peuvent contenir du BBCode décoratif : on le retire.
        if (e.NameFr != null) e.NameFr = GameTextRenderer.StripBbCode(e.NameFr);
        if (e.NameEn != null) e.NameEn = GameTextRenderer.StripBbCode(e.NameEn);
        (e.Name, e.NameSource) = e.NameFr != null ? (e.NameFr, "fra") : e.NameEn != null ? (e.NameEn, "eng") : (GameIds.Entry(e.Id), "id");
    }

    private static IEnumerable<ModelFacts> ScanDir(string dir, string expectedBase)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            var f = ModelSourceScanner.ScanFile(file, expectedBase);
            if (f != null) yield return f;
        }
    }

    // ---------- Localisation ----------
    private sealed class Loc(Dictionary<string, Dictionary<string, string>> fr, Dictionary<string, Dictionary<string, string>> en)
    {
        public string? Fr(string file, string key) => Get(fr, file, key);
        public string? En(string file, string key) => Get(en, file, key);
        private static string? Get(Dictionary<string, Dictionary<string, string>> d, string file, string key) =>
            d.TryGetValue(file, out var t) && t.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    }

    private static Dictionary<string, Dictionary<string, string>> LoadLoc(string locDir, string lang, KbDocument doc)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var f in LocFiles)
        {
            var path = Path.Combine(locDir, lang, f + ".json");
            if (!File.Exists(path)) { doc.Coverage.Notes.Add($"fichier de localisation absent : {lang}/{f}.json"); continue; }
            var bytes = File.ReadAllBytes(path);
            doc.Source.Hashes[$"localization/{lang}/{f}.json"] = Hash(bytes);
            var table = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes) ?? [];
            result[f] = table.Where(kv => kv.Value.ValueKind == JsonValueKind.String)
                             .ToDictionary(kv => kv.Key, kv => kv.Value.GetString()!, StringComparer.Ordinal);
        }
        return result;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();

    // ---------- Catalogue ----------
    private sealed record CatalogCard(int Cost, string Type, string Rarity, string Target);

    private static Dictionary<string, CatalogCard> LoadCatalog(string catalogDir, KbDocument doc)
    {
        var map = new Dictionary<string, CatalogCard>(StringComparer.Ordinal);
        var path = Path.Combine(catalogDir, "cards.json");
        if (!File.Exists(path)) { doc.Coverage.Notes.Add("catalogue absent : cards.json (repli sur la source)"); return map; }
        var bytes = File.ReadAllBytes(path);
        doc.Source.Hashes["catalog/cards.json"] = Hash(bytes);
        using var json = JsonDocument.Parse(bytes);
        foreach (var e in json.RootElement.EnumerateArray())
        {
            var cls = e.GetProperty("class").GetString()!;
            map[cls] = new CatalogCard(
                e.TryGetProperty("cost", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0,
                e.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                e.TryGetProperty("rarity", out var r) ? r.GetString() ?? "" : "",
                e.TryGetProperty("target", out var g) ? g.GetString() ?? "" : "");
        }
        return map;
    }

    // ---------- Pools (propriétaires) ----------
    private sealed class Pools
    {
        public Dictionary<(string Kind, string Class), List<string>> Map { get; } = [];
        public string? Owner(string kind, string cls) =>
            Map.TryGetValue((kind, cls), out var l)
                ? l.OrderBy(p => Array.IndexOf(GenericPools, p)).ThenBy(p => p, StringComparer.Ordinal).First()
                : null;
    }

    private static Pools LoadPools(string modelsDir, List<string> notes)
    {
        var pools = new Pools();
        var srcRoot = Path.GetDirectoryName(modelsDir)!; // …/src/Core
        foreach (var (sub, suffix) in new[] { ("CardPools", "CardPool"), ("RelicPools", "RelicPool"), ("PotionPools", "PotionPool") })
        {
            var dir = Path.Combine(modelsDir, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs").Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
                var owner = name[..^suffix.Length];
                var src = File.ReadAllText(file);
                var entries = PoolEntry().Matches(src).Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
                // Pools de personnage renvoyant la liste d'une Epoch (ex. IroncladPotionPool → Ironclad4Epoch.Potions).
                foreach (Match ind in PoolIndirection().Matches(src))
                {
                    var target = FindClassFile(srcRoot, ind.Groups[1].Value);
                    if (target == null) { notes.Add($"pool {name} : classe {ind.Groups[1].Value} introuvable"); continue; }
                    entries.AddRange(PoolEntry().Matches(File.ReadAllText(target)).Select(m => (m.Groups[1].Value, m.Groups[2].Value)));
                }
                foreach (var (kind, cls) in entries)
                {
                    if (!pools.Map.TryGetValue((kind, cls), out var l)) pools.Map[(kind, cls)] = l = [];
                    if (!l.Contains(owner)) l.Add(owner);
                }
            }
        }
        return pools;
    }

    private static readonly Dictionary<string, string?> ClassFileCache = [];

    private static string? FindClassFile(string srcRoot, string cls)
    {
        lock (ClassFileCache)
        {
            if (ClassFileCache.TryGetValue(srcRoot + "|" + cls, out var hit)) return hit;
            var found = Directory.EnumerateFiles(srcRoot, cls + ".cs", SearchOption.AllDirectories).FirstOrDefault();
            ClassFileCache[srcRoot + "|" + cls] = found;
            return found;
        }
    }

    // ---------- Couverture ----------
    private static void ComputeCoverage(KbDocument doc)
    {
        void Cat(string name, IEnumerable<KbNamed> items)
        {
            var list = items.ToList();
            var c = new KbCategoryCoverage
            {
                Total = list.Count,
                NameFr = list.Count(i => i.NameSource == "fra"),
                NameEnFallback = list.Count(i => i.NameSource == "eng"),
                NameIdFallback = list.Count(i => i.NameSource == "id"),
            };
            var entries = list.OfType<KbEntry>().ToList();
            if (entries.Count > 0)
            {
                c.DescriptionFr = entries.Count(e => e.DescriptionSource == "fra");
                c.DescriptionEnFallback = entries.Count(e => e.DescriptionSource == "eng");
                c.DescriptionNone = entries.Count(e => e.DescriptionSource == "none");
                c.WithUnresolvedVars = entries.Count(e => e.UnresolvedVars is { Count: > 0 });
            }
            doc.Coverage.Categories[name] = c;
            doc.Coverage.UnresolvedFr[name] = list.Where(i => i.NameSource != "fra").Select(i => $"{i.Id} ({i.NameSource})").ToList();
        }
        Cat("cards", doc.Cards);
        Cat("relics", doc.Relics);
        Cat("enchantments", doc.Enchantments);
        Cat("potions", doc.Potions);
        Cat("encounters", doc.Encounters);
        Cat("characters", doc.Characters);
    }
}
