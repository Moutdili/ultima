using Ultima.Saves.IO;
using Ultima.Saves.Model;

namespace Ultima.Saves.Tests;

public class ProfileLocatorTests
{
    [Fact]
    public void Locate_lit_profile_save_et_reste_sur_le_profil_vanilla()
    {
        var p = ProfileLocator.Locate(TestPaths.FixtureRoot);
        Assert.Equal(TestPaths.FakeSteamId, p.SteamId);
        Assert.Equal(70000000000000001UL, p.SteamId64);
        Assert.Equal(1, p.ProfileId);
        Assert.False(p.ProfileIdFromDefault);
        Assert.Equal(Path.Combine(TestPaths.FixtureAccount, "profile1", "saves", "current_run.save"), p.CurrentRunSave);
        Assert.DoesNotContain("modded", p.SavesDir);
        Assert.Equal(Path.Combine(TestPaths.FixtureRoot, "logs", "godot.log"), p.GodotLog);
    }

    [Fact]
    public void Le_leurre_du_profil_modded_n_est_jamais_lu()
    {
        // fixtures : modded/profile1 contient un leurre gold=999, hp=1 (voir anonymize.py)
        var p = ProfileLocator.Locate(TestPaths.FixtureRoot);
        var s = new RunSaveReader().ReadCurrent(p)!;
        Assert.Equal(99, s.Players[0].Gold);
        Assert.Equal(70, s.Players[0].CurrentHp);
        Assert.DoesNotContain("modded", s.SourcePath);
    }

    [Fact]
    public void Profil_actif_2_et_profile_save_absent()
    {
        using var t = new TempDir();
        var acc = Path.Combine(t.Path, "steam", "123");
        Directory.CreateDirectory(acc);
        var p0 = ProfileLocator.Locate(t.Path);
        Assert.Equal(1, p0.ProfileId);
        Assert.True(p0.ProfileIdFromDefault);

        File.WriteAllText(Path.Combine(acc, "profile.save"), "{\"last_profile_id\": 2, \"schema_version\": 2}");
        var p = ProfileLocator.Locate(t.Path);
        Assert.Equal(2, p.ProfileId);
        Assert.EndsWith(Path.Combine("steam", "123", "profile2", "saves"), p.SavesDir);
    }

    [Fact]
    public void Garde_fou_modded()
    {
        Assert.Throws<InvalidOperationException>(() => ProfileLocator.AssertNotModded("/x/steam/1/modded/profile1"));
        ProfileLocator.AssertNotModded("/x/steam/1/profile1");
    }

    [Fact]
    public void Aucun_compte_leve()
    {
        using var t = new TempDir();
        Assert.Throws<DirectoryNotFoundException>(() => ProfileLocator.Locate(t.Path));
    }
}

public class RunSaveReaderTests
{
    private static RunState Fixture() =>
        new RunSaveReader().Read(Path.Combine(TestPaths.FixtureSaves, "current_run.save"));

    [Fact]
    public void Joueur_PV_or_energie_potions()
    {
        var s = Fixture();
        Assert.Equal(16, s.SchemaVersion);
        Assert.Equal(0, s.Ascension);
        Assert.Equal("standard", s.GameMode);
        var p = Assert.Single(s.Players);
        Assert.Same(p, s.LocalPlayer(null));
        Assert.Equal(1UL, p.NetId);
        Assert.Equal("CHARACTER.SILENT", p.CharacterId);
        Assert.Equal((70, 70, 99, 3, 3), (p.CurrentHp, p.MaxHp, p.Gold, p.MaxEnergy, p.MaxPotionSlotCount));
        Assert.Empty(p.Potions); // aucune potion dans la save disponible (liste omise par le jeu)
    }

    [Fact]
    public void Deck_et_reliques()
    {
        var p = Fixture().Players[0];
        Assert.Equal(14, p.Deck.Count);
        Assert.Equal(5, p.Deck.Count(c => c.Id == "CARD.STRIKE_SILENT"));
        Assert.Equal(5, p.Deck.Count(c => c.Id == "CARD.DEFEND_SILENT"));
        Assert.Contains(p.Deck, c => c.Id == "CARD.GRAVEBLAST" && c.FloorAdded == 1);
        Assert.All(p.Deck, c => Assert.False(c.IsUpgraded));
        Assert.Equal(["RELIC.RING_OF_THE_SNAKE", "RELIC.KALEIDOSCOPE"], p.Relics.Select(r => r.Id));
    }

    [Fact]
    public void Acte_boss_position()
    {
        var s = Fixture();
        Assert.Equal(0, s.CurrentActIndex);
        Assert.Equal("ACT.UNDERDOCKS", s.CurrentAct!.Id);
        Assert.Equal("ENCOUNTER.WATERFALL_GIANT_BOSS", s.CurrentAct.BossId);
        Assert.Equal("EVENT.NEOW", s.CurrentAct.AncientId);
        Assert.Equal([new MapCoord(3, 0), new MapCoord(4, 1)], s.VisitedMapCoords);
        Assert.Equal(new MapCoord(4, 1), s.CurrentCoord);
    }

    [Fact]
    public void Actes_futurs_masques()
    {
        var s = Fixture();
        Assert.Equal(3, s.Acts.Count);
        foreach (var a in s.Acts.Skip(1))
        {
            Assert.False(a.IsRevealed);
            Assert.Null(a.Id);
            Assert.Null(a.BossId);
            Assert.Null(a.AncientId);
            Assert.Null(a.Map);
        }
        Assert.True(s.Acts[0].IsRevealed);
    }

    [Fact]
    public void Carte_saved_map()
    {
        var m = Fixture().CurrentAct!.Map!;
        Assert.Equal((7, 16), (m.Width, m.Height));
        Assert.Equal(60, m.Points.Count);
        Assert.Equal(MapPointKind.Ancient, m.Start.Kind);
        Assert.Equal(new MapCoord(3, 0), m.Start.Coord);
        Assert.Equal([new MapCoord(0, 1), new MapCoord(2, 1), new MapCoord(4, 1), new MapCoord(6, 1)], m.Start.Children);
        Assert.Equal(MapPointKind.Boss, m.Boss.Kind);
        Assert.Equal(new MapCoord(3, 16), m.Boss.Coord);
        Assert.Null(m.SecondBoss);
        var counts = m.Points.GroupBy(p => p.Kind).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(26, counts[MapPointKind.Monster]);
        Assert.Equal(12, counts[MapPointKind.Unknown]);
        Assert.Equal(11, counts[MapPointKind.RestSite]);
        Assert.Equal(5, counts[MapPointKind.Elite]);
        Assert.Equal(3, counts[MapPointKind.Treasure]);
        Assert.Equal(3, counts[MapPointKind.Shop]);
        Assert.DoesNotContain(m.Points, p => p.Kind == MapPointKind.Unrecognized);
        Assert.Equal([new MapCoord(4, 2)], m.ByCoord[new MapCoord(4, 1)].Children);
        // toutes les cases de la rangee 15 menent au boss
        Assert.All(m.Points.Where(p => p.Coord.Row == 15), p => Assert.Equal([m.Boss.Coord], p.Children));
        // tous les enfants existent dans le graphe
        Assert.All(m.Points.SelectMany(p => p.Children), c => Assert.True(m.ByCoord.ContainsKey(c), c.ToString()));
    }

    [Fact]
    public void Historique_par_etage()
    {
        var f = Assert.Single(Fixture().Floors);
        Assert.Equal((1, 0, MapPointKind.Ancient), (f.Floor, f.ActIndex, f.Kind));
        Assert.Equal("EVENT.NEOW", f.ModelId);
        var ps = Assert.Single(f.PlayerStats);
        Assert.Equal(6, ps.CardChoices.Count);
        Assert.Equal(["CARD.GRAVEBLAST", "CARD.THUNDERCLAP"], ps.CardsPicked.Select(c => c.Id));
        Assert.Equal(["RELIC.KALEIDOSCOPE"], ps.RelicsPicked);
        Assert.Equal(3, ps.AncientChoices.Count);
        Assert.Equal(0, ps.DamageTaken);
    }

    [Fact]
    public void Absent_donne_null()
    {
        using var t = new TempDir();
        Assert.Null(new RunSaveReader().TryRead(t.File("current_run.save")));
        Assert.Null(new RunSaveReader().ReadCurrent(ProfilePaths.For(t.Path, "1", 1)));
    }

    private static string FixtureText() => File.ReadAllText(Path.Combine(TestPaths.FixtureSaves, "current_run.save"));

    [Fact]
    public void JSON_invalide_repli_sur_backup()
    {
        using var t = new TempDir();
        File.WriteAllText(t.File("current_run.save"), "{\"players\": [ {\"gold\": 1");
        File.WriteAllText(t.File("current_run.save.backup"), FixtureText());
        var s = new RunSaveReader(new RobustReadOptions { RetryDelay = TimeSpan.FromMilliseconds(1) })
            .Read(t.File("current_run.save"));
        Assert.True(s.ReadFromBackup);
        Assert.EndsWith(".backup", s.SourcePath);
        Assert.Equal(99, s.Players[0].Gold);
    }

    [Fact]
    public void Principal_absent_backup_present()
    {
        using var t = new TempDir();
        File.WriteAllText(t.File("current_run.save.backup"), FixtureText());
        var s = new RunSaveReader().Read(t.File("current_run.save"));
        Assert.True(s.ReadFromBackup);
    }

    [Fact]
    public void Save_sans_joueur_consideree_corrompue()
    {
        using var t = new TempDir();
        File.WriteAllText(t.File("current_run.save"), "{\"schema_version\":16,\"players\":[]}");
        File.WriteAllText(t.File("current_run.save.backup"), FixtureText());
        Assert.True(new RunSaveReader(new RobustReadOptions { RetryDelay = TimeSpan.Zero }).Read(t.File("current_run.save")).ReadFromBackup);
    }

    [Fact]
    public void Les_deux_illisibles_leve_SaveReadException()
    {
        using var t = new TempDir();
        File.WriteAllText(t.File("current_run.save"), "pas du json");
        File.WriteAllText(t.File("current_run.save.backup"), "");
        Assert.Throws<SaveReadException>(() =>
            new RunSaveReader(new RobustReadOptions { RetryDelay = TimeSpan.Zero }).Read(t.File("current_run.save")));
    }

    [Fact]
    public async Task JSON_invalide_transitoire_relecture()
    {
        using var t = new TempDir();
        File.WriteAllText(t.File("current_run.save"), "{ tronque");
        // pas de .backup : seule la relecture peut reussir
        var fix = Task.Run(async () =>
        {
            await Task.Delay(40);
            t.WriteLikeGame("current_run.save", FixtureText());
        });
        var s = new RunSaveReader(new RobustReadOptions { Attempts = 10, RetryDelay = TimeSpan.FromMilliseconds(100) })
            .Read(t.File("current_run.save"));
        await fix;   // attente non bloquante (xUnit1031) : le runner gère l'async
        Assert.False(s.ReadFromBackup);
        Assert.Equal(14, s.Players[0].Deck.Count);
    }

    [Fact]
    public void Coop_mp_plus_recent_choisi_et_joueur_local()
    {
        using var t = new TempDir();
        var p = ProfilePaths.For(t.Path, "70000000000000009", 1);
        Directory.CreateDirectory(p.SavesDir);
        var solo = System.Text.Json.Nodes.JsonNode.Parse(FixtureText())!;
        File.WriteAllText(p.CurrentRunSave, solo.ToJsonString());

        var mp = System.Text.Json.Nodes.JsonNode.Parse(FixtureText())!;
        mp["save_time"] = solo["save_time"]!.GetValue<long>() + 10;
        var me = mp["players"]![0]!.DeepClone();
        me["net_id"] = 70000000000000009UL;
        me["gold"] = 7;
        var other = mp["players"]![0]!.DeepClone();
        other["net_id"] = 70000000000000002UL;
        mp["players"] = new System.Text.Json.Nodes.JsonArray(other, me);
        File.WriteAllText(p.CurrentRunMpSave, mp.ToJsonString());

        var s = new RunSaveReader().ReadCurrent(p)!;
        Assert.True(s.IsMultiplayerSave);
        Assert.Equal(2, s.Players.Count);
        Assert.Equal(7, s.LocalPlayer(p.SteamId64)!.Gold);
        Assert.Null(s.LocalPlayer(null));
    }
}
