using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ZombieTests
{
    private sealed class Random(int roll = 6) : IRandomProvider
    {
        public int Rolls { get; private set; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
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
        Types = [type ?? UnitType.Zombie(), new("hero-type", 0, 0, 0, 0, 4) { Unique = true }],
        Units = [new("monster", (type ?? UnitType.Zombie()).Id, "red", 1), new("hero", "hero-type", "blue", 4)]
    };

    [Fact]
    public void ZombieStatsAndBehavior_DoNotChangeActualMovementOrLos()
    {
        var state = Corridor();
        var type = state.Types[0];
        Assert.Equal((2, 1, 3, 3, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(new TryOpenDoor(2), type.TryOpenDoor);
        Assert.Equal(UnitBehavior.ApproachThroughClosedDoors, type.Behaviors);
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("monster", new(0, 0)));
        Assert.Equal(3, new GameplayQueries(state).DistanceToAttackPositionFrom("monster", new(0, 0),
            closedDoorsTraversable: true));
        Assert.False(new GameplayQueries(state).CanAttackHostileFrom("monster", new(1, 0)));
        var pending = TestGame.StartRound(state, new Random());
        Assert.Equal(new Cell(1, 0), Assert.Single(pending.NextInput!.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).Destination);
        Assert.DoesNotContain(MovementRules.FindPaths(state, "monster", new(0, 0)).Keys, c => c.X >= 2);
        Assert.Throws<ArgumentException>(() => TestGame.Advance(pending.State, new Choice(_ => "2,0"), new Random()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApproachQueryUsesCallerParametersRegardlessOfIdentityOrBehavior(bool hasBehavior)
    {
        var state = Corridor(new("unrelated-type", 2, 1, 3, 3, 1,
            Behaviors: hasBehavior ? UnitBehavior.ApproachThroughClosedDoors : UnitBehavior.None));
        var queries = new GameplayQueries(state);
        Assert.Null(queries.DistanceToAttackPositionFrom("monster", new(0, 0)));
        Assert.Equal(3, queries.DistanceToAttackPositionFrom("monster", new(0, 0), closedDoorsTraversable: true));
        Assert.Null(queries.DistanceToAttackPositionFrom("monster", new(0, 0), closedDoorsTraversable: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultProviderInterpretsReusableBehaviorWithoutChangingLegalCandidates(bool hasBehavior)
    {
        var state = Corridor(new("unrelated-type", 2, 1, 3, 3, 1,
            Behaviors: hasBehavior ? UnitBehavior.ApproachThroughClosedDoors : UnitBehavior.None));
        var pending = TestGame.StartRound(state, new Random());
        Assert.Equal(new Cell(1, 0), Assert.Single(pending.NextInput!.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).Destination);
        var queries = new GameplayQueries(pending.State);
        Assert.Equal(hasBehavior ? "1,0" : "stay", new DefaultAutomatedProvider().Choose(pending.NextInput, queries));
        // Another automated provider can use ordinary approach analysis instead.
        Assert.Equal("stay", new ApproachMovementProvider(new Choice(_ => null)).Choose(pending.NextInput, queries));
    }

    [Fact]
    public void HumanControlledZombieCanIgnoreBehaviorAndStillUseDoorAction()
    {
        var state = Corridor();
        var random = new Random(1);
        var pending = TestGame.StartRound(state, random);
        var stayed = TestGame.Advance(pending.State, new Choice(_ => "stay"), random);
        Assert.Equal(new Cell(0, 0), stayed.State.Physical.Figures[0].Position);
        Assert.True(stayed.State.RoundComplete);
        Assert.Equal(0, random.Rolls);

        var moved = TestGame.Advance(pending.State, new Choice(_ => "1,0"), random);
        var attempted = TestGame.Advance(moved.State,
            new Choice(request => request.Candidates.Single(c => c.TryOpenDoor is not null).Key), random);
        Assert.Equal(1, random.Rolls);
        Assert.Single(TestGame.OperationEvents(attempted), e => e.Kind == "DoorOpened");
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
            TryOpenDoor: new(count), Behaviors: UnitBehavior.ApproachThroughClosedDoors));
        var random = new Random(roll);
        var pending = TestGame.StartRound(state, random);
        var moved = TestGame.Advance(pending.State, new DefaultAutomatedProvider(), random);
        Assert.Equal(new Cell(1, 0), moved.State.Physical.Figures[0].Position);
        Assert.NotNull(Assert.Single(moved.NextInput!.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).TryOpenDoor);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(moved.State))!;
        var result = TestGame.Advance(restored, new DefaultAutomatedProvider(), random);
        Assert.True(result.State.RoundComplete);
        Assert.Null(result.NextInput);
        Assert.Equal(1, random.Rolls);
        Assert.Equal(succeeds ? EdgeKind.OpenDoor : EdgeKind.ClosedDoor, result.State.Physical.Board.Edges[0].Kind);
        var attempt = Assert.Single(TestGame.OperationEvents(result), e => e.Kind == "DoorOpeningAttemptResolved");
        Assert.Equal(succeeds, attempt.Succeeded);
        Assert.Equal(roll, attempt.DieRoll);
        Assert.Equal(count, attempt.SuccessCount);
        Assert.Equal(succeeds, TestGame.OperationEvents(result).Any(e => e.Kind == "DoorOpened"));
        Assert.Equal(EdgeKind.ClosedDoor, restored.Physical.Board.Edges[0].Kind);
        Assert.Throws<InvalidOperationException>(() => TestGame.Advance(result.State, new DefaultAutomatedProvider(), random));
    }

    [Fact]
    public void ZombieAdjacentToClosedDoorStaysThenTriesToOpenIt()
    {
        var state = Corridor();
        var current = new Cell(1, 0);
        state.Physical.Figures[0] = new("monster", current);
        var random = new Random(1);
        var provider = new DefaultAutomatedProvider();
        var pending = TestGame.StartRound(state, random);
        Assert.Equal(DecisionKind.Activation, pending.NextInput!.Kind);
        var candidate = Assert.Single(pending.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn)));
        Assert.Equal(new Cell(0, 0), candidate.Destination);
        var queries = new GameplayQueries(pending.State);
        Assert.False(queries.CanAttackHostileFrom("monster", current));
        Assert.Equal(2, queries.DistanceToAttackPositionFrom("monster", current, closedDoorsTraversable: true));
        Assert.Equal(3, queries.DistanceToAttackPositionFrom("monster", candidate.Destination!, closedDoorsTraversable: true));

        var stayed = TestGame.Advance(pending.State, provider, random);
        Assert.Equal(current, stayed.State.Physical.Figures[0].Position);
        Assert.Equal(DecisionKind.Activation, stayed.NextInput!.Kind);
        Assert.NotNull(Assert.Single(stayed.NextInput.Candidates.Where(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn))).TryOpenDoor);
        Assert.Equal(0, random.Rolls);

        var acted = TestGame.Advance(stayed.State, provider, random);
        Assert.True(acted.State.RoundComplete);
        Assert.Equal(current, acted.State.Physical.Figures[0].Position);
        Assert.Equal(1, random.Rolls);
        Assert.Single(TestGame.OperationEvents(acted), e => e.Kind == "DoorOpeningAttemptResolved");
        Assert.Single(TestGame.OperationEvents(acted), e => e.Kind == "DoorOpened");
        Assert.Equal(EdgeKind.OpenDoor, acted.State.Physical.Board.Edges[0].Kind);
    }

    [Fact]
    public void AttackHasPriorityOverDoorAttempt()
    {
        var state = Corridor();
        state.Physical.Figures[0] = new("monster", new(1, 0));
        state.Physical.Figures[1] = new("hero", new(0, 0));
        var random = new Random();
        var pending = TestGame.StartRound(state, random);
        // No legal movement exists; the zero-step Move completes automatically.
        var stayed = pending;
        Assert.Contains(stayed.NextInput!.Candidates, c => c.TryOpenDoor is not null);
        var result = TestGame.Advance(stayed.State, new DefaultAutomatedProvider(), random);
        Assert.Single(TestGame.OperationEvents(result), e => e.Kind == "AttackResolved");
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
        var pending = TestGame.StartRound(state, random);
        Assert.Equal(4, pending.NextInput!.Candidates.Count(c => c.Kind is not (ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn)));
        var result = TestGame.Advance(pending.State, new DefaultAutomatedProvider(), random);
        var opened = Assert.Single(TestGame.OperationEvents(result), e => e.Kind == "DoorOpened").Door!;
        Assert.True(opened.A == new Cell(1, 0) || opened.B == new Cell(1, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void InvalidRandomFacesAreRejected(int roll)
    {
        var random = new Random(roll);
        var pending = TestGame.StartRound(Corridor(), random);
        var moved = TestGame.Advance(pending.State, new DefaultAutomatedProvider(), random);
        Assert.Throws<ArgumentException>(() => TestGame.Advance(moved.State, new DefaultAutomatedProvider(), random));
        Assert.Equal(EdgeKind.ClosedDoor, moved.State.Physical.Board.Edges[0].Kind);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void InvalidSuccessCountsAreRejected(int count)
    {
        Assert.Throws<ArgumentException>(() => TestGame.StartRound(
            Corridor(UnitType.Zombie() with { TryOpenDoor = new(count) }), new Random()));
    }

    [Theory]
    [InlineData(EdgeKind.Wall)]
    [InlineData(EdgeKind.WallWithWindow)]
    public void ClosedDoorAnalysisDoesNotCrossOtherImpassableEdges(EdgeKind edge)
    {
        var state = Corridor();
        state.Physical.Board.Edges[0] = state.Physical.Board.Edges[0] with { Kind = edge };
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("monster", new(0, 0), closedDoorsTraversable: true));
    }

    [Fact]
    public void ClosedDoorAnalysisPreservesImpassableGoalEndpointAndTerrainRules()
    {
        var board = Corridor().Physical.Board;
        board.Terrain.Add(new(new(2, 0), TerrainKind.Water));
        Assert.Equal(2, ApproachRules.Distance(board, new(0, 0), new(2, 0), closedDoorsTraversable: true));
        Assert.Null(ApproachRules.Distance(board, new(0, 0), new(3, 0), closedDoorsTraversable: true));
        Assert.DoesNotContain(new Cell(3, 0), ApproachRules.Distances(board, new(0, 0), new(2, 0),
            closedDoorsTraversable: true).Keys);
    }
}
