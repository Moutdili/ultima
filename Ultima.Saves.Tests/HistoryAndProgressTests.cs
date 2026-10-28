using Ultima.Saves.Model;

namespace Ultima.Saves.Tests;

public class HistoryReaderTests
{
    private static RunRecord Run(string name) => new HistoryReader().Read(Path.Combine(TestPaths.FixtureHistory, name));

    [Fact]
    public void Enumere_les_runs_hors_backup()
    {
        var files = HistoryReader.EnumerateRunFiles(TestPaths.FixtureHistory);
        Assert.Equal(7, files.Count);
        Assert.All(files, f => Assert.EndsWith(".run", f));
        Assert.Equal(7, new HistoryReader().ReadAll(TestPaths.FixtureHistory).Count);
    }

    [Fact]
    public void Victoire_solo()
    {
        var r = Run("win_solo.run");
        Assert.Equal(("v0.107.1", 9, true, false, 1), (r.BuildId, r.SchemaVersion, r.Win, r.WasAbandoned, r.Ascension));
        Assert.Null(r.KilledByEncounter);
        Assert.Null(r.KilledByEvent);
        Assert.Equal("CHARACTER.SILENT", r.Character());
        Assert.Equal(48, r.Floors.Count);
        var p = r.LocalPlayer(null)!;
        Assert.Equal(40, p.Deck.Count);
        Assert.Equal(7, p.Deck.Count(c => c.IsUpgraded));
        Assert.Equal(3, p.Deck.Count(c => c.Enchantment is not null));
        Assert.Equal(18, p.Relics.Count);
        Assert.Equal(MapPointKind.Boss, r.Floors[^1].Kind);
    }

    [Fact]
    public void Mort_solo_cause_potions_degats()
    {
        var r = Run("death_solo.run");
        Assert.False(r.Win);
        Assert.False(r.WasAbandoned);
        Assert.Equal("ENCOUNTER.KNOWLEDGE_DEMON_BOSS", r.KilledByEncounter);
        var p = r.Players[0];
        Assert.Equal(["POTION.REGEN_POTION", "POTION.DEXTERITY_POTION", "POTION.POWER_POTION"], p.Potions.Select(x => x.Id));
        Assert.Equal([0, 1, 2], p.Potions.Select(x => x.SlotIndex));
        Assert.Equal(352, r.Floors.Sum(f => f.ForPlayer(p.Id)!.DamageTaken));
        Assert.Equal(35, r.Floors.Count);
        // numerotation globale des etages, actes enchaines
        Assert.Equal(Enumerable.Range(1, 35), r.Floors.Select(f => f.Floor));
        Assert.True(r.Floors.Select(f => f.ActIndex).SequenceEqual(r.Floors.Select(f => f.ActIndex).Order()));
    }

    [Fact]
    public void Etage_de_combat_rencontre_cartes_proposees()
    {
        var r = Run("death_solo.run");
        var combat = r.Floors.First(f => f.Kind == MapPointKind.Monster);
        var room = Assert.Single(combat.Rooms);
        Assert.Equal("monster", room.RoomType);
        Assert.StartsWith("ENCOUNTER.", room.ModelId);
        Assert.NotEmpty(room.MonsterIds);
        Assert.True(room.TurnsTaken > 0);
        var st = Assert.Single(combat.PlayerStats);
        Assert.Equal(3, st.CardChoices.Count);
        Assert.InRange(st.CardsPicked.Count(), 0, 1);
    }

    [Fact]
    public void Abandon_avec_rencontre_en_cours()
    {
        var r = Run("abandon_solo.run");
        Assert.True(r.WasAbandoned);
        Assert.Equal("ENCOUNTER.KNIGHTS_ELITE", r.KilledByEncounter); // observe : un abandon en combat garde la rencontre
    }

    [Fact]
    public void Coop_4_joueurs_joueur_local()
    {
        var r = Run("coop4.run");
        Assert.True(r.IsMultiplayer);
        Assert.Equal(4, r.Players.Count);
        Assert.Equal("v0.103.3", r.BuildId);
        Assert.Null(r.LocalPlayer(null));
        Assert.Equal("CHARACTER.SILENT", r.Character(70000000000000001UL));
        Assert.All(r.Floors, f => Assert.Equal(4, f.PlayerStats.Count));
        Assert.All(r.Floors, f => Assert.NotNull(f.ForPlayer(70000000000000001UL)));
    }

    [Fact]
    public void Mort_par_evenement_et_daily()
    {
        var e = Run("killed_by_event.run");
        Assert.Equal("EVENT.SLIPPERY_BRIDGE", e.KilledByEvent);
        Assert.Null(e.KilledByEncounter);
        var d = Run("daily_modifiers.run");
        Assert.Equal("daily", d.GameMode);
        Assert.Equal(["MODIFIER.VINTAGE", "MODIFIER.INSANITY", "MODIFIER.NIGHT_TERRORS"], d.Modifiers);
        Assert.Equal("CHARACTER.DEFECT", d.Character());
    }

    [Fact]
    public void Ancien_build()
    {
        var r = Run("old_build.run");
        Assert.Equal("v0.103.3", r.BuildId);
        Assert.Equal("ENCOUNTER.INKLETS_NORMAL", r.KilledByEncounter);
        Assert.Equal("CHARACTER.IRONCLAD", r.Character());
    }
}

public class ProgressReaderTests
{
    [Fact]
    public void Stats_par_personnage()
    {
        var st = new ProgressReader().TryRead(ProfileLocator.Locate(TestPaths.FixtureRoot))!;
        Assert.Equal(21, st.SchemaVersion);
        Assert.Equal(4684, st.FloorsClimbed);
        Assert.Equal(6, st.Characters.Count);
        var silent = st.Characters.Single(c => c.Id == "CHARACTER.SILENT");
        Assert.Equal((14, 102, 4), (silent.TotalWins, silent.TotalLosses, silent.MaxAscension));
        Assert.Equal(489, st.Cards.Count);
        Assert.Equal(84, st.Encounters.Count);
        Assert.NotEmpty(st.Ancients);
        Assert.All(st.Encounters, e => Assert.StartsWith("ENCOUNTER.", e.EncounterId));
    }

    [Fact]
    public void Absent_donne_null()
    {
        using var t = new TempDir();
        Assert.Null(new ProgressReader().TryRead(t.File("progress.save")));
    }
}
