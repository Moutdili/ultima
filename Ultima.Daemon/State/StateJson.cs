using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.State;

/// <summary>
/// Sérialisation du contrat (skill sts2-engine-contract) : System.Text.Json, camelCase, enums en chaîne
/// (« Shop »), <c>null</c> explicite. Une ligne par état (socket) ; le même texte va dans state.json.
/// </summary>
public static class StateJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: false);
    public static JsonSerializerOptions Indented { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = indented,
            PropertyNameCaseInsensitive = true,
            // accents FR lisibles tels quels (UTF-8), comme docs/22-state.sample.json
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }

    public static string Serialize(EngineState s, bool indented = false) =>
        JsonSerializer.Serialize(s, indented ? Indented : Options);

    public static byte[] SerializeUtf8(EngineState s) => JsonSerializer.SerializeToUtf8Bytes(s, Options);

    public static EngineState? Deserialize(string json) => JsonSerializer.Deserialize<EngineState>(json, Options);
}
