using System.Collections.Concurrent;
using Ultima.Saves.Log;
using Ultima.Saves.Model;

namespace Ultima.Saves.Tests;

public class GodotLogParserTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch;

    // Lignes copiees telles quelles des logs reels (SteamID remplace).
    [Theory]
    [InlineData("[INFO] Preloading 'Combat Room' assets... count=11 vfx=0", RoomKind.Combat)]
    [InlineData("[INFO] Preloading 'Event Room' assets... count=2 vfx=0", RoomKind.Event)]
    [InlineData("[INFO] Preloading 'Merchant Room' assets... count=3 vfx=0", RoomKind.Merchant)]   // source PreloadManager.cs:119
    [InlineData("[INFO] Preloading 'RestSite Room' assets... count=3 vfx=0", RoomKind.RestSite)]   // source PreloadManager.cs:127
    [InlineData("[INFO] Preloading 'Treasure Room' assets... count=3 vfx=0", RoomKind.Treasure)]   // source PreloadManager.cs:114
    public void RoomEntered(string line, RoomKind kind)
    {
        var e = Assert.IsType<RoomEntered>(GodotLogParser.ParseLine(line, T));
        Assert.Equal(kind, e.Kind);
    }

    [Theory]
    [InlineData("[INFO] Preloading 'Combat Room' Complete: assets=11 time_elapsed=41ms")]
    [InlineData("[INFO] Preloading 'Act=OVERGROWTH' assets... count=8 vfx=0")]
    [InlineData("[INFO] Preloading 'characters=IRONCLAD' assets... count=37 vfx=48")]
    [InlineData("[INFO] Wrote 41422 bytes to modded/profile1/saves/current_run.save in steam remote store")]
    [InlineData("[INFO] DevConsole: win ")]
    [InlineData("")]
    public void Lignes_ignorees(string line) => Assert.Null(GodotLogParser.ParseLine(line, T));

    [Fact]
    public void CombatRoomCreated_normalise_l_identifiant()
    {
        var e = Assert.IsType<CombatRoomCreated>(GodotLogParser.ParseLine(
            "[INFO] Creating NCombatRoom with mode=ActiveCombat encounter=SLIMES_WEAK.", T));
        Assert.Equal("ENCOUNTER.SLIMES_WEAK", e.EncounterId);
        Assert.True(e.IsActiveCombat);
    }

    [Fact]
    public void CardRewardRolled_sans_valeurs()
    {
        var e = Assert.IsType<CardRewardRolled>(GodotLogParser.ParseLine(
            "[INFO] Card rarity: Rolled 0.47728595, need < -0.020000001 for rare (offset = -0.05)", T));
        // seul le marqueur est publie : le type n'a aucune propriete au-dela de l'horodatage et de la ligne brute
        Assert.Equal(["EqualityContract", "RawLine", "ReceivedAt"],
            e.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic).Select(p => p.Name).Order());
    }

    [Fact]
    public void CardRewardSelected()
    {
        var e = Assert.IsType<CardRewardSelected>(GodotLogParser.ParseLine("[INFO] Player 1 selected card reward", T));
        Assert.Equal(1UL, e.PlayerNetId);
    }

    [Fact]
    public void MapVote_nouveau_et_changement()
    {
        var e = Assert.IsType<MapVote>(GodotLogParser.ParseLine(
            "[INFO] Player vote changed for 1: ->MapVote (gen: 3 coord: (3, 14))", T));
        Assert.Equal((1UL, (MapCoord?)null, (MapCoord?)new MapCoord(3, 14), (int?)3), (e.PlayerNetId, e.From, e.To, e.Generation));

        var c = Assert.IsType<MapVote>(GodotLogParser.ParseLine(
            "[INFO] Player vote changed for 70000000000000001: MapVote (gen: 1 coord: (4, 2))->MapVote (gen: 1 coord: (5, 2))", T));
        Assert.Equal(new MapCoord(4, 2), c.From);
        Assert.Equal(new MapCoord(5, 2), c.To);
        Assert.Equal(70000000000000001UL, c.PlayerNetId);

        var cancel = Assert.IsType<MapVote>(GodotLogParser.ParseLine(
            "[INFO] Player vote changed for 1: MapVote (gen: 1 coord: (4, 2))->", T));
        Assert.Null(cancel.To);
    }

    [Fact]
    public void SaveWritten_type_et_profil()
    {
        var e = Assert.IsType<SaveWritten>(GodotLogParser.ParseLine(
            "[INFO] Wrote 42463 bytes to path=user://steam/70000000000000001/modded/profile1/saves/current_run.save save_dir=user://steam/70000000000000001", T));
        Assert.Equal((42463L, SaveFileKind.CurrentRun, true), (e.Bytes, e.Kind, e.IsModdedProfile));

        var h = Assert.IsType<SaveWritten>(GodotLogParser.ParseLine(
            "[INFO] Wrote 4708 bytes to path=user://steam/7/profile1/saves/history/1793134265.run save_dir=user://steam/7", T));
        Assert.Equal(SaveFileKind.History, h.Kind);
        Assert.False(h.IsModdedProfile);
    }

    [Fact]
    public void RunContinued()
    {
        var e = Assert.IsType<RunContinued>(GodotLogParser.ParseLine("[INFO] Continuing run with character: CHARACTER.IRONCLAD", T));
        Assert.Equal("CHARACTER.IRONCLAD", e.CharacterId);
    }
}

public class GodotLogTailTests
{
    private static List<GodotLogEvent> Collect(GodotLogTail t)
    {
        var l = new List<GodotLogEvent>();
        t.EventReceived += (_, e) => l.Add(e);
        return l;
    }

    [Fact]
    public void Echantillon_reel_depuis_le_debut()
    {
        using var t = new GodotLogTail(TestPaths.LogSample, fromStart: true);
        var ev = Collect(t);
        t.Poll();
        // Comptes verifies par grep sur fixtures/saves/godot.sample.txt (voir SCHEMA.md)
        Assert.Equal(CountLines("Preloading '[A-Za-z]+ Room' assets\\.\\.\\."), ev.OfType<RoomEntered>().Count());
        Assert.Equal(CountLines("Creating NCombatRoom"), ev.OfType<CombatRoomCreated>().Count());
        Assert.Equal(CountLines("Card rarity: Rolled"), ev.OfType<CardRewardRolled>().Count());
        Assert.Equal(CountLines("selected card reward"), ev.OfType<CardRewardSelected>().Count());
        Assert.Equal(CountLines("Player vote changed"), ev.OfType<MapVote>().Count());
        Assert.Equal(CountLines("Wrote [0-9]+ bytes to path="), ev.OfType<SaveWritten>().Count());
        Assert.True(ev.OfType<RoomEntered>().Count() >= 5);
        Assert.True(ev.OfType<MapVote>().Count() >= 4);
        Assert.Contains(ev.OfType<CombatRoomCreated>(), c => c.EncounterId == "ENCOUNTER.VANTOM_BOSS");
    }

    private static int CountLines(string pattern) =>
        File.ReadLines(TestPaths.LogSample).Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, pattern));

    [Fact]
    public void Par_defaut_ignore_l_existant_puis_suit_les_ajouts_et_lignes_partielles()
    {
        using var d = new TempDir();
        var log = d.File("godot.log");
        File.WriteAllText(log, "[INFO] Player 1 selected card reward\n");
        using var t = new GodotLogTail(log);
        var ev = Collect(t);
        Assert.Equal(0, t.Poll());

        File.AppendAllText(log, "[INFO] Player 2 selected card rew");
        Assert.Equal(0, t.Poll()); // ligne incomplete gardee en tampon
        File.AppendAllText(log, "ard\n[INFO] Preloading 'Event Room' assets... count=2 vfx=0\n");
        Assert.Equal(2, t.Poll());
        Assert.Equal(2UL, Assert.IsType<CardRewardSelected>(ev[0]).PlayerNetId);
        Assert.IsType<RoomEntered>(ev[1]);
    }

    [Fact]
    public void Rotation_comme_au_demarrage_du_jeu()
    {
        using var d = new TempDir();
        var log = d.File("godot.log");
        File.WriteAllText(log, new string('x', 5000) + "\n");
        using var t = new GodotLogTail(log);
        var ev = Collect(t);
        t.Poll();
        // le jeu renomme l'ancien log et en cree un nouveau
        File.Move(log, d.File("godot2026-10-27T21.32.33.log"));
        File.WriteAllText(log, "[INFO] Continuing run with character: CHARACTER.SILENT\n");
        t.Poll();
        var e = Assert.Single(ev);
        Assert.Equal("CHARACTER.SILENT", Assert.IsType<RunContinued>(e).CharacterId);
    }

    [Fact]
    public void Fichier_absent_puis_cree()
    {
        using var d = new TempDir();
        var log = d.File("godot.log");
        using var t = new GodotLogTail(log, fromStart: true);
        var ev = Collect(t);
        Assert.Equal(0, t.Poll());
        File.WriteAllText(log, "[INFO] Player 1 selected card reward\n");
        Assert.Equal(1, t.Poll());
    }
}

public class SaveWatcherTests
{
    private static string FixtureText() => File.ReadAllText(Path.Combine(TestPaths.FixtureSaves, "current_run.save"));

    [Fact]
    public void Publie_apres_ecriture_tmp_puis_renommage_et_a_la_suppression()
    {
        using var d = new TempDir();
        var p = ProfilePaths.For(d.Path, "1", 1);
        Directory.CreateDirectory(p.SavesDir);
        var q = new BlockingCollection<RunStateChangedEventArgs>();
        using var w = new SaveWatcher(p, TimeSpan.FromMilliseconds(100));
        w.RunStateChanged += (_, e) => q.Add(e);
        var errors = new ConcurrentQueue<Exception>();
        w.Error += (_, e) => errors.Enqueue(e.Error);
        w.Start(emitInitial: true);

        Assert.True(q.TryTake(out var init, 2000));
        Assert.Null(init!.State);
        Assert.Equal("initial", init.Trigger);

        // ecriture facon jeu : .tmp puis rename (GodotFileIo.cs:110-121)
        var json = System.Text.Json.Nodes.JsonNode.Parse(FixtureText())!;
        json["players"]![0]!["gold"] = 123;
        File.WriteAllText(Path.Combine(p.SavesDir, "current_run.save.tmp"), json.ToJsonString());
        File.Move(Path.Combine(p.SavesDir, "current_run.save.tmp"), p.CurrentRunSave, overwrite: true);

        Assert.True(q.TryTake(out var e1, 5000), "aucun evenement apres renommage");
        Assert.Equal(123, e1!.State!.Players[0].Gold);
        Assert.StartsWith("renamed:", e1.Trigger);

        // rafale identique => pas de doublon
        File.SetLastWriteTimeUtc(p.CurrentRunSave, DateTime.UtcNow);
        Assert.False(q.TryTake(out _, 600));

        // fin de run : le jeu supprime la save (RunSaveManager.cs:185)
        File.Delete(p.CurrentRunSave);
        Assert.True(q.TryTake(out var e2, 5000), "aucun evenement apres suppression");
        Assert.Null(e2!.State);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("current_run.save", true)]
    [InlineData("current_run_mp.save", true)]
    [InlineData("current_run.save.backup", true)]
    [InlineData("current_run.save.tmp", false)]
    [InlineData("progress.save", false)]
    [InlineData(null, false)]
    public void Filtre_des_noms(string? name, bool expected) => Assert.Equal(expected, SaveWatcher.IsRelevant(name));
}
