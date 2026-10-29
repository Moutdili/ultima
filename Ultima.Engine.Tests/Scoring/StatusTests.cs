using System.Text.Json;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Curses;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Keywords;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Agent S27 « Statuts » (docs/35, section statuts) : les 17 statuts sont classés par leur
/// <b>effet décrit</b> — le corpus ne peut pas les classer (cartes temporaires, n = 0 à 3) —
/// et la pénalité suit la famille : sévère −12, modéré −6, bénin −3, non classé −6 explicite.
///
/// Les catalogues sont chargés explicitement depuis la racine du dépôt : ces tests ne dépendent
/// ni de <c>ULTIMA_DATA</c> ni de fichiers non commités (<c>data/codex</c>).
/// </summary>
public sealed class StatusTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "catalog")) &&
                File.Exists(Path.Combine(dir.FullName, "data", "catalog", "status_effects.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("racine du dépôt introuvable depuis " + AppContext.BaseDirectory);
    }

    private static string CatalogDir => Path.Combine(RepoRoot(), "data", "catalog");

    private static readonly StatusCatalog Statuses = StatusCatalog.Load(CatalogDir);
    private static readonly CurseScorer Curses = new(
        CurseCatalog.Load(CatalogDir),
        CardCatalog.LoadFrom(CatalogDir),
        CardEffects.Load(CatalogDir),
        KeywordCatalog.Load(CatalogDir),
        Statuses);

    /// <summary>Les 17 statuts du registre (source exhaustive : data/catalog/registry.json).</summary>
    private static string[] RegistryStatuses()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CatalogDir, "registry.json")));
        return doc.RootElement.GetProperty("entities").GetProperty("card")
            .EnumerateArray()
            .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == "status")
            .Select(e => e.GetProperty("id").GetString()!)
            .ToArray();
    }

    [Fact]
    public void All_17_statuses_have_a_classification_and_no_unknown()
    {
        var registry = RegistryStatuses();
        Assert.Equal(17, registry.Length);
        Assert.Equal(17, Statuses.Count);
        Assert.Equal(
            registry.OrderBy(x => x, StringComparer.Ordinal),
            Statuses.Entries.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal));

        foreach (var id in registry)
        {
            var effect = Statuses.Get(id);
            Assert.NotNull(effect);
            Assert.False(string.IsNullOrWhiteSpace(effect!.Family));
            Assert.False(string.IsNullOrWhiteSpace(effect.EffectFamily));
            Assert.False(string.IsNullOrWhiteSpace(effect.Source));

            // Tout statut connu du registre obtient un verdict de statut, jamais CurseKind.None.
            var verdict = Curses.Classify(id);
            Assert.Equal(CurseKind.Status, verdict.Kind);
            Assert.True(verdict.Penalty < 0, $"{id} doit être pénalisé, vu {verdict.Penalty}");
        }
    }

    [Fact]
    public void Numbered_damage_status_outweighs_a_mere_unplayable()
    {
        var burn = Statuses.Get("BURN")!;
        var toxic = Statuses.Get("TOXIC")!;
        var wound = Statuses.Get("WOUND")!;

        // La valeur vient des données de la carte (vars.Damage), pas d'une estimation.
        Assert.Equal("damage_when_drawn", burn.Family);
        Assert.Equal("end_turn_damage", burn.EffectFamily);
        Assert.Equal(2, burn.Value);
        Assert.Equal(5, toxic.Value);
        Assert.Equal("severe", burn.Band);
        Assert.Equal(CurseScorer.StatusSeverePenalty, burn.Penalty);

        // « Injouable, rien d'autre » : encombre la main, mais ne blesse pas.
        Assert.Equal("unplayable", wound.Family);
        Assert.Equal("moderate", wound.Band);
        Assert.Equal(CurseScorer.StatusPenalty, wound.Penalty);

        // Un dégât chiffré pèse plus qu'une carte morte — même quand le statut s'épuise (Toxic).
        Assert.True(Curses.Penalty("CARD.BURN") < Curses.Penalty("CARD.WOUND"));
        Assert.True(Curses.Penalty("CARD.TOXIC") < Curses.Penalty("CARD.WOUND"));

        // Le terme cite la description et la valeur, pas seulement un chiffre.
        var verdict = Curses.Classify("CARD.BURN");
        Assert.Equal(CurseScorer.StatusSevereLabel, verdict.Label);
        Assert.Contains("subissez", verdict.Source);
        Assert.Contains("valeur 2", verdict.Source);
        Assert.Contains("end_turn_damage", verdict.Source);
    }

    [Fact]
    public void Unclassifiable_status_gets_an_explicit_minus_six()
    {
        // Placeholder du jeu, sans texte ni effet : aucune famille, aucune valeur inventée.
        var effect = Statuses.Get("DEPRECATED_CARD")!;
        Assert.Equal("other", effect.Family);
        Assert.Equal("unclassified", effect.EffectFamily);
        Assert.Null(effect.DescriptionFr);
        Assert.Null(effect.Value);
        Assert.Null(effect.Severity);
        Assert.Equal("unclassified", effect.Band);

        var verdict = Curses.Classify("CARD.DEPRECATED_CARD");
        Assert.Equal(CurseKind.Status, verdict.Kind);
        Assert.Equal(CurseScorer.StatusPenalty, verdict.Penalty);
        Assert.Equal(CurseScorer.StatusUnclassifiedLabel, verdict.Label);
        Assert.Contains("forfait modéré", verdict.Source);
        Assert.Contains("explicite", verdict.Source);
        Assert.Contains("jamais 0", verdict.Source);
    }

    [Fact]
    public void Severe_and_benign_families_use_their_own_bands()
    {
        // Sloth « pas plus de 3 cartes par tour » : sévère même sans mesure de corpus.
        var sloth = Statuses.Get("SLOTH")!;
        Assert.Equal("play_limit", sloth.EffectFamily);
        Assert.Equal(95, sloth.Severity);
        Assert.Equal("severe", sloth.Band);
        Assert.Equal(CurseScorer.StatusSeverePenalty, Curses.Penalty("CARD.SLOTH"));
        Assert.Contains("ne pouvez pas jouer plus de", Curses.Classify("CARD.SLOTH").Source);

        // Slimed s'épuise quand elle est jouée : bénin, pas « encombrement ».
        var slimed = Statuses.Get("SLIMED")!;
        Assert.Equal("self_removing", slimed.EffectFamily);
        Assert.Equal("benign", slimed.Band);
        Assert.Equal(CurseScorer.StatusBenignPenalty, slimed.Penalty);
        Assert.InRange(Curses.Penalty("CARD.SLIMED"), -3, -1); // × 0,6 (Exhaust) sur la bande bénigne

        // Aucune pénalité de statut ne dépasse celle d'une malédiction mesurée forte.
        Assert.True(Math.Abs(Curses.Penalty("CARD.SLOTH")) < Math.Abs(Curses.Penalty("CARD.INJURY")));
    }
}
