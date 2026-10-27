namespace Ultima.Core.Damage;

/// <summary>
/// Calcul de létalité sur une séquence d'attaques répétées (ex. 16x3).
/// Logique pure : aucune dépendance au jeu, donc testable en CI.
///
/// Convention : le block absorbe coup par coup et ne se régénère pas.
/// Les effets de rétention de block (Barricade &amp; co.) ne sont PAS modélisés ici :
/// l'appelant doit passer un block effectif par coup s'il en tient compte.
/// </summary>
public static class LethalIndex
{
    /// <summary>
    /// Un coup de la séquence.
    /// <paramref name="Raw"/> = dégâts annoncés, <paramref name="Absorbed"/> = part absorbée
    /// par le block, <paramref name="ToHp"/> = part qui touche réellement les PV.
    /// </summary>
    public readonly record struct Hit(int Index, int Raw, int Absorbed, int ToHp, int HpAfter, bool Lethal);

    public readonly record struct Result(
        IReadOnlyList<Hit> Hits,
        int? LethalAt,
        int HpAfter,
        int BlockAfter,
        int Overkill)
    {
        public bool IsLethal => LethalAt.HasValue;
        public bool FullyBlocked => Hits.Count > 0 && Hits.All(h => h.ToHp == 0);
    }

    public static Result Compute(int perHit, int repeats, int block, int hp)
    {
        if (repeats <= 0)
            return new Result(Array.Empty<Hit>(), null, hp, Math.Max(0, block), 0);

        if (perHit < 0) perHit = 0;
        if (block < 0) block = 0;

        var hits = new List<Hit>(repeats);
        var remainingBlock = block;
        var remainingHp = hp;
        int? lethalAt = null;

        for (var i = 1; i <= repeats; i++)
        {
            var raw = perHit;
            var absorbed = Math.Min(remainingBlock, raw);
            remainingBlock -= absorbed;
            var toHp = raw - absorbed;
            remainingHp -= toHp;

            var lethal = remainingHp <= 0;
            if (lethal && lethalAt is null) lethalAt = i;

            hits.Add(new Hit(i, raw, absorbed, toHp, remainingHp, lethal));
        }

        var overkill = remainingHp < 0 ? -remainingHp : 0;
        return new Result(hits, lethalAt, remainingHp, remainingBlock, overkill);
    }

    /// <summary>Rendu court pour l'UI : "16x3 -> 3e coup LETAL (-13 PV)".</summary>
    public static string Describe(int perHit, int repeats, int block, int hp)
    {
        var r = Compute(perHit, repeats, block, hp);
        var seq = $"{perHit}x{repeats}";

        if (perHit == 0 || r.FullyBlocked)
            return $"{seq} -> tout bloque";

        if (!r.IsLethal)
            return r.BlockAfter == block
                ? $"{seq} -> {r.HpAfter} PV restants"
                : $"{seq} -> {r.HpAfter} PV restants (block {r.BlockAfter})";

        return r.LethalAt == 1
            ? $"{seq} -> LETAL au 1er coup (overkill {r.Overkill})"
            : $"{seq} -> {r.LethalAt}e coup LETAL (-{r.Overkill} PV)";
    }
}
