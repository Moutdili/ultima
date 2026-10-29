using System.Diagnostics;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Saves;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Preuves de l'agent S5 (docs/24 §5) : note du deck, archétypes issus de la méta, malédictions,
/// cas limites (deck vide, deck de 60 cartes) et intégration par la façade <see cref="Scorer"/>.
/// Aucun test ne lit de champ caché de la save (ni rng, ni odds, ni encounter_ids).
/// </summary>
public sealed class DeckTests
{
    private static readonly IMetaTable Meta = MetaTable.Load();

    private static readonly DeckEvaluator Evaluator =
        new(Meta, new CardScorer(Meta, PersonalStats.Empty(), new SynergyEngine(Meta)));

    private const string Boss = "ENCOUNTER.WATERFALL_GIANT_BOSS";

    private static CardInstance Card(string id) => new(id, 0, null, null);

    private static ScoringContext Ctx(
        IReadOnlyList<CardInstance> deck,
        string? boss = Boss,
        IReadOnlyList<string>? relics = null,
        int hp = 70,
        int maxHp = 70,
        string character = "CHARACTER.SILENT") =>
        new(character, 0, boss, 0, 5, deck, relics ?? Array.Empty<string>(), 99, hp, maxHp);

    /// <summary>Racine du dépôt (contient fixtures/saves et data/meta), trouvée depuis l'assembly.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "fixtures", "saves")) &&
                Directory.Exists(Path.Combine(dir.FullName, "data", "meta")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("racine du dépôt introuvable depuis " + AppContext.BaseDirectory);
    }

    private static string FixtureSave => Path.Combine(
        RepoRoot(), "fixtures", "saves", "root", "steam", "70000000000000001", "profile1", "saves", "current_run.save");

    /// <summary>Contexte bâti sur la fixture réelle (Silent, deck 14, boss Waterfall Giant).</summary>
    private static ScoringContext FixtureContext()
    {
        var run = new RunSaveReader().Read(FixtureSave);
        var p = run.Players[0];
        return new ScoringContext(
            p.CharacterId, run.CurrentActIndex, run.CurrentAct?.BossId, run.Ascension, run.FloorCount,
            p.Deck, p.Relics.Select(r => r.Id).ToArray(), p.Gold, p.CurrentHp, p.MaxHp);
    }

    // ---------------------------------------------------------------- 1. deck vide

    [Fact]
    public void Deck_vide_note_basse_confidence_low_sans_exception()
    {
        var d = Evaluator.Evaluate(Ctx(Array.Empty<CardInstance>()));

        Assert.InRange(d.Score.Total, 0, 100);
        Assert.True(d.Score.Total <= 20, $"note basse attendue pour un deck vide, obtenu {d.Score.Total}");
        Assert.Equal(Confidence.Low, d.Score.Confidence);
        Assert.Null(d.Archetype);
        Assert.Empty(d.Curses);
        Assert.Empty(d.PotentialArchetypes);
    }

    // ---------------------------------------------------------------- 2. malédictions

    [Fact]
    public void Malediction_connue_listee_et_penalisee()
    {
        var clean = new[]
        {
            Card("CARD.GRAVEBLAST"), Card("CARD.THUNDERCLAP"),
            Card("CARD.STRIKE_SILENT"), Card("CARD.DEFEND_SILENT")
        };
        var cursed = clean.Append(Card("CARD.ASCENDERS_BANE")).ToArray();

        var sans = Evaluator.Evaluate(Ctx(clean));
        var avec = Evaluator.Evaluate(Ctx(cursed));

        Assert.Empty(sans.Curses);

        var c = Assert.Single(avec.Curses);
        Assert.Equal("CARD.ASCENDERS_BANE", c.Id);
        Assert.True(c.IsCurse);
        Assert.Equal("Curse", c.Type);
        Assert.Equal("Curse", c.Rarity);
        Assert.False(string.IsNullOrWhiteSpace(c.Name));
        Assert.Null(c.Score);

        var malus = Assert.Single(avec.Score.Terms, t => t.Label.Contains("Malédiction", StringComparison.OrdinalIgnoreCase));
        Assert.True(malus.Points < 0, $"terme négatif attendu, obtenu {malus.Points}");
        Assert.Contains(c.Name, malus.Source, StringComparison.OrdinalIgnoreCase);
        Assert.True(avec.Score.Total < sans.Score.Total,
            $"la malédiction doit baisser la note ({avec.Score.Total} vs {sans.Score.Total})");
    }

    [Fact]
    public void Malediction_detectee_par_la_meta_sans_passage_par_le_catalogue()
    {
        // Méta minimale : une seule carte, de type Curse, absente du catalogue du jeu.
        var meta = new FakeMeta();
        meta.Cards["MALEDICTION_TEST"] = new MetaEntry(
            "MALEDICTION_TEST", "MaledictionTest", "Malédiction de test", null, "Curse", "Curse", 0, "silent",
            Array.Empty<SourceRating>(), 0, 1, 0);

        var evaluator = new DeckEvaluator(meta, new CardScorer(meta, PersonalStats.Empty(), new SynergyEngine(meta)));
        var d = evaluator.Evaluate(Ctx(new[] { Card("CARD.MALEDICTION_TEST"), Card("CARD.STRIKE_SILENT") }));

        var c = Assert.Single(d.Curses);
        Assert.Equal("Malédiction de test", c.Name);
        Assert.True(c.IsCurse);
        Assert.Contains(d.Score.Terms, t =>
            t.Points < 0 && t.Label.Contains("Malédiction", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- 3. deck réel de la fixture

    [Fact]
    public void Deck_reel_de_la_fixture_est_note_et_explique()
    {
        var ctx = FixtureContext();
        Assert.Equal(14, ctx.Deck.Count);

        var d = Evaluator.Evaluate(ctx);

        Assert.InRange(d.Score.Total, 0, 100);
        Assert.True(d.Score.Terms.Count >= 2,
            "au moins 2 termes attendus, obtenus : " + string.Join(" | ", d.Score.Terms.Select(t => t.Label)));
        Assert.All(d.Score.Terms, t => Assert.False(string.IsNullOrWhiteSpace(t.Source)));
        Assert.All(d.Score.Terms, t => Assert.NotEqual(0, t.Points));
        Assert.Empty(d.Curses);
    }

    // ---------------------------------------------------------------- 4. archétypes

    [Fact]
    public void Archetypes_potentiels_viennent_uniquement_de_la_meta()
    {
        var archetypes = Meta.Archetypes("CHARACTER.SILENT");
        Assert.NotEmpty(archetypes);
        var names = archetypes.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // (a) deck réel : tout nom publié doit venir de la méta (liste possiblement vide).
        var fx = Evaluator.Evaluate(FixtureContext());
        Assert.All(fx.PotentialArchetypes, n => Assert.Contains(n, names));
        if (fx.Archetype is not null) Assert.Contains(fx.Archetype, names);

        // (b) deck construit avec les cartes clés d'un archétype réel : il doit être reconnu.
        var target = archetypes.First(a => a.KeyCardIds.Count >= 3 && a.KeyRelicIds.Count > 0);
        var deck = target.KeyCardIds
            .Select(id => Card("CARD." + id))
            .Concat(new[] { Card("CARD.STRIKE_SILENT"), Card("CARD.DEFEND_SILENT") })
            .ToArray();
        var relics = target.KeyRelicIds.Select(id => "RELIC." + id).ToArray();

        var d = Evaluator.Evaluate(Ctx(deck, relics: relics));

        Assert.NotEmpty(d.PotentialArchetypes);
        Assert.True(d.PotentialArchetypes.Count <= 3);
        Assert.All(d.PotentialArchetypes, n => Assert.Contains(n, names));
        Assert.Equal(d.PotentialArchetypes.Count, d.PotentialArchetypes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(target.Name, d.Archetype);
        Assert.Equal(target.Name, d.PotentialArchetypes[0]);
        Assert.Contains(d.Score.Terms, t =>
            t.Points > 0 && t.Label.Contains("archétype", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- 5. intégration façade

    [Fact]
    public void Facade_Scorer_et_evaluateur_direct_donnent_le_meme_resultat()
    {
        var ctx = FixtureContext();
        var viaFacade = new Scorer().EvaluateDeck(ctx);
        var direct = Evaluator.Evaluate(ctx);

        Assert.Equal(direct.Score.Total, viaFacade.Score.Total);
        Assert.Equal(direct.Score.Confidence, viaFacade.Score.Confidence);
        Assert.Equal(
            direct.Score.Terms.Select(t => (t.Label, t.Points, t.Source)),
            viaFacade.Score.Terms.Select(t => (t.Label, t.Points, t.Source)));
        Assert.Equal(direct.Archetype, viaFacade.Archetype);
        Assert.Equal(direct.PotentialArchetypes, viaFacade.PotentialArchetypes);
        Assert.Equal(direct.Curses.Select(c => c.Id), viaFacade.Curses.Select(c => c.Id));
    }

    // ---------------------------------------------------------------- cas limites

    [Fact]
    public void Boss_inconnu_n_invente_aucun_terme()
    {
        var d = Evaluator.Evaluate(Ctx(new[] { Card("CARD.GRAVEBLAST") }, boss: "ENCOUNTER.BOSS_QUI_N_EXISTE_PAS"));

        Assert.InRange(d.Score.Total, 0, 100);
        Assert.DoesNotContain(d.Score.Terms, t => t.Label.Contains("boss", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Deck_de_60_cartes_reste_sous_5ms()
    {
        var pool = new[]
        {
            "CARD.GRAVEBLAST", "CARD.THUNDERCLAP", "CARD.STRIKE_SILENT",
            "CARD.DEFEND_SILENT", "CARD.NEUTRALIZE", "CARD.SURVIVOR"
        };
        var deck = Enumerable.Range(0, 60)
            .Select(i => new CardInstance(pool[i % pool.Length], i % 3, null, null))
            .ToArray();
        var ctx = new ScoringContext("CHARACTER.SILENT", 0, Boss, 0, 5, deck,
            new[] { "RELIC.RING_OF_THE_SNAKE" }, 99, 55, 70);

        Evaluator.Evaluate(ctx); // chauffe : chargement lazy des catalogues (data/catalog)

        const int iterations = 20;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) Evaluator.Evaluate(ctx);
        sw.Stop();

        double perCall = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(perCall < 5.0, $"attendu < 5 ms par appel sur 60 cartes, mesuré {perCall:0.###} ms");
        Assert.InRange(Evaluator.Evaluate(ctx).Score.Total, 0, 100);
    }

    [Fact]
    public void Deux_appels_donnent_exactement_le_meme_resultat()
    {
        var ctx = FixtureContext();
        var a = Evaluator.Evaluate(ctx);
        var b = Evaluator.Evaluate(ctx);

        Assert.Equal(a.Score.Total, b.Score.Total);
        Assert.Equal(a.Score.Terms.Select(t => (t.Label, t.Points)), b.Score.Terms.Select(t => (t.Label, t.Points)));
        Assert.Equal(a.Archetype, b.Archetype);
        Assert.Equal(a.PotentialArchetypes, b.PotentialArchetypes);
        Assert.Equal(a.Curses.Select(c => c.Id), b.Curses.Select(c => c.Id));
    }

    /// <summary>Méta minimale en mémoire : prouve la détection de malédiction sans catalogue.</summary>
    private sealed class FakeMeta : IMetaTable
    {
        public Dictionary<string, MetaEntry> Cards { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MetaEntry? Card(string id) => Cards.TryGetValue(Ids.Bare(id), out var e) ? e : null;

        public MetaEntry? Relic(string id) => null;

        public double? SkipRate => null;

        public double? SkipElo => null;

        public IReadOnlyList<MetaArchetype> Archetypes(string characterId) => Array.Empty<MetaArchetype>();

        public string Variant => "test";

        public string? Generated => null;

        public string? PatchTarget => null;
    }
}
