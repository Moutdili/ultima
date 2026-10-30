using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Custom;

/// <summary>Module refusé : message lisible (le daemon le publie en <c>Health=error</c> + <c>Problem</c>).</summary>
public sealed class ModuleFormatException(string message) : Exception(message);

/// <summary>Un champ <c>{chemin|filtre…}</c> d'un gabarit.</summary>
public sealed record Placeholder(string Path, IReadOnlyList<string> Filters);

/// <summary>Morceau de gabarit : texte littéral, ou champ à résoudre.</summary>
public sealed record TemplatePart(string? Literal, Placeholder? Field);

public sealed record ModuleTemplate(IReadOnlyList<TemplatePart> Parts);

/// <summary>Comparaison d'une condition <c>when</c> : <c>{chemin} op littéral|{chemin}</c>.</summary>
public sealed record Comparison(string Path, string Op, string? OtherPath, double? Number, string? Text, bool? Boolean);

/// <summary>Condition : OU de ET ( <c>and</c> plus fort que <c>or</c> ), sans parenthèses.</summary>
public sealed record Condition(IReadOnlyList<IReadOnlyList<Comparison>> OrOfAnds);

public sealed record ModuleRow(ModuleTemplate Text, Condition? When, string? Tone, string? Grade);

/// <summary>Module déclaratif compilé (aucun code exécuté : gabarits et comparaisons seulement).</summary>
public sealed record ModuleDefinition(string Id, string Label, IReadOnlyList<string>? Screens, IReadOnlyList<ModuleRow> Rows);

/// <summary>
/// Compilateur des modules déclaratifs (docs/44 §6). Tout ce qui peut échouer échoue ICI, au chargement
/// (le module est alors publié en <c>Health=error</c>) : à la publication, le rendu ne peut plus lever.
/// </summary>
public static class ModuleParser
{
    public const int MaxPlaceholders = 10;
    public const int MaxTerms = 8;
    public const int MaxRowText = 160;

    /// <summary>Filtres admis (liste fermée : tout autre filtre rend le module invalide).</summary>
    public static readonly IReadOnlyList<string> Filters = new[] { "upper", "pct", "grade", "count" };

    public static ModuleDefinition Parse(JsonObject root)
    {
        var id = Str(root, "id") ?? throw new ModuleFormatException("id manquant");
        if (!id.StartsWith(CustomIds.Prefix, StringComparison.Ordinal))
            throw new ModuleFormatException($"id « {id} » : doit commencer par « {CustomIds.Prefix} »");
        var label = Str(root, "label") ?? throw new ModuleFormatException("label manquant");
        if (root["api"] is not JsonValue api || !api.TryGetValue<int>(out var apiVersion))
            throw new ModuleFormatException("api manquant");
        if (apiVersion != 1) throw new ModuleFormatException($"api {apiVersion} inconnue (seule l'API 1 existe)");

        IReadOnlyList<string>? screens = null;
        if (root["screens"] is JsonArray sa)
        {
            var list = new List<string>();
            foreach (var s in sa)
            {
                if (s is not JsonValue v || !v.TryGetValue<string>(out var name))
                    throw new ModuleFormatException("screens : nom d'écran attendu");
                if (!ScreenIds.All.Contains(name))
                    throw new ModuleFormatException($"screens : écran inconnu « {name} »");
                list.Add(name);
            }
            screens = list;
        }

        if (root["rows"] is not JsonArray rows || rows.Count == 0)
            throw new ModuleFormatException("rows : au moins une ligne est nécessaire");
        var compiled = new List<ModuleRow>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not JsonObject r) throw new ModuleFormatException($"rows[{i}] : objet attendu");
            var text = Str(r, "text") ?? throw new ModuleFormatException($"rows[{i}].text manquant");
            compiled.Add(new ModuleRow(
                ParseTemplate(text),
                Str(r, "when") is { Length: > 0 } when ? ParseCondition(when) : null,
                Str(r, "tone"),
                Str(r, "grade")));
        }
        return new ModuleDefinition(id, label, screens, compiled);
    }

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // ---------------- gabarits ----------------

    public static ModuleTemplate ParseTemplate(string text)
    {
        var parts = new List<TemplatePart>();
        var literal = new StringBuilder();
        int count = 0;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c != '{') { literal.Append(c); i++; continue; }
            int end = text.IndexOf('}', i + 1);
            if (end < 0) throw new ModuleFormatException($"gabarit non fermé : « {text} »");
            if (literal.Length > 0) { parts.Add(new TemplatePart(literal.ToString(), null)); literal.Clear(); }
            var inner = text[(i + 1)..end];
            if (++count > MaxPlaceholders) throw new ModuleFormatException($"gabarit : plus de {MaxPlaceholders} champs");
            parts.Add(new TemplatePart(null, ParsePlaceholder(inner)));
            i = end + 1;
        }
        if (literal.Length > 0) parts.Add(new TemplatePart(literal.ToString(), null));
        return new ModuleTemplate(parts);
    }

    private static Placeholder ParsePlaceholder(string inner)
    {
        var bits = inner.Split('|');
        var path = bits[0].Trim();
        if (path.Length == 0) throw new ModuleFormatException("gabarit : champ vide ( « {} » )");
        if (path.Length > StatePaths.MaxPathLength) throw new ModuleFormatException($"gabarit : chemin trop long ({path.Length})");
        var filters = new List<string>();
        foreach (var f in bits.Skip(1))
        {
            var name = f.Trim().ToLowerInvariant();
            if (!Filters.Contains(name)) throw new ModuleFormatException($"filtre inconnu : « |{f.Trim()} »");
            filters.Add(name);
        }
        return new Placeholder(path, filters);
    }

    // ---------------- conditions ----------------

    public static Condition ParseCondition(string when)
    {
        var text = when.Trim();
        if (text.Contains('(') || text.Contains(')')) throw new ModuleFormatException("when : les parenthèses ne sont pas admises");
        var orGroups = new List<IReadOnlyList<Comparison>>();
        int terms = 0;
        foreach (var orPart in Split(text, "or"))
        {
            var ands = new List<Comparison>();
            foreach (var andPart in Split(orPart, "and"))
            {
                if (++terms > MaxTerms) throw new ModuleFormatException($"when : plus de {MaxTerms} comparaisons");
                ands.Add(ParseComparison(andPart));
            }
            orGroups.Add(ands);
        }
        return new Condition(orGroups);
    }

    /// <summary>Découpe sur « and » / « or » entourés d'espaces (jamais dans une chaîne citée).</summary>
    private static List<string> Split(string text, string keyword)
    {
        var parts = new List<string>();
        int start = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            var token = " " + keyword + " ";
            if (i + token.Length <= text.Length && string.Compare(text, i, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                parts.Add(text[start..i]);
                i += token.Length - 1;
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        var cleaned = parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (cleaned.Count == 0) throw new ModuleFormatException($"when : expression vide (« {text} »)");
        return cleaned;
    }

    private static readonly string[] Ops = { "<=", ">=", "==", "!=", "<", ">" };

    private static Comparison ParseComparison(string term)
    {
        int opIndex = -1;
        string op = "";
        foreach (var candidate in Ops)
        {
            var i = term.IndexOf(candidate, StringComparison.Ordinal);
            if (i <= 0) continue;
            if (opIndex < 0 || i < opIndex) { opIndex = i; op = candidate; }
        }
        if (opIndex <= 0) throw new ModuleFormatException($"when : comparaison attendue (« {term} »)");
        var left = term[..opIndex].Trim();
        var right = term[(opIndex + op.Length)..].Trim();
        if (right.Length == 0) throw new ModuleFormatException($"when : valeur manquante après « {op} »");
        var path = Unwrap(left) ?? BarePath(left)
            ?? throw new ModuleFormatException($"when : « {left} » doit être un chemin ({{run.gold}} ou run.gold)");
        if (path.Length == 0) throw new ModuleFormatException("when : chemin vide");
        if (path.Contains('|')) throw new ModuleFormatException($"when : les filtres ne sont pas admis (« {left} »)");
        if (path.Length > StatePaths.MaxPathLength) throw new ModuleFormatException("when : chemin trop long");

        if (Unwrap(right) is { } other) return new Comparison(path, op, other, null, null, null);
        if (right.Length >= 2 && (right[0] == '"' || right[0] == '\'') && right[^1] == right[0])
            return new Comparison(path, op, null, null, right[1..^1], null);
        if (double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return new Comparison(path, op, null, number, null, null);
        if (bool.TryParse(right, out var boolean)) return new Comparison(path, op, null, null, null, boolean);
        if (right.Equals("oui", StringComparison.OrdinalIgnoreCase)) return new Comparison(path, op, null, null, null, true);
        if (right.Equals("non", StringComparison.OrdinalIgnoreCase)) return new Comparison(path, op, null, null, null, false);
        return new Comparison(path, op, null, null, right, null);
    }

    private static string? Unwrap(string s) =>
        s.Length > 2 && s[0] == '{' && s[^1] == '}' ? s[1..^1].Trim() : null;

    /// <summary>Chemin nu (sans accolades), comme dans l'exemple de docs/44 §6 : « run.gold &gt;= 150 ».</summary>
    private static string? BarePath(string s)
    {
        if (s.Length == 0 || s.Length > StatePaths.MaxPathLength) return null;
        char first = s[0];
        if (!char.IsLetter(first) && first != '_') return null;
        foreach (char c in s)
            if (!char.IsLetterOrDigit(c) && c is not ('.' or '_' or '[' or ']')) return null;
        return s;
    }

    // ---------------- évaluation ----------------

    /// <summary>Rendu d'un module sur l'état publié. Ne lève jamais pour un module compilé.</summary>
    public static IReadOnlyList<CustomRowView> Render(EngineState state, ModuleDefinition def)
    {
        var rows = new List<CustomRowView>(def.Rows.Count);
        foreach (var row in def.Rows)
        {
            if (row.When is { } when && !Holds(state, when)) continue;
            var text = RenderTemplate(state, row.Text);
            if (text.Length > MaxRowText) text = text[..(MaxRowText - 1)] + "…";
            rows.Add(new CustomRowView(text, string.IsNullOrEmpty(row.Tone) ? null : row.Tone,
                string.IsNullOrEmpty(row.Grade) ? null : row.Grade));
        }
        return rows;
    }

    public static string RenderTemplate(EngineState state, ModuleTemplate template)
    {
        var sb = new StringBuilder();
        foreach (var part in template.Parts)
        {
            if (part.Literal is { } literal) { sb.Append(literal); continue; }
            var field = part.Field!;
            var text = StatePaths.TryGet(state, field.Path, out var value) ? StatePaths.Display(value) : null;
            text ??= "—"; // chemin absent : « — », jamais 0 ni la clé
            foreach (var filter in field.Filters) text = Apply(filter, text);
            sb.Append(text);
        }
        return sb.ToString();
    }

    private static string Apply(string filter, string text) => filter switch
    {
        "upper" => text.ToUpperInvariant(),
        "pct" => Percent(text),
        "grade" => Grade(text),
        "count" => Count(text),
        _ => text,
    };

    private static string Percent(string text)
    {
        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return text;
        if (v > 0 && v <= 1) v *= 100;
        return StatePaths.Number(Math.Round(v, 1)) + " %";
    }

    private static string Grade(string text)
    {
        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Grades.FromTotal((int)Math.Round(v));
        var up = text.ToUpperInvariant();
        return up.Length == 1 && "SABCDF".Contains(up, StringComparison.Ordinal) ? up : text;
    }

    private static string Count(string text)
    {
        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
        return text;
    }

    /// <summary>
    /// Condition vraie ? Chemin absent ⇒ comparaison FAUSSE (jamais « 0 » ni « chaîne vide »).
    /// </summary>
    public static bool Holds(EngineState state, Condition condition)
    {
        foreach (var group in condition.OrOfAnds)
        {
            bool all = true;
            foreach (var c in group)
                if (!Compare(state, c)) { all = false; break; }
            if (all) return true;
        }
        return false;
    }

    private static bool Compare(EngineState state, Comparison c)
    {
        if (!StatePaths.TryGet(state, c.Path, out var left)) return false;
        object? right;
        if (c.OtherPath is not null)
        {
            if (!StatePaths.TryGet(state, c.OtherPath, out right)) return false;
        }
        else if (c.Number is { } n) right = n;
        else if (c.Boolean is { } b) right = b;
        else right = c.Text;

        if (right is bool rb)
        {
            if (left is not bool lb) return false;
            return c.Op switch { "==" => lb == rb, "!=" => lb != rb, _ => false };
        }
        if (StatePaths.TryNumber(left, out var ln) && StatePaths.TryNumber(right, out var rn))
        {
            int cmp = ln.CompareTo(rn);
            return c.Op switch
            {
                "==" => cmp == 0,
                "!=" => cmp != 0,
                "<" => cmp < 0,
                "<=" => cmp <= 0,
                ">" => cmp > 0,
                ">=" => cmp >= 0,
                _ => false,
            };
        }
        var ls = StatePaths.Display(left);
        var rs = StatePaths.Display(right);
        if (ls is null || rs is null) return false;
        int k = string.Compare(ls, rs, StringComparison.Ordinal);
        return c.Op switch
        {
            "==" => k == 0,
            "!=" => k != 0,
            "<" => k < 0,
            "<=" => k <= 0,
            ">" => k > 0,
            ">=" => k >= 0,
            _ => false,
        };
    }
}
