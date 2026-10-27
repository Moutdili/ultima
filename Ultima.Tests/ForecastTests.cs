using System;
using System.Collections.Generic;
using System.Linq;
using Ultima.Core.Damage;
using Ultima.Core.Forecast;
using Ultima.Core.Model;
using Xunit;

namespace Ultima.Tests;

/// <summary>
/// F1/F2 via ForecastEngine (SPEC §4) + oracle. Arithmétique vérifiée à la main dans les commentaires.
/// Les multiplicateurs (0.75 / 0.6 / 1.5 / 1.25) n'existent QUE dans les tests : le code de prod les reçoit
/// du snapshot.
/// </summary>
public class ForecastTests
{
    private const ulong LocalId = 1;
    private const ulong AllyId = 2;

    private static IntentSnapshot Attack(int perHit, int repeats, decimal? raw = null,
        decimal? weak = null, decimal? vuln = null, string label = "", params TargetHit[] perTarget)
        => new("Attack", perHit, repeats, perHit * repeats, label)
        {
            RawPerHit = raw ?? perHit,
            WeakMultiplier = weak,
            VulnerableMultiplier = vuln,
            PerTarget = perTarget,
        };

    private static EnemySnapshot Enemy(string name, uint id, params IntentSnapshot[] intents)
        => new(name, 40, 40, 0, intents) { CombatId = id };

    private static CombatSnapshot Snap(int hp, int block, params EnemySnapshot[] enemies)
        => SnapWith(hp, block, new[] { Local(hp, block) }, enemies);

    private static CombatSnapshot SnapWith(int hp, int block, PlayerSnapshot[] players, params EnemySnapshot[] enemies)
        => new(1, "Me", hp, 80, block, 3, enemies,
            Array.Empty<CardSnapshot>(), Array.Empty<CardSnapshot>(), Array.Empty<CardSnapshot>())
        {
            Players = players,
            LocalNetId = LocalId,
            IsMultiplayer = players.Length > 1,
        };

    private static PlayerSnapshot Local(int hp, int block, bool vulnerable = false)
        => new(LocalId, "Me", "Ironclad", hp, 80, block, IsLocal: true) { IsVulnerable = vulnerable };

    // ---------- F2 acceptance (SPEC §4) ----------

    [Fact]
    public void F2_16x3_on_35hp_third_hit_lethal()
    {
        // 35 - 16 - 16 - 16 = -13 -> 3e coup létal, overkill 13
        var f = ForecastEngine.Build(Snap(35, 0, Enemy("Jaw Worm", 7, Attack(16, 3))));

        Assert.Equal(3, f.LethalHitIndex);
        Assert.Equal("Jaw Worm", f.LethalEnemyName);
        Assert.Equal(48, f.TotalIncoming);
        Assert.Equal(-13, f.HpAfter);
        Assert.Equal(13, f.Overkill);
        Assert.Equal(3, f.Enemies[0].Lethal.LethalAt);
        Assert.Equal(7u, f.Enemies[0].CombatId);
        Assert.Equal(48, f.Enemies[0].Attacks[0].Total);
    }

    [Fact]
    public void F2_16x3_with_20_block_second_hit_lethal_with_overkill()
    {
        // NB : SPEC dit « 20 block et 35 PV → 2e coup » : arithmétiquement faux (35 PV survivent à 7,
        // cf. test suivant). Cas de référence de LethalIndexTests : 12 PV.
        // coup 1 : 16 absorbés (block 4) ; coup 2 : 4 absorbés, 12 -> 0 PV = LÉTAL ; coup 3 : -16 = overkill 16
        var f = ForecastEngine.Build(Snap(12, 20, Enemy("Cultist", 1, Attack(16, 3))));

        Assert.Equal(2, f.LethalHitIndex);
        Assert.Equal(16, f.Overkill);
        Assert.Equal(-16, f.HpAfter);
        Assert.Equal(0, f.BlockAfter);
        Assert.Equal(0, f.Enemies[0].Lethal.Hits[1].HpAfter);
    }

    [Fact]
    public void F2_16x3_with_20_block_on_35hp_survives_at_7()
    {
        // 48 - 20 = 28 sur 35 PV -> 7 PV, pas létal
        var f = ForecastEngine.Build(Snap(35, 20, Enemy("Cultist", 1, Attack(16, 3))));

        Assert.Null(f.LethalHitIndex);
        Assert.Null(f.LethalEnemyName);
        Assert.Equal(7, f.HpAfter);
        Assert.Equal(0, f.Overkill);
    }

    [Theory]
    [InlineData(48, 0)]
    [InlineData(60, 12)]
    public void F2_16x3_with_block_at_least_48_fully_blocked(int block, int blockAfter)
    {
        var f = ForecastEngine.Build(Snap(35, block, Enemy("Cultist", 1, Attack(16, 3))));

        Assert.Null(f.LethalHitIndex);
        Assert.Equal(35, f.HpAfter);
        Assert.Equal(blockAfter, f.BlockAfter);
        Assert.Equal(48, f.TotalIncoming);       // avant block
        Assert.True(f.Enemies[0].Lethal.FullyBlocked);
    }

    // ---------- F1 : variantes ----------

    [Theory]
    [InlineData(0.75, 1.5, 12, 24)]   // 16*0.75 = 12 ; 16*1.5 = 24
    [InlineData(0.6, 1.25, 9, 20)]    // 16*0.6 = 9.6 -> 9 ; 16*1.25 = 20 : prouve l'absence de valeur en dur
    public void F1_variants_use_supplied_multipliers(double weak, double vuln, int expWeak, int expVuln)
    {
        var f = ForecastEngine.Build(Snap(80, 0,
            Enemy("Louse", 1, Attack(16, 3, weak: (decimal)weak, vuln: (decimal)vuln))));
        var a = f.Enemies[0].Attacks.Single();

        Assert.Equal(16, a.PerHit);               // actuel = snapshot, jamais recalculé
        Assert.Equal(3, a.Repeats);
        Assert.Equal(expWeak, a.PerHitIfWeak);
        Assert.Equal(expVuln, a.PerHitIfVulnerable);
        Assert.False(a.AlreadyWeak);
        Assert.False(a.AlreadyVulnerable);
    }

    [Fact]
    public void F1_variants_use_RawPerHit_before_truncation()
    {
        // Raw 13.5 (PerHit 13) : 13.5*0.75 = 10.125 -> 10 (13*0.75 = 9.75 -> 9 serait FAUX)
        //                        13.5*1.5  = 20.25  -> 20 (13*1.5  = 19.5 -> 19 serait FAUX)
        var f = ForecastEngine.Build(Snap(80, 0,
            Enemy("Louse", 1, Attack(13, 2, raw: 13.5m, weak: 0.75m, vuln: 1.5m))));
        var a = f.Enemies[0].Attacks.Single();

        Assert.Equal(13, a.PerHit);
        Assert.Equal(10, a.PerHitIfWeak);
        Assert.Equal(20, a.PerHitIfVulnerable);
    }

    [Fact]
    public void F1_variants_fall_back_on_PerHit_when_RawPerHit_missing()
    {
        // reader incomplet : RawPerHit = 0, PerHit = 13 -> base 13 ; 13*0.75 = 9.75 -> 9
        var f = ForecastEngine.Build(Snap(80, 0,
            Enemy("Louse", 1, Attack(13, 1, raw: 0m, weak: 0.75m, vuln: 1.5m))));
        var a = f.Enemies[0].Attacks.Single();

        Assert.Equal(9, a.PerHitIfWeak);
        Assert.Equal(19, a.PerHitIfVulnerable);
    }

    [Fact]
    public void F1_null_multiplier_means_not_applicable()
    {
        var f = ForecastEngine.Build(Snap(80, 0, Enemy("Louse", 1, Attack(16, 1))));
        var a = f.Enemies[0].Attacks.Single();

        Assert.Null(a.PerHitIfWeak);
        Assert.Null(a.PerHitIfVulnerable);
        Assert.False(a.AlreadyWeak);
        Assert.False(a.AlreadyVulnerable);
    }

    [Fact]
    public void F1_no_double_count_when_enemy_already_weak()
    {
        var weakEnemy = Enemy("Louse", 1, Attack(12, 2, weak: 0.75m, vuln: 1.5m)) with { IsWeak = true };
        var a = ForecastEngine.Build(Snap(80, 0, weakEnemy)).Enemies[0].Attacks.Single();

        Assert.Equal(12, a.PerHit);               // déjà affaibli : l'actuel est intact
        Assert.Null(a.PerHitIfWeak);              // jamais re-multiplié
        Assert.True(a.AlreadyWeak);
        Assert.Equal(18, a.PerHitIfVulnerable);   // 12*1.5 = 18
    }

    [Fact]
    public void F1_no_double_count_when_local_player_already_vulnerable()
    {
        var s = SnapWith(80, 0, new[] { Local(80, 0, vulnerable: true) },
            Enemy("Louse", 1, Attack(24, 1, weak: 0.75m, vuln: 1.5m)));
        var a = ForecastEngine.Build(s).Enemies[0].Attacks.Single();

        Assert.Null(a.PerHitIfVulnerable);
        Assert.True(a.AlreadyVulnerable);
        Assert.Equal(18, a.PerHitIfWeak);         // 24*0.75 = 18
    }

    // ---------- F2 : tour complet ----------

    [Fact]
    public void Turn_concatenates_enemies_with_shared_block_and_names_lethal_enemy()
    {
        // A 10x2 puis B 8x3, 30 PV, 5 block : 10(5 absorbés)->25, 10->15, 8->7, 8->-1 LÉTAL (4e, B), 8->-9
        var f = ForecastEngine.Build(Snap(30, 5,
            Enemy("A", 1, Attack(10, 2)),
            Enemy("B", 2, Attack(8, 3))));

        Assert.Equal(44, f.TotalIncoming);
        Assert.Equal(4, f.LethalHitIndex);
        Assert.Equal("B", f.LethalEnemyName);
        Assert.Equal(-9, f.HpAfter);
        Assert.Equal(9, f.Overkill);
        Assert.Equal(0, f.BlockAfter);

        // seuls, aucun des deux ne tue : A -> 30-5-10 = 15 ; B -> 30-3-8-8 = 11
        Assert.Null(f.Enemies[0].Lethal.LethalAt);
        Assert.Equal(15, f.Enemies[0].Lethal.HpAfter);
        Assert.Null(f.Enemies[1].Lethal.LethalAt);
        Assert.Equal(11, f.Enemies[1].Lethal.HpAfter);
    }

    [Fact]
    public void Turn_respects_snapshot_enemy_order()
    {
        // B 8x3 puis A 10x2 : 8(5 absorbés)->27, 8->19, 8->11, 10->1, 10->-9 LÉTAL (5e, A)
        var f = ForecastEngine.Build(Snap(30, 5,
            Enemy("B", 2, Attack(8, 3)),
            Enemy("A", 1, Attack(10, 2))));

        Assert.Equal(5, f.LethalHitIndex);
        Assert.Equal("A", f.LethalEnemyName);
        Assert.Equal(new[] { "B", "A" }, f.Enemies.Select(e => e.EnemyName));
    }

    [Fact]
    public void Enemy_lethal_concatenates_all_its_attack_intents()
    {
        // 6x2 puis 9x1 contre 20 PV : 14, 8, -1 -> 3e coup
        var f = ForecastEngine.Build(Snap(20, 0, Enemy("Twin", 1, Attack(6, 2), Attack(9, 1))));

        Assert.Equal(2, f.Enemies[0].Attacks.Count);
        Assert.Equal(3, f.Enemies[0].Lethal.LethalAt);
        Assert.Equal(1, f.Enemies[0].Lethal.Overkill);
        Assert.Equal(21, f.TotalIncoming);
    }

    [Fact]
    public void Non_attack_intents_and_zero_repeats_are_ignored()
    {
        var f = ForecastEngine.Build(Snap(30, 0, Enemy("Mix", 1,
            new IntentSnapshot("Defend", 0, 0, 0, ""),
            new IntentSnapshot("Buff", 5, 1, 5, "5"),
            Attack(9, 0),
            Attack(7, 1))));

        var a = Assert.Single(f.Enemies[0].Attacks);
        Assert.Equal(7, a.PerHit);
        Assert.Equal(7, f.TotalIncoming);
        Assert.Equal(23, f.HpAfter);
    }

    [Fact]
    public void Negative_values_are_clamped()
    {
        var f = ForecastEngine.Build(Snap(30, -4, Enemy("Odd", 1,
            Attack(-3, 2, raw: -3.5m, weak: 0.75m, vuln: 1.5m))));
        var a = f.Enemies[0].Attacks.Single();

        Assert.Equal(0, a.PerHit);
        Assert.Equal(0, a.PerHitIfWeak);
        Assert.Equal(0, a.PerHitIfVulnerable);
        Assert.Equal(0, f.TotalIncoming);
        Assert.Equal(30, f.HpAfter);
        Assert.Equal(0, f.BlockAfter);
        Assert.Null(f.LethalHitIndex);
    }

    [Fact]
    public void No_enemy_means_nothing_incoming()
    {
        var f = ForecastEngine.Build(Snap(30, 6));

        Assert.Empty(f.Enemies);
        Assert.Equal(0, f.TotalIncoming);
        Assert.Null(f.LethalHitIndex);
        Assert.Null(f.LethalEnemyName);
        Assert.Equal(30, f.HpAfter);
        Assert.Equal(6, f.BlockAfter);
        Assert.Equal(0, f.Overkill);
        var p = Assert.Single(f.Players);
        Assert.Equal(0, p.Incoming);
        Assert.Null(p.LethalHitIndex);
    }

    // ---------- co-op : pire cas ----------

    [Fact]
    public void Coop_worst_case_per_player_uses_PerTarget_with_PerHit_fallback()
    {
        // Moi : 10,10,6 = 26 sur 30 PV -> 4 PV.
        // Allié (PerTarget 15, block 5) : 15(5 absorbés)->10, 15->-5 LÉTAL (2e), 6->-11.
        var players = new[]
        {
            Local(30, 0),
            new PlayerSnapshot(AllyId, "Ally", "Silent", 20, 70, 5, IsLocal: false),
        };
        var f = ForecastEngine.Build(SnapWith(30, 0, players,
            Enemy("A", 1, Attack(10, 2, perTarget: new[] { new TargetHit(LocalId, 10), new TargetHit(AllyId, 15) })),
            Enemy("B", 2, Attack(6, 1))));

        Assert.Equal(2, f.Players.Count);

        var me = f.Players.Single(p => p.NetId == LocalId);
        Assert.Equal(26, me.Incoming);
        Assert.Null(me.LethalHitIndex);
        Assert.Equal(4, me.HpAfter);

        var ally = f.Players.Single(p => p.NetId == AllyId);
        Assert.Equal("Ally", ally.Name);
        Assert.Equal("Silent", ally.CharacterId);
        Assert.Equal(36, ally.Incoming);
        Assert.Equal(2, ally.LethalHitIndex);
        Assert.Equal(-11, ally.HpAfter);
    }

    [Fact]
    public void Solo_without_players_list_synthesizes_local_threat()
    {
        var s = SnapWith(35, 0, Array.Empty<PlayerSnapshot>(), Enemy("Jaw Worm", 1, Attack(16, 3)));
        var p = Assert.Single(ForecastEngine.Build(s).Players);

        Assert.Equal(LocalId, p.NetId);
        Assert.Equal(48, p.Incoming);
        Assert.Equal(3, p.LethalHitIndex);
        Assert.Equal(-13, p.HpAfter);
    }

    // ---------- SequenceLethal == LethalIndex sur NxM ----------

    [Theory]
    [InlineData(16, 3, 0, 35)]
    [InlineData(16, 3, 20, 12)]
    [InlineData(16, 3, 20, 35)]
    [InlineData(16, 3, 48, 35)]
    [InlineData(16, 3, 60, 35)]
    [InlineData(0, 5, 0, 10)]
    [InlineData(7, 0, 5, 10)]
    [InlineData(-4, 2, 0, 10)]
    [InlineData(5, 4, -3, 10)]
    [InlineData(3, 2, 0, 0)]
    [InlineData(10, 1, 0, 10)]
    public void SequenceLethal_matches_LethalIndex_on_homogeneous_NxM(int perHit, int repeats, int block, int hp)
    {
        var expected = LethalIndex.Compute(perHit, repeats, block, hp);
        var actual = SequenceLethal.Compute(Enumerable.Repeat(perHit, Math.Max(0, repeats)).ToList(), block, hp);

        Assert.Equal(expected.Hits, actual.Hits);
        Assert.Equal(expected.LethalAt, actual.LethalAt);
        Assert.Equal(expected.HpAfter, actual.HpAfter);
        Assert.Equal(expected.BlockAfter, actual.BlockAfter);
        Assert.Equal(expected.Overkill, actual.Overkill);
        Assert.Equal(expected.FullyBlocked, actual.FullyBlocked);
    }

    // ---------- oracle ----------

    [Fact]
    public void Oracle_strips_bbcode_and_matches_NxM()
    {
        Assert.True(IntentOracle.Matches(Attack(16, 3, label: "[color=#FF5555]16[/color]x3"), out var detail));
        Assert.Contains("16,3", detail);
    }

    [Fact]
    public void Oracle_single_number_matches_PerHit_or_TotalDamage()
    {
        Assert.True(IntentOracle.Matches(Attack(12, 1, label: "12"), out _));
        Assert.True(IntentOracle.Matches(Attack(6, 2, label: "12"), out _));      // total 6*2
    }

    [Theory]
    [InlineData(16, 3, "[color=#FF5555]15[/color]x3")]
    [InlineData(16, 3, "16x2")]
    [InlineData(12, 1, "13")]
    public void Oracle_detects_mismatch(int perHit, int repeats, string label)
    {
        Assert.False(IntentOracle.Matches(Attack(perHit, repeats, label: label), out var detail));
        Assert.Contains("MISMATCH", detail);
    }

    [Fact]
    public void Oracle_no_number_or_non_attack_is_not_a_mismatch()
    {
        Assert.True(IntentOracle.Matches(Attack(16, 3, label: "[img]res://intent.png[/img]"), out _));
        Assert.True(IntentOracle.Matches(Attack(16, 3, label: ""), out _));
        Assert.True(IntentOracle.Matches(new IntentSnapshot("Buff", 0, 0, 0, "99"), out _));
    }
}
