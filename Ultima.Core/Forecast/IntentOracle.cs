using System.Globalization;
using System.Text.RegularExpressions;
using Ultima.Core.Model;

namespace Ultima.Core.Forecast;

/// <summary>
/// Oracle F1 : confronte les valeurs lues (PerHit/Repeats/TotalDamage) au texte que le jeu affiche.
/// Le Label n'est JAMAIS une donnée de calcul : il sert uniquement à détecter un reader faux.
/// </summary>
public static class IntentOracle
{
    private static readonly Regex BbCode = new(@"\[[^\]]*\]", RegexOptions.CultureInvariant);
    private static readonly Regex Integers = new(@"[0-9]+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Attaque : ≥ 2 entiers dans le label (BBCode retiré) → 1er == PerHit et 2e == Repeats ;
    /// 1 entier → == PerHit ou == TotalDamage ; 0 entier → true (pas d'info). Non-attaque → true.
    /// </summary>
    public static bool Matches(IntentSnapshot i, out string detail)
    {
        if (i is null) { detail = "no intent"; return true; }
        if (!ForecastEngine.IsAttack(i)) { detail = $"non-attack ({i.Kind})"; return true; }

        var text = BbCode.Replace(i.Label ?? "", "");
        var numbers = new List<int>();
        foreach (Match m in Integers.Matches(text))
            if (int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                numbers.Add(n);

        var seen = numbers.Count == 0 ? "-" : string.Join(",", numbers);
        var expected = $"perHit={i.PerHit} repeats={i.Repeats} total={i.TotalDamage}";

        bool ok;
        if (numbers.Count == 0)
        {
            detail = $"label '{text}' has no number: no info ({expected})";
            return true;
        }
        if (numbers.Count >= 2)
            ok = numbers[0] == i.PerHit && numbers[1] == i.Repeats;
        else
            ok = numbers[0] == i.PerHit || numbers[0] == i.TotalDamage;

        detail = $"label '{text}' -> [{seen}] vs {expected}: {(ok ? "ok" : "MISMATCH")}";
        return ok;
    }
}
