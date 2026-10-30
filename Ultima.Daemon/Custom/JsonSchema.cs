using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ultima.Daemon.Custom;

/// <summary>
/// Validateur JSON Schema **minimal et borné** : uniquement les mots-clés utilisés par les schémas du
/// contrat v1.4 (<c>schemas/{module,plugin,plugin-output}.schema.json</c>) — <c>type</c>, <c>required</c>,
/// <c>properties</c>, <c>additionalProperties: false</c>, <c>pattern</c>, <c>minLength</c>, <c>maxLength</c>,
/// <c>items</c>, <c>minItems</c>, <c>maxItems</c>, <c>uniqueItems</c>, <c>enum</c>, <c>const</c>,
/// <c>minimum</c>, <c>maximum</c>. Aucun mot-clé inconnu n'est interprété : le schéma est la seule source
/// de vérité, le daemon ne duplique aucune borne.
/// </summary>
/// <remarks>
/// Les schémas sont livrés à côté de l'exécutable (voir <see cref="SchemaSet"/>) : le daemon valide donc
/// avec le fichier du dépôt, jamais avec une copie qui pourrait diverger. Regex bornées (100 ms) et
/// profondeur limitée : un schéma ou un module hostile ne peut pas faire boucler le daemon.
/// </remarks>
public sealed partial class JsonSchema
{
    private const int MaxDepth = 8;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly JsonObject _schema;

    private JsonSchema(JsonObject schema, string name) { _schema = schema; Name = name; }

    /// <summary>Nom du fichier (ex. « module »), pour les messages.</summary>
    public string Name { get; }

    public static JsonSchema? TryLoad(string path, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path)) { error = $"schéma introuvable : {path}"; return null; }
            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
            if (node is not JsonObject o) { error = $"schéma illisible : {path}"; return null; }
            return new JsonSchema(o, Path.GetFileNameWithoutExtension(path).Replace(".schema", "", StringComparison.Ordinal));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            error = $"schéma illisible : {path} ({e.Message})";
            return null;
        }
    }

    public static JsonSchema FromJson(string json, string name) =>
        new(JsonNode.Parse(json) as JsonObject ?? throw new JsonException("schéma non objet"), name);

    /// <summary>Valide une instance. Liste vide = valide. Les messages sont lisibles (chemin + règle).</summary>
    public IReadOnlyList<string> Validate(JsonNode? instance)
    {
        var errors = new List<string>();
        Check(_schema, instance, "$", errors, 0);
        return errors;
    }

    /// <summary>Valide un texte JSON. JSON illisible = une erreur (jamais une exception).</summary>
    public IReadOnlyList<string> ValidateText(string json, out JsonNode? parsed)
    {
        parsed = null;
        try { parsed = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (JsonException e) { return new[] { $"JSON invalide : {Short(e.Message)}" }; }
        return Validate(parsed);
    }

    private static string Short(string s) => s.Length <= 200 ? s : s[..200] + "…";

    private void Check(JsonNode? schemaNode, JsonNode? value, string at, List<string> errors, int depth)
    {
        if (errors.Count >= 20) return; // borne : jamais 10 000 messages
        if (depth > MaxDepth) { errors.Add($"{at} : profondeur maximale dépassée"); return; }
        if (schemaNode is not JsonObject s) return;

        if (s["const"] is { } c && !SameJson(c, value)) { errors.Add($"{at} : valeur attendue {Json(c)}"); return; }

        if (s["enum"] is JsonArray en && !en.Any(e => SameJson(e, value)))
            errors.Add($"{at} : valeur hors liste ({Json(value)} ∉ [{string.Join(", ", en.Select(Json))}])");

        if (s["type"] is JsonValue tv && tv.TryGetValue<string>(out var type) && !HasType(type, value))
        {
            errors.Add($"{at} : type attendu {type}, reçu {KindOf(value)}");
            return;
        }

        switch (value)
        {
            case JsonObject obj:
                CheckObject(s, obj, at, errors, depth);
                break;
            case JsonArray arr:
                CheckArray(s, arr, at, errors, depth);
                break;
            case JsonValue v when v.TryGetValue<string>(out var str):
                if (s["maxLength"] is JsonValue mx && mx.TryGetValue<int>(out var max) && str.Length > max)
                    errors.Add($"{at} : {str.Length} caractères (maximum {max})");
                if (s["minLength"] is JsonValue mn && mn.TryGetValue<int>(out var min) && str.Length < min)
                    errors.Add($"{at} : {str.Length} caractères (minimum {min})");
                if (s["pattern"] is JsonValue pv && pv.TryGetValue<string>(out var pattern) && !Matches(pattern, str))
                    errors.Add($"{at} : « {Short(str)} » ne respecte pas {pattern}");
                break;
            case JsonValue v when IsNumber(v):
                if (s["minimum"] is JsonValue lo && lo.TryGetValue<double>(out var dmin) && Num(v) < dmin)
                    errors.Add($"{at} : {Json(value)} < minimum {dmin}");
                if (s["maximum"] is JsonValue hi && hi.TryGetValue<double>(out var dmax) && Num(v) > dmax)
                    errors.Add($"{at} : {Json(value)} > maximum {dmax}");
                break;
        }
    }

    private void CheckObject(JsonObject schema, JsonObject obj, string at, List<string> errors, int depth)
    {
        if (schema["required"] is JsonArray req)
            foreach (var r in req)
                if (r is JsonValue rv && rv.TryGetValue<string>(out var name) && !obj.ContainsKey(name))
                    errors.Add($"{at}.{name} : champ obligatoire absent");

        var props = schema["properties"] as JsonObject;
        bool closed = schema["additionalProperties"] is JsonValue ap && ap.TryGetValue<bool>(out var b) && !b;
        foreach (var (key, child) in obj)
        {
            if (props is not null && props.TryGetPropertyValue(key, out var sub)) Check(sub, child, $"{at}.{key}", errors, depth + 1);
            else if (closed) errors.Add($"{at}.{key} : champ inconnu");
        }
    }

    private void CheckArray(JsonObject schema, JsonArray arr, string at, List<string> errors, int depth)
    {
        if (schema["maxItems"] is JsonValue mx && mx.TryGetValue<int>(out var max) && arr.Count > max)
            errors.Add($"{at} : {arr.Count} éléments (maximum {max})");
        if (schema["minItems"] is JsonValue mn && mn.TryGetValue<int>(out var min) && arr.Count < min)
            errors.Add($"{at} : {arr.Count} éléments (minimum {min})");
        bool unique = schema["uniqueItems"] is JsonValue u && u.TryGetValue<bool>(out var ub) && ub;
        if (unique)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arr.Count; i++)
                if (!seen.Add(arr[i]?.ToJsonString() ?? "null")) errors.Add($"{at}[{i}] : doublon");
        }
        if (schema["items"] is { } items)
            for (int i = 0; i < arr.Count; i++) Check(items, arr[i], $"{at}[{i}]", errors, depth + 1);
    }

    private static bool Matches(string pattern, string value)
    {
        try { return Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, RegexTimeout); }
        catch (RegexMatchTimeoutException) { return false; }
        catch (ArgumentException) { return false; } // motif invalide : on refuse, jamais de chute
    }

    private static bool HasType(string type, JsonNode? value) => type switch
    {
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "string" => value is JsonValue v && v.TryGetValue<string>(out _),
        "boolean" => value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "integer" => value is JsonValue v && IsNumber(v) && Num(v) == Math.Floor(Num(v)),
        "number" => value is JsonValue v && IsNumber(v),
        "null" => value is null,
        _ => true, // type inconnu : le schéma fait foi, mot-clé ignoré
    };

    private static string KindOf(JsonNode? value) => value switch
    {
        null => "null",
        JsonObject => "objet",
        JsonArray => "tableau",
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => "chaîne",
            JsonValueKind.True or JsonValueKind.False => "booléen",
            JsonValueKind.Number => "nombre",
            _ => "valeur",
        },
        _ => "valeur",
    };

    private static bool IsNumber(JsonValue v) => v.GetValueKind() == JsonValueKind.Number;

    private static double Num(JsonValue v) => v.TryGetValue<double>(out var d) ? d : 0;

    private static bool SameJson(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (IsNumberNode(a) && IsNumberNode(b)) return Num((JsonValue)a) == Num((JsonValue)b);
        return a.ToJsonString() == b.ToJsonString();
    }

    private static bool IsNumberNode(JsonNode n) => n is JsonValue v && IsNumberValue(v);

    private static bool IsNumberValue(JsonValue v) => v.GetValueKind() == JsonValueKind.Number;

    private static string Json(JsonNode? n) => n?.ToJsonString() ?? "null";
}
