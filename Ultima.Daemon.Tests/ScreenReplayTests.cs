using Ultima.Daemon.Mirror;
using Ultima.Daemon.Publish;
using Ultima.Daemon.Screens;
using Ultima.Daemon.State;
using Ultima.Daemon.Knowledge;
using Ultima.Engine.Contracts;
using Ultima.Saves.Log;

namespace Ultima.Daemon.Tests;

/// <summary>
/// Machine à états des écrans REJOUÉE sur des logs réels de la fenêtre de test du 2026-10-29
/// (anonymisés par Fixtures/make_fixtures.py).
/// </summary>
public class ScreenReplayTests
{
    private const string Solo = "godot-solo-2026-10-29.log";
    private const string Coop = "godot-coop-2026-10-29.events.log";

    private static Dictionary<int, Screen> ScreenAfterEachLine(string file, ulong? local, out ScreenTracker t)
    {
        t = new ScreenTracker { LocalNetId = local };
        t.OnGameRunning(true);
        var map = new Dictionary<int, Screen>();
        foreach (var (line, e) in Fixtures.Events(file))
        {
            t.OnLog(e);
            map[line] = t.Screen;
        }
        return map;
    }

    [Fact]
    public void Solo_session_follows_the_real_screens()
    {
        var s = ScreenAfterEachLine(Solo, local: 1, out _);
        // numéros de ligne du log réel (13:07, Défaut puis Nécrolieuse) — voir le fichier
        Assert.Equal(Screen.Event, s[283]);   // Preloading 'Event Room' (ancien)
        Assert.Equal(Screen.Map, s[311]);     // Player vote changed for 1 → (3,1)
        Assert.Equal(Screen.Combat, s[317]);  // Preloading 'Combat Room'
        Assert.Equal(Screen.Combat, s[319]);  // Creating NCombatRoom … SHRINKER_BEETLE_WEAK
        Assert.Equal(Screen.Reward, s[405]);  // 1er tirage de rareté après le combat
        Assert.Equal(Screen.Reward, s[407]);
        Assert.Equal(Screen.Map, s[423]);     // Player 1 selected card reward
        Assert.Equal(Screen.Event, s[473]);
        Assert.Equal(Screen.Map, s[485]);     // Continuing run with character: CHARACTER.DEFECT
        Assert.Equal(Screen.Event, s[577]);
        Assert.Equal(Screen.Event, s[581]);   // récompense d'événement sans tirage : reste Event
        Assert.Equal(Screen.Map, s[589]);
        Assert.Equal(Screen.Combat, s[597]);  // SLIMES_WEAK
        Assert.Equal(Screen.Reward, s[632]);
        Assert.Equal(Screen.Map, s[637]);
        Assert.Equal(Screen.Map, s[639]);     // vote (4,2)
        Assert.Equal(Screen.Map, s[644]);     // tirages du MARCHAND depuis la carte : pas une récompense
        Assert.Equal(Screen.Map, s[648]);
        Assert.Equal(Screen.Shop, s[650]);    // Preloading 'Merchant Room'
    }

    [Fact]
    public void Coop_only_the_local_player_moves_the_screen()
    {
        // 4 joueurs ; Mio = 70000000000000001. Les votes/choix des autres ne doivent pas changer l'écran.
        var t = new ScreenTracker { LocalNetId = 70000000000000001 };
        t.OnGameRunning(true);
        int foreignWhileReward = 0, rewards = 0;
        foreach (var (_, e) in Fixtures.Events(Coop))
        {
            var before = t.Screen;
            t.OnLog(e);
            if (before == Screen.Reward && e is CardRewardSelected sel && sel.PlayerNetId != 70000000000000001)
            {
                foreignWhileReward++;
                Assert.Equal(Screen.Reward, t.Screen);
            }
            if (before != Screen.Reward && t.Screen == Screen.Reward) rewards++;
        }
        Assert.True(foreignWhileReward > 0, "le log co-op contient des choix d'autres joueurs pendant notre récompense");
        Assert.True(rewards > 5);
    }

    [Fact]
    public void Rarity_rolls_from_the_map_never_open_a_reward()
    {
        var t = new ScreenTracker();
        t.OnGameRunning(true);
        t.OnLog(new RunContinued(default, "", "CHARACTER.NECROBINDER"));
        var need = t.OnLog(new CardRewardRolled(default, ""));
        Assert.Equal(MirrorNeed.None, need);
        Assert.Equal(Screen.Map, t.Screen);
    }

    [Fact]
    public void Game_start_stop_and_run_end()
    {
        var t = new ScreenTracker();
        Assert.Equal(Screen.Unknown, t.Screen);
        t.OnGameRunning(true);
        Assert.Equal(Screen.Menu, t.Screen);
        t.OnLog(new RoomEntered(default, "", RoomKind.Combat, "Combat Room"));
        t.OnRun(null, gameRunning: true);
        Assert.Equal(Screen.Menu, t.Screen);
        t.OnGameRunning(false);
        Assert.Equal(Screen.Unknown, t.Screen);
    }

    /// <summary>
    /// Rejeu COMPLET du log solo à travers le cœur du daemon avec un faux miroir et une horloge virtuelle
    /// (1 s entre événements, 1 ms dans une rafale de tirages) : le miroir n'est appelé QUE sur transitions.
    /// </summary>
    [Fact]
    public void Mirror_is_called_only_on_transitions_never_in_combat()
    {
        var timers = new ManualTimers();
        var mirror = new FakeMirror();
        var published = new List<EngineState>();
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), mirror, timers,
            a => a(), published.Add);
        core.OnGame(true);
        Assert.Equal(1, mirror.Layouts); // vérification de structure au lancement du jeu (fichier, pas mémoire)

        var t = TimeSpan.Zero;
        GodotLogEvent? prev = null;
        foreach (var (_, e) in Fixtures.Events(Solo))
        {
            t += e is CardRewardRolled && prev is CardRewardRolled ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromSeconds(1);
            timers.AdvanceTo(t);
            core.OnLog(e);
            prev = e;
        }
        timers.Advance(TimeSpan.FromSeconds(10));

        // 2 écrans de récompense (2×3 tirages → 2 lectures grâce à l'anti-rebond) + 1 marchand
        Assert.Equal(new[] { MirrorNeed.Reward, MirrorNeed.Reward, MirrorNeed.Shop }, mirror.Probes);
        Assert.Equal(3, core.MirrorProbes);
        Assert.DoesNotContain(core.ProbeLog, p => p.Screen == Screen.Combat);
        Assert.Equal(Screen.Shop, core.Screen);
        Assert.NotNull(core.LastState!.Shop);
        Assert.Equal(3, core.LastState.Shop!.Items.Count);

        // Au repos : aucune lecture, aucun minuteur armé.
        int before = mirror.Probes.Count;
        timers.Advance(TimeSpan.FromHours(1));
        Assert.Equal(before, mirror.Probes.Count);
        Assert.Equal(0, timers.Armed);
    }

    [Fact]
    public void Reward_offer_is_published_then_cleared_after_selection()
    {
        var timers = new ManualTimers();
        var mirror = new FakeMirror();
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), mirror, timers, a => a(), _ => { });
        core.OnGame(true);
        core.OnLog(new RoomEntered(default, "", RoomKind.Combat, "Combat Room"));
        for (int i = 0; i < 3; i++) core.OnLog(new CardRewardRolled(default, ""));
        Assert.Empty(mirror.Probes);          // anti-rebond : rien avant 250 ms
        timers.Advance(TimeSpan.FromMilliseconds(249));
        Assert.Empty(mirror.Probes);
        timers.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Single(mirror.Probes);
        var st = core.LastState!;
        Assert.Equal(Screen.Reward, st.Screen);
        Assert.Equal(new[] { "CARD.A", "CARD.B", "CARD.C" }, st.Reward!.Offered.Select(c => c.Id));
        Assert.False(st.Reward.AlreadyTaken);
        core.OnLog(new CardRewardSelected(default, "", 1));
        Assert.Equal(Screen.Map, core.LastState!.Screen);
        Assert.Null(core.LastState.Reward);
    }

    [Fact]
    public void Combat_starting_during_debounce_cancels_the_read()
    {
        var timers = new ManualTimers();
        var mirror = new FakeMirror();
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), mirror, timers, a => a(), _ => { });
        core.OnGame(true);
        core.OnLog(new RoomEntered(default, "", RoomKind.Merchant, "Merchant Room"));
        core.OnLog(new RoomEntered(default, "", RoomKind.Combat, "Combat Room"));
        timers.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(mirror.Probes);
    }

    [Fact]
    public void No_mirror_when_game_closed_or_disabled()
    {
        var timers = new ManualTimers();
        var mirror = new FakeMirror();
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), mirror, timers, a => a(), _ => { });
        core.OnLog(new RoomEntered(default, "", RoomKind.Merchant, "Merchant Room")); // jeu « fermé »
        timers.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(mirror.Probes);
        Assert.Equal(0, mirror.Layouts);

        var core2 = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), null, timers, a => a(), _ => { });
        core2.OnGame(true);
        core2.OnLog(new RoomEntered(default, "", RoomKind.Merchant, "Merchant Room"));
        timers.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, core2.Scheduler.Refused);
        Assert.Equal(0, core2.MirrorProbes);
    }

    [Fact]
    public void Layout_drift_disables_the_mirror_and_is_announced()
    {
        var timers = new ManualTimers();
        var mirror = new FakeMirror { LayoutExit = ProbeExit.LayoutDrift };
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), mirror, timers, a => a(), _ => { });
        core.OnGame(true);
        Assert.Equal(DaemonCore.DegradedPatch, core.LastState!.Health.Degraded);
        core.OnLog(new RoomEntered(default, "", RoomKind.Merchant, "Merchant Room"));
        timers.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(mirror.Probes);
        Assert.Null(core.LastState!.Shop);
    }

    [Fact]
    public void Mirror_failures_map_to_degraded_messages_never_to_data()
    {
        var timers = new ManualTimers();
        var mirror = new FakeMirror
        {
            Respond = _ => new MirrorResult(ProbeExit.AttachFailed, 40, "x", new ProbeHealth(true, false, 1, 9, null, null),
                null, null, null, Array.Empty<string>()),
        };
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), mirror, timers, a => a(), _ => { });
        core.OnGame(true);
        core.OnLog(new RoomEntered(default, "", RoomKind.Merchant, "Merchant Room"));
        timers.Advance(TimeSpan.FromSeconds(1));
        var st = core.LastState!;
        Assert.Contains("ptrace_scope=1", st.Health.Degraded);
        Assert.False(st.Health.MirrorOk);
        Assert.Equal(40, st.Health.MirrorLatencyMs);
        Assert.Null(st.Shop);
    }
}
