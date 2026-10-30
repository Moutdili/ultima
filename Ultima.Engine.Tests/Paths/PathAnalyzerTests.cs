using System.Diagnostics;
using Ultima.Engine.Contracts;
using Ultima.Engine.Paths;
using Ultima.Engine.Scoring.Personal;
using Ultima.Saves;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Paths;

/// <summary>
/// Tests du cartographe sur la map réelle de la fenêtre de test n°1 (7×16, 60 points, position
/// (4,1), boss ENCOUNTER.WATERFALL_GIANT_BOSS). L'oracle est une énumération brute indépendante
/// (20 chemins) : elle vérifie le DP, pas l'inverse.
/// </summary>
public sealed class PathAnalyzerTests
{
    private const string FakeSteamId = "70000000000000001";

    private static readonly Lazy<RealMap> Fixture = new(LoadRealMap, isThreadSafe: true);

    private sealed record RealMap(ActMap Map, MapCoord Position, int ActIndex);

    // ---------- chargement de la fixture ----------

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "fixtures", "saves")) &&
                Directory.Exists(Path.Combine(d.FullName, "Ultima.Saves")))
                return d.FullName;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("fixtures/saves introuvable depuis " + AppContext.BaseDirectory);
    }

    private static string CurrentRunSave => Path.Combine(
        RepoRoot(), "fixtures", "saves", "root", "steam", FakeSteamId, "profile1", "saves", "current_run.save");

    private static RealMap LoadRealMap()
    {
        var run = new RunSaveReader().Read(CurrentRunSave);
        var act = run.Acts[run.CurrentActIndex];
        Assert.NotNull(act.Map);
        Assert.NotEmpty(run.VisitedMapCoords);
        return new RealMap(act.Map!, run.VisitedMapCoords[^1], run.CurrentActIndex);
    }

    private static PathsView Analyze(IPersonalStats? stats = null)
    {
        var m = Fixture.Value;
        var analyzer = stats is null ? new PathAnalyzer() : new PathAnalyzer(stats);
        return analyzer.Analyze(m.Map, m.Position, m.ActIndex, "CHARACTER.SILENT", 70);
    }

    // ---------- oracle : énumération brute des chemins réels ----------

    /// <summary>Tous les chemins depuis <paramref name="from"/> (inclus) jusqu'à un terminal.</summary>
    private static List<List<MapCoord>> BruteForcePathsFrom(ActMap map, MapCoord from)
    {
        var all = new List<List<MapCoord>>();
        var current = new List<MapCoord>();

        void Walk(MapCoord node)
        {
            current.Add(node);
            var kids = map.ByCoord[node].Children.Where(c => map.ByCoord.ContainsKey(c)).ToList();
            if (kids.Count == 0) all.Add(new List<MapCoord>(current));
            else foreach (var k in kids) Walk(k);
            current.RemoveAt(current.Count - 1);
        }

        Walk(from);
        return all;
    }

    private static List<List<MapCoord>> BruteForcePaths(ActMap map, MapCoord position) =>
        map.ByCoord[position].Children
            .Where(c => map.ByCoord.ContainsKey(c))
            .SelectMany(c => BruteForcePathsFrom(map, c))
            .ToList();

    private static RoomCounts CountRooms(ActMap map, IReadOnlyList<MapCoord> path)
    {
        int monster = 0, elite = 0, unknown = 0, rest = 0, shop = 0, treasure = 0;
        foreach (var c in path)
        {
            switch (map.ByCoord[c].Kind)
            {
                case MapPointKind.Monster: monster++; break;
                case MapPointKind.Elite: elite++; break;
                case MapPointKind.Unknown: unknown++; break;
                case MapPointKind.RestSite: rest++; break;
                case MapPointKind.Shop: shop++; break;
                case MapPointKind.Treasure: treasure++; break;
            }
        }
        return new RoomCounts(monster, elite, unknown, rest, shop, treasure);
    }

    private static int TypeOf(RoomCounts c, int t) => t switch
    {
        0 => c.Monster, 1 => c.Elite, 2 => c.Unknown, 3 => c.Rest, 4 => c.Shop, _ => c.Treasure
    };

    /// <summary>Médiane basse (rang (n+1)/2) : même convention que l'analyseur.</summary>
    private static int LowerMedian(List<int> values)
    {
        values.Sort();
        return values[(values.Count + 1) / 2 - 1];
    }

    // ---------- 1. la map réelle est bien celle documentée ----------

    [Fact]
    public void Real_map_matches_the_documented_fixture()
    {
        var m = Fixture.Value;
        Assert.Equal(7, m.Map.Width);
        Assert.Equal(16, m.Map.Height);
        Assert.Equal(60, m.Map.Points.Count);
        Assert.Equal(new MapCoord(4, 1), m.Position);
        Assert.Equal(MapPointKind.Boss, m.Map.Boss.Kind);
        Assert.Equal(new MapCoord(3, 16), m.Map.Boss.Coord);
        Assert.Equal("monster", m.Map.ByCoord[m.Position].RawType);
    }

    // ---------- 2. totaux et choix immédiats ----------

    [Fact]
    public void Real_map_total_paths_matches_children_sum_and_brute_force()
    {
        var m = Fixture.Value;
        var view = Analyze();

        var children = m.Map.ByCoord[m.Position].Children;
        var brute = BruteForcePaths(m.Map, m.Position);

        Assert.True(view.TotalPaths > 0, $"TotalPaths={view.TotalPaths}");
        Assert.Equal(brute.Count, view.TotalPaths);
        Assert.Equal(children.Count, view.NextChoices.Count);
        Assert.Equal(view.TotalPaths, view.NextChoices.Sum(c => c.PathCount));

        // Chaque PathCount est le nombre exact de chemins partant de cet enfant.
        foreach (var c in view.NextChoices)
        {
            var fromChild = brute.Count(p => p[0] == new MapCoord(c.Col, c.Row));
            Assert.Equal(fromChild, c.PathCount);
            Assert.Contains(children, k => k.Col == c.Col && k.Row == c.Row);
            Assert.Equal(m.Map.ByCoord[new MapCoord(c.Col, c.Row)].RawType, c.RoomType);
        }
    }

    // ---------- 3. min / médiane / max exacts ----------

    [Fact]
    public void Each_choice_has_ordered_counts_and_positive_path_count()
    {
        var m = Fixture.Value;
        var view = Analyze();
        var brute = BruteForcePaths(m.Map, m.Position);

        Assert.NotEmpty(view.NextChoices);
        foreach (var c in view.NextChoices)
        {
            Assert.True(c.PathCount > 0, $"{c.Col},{c.Row} PathCount={c.PathCount}");

            var paths = brute.Where(p => p[0] == new MapCoord(c.Col, c.Row)).ToList();
            var counts = paths.Select(p => CountRooms(m.Map, p)).ToList();

            for (int t = 0; t < 6; t++)
            {
                var values = counts.Select(x => TypeOf(x, t)).ToList();
                Assert.Equal(values.Min(), TypeOf(c.Min, t));
                Assert.Equal(values.Max(), TypeOf(c.Max, t));
                Assert.Equal(LowerMedian(values), TypeOf(c.Median, t));

                // Invariant demandé par le contrat, type par type.
                Assert.True(TypeOf(c.Min, t) <= TypeOf(c.Median, t) && TypeOf(c.Median, t) <= TypeOf(c.Max, t),
                    $"{c.Col},{c.Row} type {t} : {TypeOf(c.Min, t)}/{TypeOf(c.Median, t)}/{TypeOf(c.Max, t)}");
            }
        }
    }

    // ---------- 4. fréquences ----------

    [Fact]
    public void Node_frequencies_are_bounded_and_children_shares_sum_to_one()
    {
        var m = Fixture.Value;
        var view = Analyze();

        Assert.NotEmpty(view.Nodes);
        Assert.All(view.Nodes, n =>
        {
            Assert.InRange(n.Share, 0.0, 1.0);
            Assert.InRange(n.PathsThrough, 0, view.TotalPaths);
            Assert.False(string.IsNullOrWhiteSpace(n.RoomType));
        });

        var children = m.Map.ByCoord[m.Position].Children;
        var childShares = children
            .Select(k => view.Nodes.Single(n => n.Col == k.Col && n.Row == k.Row).Share)
            .Sum();
        Assert.Equal(1.0, childShares, 9);

        // Ni la position courante ni le boss (terminal) ne sont des choix de route.
        Assert.DoesNotContain(view.Nodes, n => (n.Col, n.Row) == (m.Position.Col, m.Position.Row));
        Assert.DoesNotContain(view.Nodes, n => (n.Col, n.Row) == (m.Map.Boss.Coord.Col, m.Map.Boss.Coord.Row));
    }

    [Fact]
    public void Node_frequencies_match_brute_force_enumeration()
    {
        var m = Fixture.Value;
        var view = Analyze();
        var brute = BruteForcePaths(m.Map, m.Position);

        foreach (var n in view.Nodes)
        {
            var coord = new MapCoord(n.Col, n.Row);
            var through = brute.Count(p => p.Contains(coord));
            Assert.Equal(through, n.PathsThrough);
            Assert.Equal((double)through / brute.Count, n.Share, 12);
        }

        // Toutes les cases atteignables (hors position et boss) sont listées.
        var expected = brute.SelectMany(p => p)
            .Where(c => c != m.Position && c != m.Map.Boss.Coord)
            .Distinct()
            .Count();
        Assert.Equal(expected, view.Nodes.Count);
    }

    // ---------- 5. déterminisme ----------

    [Fact]
    public void Two_identical_calls_return_identical_values()
    {
        var m = Fixture.Value;
        var analyzer = new PathAnalyzer();

        var a = analyzer.Analyze(m.Map, m.Position, m.ActIndex, "CHARACTER.SILENT", 70);
        var b = analyzer.Analyze(m.Map, m.Position, m.ActIndex, "CHARACTER.SILENT", 70);
        AssertSame(a, b);

        // Instance neuve, caractère et PV différents : mêmes chemins (aucun état, aucune graine).
        var c = new PathAnalyzer().Analyze(m.Map, m.Position, m.ActIndex, "CHARACTER.DEFECT", 1);
        AssertSame(a, c);
    }

    private static void AssertSame(PathsView a, PathsView b)
    {
        Assert.Equal(a.TotalPaths, b.TotalPaths);
        Assert.Equal(a.NextChoices.Count, b.NextChoices.Count);
        for (int i = 0; i < a.NextChoices.Count; i++)
        {
            var x = a.NextChoices[i];
            var y = b.NextChoices[i];
            Assert.Equal(x.Col, y.Col);
            Assert.Equal(x.Row, y.Row);
            Assert.Equal(x.RoomType, y.RoomType);
            Assert.Equal(x.PathCount, y.PathCount);
            Assert.Equal(x.Min, y.Min);
            Assert.Equal(x.Median, y.Median);
            Assert.Equal(x.Max, y.Max);
            Assert.Equal(x.ExpectedDamage, y.ExpectedDamage);
            Assert.Equal(x.RiskBasis, y.RiskBasis);
        }
        Assert.Equal(a.Nodes.Count, b.Nodes.Count);
        for (int i = 0; i < a.Nodes.Count; i++)
        {
            Assert.Equal(a.Nodes[i].Col, b.Nodes[i].Col);
            Assert.Equal(a.Nodes[i].Row, b.Nodes[i].Row);
            Assert.Equal(a.Nodes[i].RoomType, b.Nodes[i].RoomType);
            Assert.Equal(a.Nodes[i].PathsThrough, b.Nodes[i].PathsThrough);
            Assert.Equal(a.Nodes[i].Share, b.Nodes[i].Share);
        }
    }

    // ---------- 6. performance ----------

    [Fact]
    public void Two_hundred_calls_stay_under_one_second()
    {
        var m = Fixture.Value;
        var analyzer = new PathAnalyzer();

        analyzer.Analyze(m.Map, m.Position, m.ActIndex, "CHARACTER.SILENT", 70); // warm-up JIT

        // Deux instruments, deux rôles (voir DeckTests.Deck_de_60_cartes_reste_sous_5ms) : le temps
        // mural dépend de la charge de la machine — cinq projets de tests tournent en parallèle dans
        // la solution, et ce test tombait sans qu'aucune ligne de code n'ait changé. Les octets
        // alloués par thread sont déterministes ; le temps reste un plafond de catastrophe.
        const int calls = 200;
        var alloc0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < calls; i++)
            analyzer.Analyze(m.Map, m.Position, m.ActIndex, "CHARACTER.SILENT", 70);
        sw.Stop();
        var allocPerCall = (GC.GetAllocatedBytesForCurrentThread() - alloc0) / calls;
        var perCall = sw.Elapsed.TotalMilliseconds / calls;

        Assert.True(allocPerCall < 2_000_000,
            $"moins de 2 Mo alloués par analyse, mesuré {allocPerCall} o");
        Assert.True(sw.ElapsedMilliseconds < 4000,
            $"{calls} appels en {sw.ElapsedMilliseconds} ms (moyenne {perCall:0.###} ms)");
    }

    // ---------- 7. cas limites ----------

    [Fact]
    public void Position_on_the_boss_returns_an_empty_view_without_throwing()
    {
        var m = Fixture.Value;
        var view = new PathAnalyzer().Analyze(m.Map, m.Map.Boss.Coord, m.ActIndex, "CHARACTER.SILENT", 70);

        // Documenté : au boss il n'y a plus de chemin à parcourir → TotalPaths = 0 (et non 1),
        // ce qui garde l'invariant Σ PathChoice.PathCount == TotalPaths.
        Assert.Equal(0, view.TotalPaths);
        Assert.Empty(view.NextChoices);
        Assert.Empty(view.Nodes);
    }

    [Fact]
    public void Map_without_children_does_not_throw()
    {
        var start = new MapNode(new MapCoord(3, 0), MapPointKind.Ancient, "ancient", Array.Empty<MapCoord>());
        var boss = new MapNode(new MapCoord(3, 16), MapPointKind.Boss, "boss", Array.Empty<MapCoord>());
        var map = new ActMap(7, 16, start, boss, null, new[] { start.Coord }, Array.Empty<MapNode>());

        var fromStart = new PathAnalyzer().Analyze(map, start.Coord, 0, "CHARACTER.SILENT", 70);
        Assert.Equal(0, fromStart.TotalPaths);
        Assert.Empty(fromStart.NextChoices);
        Assert.Empty(fromStart.Nodes);

        var fromBoss = new PathAnalyzer().Analyze(map, boss.Coord, 0, "CHARACTER.SILENT", 70);
        Assert.Equal(0, fromBoss.TotalPaths);
        Assert.Empty(fromBoss.NextChoices);
    }

    [Fact]
    public void Position_outside_the_map_returns_an_empty_view()
    {
        var m = Fixture.Value;
        var view = new PathAnalyzer().Analyze(m.Map, new MapCoord(99, 99), m.ActIndex, "CHARACTER.SILENT", 70);

        Assert.Equal(0, view.TotalPaths);
        Assert.Empty(view.NextChoices);
        Assert.Empty(view.Nodes);
    }

    [Fact]
    public void Position_at_the_start_explores_all_four_branches()
    {
        var m = Fixture.Value;
        var view = new PathAnalyzer().Analyze(m.Map, m.Map.Start.Coord, m.ActIndex, "CHARACTER.SILENT", 70);
        var brute = BruteForcePaths(m.Map, m.Map.Start.Coord);

        Assert.Equal(4, view.NextChoices.Count);
        Assert.Equal(brute.Count, view.TotalPaths);
        Assert.Equal(view.TotalPaths, view.NextChoices.Sum(c => c.PathCount));
        Assert.Equal(1.0, view.NextChoices.Sum(c => view.Nodes.Single(n => n.Col == c.Col && n.Row == c.Row).Share), 9);
    }

    // ---------- 8. risque : source explicite, jamais de faux chiffre ----------

    [Fact]
    public void Without_personal_history_risk_says_no_data()
    {
        var view = Analyze();

        Assert.All(view.NextChoices, c =>
        {
            Assert.Equal("aucune donnée", c.RiskBasis);
            Assert.Equal(0, c.ExpectedDamage);
        });
    }

    [Fact]
    public void Expected_damage_is_mean_rooms_times_personal_risk()
    {
        var m = Fixture.Value;
        var stats = new FixedStats();
        var view = Analyze(stats);
        var brute = BruteForcePaths(m.Map, m.Position);

        foreach (var c in view.NextChoices)
        {
            var counts = brute.Where(p => p[0] == new MapCoord(c.Col, c.Row))
                .Select(p => CountRooms(m.Map, p))
                .ToList();

            var expected = counts.Average(x => x.Monster) * 5 + counts.Average(x => x.Elite) * 12;
            Assert.Equal(expected, c.ExpectedDamage, 9);

            Assert.Contains("test: 20 salles", c.RiskBasis);
            Assert.DoesNotContain("aucune donnée", c.RiskBasis);
            Assert.Contains("sans donnée", c.RiskBasis); // unknown/rest/shop/treasure non couverts
        }

        Assert.Contains(view.NextChoices, c => c.ExpectedDamage > 0);
    }

    private sealed class FixedStats : IPersonalStats
    {
        public PersonalCardStats? Card(string cardId) => null;

        public RoomRisk RoomRisk(int actIndex, string roomType) => roomType switch
        {
            "monster" => new RoomRisk(5, 20, "test: 20 salles"),
            "elite" => new RoomRisk(12, 8, "test: 20 salles"),
            _ => new RoomRisk(0, 0, "aucune donnée")
        };

        public int RunCount => 12;

        public int RunsForCharacter(string characterId) => 12;
    }

    // ---------- 9. carte synthétique au-delà du cap (DP + échantillonnage déterministe) ----------

    [Fact]
    public void Huge_dag_beyond_the_cap_stays_exact_and_deterministic()
    {
        // 3 nœuds par rangée, 13 rangées, connectivité totale : 3^13 = 1 594 323 chemins > PathCap.
        var points = new List<MapNode>();
        var boss = new MapNode(new MapCoord(3, 15), MapPointKind.Boss, "boss", Array.Empty<MapCoord>());
        var start = new MapNode(new MapCoord(3, 0), MapPointKind.Ancient, "ancient",
            new[] { new MapCoord(1, 1), new MapCoord(3, 1), new MapCoord(5, 1) });

        for (int row = 1; row <= 13; row++)
        {
            IReadOnlyList<MapCoord> children = row == 13
                ? new[] { boss.Coord }
                : new[] { new MapCoord(1, row + 1), new MapCoord(3, row + 1), new MapCoord(5, row + 1) };

            points.Add(new MapNode(new MapCoord(1, row), MapPointKind.Monster, "monster", children));
            points.Add(new MapNode(new MapCoord(3, row), MapPointKind.Elite, "elite", children));
            points.Add(new MapNode(new MapCoord(5, row), MapPointKind.RestSite, "rest_site", children));
        }

        var map = new ActMap(7, 16, start, boss, null,
            new[] { new MapCoord(1, 1), new MapCoord(3, 1), new MapCoord(5, 1) }, points);

        var analyzer = new PathAnalyzer();
        var view = analyzer.Analyze(map, start.Coord, 0, "CHARACTER.SILENT", 70);

        Assert.True(view.TotalPaths > PathAnalyzer.PathCap, $"TotalPaths={view.TotalPaths}");
        Assert.Equal(1594323, view.TotalPaths);
        Assert.Equal(3, view.NextChoices.Count);
        Assert.Equal(view.TotalPaths, view.NextChoices.Sum(c => c.PathCount));

        foreach (var c in view.NextChoices)
        {
            // 13 rangées de salles, boss exclu. Min/max restent exacts via DP malgré le cap :
            // la salle de départ du chemin est inévitable (1), les autres types peuvent manquer (0),
            // et chaque type peut couvrir toutes les rangées restantes (13, ou 12 hors type de départ).
            Assert.Equal(1, c.Min.Monster + c.Min.Elite + c.Min.Rest);
            Assert.Equal(c.RoomType == "monster" ? 13 : 12, c.Max.Monster);
            Assert.Equal(c.RoomType == "elite" ? 13 : 12, c.Max.Elite);
            Assert.Equal(c.RoomType == "rest_site" ? 13 : 12, c.Max.Rest);
            Assert.True(c.Min.Monster <= c.Median.Monster && c.Median.Monster <= c.Max.Monster);
            Assert.True(c.Min.Elite <= c.Median.Elite && c.Median.Elite <= c.Max.Elite);
            Assert.True(c.Min.Rest <= c.Median.Rest && c.Median.Rest <= c.Max.Rest);
            // Médiane échantillonnée : ~13/3 par type, jamais hors bornes.
            Assert.InRange(c.Median.Monster, 2, 6);
            Assert.InRange(c.Median.Elite, 2, 6);
            Assert.InRange(c.Median.Rest, 2, 6);
        }

        // Fréquences exactes (préfixe × suffixe) : 39 nœuds, chacun sur 1/3 des chemins.
        Assert.Equal(39, view.Nodes.Count);
        Assert.All(view.Nodes, n => Assert.Equal(1.0 / 3.0, n.Share, 9));
        Assert.Equal(1.0, view.Nodes.Where(n => n.Row == 1).Sum(n => n.Share), 9);

        // Déterminisme de l'échantillonnage (aucun aléatoire).
        var again = analyzer.Analyze(map, start.Coord, 0, "CHARACTER.SILENT", 70);
        AssertSame(view, again);
    }

    [Fact]
    public void Cycle_in_a_corrupt_map_is_reported_as_empty_not_crash()
    {
        var a = new MapCoord(1, 1);
        var b = new MapCoord(3, 2);
        var start = new MapNode(new MapCoord(3, 0), MapPointKind.Ancient, "ancient", new[] { a });
        var boss = new MapNode(new MapCoord(3, 16), MapPointKind.Boss, "boss", Array.Empty<MapCoord>());
        var map = new ActMap(7, 16, start, boss, null, new[] { a }, new[]
        {
            new MapNode(a, MapPointKind.Monster, "monster", new[] { b }),
            new MapNode(b, MapPointKind.Monster, "monster", new[] { a })
        });

        var view = new PathAnalyzer().Analyze(map, start.Coord, 0, "CHARACTER.SILENT", 70);

        Assert.Equal(0, view.TotalPaths);
        Assert.Empty(view.NextChoices);
        Assert.Empty(view.Nodes);
    }

    /// <summary>
    /// Contrat v1.1 : `PathChoice.RoomType` / `NodeFrequency.RoomType` ne doivent contenir que les
    /// valeurs fixées par <see cref="RoomTypes"/>. Un ancien libellé (« event », « rest »…) casserait
    /// la correspondance côté overlay sans erreur visible.
    /// </summary>
    [Fact]
    public void Room_types_are_the_fixed_contract_values()
    {
        var view = Analyze();

        Assert.NotEmpty(view.NextChoices);
        Assert.All(view.NextChoices, c => Assert.Contains(c.RoomType, RoomTypes.All));
        Assert.All(view.Nodes, n => Assert.Contains(n.RoomType, RoomTypes.All));

        // Les 6 types comptés dans RoomCounts viennent aussi du contrat.
        foreach (var t in PathAnalyzer.CountedTypes)
            Assert.Contains(t, RoomTypes.All);
    }
}
