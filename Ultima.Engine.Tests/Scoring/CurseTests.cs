using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Curses;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Engine.Scoring.Relics;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Agent S25 « Malédictions » (docs/35) : pénalité tirée de l'impact mesuré (63 634 runs entrées
/// en acte 3) au lieu du −30 forfaitaire, conseiller de retrait qui compare le gain de G au prix
/// réel (75 or, 100 à A6+), et valeur nette d'une relique qui impose une malédiction.
///
/// Les catalogues sont chargés <b>explicitement</b> depuis la racine du dépôt : ces tests ne
/// dépendent donc pas de la variable d'environnement <c>ULTIMA_DATA</c>, qui pointe ailleurs sur
/// certaines machines et ferait lire un autre jeu de données.
/// </summary>
public sealed class CurseTests
{
    // ---------------------------------------------------------------- fixtures

    /// <summary>Racine du dépôt (contient data/catalog), trouvée depuis l'assembly.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "catalog")) &&
                File.Exists(Path.Combine(dir.FullName, "data", "catalog", "curse_impact.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("racine du dépôt introuvable depuis " + AppContext.BaseDirectory);
    }

    private static string CatalogDir => Path.Combine(RepoRoot(), "data", "catalog");

    private static readonly CurseCatalog Catalog = CurseCatalog.Load(CatalogDir);
    private static readonly CardCatalog Cards = CardCatalog.LoadFrom(CatalogDir);
    private static readonly CurseScorer Curses = new(Catalog, Cards);

    private static MetaEntry Entry(string id, double? consensus, string? type, string? rarity,
        int sources = 1) => new(
        Ids.Bare(id), Ids.Bare(id), Ids.Bare(id), null, type, rarity, null, "shared",
        sources > 0
            ? new[] { new SourceRating("test-source", null, consensus, "v0.107.1", "2026-10-28", null, 100) }
            : Array.Empty<SourceRating>(),
        consensus, sources, 0);

    /// <summary>Méta minimale : deux malédictions notées pour comparer les pénalités à base égale.</summary>
    private sealed class FakeMeta : IMetaTable
    {
        public Dictionary<string, MetaEntry> CardsById { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, MetaEntry> RelicsById { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MetaEntry? Card(string id) => CardsById.TryGetValue(Ids.Bare(id), out var e) ? e : null;
        public MetaEntry? Relic(string id) => RelicsById.TryGetValue(Ids.Bare(id), out var e) ? e : null;
        public double? SkipRate => 0.5;
        public double? SkipElo => 1500;
        public IReadOnlyList<MetaArchetype> Archetypes(string characterId) => Array.Empty<MetaArchetype>();
        public string Variant => "test";
        public string? Generated => "2026-10-28";
        public string? PatchTarget => "v0.107.1";
    }

    private static CardInstance Card(string id, int upgrade = 0) => new(id, upgrade, null, null);

    /// <summary>Deck Silent de 12 cartes avec deux malédictions : assez gros pour que G mesure.</summary>
    private static IReadOnlyList<CardInstance> Deck() => new[]
    {
        Card("STRIKE_SILENT"), Card("STRIKE_SILENT"), Card("STRIKE_SILENT"), Card("STRIKE_SILENT"),
        Card("DEFEND_SILENT"), Card("DEFEND_SILENT"), Card("DEFEND_SILENT"), Card("DEFEND_SILENT"),
        Card("NEUTRALIZE"), Card("SURVIVOR"), Card("POOR_SLEEP"), Card("CLUMSY"),
    };

    private static ScoringContext Ctx(int ascension = 5, int gold = 200,
        IReadOnlyList<CardInstance>? deck = null) => new(
        "CHARACTER.SILENT", 0, "ENCOUNTER.WATERFALL_GIANT_BOSS", ascension, 20,
        deck ?? Deck(), Array.Empty<string>(), gold, 62, 70);

    /// <summary>Méta de test pour les reliques : trois reliques notées, dont deux à malédiction.</summary>
    private static FakeMeta RelicMeta()
    {
        var meta = new FakeMeta();
        meta.RelicsById["CALLING_BELL"] = Entry("CALLING_BELL", consensus: 39, type: null, rarity: null);
        meta.RelicsById["NEOWS_BONES"] = Entry("NEOWS_BONES", consensus: 20, type: null, rarity: null);
        meta.RelicsById["ANCHOR"] = Entry("ANCHOR", consensus: 55, type: null, rarity: null);
        return meta;
    }

    private static ScoreTerm CurseTerm(Score score) =>
        Assert.Single(score.Terms, t => t.Label.StartsWith("Malédiction", StringComparison.Ordinal));

    // ------------------------------------------------- 1. le catalogue mesuré

    [Fact]
    public void Catalog_carries_the_measured_corpus_and_baseline()
    {
        Assert.Equal(63_634, Catalog.SampleRuns);
        Assert.InRange(Catalog.BaselineWinRate, 78.0, 79.0);
        Assert.Equal(300, Catalog.MinSample);
        Assert.True(Catalog.Count >= 17, $"entrées lues : {Catalog.Count}");
        Assert.Null(Catalog.Problem);
        Assert.Contains(Catalog.Entries, e => e.Id == "POOR_SLEEP" && e.Toxicity == 97 && e.Reliable);
        Assert.Contains(Catalog.Entries, e => e.Id == "INJURY" && e.N == 5190 && e.DeltaPp < -3);
    }

    [Fact]
    public void Lookup_is_canonical_whatever_the_id_spelling()
    {
        Assert.NotNull(Catalog.Get("POOR_SLEEP"));
        Assert.NotNull(Catalog.Get("CARD.POOR_SLEEP"));
        Assert.NotNull(Catalog.Get("PoorSleep"));
        Assert.Null(Catalog.Get("STRIKE_SILENT"));
        Assert.Null(Catalog.Get("CARD.PAS_UNE_CARTE"));
    }

    // ------------------------------------------------- 2. la pénalité dérivée

    [Fact]
    public void Penalty_follows_the_published_toxicity_scale()
    {
        // −round(toxicité × 0,26) : les trois ancres de docs/35.
        Assert.Equal(-25, Curses.Penalty("CARD.POOR_SLEEP"));
        Assert.Equal(-17, Curses.Penalty("CARD.INJURY"));
        Assert.Equal(-14, Curses.Penalty("CARD.CLUMSY"));

        // Monotonie : plus la toxicité mesurée est haute, plus la pénalité est lourde.
        Assert.True(Curses.Penalty("POOR_SLEEP") < Curses.Penalty("INJURY"));
        Assert.True(Curses.Penalty("INJURY") < Curses.Penalty("CLUMSY"));
        Assert.True(Curses.Penalty("CLUMSY") <= Curses.Penalty("REGRET"));

        // Plus de −30 forfaitaire : deux malédictions différentes ne valent plus pareil.
        Assert.NotEqual(-30, Curses.Penalty("POOR_SLEEP"));
        Assert.NotEqual(Curses.Penalty("POOR_SLEEP"), Curses.Penalty("CLUMSY"));
    }

    [Fact]
    public void Measured_verdict_names_its_source_and_sample()
    {
        var verdict = Curses.Classify("CARD.POOR_SLEEP");

        Assert.Equal(CurseKind.Curse, verdict.Kind);
        Assert.Equal(CurseScorer.MeasuredLabel, verdict.Label);
        Assert.Contains("63 634", verdict.Source);
        Assert.Contains("POOR_SLEEP", verdict.Source);
        Assert.Contains("97/100", verdict.Source);
        Assert.NotNull(verdict.Measured);
        Assert.Equal(314, verdict.Measured!.N);
    }

    [Fact]
    public void PoorSleep_costs_more_than_Clumsy_in_the_card_score()
    {
        var meta = new FakeMeta();
        meta.CardsById["POOR_SLEEP"] = Entry("POOR_SLEEP", consensus: 60, type: "Curse", rarity: "Curse");
        meta.CardsById["CLUMSY"] = Entry("CLUMSY", consensus: 60, type: "Curse", rarity: "Curse");

        var scorer = new CardScorer(meta, PersonalStats.Empty(), new SynergyEngine(meta), curses: Curses);

        var poorSleep = scorer.ScoreCard(Card("CARD.POOR_SLEEP"), Ctx());
        var clumsy = scorer.ScoreCard(Card("CARD.CLUMSY"), Ctx());

        var poorSleepTerm = CurseTerm(poorSleep);
        var clumsyTerm = CurseTerm(clumsy);

        Assert.Equal(-25, poorSleepTerm.Points);
        Assert.Equal(-14, clumsyTerm.Points);
        Assert.True(poorSleepTerm.Points < clumsyTerm.Points);
        Assert.True(poorSleep.Total < clumsy.Total, $"Poor Sleep {poorSleep.Total} < Clumsy {clumsy.Total}");
        Assert.Contains("63 634", poorSleepTerm.Source);
        Assert.Contains("9,46", poorSleepTerm.Source);   // −9,46 pp mesuré
    }

    // --------------------------------------- 3. malédiction inconnue, statut, n faible

    [Fact]
    public void Unknown_curse_gets_an_explicit_never_zero_penalty()
    {
        // Bad Luck est une vraie malédiction du jeu, absente du corpus (jamais acquise en acte 3).
        var verdict = Curses.Classify("CARD.BAD_LUCK");

        Assert.Equal(CurseKind.Curse, verdict.Kind);
        Assert.Equal(CurseScorer.UnmeasuredPenalty, verdict.Penalty);
        Assert.NotEqual(0, verdict.Penalty);
        // Depuis que le catalogue est exhaustif, BAD_LUCK existe avec n = 0 : elle n'est plus
        // « inconnue » mais « non classable » — le libellé le dit, la pénalité reste explicite.
        Assert.Equal(CurseScorer.UnmeasuredLabel, verdict.Label);
        Assert.Contains("non classable", verdict.Label);
        Assert.Contains("BAD_LUCK", verdict.Source);

        // Et le terme arrive bien dans le score de carte.
        var meta = new FakeMeta();
        var scorer = new CardScorer(meta, PersonalStats.Empty(), new SynergyEngine(meta), curses: Curses);
        var score = scorer.ScoreCard(Card("CARD.BAD_LUCK"), Ctx());
        Assert.Equal(-12, CurseTerm(score).Points);
    }

    [Fact]
    public void Unreliable_sample_is_flagged_not_ranked()
    {
        // Ascender's Bane : n = 6, hors échantillon — la toxicité 26 du fichier est du bruit.
        var verdict = Curses.Classify("CARD.ASCENDERS_BANE");

        Assert.Equal(CurseKind.Curse, verdict.Kind);
        Assert.Equal(CurseScorer.UnmeasuredLabel, verdict.Label);
        Assert.Equal(CurseScorer.UnmeasuredPenalty, verdict.Penalty);
        Assert.Contains("n = 6", verdict.Source);
        Assert.Contains("non classable", verdict.Source);
    }

    [Fact]
    public void Status_is_scored_apart_with_a_weak_documented_penalty()
    {
        var burn = Curses.Classify("CARD.BURN");
        var sloth = Curses.Classify("CARD.SLOTH");

        Assert.Equal(CurseKind.Status, burn.Kind);
        Assert.Equal(CurseScorer.StatusPenalty, burn.Penalty);
        Assert.Equal(CurseScorer.StatusPenalty, sloth.Penalty);
        Assert.Equal(CurseScorer.StatusLabel, burn.Label);
        Assert.Contains("la data ne classe pas les statuts", burn.Source);

        // Un statut pèse la moitié d'une malédiction générique (−6 vs −12), exactement le rapport
        // de G entre StatusPenalty (3) et CursePenalty (6) : deux fois moins qu'un emplacement mort.
        Assert.Equal(CurseScorer.UnmeasuredPenalty / 2, CurseScorer.StatusPenalty);
        Assert.True(Math.Abs(burn.Penalty) < Math.Abs(Curses.Penalty("CARD.CLUMSY")));
        Assert.True(Math.Abs(burn.Penalty) < Math.Abs(Curses.Penalty("CARD.INJURY")));
    }

    [Fact]
    public void Ordinary_card_is_not_penalised_at_all()
    {
        var verdict = Curses.Classify("CARD.STRIKE_SILENT");

        Assert.Equal(CurseKind.None, verdict.Kind);
        Assert.Equal(0, verdict.Penalty);
        Assert.Null(verdict.Term);
    }

    [Fact]
    public void Missing_catalog_yields_an_explicit_penalty_not_zero()
    {
        var meta = new FakeMeta();
        meta.CardsById["POOR_SLEEP"] = Entry("POOR_SLEEP", consensus: null, type: "Curse", rarity: "Curse", sources: 0);

        var withoutData = new CurseScorer(CurseCatalog.Empty, Cards);
        var verdict = withoutData.Classify("CARD.POOR_SLEEP", meta);

        Assert.Equal(CurseKind.Curse, verdict.Kind);
        Assert.Equal(CurseScorer.UnmeasuredPenalty, verdict.Penalty);
        Assert.NotEqual(0, verdict.Penalty);
        Assert.Contains("aucune donnée mesurée", verdict.Source);
    }

    // ------------------------------------------------- 4. conseiller de retrait

    [Fact]
    public void Removal_price_doubles_at_ascension_six()
    {
        Assert.Equal(75, RemovalAdvisor.PriceFor(0));
        Assert.Equal(75, RemovalAdvisor.PriceFor(5));
        Assert.Equal(100, RemovalAdvisor.PriceFor(6));   // Inflation (docs/34)
        Assert.Equal(100, RemovalAdvisor.PriceFor(10));

        Assert.Equal(6.25, RemovalAdvisor.BreakEvenGain(75), 2);
        Assert.Equal(8.33, RemovalAdvisor.BreakEvenGain(100), 2);

        // Le contexte porte l'ascension : le prix suit.
        var advisor = new RemovalAdvisor(curses: Curses, cards: Cards);
        Assert.Equal(75, advisor.Recommend(Ctx(ascension: 5)).First().Price);
        Assert.Equal(100, advisor.Recommend(Ctx(ascension: 6)).First().Price);
    }

    [Fact]
    public void Removal_advisor_ranks_a_curse_before_a_strike()
    {
        var advisor = new RemovalAdvisor(curses: Curses, cards: Cards);
        var ranked = advisor.Recommend(Ctx());

        Assert.NotEmpty(ranked);

        var curse = ranked.Single(c => c.CardId == "POOR_SLEEP");
        var strike = ranked.Single(c => c.CardId == "STRIKE_SILENT");

        Assert.True(curse.IsCurse);
        Assert.False(strike.IsCurse);
        Assert.True(curse.Score > strike.Score, $"malédiction {curse.Score} > Frappe {strike.Score}");
        Assert.Equal(ranked[0].CardId, curse.CardId);   // la malédiction sort en tête
        Assert.Equal(4, strike.Copies);                 // 4 Frappes groupées, une seule retirée
        Assert.Equal(10, curse.CurseBonus);             // toxicité 97 / 10
        Assert.Contains("toxicité mesurée 97/100", curse.Reason);
    }

    [Fact]
    public void Removal_advisor_compares_gain_to_price()
    {
        var advisor = new RemovalAdvisor(curses: Curses, cards: Cards);

        var atA5 = advisor.Recommend(Ctx(ascension: 5, gold: 200));
        var atA6 = advisor.Recommend(Ctx(ascension: 6, gold: 200));

        var poorSleepA5 = atA5.Single(c => c.CardId == "POOR_SLEEP");
        var poorSleepA6 = atA6.Single(c => c.CardId == "POOR_SLEEP");
        var clumsyA6 = atA6.Single(c => c.CardId == "CLUMSY");
        var strikeA6 = atA6.Single(c => c.CardId == "STRIKE_SILENT");

        // Poor Sleep vaut son prix aux deux paliers, Clumsy seulement hors Inflation.
        Assert.True(poorSleepA5.WorthIt);
        Assert.True(poorSleepA6.WorthIt);
        Assert.False(clumsyA6.WorthIt);
        Assert.False(strikeA6.WorthIt);

        // Le seuil est bien prix / 12, pas un nombre magique.
        Assert.Equal(poorSleepA6.Score >= 100 / 12.0, poorSleepA6.WorthIt);
        Assert.Equal(clumsyA6.Score >= 100 / 12.0, clumsyA6.WorthIt);
        Assert.All(atA5, c => Assert.Equal(c.Score >= 75 / 12.0, c.WorthIt));
    }

    [Fact]
    public void Removal_advisor_is_affordable_only_when_gold_covers_the_price()
    {
        var advisor = new RemovalAdvisor(curses: Curses, cards: Cards);

        var poor = advisor.Recommend(Ctx(ascension: 6, gold: 90)).Single(c => c.CardId == "POOR_SLEEP");
        Assert.Equal(100, poor.Price);
        Assert.False(poor.Affordable);
        Assert.True(poor.WorthIt);   // rentable, mais pas payable aujourd'hui

        var rich = advisor.Recommend(Ctx(ascension: 6, gold: 150)).Single(c => c.CardId == "POOR_SLEEP");
        Assert.True(rich.Affordable);
    }

    [Fact]
    public void Removal_advisor_survives_an_empty_deck()
    {
        var advisor = new RemovalAdvisor(curses: Curses, cards: Cards);

        Assert.Empty(advisor.Recommend(Ctx(deck: Array.Empty<CardInstance>())));
    }

    // --------------------------------- 5. intégration par la façade (Scorer)

    [Fact]
    public void Facade_actually_attaches_a_curse_term_to_a_real_curse()
    {
        // Avant S25, la méta publique ne contenant aucune malédiction, le terme « Malédiction »
        // ne s'appliquait JAMAIS à une vraie malédiction (le −30 était du code mort). Ce test
        // échouait avant le passage au catalogue du jeu.
        var scorer = new Scorer();
        var score = scorer.ScoreCard(Card("CARD.POOR_SLEEP"), Ctx());

        var term = CurseTerm(score);
        Assert.True(term.Points <= CurseScorer.UnmeasuredPenalty,
            $"terme attendu ≤ {CurseScorer.UnmeasuredPenalty}, vu {term.Points}");
        Assert.False(string.IsNullOrWhiteSpace(term.Source));
    }

    [Fact]
    public void Facade_exposes_the_removal_advisor()
    {
        var scorer = new Scorer();
        var ranked = scorer.Removal.Recommend(Ctx(ascension: 6, gold: 200));

        Assert.NotEmpty(ranked);
        Assert.All(ranked, c => Assert.Equal(100, c.Price));
        Assert.Contains(ranked, c => c.CardId == "POOR_SLEEP" && c.IsCurse);
    }

    // --------------------------------- 6. relique qui impose une malédiction

    [Fact]
    public void Relic_curse_catalog_only_prices_proven_mappings()
    {
        var relics = RelicCurseCatalog.Load(CatalogDir);

        Assert.Null(relics.Problem);
        var entry = Assert.Single(relics.Entries);
        Assert.Equal("CALLING_BELL", entry.Relic);
        Assert.Equal("CURSE_OF_THE_BELL", entry.Curse);
        Assert.Equal(1, entry.Count);
        Assert.Contains("1 623", entry.Evidence);

        // Les reliques à malédictions aléatoires sont listées comme non chiffrables, pas devinées.
        Assert.Contains("NEOWS_BONES", relics.Rejected);
        Assert.Contains("SERE_TALON", relics.Rejected);
        Assert.Contains("DARKSTONE_PERIAPT", relics.Rejected);
        Assert.Null(relics.Get("NEOWS_BONES")!.Curse);
        Assert.True(relics.Get("NEOWS_BONES")!.IsRandom);
    }

    [Fact]
    public void Relic_with_forced_curse_has_a_lower_net_value()
    {
        var meta = RelicMeta();
        var scorer = new RelicScorer(meta, PersonalStats.Empty(), RelicCatalog.Load(CatalogDir),
            curseRelics: RelicCurseCatalog.Load(CatalogDir), curses: Curses);

        var net = scorer.NetValue("RELIC.CALLING_BELL", Ctx(ascension: 5));

        Assert.True(net.HasForcedCurse);
        Assert.NotNull(net.Curse!.Measured);
        Assert.Equal("CURSE_OF_THE_BELL", net.Curse!.Measured!.Id);
        Assert.Equal(-14, net.CursePenalty);              // toxicité 53 × 0,26
        Assert.Equal(0, net.RemovalCost);                 // pas de retrait supposé par défaut

        Assert.True(net.Net.Total < net.Gross.Total,
            $"net {net.Net.Total} < brut {net.Gross.Total}");
        Assert.Equal(net.Gross.Total - 14, Math.Max(0, net.Net.Total));
        Assert.Contains(net.Net.Terms, t => t.Label == CurseScorer.MeasuredLabel && t.Points == -14);
    }

    [Fact]
    public void Removal_cost_of_the_imposed_curse_depends_on_ascension()
    {
        var meta = RelicMeta();
        var scorer = new RelicScorer(meta, PersonalStats.Empty(), RelicCatalog.Load(CatalogDir),
            curseRelics: RelicCurseCatalog.Load(CatalogDir), curses: Curses);

        var atA5 = scorer.NetValue("RELIC.CALLING_BELL", Ctx(ascension: 5), includeRemovalCost: true);
        var atA6 = scorer.NetValue("RELIC.CALLING_BELL", Ctx(ascension: 6), includeRemovalCost: true);

        Assert.Equal(-6, atA5.RemovalCost);   // round(75 / 12)
        Assert.Equal(-8, atA6.RemovalCost);   // round(100 / 12)
        Assert.True(atA6.Net.Total < atA5.Net.Total);
        Assert.Contains(atA6.Net.Terms, t => t.Label.Contains("100 or", StringComparison.Ordinal));
    }

    [Fact]
    public void Random_curse_relic_is_flagged_but_not_priced()
    {
        var meta = RelicMeta();
        var scorer = new RelicScorer(meta, PersonalStats.Empty(), RelicCatalog.Load(CatalogDir),
            curseRelics: RelicCurseCatalog.Load(CatalogDir), curses: Curses);

        var net = scorer.NetValue("RELIC.NEOWS_BONES", Ctx());

        Assert.False(net.HasForcedCurse);
        Assert.Equal(0, net.CursePenalty);
        Assert.Equal(0, net.RemovalCost);
        Assert.Equal(net.Gross.Total, net.Net.Total);
        var note = Assert.Single(net.Net.Terms, t => t.Label.StartsWith("Malédiction aléatoire", StringComparison.Ordinal));
        Assert.Equal(0, note.Points);
        Assert.False(string.IsNullOrWhiteSpace(note.Source));
    }

    [Fact]
    public void Relic_without_curse_keeps_its_gross_value()
    {
        var meta = RelicMeta();
        var scorer = new RelicScorer(meta, PersonalStats.Empty(), RelicCatalog.Load(CatalogDir),
            curseRelics: RelicCurseCatalog.Load(CatalogDir), curses: Curses);

        var net = scorer.NetValue("RELIC.ANCHOR", Ctx());

        Assert.False(net.HasForcedCurse);
        Assert.Equal(net.Gross.Total, net.Net.Total);
        Assert.Equal(net.Gross.Total, scorer.Score("RELIC.ANCHOR", Ctx()).Total);
    }
}
