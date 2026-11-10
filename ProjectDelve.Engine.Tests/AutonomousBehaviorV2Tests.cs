using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AutonomousBehaviorV2Tests
{
    private static GameState State(Cell? actor = null, int width = 12, int height = 5, int movement = 2) => new()
    {
        Physical = new(new Board(width, height, []), [new("actor", actor ?? new(4, 2))]),
        Types = [new("actor-type", movement, 1, 1, 0, 1,
            Behaviors: UnitBehavior.FollowSummoner | UnitBehavior.GoToFriendly), new("friend-type", 0, 1, 1, 0, 1)],
        Units = [new("actor", "actor-type", "red", 1) { SummonerUnitId = "master" }]
    };

    private static void Add(GameState state, string id, Cell position, string side = "red", string? group = null)
    {
        state.Units.Add(new(id, "friend-type", side, 1, group));
        state.Physical.Figures.Add(new(id, position));
    }

    private static DecisionRequest Moves(GameState state) => new(DecisionKind.Move, "actor-type", "actor",
        MovementRules.FindPaths(state, "actor", state.Physical.Figures[0].Position, state.Types[0].Mov)
            .Where(p => p.Value.Count > 1).Select(p => new Candidate($"{p.Key.X},{p.Key.Y}", p.Key, p.Value.ToList())).ToList(), true);

    private static string? Choose(GameState state) => new DefaultAutomatedProvider().Choose(Moves(state), new GameplayQueries(state));

    [Fact]
    public void SummonerWinsOverNearerFriendlyAndUnrelatedGroups()
    {
        var state = State();
        Add(state, "master", new(10, 2), group: "source");
        Add(state, "nearer", new(1, 2), group: "actor-party");
        Assert.Equal("6,2", Choose(state));
        state.Units[0] = state.Units[0] with { GroupId = "actor-party" };
        Assert.Equal("6,2", Choose(state));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("dead")]
    [InlineData("hostile")]
    [InlineData("unplaced")]
    [InlineData("unreachable")]
    public void InvalidSummonerIsInapplicableAndEnablesNearestFriendlyFallback(string condition)
    {
        var state = State(new(4, 0), height: 1);
        state.Types[0] = state.Types[0] with { Actions = UnitAction.None };
        Add(state, "fallback", new(0, 0));
        if (condition != "missing") Add(state, "master", new(10, 0), condition == "hostile" ? "blue" : "red");
        if (condition == "dead") state.Units[^1] = state.Units[^1] with { CurrentHp = 0 };
        if (condition is "dead" or "unplaced") state.Physical.Figures.RemoveAll(f => f.Id == "master");
        if (condition == "unreachable") state.Physical.Board.Edges.Add(new(new(7, 0), new(8, 0), EdgeKind.Wall));
        Assert.Equal(MovementPolicyOutcome.Inapplicable,
            AutonomousMovementBehavior.FollowSummoner(Moves(state), new GameplayQueries(state), false).Outcome);
        Assert.Equal("2,0", Choose(state));
    }

    [Fact]
    public void AdjacentSummonerChoosesStayWithoutSeekingAnotherFriendly()
    {
        var state = State(); Add(state, "master", new(5, 2)); Add(state, "other", new(0, 2));
        Assert.Equal(MovementPolicyOutcome.Stay,
            AutonomousMovementBehavior.FollowSummoner(Moves(state), new GameplayQueries(state), false).Outcome);
        Assert.Null(Choose(state));
    }

    [Fact]
    public void TemporaryCongestionIsStayAndDoesNotEnableFallback()
    {
        var state = State(new(2, 0), width: 7, height: 1, movement: 1);
        Add(state, "master", new(5, 0)); Add(state, "blocker", new(3, 0)); Add(state, "fallback", new(0, 0));
        Assert.Equal(MovementPolicyOutcome.Stay,
            AutonomousMovementBehavior.FollowSummoner(Moves(state), new GameplayQueries(state), false).Outcome);
        Assert.Null(Choose(state));
    }

    [Fact]
    public void NearestFriendlyTargetTieUsesBoardOrderRegardlessOfUnitInsertion()
    {
        var state = State(); Add(state, "right", new(7, 2)); Add(state, "left", new(1, 2));
        Assert.Equal("2,2", Choose(state));
        state.Units.Reverse(); state.Physical.Figures.Reverse();
        var request = MovesForActor(state);
        Assert.Equal("2,2", new DefaultAutomatedProvider().Choose(request, new GameplayQueries(state)));
    }

    private static DecisionRequest MovesForActor(GameState state)
    {
        var copy = state.Copy();
        copy.Physical.Figures.Sort((a, b) => a.Id == "actor" ? -1 : b.Id == "actor" ? 1 : 0);
        return Moves(copy);
    }

    [Fact]
    public void OptimizedFollowImprovesAuraWhileRemainingAdjacentToSummonerAndThenStaysOnTie()
    {
        var state = State(new(2, 2));
        state.Types[0] = state.Types[0] with { Behaviors = state.Types[0].Behaviors | UnitBehavior.OptimizeFollowAdjacency };
        Add(state, "master", new(3, 2)); Add(state, "other", new(4, 0));
        Assert.Equal("3,1", Choose(state));
        state.Physical.Figures[0] = new("actor", new(3, 1));
        Assert.Null(Choose(state));
    }

    [Fact]
    public void AuraCountsLargeFriendlyOnceAndExcludesSelfAndHostiles()
    {
        var state = State(new(2, 2));
        state.Types[1] = state.Types[1] with { Footprint = Footprint.TwoByTwo };
        Add(state, "master", new(3, 1)); Add(state, "enemy", new(0, 1), "blue");
        Assert.Equal(1, new GameplayQueries(state).CountAdjacentFriendliesFrom("actor", new(2, 2)));
    }

    [Fact]
    public void ReachableAttackApproachAndCongestionTakePriorityOverSummoner()
    {
        var state = State(new(4, 0), height: 1);
        Add(state, "master", new(0, 0)); Add(state, "enemy", new(10, 0), "blue");
        Assert.Equal("6,0", Choose(state));
        state.Types[0] = state.Types[0] with { Mov = 1 };
        Add(state, "blocker", new(5, 0));
        Assert.Null(Choose(state));
    }

    [Fact]
    public void StandingOrderStillOverridesSummonerPreference()
    {
        var state = State(); Add(state, "master", new(10, 2));
        state.Orders.Add(new(new("actor-type", "red"), OrderKind.MoveTo, new(0, 2)));
        Assert.Equal("2,2", new OrderAwareAutomatedProvider().Choose(Moves(state), new GameplayQueries(state)));
    }

    private sealed class Misses : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }

    private sealed class Pick(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    [Fact]
    public void RealRaptorAttackKeepsOneStepRetreatAheadOfSummonerAndOrdinaryOrder()
    {
        var raptor = UnitRoster.Raptor();
        var state = new GameState
        {
            Physical = new(new Board(12, 5, []), [new("raptor", new(3, 2)), new("enemy", new(3, 1)), new("master", new(10, 2))]),
            Types = [raptor, new("other", 0, 1, 1, 0, 3)],
            Units = [new("raptor", raptor.Id, "red", 1) { SummonerUnitId = "master" },
                new("enemy", "other", "blue", 3), new("master", "other", "red", 3)],
            Orders = [new(new(raptor.Id, "red"), OrderKind.MoveTo, new(6, 2))],
            Objectives = [new("red", ScenarioObjective.NoObjective), new("blue", ScenarioObjective.NoObjective)],
            Round = 1, CurrentUnitId = "raptor", ActiveToken = new(raptor.Id, "red")
        };
        TestGame.Author(state);
        var provider = new OrderAwareAutomatedProvider();
        var choices = GameEngine.RefreshChoices(state, new Misses(), false);
        var ordinary = provider.Choose(choices.NextInput!, new GameplayQueries(choices.State));
        Assert.Equal(new Cell(6, 2), choices.NextInput!.Candidates.Single(c => c.Key == ordinary).Destination);

        state.MoveDone = true;
        choices = GameEngine.RefreshChoices(state, new Misses(), false);
        var attack = choices.NextInput!.Candidates.Single(c => c.Action == UnitAction.NormalAttack && c.TargetId == "enemy");
        var attacked = TestGame.Advance(choices.State, new Pick(attack.Key), new Misses(), false);
        Assert.Equal(3, attacked.State.Units.Single(u => u.Id == "enemy").CurrentHp);
        Assert.Equal(DecisionKind.Move, attacked.NextInput!.Kind);
        Assert.True(attacked.NextInput.IsMoveAfterAttack);
        Assert.All(attacked.NextInput.Candidates, c => Assert.Equal(1, MovementRules.Cost(c.Path!)));
        var retreat = provider.Choose(attacked.NextInput, new GameplayQueries(attacked.State));
        Assert.Equal(new Cell(3, 3), attacked.NextInput.Candidates.Single(c => c.Key == retreat).Destination);
        var moved = TestGame.Advance(attacked.State, new Pick(retreat), new Misses(), false);
        Assert.Equal(new Cell(3, 3), moved.State.Physical.Figures.Single(f => f.Id == "raptor").Position);
    }
}
