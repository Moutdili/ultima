using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ultima.Knowledge.Text;

/// <summary>Glyphes utilisés à la place des icônes <c>[img]</c> du jeu.</summary>
public sealed record RenderOptions(string EnergyGlyph = "⚡", string StarGlyph = "★", string UnknownValue = "X");

public sealed record RenderResult(string Text, IReadOnlyList<string> UnresolvedVars, IReadOnlyList<string> Issues);

/// <summary>
/// Rendu des textes du jeu : sous-ensemble de SmartFormat v3 tel que configuré par le jeu
/// (<c>LocManager.LoadLocFormatters</c>, <c>Localization/LocManager.cs:228</c>) puis suppression du BBCode.
/// Formateurs : implicite (plural puis conditional), <c>plural</c>/<c>p</c>, <c>cond</c>, <c>choose</c>,
/// <c>show</c>, <c>diff</c>, <c>inverseDiff</c>, <c>energyIcons</c>, <c>starIcons</c>, <c>abs</c>,
/// <c>percentMore</c>, <c>percentLess</c>. Une variable inconnue est rendue par
/// <see cref="RenderOptions.UnknownValue"/> et listée dans <see cref="RenderResult.UnresolvedVars"/> :
/// jamais d'accolade brute en sortie.
/// </summary>
public sealed partial class GameTextRenderer
{
    private readonly RenderOptions _opt;

    public GameTextRenderer(RenderOptions? options = null) => _opt = options ?? new RenderOptions();

    public RenderOptions Options => _opt;

    // ---------- AST ----------
    private abstract record Node;
    private sealed record TextNode(string Text) : Node;
    private sealed record PlaceholderNode(string Selector, string? Formatter, string? Options, List<Node>? Format) : Node;

    [GeneratedRegex(@"^  (?:   ([&/]?)   ([<>=!]=?)   ([0-9.-]+)   )+   \?", RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex ComplexCondition();

    [GeneratedRegex(@"\[img\][^\[]*\[/img\]", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTag();

    [GeneratedRegex(@"\[/?[a-zA-Z_][a-zA-Z0-9_]*(?:[= ][^\[\]]*)?\]")]
    private static partial Regex BbTag();

    private static readonly HashSet<string> KnownFormatters = new(StringComparer.Ordinal)
    {
        "plural", "p", "cond", "conditional", "choose", "show", "diff", "inverseDiff", "energyIcons",
        "starIcons", "abs", "percentMore", "percentLess",
    };

    /// <summary>Supprime le BBCode du jeu (<c>[gold]</c>, <c>[blue]</c>, <c>[sine]</c>, <c>[img]…[/img]</c>…).</summary>
    public static string StripBbCode(string text)
    {
        var t = ImgTag().Replace(text, "");
        return BbTag().Replace(t, "");
    }

    public RenderResult Render(string template, IReadOnlyDictionary<string, TextValue> vars, string lang)
    {
        var ctx = new Ctx(vars, lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase));
        int i = 0;
        var nodes = ParseFormat(template, ref i, nested: false, ctx);
        var sb = new StringBuilder();
        Emit(nodes, null, sb, ctx);
        var text = StripBbCode(sb.ToString());
        // Filet de sécurité : jamais d'accolade/crochet de balisage en sortie.
        if (text.IndexOfAny(['{', '}']) >= 0)
        {
            ctx.Issues.Add("accolade résiduelle supprimée");
            text = text.Replace("{", "").Replace("}", "");
        }
        // Une option vide ({IfUpgraded:show:…|}) laisse une double espace dans le texte du jeu : on la resserre.
        text = MultiSpace().Replace(text, " ");
        text = SpaceBeforeNewline().Replace(text, "\n");
        return new RenderResult(text.Trim(), ctx.Unresolved.ToList(), ctx.Issues);
    }

    [GeneratedRegex(@" {2,}")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@" +\n")]
    private static partial Regex SpaceBeforeNewline();

    private sealed class Ctx(IReadOnlyDictionary<string, TextValue> vars, bool french)
    {
        public IReadOnlyDictionary<string, TextValue> Vars { get; } = vars;
        public bool French { get; } = french;
        public SortedSet<string> Unresolved { get; } = new(StringComparer.Ordinal);
        public List<string> Issues { get; } = [];

        public bool TryGet(string name, out TextValue v)
        {
            if (Vars.TryGetValue(name, out v)) return true;
            foreach (var kv in Vars)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) { v = kv.Value; return true; }
            // Sélecteur pointé du jeu : {StarterCard.StringValue} → valeur chaîne de la variable StarterCard.
            var dot = name.IndexOf('.');
            if (dot > 0 && TryGet(name[..dot], out var head))
            {
                var member = name[(dot + 1)..];
                if (member == "StringValue") { v = TextValue.Of(head.Kind == TextValueKind.String ? head.Str ?? "" : head.ToString()); return true; }
                if (member is "BaseValue" or "IntValue" or "PreviewValue") { v = head; return true; }
            }
            return false;
        }
    }

    // ---------- Parsing ----------
    private static List<Node> ParseFormat(string s, ref int i, bool nested, Ctx ctx)
    {
        var nodes = new List<Node>();
        var text = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '{')
            {
                if (text.Length > 0) { nodes.Add(new TextNode(text.ToString())); text.Clear(); }
                i++;
                nodes.Add(ParsePlaceholder(s, ref i, ctx));
                continue;
            }
            if (c == '}')
            {
                if (nested) { i++; break; }
                ctx.Issues.Add("accolade fermante orpheline");
                i++;
                continue;
            }
            text.Append(c);
            i++;
        }
        if (text.Length > 0) nodes.Add(new TextNode(text.ToString()));
        return nodes;
    }

    private static Node ParsePlaceholder(string s, ref int i, Ctx ctx)
    {
        int start = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.')) i++;
        string selector = s[start..i];
        if (i >= s.Length)
        {
            ctx.Issues.Add("placeholder non fermé");
            return new PlaceholderNode(selector, null, null, null);
        }
        if (s[i] == '}') { i++; return new PlaceholderNode(selector, null, null, null); }
        if (s[i] != ':')
        {
            // Sélecteur invalide : on lit jusqu'à la fermeture et on traite le reste comme format.
            ctx.Issues.Add($"sélecteur invalide '{selector}{s[i]}'");
            var fmt = ParseFormat(s, ref i, nested: true, ctx);
            return new PlaceholderNode(selector, null, null, fmt);
        }
        i++; // ':'
        int afterColon = i;
        int nameStart = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
        string name = s[nameStart..i];
        if (name.Length > 0 && i < s.Length && (s[i] == '(' || s[i] == ':'))
        {
            string? options = null;
            if (s[i] == '(')
            {
                int close = s.IndexOf(')', i);
                if (close < 0) { ctx.Issues.Add("options non fermées"); close = s.Length - 1; }
                options = s[(i + 1)..close];
                i = close + 1;
            }
            if (i < s.Length && s[i] == ':')
            {
                i++;
                var fmt = ParseFormat(s, ref i, nested: true, ctx);
                return new PlaceholderNode(selector, name, options, fmt);
            }
            if (i < s.Length && s[i] == '}') { i++; return new PlaceholderNode(selector, name, options, null); }
        }
        // Pas de nom de formateur : tout ce qui suit ':' est le format (formateur implicite).
        i = afterColon;
        var implicitFmt = ParseFormat(s, ref i, nested: true, ctx);
        return new PlaceholderNode(selector, null, null, implicitFmt);
    }

    /// <summary>Découpe un format sur '|' au niveau supérieur (pas dans les placeholders imbriqués).</summary>
    private static List<List<Node>> Split(List<Node> format)
    {
        var parts = new List<List<Node>> { new() };
        foreach (var n in format)
        {
            if (n is TextNode t && t.Text.Contains('|'))
            {
                var pieces = t.Text.Split('|');
                for (int k = 0; k < pieces.Length; k++)
                {
                    if (k > 0) parts.Add([]);
                    if (pieces[k].Length > 0) parts[^1].Add(new TextNode(pieces[k]));
                }
            }
            else parts[^1].Add(n);
        }
        return parts;
    }

    // ---------- Evaluation ----------
    private void Emit(List<Node> nodes, TextValue? current, StringBuilder sb, Ctx ctx)
    {
        foreach (var n in nodes)
        {
            switch (n)
            {
                case TextNode t: sb.Append(t.Text); break;
                case PlaceholderNode p: EmitPlaceholder(p, current, sb, ctx); break;
            }
        }
    }

    private void EmitPlaceholder(PlaceholderNode p, TextValue? current, StringBuilder sb, Ctx ctx)
    {
        TextValue? value;
        if (p.Selector.Length == 0) value = current;
        else if (ctx.TryGet(p.Selector, out var v)) value = v;
        else
        {
            value = null;
            ctx.Unresolved.Add(p.Selector);
        }

        var f = p.Formatter;
        switch (f)
        {
            case null:
                if (p.Format is null) { sb.Append(value?.ToString() ?? _opt.UnknownValue); return; }
                EmitImplicit(p.Format, value, sb, ctx);
                return;
            case "diff":
            case "inverseDiff":
                sb.Append(value is { } dv ? dv.ToString() : _opt.UnknownValue);
                return;
            case "plural":
            case "p":
                EmitPlural(p.Format ?? [], value, sb, ctx, named: true);
                return;
            case "cond":
            case "conditional":
                EmitConditional(p.Format ?? [], value, sb, ctx);
                return;
            case "choose":
                EmitChoose(p.Options ?? "", p.Format ?? [], value, sb, ctx);
                return;
            case "show":
                {
                    var parts = Split(p.Format ?? []);
                    bool up = value is { Kind: TextValueKind.IfUpgraded or TextValueKind.Bool, Bool: true };
                    var chosen = up ? parts[0] : (parts.Count > 1 ? parts[1] : null);
                    if (chosen != null) Emit(chosen, value, sb, ctx);
                    return;
                }
            case "energyIcons":
                {
                    int count;
                    if (value is { Kind: TextValueKind.Number } nv) count = (int)nv.Number;
                    else if (int.TryParse(p.Options, NumberStyles.Integer, CultureInfo.InvariantCulture, out var o)) count = o;
                    else { sb.Append(_opt.UnknownValue).Append(_opt.EnergyGlyph); return; }
                    sb.Append(Icons(count, _opt.EnergyGlyph));
                    return;
                }
            case "starIcons":
                if (value is { Kind: TextValueKind.Number } sv) sb.Append(Icons((int)sv.Number, _opt.StarGlyph));
                else sb.Append(_opt.UnknownValue).Append(_opt.StarGlyph);
                return;
            case "abs":
                sb.Append(value is { Kind: TextValueKind.Number } av ? TextValue.FormatNumber(Math.Abs(av.Number)) : _opt.UnknownValue);
                return;
            case "percentMore":
                sb.Append(value is { Kind: TextValueKind.Number } pm
                    ? Convert.ToInt32((pm.Number - 1m) * 100m).ToString(CultureInfo.InvariantCulture) : _opt.UnknownValue);
                return;
            case "percentLess":
                sb.Append(value is { Kind: TextValueKind.Number } pl
                    ? Convert.ToInt32((1m - pl.Number) * 100m).ToString(CultureInfo.InvariantCulture) : _opt.UnknownValue);
                return;
            default:
                ctx.Issues.Add($"formateur inconnu '{f}'");
                if (p.Format is null) sb.Append(value?.ToString() ?? _opt.UnknownValue);
                else EmitImplicit(p.Format, value, sb, ctx);
                return;
        }
    }

    /// <summary>Comme le jeu (EnergyIconsFormatter) : 1 à 3 icônes répétées, sinon « n » + icône.</summary>
    private static string Icons(int count, string glyph) =>
        count is > 0 and < 4 ? string.Concat(Enumerable.Repeat(glyph, count))
                             : count.ToString(CultureInfo.InvariantCulture) + glyph;

    /// <summary>Formateur implicite : PluralLocalizationFormatter (nombres) puis ConditionalFormatter.</summary>
    private void EmitImplicit(List<Node> format, TextValue? value, StringBuilder sb, Ctx ctx)
    {
        var parts = Split(format);
        if (parts.Count <= 1)
        {
            Emit(format, value, sb, ctx);
            return;
        }
        if (value is { Kind: TextValueKind.Number }) EmitPlural(format, value, sb, ctx, named: false);
        else EmitConditional(format, value, sb, ctx);
    }

    private void EmitPlural(List<Node> format, TextValue? value, StringBuilder sb, Ctx ctx, bool named)
    {
        var parts = Split(format);
        if (value is not { Kind: TextValueKind.Number or TextValueKind.IfUpgraded } v)
        {
            if (!named) { EmitConditional(format, value, sb, ctx); return; }
            // Valeur inconnue : forme la plus générale (dernière), l'inconnue est déjà signalée.
            Emit(parts[^1], value, sb, ctx);
            return;
        }
        int idx = ctx.French ? FrenchRule(v.Number, parts.Count) : EnglishRule(v.Number, parts.Count);
        if (idx < 0 || idx >= parts.Count) idx = parts.Count - 1;
        Emit(parts[idx], value, sb, ctx);
    }

    // SmartFormat PluralRules.DualFromZeroToTwo ("fr")
    private static int FrenchRule(decimal n, int count) => count switch
    {
        1 => 0,
        2 => n < 2m ? 0 : 1,
        3 => n == 0m ? 0 : n > 0m && n < 2m ? 1 : n > 2m ? 2 : -1,
        _ => -1,
    };

    // SmartFormat PluralRules.DualOneOther ("en")
    private static int EnglishRule(decimal n, int count) => count switch
    {
        1 => 0,
        2 => n == 1m ? 0 : 1,
        3 => n == 0m ? 0 : n == 1m ? 1 : 2,
        4 => n < 0m ? 0 : n == 0m ? 1 : n == 1m ? 2 : 3,
        _ => -1,
    };

    private void EmitConditional(List<Node> format, TextValue? value, StringBuilder sb, Ctx ctx)
    {
        var parts = Split(format);
        if (parts.Count == 1) { Emit(parts[0], value, sb, ctx); return; }

        if (value is { Kind: TextValueKind.Number } num)
        {
            for (int k = 0; k < parts.Count; k++)
            {
                if (TryCondition(parts[k], num.Number, out bool ok, out var output))
                {
                    if (ok) { Emit(output, value, sb, ctx); return; }
                }
                else
                {
                    if (k == 0) break;
                    Emit(output, value, sb, ctx);
                    return;
                }
            }
            int idx = num.Number < 0 ? parts.Count - 1 : Math.Min((int)Math.Floor(num.Number), parts.Count - 1);
            Emit(parts[idx], value, sb, ctx);
            return;
        }
        int index = value switch
        {
            { Kind: TextValueKind.Bool or TextValueKind.IfUpgraded } b => b.Bool ? 0 : 1,
            { Kind: TextValueKind.String } s => string.IsNullOrEmpty(s.Str) ? 1 : 0,
            _ => 1, // null / inconnue
        };
        Emit(parts[Math.Min(index, parts.Count - 1)], value, sb, ctx);
    }

    private static bool TryCondition(List<Node> part, decimal value, out bool result, out List<Node> output)
    {
        result = false;
        output = part;
        if (part.Count == 0 || part[0] is not TextNode first) return false;
        var m = ComplexCondition().Match(first.Text);
        if (!m.Success) return false;
        var ops = m.Groups[1].Captures;
        var cmp = m.Groups[2].Captures;
        var nums = m.Groups[3].Captures;
        for (int k = 0; k < ops.Count; k++)
        {
            if (!decimal.TryParse(nums[k].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var rhs)) return false;
            bool r = cmp[k].Value switch
            {
                ">" => value > rhs,
                "<" => value < rhs,
                ">=" => value >= rhs,
                "<=" => value <= rhs,
                "=" or "==" => value == rhs,
                "!" or "!=" => value != rhs,
                _ => false,
            };
            if (k == 0) result = r;
            else if (ops[k].Value == "/") result |= r;
            else result &= r;
        }
        var rest = first.Text[m.Length..];
        output = [.. (rest.Length > 0 ? new List<Node> { new TextNode(rest) } : []), .. part.Skip(1)];
        return true;
    }

    private void EmitChoose(string options, List<Node> format, TextValue? value, StringBuilder sb, Ctx ctx)
    {
        var opts = options.Split('|');
        var parts = Split(format);
        string? key = value switch
        {
            null => "null",
            { Kind: TextValueKind.Bool } b => b.Bool.ToString(),
            { } v => v.ToString(),
        };
        int idx = Array.FindIndex(opts, o => string.Equals(o, key, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx >= parts.Count) idx = parts.Count - 1;
        Emit(parts[idx], value, sb, ctx);
    }
}
