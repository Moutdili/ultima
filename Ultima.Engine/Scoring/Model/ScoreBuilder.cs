using Ultima.Engine.Contracts;

namespace Ultima.Engine.Scoring.Model;

/// <summary>
/// Construit un <see cref="Score"/> explicable : Total = somme bornée 0..100 de termes nommés.
/// Aucun nombre magique : chaque terme porte son libellé et sa source.
/// </summary>
public sealed class ScoreBuilder
{
    private readonly List<ScoreTerm> _terms = new();

    public int RawTotal { get; private set; }
    public int Count => _terms.Count;

    public ScoreBuilder Add(string label, int points, string source)
    {
        if (points == 0) return this;
        _terms.Add(new ScoreTerm(label, points, source));
        RawTotal += points;
        return this;
    }

    /// <summary>Ajoute un terme déjà formé (0 point ignoré).</summary>
    public ScoreBuilder Add(ScoreTerm term) => Add(term.Label, term.Points, term.Source);

    public ScoreBuilder AddRange(IEnumerable<ScoreTerm> terms)
    {
        foreach (var t in terms) Add(t);
        return this;
    }

    public Score Build(string confidence) =>
        new(ScoreMath.Clamp(RawTotal, 0, 100), _terms.ToArray(), confidence);

    /// <summary>Score vide mais valide (utilisé quand une donnée manque, jamais un faux chiffre).</summary>
    public static Score Empty(string label, string source, string confidence = Confidence.Unknown) =>
        new(0, Array.Empty<ScoreTerm>(), confidence);
}

public static class ScoreMath
{
    public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

    public static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;

    /// <summary>Médiane d'une série non vide (copie triée).</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>Arrondi commercial, entier.</summary>
    public static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
}

public static class Confidence
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string Unknown = "unknown";

    /// <summary>
    /// Règle unique du moteur : high = ≥2 sources méta ET ≥30 runs perso ;
    /// medium = ≥1 source ou ≥10 runs ; sinon low.
    /// </summary>
    public static string Of(int metaSources, int personalRuns)
    {
        if (metaSources >= 2 && personalRuns >= 30) return High;
        if (metaSources >= 1 || personalRuns >= 10) return Medium;
        return Low;
    }
}
