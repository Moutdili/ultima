using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Personal;
using Ultima.Saves.Model;

namespace Ultima.Engine.Paths;

/// <summary>
/// Cartographe (docs/24-hermes-arch.md §3) : énumération exacte du DAG position → boss, comptes de
/// salles par choix immédiat, fréquence de passage par case et risque estimé.
/// <para>
/// Déterministe et sans donnée cachée : seuls les <c>children</c> visibles de la carte et
/// l'historique personnel (<see cref="IPersonalStats"/>) sont lus. Aucun <c>rng</c>, <c>odds</c>,
/// <c>event_ids</c> ni <c>*_encounter_ids</c> n'est utilisé.
/// </para>
/// <para>
/// Conventions de comptage (vérifiées sur la map réelle 7×16) : la position courante n'est jamais
/// comptée ; le nœud boss (terminal) n'est compté ni comme salle ni dans <see cref="PathsView.Nodes"/> ;
/// un nœud terminal vaut 1 chemin (suffixe vide). Si la position n'a aucun enfant (déjà au boss ou
/// cul-de-sac), <see cref="PathsView.TotalPaths"/> vaut 0 et les listes sont vides.
/// </para>
/// </summary>
public sealed class PathAnalyzer : IPathAnalyzer
{
    /// <summary>
    /// Cap d'énumération des chemins. En deçà : médianes exactes par DFS agrégé (aucune liste de
    /// chemins en mémoire). Au-delà : min/max/moyenne restent exacts en DP et la médiane est
    /// échantillonnée de façon déterministe par dérangement (unranking) à pas constant.
    /// </summary>
    public const int PathCap = 200_000;

    /// <summary>
    /// Types de salle comptés, dans l'ordre des 6 champs de <see cref="RoomCounts"/>. Les chaînes
    /// viennent des constantes du contrat (<c>RoomTypes</c>, v1.1) : un renommage côté contrat casse
    /// la compilation ici au lieu de produire des libellés divergents dans l'overlay.
    /// </summary>
    public static IReadOnlyList<string> CountedTypes { get; } = new[]
    {
        Contracts.RoomTypes.Monster, Contracts.RoomTypes.Elite, Contracts.RoomTypes.Unknown,
        Contracts.RoomTypes.RestSite, Contracts.RoomTypes.Shop, Contracts.RoomTypes.Treasure,
    };

    private readonly IPersonalStats _personal;

    /// <summary>Constructeur pour la façade : aucune donnée perso, donc <c>RiskBasis = "aucune donnée"</c>.</summary>
    public PathAnalyzer() : this(PersonalStats.Empty()) { }

    public PathAnalyzer(IPersonalStats personal) =>
        _personal = personal ?? throw new ArgumentNullException(nameof(personal));

    /// <summary>Analyse les chemins depuis <paramref name="position"/> jusqu'au boss de l'acte.</summary>
    /// <param name="actIndex">Acte courant : indexe <see cref="IPersonalStats.RoomRisk"/>.</param>
    /// <param name="characterId">Réservé (risque par personnage, vague ultérieure) : non utilisé ici.</param>
    /// <param name="hp">Réservé (arbitrage survie, vague ultérieure) : non utilisé ici.</param>
    public PathsView Analyze(ActMap map, MapCoord position, int actIndex, string characterId, int hp)
    {
        if (map is null) return EmptyView();

        var index = map.ByCoord;
        if (!index.TryGetValue(position, out _)) return EmptyView();

        // 1) Sous-graphe atteignable depuis la position : arêtes = children existants, dédupliqués.
        var adj = new Dictionary<MapCoord, List<MapCoord>>();
        var order = new List<MapCoord>();
        var seen = new HashSet<MapCoord> { position };
        var stack = new Stack<MapCoord>();
        stack.Push(position);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            order.Add(n);
            var raw = index[n].Children;
            var kids = new List<MapCoord>(raw.Count);
            foreach (var c in raw)
                if (index.ContainsKey(c) && !kids.Contains(c)) kids.Add(c);
            adj[n] = kids;
            for (int i = kids.Count - 1; i >= 0; i--)
                if (seen.Add(kids[i])) stack.Push(kids[i]);
        }

        // 2) Ordre topologique (Kahn) : un cycle signifie une carte invalide, on ne devine pas.
        var indeg = new Dictionary<MapCoord, int>(order.Count);
        foreach (var n in order) indeg[n] = 0;
        foreach (var n in order)
            foreach (var c in adj[n]) indeg[c]++;

        var queue = new Queue<MapCoord>();
        foreach (var n in order)
            if (indeg[n] == 0) queue.Enqueue(n);

        var topo = new List<MapCoord>(order.Count);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            topo.Add(n);
            foreach (var c in adj[n])
                if (--indeg[c] == 0) queue.Enqueue(c);
        }
        if (topo.Count != order.Count) return EmptyView();

        // Kahn seul ne suffit pas (un cycle en aval d'un nœud racine reste ordonnançable partiellement) :
        // on vérifie que chaque arête va strictement dans le sens de l'ordre.
        var rank = new Dictionary<MapCoord, int>(topo.Count);
        for (int i = 0; i < topo.Count; i++) rank[topo[i]] = i;
        foreach (var n in topo)
            foreach (var c in adj[n])
                if (rank[c] <= rank[n]) return EmptyView();

        // 3) Nombre de chemins par nœud (suffixe vers le boss), mémoïsé en ordre inverse.
        var suffix = new Dictionary<MapCoord, long>(topo.Count);
        for (int i = topo.Count - 1; i >= 0; i--)
        {
            var n = topo[i];
            var kids = adj[n];
            long v = 0;
            if (kids.Count == 0) v = 1;
            else foreach (var c in kids) v = AddClamp(v, suffix[c]);
            suffix[n] = v;
        }

        var rootKids = adj[position];
        long total = 0;
        foreach (var c in rootKids) total = AddClamp(total, suffix[c]);
        if (total <= 0) return EmptyView(); // déjà au boss, ou aucune issue

        // 4) Préfixe : chemins menant de la position à chaque nœud (pour la fréquence exacte).
        var prefix = new Dictionary<MapCoord, long>(topo.Count);
        foreach (var n in topo) prefix[n] = 0;
        prefix[position] = 1;
        foreach (var n in topo)
        {
            var pv = prefix[n];
            if (pv <= 0) continue;
            foreach (var c in adj[n]) prefix[c] = AddClamp(prefix[c], pv);
        }

        // 5) min / max / moyenne des salles par type : DP exact, indépendant du cap d'énumération.
        var minC = new Dictionary<MapCoord, Counts>(topo.Count);
        var maxC = new Dictionary<MapCoord, Counts>(topo.Count);
        var meanC = new Dictionary<MapCoord, double[]>(topo.Count);
        for (int i = topo.Count - 1; i >= 0; i--)
        {
            var n = topo[i];
            var kids = adj[n];
            int bucket = BucketOf(index[n]);
            var mean = new double[6];
            var mn = new int[6];
            var mx = new int[6];

            for (int t = 0; t < 6; t++)
            {
                int extra = bucket == t ? 1 : 0;
                if (kids.Count == 0)
                {
                    mn[t] = extra;
                    mx[t] = extra;
                    mean[t] = extra;
                    continue;
                }

                int lo = int.MaxValue, hi = int.MinValue;
                double weighted = 0;
                foreach (var c in kids)
                {
                    var kmin = minC[c].Get(t);
                    if (kmin < lo) lo = kmin;
                    var kmax = maxC[c].Get(t);
                    if (kmax > hi) hi = kmax;
                    weighted += suffix[c] * meanC[c][t];
                }

                var fn = suffix[n];
                mn[t] = extra + lo;
                mx[t] = extra + hi;
                mean[t] = extra + (fn > 0 ? weighted / fn : 0);
            }

            minC[n] = Counts.From(mn);
            maxC[n] = Counts.From(mx);
            meanC[n] = mean;
        }

        // 6) Choix immédiats : comptes exacts, médianes (énumération ou échantillonnage), risque.
        long perChildBudget = Math.Max(1, PathCap / Math.Max(1, rootKids.Count));
        var choices = new List<PathChoice>(rootKids.Count);
        foreach (var c in rootKids)
        {
            var node = index[c];
            var paths = suffix[c];

            var hist = paths <= PathCap
                ? EnumerateHistogram(c, index, adj)
                : UnrankHistogram(c, paths, perChildBudget, index, adj, suffix);

            long sampled = 0;
            foreach (var w in hist.Values) sampled += w;

            var median = new RoomCounts(
                Median(hist, 0, sampled), Median(hist, 1, sampled), Median(hist, 2, sampled),
                Median(hist, 3, sampled), Median(hist, 4, sampled), Median(hist, 5, sampled));

            double damage = 0, coveredRooms = 0;
            var bases = new List<string>(6);
            int missing = 0;
            for (int t = 0; t < 6; t++)
            {
                var m = meanC[c][t];
                var risk = _personal.RoomRisk(actIndex, CountedTypes[t]);
                if (risk.Samples > 0)
                {
                    damage += m * risk.AverageDamage;
                    if (m > 0)
                    {
                        coveredRooms += m;
                        if (!string.IsNullOrWhiteSpace(risk.Basis)) bases.Add(risk.Basis);
                    }
                }
                else if (m > 0) missing++;
            }

            string basis;
            if (bases.Count == 0)
            {
                damage = 0;
                basis = "aucune donnée";
            }
            else
            {
                basis = $"{string.Join(" + ", bases.Distinct())} · {(int)Math.Round(coveredRooms)} salles";
                if (missing > 0) basis += $" · {missing} type(s) sans donnée";
            }

            choices.Add(new PathChoice(
                c.Col, c.Row, RoomTypeOf(node), IntClamp(paths),
                minC[c].ToRoomCounts(), median, maxC[c].ToRoomCounts(),
                damage, basis));
        }

        choices.Sort(static (a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));

        // 7) Fréquence par case : préfixe × suffixe (exact même au-delà du cap d'énumération).
        var nodes = new List<NodeFrequency>(topo.Count);
        foreach (var n in topo)
        {
            if (n.Equals(position)) continue;
            var node = index[n];
            if (node.Kind == MapPointKind.Boss) continue; // terminal : pas un choix de route

            var through = MulClamp(prefix[n], suffix[n]);
            if (through > total) through = total;
            if (through <= 0) continue;

            var share = (double)through / total;
            if (share > 1.0) share = 1.0;
            nodes.Add(new NodeFrequency(n.Col, n.Row, RoomTypeOf(node), IntClamp(through), share));
        }

        nodes.Sort(static (a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));

        return new PathsView(IntClamp(total), choices, nodes);
    }

    private static PathsView EmptyView() =>
        new(0, Array.Empty<PathChoice>(), Array.Empty<NodeFrequency>());

    /// <summary>
    /// DFS agrégé : une entrée d'histogramme par vecteur de salles distinct (jamais une liste de
    /// chemins). Un nœud terminal clôt le chemin ; le boss n'est pas une salle comptée.
    /// </summary>
    private static Dictionary<Counts, int> EnumerateHistogram(
        MapCoord root, IReadOnlyDictionary<MapCoord, MapNode> index,
        IReadOnlyDictionary<MapCoord, List<MapCoord>> adj)
    {
        var hist = new Dictionary<Counts, int>();
        var stack = new Stack<(MapCoord Node, Counts Acc)>();
        stack.Push((root, Counts.Zero));

        int emitted = 0;
        while (stack.Count > 0)
        {
            var (n, acc) = stack.Pop();
            var bucket = BucketOf(index[n]);
            var at = bucket >= 0 ? acc.Add(Counts.One(bucket)) : acc;

            var kids = adj[n];
            if (kids.Count == 0)
            {
                hist[at] = hist.TryGetValue(at, out var w) ? w + 1 : 1;
                if (++emitted > PathCap) break; // garde-fou : ne devrait jamais arriver (appel filtré en amont)
                continue;
            }

            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], at));
        }

        return hist;
    }

    /// <summary>
    /// Échantillonnage déterministe au-delà du cap : on dénumère les chemins d'indice
    /// 0, pas, 2·pas… (aucun aléatoire), ce qui couvre toutes les branches sans biais d'ordre.
    /// </summary>
    private static Dictionary<Counts, int> UnrankHistogram(
        MapCoord root, long paths, long budget,
        IReadOnlyDictionary<MapCoord, MapNode> index,
        IReadOnlyDictionary<MapCoord, List<MapCoord>> adj,
        IReadOnlyDictionary<MapCoord, long> suffix)
    {
        var hist = new Dictionary<Counts, int>();
        long stride = Math.Max(1, (paths + budget - 1) / budget);
        for (long i = 0; i < paths; i += stride)
        {
            var counts = Unrank(root, i, index, adj, suffix);
            hist[counts] = hist.TryGetValue(counts, out var w) ? w + 1 : 1;
        }
        return hist;
    }

    /// <summary>k-ième chemin (0-based, ordre des children) depuis <paramref name="root"/>.</summary>
    private static Counts Unrank(
        MapCoord root, long k,
        IReadOnlyDictionary<MapCoord, MapNode> index,
        IReadOnlyDictionary<MapCoord, List<MapCoord>> adj,
        IReadOnlyDictionary<MapCoord, long> suffix)
    {
        var acc = Counts.Zero;
        var cur = root;
        long idx = k;

        while (true)
        {
            var bucket = BucketOf(index[cur]);
            if (bucket >= 0) acc = acc.Add(Counts.One(bucket));

            var kids = adj[cur];
            if (kids.Count == 0) return acc;

            var next = kids[^1];
            foreach (var c in kids)
            {
                var fc = suffix[c];
                if (idx < fc) { next = c; break; }
                idx -= fc;
            }
            cur = next;
        }
    }

    /// <summary>
    /// Médiane d'un type sur l'histogramme (pondérée). Médiane basse : toujours une valeur observée,
    /// donc <c>Min ≤ Median ≤ Max</c> par construction.
    /// </summary>
    private static int Median(IReadOnlyDictionary<Counts, int> hist, int type, long total)
    {
        if (total <= 0 || hist.Count == 0) return 0;

        var weights = new SortedDictionary<int, int>();
        foreach (var kv in hist)
        {
            var v = kv.Key.Get(type);
            weights[v] = weights.TryGetValue(v, out var w) ? w + kv.Value : kv.Value;
        }

        long target = (total + 1) / 2;
        long acc = 0;
        int last = 0;
        foreach (var kv in weights)
        {
            acc += kv.Value;
            last = kv.Key;
            if (acc >= target) return kv.Key;
        }
        return last;
    }

    /// <summary>Type compté (0..5, ordre de <see cref="RoomCounts"/>) ou -1 si hors comptage.</summary>
    private static int BucketOf(MapNode node)
    {
        var b = node.Kind switch
        {
            MapPointKind.Monster => 0,
            MapPointKind.Elite => 1,
            MapPointKind.Unknown => 2,
            MapPointKind.RestSite => 3,
            MapPointKind.Shop => 4,
            MapPointKind.Treasure => 5,
            _ => -1
        };
        if (b >= 0) return b;

        // Type inconnu (patch) : on retente sur la chaîne brute, sinon hors comptage.
        return MapPointKinds.Parse(node.RawType) switch
        {
            MapPointKind.Monster => 0,
            MapPointKind.Elite => 1,
            MapPointKind.Unknown => 2,
            MapPointKind.RestSite => 3,
            MapPointKind.Shop => 4,
            MapPointKind.Treasure => 5,
            _ => -1
        };
    }

    /// <summary>Chaîne du jeu (monster / elite / unknown / rest_site / shop / treasure / boss).</summary>
    private static string RoomTypeOf(MapNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.RawType)) return node.RawType;
        return node.Kind switch
        {
            MapPointKind.Monster => Contracts.RoomTypes.Monster,
            MapPointKind.Elite => Contracts.RoomTypes.Elite,
            MapPointKind.Unknown => Contracts.RoomTypes.Unknown,
            MapPointKind.RestSite => Contracts.RoomTypes.RestSite,
            MapPointKind.Shop => Contracts.RoomTypes.Shop,
            MapPointKind.Treasure => Contracts.RoomTypes.Treasure,
            MapPointKind.Boss => Contracts.RoomTypes.Boss,
            MapPointKind.Ancient => Contracts.RoomTypes.Ancient,
            // "unassigned" n'est pas un type de salle jouable : la save n'a pas encore rempli la case.
            MapPointKind.Unassigned => "unassigned",
            _ => Contracts.RoomTypes.Unknown
        };
    }

    private static long AddClamp(long a, long b)
    {
        if (b > 0 && a > long.MaxValue - b) return long.MaxValue;
        return a + b;
    }

    private static long MulClamp(long a, long b)
    {
        if (a <= 0 || b <= 0) return 0;
        if (a > long.MaxValue / b) return long.MaxValue;
        return a * b;
    }

    private static int IntClamp(long v) => v > int.MaxValue ? int.MaxValue : (int)v;

    /// <summary>Vecteur de salles d'un chemin (struct : aucune allocation par chemin).</summary>
    private readonly record struct Counts(int Monster, int Elite, int Unknown, int Rest, int Shop, int Treasure)
    {
        public static readonly Counts Zero = new(0, 0, 0, 0, 0, 0);

        public int Get(int type) => type switch
        {
            0 => Monster,
            1 => Elite,
            2 => Unknown,
            3 => Rest,
            4 => Shop,
            5 => Treasure,
            _ => 0
        };

        public Counts Add(in Counts o) => new(
            Monster + o.Monster, Elite + o.Elite, Unknown + o.Unknown,
            Rest + o.Rest, Shop + o.Shop, Treasure + o.Treasure);

        public static Counts One(int type) => type switch
        {
            0 => new Counts(1, 0, 0, 0, 0, 0),
            1 => new Counts(0, 1, 0, 0, 0, 0),
            2 => new Counts(0, 0, 1, 0, 0, 0),
            3 => new Counts(0, 0, 0, 1, 0, 0),
            4 => new Counts(0, 0, 0, 0, 1, 0),
            _ => new Counts(0, 0, 0, 0, 0, 1)
        };

        public static Counts From(int[] v) => new(v[0], v[1], v[2], v[3], v[4], v[5]);

        public RoomCounts ToRoomCounts() => new(Monster, Elite, Unknown, Rest, Shop, Treasure);
    }
}
