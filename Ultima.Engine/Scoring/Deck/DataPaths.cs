using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Deck;

/// <summary>
/// Localise <c>data/catalog</c> (types de cartes, malédictions, profils de boss).
/// Même logique que <see cref="ScoringData"/> pour <c>data/meta</c> : voisin de la méta, sinon
/// remontée depuis l'assembly. Retourne null si absent (les termes dépendants sont alors omis,
/// jamais remplacés par un chiffre inventé).
/// </summary>
public static class DataPaths
{
    public static string? CatalogDir()
    {
        var metaDir = ScoringData.ResolveMetaDir();
        if (!string.IsNullOrWhiteSpace(metaDir))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(metaDir)) ?? ".", "catalog");
            if (File.Exists(Path.Combine(sibling, "cards.json"))) return sibling;
        }

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "data", "catalog");
            if (File.Exists(Path.Combine(candidate, "cards.json"))) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }
}
