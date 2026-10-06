using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class TerrainAndApproachTests
{
    private static GameState Corridor() => new()
    {
        Physical = new(new Board(7, 1, []), [new("monster", new(0, 0)), new("hero", new(6, 0))]),
        Types = [new("monster-type", 1, 1, 1, 0, 1), new("hero-type", 0, 0, 0, 0, 1)],
        Units = [new("monster", "monster-type", "red", 1), new("hero", "hero-type", "blue", 1)]
    };

    [Theory]
    [InlineData(TerrainKind.Grass, true, false)]
    [InlineData(TerrainKind.Tree, false, true)]
    [InlineData(TerrainKind.Water, false, false)]
    [InlineData(TerrainKind.StoneFloor, true, false)]
    [InlineData(TerrainKind.StoneFloorWithTable, false, false)]
    public void TerrainSeparatesMovementAndSight(TerrainKind kind, bool passable, bool blocksLos)
    {
        var state = Corridor();
        state.Physical.Board.Terrain.Add(new(new(1, 0), kind));
        state.Types[0] = state.Types[0] with { Rng = 6 };
        Assert.Equal(passable, MovementRules.FindPaths(state, "monster", new(0, 0)).ContainsKey(new(2, 0)));
        Assert.Equal(passable ? 6 : (int?)null, ApproachRules.Distance(state.Physical.Board, new(0, 0), new(6, 0)));
        Assert.Equal(blocksLos ? UnitTargetEvaluation.NotPossible : UnitTargetEvaluation.Possible,
            AttackRules.EvaluateFrom(state, "monster", new(0, 0), "hero"));
    }

    [Theory]
    [InlineData(EdgeKind.None, true, false)]
    [InlineData(EdgeKind.Wall, false, true)]
    [InlineData(EdgeKind.ClosedDoor, false, true)]
    [InlineData(EdgeKind.OpenDoor, true, false)]
    [InlineData(EdgeKind.WallWithWindow, false, false)]
    public void SharedEdgesSeparateMovementAndSight(EdgeKind kind, bool passable, bool blocksLos)
    {
        var state = Corridor();
        state.Physical.Board.Edges.Add(new(new(1, 0), new(0, 0), kind));
        state.Types[0] = state.Types[0] with { Rng = 6 };
        Assert.Equal(passable, MovementRules.FindPaths(state, "monster", new(0, 0)).ContainsKey(new(1, 0)));
        Assert.Equal(passable ? 6 : (int?)null, ApproachRules.Distance(state.Physical.Board, new(0, 0), new(6, 0)));
        Assert.Equal(passable ? 6 : (int?)null, ApproachRules.Distance(state.Physical.Board, new(6, 0), new(0, 0)));
        Assert.Equal(blocksLos ? UnitTargetEvaluation.NotPossible : UnitTargetEvaluation.Possible,
            AttackRules.EvaluateFrom(state, "monster", new(0, 0), "hero"));
    }

    [Fact]
    public void ImpassableGoalIsOnlyAnEndpointAndDoesNotBypassAnEdge()
    {
        var board = Corridor().Physical.Board;
        board.Terrain.Add(new(new(2, 0), TerrainKind.Water));
        var distances = ApproachRules.Distances(board, new(0, 0), new(2, 0));
        Assert.Equal(2, distances[new(2, 0)]);
        Assert.DoesNotContain(new Cell(3, 0), distances.Keys);
        Assert.Null(ApproachRules.Distance(board, new(0, 0), new(6, 0)));
        board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.Wall));
        Assert.Null(ApproachRules.Distance(board, new(0, 0), new(2, 0)));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("blue")]
    public void MonstersBehindDoorwayOccupantsRankApproachThroughThem(string occupantSide)
    {
        var state = Corridor();
        state.Physical.Board.Edges.Add(new(new(3, 0), new(4, 0), EdgeKind.OpenDoor));
        foreach (var x in new[] { 3, 4, 5 })
        {
            state.Units.Add(new($"front-{x}", "hero-type", occupantSide, 1));
            state.Physical.Figures.Add(new($"front-{x}", new(x, 0)));
        }
        var original = JsonSerializer.Serialize(state);
        var paths = MovementRules.FindPaths(state, "monster", new(0, 0), 1);
        var request = new DecisionRequest(DecisionKind.Move, "monster-type", "monster",
            paths.Where(p => p.Key != new Cell(0, 0)).Select(p =>
                new Candidate($"{p.Key.X},{p.Key.Y}", p.Key, p.Value.ToList())).ToList(), true);
        var queries = new GameplayQueries(state);
        Assert.Equal(occupantSide == "red" ? 4 : 1, queries.DistanceToAttackPositionFrom("monster", new(1, 0)));
        Assert.Equal("1,0", ApproachMovementProvider.ChooseMovement(request, queries));
        var actual = MovementRules.FindPaths(state, "monster", new(0, 0));
        Assert.DoesNotContain(new Cell(3, 0), actual.Keys);
        Assert.DoesNotContain(new Cell(5, 0), actual.Keys);
        Assert.Equal(original, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void TreeCornerTouchDoesNotBlockDiagonalMelee()
    {
        var state = Corridor();
        state.Physical = new(new Board(2, 2, []) { Terrain = [new(new(1, 0), TerrainKind.Tree)] },
            [new("monster", new(0, 0)), new("hero", new(1, 1))]);
        Assert.Equal(UnitTargetEvaluation.Possible, AttackRules.EvaluateFrom(state, "monster", new(0, 0), "hero"));
    }

    [Fact]
    public void TerrainSurvivesSerializationAndDetachedQueries()
    {
        var state = Corridor();
        state.Physical.Board.Terrain.Add(new(new(2, 0), TerrainKind.Water));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        var queries = new GameplayQueries(restored);
        restored.Physical.Board.Terrain.Clear();
        Assert.Null(queries.DistanceToAttackPositionFrom("monster", new(0, 0)));
        Assert.Equal(5, new GameplayQueries(restored).DistanceToAttackPositionFrom("monster", new(0, 0)));
    }
}
