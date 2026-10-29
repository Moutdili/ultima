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

    private static ScoringContext Ctx(IReadOnlyList<CardInstance> deck, string character = "CHARACTER.IRONCLAD", int hp = 70) =>
        new(character, 0, BossAct1, 0, 10, deck, Array.Empty<string>(), 99, hp, 70);

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

        // Idempotence : le besoin est couvert, le fit résiduel est marginal — 4 points sur un
        // plafond de 20, soit 5× moins que dans le deck sans bloc. ΔG mesuré = +2 pts sous les
        // poids calibrés (survie +3, consistance −2) : la survie pèse 85 % à l'acte 1 depuis S20,
        // donc +3 de survie valent +2,55 arrondis à +2 ; l'offense ne pèse plus que 10 %.
        Assert.InRange(Fit(covered)?.Points ?? 0, 0, 4);
        Assert.True(bare.Total > covered.Total,
            $"Impervious dans un deck sans bloc = {bare.Total}, dans un deck couvert = {covered.Total}");

        // S16 (L2) : à PV plein, le besoin « coup du boss » s'ajoute aux DEUX cas (+5) car même le deck
        // couvert ne tient que 9 bloc/tour pour un pire coup de 15. L2 ne crée donc pas l'écart : c'est
        // le fit (L1) qui sépare les deux notes. Totaux mesurés sous les poids calibrés : 87 (S) pour
        // le cas dégarni, 71 (A) pour le deck couvert — le fit y vaut 20 contre 4.
        Assert.NotNull(bare.Terms.SingleOrDefault(t => t.Label == "Besoin immédiat — coup du boss"));
        Assert.NotNull(covered.Terms.SingleOrDefault(t => t.Label == "Besoin immédiat — coup du boss"));
        Assert.DoesNotContain(bare.Terms, t => t.Label.Contains("PV bas", StringComparison.Ordinal));
        Assert.Equal("S", bare.Grade);
        Assert.Equal("A", covered.Grade);
    }

    // ---------------------------------------------------- (b) le fit porte la note

    [Fact]
    public void Low_meta_offense_card_fit_shrinks_under_calibrated_weights()
    {
        // Big Bang : 0 énergie, pioche 1, énergie 1, aucun dégât. Un deck de 12 Big Bang ne fait
        // donc aucun dégât : Crash Landing (Attaque 21 en zone) comble offense ET couverture.
        //
        // S20 : ce cas mesurait « C-tier + fit max ⇒ A » sous les poids écrits à la main (offense
        // 30 %, couverture 10 % → ΔG 17, fit plafonné à 20, total 73 A). Les poids calibrés sur
        // 71 634 runs donnent 10 % et 5 % à l'acte 1 : ΔG tombe à 6, le fit à 12 et la carte
        // reste B (65). Effet assumé du branchement — la data ne soutient pas mieux que 10 % pour l'offense à
        // l'acte 1 (tools/calibration/report.md §3.2). L'amplitude du fit (×2, plafond 20) et la
        // note méta sont inchangées : c'est la valeur de ΔG qui baisse, pas l'échelle.
        var score = new Scorer().ScoreCard(Card("CARD.CRASH_LANDING"), Ctx(Repeat("CARD.BIG_BANG", 12), "CHARACTER.REGENT"));

        var fit = Fit(score);
        Assert.NotNull(fit);
        Assert.Equal(12, fit!.Points); // 2·ΔG = 2·6
        Assert.Contains("ΔG = +6 pts", fit.Source);

        // La méta seule la laisse en C : c'est bien le fit qui la fait changer de lettre (C → B).
        var meta = Assert.Single(score.Terms, t => t.Label.StartsWith("Méta communautaire", StringComparison.Ordinal));
        Assert.Equal(50, meta.Points);
        Assert.True(score.Total - fit.Points < 54,
            $"sans le fit, {score.Total - fit.Points} pts (< 54) ; avec, {score.Total}");
        Assert.Equal(65, score.Total);
        Assert.Equal("B", score.Grade);
    }

    [Fact]
    public void Low_meta_block_card_still_reaches_max_fit()
    {
        // Contrepartie du test précédent : la dimension dominante de l'acte 1 est la survie
        // (85 % calibré). Taunt (C-tier, méta 41) dans 12 Big Bang garde un fit maximal : la
        // calibration ne rabote pas l'amplitude du fit, elle déplace la valeur vers la dimension
        // que la data associe au gain. À PV bas (20/70), la carte sort A.
        var score = new Scorer().ScoreCard(Card("CARD.TAUNT"), Ctx(Repeat("CARD.BIG_BANG", 12), hp: 20));

        var fit = Fit(score);
        Assert.NotNull(fit);
        Assert.Equal(CardScorer.MaxFit, fit!.Points);
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
