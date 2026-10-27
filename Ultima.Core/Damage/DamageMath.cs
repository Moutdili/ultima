using System.Globalization;

namespace Ultima.Core.Damage;

/// <summary>
/// Arithmétique des variantes de dégâts. Les multiplicateurs sont FOURNIS par l'appelant
/// (lus dans les modèles du jeu : WeakPower, VulnerablePower, PaperKrane, Debilitate...).
/// Aucune valeur de jeu n'est codée en dur ici — c'est une règle du projet.
/// </summary>
public static class DamageMath
{
    /// <summary>Le jeu tronque le décimal vers zéro : on reproduit exactement ce comportement.</summary>
    public static int Truncate(decimal value) => (int)decimal.Truncate(value);

    public static int ApplyMultiplier(int current, decimal multiplier)
        => Math.Max(0, Truncate(current * multiplier));

    public static int ApplyAll(int current, params decimal[] multipliers)
    {
        var value = (decimal)current;
        foreach (var m in multipliers) value *= m;
        return Math.Max(0, Truncate(value));
    }

    /// <summary>Total d'une séquence, block inclus, sans dépasser 0.</summary>
    public static int SequenceTotal(int perHit, int repeats, int block)
    {
        if (repeats <= 0) return 0;
        var raw = perHit * repeats;
        var afterBlock = raw - Math.Max(0, block);
        return Math.Max(0, afterBlock);
    }

    public static string Format(decimal multiplier) => multiplier.ToString("0.###", CultureInfo.InvariantCulture);
}
