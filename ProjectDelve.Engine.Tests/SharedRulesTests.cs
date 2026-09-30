using System.Text.Json;
using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SharedRulesTests
{
    private static GameState State(int rng = 1, int atk = 1) => new()
    {
        Physical = new PhysicalState(new Board(5, 3, []),
            [new Figure("mover", new Cell(0, 1)), new Figure("friend", new Cell(1, 1)),
                new Figure("target", new Cell(4, 1))]),
        Types = [new UnitType("mover-type", 1, rng, atk, 0, 1),
            new UnitType("other", 0, 0, 0, 0, 1)],
        Units = [new Unit("mover", "mover-type", "blue", 1),
            new Unit("friend", "other", "blue", 1), new Unit("target", "other", "red", 1)]
    };

    [Fact]
    public void HypotheticalMovementVacatesOriginalCellAndPreservesOtherOccupants()
    {
        var state = State();
        var original = JsonSerializer.Serialize(state);
        var from = new Cell(2, 1);

        var paths = MovementRules.FindPaths(state, "mover", from);

        Assert.Equal(new[] { from }, paths[from]);
        Assert.Equal(new[] { from, new Cell(1, 1), new Cell(0, 1) }, paths[new Cell(0, 1)]);
        Assert.False(paths.ContainsKey(new Cell(1, 1))); // Friendly pass-through only.
        Assert.False(paths.ContainsKey(new Cell(4, 1))); // Hostile occupancy.
        Assert.Equal(original, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void UnlimitedSearchAndBoundedSearchUseTheSameCanonicalPaths()
    {
        var state = State();
        var from = new Cell(0, 1);
        var all = MovementRules.FindPaths(state, "mover", from);
        var bounded = MovementRules.FindPaths(state, "mover", from, 2);

        Assert.Equal(all.Where(p => p.Value.Count - 1 <= 2).Select(p => p.Key), bounded.Keys);
        foreach (var (destination, path) in bounded)
            Assert.Equal(all[destination], path);
        Assert.Equal(new[] { from }, MovementRules.FindPaths(state, "mover", from, 0)[from]);
        Assert.Single(MovementRules.FindPaths(state, "mover", from, 0));
        Assert.True(all[new Cell(3, 1)].Count - 1 > state.Types[0].Mov);
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.OpenDoor, true)]
    public void MovementRetainsEdgePassability(EdgeKind kind, bool reachable)
    {
        var state = State();
        var from = new Cell(0, 1);
        var to = new Cell(0, 0);
        state.Physical.Board.Edges.Add(new Edge(from, to, kind));

        Assert.Equal(reachable, MovementRules.FindPaths(state, "mover", from, 1).ContainsKey(to));
    }

    [Fact]
    public void HypotheticalRulesMatchPhysicalRelocationWithoutMutatingTheWorld()
    {
        var state = State(rng: 3);
        var original = JsonSerializer.Serialize(state);
        var from = new Cell(2, 1);
        var relocated = JsonSerializer.Deserialize<GameState>(original)!;
        relocated.Physical.Figures[0] = relocated.Physical.Figures[0] with { Position = from };

        var hypotheticalPaths = MovementRules.FindPaths(state, "mover", from);
        var relocatedPaths = MovementRules.FindPaths(relocated, "mover", from);
        Assert.Equal(relocatedPaths.Keys, hypotheticalPaths.Keys);
        foreach (var (destination, path) in relocatedPaths)
            Assert.Equal(path, hypotheticalPaths[destination]);
        foreach (var target in state.Units)
            Assert.Equal(AttackRules.EvaluateFrom(relocated, "mover", from, target.Id),
                AttackRules.EvaluateFrom(state, "mover", from, target.Id));
        Assert.Equal(NormalAttackEvaluation.Possible,
            AttackRules.EvaluateFrom(state, "mover", from, "target"));
        Assert.Equal(original, JsonSerializer.Serialize(state));
    }

    [Theory]
    [InlineData(1, 1, 3, 0, true)] // Diagonal melee.
    [InlineData(1, 1, 2, 1, false)]
    [InlineData(2, 1, 3, 0, true)] // Manhattan distance 2.
    [InlineData(2, 1, 2, 0, false)]
    [InlineData(0, 1, 3, 1, false)]
    [InlineData(1, 0, 3, 1, false)]
    public void AttackRetainsRangeAndStatRules(int rng, int atk, int x, int y,
        bool possible)
    {
        Assert.Equal(possible, AttackRules.EvaluateFrom(State(rng, atk), "mover", new Cell(x, y), "target")
            == NormalAttackEvaluation.Possible);
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.OpenDoor, true)]
    public void OpenDoorLosIsClearWhileOtherFeaturedEdgesRemainUndefined(EdgeKind kind, bool clear)
    {
        var state = State(rng: 3);
        var from = new Cell(2, 1);
        state.Physical.Board.Edges.Add(new Edge(from, new Cell(3, 1), kind));

        Assert.Equal(clear ? NormalAttackEvaluation.Possible : NormalAttackEvaluation.UndefinedLineOfSight,
            AttackRules.EvaluateFrom(state, "mover", from, "target"));
    }

    [Theory]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.OpenDoor, true)]
    public void OpenDoorProvidesClearCornerPassage(EdgeKind kind, bool clear)
    {
        var state = State();
        var from = new Cell(3, 0);
        // Both passages are featured, but the upper one is clear when opened.
        state.Physical.Board.Edges.Add(new Edge(new Cell(3, 0), new Cell(4, 0), kind));
        state.Physical.Board.Edges.Add(new Edge(new Cell(3, 0), new Cell(3, 1), EdgeKind.Wall));

        Assert.Equal(clear ? NormalAttackEvaluation.Possible : NormalAttackEvaluation.UndefinedLineOfSight,
            AttackRules.EvaluateFrom(state, "mover", from, "target"));
    }

    [Fact]
    public void InterveningHostileLosRemainsUndefinedWhileFriendlyLosIsClear()
    {
        var state = State(rng: 4);
        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new Cell(2, 1) };
        var from = new Cell(0, 1);
        Assert.Equal(NormalAttackEvaluation.Possible,
            AttackRules.EvaluateFrom(state, "mover", from, "target"));

        state.Units[1] = state.Units[1] with { SideId = "red" };
        Assert.Equal(NormalAttackEvaluation.UndefinedLineOfSight,
            AttackRules.EvaluateFrom(state, "mover", from, "target"));
        Assert.Equal(NormalAttackEvaluation.Possible,
            AttackRules.EvaluateFrom(state, "mover", from, "friend"));
    }

    [Fact]
    public void FriendlyAndDeadTargetsCannotBeAttacked()
    {
        var state = State(rng: 4);
        Assert.Equal(NormalAttackEvaluation.NotPossible,
            AttackRules.EvaluateFrom(state, "mover", new Cell(0, 1), "friend"));
        state.Units[2] = state.Units[2] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAt(2);
        Assert.Equal(NormalAttackEvaluation.NotPossible,
            AttackRules.EvaluateFrom(state, "mover", new Cell(0, 1), "target"));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(5, 1)]
    [InlineData(1, 1)] // Friendly occupied.
    [InlineData(4, 1)] // Hostile occupied.
    public void HypotheticalOriginsMustBeOnBoardAndFreeOfOtherFigures(int x, int y)
    {
        var state = State();
        var from = new Cell(x, y);
        Assert.Throws<ArgumentException>(() => MovementRules.FindPaths(state, "mover", from));
        Assert.Throws<ArgumentException>(() => AttackRules.EvaluateFrom(state, "mover", from, "target"));
    }
}
