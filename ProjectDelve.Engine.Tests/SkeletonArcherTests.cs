using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SkeletonArcherTests
{
    private sealed class Random : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private static GameState State(Cell start, Cell target, int width = 8, int height = 1,
        UnitType? type = null) => new()
    {
        Physical = new(new Board(width, height, []), [new("archer", start), new("hero", target)]),
        Types = [type ?? UnitType.SkeletonArcher(), new("hero-type", 0, 0, 0, 0, 4) { Unique = true }],
        Units = [new("archer", (type ?? UnitType.SkeletonArcher()).Id, "red", 1),
            new("hero", "hero-type", "blue", 4)]
    };

    private static EngineResult Pending(GameState state)
    {
        var pending = GameEngine.StartRound(state, new Random());
        Assert.Equal(DecisionKind.Activation, pending.NextInput!.Kind);
        return pending;
    }

    private static string? Choose(GameState state)
    {
        var pending = Pending(state);
        var choice = new DefaultMonsterProvider().Choose(pending.NextInput!, new GameplayQueries(pending.State));
        if (choice is not null) Assert.Contains(pending.NextInput!.Candidates, c => c.Key == choice);
        return choice;
    }

    [Fact]
    public void ContentHasSpecifiedStatsActionAndReusableBehavior()
    {
        var type = UnitType.SkeletonArcher();
        Assert.Equal((3, 4, 3, 3, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.NormalAttack, type.Actions);
        Assert.Null(type.TryOpenDoor);
        Assert.Equal(UnitBehavior.MaximizeAttackDistance, type.Behaviors);
        Assert.Equal(type, JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type)));
    }

    [Fact]
    public void AlreadyInRangeRetreatsAndEngineLeavesLegalAttackPending()
    {
        var pending = Pending(State(new(2, 0), new(0, 0)));
        var canonical = pending.NextInput!.Candidates.Single(c => c.Key == "4,0").Path;
        var result = GameEngine.Advance(pending.State, new DefaultMonsterProvider(), new Random());
        Assert.Equal(new Cell(4, 0), result.State.Physical.Figures[0].Position);
        Assert.Equal(canonical, Assert.Single(result.Events, e => e.Kind == "MovementCompleted").Path);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal("hero", Assert.Single(result.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).TargetId);
    }

    [Theory]
    [InlineData(4, 8)] // Full RNG distance is available.
    [InlineData(3, 4)] // Board ends before full RNG distance.
    public void StaysAtMaximumAvailableAttackDistance(int start, int width)
    {
        Assert.Equal("stay", Choose(State(new(start, 0), new(0, 0), width)));
    }

    [Fact]
    public void DoesNotRetreatOutOfAllAttackPositions()
    {
        var state = State(new(3, 0), new(0, 0));
        var pending = Pending(state);
        Assert.Contains(pending.NextInput!.Candidates, c => c.Key == "6,0");
        Assert.False(new GameplayQueries(state).CanAttackHostileFrom("archer", new(6, 0)));
        Assert.Equal("4,0", Choose(state));
    }

    [Fact]
    public void MultipleHostilesUseNearestAttackableRatherThanFarthestTarget()
    {
        var state = State(new(3, 0), new(0, 0), width: 7);
        state.Types.Add(state.Types.Single(t => t.Id == "hero-type") with { Id = "other-type" });
        state.Units.Add(new("other", "other-type", "blue", 4));
        state.Physical.Figures.Add(new("other", new(6, 0)));
        var queries = new GameplayQueries(state);
        Assert.Equal(3, queries.DistanceToNearestAttackableHostileFrom("archer", new(3, 0)));
        Assert.Equal(2, queries.DistanceToNearestAttackableHostileFrom("archer", new(2, 0)));
        Assert.Equal(2, queries.DistanceToNearestAttackableHostileFrom("archer", new(4, 0)));
        Assert.Equal("stay", Choose(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeometricallyNearHostileDoesNotCountWhenLosIsBlocked(bool terrain)
    {
        var state = State(new(2, 0), new(0, 0), width: 7);
        state.Types.Add(state.Types.Single(t => t.Id == "hero-type") with { Id = "other-type" });
        state.Units.Add(new("other", "other-type", "blue", 4));
        state.Physical.Figures.Add(new("other", new(6, 0)));
        if (terrain) state.Physical.Board.Terrain.Add(new(new(1, 0), TerrainKind.Tree));
        else state.Physical.Board.Edges.Add(new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor));
        var queries = new GameplayQueries(state);
        Assert.Equal(4, queries.DistanceToNearestAttackableHostileFrom("archer", new(2, 0)));
        Assert.Equal(3, queries.DistanceToNearestAttackableHostileFrom("archer", new(3, 0)));
        Assert.Equal("stay", Choose(state));
    }

    [Fact]
    public void NoReachableAttackPositionFallsBackToNormalApproach()
    {
        var state = State(new(0, 0), new(9, 0), width: 10);
        var pending = Pending(state);
        var queries = new GameplayQueries(state);
        Assert.Null(queries.DistanceToNearestAttackableHostileFrom("archer", new(0, 0)));
        Assert.All(pending.NextInput!.Candidates,
            c => Assert.Null(queries.DistanceToNearestAttackableHostileFrom("archer", c.Destination!)));
        Assert.Equal("3,0", Choose(state));
        Assert.Equal(new MonsterMovementProvider(new DefaultMonsterProvider()).Choose(pending.NextInput, queries),
            Choose(state));
    }

    [Fact]
    public void ActualPathLengthWinsBeforeEarlierBoardPosition()
    {
        // The wall makes (3,0) take three steps, while (4,1) takes one.
        var state = State(new(3, 1), new(0, 1), width: 6, height: 3);
        state.Physical.Board.Edges.Add(new(new(3, 1), new(3, 0), EdgeKind.Wall));
        var pending = Pending(state);
        Assert.Equal(4, pending.NextInput!.Candidates.Single(c => c.Key == "3,0").Path!.Count);
        Assert.Equal("4,1", Choose(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualDistanceAndPathLengthUseYThenXRegardlessOfCandidateOrder(bool reverse)
    {
        var state = State(new(2, 2), new(2, 1), width: 5, height: 6);
        var pending = Pending(state);
        if (reverse) pending.NextInput!.Candidates.Reverse();
        // (0,3), (4,3), (1,4), (3,4), (2,5) all have distance four and path length three.
        Assert.Equal("0,3", new DefaultMonsterProvider().Choose(pending.NextInput!, new GameplayQueries(state)));
    }

    [Fact]
    public void BehaviorIsReusableAndDoesNotChangeLegalChoicesOrOrdinaryMovement()
    {
        var ordinary = new UnitType("unrelated-type", 3, 4, 3, 3, 1);
        var normal = State(new(2, 0), new(0, 0), type: ordinary);
        var keepAway = State(new(2, 0), new(0, 0),
            type: ordinary with { Behaviors = UnitBehavior.MaximizeAttackDistance });
        Assert.Equal(JsonSerializer.Serialize(Pending(normal).NextInput),
            JsonSerializer.Serialize(Pending(keepAway).NextInput));
        Assert.Equal("stay", Choose(normal));
        Assert.Equal("4,0", Choose(keepAway));
        var pending = Pending(keepAway);
        // A different provider can use the ordinary movement preference on the same content.
        Assert.Equal("stay", new MonsterMovementProvider(new DefaultMonsterProvider())
            .Choose(pending.NextInput!, new GameplayQueries(keepAway)));
    }

    [Fact]
    public void StayingIsExcludedWhenDecisionDoesNotPermitIt()
    {
        var state = State(new(4, 0), new(0, 0), width: 5);
        var pending = Pending(state).NextInput!;
        var request = pending with { Kind = DecisionKind.Move, AllowsNone = false,
            Candidates = pending.Candidates.Where(c => c.Kind == ActivationChoiceKind.Move).ToList() };
        Assert.Equal("3,0", new DefaultMonsterProvider().Choose(request, new GameplayQueries(state)));
    }
}
