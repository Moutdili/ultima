using System.Globalization;
using System.Text.RegularExpressions;

namespace Ultima.Knowledge.Build;

/// <summary>Faits extraits d'un fichier modèle C# du jeu (Models/Cards/X.cs…), lecture seule.</summary>
public sealed class ModelFacts
{
    public string ClassName { get; init; } = "";
    public string BaseClass { get; init; } = "";
    public string File { get; init; } = "";
    public List<KbVar> Vars { get; } = [];
    public List<string> Keywords { get; } = [];
    public List<string> KeywordsAddedOnUpgrade { get; } = [];
    public List<string> KeywordsRemovedOnUpgrade { get; } = [];
    public int? CostUpgradeDelta { get; set; }
    public int? Cost { get; set; }
    public string? CardType { get; set; }
    public string? CardRarity { get; set; }
    public string? Target { get; set; }
    public bool CostX { get; set; }
    public bool GainsBlock { get; set; }
    public int? MaxUpgradeLevel { get; set; }
    public string? Rarity { get; set; }
    public string? Usage { get; set; }
    public bool ShowAmount { get; set; }
    public bool HasExtraCardText { get; set; }
    public List<string> Issues { get; } = [];
}

/// <summary>
/// Extraction par motifs du code source shippé par MegaCrit (<c>game/pck/src/Core/Models</c>, v0.107.1).
/// Ce n'est pas un compilateur : tout ce qui n'est pas reconnu est signalé dans <see cref="ModelFacts.Issues"/>.
/// </summary>
public static partial class ModelSourceScanner
{
    // Noms par défaut des DynamicVar typés (Localization/DynamicVars/*.cs : defaultName).
    private static readonly Dictionary<string, string> DefaultNames = new(StringComparer.Ordinal)
    {
        ["BlockVar"] = "Block", ["CalculatedBlockVar"] = "CalculatedBlock", ["CalculatedDamageVar"] = "CalculatedDamage",
        ["CalculationBaseVar"] = "CalculationBase", ["CalculationExtraVar"] = "CalculationExtra", ["CardsVar"] = "Cards",
        ["DamageVar"] = "Damage", ["EnergyVar"] = "Energy", ["ExtraDamageVar"] = "ExtraDamage", ["ForgeVar"] = "Forge",
        ["GoldVar"] = "Gold", ["HealVar"] = "Heal", ["HpLossVar"] = "HpLoss", ["MaxHpVar"] = "MaxHp",
        ["OstyDamageVar"] = "OstyDamage", ["RepeatVar"] = "Repeat", ["StarsVar"] = "Stars", ["SummonVar"] = "Summon",
        ["IfUpgradedVar"] = "IfUpgraded",
    };

    // Accesseurs de DynamicVarSet (Localization/DynamicVars/DynamicVarSet.cs).
    private static readonly Dictionary<string, string> SetAccessors = new(StringComparer.Ordinal)
    {
        ["Block"] = "Block", ["CalculatedBlock"] = "CalculatedBlock", ["CalculatedDamage"] = "CalculatedDamage",
        ["CalculationBase"] = "CalculationBase", ["CalculationExtra"] = "CalculationExtra", ["Cards"] = "Cards",
        ["Damage"] = "Damage", ["Dexterity"] = "DexterityPower", ["Doom"] = "DoomPower", ["Energy"] = "Energy",
        ["ExtraDamage"] = "ExtraDamage", ["Forge"] = "Forge", ["Gold"] = "Gold", ["Heal"] = "Heal", ["HpLoss"] = "HpLoss",
        ["MaxHp"] = "MaxHp", ["OstyDamage"] = "OstyDamage", ["Poison"] = "PoisonPower", ["Repeat"] = "Repeat",
        ["Stars"] = "Stars", ["Strength"] = "StrengthPower", ["Summon"] = "Summon", ["Vulnerable"] = "VulnerablePower",
        ["Weak"] = "WeakPower",
    };

    [GeneratedRegex(@"public\s+(?:sealed\s+)?(abstract\s+)?(?:partial\s+)?class\s+(\w+)\s*:\s*(\w+)")]
    private static partial Regex ClassDecl();

    [GeneratedRegex(@"const\s+(?:string|int|decimal)\s+(\w+)\s*=\s*([^;]+);")]
    private static partial Regex ConstDecl();

    [GeneratedRegex(@"private\s+(?:static\s+)?(?:readonly\s+)?(?:int|decimal)\s+(_[a-z]\w*)\s*=\s*(-?[0-9.]+m?)\s*;")]
    private static partial Regex FieldInit();

    [GeneratedRegex(@"^ModelDb\.(Card|Relic|Enchantment|Potion)<(\w+)>\(\)\.Title")]
    private static partial Regex ModelTitleRef();

    [GeneratedRegex(@"new\s+(\w*Var)(?:<(\w+)>)?\s*\(")]
    private static partial Regex VarCtor();

    [GeneratedRegex(@"\n\t(?:public|protected|private|internal)\b|\n}")]
    private static partial Regex NextMember();

    [GeneratedRegex(@"CardKeyword\.(\w+)")]
    private static partial Regex KeywordRef();

    [GeneratedRegex(@"DynamicVars(?:\[""(\w+)""\]|\[(\w+)\]|\.(\w+))\.UpgradeValueBy\(\s*(-?[0-9.]+)m?\s*\)")]
    private static partial Regex UpgradeValue();

    [GeneratedRegex(@"DynamicVars(?:\[""(\w+)""\]|\.(\w+))\.BaseValue\s*=\s*base\.Amount\s*;")]
    private static partial Regex AmountBinding();

    [GeneratedRegex(@"EnergyCost\.UpgradeBy\(\s*(-?\d+)\s*\)")]
    private static partial Regex UpgradeCost();

    [GeneratedRegex(@"(Add|Remove)Keyword\(\s*CardKeyword\.(\w+)\s*\)")]
    private static partial Regex KeywordChange();

    [GeneratedRegex(@":\s*base\(\s*(-?\d+)\s*,\s*CardType\.(\w+)\s*,\s*CardRarity\.(\w+)\s*,\s*TargetType\.(\w+)\s*\)")]
    private static partial Regex CardCtor();

    [GeneratedRegex(@"override\s+(?:Relic|Potion)Rarity\s+Rarity\s*=>\s*\w+Rarity\.(\w+)")]
    private static partial Regex RarityDecl();

    [GeneratedRegex(@"override\s+PotionUsage\s+Usage\s*=>\s*PotionUsage\.(\w+)")]
    private static partial Regex UsageDecl();

    [GeneratedRegex(@"override\s+int\s+MaxUpgradeLevel\s*=>\s*(\d+)")]
    private static partial Regex MaxUpgradeDecl();

    public static ModelFacts? ScanFile(string path, string expectedBase)
    {
        var src = File.ReadAllText(path);
        return Scan(src, path, expectedBase);
    }

    public static ModelFacts? Scan(string src, string path, string expectedBase)
    {
        var m = ClassDecl().Match(src);
        if (!m.Success || m.Groups[1].Success || m.Groups[3].Value != expectedBase) return null;
        var facts = new ModelFacts { ClassName = m.Groups[2].Value, BaseClass = m.Groups[3].Value, File = path };

        var consts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match c in ConstDecl().Matches(src)) consts[c.Groups[1].Value] = c.Groups[2].Value.Trim();
        // Propriétés d'instance à champ de soutien initialisé (ex. CombatsLeft → _combatsLeft = 1) :
        // valeur canonique d'un modèle neuf.
        foreach (Match f in FieldInit().Matches(src))
        {
            var field = f.Groups[1].Value;
            var prop = char.ToUpperInvariant(field[1]) + field[2..];
            consts.TryAdd(prop, f.Groups[2].Value.Trim());
            consts.TryAdd(field, f.Groups[2].Value.Trim());
        }

        var varsRegion = Region(src, "CanonicalVars");
        if (varsRegion != null) ParseVars(varsRegion, consts, facts);

        var kwRegion = Region(src, "CanonicalKeywords");
        if (kwRegion != null)
            foreach (Match k in KeywordRef().Matches(kwRegion))
                if (!facts.Keywords.Contains(k.Groups[1].Value)) facts.Keywords.Add(k.Groups[1].Value);

        var upRegion = Region(src, "void OnUpgrade");
        if (upRegion != null)
        {
            foreach (Match u in UpgradeValue().Matches(upRegion))
            {
                string? name = u.Groups[1].Success ? u.Groups[1].Value
                    : u.Groups[2].Success ? Resolve(u.Groups[2].Value, consts)?.Trim('"')
                    : SetAccessors.GetValueOrDefault(u.Groups[3].Value);
                var v = name == null ? null : facts.Vars.FirstOrDefault(x => x.Name == name);
                if (v == null) { facts.Issues.Add($"amélioration d'une variable inconnue '{u.Value}'"); continue; }
                v.Upgrade += decimal.Parse(u.Groups[4].Value, CultureInfo.InvariantCulture);
            }
            var cu = UpgradeCost().Match(upRegion);
            if (cu.Success) facts.CostUpgradeDelta = int.Parse(cu.Groups[1].Value, CultureInfo.InvariantCulture);
            foreach (Match k in KeywordChange().Matches(upRegion))
                (k.Groups[1].Value == "Add" ? facts.KeywordsAddedOnUpgrade : facts.KeywordsRemovedOnUpgrade).Add(k.Groups[2].Value);
        }

        // Variable recopiée du montant de l'enchantement (Adroit : DynamicVars.Block.BaseValue = base.Amount).
        foreach (Match b in AmountBinding().Matches(src))
        {
            var name = b.Groups[1].Success ? b.Groups[1].Value : SetAccessors.GetValueOrDefault(b.Groups[2].Value);
            var v = facts.Vars.FirstOrDefault(x => x.Name == name);
            if (v != null) v.Kind = "amount";
        }

        var ctor = CardCtor().Match(src);
        if (ctor.Success)
        {
            facts.Cost = int.Parse(ctor.Groups[1].Value, CultureInfo.InvariantCulture);
            facts.CardType = ctor.Groups[2].Value;
            facts.CardRarity = ctor.Groups[3].Value;
            facts.Target = ctor.Groups[4].Value;
        }
        facts.CostX = Regex.IsMatch(src, @"override\s+bool\s+HasEnergyCostX\s*=>\s*true");
        facts.GainsBlock = Regex.IsMatch(src, @"override\s+bool\s+GainsBlock\s*=>\s*true");
        facts.ShowAmount = Regex.IsMatch(src, @"override\s+bool\s+ShowAmount\s*=>\s*true");
        facts.HasExtraCardText = Regex.IsMatch(src, @"override\s+bool\s+HasExtraCardText\s*=>\s*true");
        var mu = MaxUpgradeDecl().Match(src);
        if (mu.Success) facts.MaxUpgradeLevel = int.Parse(mu.Groups[1].Value, CultureInfo.InvariantCulture);
        var r = RarityDecl().Match(src);
        if (r.Success) facts.Rarity = r.Groups[1].Value;
        var us = UsageDecl().Match(src);
        if (us.Success) facts.Usage = us.Groups[1].Value;
        return facts;
    }

    /// <summary>Texte du membre <paramref name="marker"/> jusqu'au membre suivant (indentation d'une tabulation).</summary>
    private static string? Region(string src, string marker)
    {
        int idx = src.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var next = NextMember().Match(src, idx + marker.Length);
        return next.Success ? src[idx..next.Index] : src[idx..];
    }

    private static void ParseVars(string region, Dictionary<string, string> consts, ModelFacts facts)
    {
        foreach (Match m in VarCtor().Matches(region))
        {
            string type = m.Groups[1].Value;
            string? generic = m.Groups[2].Success ? m.Groups[2].Value : null;
            var args = ReadArgs(region, m.Index + m.Length);
            var v = BuildVar(type, generic, args, consts, facts);
            if (v == null) continue;
            if (facts.Vars.Any(x => x.Name == v.Name)) { facts.Issues.Add($"variable en double '{v.Name}'"); continue; }
            facts.Vars.Add(v);
        }
    }

    private static KbVar? BuildVar(string type, string? generic, List<string> args, Dictionary<string, string> consts, ModelFacts facts)
    {
        string? first = args.Count > 0 ? Resolve(args[0], consts) : null;
        bool firstIsName = first != null && first.StartsWith('"');
        string Unq(string s) => s.Trim('"');

        switch (type)
        {
            case "DynamicVar":
            case "IntVar":
                if (!firstIsName) { facts.Issues.Add($"{type} sans nom littéral ({string.Join(",", args)})"); return null; }
                return Num(Unq(first!), args.Count > 1 ? args[1] : null);
            case "BoolVar":
                if (!firstIsName) { facts.Issues.Add($"BoolVar sans nom littéral"); return null; }
                {
                    var b = args.Count > 1 ? Resolve(args[1], consts) : "false";
                    return new KbVar { Name = Unq(first!), Kind = "bool", Base = b == "true" ? 1 : 0 };
                }
            case "StringVar":
                if (!firstIsName) { facts.Issues.Add($"StringVar sans nom littéral"); return null; }
                {
                    var s = args.Count > 1 ? Resolve(args[1], consts) : "\"\"";
                    var title = s == null ? Match.Empty : ModelTitleRef().Match(s);
                    if (title.Success)
                        return new KbVar { Name = Unq(first!), Kind = "string", Ref = title.Groups[1].Value.ToUpperInvariant() + "." + GameIds.Slugify(title.Groups[2].Value) };
                    // Chaîne fixée à l'exécution (carte de départ, personnage…) : vide pour un modèle canonique,
                    // ce qui sélectionne la variante générique des {X.StringValue:cond:…|…}.
                    return new KbVar { Name = Unq(first!), Kind = "string", Str = s != null && s.StartsWith('"') ? Unq(s) : "" };
                }
            case "CalculatedVar":
                if (!firstIsName) { facts.Issues.Add($"CalculatedVar sans nom littéral"); return null; }
                return new KbVar { Name = Unq(first!), Kind = "calculated" };
            case "CalculatedDamageVar":
            case "CalculatedBlockVar":
                return new KbVar { Name = DefaultNames[type], Kind = "calculated" };
            case "IfUpgradedVar":
                return null; // fourni par le contexte de rendu
            case "PowerVar":
                if (generic == null) return null;
                return firstIsName ? Num(Unq(first!), args.Count > 1 ? args[1] : null) : Num(generic, args.Count > 0 ? args[0] : null);
            default:
                if (DefaultNames.TryGetValue(type, out var def))
                    return firstIsName ? Num(Unq(first!), args.Count > 1 ? args[1] : null) : Num(def, args.Count > 0 ? args[0] : null);
                facts.Issues.Add($"type de variable inconnu '{type}'");
                return null;
        }

        KbVar Num(string name, string? raw)
        {
            var resolved = raw == null ? null : Resolve(raw, consts);
            if (resolved != null && TryNumber(resolved, out var n)) return new KbVar { Name = name, Base = n };
            facts.Issues.Add($"valeur non littérale pour '{name}' ({raw})");
            return new KbVar { Name = name, Kind = "unknown" };
        }
    }

    private static string? Resolve(string raw, Dictionary<string, string> consts)
    {
        var t = raw.Trim();
        for (int depth = 0; depth < 4 && consts.TryGetValue(t, out var c); depth++) t = c;
        return t;
    }

    private static bool TryNumber(string s, out decimal n)
    {
        var t = s.Trim().TrimEnd('m', 'M');
        if (t.StartsWith("(decimal)", StringComparison.Ordinal)) t = t["(decimal)".Length..].Trim();
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out n);
    }

    /// <summary>Lit les arguments de premier niveau d'un appel (parenthèses, chaînes et lambdas équilibrées).</summary>
    private static List<string> ReadArgs(string s, int i)
    {
        var args = new List<string>();
        int depth = 0, start = i;
        bool inStr = false;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
            if (c == '"') { inStr = true; continue; }
            if (c is '(' or '[' or '{') { depth++; continue; }
            if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    var last = s[start..i].Trim();
                    if (last.Length > 0) args.Add(last);
                    return args;
                }
                depth--;
                continue;
            }
            if (c == ',' && depth == 0) { args.Add(s[start..i].Trim()); start = i + 1; }
        }
        return args;
    }
}
