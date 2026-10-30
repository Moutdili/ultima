using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Strength;
using Ultima.Saves.Model;

// Calcule la distribution de la force du deck (score G) par acte et par personnage,
// à partir des decks finaux du corpus. Sortie : data/calibration/deck_percentiles.json.
//
// Usage : dotnet run --project tools/deck_percentiles -- [entrée] [sortie] [max_runs]
// Défaut : tmp/decks.jsonl.gz → data/calibration/deck_percentiles.json, 200 000 runs.

const int MinRuns = 200;   // sous ce seuil, un bucket n'est pas publié

var input = args.Length > 0 ? args[0] : "tmp/decks.jsonl.gz";
var output = args.Length > 1 ? args[1] : "data/calibration/deck_percentiles.json";
var indexMode = args.Contains("--index", StringComparer.Ordinal);
var maxRuns = args.Length > 2 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap)
    ? cap
    : 200_000;

if (!File.Exists(input))
{
    Console.Error.WriteLine($"entrée introuvable : {input} (lancer d'abord tools/deck_percentiles/extract.py)");
    return 2;
}

// Les catalogues sont chargés une seule fois : un calculateur par run coûterait des minutes.
var calculator = new PlayerStrengthCalculator();

var buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
var sizes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
var deckSizesUsed = new List<int>();
var runs = 0;
var skipped = 0;

using var stream = new GZipStream(File.OpenRead(input), CompressionMode.Decompress);
using var reader = new StreamReader(stream);

while (reader.ReadLine() is { } line)
{
    if (runs >= maxRuns) break;
    if (line.Length == 0) continue;

    JsonDocument doc;
    try
    {
        doc = JsonDocument.Parse(line);
    }
    catch (JsonException)
    {
        skipped++;
        continue;
    }

    using (doc)
    {
        var root = doc.RootElement;

        string character;
        int ascension, act, floor, hp, maxHp;
        var deck = new List<CardInstance>(32);

        if (indexMode)
        {
            // Format de l'index (tools/index_runs.py) : pl = joueurs, d = décisions.
            if (!root.TryGetProperty("pl", out var players) || players.ValueKind != JsonValueKind.Array
                || players.GetArrayLength() != 1)
            {
                skipped++;
                continue;
            }

            var player = players[0];
            character = player.TryGetProperty("c", out var pc) ? pc.GetString() ?? "" : "";
            ascension = root.TryGetProperty("asc", out var ia) && ia.ValueKind == JsonValueKind.Number ? ia.GetInt32() : 0;
            hp = player.TryGetProperty("hp", out var ih) && ih.ValueKind == JsonValueKind.Number ? ih.GetInt32() : 0;
            maxHp = player.TryGetProperty("mhp", out var im) && im.ValueKind == JsonValueKind.Number ? im.GetInt32() : 0;
            act = 1;
            floor = 1;
            var decisionCount = 0;

            if (root.TryGetProperty("d", out var decisions) && decisions.ValueKind == JsonValueKind.Array)
            {
                decisionCount = decisions.GetArrayLength();
                foreach (var decision in decisions.EnumerateArray())
                {
                    if (decision.ValueKind != JsonValueKind.Object) continue;
                    if (decision.TryGetProperty("a", out var da) && da.ValueKind == JsonValueKind.Number)
                        act = Math.Max(act, da.GetInt32() + 1);
                    if (decision.TryGetProperty("f", out var df) && df.ValueKind == JsonValueKind.Number)
                        floor = Math.Max(floor, df.GetInt32() + 1);
                }
            }

            // Sans décision, l'acte et l'étage sont inconnus : on ne devine pas, on saute.
            if (decisionCount == 0)
            {
                skipped++;
                continue;
            }

            if (player.TryGetProperty("deck", out var indexDeck) && indexDeck.ValueKind == JsonValueKind.Array)
            {
                foreach (var card in indexDeck.EnumerateArray())
                {
                    var id = card.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    deck.Add(new CardInstance(id!, 0, null, null));   // l'index ne garde pas le niveau
                }
            }
        }
        else
        {
            character = root.TryGetProperty("c", out var c) ? c.GetString() ?? "" : "";
            ascension = root.TryGetProperty("a", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : 0;
            act = root.TryGetProperty("k", out var k) && k.ValueKind == JsonValueKind.Number ? Math.Max(1, k.GetInt32()) : 1;
            floor = root.TryGetProperty("f", out var f) && f.ValueKind == JsonValueKind.Number ? Math.Max(1, f.GetInt32()) : 1;
            maxHp = root.TryGetProperty("mhp", out var mh) && mh.ValueKind == JsonValueKind.Number ? mh.GetInt32() : 0;
            hp = root.TryGetProperty("hp", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : 0;

            if (root.TryGetProperty("d", out var cards) && cards.ValueKind == JsonValueKind.Array)
            {
                foreach (var card in cards.EnumerateArray())
                {
                    if (card.ValueKind != JsonValueKind.Array || card.GetArrayLength() < 1) continue;
                    var id = card[0].GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var upgrade = card.GetArrayLength() > 1 && card[1].ValueKind == JsonValueKind.Number
                        ? card[1].GetInt32()
                        : 0;
                    deck.Add(new CardInstance(id!, upgrade, null, null));
                }
            }
        }

        if (character.Length == 0)
        {
            skipped++;
            continue;
        }

        // L'index ne publie pas les PV (players[].current_hp absent de l'export v0.107.1) :
        // on compare donc tous les decks à une même convention, PV pleins à 70. C'est la seule
        // façon d'avoir une référence comparable ; le champ est documenté dans le fichier produit.
        const int ReferenceMaxHp = 70;
        maxHp = ReferenceMaxHp;
        hp = ReferenceMaxHp;

        if (deck.Count == 0)
        {
            skipped++;
            continue;
        }

        // PV pleins : la note de deck ne doit pas dépendre des blessures (docs/27 §2).
        var ctx = new ScoringContext(
            $"CHARACTER.{character}", Math.Max(0, act - 1), null, ascension, floor,
            deck, Array.Empty<string>(), 0, maxHp, maxHp);

        int g;
        try
        {
            g = calculator.Compute(ctx).Total;
        }
        catch (Exception ex)
        {
            // Un deck exotique ne doit pas arrêter le corpus : on le compte et on continue.
            Console.Error.WriteLine($"run ignoré ({character}, acte {act}) : {ex.Message}");
            skipped++;
            continue;
        }

        Add(buckets, $"{character}|{act}", g);
        Add(sizes, $"{character}|{act}", deck.Count);
        Add(buckets, $"*|{act}", g);
        Add(sizes, $"*|{act}", deck.Count);
        deckSizesUsed.Add(deck.Count);
        runs++;
    }

    if (runs % 25_000 == 0) Console.Error.WriteLine($"  {runs} decks calculés…");
}

if (runs == 0)
{
    Console.Error.WriteLine("aucun deck exploitable : rien n'a été écrit.");
    return 3;
}

var entries = new List<Bucket>();
foreach (var (key, values) in buckets.OrderBy(kv => kv.Key, StringComparer.Ordinal))
{
    if (values.Count < MinRuns) continue;
    values.Sort();
    var parts = key.Split('|');
    var deckSizes = sizes[key];
    deckSizes.Sort();

    entries.Add(new Bucket(
        Key: key,
        Character: parts[0],
        Act: int.Parse(parts[1], CultureInfo.InvariantCulture),
        N: values.Count,
        P10: Percentile(values, 10),
        P25: Percentile(values, 25),
        P50: Percentile(values, 50),
        P75: Percentile(values, 75),
        P90: Percentile(values, 90),
        MedianDeck: Percentile(deckSizes, 50)));
}

deckSizesUsed.Sort();

var payload = new Payload(
    Schema: "ultima.deck-percentiles/1",
    Source: new SourceInfo(
        File: "data/exports/runs-v0.107.1.jsonl.gz",
        Method: "decks finaux, runs solo, score G = PlayerStrengthCalculator.Compute",
        Caveat: "decks FINAUX des runs arrêtés au même acte (les cartes retirées ne sont pas " +
                "reconstituables, la référence est donc légèrement haute) ; PV pleins à 70 pour tous " +
                "les decks, l'index ne publiant pas les PV de fin de run.",
        Runs: runs,
        Skipped: skipped,
        MedianDeck: Percentile(deckSizesUsed, 50)),
    MinRuns: MinRuns,
    Buckets: entries);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
{
    WriteIndented = false,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
});
File.WriteAllText(output, json);

Console.WriteLine($"écrit {output} ({new FileInfo(output).Length / 1024.0:0.#} Ko) — {runs} decks, {entries.Count} buckets");
return 0;

static void Add(Dictionary<string, List<int>> map, string key, int value)
{
    if (!map.TryGetValue(key, out var list))
    {
        list = new List<int>(1024);
        map[key] = list;
    }
    list.Add(value);
}

static int Percentile(List<int> sorted, int p)
{
    if (sorted.Count == 0) return 0;
    var rank = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
    return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
}

internal sealed record Bucket(
    string Key, string Character, int Act, int N,
    int P10, int P25, int P50, int P75, int P90, int MedianDeck);

internal sealed record SourceInfo(string File, string Method, string Caveat, int Runs, int Skipped, int MedianDeck);

internal sealed record Payload(string Schema, SourceInfo Source, int MinRuns, List<Bucket> Buckets);
