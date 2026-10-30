using System.Text.Json;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Personal;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Agent S3 « Stratège cartes » : ScoreCard / ScoreSkip / synergies.
/// Toutes les preuves portent sur les données réelles de data/meta (patch v0.107.1).
/// </summary>
public sealed class CardScorerTests
{
    // Deck Defect réel : 5 pouvoirs (moteur), 3 cartes Givre, aucun coût 0.
    private static readonly string[] DefectDeck =
    {
        "GLACIER", "COLD_SNAP", "COOLHEADED", "CHARGE_BATTERY", "LOOP",
        "STORM", "DEFRAGMENT", "CAPACITOR", "MACHINE_LEARNING", "BALL_LIGHTNING",
    };

    private static readonly ScoringContext DefectCtx = new(
        "CHARACTER.DEFECT", 0, "ENCOUNTER.WATERFALL_GIANT_BOSS", 0, 5,
        Deck(DefectDeck), Array.Empty<string>(), 99, 70, 70);

    private static CardInstance Card(string id) => new(id, 0, null, null);

    private static IReadOnlyList<CardInstance> Deck(IEnumerable<string> ids) =>
        ids.Select(Card).ToArray();

    // Deck de 14 cartes : 2 à 1 énergie, 12 à 2+ énergie (coûts du catalogue).
    private static readonly string[] SparseOneCost =
    {
        "PAGESTORM", "ACCURACY",
        "BASH", "BLOOD_WALL", "BLUDGEON", "BARRICADE", "ADAPTIVE_STRIKE", "ALL_FOR_ONE",
        "APOTHEOSIS", "BOUNCING_FLASK", "ABRASIVE", "BEAT_DOWN", "BOMBARDMENT", "BULLET_TIME",
    };

    // Même taille, mais 8 cartes à 1 énergie : le coût sature réellement le deck.
    private static readonly string[] DenseOneCost =
    {
        "PAGESTORM", "ACCURACY", "ACROBATICS", "AFTERIMAGE", "AFTERLIFE", "AGGRESSION", "ALCHEMIZE", "ANOINTED",
        "BASH", "BLOOD_WALL", "BLUDGEON", "BARRICADE", "ADAPTIVE_STRIKE", "ALL_FOR_ONE",
    };

    private static ScoringContext CtxWithDeck(IEnumerable<string> deck) =>
        new("CHARACTER.IRONCLAD", 1, "ENCOUNTER.WATERFALL_GIANT_BOSS", 0, 99,
            Deck(deck), Array.Empty<string>(), 70, 70, 70);

    [Fact]
    public void La_courbe_de_cout_compte_tout_le_deck_et_pas_seulement_la_meta()
    {
        // Régression : la densité était calculée sur les seules cartes connues de la méta
        // (4 sur 14), d'où un faux « 2/4 = 50 %, coût saturé » sur un deck qui n'en a que 2 sur 14.
        var score = new Scorer().ScoreCard(Card("CARD.PAGESTORM"), CtxWithDeck(SparseOneCost));

        Assert.DoesNotContain(score.Terms, t => t.Label.StartsWith("Courbe de coût") && t.Points < 0);

        var curve = score.Terms.FirstOrDefault(t => t.Label.StartsWith("Courbe de coût"));
        if (curve is not null) Assert.Contains("14", curve.Source);
    }

    [Fact]
    public void La_courbe_de_cout_signale_la_saturation_quand_elle_est_reelle()
    {
        var score = new Scorer().ScoreCard(Card("CARD.PAGESTORM"), CtxWithDeck(DenseOneCost));

        var curve = Assert.Single(score.Terms, t => t.Label.StartsWith("Courbe de coût"));
        Assert.True(curve.Points < 0, $"8 cartes à 1 énergie sur 14 doivent peser négativement (points = {curve.Points})");
        Assert.Contains("8/14", curve.Source);
    }

    [Fact]
    public void Three_real_defect_cards_score_in_range_with_explainable_terms()
    {
        var scorer = new Scorer();
        var ids = new[] { "CARD.ITERATION", "CARD.HAILSTORM", "CARD.BOOT_SEQUENCE" };

        var scores = ids.Select(id => scorer.ScoreCard(Card(id), DefectCtx)).ToArray();

        foreach (var (id, score) in ids.Zip(scores))
        {
            Assert.InRange(score.Total, 0, 100);
            Assert.True(score.Terms.Count >= 2, $"{id} : {score.Terms.Count} terme(s)");
            Assert.All(score.Terms, t => Assert.False(string.IsNullOrWhiteSpace(t.Source), $"{id} : source vide"));
            Assert.All(score.Terms, t => Assert.False(string.IsNullOrWhiteSpace(t.Label), $"{id} : libellé vide"));
        }

        // Terme boss : WaterfallGiant « scale » (BuffIntent) → le moteur de scaling (power) compte.
        var iteration = scorer.ScoreCard(Card("CARD.ITERATION"), DefectCtx);
        var boss = Assert.Single(iteration.Terms, t => t.Label.StartsWith("Adéquation boss", StringComparison.Ordinal));
        Assert.Equal("WaterfallGiant", boss.Label["Adéquation boss — ".Length..]);
        Assert.Contains("monsters.json", boss.Source);
        Assert.Contains("scaling", boss.Source);

        // Deux passes : aucun état caché, mêmes totaux et mêmes libellés.
        var again = ids.Select(id => scorer.ScoreCard(Card(id), DefectCtx)).ToArray();
        Assert.Equal(scores.Select(s => s.Total), again.Select(s => s.Total));
        Assert.Equal(
            scores.Select(s => string.Join("|", s.Terms.Select(t => $"{t.Label}:{t.Points}"))),
            again.Select(s => string.Join("|", s.Terms.Select(t => $"{t.Label}:{t.Points}"))));
    }

    [Fact]
    public void Poison_synergy_is_named_and_cites_its_rule()
    {
        var meta = MetaTable.Load();
        var synergy = new SynergyEngine(meta);

        // 5 cartes Poison réelles (nom anglais poison / envenom / noxious / snakebite).
        var poisonDeck = new[]
        {
            "CARD.DEADLY_POISON", "CARD.POISONED_STAB", "CARD.NOXIOUS_FUMES",
            "CARD.ENVENOM", "CARD.SNAKEBITE",
        };
        var ctx = DefectCtx with { Deck = Deck(poisonDeck), CharacterId = "CHARACTER.SILENT" };

        Assert.Contains("poison", synergy.Tags(Card("CARD.DEADLY_POISON")));

        var terms = synergy.Terms(Card("CARD.DEADLY_POISON"), ctx);
        var poison = Assert.Single(terms, t => t.Label.Contains("Poison", StringComparison.Ordinal));

        Assert.Contains("5 cartes", poison.Label);
        Assert.Equal(3, poison.Points);
        Assert.Contains("≥3 cartes", poison.Source);
        Assert.Contains("poison", poison.Source);
        Assert.Contains("nom anglais", poison.Source);

        // Synergie reliques : la relique du même cluster que la carte (archetypes.json) compte.
        var withRelic = ctx with { RelicIds = new[] { "RELIC.SWORD_OF_STONE" } };
        var relicTerms = synergy.Terms(Card("CARD.DEADLY_POISON"), withRelic);
        var cluster = Assert.Single(relicTerms, t => t.Label.StartsWith("Cluster", StringComparison.Ordinal));
        Assert.Equal(2, cluster.Points);
        Assert.Contains("Deadly Poison + Poisoned Stab", cluster.Label);
        Assert.Contains("SWORD_OF_STONE", cluster.Source);
        Assert.Contains("archetypes.json", cluster.Source);
    }

    [Fact]
    public void Total_is_bounded_for_60_cards_drawn_from_data_meta()
    {
        var meta = MetaTable.Load();
        var scorer = new Scorer(meta, PersonalStats.Empty());
        var ids = CardIdsFromDataMeta().Take(60).ToArray();

        Assert.True(ids.Length >= 50, $"cartes lues : {ids.Length}");

        foreach (var id in ids)
            Assert.InRange(scorer.ScoreCard(Card("CARD." + id), DefectCtx).Total, 0, 100);
    }

    [Fact]
    public void Empty_deck_and_empty_offer_stay_bounded()
    {
        var scorer = new Scorer();
        var empty = DefectCtx with { Deck = Array.Empty<CardInstance>() };

        var card = scorer.ScoreCard(Card("CARD.ITERATION"), empty);
        Assert.InRange(card.Total, 0, 100);

        var skipNothing = scorer.ScoreSkip(Array.Empty<CardInstance>(), empty);
        Assert.InRange(skipNothing.Total, 0, 100);

        var skipOffers = scorer.ScoreSkip(Deck(new[] { "CARD.STORM", "CARD.GLACIER" }), empty);
        Assert.InRange(skipOffers.Total, 0, 100);
        Assert.NotEmpty(skipOffers.Terms);
    }

    [Fact]
    public void Facade_gives_the_same_score_as_direct_card_scorer()
    {
        var meta = MetaTable.Load();
        var personal = PersonalStats.Empty();
        var direct = new CardScorer(meta, personal, new SynergyEngine(meta));
        var facade = new Scorer(meta, personal);
        var card = Card("CARD.HAILSTORM");

        var a = direct.ScoreCard(card, DefectCtx);
        var b = facade.ScoreCard(card, DefectCtx);

        Assert.Equal(a.Total, b.Total);
        Assert.Equal(a.Confidence, b.Confidence);
        Assert.Equal(a.Terms.Select(t => (t.Label, t.Points)), b.Terms.Select(t => (t.Label, t.Points)));
    }

    [Fact]
    public void Enchantment_adds_a_named_term_and_stays_bounded()
    {
        var scorer = new Scorer();

        var plain = scorer.ScoreCard(Card("CARD.STORM"), DefectCtx);
        var enchanted = scorer.ScoreCard(new CardInstance("CARD.STORM", 0, new Enchantment("SWIFT", 2), null), DefectCtx);

        var term = Assert.Single(enchanted.Terms, t => t.Label.StartsWith("Enchantement", StringComparison.Ordinal));
        Assert.Equal(5, term.Points); // 3 + min(2, 2)
        Assert.Equal("enchantement", term.Source);
        Assert.Equal(plain.Total + 5, enchanted.Total);
        Assert.InRange(enchanted.Total, 0, 100);
    }

    /// <summary>Ids réels lus dans data/meta/card_tiers.json (aucune liste écrite à la main).</summary>
    private static IEnumerable<string> CardIdsFromDataMeta()
    {
        var dir = ScoringData.ResolveMetaDir();
        var local = Path.Combine(dir, "local", "card_tiers.json");
        var path = File.Exists(local) ? local : Path.Combine(dir, "card_tiers.json");
        Assert.True(File.Exists(path), $"card_tiers.json introuvable : {path}");

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var character in doc.RootElement.GetProperty("characters").EnumerateObject())
            foreach (var card in character.Value.EnumerateObject())
                if (card.Value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    yield return id.GetString()!;
    }
}
