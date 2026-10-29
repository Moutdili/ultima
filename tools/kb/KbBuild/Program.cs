using System.Diagnostics;
using System.Text.Json;
using Ultima.Knowledge;
using Ultima.Knowledge.Build;

// ultima-kb-build --game <dir> --catalog <dir> --out <dir> [--game-version v0.107.1]
// Lecture seule sur le jeu. Écrit <out>/kb.json et <out>/coverage.md (hors dépôt : textes MegaCrit).
var opts = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i + 1 < args.Length; i += 2) opts[args[i]] = args[i + 1];
string Need(string k) => opts.TryGetValue(k, out var v) ? v : throw new ArgumentException($"argument manquant {k}");

try
{
    var game = Path.GetFullPath(Need("--game"));
    var catalog = Path.GetFullPath(Need("--catalog"));
    var outDir = Path.GetFullPath(Need("--out"));
    var version = opts.GetValueOrDefault("--game-version", "v0.107.1");
    if (!Directory.Exists(Path.Combine(game, "pck", "localization")))
        throw new DirectoryNotFoundException($"localisation introuvable sous {game}/pck/localization");

    var sw = Stopwatch.StartNew();
    var doc = KbBuilder.Build(new KbBuildInput(game, catalog, version));
    Directory.CreateDirectory(outDir);
    var kbPath = Path.Combine(outDir, "kb.json");
    var tmp = kbPath + ".tmp";
    File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(doc, KbJson.Options));
    File.Move(tmp, kbPath, overwrite: true);
    File.WriteAllText(Path.Combine(outDir, "coverage.md"), CoverageReport.ToMarkdown(doc));
    var build = sw.ElapsedMilliseconds;

    sw.Restart();
    var kb = KnowledgeBase.Load(kbPath);
    var load = sw.ElapsedMilliseconds;

    Console.WriteLine($"kb.json : {kbPath} ({new FileInfo(kbPath).Length / 1024} Kio, génération {build} ms, chargement {load} ms)");
    foreach (var (name, c) in doc.Coverage.Categories)
        Console.WriteLine($"  {name,-13} {c.NameFr,4}/{c.Total,-4} noms FR  (repli EN {c.NameEnFallback}, id {c.NameIdFallback}) ; desc. FR {c.DescriptionFr}, EN {c.DescriptionEnFallback}, aucune {c.DescriptionNone} ; var. non résolues {c.WithUnresolvedVars} ; loc. seule {c.LocalizationOnly}");
    Console.WriteLine($"  notes : {doc.Coverage.Notes.Count} (voir coverage.md)");
    _ = kb;
    return 0;
}
catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"build-kb : {e.Message}");
    return 2;
}
