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
    public void Unaffordable_item_keeps_the_letter_and_says_why()
    {
        // Décision du chef (docs/42, revue 18:10) : « trop cher » ≠ « mauvais ».
        // L'abordabilité est déjà le booléen ShopItem.Affordable ; la note dit la valeur.
        var scorer = new Scorer();
        var ctx = Ctx(gold: 99);

        var affordable = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 40, ctx);
        var tooDear = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 250, ctx);

        Assert.Equal(affordable.Total, tooDear.Total);
        Assert.Equal(affordable.Grade, tooDear.Grade);

        var blocked = Assert.Single(tooDear.Terms, t => t.Label == "Non abordable");
        Assert.Equal(0, blocked.Points);
        Assert.Contains("250 or", blocked.Source);
        Assert.DoesNotContain(affordable.Terms, t => t.Label == "Non abordable");
    }

    [Fact]
    public void Shop_card_letter_is_the_reward_letter()
    {
        var scorer = new Scorer();
        var ctx = Ctx(gold: 120);

        var shop = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 45, ctx);
        var reward = scorer.ScoreCard(new CardInstance("CARD.ALCHEMIZE", 0, null, null), ctx);

        Assert.Equal(reward.Total, shop.Total);
        Assert.Equal(reward.Grade, shop.Grade);
        Assert.Contains(shop.Terms, t => t.Label.StartsWith("Économie", StringComparison.Ordinal) && t.Points == 0);
    }

    [Fact]
    public void Zero_gold_is_handled_without_division_by_zero()
    {
        var scorer = new Scorer();
        var ctx = Ctx(gold: 0);

        var score = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 50, ctx);

        Assert.InRange(score.Total, 0, 100);
        Assert.Contains(score.Terms, t => t.Label == "Non abordable" && t.Points == 0);
        Assert.Contains(score.Terms, t => t.Label.StartsWith("Économie", StringComparison.Ordinal) && t.Points == 0);
    }

    [Fact]
    public void Free_item_is_never_unaffordable()
    {
        var scorer = new Scorer();
        var ctx = Ctx(gold: 99);

        var score = scorer.ScoreShopItem("card", "CARD.ALCHEMIZE", 0, ctx);

        Assert.InRange(score.Total, 0, 100);
        Assert.DoesNotContain(score.Terms, t => t.Label.Contains("Non abordable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(score.Terms, t =>
            t.Label.StartsWith("Économie", StringComparison.Ordinal) && t.Source.Contains("gratuit"));
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

        static string EconomySource(Score score) =>
            score.Terms.Single(t => t.Label.StartsWith("Économie", StringComparison.Ordinal)).Source;

        // La remise ne change pas la valeur de la carte : seule la ligne économie la mentionne.
        Assert.Equal(full.Total, onSale.Total);
        Assert.Equal(full.Grade, onSale.Grade);
        Assert.NotEqual(EconomySource(full), EconomySource(onSale));
        Assert.Contains("60 or", EconomySource(full));
        Assert.Contains("30 or", EconomySource(onSale));
    }

    // ------------------------------------------------------- 2. retrait de carte

    [Fact]
    public void Removal_with_a_curse_beats_removal_without()
    {
        var scorer = new Scorer();
        const int price = 75;

        var withCurse = scorer.ScoreRemoval(price, Ctx(gold: 99, "CARD.DECAY", "CARD.STRIKE_SILENT"));
        var withoutCurse = scorer.ScoreRemoval(price, Ctx(gold: 99, "CARD.STRIKE_SILENT", "CARD.DEFEND_SILENT"));

        // Une seule vérité (T5 §1) : le gain vient du RemovalAdvisor, pas d'un bonus fixe.
        var curseTerm = Assert.Single(withCurse.Terms, t => t.Label.StartsWith("Meilleur retrait"));
        Assert.Contains("toxicité mesurée", curseTerm.Source);
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

        // La reconnaissance vient de la méta (type « Curse »), pas de l'identifiant.
        var candidate = scorer.Removal.Recommend(Ctx(gold: 99, "CARD.BAD_OMEN_TEST")).First();
        Assert.True(candidate.IsCurse, "la carte doit être reconnue comme malédiction par la méta");

        // Cette malédiction synthétique n'a AUCUNE toxicité mesurée : elle ne doit donc pas être
        // présentée comme un gain (le classement « malédiction > Frappe » se prouve avec une vraie
        // malédiction mesurée, test Removal_with_a_curse_beats_removal_without).
        Assert.Equal(0, candidate.Toxicity);
        Assert.False(candidate.WorthIt, "sans toxicité mesurée, retirer cette carte ne vaut pas son prix");
    }

    [Fact]
    public void Price_never_changes_the_letter_only_the_notes()
    {
        var scorer = new Scorer();
        var cheap = scorer.ScoreRemoval(10, Ctx(gold: 100, "CARD.STRIKE_SILENT"));
        var dear = scorer.ScoreRemoval(150, Ctx(gold: 100, "CARD.STRIKE_SILENT"));

        // Le prix est un terme informatif à 0 point : il ne note pas (arbitrage T5 §1).
        Assert.Equal(cheap.Total, dear.Total);
        Assert.All(cheap.Terms.Where(t => t.Label == "Prix du retrait"), t => Assert.Equal(0, t.Points));
        Assert.Contains(dear.Terms, t => t.Label == "Retrait non abordable" && t.Points == 0);

        // Ce qui dépend du prix, c'est la rentabilité, portée par le conseiller.
        var deck = Ctx(gold: 100, "CARD.STRIKE_SILENT");
        var advisor = scorer.Removal.Recommend(deck, 10, 100).First();
        var expensive = scorer.Removal.Recommend(deck, 150, 100).First();
        Assert.True(advisor.WorthIt || !expensive.WorthIt,
            $"un retrait à 10 or doit être plus souvent rentable qu'à 150 (seuils {advisor.BreakEven:0.##} vs {expensive.BreakEven:0.##})");
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
    public void Potion_is_capped_and_marked_not_comparable()
    {
        // Aucune source publique ne note les potions : un placeholder ne doit pas battre une carte
        // mesurée. Note plafonnée + terme explicite + confiance inconnue.
        var scorer = new Scorer();
        var score = scorer.ScoreShopItem("potion", "POTION.FIRE_POTION", 50, Ctx(gold: 99));

        Assert.InRange(score.Total, 0, ShopScorer.PotionCap);
        Assert.Equal(Confidence.Unknown, score.Confidence);
        Assert.All(score.Terms, t => Assert.False(string.IsNullOrWhiteSpace(t.Source)));
        Assert.Contains(score.Terms, t => t.Label == "Non comparable aux cartes" && t.Points == 0);
        Assert.Contains(score.Terms, t => t.Label.StartsWith("Économie", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_kind_does_not_throw()
    {
        var scorer = new Scorer();
        var score = scorer.ScoreShopItem("relic-bundle", "X", 30, Ctx(gold: 99));

        Assert.InRange(score.Total, 0, 100);
        Assert.Equal(Confidence.Unknown, score.Confidence);
        Assert.Contains(score.Terms, t => t.Label.Contains("inconnu", StringComparison.OrdinalIgnoreCase));
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
