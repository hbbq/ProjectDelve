using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class GameplayQueryTests
{
    private static GameState State() => new()
    {
        Physical = new PhysicalState(new Board(5, 1, []),
            [new Figure("monster", new Cell(1, 0)), new Figure("hostile", new Cell(4, 0))]),
        Types = [new UnitType("monster-type", 1, 1, 1, 0, 1), new UnitType("other", 0, 0, 0, 0, 1)],
        Units = [new Unit("monster", "monster-type", "red", 1), new Unit("hostile", "other", "blue", 1)]
    };

    [Fact]
    public void DistanceMeasuresAttackPositionBeyondMovRatherThanHostileCell()
    {
        var queries = new GameplayQueries(State());
        Assert.Equal(2, queries.DistanceToAttackPositionFrom("monster", new Cell(1, 0)));
        Assert.Equal(0, queries.DistanceToAttackPositionFrom("monster", new Cell(3, 0)));
        Assert.True(queries.CanAttackHostileFrom("monster", new Cell(3, 0)));
    }

    [Fact]
    public void HypotheticalPositionVacatesOriginalCellWithoutChangingWorld()
    {
        var state = State();
        var original = JsonSerializer.Serialize(state);
        var queries = new GameplayQueries(state);
        // Route from 0 to the attack cell crosses the monster's original cell.
        Assert.Equal(3, queries.DistanceToAttackPositionFrom("monster", new Cell(0, 0)));
        Assert.Equal(new Cell(1, 0), queries.PositionOf("monster"));
        Assert.Equal(original, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void QueriesRemainBoundToDetachedSnapshot()
    {
        var state = State();
        var queries = new GameplayQueries(state);
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new Cell(0, 0) };
        state.Physical.Board.Edges.Add(new Edge(new Cell(2, 0), new Cell(3, 0), EdgeKind.Wall));
        state.Types[0] = state.Types[0] with { Atk = 0 };
        state.Units.Clear();

        Assert.Equal(new Cell(1, 0), queries.PositionOf("monster"));
        Assert.True(queries.CanAttackHostileFrom("monster", new Cell(3, 0)));
        Assert.Equal(2, queries.DistanceToAttackPositionFrom("monster", new Cell(1, 0)));
        Assert.Equal(3, queries.ManhattanDistanceBetweenUnits("monster", "hostile"));
    }

    [Fact]
    public void FriendlyOccupiedAttackPositionCannotBeUsedAsDestination()
    {
        var state = State();
        state.Units.Add(new Unit("friend", "other", "red", 1));
        state.Physical.Figures.Add(new Figure("friend", new Cell(3, 0)));
        // The only melee position is occupied, although traversal permits friends.
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("monster", new Cell(1, 0)));
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.ClosedDoor)]
    [InlineData(EdgeKind.OpenDoor)]
    public void UndefinedEdgeLosNeverCountsAsAnAttackPosition(EdgeKind kind)
    {
        var state = State();
        state.Physical.Board.Edges.Add(new Edge(new Cell(3, 0), new Cell(4, 0), kind));
        var queries = new GameplayQueries(state);

        Assert.False(queries.CanAttackHostileFrom("monster", new Cell(3, 0)));
        Assert.Null(queries.DistanceToAttackPositionFrom("monster", new Cell(1, 0)));
    }

    [Fact]
    public void QueriesUseSharedAttackEvaluationIncludingInterveningHostileLos()
    {
        var state = State();
        state.Types[0] = state.Types[0] with { Rng = 4 };
        state.Units.Add(new Unit("intervening", "other", "blue", 1));
        state.Physical.Figures.Add(new Figure("intervening", new Cell(2, 0)));
        var queries = new GameplayQueries(state);

        Assert.Equal(NormalAttackEvaluation.UndefinedLineOfSight,
            AttackRules.EvaluateFrom(state, "monster", new Cell(1, 0), "hostile"));
        // The intervening hostile remains a supported target itself.
        Assert.True(queries.CanAttackHostileFrom("monster", new Cell(1, 0)));
        Assert.Equal(0, queries.DistanceToAttackPositionFrom("monster", new Cell(1, 0)));
    }
}
