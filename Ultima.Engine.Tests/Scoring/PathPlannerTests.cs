using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Paths;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// La route compte autant que les cartes : une élite à 15 PV n'a pas la même valeur qu'à 70.
/// Le planificateur fait une DP arrière sur la carte et choisit la suite de salles.
/// </summary>
public sealed class PathPlannerTests
{
    private static MapNode N(string id, string type, params string[] next) => new(id, type, next);

    // start → (repos | élite) → boss : un seul combat risqué, la survie est possible.
    private static readonly MapNode[] Fork =
    {
        N("S", RoomTypes.Monster, "R", "E"),
        N("R", RoomTypes.RestSite, "B"),
        N("E", RoomTypes.Elite, "B"),
        N("B", RoomTypes.Boss),
    };

    [Fact]
    public void Wounded_player_rests_before_the_elite()
    {
        var plan = new PathPlanner().Plan(Fork, "S", new PathState(15, 70, 100));

        var second = plan.Steps[1];
        Assert.Equal(RoomTypes.RestSite, second.Type);
        Assert.Contains("repos", plan.Explanation);
    }

    [Fact]
    public void Healthy_player_takes_the_elite()
    {
        var plan = new PathPlanner().Plan(Fork, "S", new PathState(70, 70, 100));

        Assert.Equal(RoomTypes.Elite, plan.Steps[1].Type);
    }

    [Fact]
    public void Shop_is_only_worth_it_with_gold()
    {
        var map = new[]
        {
            N("S", RoomTypes.Monster, "SH", "M"),
            N("SH", RoomTypes.Shop, "B"),
            N("M", RoomTypes.Monster, "B"),
            N("B", RoomTypes.Boss),
        };
        var planner = new PathPlanner();

        var poor = planner.Plan(map, "S", new PathState(70, 70, 40));
        var rich = planner.Plan(map, "S", new PathState(70, 70, 300));

        Assert.Equal(RoomTypes.Monster, poor.Steps[1].Type);
        Assert.Equal(RoomTypes.Shop, rich.Steps[1].Type);
    }

    [Fact]
    public void Two_elites_in_a_row_are_worse_than_one_plus_a_rest()
    {
        // Même à 70 PV, enchaîner deux élites puis le boss tue : la DP doit préférer le repos
        // qui garde assez de PV pour encaisser le boss (dégâts cumulés, pas salle par salle).
        var map = new[]
        {
            N("S", RoomTypes.Monster, "R", "E"),
            N("R", RoomTypes.RestSite, "E2"),
            N("E", RoomTypes.Elite, "E2"),
            N("E2", RoomTypes.Elite, "B"),
            N("B", RoomTypes.Boss),
        };

        var plan = new PathPlanner().Plan(map, "S", new PathState(70, 70, 100));

        Assert.Equal(RoomTypes.RestSite, plan.Steps[1].Type);
        Assert.DoesNotContain("MORTEL", plan.Explanation);
    }

    [Fact]
    public void Cycle_does_not_hang()
    {
        var map = new[]
        {
            N("A", RoomTypes.Monster, "B"),
            N("B", RoomTypes.Monster, "A"),
        };

        var plan = new PathPlanner().Plan(map, "A", new PathState(70, 70, 0));
        Assert.NotEmpty(plan.Steps);
    }

    [Fact]
    public void Unknown_start_is_explicit()
    {
        var plan = new PathPlanner().Plan(Fork, "NOPE", new PathState(70, 70, 0));

        Assert.Empty(plan.Steps);
        Assert.Contains("inconnu", plan.Explanation);
    }

    [Fact]
    public void Explains_each_step_with_numbers()
    {
        var plan = new PathPlanner().Plan(Fork, "S", new PathState(70, 70, 100));

        Assert.All(plan.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
        Assert.Contains(plan.Steps, s => s.Reason.Contains("élite"));
    }
}
