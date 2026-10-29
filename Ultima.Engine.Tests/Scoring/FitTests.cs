using System.Text.Json;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Relics;
using Ultima.Engine.Scoring.Strength;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// S14 — le terme <b>Fit au deck (ΔG)</b> (docs/27 §3 couche L1 et §7bis).
/// L'exigence de Mio : « une carte C-tier qui s'imbrique parfaitement doit pouvoir sortir A, voire S ».
/// Les cartes et les decks sont réels (<c>data/catalog/cards_effects.json</c>) : Big Bang (0 énergie,
/// pioche 1, énergie 1, aucun dégât), Crash Landing (Attaque 21 en zone), Impervious (30 de bloc),
/// Molten Fist / Bully (attaques), Tremble (Skill sans bloc). Chaque chiffre est donc vérifiable.
/// </summary>
public sealed class FitTests
{
    private const string BossAct1 = "ENCOUNTER.WATERFALL_GIANT_BOSS";
    private const string FitLabel = "Fit au deck (ΔG)";

    private static CardInstance Card(string id) => new(id, 0, null, null);

    private static IReadOnlyList<CardInstance> Repeat(string id, int count) =>
        Enumerable.Range(0, count).Select(_ => Card(id)).ToArray();

    private static ScoringContext Ctx(IReadOnlyList<CardInstance> deck, string character = "CHARACTER.IRONCLAD") =>
        new(character, 0, BossAct1, 0, 10, deck, Array.Empty<string>(), 99, 70, 70);

    /// <summary>Deck de 12 attaques ironclad, sans une seule carte de bloc : le trou est net.</summary>
    private static IReadOnlyList<CardInstance> AttackOnlyDeck() =>
        Repeat("CARD.MOLTEN_FIST", 4).Concat(Repeat("CARD.BULLY", 4)).Concat(Repeat("CARD.TREMBLE", 4)).ToArray();

    private static ScoreTerm? Fit(Score score) => score.Terms.FirstOrDefault(t => t.Label == FitLabel);

    // ---------------------------------------------------------------- (a) idempotence

    [Fact]
    public void Same_card_scores_higher_when_the_need_is_not_covered()
    {
        var scorer = new Scorer();

        // Deck sans bloc : Impervious comble un trou (ΔG positif, plafonné).
        var bare = scorer.ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(Repeat("CARD.STRIKE_IRONCLAD", 10)));

        // Même carte, même deck + 15 Defend : le bloc est déjà couvert, G sature, ΔG ≈ 0.
        var covered = scorer.ScoreCard(Card("CARD.IMPERVIOUS"),
            Ctx(Repeat("CARD.STRIKE_IRONCLAD", 10).Concat(Repeat("CARD.DEFEND_IRONCLAD", 15)).ToArray()));

        var bareFit = Fit(bare);
        Assert.NotNull(bareFit);
        Assert.Equal(CardScorer.MaxFit, bareFit!.Points);

        Assert.Null(Fit(covered)); // idempotence : plus aucun fit à émettre
        Assert.True(bare.Total > covered.Total,
            $"Impervious dans un deck sans bloc = {bare.Total}, dans un deck couvert = {covered.Total}");
        Assert.Equal("A", bare.Grade);
        Assert.Equal("B", covered.Grade);
    }

    // ---------------------------------------------------- (b) le fit porte la note

    [Fact]
    public void Low_meta_card_reaches_A_through_fit()
    {
        // Big Bang : 0 énergie, pioche 1, énergie 1, aucun dégât. Un deck de 12 Big Bang ne fait
        // donc aucun dégât : Crash Landing (Attaque 21 en zone) comble offense ET couverture.
        var score = new Scorer().ScoreCard(Card("CARD.CRASH_LANDING"), Ctx(Repeat("CARD.BIG_BANG", 12), "CHARACTER.REGENT"));

        var fit = Fit(score);
        Assert.NotNull(fit);
        Assert.Equal(CardScorer.MaxFit, fit!.Points); // ΔG mesuré 11 → 2·ΔG = 22, borné à 20

        // La méta seule la laisse en C : c'est bien le fit qui la fait changer de lettre.
        var meta = Assert.Single(score.Terms, t => t.Label.StartsWith("Méta communautaire", StringComparison.Ordinal));
        Assert.InRange(meta.Points, 0, 54);
        Assert.True(score.Total - fit.Points < 70,
            $"sans le fit, {score.Total - fit.Points} pts (< 70) ; avec, {score.Total}");
        Assert.True(score.Total >= 70, $"Crash Landing devrait sortir A, obtenu {score.Total} ({score.Grade})");
        Assert.Equal("A", score.Grade);
    }

    [Fact]
    public void S_cap_is_reachable_when_a_perfect_fit_adds_to_a_real_base()
    {
        // Impervious (30 de bloc) dans 12 attaques : aucune carte de bloc au deck.
        var score = new Scorer().ScoreCard(Card("CARD.IMPERVIOUS"), Ctx(AttackOnlyDeck()));

        var fit = Fit(score);
        Assert.NotNull(fit);
        Assert.Equal(CardScorer.MaxFit, fit!.Points);

        // Sans le fit, la carte reste B : S n'est atteignable QUE parce que le fit s'ajoute.
        Assert.True(score.Total - fit.Points < 85,
            $"sans le fit, {score.Total - fit.Points} pts ; avec, {score.Total}");
        Assert.Equal("S", score.Grade);
        Assert.Contains("bloc/tour 0.0→", fit.Source); // la source nomme la mesure qui bouge
        Assert.Contains("survie", fit.Source);
        Assert.Contains("docs/27", fit.Source);
    }

    // ------------------------------------------------------------ (c) bornes

    [Fact]
    public void Card_fit_is_bounded_to_20_points()
    {
        var scorer = new Scorer();
        var decks = new[]
        {
            Ctx(Repeat("CARD.STRIKE_IRONCLAD", 10)),
            Ctx(Repeat("CARD.DEFEND_IRONCLAD", 10)),
            Ctx(AttackOnlyDeck()),
        };

        var seen = 0;
        foreach (var ctx in decks)
            foreach (var id in IroncladCardIds())
            {
                var score = scorer.ScoreCard(Card(id), ctx);
                if (Fit(score) is not { } fit) continue;
                seen++;
                Assert.InRange(fit.Points, -CardScorer.MaxFit, CardScorer.MaxFit);
                Assert.False(string.IsNullOrWhiteSpace(fit.Source), $"{id} : source vide");
            }

        Assert.True(seen > 20, $"trop peu de termes de fit observés ({seen}) : le test ne prouve rien");
    }

    [Fact]
    public void Relic_fit_is_bounded_to_15_points_and_lifts_a_hole_filler()
    {
        var scorer = new Scorer();
        var ctx = Ctx(Repeat("CARD.STRIKE_IRONCLAD", 10)); // deck sans bloc

        foreach (var id in new[] { "RELIC.ANCHOR", "RELIC.HAND_DRILL", "RELIC.BAG_OF_PREPARATION", "RELIC.INTIMIDATING_HELMET" })
        {
            var score = scorer.ScoreRelic(id, ctx);
            if (Fit(score) is not { } fit) continue;
            Assert.InRange(fit.Points, -RelicScorer.MaxFit, RelicScorer.MaxFit);
            Assert.Contains("docs/27", fit.Source);
        }

        // Ancre : +2 bloc/tour dans un deck qui n'en a aucun → ΔG positif, terme nommé.
        var anchor = scorer.ScoreRelic("RELIC.ANCHOR", ctx);
        var anchorFit = Fit(anchor);
        Assert.NotNull(anchorFit);
        Assert.True(anchorFit!.Points > 0, $"Ancre dans un deck sans bloc : fit {anchorFit.Points}");
        Assert.Contains("bloc/tour", anchorFit.Source);
        Assert.Equal("Fit au deck (ΔG)", anchorFit.Label);

        // Relique déjà possédée : aucun double comptage, donc aucun fit.
        var owned = scorer.ScoreRelic("RELIC.ANCHOR", ctx with { RelicIds = new[] { "RELIC.ANCHOR" } });
        Assert.Null(Fit(owned));
    }

    // --------------------------------------------------------- (d) deck vide

    [Fact]
    public void Empty_deck_has_no_fit_term()
    {
        var scorer = new Scorer();
        var empty = Ctx(Array.Empty<CardInstance>());

        foreach (var id in new[] { "CARD.IMPERVIOUS", "CARD.CRASH_LANDING", "CARD.STRIKE_IRONCLAD" })
            Assert.Null(Fit(scorer.ScoreCard(Card(id), empty)));

        var relic = scorer.ScoreRelic("RELIC.ANCHOR", empty);
        Assert.Null(Fit(relic));

        // Deck naissant : sous MinDeckForFit, le débit de G mesure la dilution, pas un manque.
        var one = Ctx(new[] { Card("CARD.STRIKE_IRONCLAD") });
        Assert.Null(Fit(scorer.ScoreCard(Card("CARD.IMPERVIOUS"), one)));
        Assert.Equal(CardScorer.MinDeckForFit, RelicScorer.MinDeckForFit);
    }

    // ------------------------------------------------- garanties de structure

    [Fact]
    public void Meta_term_does_not_depend_on_the_deck()
    {
        // docs/27 §3 : L0 (méta/rang) reste la base, le fit est un terme SÉPARÉ qui s'y ajoute.
        var scorer = new Scorer();
        // Cartes notées par la méta (les cartes de base ne le sont pas) : le terme L0 doit être
        // identique quel que soit le deck, seul le fit bouge.
        var ids = new[] { "CARD.IMPERVIOUS", "CARD.PACTS_END", "CARD.CRASH_LANDING" };
        var decks = new[]
        {
            Ctx(Repeat("CARD.STRIKE_IRONCLAD", 10)),
            Ctx(AttackOnlyDeck()),
        };

        foreach (var id in ids)
        {
            var meta = decks.Select(ctx => scorer.ScoreCard(Card(id), ctx).Terms
                .Single(t => t.Label == "Méta communautaire").Points).Distinct().ToArray();
            Assert.Single(meta);
        }
    }

    [Fact]
    public void Total_stays_bounded_even_with_a_maxed_fit()
    {
        var scorer = new Scorer();
        foreach (var ctx in new[] { Ctx(Repeat("CARD.STRIKE_IRONCLAD", 10)), Ctx(AttackOnlyDeck()) })
            foreach (var id in IroncladCardIds())
                Assert.InRange(scorer.ScoreCard(Card(id), ctx).Total, 0, 100);
    }

    [Fact]
    public void Delta_is_computable_on_an_empty_deck_but_not_scored()
    {
        // Le ΔG lui-même reste calculable sur un deck vide (il vaut G(carte) − 0) : c'est le TERME
        // de score qui est omis, pour ne pas noter une carte sur un deck qui n'existe pas.
        var calc = new PlayerStrengthCalculator();
        var strength = PlayerStrength.G(Ctx(Array.Empty<CardInstance>()));
        Assert.Equal(0, strength.Total);
        Assert.True(calc.DeltaForCard(Ctx(Array.Empty<CardInstance>()), Card("CARD.IMPERVIOUS")) > 0);
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
