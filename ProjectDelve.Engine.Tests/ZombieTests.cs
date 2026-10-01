using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ZombieTests
{
    private sealed class Random(int roll = 6) : IRandomProvider
    {
        public int Rolls { get; private set; }
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public int RollD6() { Rolls++; return roll; }
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    private sealed class Choice(Func<DecisionRequest, string?> choose) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => choose(request);
    }

    private static GameState Corridor(UnitType? type = null) => new()
    {
        Physical = new(new Board(5, 1, [new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor)]),
            [new("monster", new(0, 0)), new("hero", new(4, 0))]),
        Types = [type ?? UnitType.Zombie(), new("hero-type", 0, 0, 0, 0, 4)],
        Units = [new("monster", (type ?? UnitType.Zombie()).Id, "red", 1), new("hero", "hero-type", "blue", 4)]
    };

    [Fact]
    public void ZombieStatsAndApproach_DoNotChangeActualMovementOrLos()
    {
        var state = Corridor();
        var type = state.Types[0];
        Assert.Equal((2, 1, 3, 3, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(3, new GameplayQueries(state).DistanceToAttackPositionFrom("monster", new(0, 0)));
        Assert.False(new GameplayQueries(state).CanAttackHostileFrom("monster", new(1, 0)));
        var pending = GameEngine.StartRound(state, new Random());
        Assert.Equal(new Cell(1, 0), Assert.Single(pending.NextInput!.Candidates).Destination);
        Assert.DoesNotContain(MovementRules.FindPaths(state, "monster", new(0, 0)).Keys, c => c.X >= 2);
        state.Types[0] = type with { Capabilities = UnitCapability.None };
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("monster", new(0, 0)));
    }

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, true)]
    [InlineData(2, 3, false)]
    [InlineData(2, 4, false)]
    [InlineData(2, 5, false)]
    [InlineData(2, 6, false)]
    [InlineData(4, 4, true)]
    [InlineData(4, 5, false)]
    [InlineData(0, 1, false)]
    [InlineData(6, 6, true)]
    public void ReusableActionResolvesOneD6AndConsumesAct(int count, int roll, bool succeeds)
    {
        // No Zombie identity, factory, or agency is involved in this content.
        var state = Corridor(new("other-type", 2, 1, 1, 0, 1,
            TryOpenDoor: new(count), Capabilities: UnitCapability.ApproachThroughClosedDoors));
        var random = new Random(roll);
        var pending = GameEngine.StartRound(state, random);
        var moved = GameEngine.Advance(pending.State, new DefaultMonsterProvider(), random);
        Assert.Equal(new Cell(1, 0), moved.State.Physical.Figures[0].Position);
        Assert.NotNull(Assert.Single(moved.NextInput!.Candidates).TryOpenDoor);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(moved.State))!;
        var result = GameEngine.Advance(restored, new DefaultMonsterProvider(), random);
        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Equal(1, random.Rolls);
        Assert.Equal(succeeds ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor, result.State.Physical.Board.Edges[0].Kind);
        var attempt = Assert.Single(result.Events, e => e.Kind == "DoorOpeningAttemptResolved");
        Assert.Equal(succeeds, attempt.Succeeded);
        Assert.Equal(roll, attempt.DieRoll);
        Assert.Equal(count, attempt.SuccessCount);
        Assert.Equal(succeeds, result.Events.Any(e => e.Kind == "DoorOpened"));
        Assert.Equal(EdgeKind.ClosedDoor, restored.Physical.Board.Edges[0].Kind);
        Assert.Throws<InvalidOperationException>(() => GameEngine.Advance(result.State, new DefaultMonsterProvider(), random));
    }

    [Fact]
    public void AttackHasPriorityOverDoorAttempt()
    {
        var state = Corridor();
        state.Physical.Figures[0] = new("monster", new(1, 0));
        state.Physical.Figures[1] = new("hero", new(0, 0));
        var random = new Random();
        var pending = GameEngine.StartRound(state, random);
        // No legal movement exists; the zero-step Move completes automatically.
        var stayed = pending;
        Assert.Contains(stayed.NextInput!.Candidates, c => c.TryOpenDoor is not null);
        var result = GameEngine.Advance(stayed.State, new DefaultMonsterProvider(), random);
        Assert.Single(result.Events, e => e.Kind == "AttackResolved");
        Assert.Equal(0, random.Rolls);
        Assert.Equal(EdgeKind.ClosedDoor, result.State.Physical.Board.Edges[0].Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoorChoiceUsesOppositeCellTopLeftOrder(bool reverse)
    {
        var state = new GameState
        {
            Physical = new(new Board(3, 3, [new(new(1, 1), new(2, 1), EdgeKind.ClosedDoor),
                new(new(1, 1), new(0, 1), EdgeKind.ClosedDoor),
                new(new(1, 1), new(1, 0), EdgeKind.ClosedDoor),
                new(new(1, 2), new(1, 1), EdgeKind.ClosedDoor)]), [new("actor", new(1, 1))]),
            Types = [new("other", 0, 0, 0, 0, 1, UnitAction.None, new(2))],
            Units = [new("actor", "other", "blue", 1)]
        };
        if (reverse) state.Physical.Board.Edges.Reverse();
        var random = new Random(1);
        var pending = GameEngine.StartRound(state, random);
        Assert.Equal(4, pending.NextInput!.Candidates.Count);
        var result = GameEngine.Advance(pending.State, new DefaultMonsterProvider(), random);
        var opened = Assert.Single(result.Events, e => e.Kind == "DoorOpened").Door!;
        Assert.True(opened.A == new Cell(1, 0) || opened.B == new Cell(1, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void InvalidRandomFacesAreRejected(int roll)
    {
        var random = new Random(roll);
        var pending = GameEngine.StartRound(Corridor(), random);
        var moved = GameEngine.Advance(pending.State, new DefaultMonsterProvider(), random);
        Assert.Throws<ArgumentException>(() => GameEngine.Advance(moved.State, new DefaultMonsterProvider(), random));
        Assert.Equal(EdgeKind.ClosedDoor, moved.State.Physical.Board.Edges[0].Kind);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void InvalidSuccessCountsAreRejected(int count)
    {
        Assert.Throws<ArgumentException>(() => GameEngine.StartRound(
            Corridor(UnitType.Zombie() with { TryOpenDoor = new(count) }), new Random()));
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.WallWithWindow)]
    public void CapabilityDoesNotCrossOtherImpassableEdges(EdgeKind edge)
    {
        var state = Corridor();
        state.Physical.Board.Edges[0] = state.Physical.Board.Edges[0] with { Kind = edge };
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("monster", new(0, 0)));
    }

    [Fact]
    public void CapabilityPreservesImpassableGoalEndpointAndTerrainRules()
    {
        var board = Corridor().Physical.Board;
        board.Terrain.Add(new(new(2, 0), TerrainKind.Water));
        var capability = UnitCapability.ApproachThroughClosedDoors;
        Assert.Equal(2, ApproachRules.Distance(board, new(0, 0), new(2, 0), capability));
        Assert.Null(ApproachRules.Distance(board, new(0, 0), new(3, 0), capability));
        Assert.DoesNotContain(new Cell(3, 0), ApproachRules.Distances(board, new(0, 0), new(2, 0), capability).Keys);
    }
}
