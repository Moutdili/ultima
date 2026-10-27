using Ultima.Core.Damage;

namespace Ultima.Core.Forecast;

/// <summary>
/// Létalité sur une séquence HÉTÉROGÈNE de coups (plusieurs intents / plusieurs ennemis concaténés).
/// Sémantique EXACTEMENT identique à <see cref="LethalIndex.Compute"/> (qui ne gère qu'un NxM homogène) :
/// le block absorbe coup par coup sans se régénérer, le coup létal est le premier qui met les PV ≤ 0,
/// la séquence continue après la mort (overkill = PV négatifs finaux), un coup négatif vaut 0.
/// Logique pure : aucune dépendance au jeu.
/// </summary>
public static class SequenceLethal
{
    public static LethalIndex.Result Compute(IReadOnlyList<int> hits, int block, int hp)
    {
        if (hits is null || hits.Count == 0)
            return new LethalIndex.Result(Array.Empty<LethalIndex.Hit>(), null, hp, Math.Max(0, block), 0);

        if (block < 0) block = 0;

        var result = new List<LethalIndex.Hit>(hits.Count);
        var remainingBlock = block;
        var remainingHp = hp;
        int? lethalAt = null;

        for (var i = 0; i < hits.Count; i++)
        {
            var index = i + 1;
            var raw = Math.Max(0, hits[i]);
            var absorbed = Math.Min(remainingBlock, raw);
            remainingBlock -= absorbed;
            var toHp = raw - absorbed;
            remainingHp -= toHp;

            var lethal = remainingHp <= 0;
            if (lethal && lethalAt is null) lethalAt = index;

            result.Add(new LethalIndex.Hit(index, raw, absorbed, toHp, remainingHp, lethal));
        }

        var overkill = remainingHp < 0 ? -remainingHp : 0;
        return new LethalIndex.Result(result, lethalAt, remainingHp, remainingBlock, overkill);
    }
}
