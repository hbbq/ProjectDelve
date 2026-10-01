using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DefaultMonsterProviderTests
{
    private sealed class Random : IRandomProvider
    {
        public string DrawToken(IReadOnlyList<string> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public int RollD6() => throw new InvalidOperationException("Unexpected D6 roll.");
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(int rng = 5, int mov = 0) => new()
    {
        Physical = new PhysicalState(new Board(7, 7, []), [new Figure("monster", new Cell(3, 3))]),
        Types = [new UnitType("monster-type", mov, rng, 1, 0, 1), new UnitType("other", 0, 0, 0, 0, 1)],
        Units = [new Unit("monster", "monster-type", "red", 1)]
    };

    private static void AddUnit(GameState state, string id, Cell position,
        string side = "blue", string type = "other")
    {
        state.Units.Add(new Unit(id, type, side, 1));
        state.Physical.Figures.Add(new Figure(id, position));
    }

    private static DecisionRequest PendingAttack(GameState state)
    {
        var pending = GameEngine.StartRound(state, new Random());
        Assert.Equal(DecisionKind.Act, pending.NextInput!.Kind);
        return pending.NextInput;
    }

    [Fact]
    public void SelectUnitRanksOnlySuppliedCandidatesInTopLeftBoardOrder()
    {
        var state = State();
        AddUnit(state, "top-right", new Cell(4, 1), "red", "monster-type");
        AddUnit(state, "top-left", new Cell(2, 1), "red", "monster-type");
        AddUnit(state, "lower-left", new Cell(0, 2), "red", "monster-type");
        AddUnit(state, "not-eligible", new Cell(0, 0));
        var request = new DecisionRequest(DecisionKind.SelectUnit, "monster-type", null,
            [new Candidate("monster"), new Candidate("lower-left"),
                new Candidate("top-right"), new Candidate("top-left")], false);

        Assert.Equal("top-left", new DefaultMonsterProvider().Choose(request, new GameplayQueries(state)));
    }

    [Fact]
    public void SelectUnitUsesCurrentPositionsAfterEarlierUnitHasMoved()
    {
        var state = State(mov: 2);
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new Cell(0, 0) };
        AddUnit(state, "second", new Cell(2, 1), "red", "monster-type");
        AddUnit(state, "third", new Cell(3, 1), "red", "monster-type");
        AddUnit(state, "hostile", new Cell(2, 2));
        var provider = new DefaultMonsterProvider();
        var random = new Random();
        var result = GameEngine.StartRound(state, random);
        Assert.Equal("monster", provider.Choose(result.NextInput!, new GameplayQueries(result.State)));

        // Finish the group's Bonus Action selections and reach monster's Move.
        while (result.NextInput!.Kind == DecisionKind.SelectUnit)
            result = GameEngine.Advance(result.State, provider, random);
        Assert.Equal("monster", result.NextInput.UnitId);
        result = GameEngine.Advance(result.State, new Choice("0,2"), random);
        Assert.Equal(new Cell(0, 2), result.State.Physical.Figures.Single(f => f.Id == "monster").Position);
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Key == "monster");

        // The remaining Units finish Move without moving.
        result = GameEngine.Advance(result.State, provider, random);
        Assert.Equal("second", result.NextInput!.UnitId);
        result = GameEngine.Advance(result.State, new Choice(null), random);
        Assert.Equal("third", result.NextInput!.UnitId);
        result = GameEngine.Advance(result.State, new Choice(null), random);

        // In Act all three are eligible again. The moved monster now sorts last.
        Assert.Equal(Phase.Act, result.State.Phase);
        Assert.Equal(DecisionKind.SelectUnit, result.NextInput!.Kind);
        Assert.Contains(result.NextInput.Candidates, c => c.Key == "monster");
        Assert.Equal("second", provider.Choose(result.NextInput, new GameplayQueries(result.State)));
        result = GameEngine.Advance(result.State, provider, random);
        Assert.Equal(DecisionKind.Act, result.NextInput!.Kind);
        Assert.Equal("second", result.NextInput.UnitId);
    }

    [Fact]
    public void AttackChoosesNearestLegalTargetBeforeTopLeftBoardOrder()
    {
        var state = State();
        AddUnit(state, "far", new Cell(0, 3));
        AddUnit(state, "near", new Cell(3, 5));
        AddUnit(state, "friendly", new Cell(3, 2), "red");
        var request = PendingAttack(state);
        Assert.Equal(2, request.Candidates.Count);
        Assert.DoesNotContain(request.Candidates, c => c.TargetId == "friendly");

        Assert.Equal("attack:near", new DefaultMonsterProvider().Choose(request, new GameplayQueries(state)));
    }

    [Theory]
    [InlineData(3, 1, 1, 3)] // Ascending Y first.
    [InlineData(1, 3, 5, 3)] // Then ascending X within the same row.
    public void EqualDistanceTargetsUseTopLeftBoardOrder(int firstX, int firstY, int otherX, int otherY)
    {
        var state = State();
        AddUnit(state, "other", new Cell(otherX, otherY));
        AddUnit(state, "first", new Cell(firstX, firstY));
        var request = PendingAttack(state);
        Assert.Equal(2, request.Candidates.Count);

        Assert.Equal("attack:first", new DefaultMonsterProvider().Choose(request, new GameplayQueries(state)));
    }

    [Fact]
    public void MeleePrefersOrthogonalTargetOverEarlierDiagonalTargetByManhattanDistance()
    {
        var state = State(rng: 1);
        AddUnit(state, "diagonal", new Cell(2, 2));
        AddUnit(state, "orthogonal", new Cell(4, 3));
        var request = PendingAttack(state);
        Assert.Equal(2, request.Candidates.Count);
        var queries = new GameplayQueries(state);
        Assert.Equal(2, queries.ManhattanDistanceBetweenUnits("monster", "diagonal"));
        Assert.Equal(1, queries.ManhattanDistanceBetweenUnits("monster", "orthogonal"));

        Assert.Equal("attack:orthogonal", new DefaultMonsterProvider().Choose(request, queries));
    }

    [Fact]
    public void AttackDoesNotAddAnOmittedCloserTarget()
    {
        var state = State();
        AddUnit(state, "omitted", new Cell(3, 2));
        AddUnit(state, "supplied", new Cell(0, 3));
        var request = new DecisionRequest(DecisionKind.Act, "monster-type", "monster",
            [new Candidate("attack:supplied", Action: UnitAction.NormalAttack, TargetId: "supplied")], true);

        Assert.Equal("attack:supplied", new DefaultMonsterProvider().Choose(request, new GameplayQueries(state)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttackWithoutCandidatesChoosesNoneOnlyWhenAllowed(bool allowsNone)
    {
        var request = new DecisionRequest(DecisionKind.Act, "monster-type", "monster", [], allowsNone);
        var provider = new DefaultMonsterProvider();
        var queries = new GameplayQueries(State());

        if (allowsNone) Assert.Null(provider.Choose(request, queries));
        else Assert.Throws<InvalidOperationException>(() => provider.Choose(request, queries));
    }

    [Fact]
    public void DefaultProviderReusesExistingMovementChoiceAndCanonicalPath()
    {
        var state = State(rng: 1, mov: 2);
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Position = new Cell(0, 1) };
        AddUnit(state, "hostile", new Cell(3, 1));
        var pending = GameEngine.StartRound(state, new Random());
        Assert.Equal(DecisionKind.Move, pending.NextInput!.Kind);
        var queries = new GameplayQueries(pending.State);
        var original = new MonsterMovementProvider(new Choice(null)).Choose(pending.NextInput, queries);
        Assert.Equal("2,1", original);
        Assert.Equal(original, new DefaultMonsterProvider().Choose(pending.NextInput, queries));

        var result = GameEngine.Advance(pending.State, new DefaultMonsterProvider(), new Random());
        Assert.Equal(pending.NextInput.Candidates.Single(c => c.Key == original).Path,
            Assert.Single(result.Events, e => e.Kind == "MovementCompleted").Path);
        result = GameEngine.Advance(result.State, new DefaultMonsterProvider(), new Random());
        Assert.Equal("hostile", Assert.Single(result.Events, e => e.Kind == "AttackResolved").TargetId);
    }
}
