using Ultima.Daemon.Knowledge;
using Ultima.Daemon.Mirror;
using Ultima.Daemon.Screens;
using Ultima.Daemon.Settings;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;
using Ultima.Saves;
using Ultima.Saves.Log;
using Ultima.Saves.Model;

namespace Ultima.Daemon.Tests;

public class AssemblyTests
{
    private static RunState FixtureRun() =>
        new RunSaveReader().ReadCurrent(ProfileLocator.Locate(Path.Combine(Fixtures.RepoRoot(), "fixtures", "saves", "root")))!;

    /// <summary>Calcul qui lève partout (Hermes en panne).</summary>
    private sealed class ThrowingScorer : IScorer
    {
        public Score ScoreCard(CardInstance card, ScoringContext ctx) => throw new InvalidOperationException("boom");
        public Score ScoreSkip(IReadOnlyList<CardInstance> offered, ScoringContext ctx) => throw new InvalidOperationException();
        public Score ScoreRelic(string relicId, ScoringContext ctx) => throw new InvalidOperationException();
        public Score ScoreShopItem(string kind, string id, int price, ScoringContext ctx) => throw new InvalidOperationException();
        public Score ScoreRemoval(int price, ScoringContext ctx) => throw new InvalidOperationException();
        public DeckEvaluation EvaluateDeck(ScoringContext ctx) => throw new InvalidOperationException();
    }

    /// <summary>Calcul « stub vide » : termes vides.</summary>
    private sealed class EmptyScorer : IScorer
    {
        private static Score E => new(0, Array.Empty<ScoreTerm>(), "low");
        public Score ScoreCard(CardInstance card, ScoringContext ctx) => E;
        public Score ScoreSkip(IReadOnlyList<CardInstance> offered, ScoringContext ctx) => E;
        public Score ScoreRelic(string relicId, ScoringContext ctx) => E;
        public Score ScoreShopItem(string kind, string id, int price, ScoringContext ctx) => E;
        public Score ScoreRemoval(int price, ScoringContext ctx) => E;
        public DeckEvaluation EvaluateDeck(ScoringContext ctx) => new(E, null, Array.Empty<string>(), Array.Empty<CardView>());
    }

    private sealed class ThrowingPaths : IPathAnalyzer
    {
        public PathsView Analyze(ActMap map, MapCoord position, int actIndex, string characterId, int hp) => throw new StackOverflowExceptionLike();
        private sealed class StackOverflowExceptionLike : Exception;
    }

    private sealed class FrKnowledge : IKnowledge
    {
        public CardInfo? Card(string id) => id == "CARD.STRIKE_SILENT"
            ? new CardInfo(id, "StrikeSilent", "Frappe", "Inflige 6 dégâts.", "Attack", "Basic", 1, "SILENT", false, Array.Empty<string>())
            : null;
        public RelicInfo? Relic(string id) => null;
        public EnchantmentInfo? Enchantment(string id) => id == "ENCHANTMENT.SWIFT"
            ? new EnchantmentInfo(id, "Swift", "Célérité", "Piochez 1 carte…") : null;
        public PotionInfo? Potion(string id) => null;
        public string? EncounterName(string id) => null;
        public string? CharacterName(string id) => id == "CHARACTER.SILENT" ? "Silencieuse" : null;
    }

    [Fact]
    public void Calculation_exceptions_become_null_fields_never_a_crash()
    {
        var asm = new StateAssembler(NullKnowledge.Instance, new ThrowingScorer(), new ThrowingPaths(), null);
        var part = asm.BuildRun(FixtureRun())!;
        Assert.All(part.View.Deck, c => Assert.Null(c.Score));
        Assert.All(part.View.Relics, r => Assert.Null(r.Score));
        Assert.Null(part.Paths);
        Assert.NotNull(part.Deck);
        Assert.Null(part.Deck!.Score);
        var reward = asm.BuildReward(FakeMirror.Rewards("CARD.A"), part)!;
        Assert.Null(reward.SkipScore);
        var shop = asm.BuildShop(FakeMirror.Shop(), part, 50)!;
        Assert.All(shop.Items, i => Assert.Null(i.Score));
        Assert.Null(shop.RemovalScore);
    }

    [Fact]
    public void Empty_stub_scores_are_published_as_null_not_zero()
    {
        var asm = new StateAssembler(NullKnowledge.Instance, new EmptyScorer(), null, null);
        var part = asm.BuildRun(FixtureRun())!;
        Assert.All(part.View.Deck, c => Assert.Null(c.Score));
        Assert.Null(part.Deck!.Score);
    }

    [Fact]
    public void Names_come_from_knowledge_with_id_fallback_and_curses_enchantments_are_filled()
    {
        var run = FixtureRun();
        var me = run.Players[0];
        // deck enrichi : une malédiction et une carte enchantée (formes de la save réelle)
        var deck = me.Deck.Concat(new[]
        {
            new CardInstance("CARD.ASCENDERS_BANE", 0, null, 0),
            new CardInstance("CARD.STRIKE_SILENT", 1, new Enchantment("ENCHANTMENT.SWIFT", 1), 2),
        }).ToArray();
        run = run with { Players = new[] { me with { Deck = deck } } };

        var part = new StateAssembler(new FrKnowledge(), null, null, null).BuildRun(run)!;
        Assert.Equal("Silencieuse", part.View.CharacterName);
        var strike = part.View.Deck.First(c => c.Id == "CARD.STRIKE_SILENT");
        Assert.Equal("Frappe", strike.Name);
        Assert.Equal("Inflige 6 dégâts.", strike.Description);
        var other = part.View.Deck.First(c => c.Id != "CARD.STRIKE_SILENT" && !c.IsCurse);
        Assert.Equal(other.Id, other.Name); // inconnu de la connaissance → nom = identifiant
        Assert.Equal(-1, other.Cost);
        var enchanted = part.View.Deck.Single(c => c.Enchantment is not null);
        Assert.Equal("Célérité", enchanted.Enchantment!.Name);
        Assert.Equal(1, enchanted.Enchantment.Amount);
        Assert.Equal(1, enchanted.Upgrade);
        var curse = Assert.Single(part.Deck!.Curses);
        Assert.Equal("CARD.ASCENDERS_BANE", curse.Id);
        Assert.True(curse.IsCurse);
    }

    [Fact]
    public void Shop_prices_use_displayed_estimate_and_affordability()
    {
        var asm = new StateAssembler(NullKnowledge.Instance, null, null, null);
        var part = asm.BuildRun(FixtureRun());
        var shop = asm.BuildShop(FakeMirror.Shop(), part, memGold: 50)!;
        var scourge = shop.Items.Single(i => i.Id == "CARD.SCOURGE");
        Assert.Equal(24, scourge.Price);
        Assert.True(scourge.OnSale);
        Assert.True(scourge.Affordable);
        Assert.False(shop.Items.Single(i => i.Kind == "relic").Affordable);
        Assert.Equal(75, shop.RemovalPrice);
        Assert.False(shop.RemovalUsed);
    }

    [Fact]
    public void Cross_check_divergence_degrades_and_drops_the_shop()
    {
        var run = FixtureRun();
        var me = run.Players[0];
        var timers = new ManualTimers();
        var mirror = new FakeMirror
        {
            Respond = _ => new MirrorResult(0, 90, null, null,
                new MRun(true, 1, 0, 0, "MerchantRoom", new[]
                {
                    new MPlayer(me.NetId, me.CharacterId, me.CurrentHp, me.MaxHp, 0, me.Gold + 7, 3,
                        me.Deck.Select(c => new MCard(c.Id, c.UpgradeLevel)).ToArray(),
                        me.Relics.Select(r => (string?)r.Id).ToArray(), Array.Empty<string?>()),
                }),
                null, FakeMirror.Shop(), Array.Empty<string>()),
        };
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), mirror, timers, a => a(), _ => { });
        core.OnGame(true);
        core.OnRun(run);
        core.OnLog(new RoomEntered(default, "", RoomKind.Merchant, "Merchant Room"));
        timers.Advance(TimeSpan.FromSeconds(1));
        var st = core.LastState!;
        Assert.Contains("divergence save↔mémoire", st.Health.Degraded);
        Assert.Contains($"or {me.Gold + 7}≠{me.Gold}", st.Health.Degraded);
        Assert.Null(st.Shop);
        Assert.False(st.Health.MirrorOk);

        // accord → pas de dégradation, shop publié
        mirror.Respond = n => FakeMirror.Default(n) with
        {
            Run = new MRun(true, 1, 0, 0, "MerchantRoom", new[]
            {
                new MPlayer(me.NetId, me.CharacterId, me.CurrentHp, me.MaxHp, 0, me.Gold, 3,
                    me.Deck.Select(c => new MCard(c.Id, c.UpgradeLevel)).ToArray(),
                    me.Relics.Select(r => (string?)r.Id).ToArray(), Array.Empty<string?>()),
            }),
        };
        core.OnLog(new MapVote(default, "", 1, null, new MapCoord(1, 1), 1));
        core.OnLog(new RoomEntered(default, "", RoomKind.Merchant, "Merchant Room"));
        timers.Advance(TimeSpan.FromSeconds(1));
        st = core.LastState!;
        Assert.Null(st.Health.Degraded);
        Assert.NotNull(st.Shop);
        Assert.True(st.Health.MirrorOk);
    }

    /// <summary>
    /// Non-régression (trouvé par la preuve de bout en bout sur la cible de test, 2026-10-29) : la relecture
    /// de la save pendant la vérification croisée ne doit PAS relancer une lecture du marchand qui publierait
    /// l'inventaire sans vérification.
    /// </summary>
    [Fact]
    public void Divergence_with_save_reread_publishes_no_shop_and_does_not_reread_memory()
    {
        var run = FixtureRun();
        var me = run.Players[0];
        var timers = new ManualTimers();
        var mirror = new FakeMirror
        {
            Respond = n => FakeMirror.Default(n) with
            {
                Run = new MRun(true, 1, 0, 0, "MerchantRoom", new[]
                {
                    new MPlayer(me.NetId, me.CharacterId, 61, 70, 0, 200, 3, new[] { new MCard("CARD.X", 0) },
                        me.Relics.Select(_ => (string?)"RELIC.Y").ToArray(), Array.Empty<string?>()),
                }),
            },
        };
        var core = new DaemonCore(new StateAssembler(NullKnowledge.Instance, null, null, null), mirror, timers, a => a(), _ => { },
            rereadSave: () => run with { SaveTime = run.SaveTime + 1 });
        core.OnGame(true);
        core.OnRun(run);
        core.OnLog(new RoomEntered(default, "", RoomKind.Merchant, "Merchant Room"));
        timers.Advance(TimeSpan.FromSeconds(10));
        var st = core.LastState!;
        Assert.Single(mirror.Probes);
        Assert.Null(st.Shop);
        Assert.False(st.Health.MirrorOk);
        Assert.Contains("divergence save↔mémoire", st.Health.Degraded);
        Assert.Contains("reliques différentes", st.Health.Degraded);
    }

    [Fact]
    public void Settings_missing_invalid_and_normalized()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ultima-set-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            Assert.Same(OverlaySettings.Default, SettingsStore.TryLoad(path, out var e0));
            Assert.Null(e0);
            File.WriteAllText(path, "{ pas du json");
            Assert.Null(SettingsStore.TryLoad(path, out var e1));
            Assert.NotNull(e1);
            File.WriteAllText(path, """{"modules":[{"id":"curses","enabled":true,"order":0},{"id":"bogus","enabled":true,"order":1},{"id":"deck","enabled":false,"order":2},{"id":"curses","enabled":false,"order":3}]}""");
            var s = SettingsStore.TryLoad(path, out var e2)!;
            Assert.Null(e2);
            var defaults = OverlaySettings.Default.Modules.Select(m => m.Id).ToArray();
            // curses, deck (dans l'ordre du fichier) puis les modules manquants dans l'ordre par défaut
            Assert.Equal(new[] { "curses", "deck" }.Concat(defaults.Where(id => id is not "curses" and not "deck")),
                s.Modules.Select(m => m.Id));
            Assert.Equal(Enumerable.Range(0, defaults.Length), s.Modules.Select(m => m.Order));
            Assert.False(s.Modules[1].Enabled);
            Assert.True(s.Modules[0].Enabled); // le doublon désactivé est ignoré
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Probe_json_line_is_parsed()
    {
        // ligne réelle de `ultima-mirror probe --json` sur la cible de test Sts2Shape (raccourcie)
        const string line = """{"v":1,"cmd":"probe","health":{"gameRunning":true,"attached":true,"ptraceScope":0,"pid":649901,"attachMs":64,"runtime":"9.0"},"sections":{"reward":{"stable":true,"attempts":2,"ms":3,"error":null,"value":{"openRewardSets":1,"cardRewards":[{"alreadyTaken":false,"offered":[{"id":"CARD.BACKFLIP_TEST","upgrade":0},{"id":"CARD.PREPARED_TEST","upgrade":1}]}]}},"shop":{"stable":false,"attempts":4,"ms":11,"error":"x","value":null}},"compare":null,"exit":7,"ms":226,"error":"1 section(s) INCONNUE(s)"}""";
        var r = MirrorResult.Parse("bruit éventuel\n" + line + "\n", 7, 230);
        Assert.Equal(7, r.Exit);
        Assert.Equal(230, r.ElapsedMs);
        Assert.Equal(0, r.Health!.PtraceScope);
        Assert.Equal("CARD.PREPARED_TEST", r.Reward!.CardRewards[0].Offered[1].Id);
        Assert.Equal(1, r.Reward.CardRewards[0].Offered[1].Upgrade);
        Assert.Null(r.Shop);
        Assert.Equal(new[] { "shop" }, r.UnstableSections);
        Assert.Equal(ProbeExit.BadOutput, MirrorResult.Parse("rien", 0, 1).Exit);
        Assert.Equal("run,shop", Mirror.ProcessMirrorRunner.SectionsArg(MirrorNeed.Shop));
        Assert.Equal("reward", Mirror.ProcessMirrorRunner.SectionsArg(MirrorNeed.Reward));
    }
}
