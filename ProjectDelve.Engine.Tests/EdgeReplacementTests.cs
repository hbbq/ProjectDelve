using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EdgeReplacementTests
{
    private static GameState World() => new()
    {
        Physical = new(new Board(7, 7, []), []), Types = [], Units = []
    };

    [Fact]
    public void ExplicitReplacementClearsDoorsAndLeavesUnspecifiedEdgesAloneInEitherOrder()
    {
        var first = World(); var second = World();
        Edge[] initial = [new(new(1, 1), new(2, 1), EdgeKind.ClosedDoor),
            new(new(2, 1), new(3, 1), EdgeKind.WallWithWindow), new(new(5, 5), new(6, 5), EdgeKind.OpenDoor)];
        first.Physical.Board.Edges.AddRange(initial); second.Physical.Board.Edges.AddRange(initial);
        Edge[] desired = [new(new(2, 1), new(1, 1), EdgeKind.None),
            new(new(2, 1), new(3, 1), EdgeKind.Wall), new(new(0, 0), new(1, 0), EdgeKind.OpenDoor)];
        EdgeReplacementRules.ReplaceEdges(first, desired);
        EdgeReplacementRules.ReplaceEdges(second, desired.Reverse());
        Assert.Equal(first.Physical.Board.Edges, second.Physical.Board.Edges);
        Assert.Equal(EdgeKind.None, first.Physical.Board.EdgeBetween(new(1, 1), new(2, 1)));
        Assert.Contains(initial[2], first.Physical.Board.Edges);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, EdgeKind.Wall)]
    [InlineData(6, 0, 7, 0, EdgeKind.None)]
    [InlineData(1, 1, 1, 1, EdgeKind.Wall)]
    [InlineData(1, 1, 2, 2, EdgeKind.Wall)]
    [InlineData(1, 1, 3, 1, EdgeKind.Wall)]
    [InlineData(1, 1, 2, 1, (EdgeKind)999)]
    public void InvalidGeometryOrKindDoesNotPreventIndependentReplacement(int x, int y, int bx, int by, EdgeKind kind)
    {
        var state = World(); var old = new Edge(new(1, 1), new(2, 1), EdgeKind.ClosedDoor);
        state.Physical.Board.Edges.Add(old);
        var bad = new Edge(new(x, y), new(bx, by), kind);
        Assert.False(EdgeReplacementRules.CanReplace(state, bad));
        EdgeReplacementRules.ReplaceEdges(state, [bad, new(new(3, 3), new(4, 3), EdgeKind.Wall)]);
        Assert.Contains(old, state.Physical.Board.Edges);
        Assert.Equal(EdgeKind.Wall, state.Physical.Board.EdgeBetween(new(3, 3), new(4, 3)));
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.WallWithWindow, false)]
    [InlineData(EdgeKind.ClosedDoor, true)]
    [InlineData(EdgeKind.OpenDoor, true)]
    [InlineData(EdgeKind.None, true)]
    public void WaterWaterRejectsOnlyWallKindsAndRetainsExactOldDoor(EdgeKind desired, bool allowed)
    {
        var state = World(); var old = new Edge(new(1, 1), new(2, 1), EdgeKind.ClosedDoor);
        state.Physical.Board.Edges.Add(old);
        state.Physical.Board.Terrain.AddRange([new(old.A, TerrainKind.Water), new(old.B, TerrainKind.Water)]);
        Assert.Equal(allowed, EdgeReplacementRules.CanReplace(state, old with { Kind = desired }));
        EdgeReplacementRules.ReplaceEdges(state, [old with { Kind = desired }]);
        Assert.Equal(allowed ? desired : old.Kind, state.Physical.Board.EdgeBetween(old.A, old.B));
        if (!allowed) Assert.Same(old, Assert.Single(state.Physical.Board.Edges));
    }

    [Theory]
    [InlineData(Posture.Upright, false)]
    [InlineData(Posture.Lying, false)]
    [InlineData(Posture.Upright, true)]
    [InlineData(Posture.Lying, true)]
    public void LargeFigureInternalEdgesRemainPassableRegardlessOfPostureOrPhase(Posture posture, bool phase)
    {
        var state = World(); var type = UnitRoster.RedDragon() with { Phase = phase ? new() : null };
        state.Types.Add(type); state.Units.Add(type.CreateUnit("large", "red"));
        state.Physical.Figures.Add(new("large", new(2, 2), posture));
        var old = new Edge(new(2, 2), new(3, 2), EdgeKind.OpenDoor);
        state.Physical.Board.Edges.Add(old);
        foreach (var kind in new[] { EdgeKind.Wall, EdgeKind.WallWithWindow, EdgeKind.ClosedDoor })
        {
            EdgeReplacementRules.ReplaceEdges(state, [old with { Kind = kind }]);
            Assert.Same(old, Assert.Single(state.Physical.Board.Edges));
        }
        EdgeReplacementRules.ReplaceEdges(state, [old with { Kind = EdgeKind.None },
            new(new(2, 3), new(3, 3), EdgeKind.OpenDoor), new(new(1, 2), new(2, 2), EdgeKind.Wall)]);
        Assert.Equal(EdgeKind.None, state.Physical.Board.EdgeBetween(old.A, old.B));
        Assert.Equal(EdgeKind.OpenDoor, state.Physical.Board.EdgeBetween(new(2, 3), new(3, 3)));
        Assert.Equal(EdgeKind.Wall, state.Physical.Board.EdgeBetween(new(1, 2), new(2, 2)));
        Assert.Equal(posture, state.Physical.Figures[0].Posture);
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.WallWithWindow)]
    public void InitialSetupRejectsWaterWaterWallsUsingSharedLegality(EdgeKind kind)
    {
        var state = World(); var edge = new Edge(new(1, 1), new(2, 1), kind);
        state.Physical.Board.Edges.Add(edge);
        state.Physical.Board.Terrain.AddRange([new(edge.A, TerrainKind.Water), new(edge.B, TerrainKind.Water)]);
        Assert.Throws<ArgumentException>(() => GameEngine.StartRound(state, new Dice()));
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
