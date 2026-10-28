using Ultima.Saves.Log;
using Xunit.Abstractions;

namespace Ultima.Saves.Tests;

/// <summary>
/// Tests « de terrain » sur les VRAIES donnees de Mio (lecture seule, sans verrou).
/// Ignores automatiquement si le dossier du jeu est absent (CI).
/// </summary>
public class RealDataTests(ITestOutputHelper output)
{
    [RealDataFact]
    public void Toutes_les_runs_reelles_sont_parsees()
    {
        var p = TestPaths.RealProfile()!;
        var files = HistoryReader.EnumerateRunFiles(p.HistoryDir);
        var errors = new List<string>();
        var runs = new HistoryReader().ReadAll(p.HistoryDir, (f, e) => errors.Add($"{Path.GetFileName(f)}: {e}"));

        output.WriteLine($"profil actif : profile{p.ProfileId} (vanilla, defaut={p.ProfileIdFromDefault})");
        output.WriteLine($"fichiers .run : {files.Count} ; parsees : {runs.Count} ; erreurs : {errors.Count}");
        foreach (var g in runs.GroupBy(r => r.BuildId).OrderBy(g => g.Key))
            output.WriteLine($"  build_id {g.Key} : {g.Count()} runs ({g.Count(r => r.Win)} victoires, {g.Count(r => r.WasAbandoned)} abandons)");
        foreach (var g in runs.GroupBy(r => r.SchemaVersion))
            output.WriteLine($"  schema_version {g.Key} : {g.Count()}");
        foreach (var g in runs.GroupBy(r => r.Players.Count).OrderBy(g => g.Key))
            output.WriteLine($"  {g.Key} joueur(s) : {g.Count()}");
        output.WriteLine($"  etages : {runs.Sum(r => r.Floors.Count)} ; cartes proposees : {runs.Sum(r => r.Floors.Sum(f => f.PlayerStats.Sum(s => s.CardChoices.Count)))}");
        foreach (var e in errors) output.WriteLine("ERREUR " + e);

        Assert.Empty(errors);
        Assert.Equal(files.Count, runs.Count);
        Assert.True(runs.Count >= 250, $"attendu >= 250 runs, trouve {runs.Count}");

        // joueur local identifiable dans chaque run (solo : id=1 ; co-op : SteamID64 du dossier)
        var orphan = runs.Where(r => r.LocalPlayer(p.SteamId64) is null).Select(r => r.FileName).ToList();
        output.WriteLine($"  runs sans joueur local identifie : {orphan.Count}");
        Assert.Empty(orphan);

        // aucun type de case inconnu ; floors numerotes 1..n
        Assert.DoesNotContain(runs.SelectMany(r => r.Floors), f => f.Kind == Model.MapPointKind.Unrecognized);
        Assert.All(runs, r => Assert.Equal(Enumerable.Range(1, r.Floors.Count), r.Floors.Select(f => f.Floor)));
    }

    [RealDataFact]
    public void Save_et_progress_reels()
    {
        var p = TestPaths.RealProfile()!;
        Assert.DoesNotContain("modded", p.SavesDir);
        var run = new RunSaveReader().ReadCurrent(p);
        output.WriteLine(run is null ? "profil vanilla : aucune run en cours" : $"run en cours : {run.Players[0].CharacterId}");

        var st = new ProgressReader().TryRead(p);
        Assert.NotNull(st);
        foreach (var c in st!.Characters)
            output.WriteLine($"  {c.Id} : {c.TotalWins} V / {c.TotalLosses} D, ascension max {c.MaxAscension}");
        Assert.NotEmpty(st.Characters);
    }

    [RealDataFact]
    public void Exemple_de_format_profil_modded()
    {
        // Seule save de run disponible aujourd'hui : profil modded/, lue comme EXEMPLE de format uniquement.
        var p = TestPaths.RealProfile()!;
        var path = Path.Combine(p.AccountDir, "modded", "profile1", "saves", "current_run.save");
        if (!File.Exists(path)) { output.WriteLine("pas d'exemple modded"); return; }
        var s = new RunSaveReader().Read(path);
        output.WriteLine($"schema {s.SchemaVersion}, {s.Players[0].CharacterId}, deck {s.Players[0].Deck.Count}, position {s.CurrentCoord}");
        Assert.Equal(16, s.SchemaVersion);
        Assert.NotEmpty(s.Players[0].Deck);
    }

    [RealDataFact]
    public void Logs_reels()
    {
        var logs = Directory.GetFiles(Path.Combine(TestPaths.RealRoot, "logs"), "godot*.log");
        int total = 0;
        foreach (var f in logs)
        {
            using var t = new GodotLogTail(f, fromStart: true);
            var counts = new Dictionary<string, int>();
            t.EventReceived += (_, e) => counts[e.GetType().Name] = counts.GetValueOrDefault(e.GetType().Name) + 1;
            total += t.Poll();
            output.WriteLine($"{Path.GetFileName(f)} : " + string.Join(", ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
        }
        Assert.True(total > 0);
    }
}
