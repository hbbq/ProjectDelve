using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MonsterMovementTests
{
    private sealed class Random : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException("Unexpected attack roll.");
        public int RollD6() => throw new InvalidOperationException("Unexpected D6 roll.");
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException("Unexpected defence roll.");
    }

    private sealed class OtherDecisions : IDecisionProvider
    {
        public int Calls { get; private set; }
        public DecisionRequest? Request { get; private set; }
        public IGameplayQueries? Queries { get; private set; }
        public string? Choose(DecisionRequest request, IGameplayQueries queries)
        {
            Calls++;
            Request = request;
            Queries = queries;
            return request.Candidates[0].Key;
        }
    }

    private static GameState State(Cell start, Cell target, int mov = 2, int width = 7, int height = 5) => new()
    {
        Physical = new PhysicalState(new Board(width, height, []),
            [new Figure("monster", start), new Figure("hostile", target)]),
        Types = [new UnitType("monster-type", mov, 1, 1, 0, 1), new UnitType("hostile-type", 0, 0, 0, 0, 1)],
        Units = [new Unit("monster", "monster-type", "red", 1), new Unit("hostile", "hostile-type", "blue", 1)]
    };

    private static EngineResult PendingMove(GameState state)
    {
        var result = TestGame.StartRound(state, new Random());
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        return result;
    }

    private static string? ChooseMove(GameState state)
    {
        var pending = PendingMove(state);
        var other = new OtherDecisions();
        var choice = new ApproachMovementProvider(other)
            .Choose(pending.NextInput!, new GameplayQueries(pending.State));
        Assert.Equal(0, other.Calls);
        if (choice is not null)
            Assert.Contains(pending.NextInput!.Candidates, c => c.Key == choice);
        return choice;
    }

    [Fact]
    public void AlreadyAbleToAttackStaysAndDoesNotDelegateMovement()
    {
        Assert.Equal("stay", ChooseMove(State(new Cell(1, 1), new Cell(2, 2))));
    }

    [Fact]
    public void AttackDestinationUsesShortestMovementBeforeTopLeftBoardOrder()
    {
        // (2,0) is earlier in board order but takes 3 steps; (2,1) takes 2.
        Assert.Equal("2,1", ChooseMove(State(new Cell(0, 1), new Cell(3, 1), mov: 3)));
    }

    [Fact]
    public void EqualLengthAttackDestinationsUseTopLeftBoardOrder()
    {
        var state = State(new Cell(2, 2), new Cell(2, 0));
        // Occupied middle cell can be traversed, but cannot be chosen.
        state.Units.Add(new Unit("friend", "hostile-type", "red", 1));
        state.Physical.Figures.Add(new Figure("friend", new Cell(2, 1)));
        // (1,1) and (3,1) tie and are resolved by ascending X.
        Assert.Equal("1,1", ChooseMove(state));
    }

    [Fact]
    public void RemainingDistanceFollowsTraversableRouteRatherThanManhattanDistance()
    {
        var state = State(new Cell(1, 2), new Cell(4, 2), mov: 1, width: 6, height: 5);
        // Vertical barrier with its only passage at the top. Moving right
        // reduces Manhattan distance, but moving up shortens the actual route.
        for (var y = 1; y < 5; y++)
            state.Physical.Board.Edges.Add(new Edge(new Cell(2, y), new Cell(3, y), EdgeKind.Wall));
        // The cell to the right is a dead end, so moving there requires
        // retracing a step before approaching the passage.
        state.Physical.Board.Edges.Add(new Edge(new Cell(2, 1), new Cell(2, 2), EdgeKind.Wall));
        state.Physical.Board.Edges.Add(new Edge(new Cell(2, 2), new Cell(2, 3), EdgeKind.Wall));

        var queries = new GameplayQueries(state);
        Assert.Equal(4, queries.DistanceToAttackPositionFrom("monster", new Cell(1, 1)));
        Assert.Equal(6, queries.DistanceToAttackPositionFrom("monster", new Cell(2, 2)));
        Assert.Equal("1,1", ChooseMove(state));
    }

    [Fact]
    public void ClosedRoomWithoutReachableAttackPositionStays()
    {
        var state = State(new Cell(0, 1), new Cell(4, 1), width: 5, height: 3);
        for (var y = 0; y < 3; y++)
            state.Physical.Board.Edges.Add(new Edge(new Cell(1, y), new Cell(2, y), EdgeKind.ClosedDoor));
        Assert.Null(new GameplayQueries(state).DistanceToAttackPositionFrom("monster", new Cell(0, 1)));
        Assert.Equal("stay", ChooseMove(state));
    }

    [Fact]
    public void AdvanceAppliesOnlyChosenCanonicalMovementAndLeavesAttackPending()
    {
        var pending = PendingMove(State(new Cell(0, 1), new Cell(3, 1), mov: 3));
        var original = JsonSerializer.Serialize(pending.State);
        var canonical = pending.NextInput!.Candidates.Single(c => c.Key == "2,1").Path!.ToArray();
        var other = new OtherDecisions();

        var result = TestGame.Advance(pending.State, new ApproachMovementProvider(other), new Random());

        Assert.Equal(canonical, Assert.Single(TestGame.OperationEvents(result), e => e.Kind == "MovementCompleted").Path);
        Assert.Equal(new Cell(2, 1), result.State.Physical.Figures.Single(f => f.Id == "monster").Position);
        Assert.Equal(DecisionKind.Activation, result.NextInput!.Kind);
        Assert.Equal(0, other.Calls);
        Assert.Equal(original, JsonSerializer.Serialize(pending.State));
    }

    [Theory]
    [InlineData(DecisionKind.SelectUnit)]
    [InlineData(DecisionKind.Act)]
    public void NonMovementDecisionsAreDelegatedUnchanged(DecisionKind kind)
    {
        var request = new DecisionRequest(kind, "monster-type", "monster", [new Candidate("selected")], true);
        var queries = new GameplayQueries(State(new Cell(0, 1), new Cell(3, 1)));
        var other = new OtherDecisions();

        Assert.Equal("selected", new ApproachMovementProvider(other).Choose(request, queries));
        Assert.Equal(1, other.Calls);
        Assert.Same(request, other.Request);
        Assert.Same(queries, other.Queries);
    }

    // Supply query answers directly to isolate ranking priorities from geometry.
    private sealed class RankingQueries : IGameplayQueries
    {
        public Cell Current { get; init; } = new(0, 0);
        public HashSet<Cell> AttackPositions { get; } = [];
        public Dictionary<Cell, int?> Distances { get; } = [];
        public Cell PositionOf(string unitId) => Current;
        public IReadOnlyList<Cell> OccupiedCellsOf(string unitId) => [Current];
        public UnitBehavior BehaviorsOf(string unitId) => UnitBehavior.None;
        public int? DistanceToNearestHostileFrom(string unitId, Cell position, bool closedDoorsTraversable = false) =>
            throw new InvalidOperationException("Ordinary approach does not flee.");
        public int ManhattanDistanceBetweenUnits(string firstUnitId, string secondUnitId) =>
            throw new InvalidOperationException("Movement ranking does not use Manhattan distance.");
        public bool CanAttackHostileFrom(string unitId, Cell position) => AttackPositions.Contains(position);
        public bool HasNearbyHostileThreatFrom(string unitId, Cell position) =>
            throw new InvalidOperationException("Ordinary movement does not evaluate nearby threats.");
        public int? DistanceToNearestAttackableHostileFrom(string unitId, Cell position) =>
            throw new InvalidOperationException("Ordinary movement does not maximize attack distance.");
        public int? DistanceToAttackPositionFrom(string unitId, Cell position, bool closedDoorsTraversable = false) =>
            Distances.GetValueOrDefault(position);
    }

    private static Candidate Choice(Cell destination, int steps) =>
        new($"{destination.X},{destination.Y}", destination,
            Enumerable.Repeat(new Cell(0, 0), steps).Append(destination).ToList());

    [Theory]
    [InlineData(3, true)] // Staying has a shorter remaining distance than every move.
    [InlineData(2, true)] // Equal distance: staying wins with movement length zero.
    [InlineData(1, false)] // A genuine improvement wins despite requiring movement.
    public void ApproachRankingIncludesStayingWhenAllowed(int candidateDistance, bool stays)
    {
        var queries = new RankingQueries { Current = new Cell(3, 3) };
        var candidates = new[] { Choice(new Cell(1, 0), 1), Choice(new Cell(2, 0), 2) };
        queries.Distances[queries.Current] = 2;
        foreach (var candidate in candidates)
            queries.Distances[candidate.Destination!] = candidateDistance;
        var request = new DecisionRequest(DecisionKind.Move, "type", "unit", candidates.ToList(), true);

        Assert.Equal(stays ? null : candidates[0].Key, new DefaultAutomatedProvider().Choose(request, queries));
    }

    [Fact]
    public void ApproachRankingExcludesStayingWhenNotAllowed()
    {
        var queries = new RankingQueries();
        var candidate = Choice(new Cell(1, 0), 1);
        queries.Distances[queries.Current] = 1;
        queries.Distances[candidate.Destination!] = 2;
        var request = new DecisionRequest(DecisionKind.Move, "type", "unit", [candidate], false);

        Assert.Equal(candidate.Key, new DefaultAutomatedProvider().Choose(request, queries));
    }

    [Fact]
    public void RemainingDistanceRanksBeforeMovementLengthAndTopLeftBoardOrder()
    {
        var queries = new RankingQueries();
        var near = Choice(new Cell(3, 3), 3);
        var far = Choice(new Cell(1, 0), 1);
        queries.Distances[near.Destination!] = 1;
        queries.Distances[far.Destination!] = 2;
        var request = new DecisionRequest(DecisionKind.Move, "type", "unit", [far, near], true);

        Assert.Equal(near.Key, new ApproachMovementProvider(new OtherDecisions()).Choose(request, queries));
    }

    [Fact]
    public void EqualRemainingDistanceRanksMovementLengthBeforeTopLeftBoardOrder()
    {
        var queries = new RankingQueries();
        var shortMove = Choice(new Cell(3, 3), 1);
        var longMove = Choice(new Cell(1, 0), 2);
        queries.Distances[shortMove.Destination!] = 3;
        queries.Distances[longMove.Destination!] = 3;
        var request = new DecisionRequest(DecisionKind.Move, "type", "unit", [longMove, shortMove], true);

        Assert.Equal(shortMove.Key, new ApproachMovementProvider(new OtherDecisions()).Choose(request, queries));
    }

    [Fact]
    public void EqualRemainingAndMovementDistancesUseTopLeftBoardOrder()
    {
        var queries = new RankingQueries();
        var candidates = new[] { Choice(new Cell(0, 3), 2), Choice(new Cell(3, 1), 2), Choice(new Cell(1, 1), 2) };
        foreach (var candidate in candidates) queries.Distances[candidate.Destination!] = 3;
        var request = new DecisionRequest(DecisionKind.Move, "type", "unit", candidates.ToList(), true);

        Assert.Equal("1,1", new ApproachMovementProvider(new OtherDecisions()).Choose(request, queries));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoReachableAttackPositionStaysOnlyWhenAllowed(bool allowsNone)
    {
        var queries = new RankingQueries();
        var request = new DecisionRequest(DecisionKind.Move, "type", "unit", [Choice(new Cell(1, 0), 1)], allowsNone);
        var provider = new ApproachMovementProvider(new OtherDecisions());

        if (allowsNone) Assert.Null(provider.Choose(request, queries));
        else Assert.Contains("staying is not allowed", Assert.Throws<InvalidOperationException>(
            () => provider.Choose(request, queries)).Message);
    }
}
