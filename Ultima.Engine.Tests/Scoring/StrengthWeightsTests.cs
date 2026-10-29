using System.Globalization;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Strength;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// S20 — branchement des poids de G calibrés sur le corpus (<c>data/calibration/weights.json</c>).
/// Deux propriétés à prouver : (a) le fichier réel est lu, validé et appliqué ; (b) tout défaut
/// (absent, corrompu, négatif, somme fausse) retombe sur les constantes <b>sans exception</b>.
/// Le test de bout en bout injecte deux jeux de poids opposés et vérifie que G change : sans cela,
/// « le fichier est lu » ne prouverait pas que l'algorithme l'utilise.
/// </summary>
public sealed class StrengthWeightsTests
{
    private const string BossAct1 = "ENCOUNTER.WATERFALL_GIANT_BOSS";

    private static CardInstance Card(string id, int upgrade = 0) => new(id, upgrade, null, null);

    /// <summary>Deck de départ Ironclad : Strike ×5, Defend ×4, Bash (dimensions distinctes).</summary>
    private static List<CardInstance> Starter() => new()
    {
        Card("CARD.STRIKE_IRONCLAD"), Card("CARD.STRIKE_IRONCLAD"), Card("CARD.STRIKE_IRONCLAD"),
        Card("CARD.STRIKE_IRONCLAD"), Card("CARD.STRIKE_IRONCLAD"),
        Card("CARD.DEFEND_IRONCLAD"), Card("CARD.DEFEND_IRONCLAD"),
        Card("CARD.DEFEND_IRONCLAD"), Card("CARD.DEFEND_IRONCLAD"),
        Card("CARD.BASH"),
    };

    private static ScoringContext Ctx(IEnumerable<CardInstance> deck, int act = 0) =>
        new("CHARACTER.IRONCLAD", act, BossAct1, 0, 10, deck.ToArray(),
            Array.Empty<string>(), 99, 70, 70);

    /// <summary>Fichier de calibration minimal mais valide pour un acte donné.</summary>
    private static string WeightsJson(
        double survie, double offense, double consistance, double couverture,
        string schema = StrengthWeights.Schema, int runs = 1234)
    {
        string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        return $$"""
        {
          "schema": "{{schema}}",
          "generated_at": "2026-10-29T15:01:15Z",
          "source": { "runs_final": {{runs}} },
          "proposed_weights": {
            "act_1": { "weights": {
              "survie": {{N(survie)}}, "offense": {{N(offense)}},
              "consistance": {{N(consistance)}}, "couverture": {{N(couverture)}} } }
          }
        }
        """;
    }

    [Fact]
    public void Real_file_is_loaded_validated_and_covers_acts_1_to_3()
    {
        var path = StrengthWeights.ResolvePath();
        Assert.False(string.IsNullOrWhiteSpace(path), "data/calibration/weights.json doit être localisable depuis les tests");

        var calibration = StrengthWeights.Load();
        Assert.True(calibration.IsCalibrated, $"repli inattendu : {calibration.Problem}");
        Assert.Null(calibration.Problem);
        Assert.Equal(3, calibration.Acts.Count);

        // Acte 1 : Survie 85 %, Offense 10 %, Consistance 0 %, Couverture 5 % (proposed_weights.act_1).
        var act1 = calibration.ForActIndex(0);
        Assert.Equal(1.0, act1.Sum, 6);
        Assert.InRange(act1.Survie, 0.84, 0.86);
        Assert.InRange(act1.Offense, 0.09, 0.11);
        Assert.InRange(act1.Consistance, 0.0, 1e-9);
        Assert.InRange(act1.Couverture, 0.04, 0.06);

        // Acte 2 : Survie 82 %, Offense 0 %, Consistance 18 %, Couverture ≈ 0 %.
        var act2 = calibration.ForActIndex(1);
        Assert.Equal(1.0, act2.Sum, 6);
        Assert.InRange(act2.Survie, 0.80, 0.84);
        Assert.InRange(act2.Offense, 0.0, 1e-9);
        Assert.InRange(act2.Consistance, 0.17, 0.19);

        // Acte 3 : déclaré non identifiable dans le fichier → constantes du code, somme 1.
        var act3 = calibration.ForActIndex(2);
        Assert.Equal(StrengthWeights.DefaultAct3, act3);
        Assert.Equal(1.0, act3.Sum, 6);
        Assert.Equal(StrengthWeights.DefaultAct3, calibration.ForActIndex(7));

        // Provenance lisible : nombre de runs du fit et date, comme demandé par l'explication.
        Assert.Equal(71634, calibration.Runs);
        Assert.Equal("2026-10-29", calibration.Date);
        Assert.Contains("calibré", calibration.Source);
        Assert.Contains("71 634", calibration.Source);
        Assert.Contains("poids calibrés sur 71 634 runs", calibration.Provenance);
    }

    [Fact]
    public void Cached_calibration_is_the_effective_one_and_is_loaded_once()
    {
        var current = StrengthWeights.Current;
        Assert.Same(current, StrengthWeights.Current);
        Assert.Equal(current.Source, StrengthWeights.Source);
        Assert.Equal(current.IsCalibrated, StrengthWeights.IsCalibrated);

        // Poids effectifs = ceux du cache, pas les constantes écrites à la main (quand le fichier existe).
        Assert.Equal(current.ForActIndex(0), StrengthWeights.Act1);
        Assert.Equal(current.ForActIndex(1), StrengthWeights.Act2);
        Assert.Equal(current.ForActIndex(2), StrengthWeights.Act3);
    }

    [Fact]
    public void Missing_file_falls_back_to_constants_without_throwing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"ultima-weights-{Guid.NewGuid():N}.json");
        var calibration = StrengthWeights.Load(missing);

        Assert.False(calibration.IsCalibrated);
        Assert.Equal("constantes par défaut", calibration.Source);
        Assert.Contains("poids par défaut", calibration.Provenance);
        Assert.Contains("absent", calibration.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StrengthWeights.DefaultAct1, calibration.ForActIndex(0));
        Assert.Equal(StrengthWeights.DefaultAct2, calibration.ForActIndex(1));
        Assert.Equal(StrengthWeights.DefaultAct3, calibration.ForActIndex(2));
        Assert.Equal(StrengthWeights.DefaultAct3, calibration.ForActIndex(9));
    }

    [Fact]
    public void Corrupted_file_falls_back_to_constants_without_throwing()
    {
        foreach (var broken in new[]
                 {
                     "{ ceci n'est pas du JSON",
                     "[]",
                     """{ "schema": "ultima.calibration.weights/1" }""",
                     """{ "schema": "autre.schema/1", "proposed_weights": {} }""",
                     """{ "schema": "ultima.calibration.weights/1", "generated_at": "2026-10-29T15:01:15Z", "source": { "runs_final": 1 }, "proposed_weights": { "act_1": { "weights": { "survie": 1 } } } }""",
                 })
        {
            var calibration = StrengthWeights.Parse(broken, "fichier de test");
            Assert.False(calibration.IsCalibrated, $"contenu accepté à tort : {broken}");
            Assert.Equal(StrengthWeights.DefaultAct1, calibration.ForActIndex(0));
            Assert.False(string.IsNullOrWhiteSpace(calibration.Problem));
            Assert.Equal("constantes par défaut", calibration.Source);
        }

        // Un fichier absent et un fichier vide suivent la même voie.
        Assert.False(StrengthWeights.Parse("", "vide").IsCalibrated);
    }

    [Fact]
    public void Negative_or_incoherent_weights_are_rejected()
    {
        // Poids négatif : la calibration est rejetée (jamais de dimension qui « retire » des points).
        var negative = StrengthWeights.Parse(WeightsJson(-0.10, 0.60, 0.30, 0.20), "négatif");
        Assert.False(negative.IsCalibrated);
        Assert.Contains("[0, 1]", negative.Problem);
        Assert.Equal(StrengthWeights.DefaultAct1, negative.ForActIndex(0));

        // Somme ≠ 1 : rejetée aussi (le total ne serait plus sur l'échelle 0..100).
        var sum = StrengthWeights.Parse(WeightsJson(0.50, 0.50, 0.50, 0.50), "somme");
        Assert.False(sum.IsCalibrated);
        Assert.Contains("somme", sum.Problem, StringComparison.OrdinalIgnoreCase);

        // Schéma inconnu : rejeté (un fichier d'une autre version n'est pas interprété au hasard).
        var schema = StrengthWeights.Parse(WeightsJson(0.40, 0.30, 0.20, 0.10, schema: "ultima.calibration.weights/2"), "schéma");
        Assert.False(schema.IsCalibrated);
        Assert.Contains("schéma", schema.Problem, StringComparison.OrdinalIgnoreCase);

        // Métadonnées manquantes (n et date servent à l'explication) : rejetées.
        var noRuns = StrengthWeights.Parse(WeightsJson(0.40, 0.30, 0.20, 0.10, runs: 0), "runs");
        Assert.False(noRuns.IsCalibrated);
        Assert.Contains("runs_final", noRuns.Problem);
    }

    [Fact]
    public void Valid_custom_file_is_applied_and_normalized_to_sum_one()
    {
        // Somme à 0,9999 comme le fit réel (arrondi 4 décimales) : appliquée et renormalisée.
        var json = WeightsJson(0.8180, 0.0, 0.1798, 0.0021, runs: 15072);
        var calibration = StrengthWeights.Parse(json, "fichier de test");

        Assert.True(calibration.IsCalibrated, calibration.Problem);
        Assert.Equal(1.0, calibration.ForActIndex(0).Sum, 9);
        Assert.InRange(calibration.ForActIndex(0).Consistance, 0.1798, 0.1800);
        Assert.Contains("15 072", calibration.Source);
        Assert.Equal("2026-10-29", calibration.Date);
    }

    [Fact]
    public void G_uses_calibrated_weights_by_default()
    {
        var strength = PlayerStrength.G(Ctx(Starter()));

        Assert.Equal(StrengthWeights.Current.ForActIndex(0), strength.Weights);
        Assert.Equal(StrengthWeights.Current.Provenance, strength.WeightSource);

        var survie = Assert.Single(strength.Terms, t => t.Label == "Survie");
        Assert.Contains("0.85", survie.Source);
        if (StrengthWeights.Current.IsCalibrated)
            Assert.Contains("poids calibrés sur 71 634 runs", survie.Source);
    }

    [Fact]
    public void Injecting_different_weights_changes_G_and_the_explanation()
    {
        var deck = Starter();
        var ctx = Ctx(deck);

        var survivalFirst = WeightCalibration.Injected(new[]
        {
            new ActWeights(0.85, 0.10, 0.00, 0.05),
            new ActWeights(0.82, 0.00, 0.18, 0.00),
            new ActWeights(0.30, 0.40, 0.15, 0.15),
        }, "poids injectés (test, survie d'abord)");

        var offenseFirst = WeightCalibration.Injected(new[]
        {
            new ActWeights(0.10, 0.80, 0.05, 0.05),
            new ActWeights(0.10, 0.80, 0.05, 0.05),
            new ActWeights(0.10, 0.80, 0.05, 0.05),
        }, "poids injectés (test, offense d'abord)");

        var a = new PlayerStrengthCalculator(calibration: survivalFirst).Compute(ctx);
        var b = new PlayerStrengthCalculator(calibration: offenseFirst).Compute(ctx);

        // Mêmes dimensions (mêmes entrées), totaux différents : c'est bien le branchement des poids.
        Assert.Equal(a.Survie, b.Survie);
        Assert.Equal(a.Offense, b.Offense);
        Assert.Equal(a.Consistance, b.Consistance);
        Assert.Equal(a.Couverture, b.Couverture);
        Assert.NotEqual(a.Total, b.Total);

        Assert.Equal(survivalFirst.ForActIndex(0), a.Weights);
        Assert.Equal("poids injectés (test, survie d'abord)", a.WeightSource);
        Assert.Equal(offenseFirst.ForActIndex(0), b.Weights);
        Assert.Equal("poids injectés (test, offense d'abord)", b.WeightSource);

        // L'explication porte la provenance injectée, pas celle du fichier.
        Assert.All(a.Terms.Where(t => t.Label is "Survie" or "Offense" or "Consistance" or "Couverture"),
            term => Assert.Contains("poids injectés (test, survie d'abord)", term.Source));

        // La somme des poids reste 1 et le total reste la combinaison linéaire des dimensions.
        Assert.Equal(1.0, a.Weights.Sum, 9);
        Assert.Equal(a.Weights.Apply(a.Survie, a.Offense, a.Consistance, a.Couverture), a.Total);
    }

    [Fact]
    public void Weights_follow_the_act_and_are_reproducible()
    {
        var deck = Starter();

        var act1 = PlayerStrength.G(Ctx(deck, act: 0));
        var act2 = PlayerStrength.G(Ctx(deck, act: 1));
        var act3 = PlayerStrength.G(Ctx(deck, act: 2));
        var again = PlayerStrength.G(Ctx(deck, act: 0));

        Assert.Equal(StrengthWeights.Current.ForActIndex(0), act1.Weights);
        Assert.Equal(StrengthWeights.Current.ForActIndex(1), act2.Weights);
        Assert.Equal(StrengthWeights.Current.ForActIndex(2), act3.Weights);

        // Reproductibilité : aucune lecture d'état caché entre deux appels.
        Assert.Equal(act1.Survie, again.Survie);
        Assert.Equal(act1.Offense, again.Offense);
        Assert.Equal(act1.Consistance, again.Consistance);
        Assert.Equal(act1.Couverture, again.Couverture);
        Assert.Equal(act1.Total, again.Total);
        Assert.Equal(act1.Weights, again.Weights);
        Assert.Equal(act1.WeightSource, again.WeightSource);

        // Acte 1 : la survie pèse plus que l'offense ; acte 2 : l'offense ne pèse plus rien dans la
        // calibration publiée, la consistance remonte (85/10/0/5 puis 82/0/18/0). Si le fichier est
        // absent, les constantes gardent l'ordre historique survie ≥ offense à l'acte 1.
        Assert.True(act1.Weights.Survie > act1.Weights.Offense);
        if (StrengthWeights.Current.IsCalibrated)
        {
            Assert.True(act2.Weights.Survie > act2.Weights.Consistance);
            Assert.True(act2.Weights.Consistance > act2.Weights.Offense);
        }
    }

    [Fact]
    public void Terms_keep_their_existing_labels()
    {
        // Contrat d'affichage : seuls les libellés existants sont produits, la provenance s'ajoute
        // dans la source du terme (pas dans son label).
        var strength = PlayerStrength.G(Ctx(Starter()));
        foreach (var label in new[] { "Survie", "Offense", "Consistance", "Couverture" })
            Assert.Single(strength.Terms, t => t.Label == label);
    }
}
