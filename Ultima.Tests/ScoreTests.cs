using System;
using System.Collections.Generic;
using System.Linq;
using Ultima.Core.Score;
using Xunit;

namespace Ultima.Tests;

/// <summary>F5 : ledger de dégâts (agrégation, non-attribués, réconciliation) + export CSV RFC 4180.</summary>
public class ScoreTests
{
    private const ulong P1 = 76561198000000001UL;
    private const ulong P2 = 76561198000000002UL;

    private static DamageRow Card(ulong netId, string player, string cls, string title, int unblocked,
        int blocked = 0, int overkill = 0, bool killed = false, int round = 1)
        => new(round, cls, title, true, "", netId, player, unblocked, blocked, overkill, killed);

    private static DamageRow Unattr(string reason, int unblocked, ulong netId = 0, string player = "",
        int blocked = 0, string cls = "", string title = "")
        => new(1, cls, title, false, reason, netId, player, unblocked, blocked, 0, false);

    [Fact]
    public void Aggregates_by_player_and_card_without_merging_homonyms()
    {
        var l = new DamageLedger();
        l.BeginCombat();
        l.Record(Card(P1, "Alice", "StrikeIronclad", "Strike", 6));
        l.Record(Card(P1, "Alice", "StrikeIronclad", "Strike", 6, blocked: 3));
        l.Record(Card(P1, "Alice", "Bash", "Bash", 8, overkill: 2, killed: true));
        l.Record(Card(P2, "Bob", "StrikeIronclad", "Strike", 4)); // même carte, autre joueur

        var agg = l.AttributedAggregates();
        Assert.Equal(3, agg.Count);
        var aliceStrike = agg.Single(a => a.DealerNetId == P1 && a.SourceClass == "StrikeIronclad");
        Assert.Equal(2, aliceStrike.Hits);
        Assert.Equal(12, aliceStrike.Unblocked);
        Assert.Equal(3, aliceStrike.Blocked);
        var bash = agg.Single(a => a.SourceClass == "Bash");
        Assert.Equal(1, bash.Kills);
        Assert.Equal(2, bash.Overkill);
        Assert.Equal(4, agg.Single(a => a.DealerNetId == P2).Unblocked);
    }

    [Fact]
    public void Unattributed_damage_is_listed_with_reason_never_attributed()
    {
        var l = new DamageLedger();
        l.BeginCombat();
        l.Record(Card(P1, "Alice", "Bash", "Bash", 8));
        l.Record(Unattr(UnattributedReason.NoDealer, 5));                       // poison
        l.Record(Unattr(UnattributedReason.NoDealer, 4));
        l.Record(Unattr(UnattributedReason.NoCardSource, 3, P1, "Alice"));      // relique / pouvoir / orbe
        l.Record(Unattr(UnattributedReason.NonPlayerDealer, 7, 0, "Louse"));

        var un = l.UnattributedAggregates();
        Assert.All(un, a => Assert.False(a.Attributed));
        Assert.All(un, a => Assert.False(string.IsNullOrEmpty(a.Reason)));
        Assert.Equal(9, un.Single(a => a.Reason == UnattributedReason.NoDealer).Unblocked);
        Assert.Equal(2, un.Single(a => a.Reason == UnattributedReason.NoDealer).Hits);
        Assert.Equal(3, un.Single(a => a.Reason == UnattributedReason.NoCardSource).Unblocked);
        Assert.Equal(8, l.AttributedUnblocked);
        Assert.Equal(19, l.UnattributedUnblocked);
        Assert.Equal(19, l.UnattributedByReason().Values.Sum());

        // attribués d'abord, puis non attribués
        var all = l.Aggregates();
        Assert.True(all[0].Attributed);
        Assert.All(all.Skip(1), a => Assert.False(a.Attributed));
    }

    [Fact]
    public void Attributed_plus_unattributed_reconciles_with_observed_total()
    {
        var l = new DamageLedger();
        l.BeginCombat();
        var rnd = new Random(42);
        int expected = 0, expectedBlocked = 0;
        for (var i = 0; i < 200; i++)
        {
            var u = rnd.Next(0, 30);
            var b = rnd.Next(0, 10);
            expected += u;
            expectedBlocked += b;
            l.Record(rnd.Next(3) switch
            {
                0 => Card(rnd.Next(2) == 0 ? P1 : P2, "P", "Card" + rnd.Next(5), "Card", u, b, killed: rnd.Next(10) == 0),
                1 => Unattr(UnattributedReason.NoDealer, u, blocked: b),
                _ => Unattr(UnattributedReason.NoCardSource, u, P1, "Alice", b),
            });
        }

        Assert.True(l.Reconciles);
        Assert.Equal(expected, l.ObservedUnblocked);
        Assert.Equal(expectedBlocked, l.ObservedBlocked);
        Assert.Equal(l.ObservedUnblocked, l.AttributedUnblocked + l.UnattributedUnblocked);
        Assert.Equal(200, l.Aggregates().Sum(a => a.Hits));
    }

    [Fact]
    public void Ledger_resets_between_combats_and_index_increments()
    {
        var l = new DamageLedger();
        Assert.Equal(0, l.CombatIndex);
        l.BeginCombat();
        l.Record(Card(P1, "Alice", "Bash", "Bash", 8, killed: true));
        Assert.Equal(1, l.CombatIndex);

        l.BeginCombat();
        Assert.Equal(2, l.CombatIndex);
        Assert.Empty(l.Rows);
        Assert.Empty(l.Aggregates());
        Assert.Equal(0, l.ObservedUnblocked);
        Assert.Equal(0, l.ObservedKills);
        Assert.True(l.Reconciles);
        Assert.Empty(CsvWriter.Lines(l));
    }

    [Fact]
    public void Seed_continues_numbering_from_existing_csv()
    {
        var existing = new[]
        {
            CsvWriter.Header,
            "3,Alice,Bash,Bash,1,8,0,0,0,true,",
            "7,Alice,,,2,9,0,0,0,false,no_dealer",
            "garbage line",
        };
        Assert.Equal(7, CsvWriter.LastCombatIndex(existing));
        Assert.Equal(0, CsvWriter.LastCombatIndex(new[] { CsvWriter.Header }));

        var l = new DamageLedger();
        l.SeedCombatIndex(CsvWriter.LastCombatIndex(existing));
        l.BeginCombat();
        Assert.Equal(8, l.CombatIndex);
    }

    [Theory]
    [InlineData("Strike", "Strike")]
    [InlineData("", "")]
    [InlineData("Hello, World", "\"Hello, World\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("cr\rhere", "\"cr\rhere\"")]
    public void Csv_escape_follows_rfc4180(string input, string expected)
        => Assert.Equal(expected, CsvWriter.Escape(input));

    [Fact]
    public void Csv_render_has_header_and_one_line_per_card_and_round_trips()
    {
        var l = new DamageLedger();
        l.BeginCombat();
        l.Record(Card(P1, "Al, \"The\" Ironclad", "StrikeIronclad", "Strike+", 9, 1, 0, false));
        l.Record(Card(P1, "Al, \"The\" Ironclad", "StrikeIronclad", "Strike+", 9, 0, 2, true));
        l.Record(Unattr(UnattributedReason.NoDealer, 4));

        var text = CsvWriter.Render(l, includeHeader: true);
        var lines = text.Split(CsvWriter.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal(CsvWriter.Header, lines[0]);
        Assert.Equal("1,\"Al, \"\"The\"\" Ironclad\",StrikeIronclad,Strike+,2,18,1,2,1,true,", lines[1]);
        Assert.Equal("1,,,,1,4,0,0,0,false,no_dealer", lines[2]);

        var fields = ParseRfc4180Line(lines[1]);
        Assert.Equal(11, fields.Count);
        Assert.Equal("Al, \"The\" Ironclad", fields[1]);
        Assert.Equal(11, ParseRfc4180Line(lines[0]).Count);

        Assert.DoesNotContain(CsvWriter.Header, CsvWriter.Render(l, includeHeader: false));
    }

    [Theory]
    [InlineData("Rd 3 (Player turn): A dealt 6 damage to B.", 3)]
    [InlineData("Rd 12 (Enemy turn): B took 4 damage.", 12)]
    [InlineData("Round 3", -1)]
    [InlineData("Rd x", -1)]
    [InlineData("", -1)]
    [InlineData(null, -1)]
    public void ParseRound_reads_game_format_or_returns_minus_one(string? text, int expected)
        => Assert.Equal(expected, DamageLedger.ParseRound(text));

    /// <summary>Lecteur RFC 4180 minimal (une ligne logique) pour vérifier l'aller-retour.</summary>
    private static List<string> ParseRfc4180Line(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
