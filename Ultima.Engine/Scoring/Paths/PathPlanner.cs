using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Paths;

/// <summary>Un nœud de la carte (salle + successeurs).</summary>
public sealed record MapNode(string Id, string Type, IReadOnlyList<string> Next);

/// <summary>État au moment de planifier : PV, or, acte.</summary>
public sealed record PathState(int Hp, int MaxHp, int Gold, int Act = 1, int Ascension = 0)
{
    public double HpPercent => MaxHp > 0 ? 100.0 * Hp / MaxHp : 50.0;
}

/// <summary>Une étape de la route recommandée, avec sa valeur et les PV après la salle.</summary>
public sealed record PathStep(string NodeId, string Type, int Value, int HpAfter, string Reason);

/// <summary>Route optimale : valeur espérée, étapes, explication.</summary>
public sealed record PathPlan(string StartId, int TotalValue, IReadOnlyList<PathStep> Steps, string Explanation);

/// <summary>
/// Planifie la meilleure route sur la carte : programmation dynamique arrière sur le DAG,
/// avec les valeurs de salle mesurées (dégâts, or) et la valeur des PV selon la vie restante.
///
/// <para>
/// Pourquoi c'est important : à état égal, la route change tout — les élites rapportent une relique
/// et ~36 or pour ~21 PV, les repos rendent 30 % des PV max, les boutiques ne valent le détour
/// qu'avec de l'or. Le planificateur choisit la suite de salles qui maximise la valeur espérée
/// jusqu'au boss, pas la salle suivante isolée.
/// </para>
/// </summary>
public sealed class PathPlanner
{
    /// <summary>Valeur d'un point d'or (100 or ≈ 6 points).</summary>
    public const double GoldValue = 0.06;

    /// <summary>Valeur d'une relique (moyenne observée ~8 points).</summary>
    public const double RelicValue = 8.0;

    /// <summary>Valeur d'une carte ajoutée.</summary>
    public const double CardValue = 3.0;

    /// <summary>Valeur d'un PV, doublée sous 50 % de vie.</summary>
    public const double HpValue = 0.35;

    /// <summary>Dégâts moyens mesurés (acte 1, pleine vie) — docs/38.</summary>
    public const double EliteDamage = 21.0;
    public const double MonsterDamage = 5.4;
    public const double UnknownDamage = 3.0;
    public const double BossDamage = 34.7;

    /// <summary>Or moyen mesuré par type de salle (acte 1) — docs/38.</summary>
    public const double EliteGold = 36.0;
    public const double MonsterGold = 13.2;
    public const double TreasureGold = 40.6;
    public const double BossGold = 80.6;

    /// <summary>Seuil de vie sous lequel une élite est pénalisée.</summary>
    public const double EliteRiskBelow = 30.0;

    /// <summary>Pénalité appliquée à une élite sous le seuil de risque.</summary>
    public const double EliteRiskPenalty = 12.0;

    /// <summary>Or à partir duquel une boutique vaut le détour.</summary>
    public const int ShopGoldThreshold = 120;

    private readonly PathStep? _none = null;

    /// <summary>Planifie depuis <paramref name="startId"/> ; DAG attendu, cycles tolérés.</summary>
    public PathPlan Plan(IEnumerable<MapNode> nodes, string startId, PathState state)
    {
        var byId = new Dictionary<string, MapNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Id)) byId[node.Id] = node;
        }

        if (!byId.ContainsKey(startId))
            return new PathPlan(startId, 0, Array.Empty<PathStep>(), $"nœud de départ inconnu : {startId}");

        var memo = new Dictionary<(string, int), (double Value, PathStep? Step)>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        (double Value, PathStep? Step) Best(string id, int hp)
        {
            var key = (id.ToUpperInvariant(), hp);
            if (memo.TryGetValue(key, out var known)) return known;
            if (!byId.TryGetValue(id, out var node)) return (0, _none);
            if (!visiting.Add(id)) return (0, _none);   // cycle : on s'arrête, pas d'infini

            var (value, after, reason) = Evaluate(node.Type, hp, state);
            var bestChild = 0.0;
            PathStep? childStep = _none;
            foreach (var next in node.Next ?? Array.Empty<string>())
            {
                var child = Best(next, Math.Max(1, after));
                if (child.Value > bestChild || childStep is null && child.Step is not null)
                {
                    bestChild = child.Value;
                    childStep = child.Step;
                }
            }

            visiting.Remove(id);
            var step = new PathStep(node.Id, node.Type, ScoreMath.Round(value + bestChild), after, reason);
            var result = (value + bestChild, (PathStep?)step);
            memo[key] = result;
            return result;
        }

        var best = Best(startId, Math.Max(1, state.Hp));
        var steps = new List<PathStep>(6);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = best.Step;
        while (current is not null && seen.Add(current.NodeId))
        {
            steps.Add(current);
            if (!byId.TryGetValue(current.NodeId, out var node) || node.Next.Count == 0) break;
            var nextHp = Math.Max(1, current.HpAfter);
            current = node.Next
                .Select(n => memo.TryGetValue((n.ToUpperInvariant(), nextHp), out var m) ? m.Step : _none)
                .Where(s => s is not null)
                .OrderByDescending(s => s!.Value)
                .FirstOrDefault();
        }

        var total = ScoreMath.Round(best.Value);
        var explanation = steps.Count == 0
            ? "aucune route calculable"
            : $"meilleure route : {string.Join(" → ", steps.Select(s => $"{Label(s.Type)}({s.Value:+#;-#;0})"))} = {total} points";
        return new PathPlan(startId, total, steps, explanation);
    }

    /// <summary>Valeur d'une salle et PV après, selon le type et l'état.</summary>
    private (double Value, int HpAfter, string Reason) Evaluate(string type, int hp, PathState state)
    {
        var damage = DamageOf(type, state.Act);
        // La vie qui compte est celle du moment, pas celle du départ : sur une route à plusieurs
        // combats, les PV baissent et le prix du risque augmente.
        var hpPercent = state.MaxHp > 0 ? 100.0 * hp / state.MaxHp : 50.0;
        var hpWeight = HpValue * (1 + Math.Max(0, (50 - hpPercent) / 50));
        var after = Math.Max(0, (int)Math.Round(hp - damage));
        var risk = 0.0;
        if (type == RoomTypes.Elite && hpPercent < EliteRiskBelow) risk = EliteRiskPenalty;
        if (damage > 0 && after <= 0) risk += 100.0;   // un combat qui tue n'est jamais un choix

        return type switch
        {
            RoomTypes.Elite => (RelicValue + CardValue + EliteGold * GoldValue - damage * hpWeight - risk, after,
                $"élite : relique + carte + {EliteGold:0} or − {damage:0.#} PV" + (risk > 100 ? " — MORTEL" : risk > 0 ? " − risque (PV bas)" : "")),
            RoomTypes.Monster => (CardValue + MonsterGold * GoldValue - damage * hpWeight, after,
                $"monstre : carte + {MonsterGold:0.#} or − {damage:0.#} PV"),
            RoomTypes.Treasure => (RelicValue + TreasureGold * GoldValue, after,
                $"trésor : relique + {TreasureGold:0.#} or"),
            RoomTypes.RestSite => (Math.Max(0.30 * state.MaxHp * 0.20, CardValue), Math.Min(state.MaxHp, hp + (int)(0.30 * state.MaxHp)),
                $"repos : soin 30 % des PV max ou amélioration"),
            RoomTypes.Shop => (state.Gold >= ShopGoldThreshold ? 6.0 : -2.0, hp,
                state.Gold >= ShopGoldThreshold ? $"boutique : {state.Gold} or disponibles" : $"boutique : seulement {state.Gold} or, détour peu rentable"),
            RoomTypes.Boss => (BossGold * GoldValue - damage * hpWeight, after, "boss : obligatoire"),
            RoomTypes.Unknown => (2.0 - UnknownDamage * hpWeight, Math.Max(0, hp - (int)UnknownDamage),
                "événement : valeur variable, petite dépense de PV"),
            _ => (0.0, hp, type),
        };
    }

    /// <summary>Libellé français d'un type de salle, pour une explication lisible.</summary>
    public static string Label(string type) => type switch
    {
        RoomTypes.Elite => "élite",
        RoomTypes.Monster => "monstre",
        RoomTypes.RestSite => "repos",
        RoomTypes.Shop => "boutique",
        RoomTypes.Treasure => "trésor",
        RoomTypes.Boss => "boss",
        RoomTypes.Unknown => "événement",
        _ => type,
    };

    private static double DamageOf(string type, int act)
    {
        var baseDamage = type switch
        {
            RoomTypes.Elite => EliteDamage,
            RoomTypes.Monster => MonsterDamage,
            RoomTypes.Boss => BossDamage,
            RoomTypes.Unknown => UnknownDamage,
            _ => 0.0,
        };
        // Les actes 2 et 3 sont plus dangereux : facteur prudent, non calibré finement.
        return baseDamage * (act <= 1 ? 1.0 : act == 2 ? 1.3 : 1.6);
    }
}
