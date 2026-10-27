using Godot;

namespace Ultima.UI.Layout;

/// <summary>
/// Géométrie pure du panneau : collé au bord droit, entre 15 % et 75 % de la hauteur,
/// première position (balayage vertical par pas) qui n'intersecte aucune zone à éviter.
/// Aucune position libre → position de chevauchement minimal + alpha réduit (rail au repos
/// seulement). Jamais appelé par frame.
/// </summary>
public static class Placement
{
    public const float BandTop = 0.15f;
    public const float BandBottom = 0.75f;
    public const float MaxHeightFraction = 0.40f;
    public const float ScanStep = 8f;
    public const float FallbackAlpha = 0.35f;

    public readonly record struct Result(Vector2 Position, float Alpha, bool Overlaps);

    /// <param name="area">Rect visible du viewport (coordonnées globales du canvas).</param>
    /// <param name="panel">Taille du panneau déployé (le rail replié tient dans son coin haut-droit).</param>
    /// <param name="avoid">Zones à ne jamais couvrir (intents, main, boutons), coordonnées globales.</param>
    public static Result Compute(Rect2 area, Vector2 panel, IReadOnlyList<Rect2> avoid, float step = ScanStep)
    {
        if (step <= 0f) step = ScanStep;
        float h = Math.Min(panel.Y, area.Size.Y * MaxHeightFraction);
        float x = area.End.X - panel.X;
        float yMin = area.Position.Y + area.Size.Y * BandTop;
        float yMax = Math.Max(yMin, area.Position.Y + area.Size.Y * BandBottom - h);

        int steps = (int)Math.Floor((yMax - yMin) / step);
        float bestY = yMin, bestOverlap = float.MaxValue;
        for (int i = 0; i <= steps + 1; i++)
        {
            float y = i <= steps ? yMin + i * step : yMax; // dernière passe : bas de bande exact
            float overlap = OverlapArea(new Rect2(x, y, panel.X, h), avoid);
            if (overlap <= 0f)
                return new Result(new Vector2(x, y), 1f, false);
            if (overlap < bestOverlap) { bestOverlap = overlap; bestY = y; }
        }
        // Aucune position libre (ex. boss large) : la position qui couvre le MOINS de zones
        // à éviter (QA v2 critère 11), et non le haut de bande arbitraire.
        return new Result(new Vector2(x, bestY), FallbackAlpha, true);
    }

    /// <summary>Somme des aires d'intersection avec les zones à éviter (0 = position libre).</summary>
    private static float OverlapArea(Rect2 r, IReadOnlyList<Rect2> avoid)
    {
        float total = 0f;
        for (int i = 0; i < avoid.Count; i++)
        {
            var a = avoid[i];
            if (a.Size.X <= 0f || a.Size.Y <= 0f || !r.Intersects(a)) continue;
            var inter = r.Intersection(a);
            total += inter.Size.X * inter.Size.Y;
        }
        return total;
    }
}
