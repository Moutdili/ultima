using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ultima.Core.Model;
using Ultima.Core.Priority;
using Xunit;

namespace Ultima.Tests;

/// <summary>F3 : catalogue (parsing tolérant) + règles d'alerte locale. F4 : utilitaires voie A/B.</summary>
public class PriorityTests
{
    private const string Json = """
    [
      { "class": "Clash", "glow_gold": true, "turn_conditional": false, "priority_candidate": true,
        "vars": { "damagevar": 14.0 }, "first_card_hint": "unknown-field", "tags": ["x"] },
      { "class": "Finisher", "glow_gold": false, "turn_conditional": true, "priority_candidate": true },
      { "class": "Strike", "glow_gold": true, "priority_candidate": false },
      { "class": "Defend" },
      { "glow_gold": true, "priority_candidate": true },
      42,
      { "class": "Clash", "glow_gold": false, "priority_candidate": true },
    ]
    """;

    private static readonly PriorityCatalog Catalog = PriorityCatalog.Parse(Json);

    private static CardSnapshot Card(string cls, bool playable = true, bool glow = false, string? name = null) =>
        new(ModelId: cls.ToUpperInvariant(), Name: name ?? cls, Cost: 1, Type: "Attack", IsUpgraded: false, Enchantment: null)
        { ClassName = cls, Playable = playable, GlowsGold = glow };

    private static CombatSnapshot Snap(params CardSnapshot[] hand) =>
        new(Turn: 3, PlayerName: "Moi", Hp: 50, MaxHp: 80, Block: 0, Energy: 3,
            Enemies: Array.Empty<EnemySnapshot>(), Hand: hand,
            DrawPile: Array.Empty<CardSnapshot>(), DiscardPile: Array.Empty<CardSnapshot>())
        {
            LocalNetId = 7,
            Players = new[]
            {
                new PlayerSnapshot(7, "Alice", "Ironclad", 50, 80, 0, IsLocal: true),
                new PlayerSnapshot(9, "Bob", "Silent", 40, 70, 0, IsLocal: false),
            },
            IsMultiplayer = true,
        };

    // ---------- parsing ----------

    [Fact]
    public void Parse_KeepsOnlyCandidates_AndToleratesUnknownFields()
    {
        Assert.Equal(2, Catalog.Count);
        Assert.True(Catalog.Contains("Clash"));
        Assert.True(Catalog.Contains("Finisher"));
        Assert.False(Catalog.Contains("Strike"));   // priority_candidate == false
        Assert.False(Catalog.Contains("Defend"));   // priority_candidate absent
        Assert.False(Catalog.Contains(""));
        Assert.False(Catalog.Contains(null));
    }

    [Fact]
    public void Parse_ReadsFlags_FirstDuplicateWins()
    {
        Assert.True(Catalog.TryGet("Clash", out var clash));
        Assert.True(clash.GlowGold);                // la 2e entrée « Clash » (glow false) est ignorée
        Assert.False(clash.TurnConditional);
        Assert.True(Catalog.TryGet("Finisher", out var fin));
        Assert.False(fin.GlowGold);
        Assert.True(fin.TurnConditional);
    }

    [Fact]
    public void Parse_RejectsNonArrayRootAndInvalidJson()
    {
        Assert.ThrowsAny<JsonException>(() => PriorityCatalog.Parse("{\"class\":\"Clash\"}"));
        Assert.ThrowsAny<JsonException>(() => PriorityCatalog.Parse("[{"));
        Assert.Equal(0, PriorityCatalog.Parse("[]").Count);
    }

    [Fact]
    public void Parse_RealCatalog_AllCandidatesLoaded()
    {
        var path = FindRepoFile(Path.Combine("data", "catalog", "priority_cards.json"));
        using var stream = File.OpenRead(path);
        var catalog = PriorityCatalog.Load(stream);
        Assert.True(catalog.Count >= 30, $"catalog has {catalog.Count} entries");
        Assert.True(catalog.Contains("Clash"));
    }

    // ---------- règles F3 ----------

    [Fact]
    public void Rule_InCatalog_Playable_Glowing_Alerts()
    {
        var alerts = PriorityRules.FromHand(Snap(Card("Clash", glow: true, name: "Choc")), Catalog);
        var a = Assert.Single(alerts);
        Assert.Equal("Choc", a.CardName);
        Assert.Equal("Clash", a.CardClass);
        Assert.Equal(7UL, a.PlayerNetId);
        Assert.Equal("Alice", a.PlayerName);
        Assert.Equal("Ironclad", a.CharacterId);
        Assert.False(a.IsRemote);
        Assert.Equal(PrioritySource.LocalHand, a.Source);
        Assert.Equal(3, a.Turn);
    }

    [Fact]
    public void Rule_NotPlayable_NoAlert()
    {
        Assert.Empty(PriorityRules.FromHand(Snap(Card("Clash", playable: false, glow: true)), Catalog));
        Assert.Empty(PriorityRules.FromHand(Snap(Card("Finisher", playable: false)), Catalog));
    }

    [Fact]
    public void Rule_GlowRequiredButAbsent_NoAlert()
    {
        Assert.Empty(PriorityRules.FromHand(Snap(Card("Clash", glow: false)), Catalog));
    }

    [Fact]
    public void Rule_NoGlowEntry_UnobservableCondition_AlertsWhenPlayable()
    {
        var a = Assert.Single(PriorityRules.FromHand(Snap(Card("Finisher", glow: false)), Catalog));
        Assert.Equal("Finisher", a.CardClass);
    }

    [Fact]
    public void Rule_NotInCatalog_NoAlert()
    {
        Assert.Empty(PriorityRules.FromHand(Snap(Card("Strike", glow: true), Card("Bash", glow: true)), Catalog));
    }

    [Fact]
    public void Rule_CardPlayed_LeavesHand_AlertDisappears()
    {
        var before = PriorityRules.FromHand(Snap(Card("Clash", glow: true), Card("Strike")), Catalog);
        Assert.Single(before);
        var after = PriorityRules.FromHand(Snap(Card("Strike")), Catalog);   // Clash jouée
        Assert.Empty(after);
    }

    [Fact]
    public void Rule_LocalPlayerFallback_WhenPlayersMissing()
    {
        var s = Snap(Card("Clash", glow: true)) with { Players = Array.Empty<PlayerSnapshot>(), LocalNetId = 42 };
        var a = Assert.Single(PriorityRules.FromHand(s, Catalog));
        Assert.Equal(42UL, a.PlayerNetId);
        Assert.Equal("Moi", a.PlayerName);
        Assert.Equal("", a.CharacterId);
    }

    [Fact]
    public void Rule_EmptyCatalogOrHand_NoAlert()
    {
        Assert.Empty(PriorityRules.FromHand(Snap(Card("Clash", glow: true)), PriorityCatalog.Empty));
        Assert.Empty(PriorityRules.FromHand(Snap(), Catalog));
    }

    // ---------- F4 : utilitaires ----------

    [Fact]
    public void Remote_ResolvesNameAndCharacter_OrFallsBack()
    {
        var a = PriorityRules.Remote(Snap(), 9, "Clash", "Choc", PrioritySource.RemoteHover, 3);
        Assert.Equal("Bob", a.PlayerName);
        Assert.Equal("Silent", a.CharacterId);
        Assert.True(a.IsRemote);
        Assert.Equal(PrioritySource.RemoteHover, a.Source);

        var unknown = PriorityRules.Remote(null, 5, "Clash", "", PrioritySource.RemoteMessage, 1);
        Assert.Equal("#5", unknown.PlayerName);
        Assert.Equal("", unknown.CharacterId);
        Assert.Equal("Clash", unknown.CardName);
    }

    [Fact]
    public void Signature_OrderIndependent_EmptyIgnoresTurn()
    {
        var s = Snap(Card("Clash", glow: true), Card("Finisher"));
        var ab = PriorityRules.FromHand(s, Catalog);
        var ba = ab.Reverse().ToList();
        Assert.Equal(PriorityRules.Signature(ab, 3), PriorityRules.Signature(ba, 3));
        Assert.NotEqual(PriorityRules.Signature(ab, 3), PriorityRules.Signature(ab, 4));
        Assert.Equal("", PriorityRules.Signature(Array.Empty<PriorityAlert>(), 3));
        Assert.Equal(PriorityRules.Signature(null, 1), PriorityRules.Signature(Array.Empty<PriorityAlert>(), 9));
    }

    [Fact]
    public void Protocol_VersionStaleAndSendGate()
    {
        Assert.True(PriorityProtocol.IsCompatible(PriorityProtocol.Version));
        Assert.False(PriorityProtocol.IsCompatible(PriorityProtocol.Version + 1));
        Assert.True(PriorityProtocol.IsStale(2, 3));
        Assert.False(PriorityProtocol.IsStale(3, 3));
        Assert.False(PriorityProtocol.IsStale(4, 3));

        var capable = new HashSet<ulong> { 9 };
        Assert.True(PriorityProtocol.CanSendPriority(new ulong[] { 9 }, capable));
        Assert.False(PriorityProtocol.CanSendPriority(new ulong[] { 9, 11 }, capable)); // 11 n'a pas dit hello
        Assert.False(PriorityProtocol.CanSendPriority(Array.Empty<ulong>(), capable));   // aucun pair
        Assert.False(PriorityProtocol.CanSendPriority(new ulong[] { 9 }, new HashSet<ulong>()));
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relative);
    }
}
