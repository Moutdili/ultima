using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Engine.Scoring.Relics;
using Ultima.Engine.Scoring.Shop;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Agent S4 « Boutiquier » : score des articles de shop, du retrait de carte et des reliques.
/// Formule figée docs/24-hermes-arch.md §2 — chaque test vérifie un terme et sa source,
/// jamais un total « magique ».
/// </summary>
public sealed class ShopTests
{
    // ---------------------------------------------------------------- fixtures

    private static ScoringContext Ctx(int gold, params string[] deck) => new(
        "CHARACTER.SILENT", 0, "ENCOUNTER.WATERFALL_GIANT_BOSS", 0, 8,
        deck.Select(id => new CardInstance(id, 0, null, null)).ToArray(),
        Array.Empty<string>(), gold, 70, 70);

    private static MetaEntry Entry(string id, double? consensus, string? type = null, string? rarity = null,
        string owner = "shared", int sources = 1) => new(
        Ids.Bare(id), Ids.Bare(id), Ids.Bare(id), null, type, rarity, null, owner,
        sources > 0
            ? new[] { new SourceRating("test-source", null, consensus, "v0.107.1", "2026-10-28", null, 100) }
            : Array.Empty<SourceRating>(),
        consensus, sources, 0);

    private sealed class FakeMeta : IMetaTable
    {
        public Dictionary<string, MetaEntry> Cards { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, MetaEntry> Relics { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<MetaArchetype>> ArchetypesByCharacter { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MetaEntry? Card(string id) => Cards.TryGetValue(Ids.Bare(id), out var e) ? e : null;
        public MetaEntry? Relic(string id) => Relics.TryGetValue(Ids.Bare(id), out var e) ? e : null;
        public double? SkipRate => 0.6;
        public double? SkipElo => 1500;

        public IReadOnlyList<MetaArchetype> Archetypes(string characterId) =>
            ArchetypesByCharacter.TryGetValue(Ids.CharacterKey(characterId), out var list)
                ? list
                : Array.Empty<MetaArchetype>();

        public string Variant => "test";
        public string? Generated => "2026-10-28";
        public string? PatchTarget => "v0.107.1";
    }

    // ------------------------------------------------- 1. prix vs or disponible

    [Fact]
    public void Unaffordable_item_has_negative_term_and_lower_total()
    {
        var scorer = new Scorer();
        var ctx = Ctx(gold: 99);

        var affordable = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 40, ctx);
        var tooDear = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 250, ctx);

        var blocked = tooDear.Terms.Where(t => t.Label.Contains("Non abordable", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(blocked);
        Assert.All(blocked, t => Assert.True(t.Points < 0, $"« {t.Label} » doit être négatif, vu {t.Points}"));
        Assert.All(blocked, t => Assert.False(string.IsNullOrWhiteSpace(t.Source)));

        Assert.True(tooDear.Total < affordable.Total,
            $"non abordable {tooDear.Total} < abordable {affordable.Total}");
        Assert.InRange(affordable.Total, 0, 100);
        Assert.InRange(tooDear.Total, 0, 100);
    }

    [Fact]
    public void Zero_gold_is_handled_without_division_by_zero()
    {
        var scorer = new Scorer();
        var ctx = Ctx(gold: 0);

        var score = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 50, ctx);

        Assert.InRange(score.Total, 0, 100);
        Assert.Contains(score.Terms, t => t.Label == "Non abordable" && t.Points < 0);
        Assert.Contains(score.Terms, t => t.Label.StartsWith("Économie", StringComparison.Ordinal) && t.Points == -15);
    }

    [Fact]
    public void Free_item_gets_max_economy_and_is_never_unaffordable()
    {
        var scorer = new Scorer();
        var ctx = Ctx(gold: 99);

        var score = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 0, ctx);

        Assert.InRange(score.Total, 0, 100);
        Assert.DoesNotContain(score.Terms, t => t.Label.Contains("Non abordable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(score.Terms, t => t.Label.StartsWith("Économie", StringComparison.Ordinal) && t.Points == 15);
    }

    [Fact]
    public void Sale_price_is_scored_as_displayed_and_never_double_discounted()
    {
        // Le miroir applique déjà MembershipCard / The Courier / IsOnSale au prix affiché.
        // Le scorer note ce prix tel quel : la valeur intrinsèque ne bouge pas, seule
        // l'économie s'améliore — aucune deuxième remise n'est inventée.
        var scorer = new Scorer();
        var ctx = Ctx(gold: 120);

        var full = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 60, ctx);
        var onSale = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 30, ctx);

        static int Term(Score score, string prefix) =>
            score.Terms.Single(t => t.Label.StartsWith(prefix, StringComparison.Ordinal)).Points;

        Assert.Equal(Term(full, "Valeur ("), Term(onSale, "Valeur ("));
        Assert.True(Term(onSale, "Économie") > Term(full, "Économie"));
        Assert.True(onSale.Total > full.Total, $"soldé {onSale.Total} > plein tarif {full.Total}");
    }

    // ------------------------------------------------------- 2. retrait de carte

    [Fact]
    public void Removal_with_a_curse_beats_removal_without()
    {
        var scorer = new Scorer();
        const int price = 75;

        var withCurse = scorer.ScoreRemoval(price, Ctx(gold: 99, "CARD.DECAY", "CARD.STRIKE_SILENT"));
        var withoutCurse = scorer.ScoreRemoval(price, Ctx(gold: 99, "CARD.STRIKE_SILENT", "CARD.DEFEND_SILENT"));

        Assert.Contains(withCurse.Terms, t => t.Label == "Retirer une malédiction" && t.Points == 30);
        Assert.DoesNotContain(withoutCurse.Terms, t => t.Label == "Retirer une malédiction");
        Assert.True(withCurse.Total > withoutCurse.Total,
            $"avec malédiction {withCurse.Total} > sans {withoutCurse.Total}");
        Assert.InRange(withCurse.Total, 0, 100);
        Assert.InRange(withoutCurse.Total, 0, 100);
    }

    [Fact]
    public void Curse_is_detected_from_the_meta_not_from_the_id_name()
    {
        // Id inventé, absent du catalogue du jeu : seule l'entrée méta (Type « Curse ») peut le
        // reconnaître. Un détecteur qui chercherait « CURSE » dans l'id échouerait ici.
        var meta = new FakeMeta();
        meta.Cards["BAD_OMEN_TEST"] = Entry("BAD_OMEN_TEST", consensus: null, type: "Curse", rarity: "Curse", sources: 0);

        Assert.True(CurseIndex.IsCurse(meta, "CARD.BAD_OMEN_TEST"));
        Assert.False(CurseIndex.IsCurse(null, "CARD.BAD_OMEN_TEST"));

        var scorer = new Scorer(meta, PersonalStats.Empty());
        var withCurse = scorer.ScoreRemoval(50, Ctx(gold: 99, "CARD.BAD_OMEN_TEST"));
        var withoutCurse = scorer.ScoreRemoval(50, Ctx(gold: 99, "CARD.STRIKE_SILENT"));

        Assert.Contains(withCurse.Terms, t => t.Label == "Retirer une malédiction");
        Assert.True(withCurse.Total > withoutCurse.Total);
    }

    [Fact]
    public void Removal_gets_more_expensive_with_price()
    {
        var scorer = new Scorer();
        var cheap = scorer.ScoreRemoval(10, Ctx(gold: 100));
        var dear = scorer.ScoreRemoval(150, Ctx(gold: 100));

        Assert.True(dear.Total < cheap.Total, $"retrait cher {dear.Total} < bon marché {cheap.Total}");
        Assert.Contains(dear.Terms, t => t.Label == "Retrait non abordable" && t.Points == 0);
    }

    // ------------------------------------------------------------- 3. reliques

    [Fact]
    public void Rated_relic_scores_in_range_with_sources()
    {
        var scorer = new Scorer();
        var score = scorer.ScoreRelic("RELIC.ANCHOR", Ctx(gold: 99, "CARD.STRIKE_SILENT"));

        Assert.InRange(score.Total, 0, 100);
        Assert.NotEmpty(score.Terms);
        Assert.All(score.Terms, t => Assert.False(string.IsNullOrWhiteSpace(t.Source)));
        Assert.Contains(score.Terms, t => t.Label == "Méta communautaire" && t.Points > 0);
    }

    [Fact]
    public void Unknown_relic_does_not_throw_and_is_explicit()
    {
        var scorer = new Scorer();
        var score = scorer.ScoreRelic("RELIC.PAS_UNE_RELIQUE", Ctx(gold: 99));

        Assert.InRange(score.Total, 0, 100);
        Assert.Equal(0, score.Total);
        Assert.Equal(Confidence.Unknown, score.Confidence);
        var term = Assert.Single(score.Terms);
        Assert.Equal("Aucune donnée", term.Label);
        Assert.Contains("PAS_UNE_RELIQUE", term.Source);
    }

    [Fact]
    public void Ancient_relic_without_consensus_is_explicit_not_a_fake_number()
    {
        var meta = new FakeMeta();
        meta.Relics["SOME_ANCIENT_RELIC"] = Entry("SOME_ANCIENT_RELIC", consensus: null, owner: "ancient_event", sources: 0);

        var scorer = new Scorer(meta, PersonalStats.Empty());
        var score = scorer.ScoreRelic("RELIC.SOME_ANCIENT_RELIC", Ctx(gold: 99));

        Assert.InRange(score.Total, 0, 100);
        Assert.Equal(0, score.Total);
        Assert.Equal(Confidence.Unknown, score.Confidence);
        Assert.DoesNotContain(score.Terms, t => t.Label == "Méta communautaire");
        var note = Assert.Single(score.Terms, t => t.Label == "Pas de note chiffrée");
        Assert.Equal(0, note.Points);
        Assert.Contains("Ancien", note.Source);
    }

    [Fact]
    public void Relic_deck_synergy_is_named_and_capped()
    {
        var meta = new FakeMeta();
        meta.Relics["SWORD_OF_STONE"] = Entry("SWORD_OF_STONE", consensus: 60, owner: "shared");
        meta.ArchetypesByCharacter["silent"] = new List<MetaArchetype>
        {
            new("Claw + Feral", "silent", "test", "cluster_statistique",
                new[] { "CLAW", "FERAL", "SCRAPE" }, new[] { "SWORD_OF_STONE" })
        };

        var scorer = new Scorer(meta, PersonalStats.Empty());
        var score = scorer.ScoreRelic("RELIC.SWORD_OF_STONE",
            Ctx(gold: 99, "CARD.CLAW", "CARD.FERAL", "CARD.SCRAPE", "CARD.STRIKE_SILENT"));

        Assert.InRange(score.Total, 0, 100);
        var cluster = Assert.Single(score.Terms, t => t.Label.StartsWith("Cluster", StringComparison.Ordinal));
        Assert.InRange(cluster.Points, 1, 3);
        Assert.Contains("3/3", cluster.Source);

        int synergy = score.Terms
            .Where(t => t.Label.StartsWith("Cluster", StringComparison.Ordinal) || t.Label == "Relique de ton personnage")
            .Sum(t => t.Points);
        Assert.InRange(synergy, 0, RelicScorer.MaxSynergy);
    }

    // -------------------------------------------------------------- 4. potions

    [Fact]
    public void Potion_kind_scores_bounded_and_explained()
    {
        var scorer = new Scorer();
        var score = scorer.ScoreShopItem("potion", "POTION.FIRE_POTION", 50, Ctx(gold: 99));

        Assert.InRange(score.Total, 0, 100);
        Assert.NotEmpty(score.Terms);
        Assert.All(score.Terms, t => Assert.False(string.IsNullOrWhiteSpace(t.Source)));
        Assert.Contains(score.Terms, t => t.Label == $"Valeur (potion)" && t.Points == (int)Math.Round(ShopScorer.PotionValue * 0.7));
    }

    [Fact]
    public void Unknown_kind_does_not_throw()
    {
        var scorer = new Scorer();
        var score = scorer.ScoreShopItem("relic-bundle", "X", 30, Ctx(gold: 99));

        Assert.InRange(score.Total, 0, 100);
        Assert.Contains(score.Terms, t => t.Source.Contains("inconnu"));
    }

    // ---------------------------------------------------------- 5. intégration

    [Fact]
    public void Facade_scores_shop_item_and_relic_within_bounds()
    {
        var scorer = new Scorer();
        var ctx = Ctx(gold: 120, "CARD.STRIKE_SILENT", "CARD.DEFEND_SILENT");

        var item = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 45, ctx);
        var relic = scorer.ScoreRelic("RELIC.ANCHOR", ctx);

        Assert.InRange(item.Total, 0, 100);
        Assert.InRange(relic.Total, 0, 100);
        Assert.NotEmpty(item.Terms);
        Assert.NotEmpty(relic.Terms);
        Assert.All(item.Terms.Concat(relic.Terms), t => Assert.False(string.IsNullOrWhiteSpace(t.Source)));
    }

    [Fact]
    public void Scores_are_deterministic()
    {
        var scorer = new Scorer();
        var ctx = Ctx(gold: 120, "CARD.DECAY", "CARD.STRIKE_SILENT");

        Assert.Equal(scorer.ScoreShopItem("relic", "RELIC.ANCHOR", 60, ctx).Total,
            scorer.ScoreShopItem("relic", "RELIC.ANCHOR", 60, ctx).Total);
        Assert.Equal(scorer.ScoreRemoval(75, ctx).Total, scorer.ScoreRemoval(75, ctx).Total);
        Assert.Equal(scorer.ScoreRelic("RELIC.ANCHOR", ctx).Total, scorer.ScoreRelic("RELIC.ANCHOR", ctx).Total);
    }

    // ------------------------------------------------------ catalogue de malédictions

    [Fact]
    public void Catalog_fallback_knows_real_curses_and_ignores_normal_cards()
    {
        // data/catalog/cards.json (type « Curse ») : utilisé car card_tiers.json ne note pas
        // les malédictions. Aucun id en dur dans le code.
        Assert.True(CurseIndex.IsCurse(null, "CARD.DECAY"));
        Assert.True(CurseIndex.IsCurse(null, "CARD.WRITHE"));
        Assert.False(CurseIndex.IsCurse(null, "CARD.STRIKE_SILENT"));
        Assert.False(CurseIndex.IsCurse(null, "CARD.DEFEND_SILENT"));
    }
}
