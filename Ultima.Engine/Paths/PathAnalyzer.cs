using Ultima.Engine.Contracts;
using Ultima.Saves.Model;

namespace Ultima.Engine.Paths;

/// <summary>
/// Analyse des chemins de la carte : énumération exacte du DAG position → boss, comptes de
/// salles par choix immédiat, fréquence de passage par case, risque estimé.
/// TODO agent S6 : implémentation complète + tests sur la map réelle (7×16, 60 points).
/// </summary>
public sealed class PathAnalyzer : IPathAnalyzer
{
    public PathsView Analyze(ActMap map, MapCoord position, int actIndex, string characterId, int hp)
    {
        // Implémentation de base : aucun chemin calculé (le daemon affiche « — »).
        return new PathsView(0, Array.Empty<PathChoice>(), Array.Empty<NodeFrequency>());
    }
}
