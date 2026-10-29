using Ultima.Engine.Contracts;

namespace Ultima.Engine.Scoring.Paths;

/// <summary>
/// Contrat v1.2 : branche la carte visible (<see cref="MapView"/>) sur <see cref="PathPlanner"/>.
/// La carte ne contient que ce que le joueur voit (types de cases, liens, position) : aucune
/// donnée cachée n'entre dans le calcul.
/// </summary>
/// <remarks>
/// La route est choisie en <b>cumulé</b>, pas salle par salle : le planificateur simule les PV
/// après chaque combat et refuse une suite qui tue avant le boss (docs/40).
/// </remarks>
public sealed class RoutePlanner : IRoutePlanner
{
    /// <summary>Source affichée sous la route : chaque terme porte la même base chiffrée.</summary>
    public const string Basis = "PathPlanner v1 · rooms.json (80 000 runs solo) · PV cumulés";

    private readonly PathPlanner _planner;

    public RoutePlanner(PathPlanner? planner = null) => _planner = planner ?? new PathPlanner();

    public PathPlanner Planner => _planner;

    public RouteView? Plan(MapView map, ScoringContext ctx)
    {
        if (map is null || map.Nodes.Count == 0) return null;

        var nodes = new List<MapNode>(map.Nodes.Count);
        foreach (var node in map.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.RoomType)) continue;
            var next = new List<string>(node.Children.Count);
            foreach (var child in node.Children) next.Add(Id(child.Col, child.Row));
            nodes.Add(new MapNode(Id(node.Col, node.Row), node.RoomType, next));
        }

        if (nodes.Count == 0) return null;

        var known = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        var starts = Starts(map, known);
        if (starts.Count == 0) return null;

        // ActIndex du contrat est 0-based ; PathPlanner raisonne en numéro d'acte (1..3).
        var state = new PathState(ctx.Hp, ctx.MaxHp, ctx.Gold, ctx.ActIndex + 1, ctx.Ascension);

        PathPlan? best = null;
        foreach (var start in starts)
        {
            var plan = _planner.Plan(nodes, start, state);
            if (plan.Steps.Count == 0) continue;
            if (best is null || plan.TotalValue > best.TotalValue) best = plan;
        }

        if (best is null) return null;

        var path = new List<MapCoordView>(best.Steps.Count);
        foreach (var step in best.Steps) path.Add(Coord(step.NodeId));

        var terms = new List<ScoreTerm>(best.Steps.Count);
        var lethal = false;
        foreach (var step in best.Steps)
        {
            terms.Add(new ScoreTerm(PathPlanner.Label(step.Type), step.Value, Basis) { Evidence = step.Reason });
            if (step.HpAfter <= 1) lethal = true;
        }

        var lastHp = best.Steps[^1].HpAfter;
        return new RouteView(path, lastHp, lethal, terms, Basis);
    }

    /// <summary>
    /// Départ : la position si elle est connue, sinon toutes les entrées de la première rangée
    /// (le joueur n'a pas encore choisi — on lui propose la meilleure entrée).
    /// </summary>
    private static List<string> Starts(MapView map, HashSet<string> known)
    {
        var starts = new List<string>(4);

        if (map.Position is { } pos)
        {
            var id = Id(pos.Col, pos.Row);
            if (known.Contains(id)) starts.Add(id);
        }

        if (starts.Count > 0) return starts;

        foreach (var node in map.Nodes)
        {
            if (node.Row != 0) continue;
            var id = Id(node.Col, node.Row);
            if (known.Contains(id) && !starts.Contains(id)) starts.Add(id);
        }

        return starts;
    }

    private static string Id(int col, int row) => $"{col},{row}";

    private static MapCoordView Coord(string nodeId)
    {
        var parts = nodeId.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out var col) && int.TryParse(parts[1], out var row))
            return new MapCoordView(col, row);

        // Identifiant inattendu : coordonnée neutre plutôt qu'une exception pendant le jeu.
        // Le terme reste lisible car il porte le libellé de la salle.
        return new MapCoordView(-1, -1);
    }
}
