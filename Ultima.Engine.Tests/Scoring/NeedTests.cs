using System.Text.Json;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Cards;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// S16 — couche <b>L2 « besoin immédiat »</b> des cartes (docs/27 §3) : 0..15 points qui décrivent
/// l'état de la run (PV bas, pire coup du boss) et non la composition du deck (ça, c'est le fit L1).
/// L'exigence de Mio : une carte C-tier basse (L0 41..45) + fit maximal (+20) doit pouvoir sortir
/// <b>A</b> (≥ 70) grâce au besoin, sans toucher à l'amplitude du fit.
/// Toutes les cartes et tous les chiffres viennent des catalogues réels (cards_effects, monsters) :
/// Impervious / Taunt / Stone Armor / Armaments (bloc), Molten Fist (attaque), Spur (soin chiffré).
/// </summary>
public sealed class NeedTests
{
    private const string BossAct1 = "ENCOUNTER.WATERFALL_GIANT_BOSS";
    private const string NeedPrefix = "Besoin immédiat";

    private static CardInstance Card(string id) => new(id, 0, null, null);

    private static IReadOnlyList<CardInstance> Repeat(string id, int count) =>
        Enumerable.Range(0, count).Select(_ => Card(id)).ToArray();

    private static ScoringContext Ctx(IReadOnlyList<CardInstance> deck, int hp = 70, int maxHp = 70,
        string? boss = BossAct1, string character = "CHARACTER.IRONCLAD") =>
        new(character, 0, boss, 0, 10, deck, Array.Empty<string>(), 99, hp, maxHp);

    /// <summary>Deck couvert : 10 Defend → 15 bloc/tour, soit exactement le pire coup du boss acte 1.</summary>
    private static IReadOnlyList<CardInstance> CoveredDeck() => Repeat("CARD.DEFEND_IRONCLAD", 10);

    /// <summary>Deck sans une seule carte de bloc : le pire coup passe (15 &gt; 0).</summary>
    private static IReadOnlyList<CardInstance> NoBlockDeck() =>
        Repeat("CARD.MOLTEN_FIST", 4).Concat(Repeat("CARD.BULLY", 4)).Concat(Repeat("CARD.TREMBLE", 4)).ToArray();

    /// <summary>Deck de 12 Big Bang : 0 dégât, 0 bloc — le trou le plus net pour mesurer un fit max.</summary>
    private static IReadOnlyList<CardInstance> BigBangDeck() => Repeat("CARD.BIG_BANG", 12);

    private static ScoreTerm[] Need(Score score) => score.Terms
        .Where(t => t.Label.StartsWith(NeedPrefix, StringComparison.Ordinal))
        .ToArray();

    private static int NeedPoints(Score score) => Need(score).Sum(t => t.Points);

    // ------------------------------------------------------- (a) PV bas ⇒ besoin de bloc

    [Fact]
    public void Same_block_card_scores_higher_at_low_hp_than_at_full_hp()
    {
        var scorer = new Scorer();

        // Deck couvert (15 bloc/tour = pire coup 15) : la règle « coup du boss » ne s'émet pas, donc
        // l'écart mesuré est exactement le terme de PV — pas un artefact de la menace.
        var low = scorer.ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(CoveredDeck(), hp: 20));
        var full = scorer.ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(CoveredDeck(), hp: 70));

        var lowNeed = Assert.Single(Need(low));
        Assert.Equal("Besoin immédiat — bloc (PV bas)", lowNeed.Label);
        Assert.InRange(lowNeed.Points, 5, CardScorer.MaxHpNeed);
        Assert.Contains("PV 20/70 (29 %)", lowNeed.Source);
        Assert.Contains("besoin de bloc", lowNeed.Source);

        Assert.Empty(Need(full)); // PV plein (et bloc couvert) : aucun besoin
        Assert.InRange(low.Total - full.Total, 5, 10);
        Assert.True(low.Total > full.Total,
            $"Impervious à 20/70 = {low.Total}, à 70/70 = {full.Total}");
    }

    [Fact]
    public void Hp_need_grows_as_hp_drops()
    {
        var scorer = new Scorer();
        var totals = new[] { 34, 21, 14, 7 }.Select(hp =>
            scorer.ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(CoveredDeck(), hp: hp)).Total).ToArray();

        for (var i = 1; i < totals.Length; i++)
            Assert.True(totals[i] >= totals[i - 1],
                $"les PV baissent de 34 à 7 : les totaux devraient croître, obtenu {string.Join(" → ", totals)}");

        // 21/70 = 30 % et 14/70 = 20 % : 20 % est le point de besoin maximal.
        Assert.Equal(CardScorer.MaxHpNeed,
            Need(scorer.ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(CoveredDeck(), hp: 14))).Sum(t => t.Points));
    }

    // ------------------------------------------------ (b) ACCEPTATION : C-tier + fit max ⇒ A

    [Fact]
    public void Low_meta_block_card_reaches_A_with_max_fit_and_low_hp()
    {
        var scorer = new Scorer();
        var score = scorer.ScoreCard(Card("CARD.TAUNT"), Ctx(BigBangDeck(), hp: 20));

        // Socle C : la méta seule laisse Taunt à 41 (Spire Codex consensus 58/100).
        var meta = Assert.Single(score.Terms, t => t.Label.StartsWith("Méta communautaire", StringComparison.Ordinal));
        Assert.InRange(meta.Points, 0, 54);

        // Fit maximal : ΔG 11 → 2·11 = 22, borné à 20 (l'amplitude n'a pas bougé).
        var fit = Assert.Single(score.Terms, t => t.Label == "Fit au deck (ΔG)");
        Assert.Equal(CardScorer.MaxFit, fit.Points);

        // Besoin : PV 20/70 (29 %) → 7, plus 5 parce que le pire coup (15) dépasse le bloc/tour (0).
        var need = Need(score);
        Assert.Equal(2, need.Length);
        Assert.Equal(7, need.Single(t => t.Label.Contains("PV bas", StringComparison.Ordinal)).Points);
        Assert.Equal(5, need.Single(t => t.Label.Contains("coup du boss", StringComparison.Ordinal)).Points);
        Assert.Equal(12, need.Sum(t => t.Points));

        Assert.True(score.Total - need.Sum(t => t.Points) < 70,
            $"sans L2, Taunt vaut {score.Total - need.Sum(t => t.Points)} (< 70, soit B)");
        Assert.True(score.Total >= 70, $"Taunt devrait sortir A, obtenu {score.Total} ({score.Grade})");
        Assert.Equal("A", score.Grade);
    }

    [Fact]
    public void A_45_point_card_with_a_big_fit_plus_need_reaches_A()
    {
        // Le cas chiffré de docs/27 §3 : 45 (C) + fit + besoin > 70. Stone Armor (bloc 5, L0 45)
        // dans 12 Big Bang : fit +14, puis L2 +12 à 20/70 PV.
        var score = new Scorer().ScoreCard(Card("CARD.STONE_ARMOR"), Ctx(BigBangDeck(), hp: 20));
        var meta = score.Terms.Single(t => t.Label.StartsWith("Méta communautaire", StringComparison.Ordinal));

        Assert.Equal(45, meta.Points);
        Assert.True(score.Total >= 70, $"Stone Armor à 20/70 = {score.Total} ({score.Grade})");
        Assert.Equal("A", score.Grade);
    }

    // ---------------------------------------------------------------- (c) borne 0..15

    [Fact]
    public void Need_is_bounded_to_15_on_every_ironclad_card()
    {
        var scorer = new Scorer();
        var ctx = Ctx(NoBlockDeck(), hp: 5); // PV critique + aucune carte de bloc : besoin maximal

        var seen = 0;
        var max = 0;
        foreach (var id in IroncladCardIds())
        {
            var score = scorer.ScoreCard(Card(id), ctx);
            foreach (var term in Need(score))
            {
                Assert.InRange(term.Points, 1, CardScorer.MaxNeed); // aucun terme à 0 conservé
                Assert.False(string.IsNullOrWhiteSpace(term.Source), $"{id} : source vide");
                Assert.False(string.IsNullOrWhiteSpace(term.Label), $"{id} : libellé vide");
            }

            var points = NeedPoints(score);
            Assert.InRange(points, 0, CardScorer.MaxNeed);
            if (points == 0) continue;
            seen++;
            max = Math.Max(max, points);
        }

        Assert.True(seen > 10, $"trop peu de termes de besoin observés ({seen}) : le test ne prouve rien");
        Assert.Equal(CardScorer.MaxNeed, max); // le plafond est réellement atteint (Armaments : 10 + 5)
    }

    // ------------------------------------------------- (d) une attaque ne gagne rien

    [Fact]
    public void Attack_card_without_block_has_no_need_even_at_low_hp()
    {
        var scorer = new Scorer();

        foreach (var hp in new[] { 50, 20, 5 })
        {
            var score = scorer.ScoreCard(Card("CARD.MOLTEN_FIST"), Ctx(NoBlockDeck(), hp: hp));
            Assert.Empty(Need(score));
        }

        // Le soin chiffré, lui, répond au besoin de PV (Spur : Heal 5 au catalogue).
        var spur = scorer.ScoreCard(Card("CARD.SPUR"), Ctx(NoBlockDeck(), hp: 10));
        var heal = Assert.Single(Need(spur));
        Assert.Contains("soin", heal.Label);
        Assert.Equal(CardScorer.MaxHpNeed, heal.Points);
        Assert.Contains("PV 10/70 (14 %)", heal.Source);
    }

    // ------------------------------------------------------- (e) PV plein ⇒ aucun terme

    [Fact]
    public void No_need_term_at_full_hp()
    {
        var scorer = new Scorer();

        // Deck couvert : ni PV bas ni coup du boss, donc AUCUN terme (pas même un terme à 0).
        Assert.Empty(Need(scorer.ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(CoveredDeck(), hp: 70))));

        // Deck sans bloc : la règle du boss reste vraie à PV plein (elle ne parle pas des PV), mais
        // aucun terme « PV bas » n'est émis — c'est la séparation des deux règles.
        var bare = scorer.ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(NoBlockDeck(), hp: 70));
        Assert.DoesNotContain(Need(bare), t => t.Label.Contains("PV bas", StringComparison.Ordinal));
    }

    [Fact]
    public void Boss_pressure_needs_a_hit_above_the_deck_block_per_turn()
    {
        var scorer = new Scorer();

        // 15 bloc/tour pour un pire coup de 15 : le coup ne « passe » pas, aucun terme de menace.
        Assert.DoesNotContain(Need(scorer.ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(CoveredDeck(), hp: 70))),
            t => t.Label.Contains("coup du boss", StringComparison.Ordinal));

        // Deck sans bloc : 15 > 0, le terme est émis avec sa source chiffrée.
        var boss = Assert.Single(Need(scorer.ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(NoBlockDeck(), hp: 70))));
        Assert.Equal("Besoin immédiat — coup du boss", boss.Label);
        Assert.Equal(CardScorer.BossPressureNeed, boss.Points);
        Assert.Contains("pire coup 15 > bloc/tour 0.0", boss.Source);
        Assert.Contains("Waterfall Giant", boss.Source);
    }

    [Fact]
    public void Deck_note_ignores_the_need_layer()
    {
        // docs/27 §2 : la note de DECK reste indépendante des PV ; seules les notes d'items bougent.
        var scorer = new Scorer();
        Assert.Equal(scorer.EvaluateDeck(Ctx(CoveredDeck(), hp: 70)).Score.Total,
            scorer.EvaluateDeck(Ctx(CoveredDeck(), hp: 15)).Score.Total);
    }

    /// <summary>Ids réels lus dans data/meta/card_tiers.json (aucune liste écrite à la main).</summary>
    private static IEnumerable<string> IroncladCardIds()
    {
        var dir = ScoringData.ResolveMetaDir();
        var local = Path.Combine(dir, "local", "card_tiers.json");
        var path = File.Exists(local) ? local : Path.Combine(dir, "card_tiers.json");
        Assert.True(File.Exists(path), $"card_tiers.json introuvable : {path}");

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var character in doc.RootElement.GetProperty("characters").EnumerateObject())
        {
            if (!character.Name.Equals("ironclad", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var card in character.Value.EnumerateObject())
                if (card.Value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    yield return "CARD." + id.GetString();
        }
    }
}
