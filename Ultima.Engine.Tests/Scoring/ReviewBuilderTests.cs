using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Review;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Revue de run (contrat v1.2, point 3 du chef) : timeline reconstruite depuis un RunRecord,
/// choix de cartes chiffrés en ΔG, autres décisions déclarées non chiffrées.
/// </summary>
public sealed class ReviewBuilderTests
{
    private static CardInstance Card(string id, int floor = 1) => new(id, 0, null, floor);

    private static readonly IReadOnlyList<CardInstance> Deck = new[]
    {
        Card("CARD.STRIKE_SILENT"), Card("CARD.STRIKE_SILENT"), Card("CARD.STRIKE_SILENT"),
        Card("CARD.DEFEND_SILENT"), Card("CARD.DEFEND_SILENT"), Card("CARD.DEFEND_SILENT"),
        Card("CARD.BIG_BANG", floor: 3),
    };

    private static PlayerFloorStats Stats(
        int hp, int gold, IReadOnlyList<CardChoice>? choices = null,
        IReadOnlyList<ModelChoice>? relics = null, int damage = 0, int goldGained = 0, ulong player = 1) =>
        new(player, hp, 70, gold, damage, 0, 0, 0, goldGained, 0, 0, 0,
            choices ?? Array.Empty<CardChoice>(),
            Array.Empty<CardInstance>(), Array.Empty<CardInstance>(), Array.Empty<CardTransform>(),
            Array.Empty<CardInstance>(), Array.Empty<string>(), Array.Empty<string>(),
            relics ?? Array.Empty<ModelChoice>(), Array.Empty<string>(),
            Array.Empty<ModelChoice>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<AncientOption>(), Array.Empty<string>());

    private static RunRecord Run(params FloorRecord[] floors) => new()
    {
        FileName = "2026-10-29_18-00-00.run",
        SchemaVersion = 2,
        BuildId = "v0.107.1",
        GameMode = "standard",
        Ascension = 9,
        Win = false,
        WasAbandoned = false,
        KilledByEncounter = "ENCOUNTER.THE_KIN_BOSS",
        KilledByEvent = null,
        StartTime = 1_790_000_000,
        RunTime = 1800,
        Acts = new[] { "ACT.OVERGROWTH", "ACT.HIVE" },
        Modifiers = Array.Empty<string>(),
        Players = new[]
        {
            new RunRecordPlayer(1, "CHARACTER.SILENT", 2, Deck,
                new[] { new RelicInstance("RELIC.ANCHOR", 1) }, Array.Empty<PotionInstance>()),
        },
        Floors = floors,
    };

    private static FloorRecord Floor(int floor, int act, string type, PlayerFloorStats stats,
        params RoomVisit[] rooms) =>
        new(floor, act, MapPointKind.Monster, type, rooms, new[] { stats });

    [Fact]
    public void Timeline_is_rebuilt_and_card_choice_is_scored()
    {
        var cards = new[]
        {
            new CardChoice(Card("CARD.CRASH_LANDING"), true),
            new CardChoice(Card("CARD.BIG_BANG"), false),
            new CardChoice(Card("CARD.STRIKE_SILENT"), false),
        };

        var run = Run(
            Floor(1, 0, "monster", Stats(hp: 68, gold: 10, choices: cards, damage: 2, goldGained: 10),
                new RoomVisit("monster", "ENCOUNTER.CULTISTS_NORMAL", new[] { "MONSTER.CULTIST" }, 5)),
            Floor(2, 0, "monster", Stats(hp: 44, gold: 25, relics: new[] { new ModelChoice("RELIC.GORGET", true) },
                damage: 24, goldGained: 15),
                new RoomVisit("monster", "ENCOUNTER.JAW_WORM_NORMAL", new[] { "MONSTER.JAW_WORM" }, 4)));

        var timeline = new ReviewBuilder().Build(run);

        Assert.Equal(ReviewBuilder.ReviewVersion, timeline.ReviewVersion);
        Assert.Single(timeline.Lanes);
        Assert.Equal(2, timeline.States.Count);
        Assert.Equal(2, timeline.Points.Count);

        var reward = timeline.Points[0];
        Assert.Equal(ReviewKind.CardReward, reward.Kind);
        Assert.Equal(3, reward.Options.Count);
        Assert.Equal("CARD.CRASH_LANDING", reward.Chosen?.Id);
        Assert.NotNull(reward.Regret);
        Assert.True(reward.Regret!.DeltaG >= 0);
        Assert.Equal(5, timeline.Points[0].TurnsTaken);

        // Étage 2 : choix de relique → présent mais non chiffré.
        Assert.Equal(ReviewKind.RelicReward, timeline.Points[1].Kind);
        Assert.Null(timeline.Points[1].Regret);
        Assert.Contains(timeline.Coverage.Reasons, r => r.Contains("non chiffrée"));

        Assert.Equal(1, timeline.Summary.Scored);
        Assert.Equal(1, timeline.Summary.NotEvaluable);
        Assert.True(timeline.Summary.RegretTotal >= 0);
        Assert.Equal(1, timeline.Coverage.Scored);
        Assert.Contains("RunRecord", timeline.Coverage.DataBasis);
    }

    [Fact]
    public void Skipping_an_offer_is_compared_to_zero()
    {
        var cards = new[]
        {
            new CardChoice(Card("CARD.CRASH_LANDING"), false),
            new CardChoice(Card("CARD.BIG_BANG"), false),
        };

        var run = Run(Floor(1, 0, "monster", Stats(hp: 70, gold: 5, choices: cards),
            new RoomVisit("monster", "ENCOUNTER.CULTISTS_NORMAL", Array.Empty<string>(), 3)));

        var timeline = new ReviewBuilder().Build(run);
        var regret = timeline.Points[0].Regret;

        Assert.NotNull(regret);
        Assert.Equal("rien (0)", regret!.Basis.Split(", ").Last().Replace("pris ", ""));
        Assert.True(regret.DeltaG >= 0);
    }

    [Fact]
    public void Run_without_decisions_says_so_instead_of_faking_a_score()
    {
        var run = Run(Floor(1, 0, "rest_site", Stats(hp: 55, gold: 0),
            new RoomVisit("rest_site", null, Array.Empty<string>(), 0)));

        var timeline = new ReviewBuilder().Build(run);

        Assert.Equal(0, timeline.Summary.Scored);
        Assert.Equal(0, timeline.Summary.RegretTotal);
        Assert.Contains("Aucune décision chiffrable", timeline.Summary.Note);
        Assert.Equal(0, timeline.Coverage.Scored);
    }

    [Fact]
    public void Deck_at_floor_keeps_only_cards_added_before_it()
    {
        var run = Run(
            Floor(1, 0, "monster", Stats(hp: 70, gold: 0),
                new RoomVisit("monster", "ENCOUNTER.CULTISTS_NORMAL", Array.Empty<string>(), 3)),
            Floor(4, 0, "monster", Stats(hp: 60, gold: 0),
                new RoomVisit("monster", "ENCOUNTER.CULTISTS_NORMAL", Array.Empty<string>(), 3)));

        var timeline = new ReviewBuilder().Build(run);

        Assert.Equal(6, timeline.States[0].Deck.Count);   // BIG_BANG arrive à l'étage 3
        Assert.Equal(7, timeline.States[1].Deck.Count);
    }
}
