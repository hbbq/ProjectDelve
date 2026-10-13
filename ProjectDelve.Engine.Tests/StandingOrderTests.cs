using System.Text.Json;
using Xunit;
using static ProjectDelve.Engine.Scenario;

namespace ProjectDelve.Engine.Tests;

public sealed class StandingOrderTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string? key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private static GameState State(UnitType? actorType = null, Cell? actor = null, Cell? enemy = null, Board? board = null)
    {
        actorType ??= UnitType.Grunt();
        var enemyType = new UnitType("enemy", 0, 1, 1, 1, 1);
        var token = new ActivationToken(actorType.Id, "red");
        return new()
        {
            Physical = new(board ?? new Board(6, 4, []), [new("actor", actor ?? new(0, 1)), new("enemy", enemy ?? new(5, 1))]),
            Types = [actorType, enemyType],
            Units = [actorType.CreateUnit("actor", "red"), enemyType.CreateUnit("enemy", "blue")],
            Controllers = [new(token, ControllerKind.Automated), new(new(enemyType.Id, "blue"), ControllerKind.Human)],
            Objectives = [new("red"), new("blue")]
        };
    }

    private static DecisionRequest Movement(GameState state, params Candidate[] candidates) => new(
        DecisionKind.Move, state.Units[0].TypeId, "actor", [.. candidates], true)
        { Token = new(state.Units[0].TypeId, "red") };

    [Fact]
    public void StateIsSparseUniqueCopiedAndNormalRemovesAssignment()
    {
        var state = State();
        var token = state.Controllers[0].Token;
        Assert.Equal(OrderKind.Normal, state.OrderFor(token).Kind);
        state.AssignOrder(token, OrderKind.HoldPosition);
        state.AssignOrder(token, OrderKind.MoveTo, new(4, 2));
        Assert.Equal(new GroupOrder(token, OrderKind.MoveTo, new(4, 2)), Assert.Single(state.Orders));
        var detached = state.Copy();
        var copy = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state.Orders, detached.Orders);
        Assert.Equal(state.Orders, copy.Orders);
        state.AssignOrder(token, OrderKind.Normal);
        Assert.Empty(state.Orders);
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.MoveTo));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(new("missing", "red"), OrderKind.DoNothing));
        var malformed = State();
        malformed.Orders = [null!];
        Assert.Throws<ArgumentException>(() => GameEngine.StartRound(malformed, new Dice(), false));
    }

    [Fact]
    public void ScenarioOrdersValidateRoundTripTransportAndInitializeState()
    {
        var definition = Define(Map(5, 3),
            [Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(0, 0)),
             Group(UnitTypeIds.Goblin, "red", ControllerKind.Automated),
             Group(UnitTypeIds.Grunt, "blue", ControllerKind.Human, At(4, 2))],
            orders: [new(UnitTypeIds.Grunt, "red", OrderKind.MoveTo, new(3, 2)),
                new(UnitTypeIds.Goblin, "red", OrderKind.DoNothing)]);
        var json = ScenarioDefinitionJson.ToJson(definition);
        var loaded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(
            ScenarioDefinitionJson.FromJson(json)));
        var state = GameEngine.CreateGame(loaded);
        Assert.Equal(OrderKind.MoveTo, state.OrderFor(new(UnitTypeIds.Grunt, "red")).Kind);
        Assert.Equal(OrderKind.DoNothing, state.OrderFor(new(UnitTypeIds.Goblin, "red")).Kind);
        Assert.Equal(OrderKind.Normal, state.OrderFor(new(UnitTypeIds.Grunt, "blue")).Kind);
        // A later Unit of an authored empty group uses that group's standing Order.
        state.PlaceUnit(UnitTypeIds.Goblin, "summoned", "red", new(1, 1));
        Assert.Equal(OrderKind.DoNothing, new GameplayQueries(state).EffectiveOrderFor("summoned").Kind);

        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with
        { Orders = [new(UnitTypeIds.Grunt, "red", OrderKind.HoldPosition), new(UnitTypeIds.Grunt, "red", OrderKind.DoNothing)] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with
        { Orders = [new(UnitTypeIds.Grunt, "red", OrderKind.MoveTo)] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with
        { Orders = [new(UnitTypeIds.Grunt, "missing", OrderKind.DoNothing)] }));
    }

    [Fact]
    public void MissingOrderHasExactNormalBehaviorParity()
    {
        var state = State(enemy: new(2, 1));
        var request = Movement(state,
            new("1,1", new(1, 1), [new(0, 1), new(1, 1)], Kind: ActivationChoiceKind.Move),
            new("0,0", new(0, 0), [new(0, 1), new(0, 0)], Kind: ActivationChoiceKind.Move));
        var queries = new GameplayQueries(state);
        Assert.Equal(new DefaultAutomatedProvider().Choose(request, queries),
            new OrderAwareAutomatedProvider().Choose(request, queries));
    }

    [Fact]
    public void DoNothingStaysEndsDeclinesOptionalAndResolvesForcedChoices()
    {
        var state = State(enemy: new(1, 1));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.DoNothing);
        var queries = new GameplayQueries(state);
        var provider = new OrderAwareAutomatedProvider();
        var activation = new DecisionRequest(DecisionKind.Activation, state.Units[0].TypeId, "actor",
        [
            new("attack:enemy", Action: UnitAction.NormalAttack, TargetId: "enemy", Kind: ActivationChoiceKind.Action),
            new("move", new(1, 0), [new(0, 1), new(1, 1)], Kind: ActivationChoiceKind.Move),
            new("stay", Kind: ActivationChoiceKind.Stay)
        ], false);
        Assert.Equal("stay", provider.Choose(activation, queries));
        var end = activation with { Candidates = [activation.Candidates[0], new Candidate("end", Kind: ActivationChoiceKind.EndTurn)] };
        Assert.Equal("end", provider.Choose(end, queries));
        Assert.Null(provider.Choose(new(DecisionKind.Cleave, state.Units[0].TypeId, "actor", [new("cleave")], true), queries));
        Assert.Equal("roll-dice", provider.Choose(new(DecisionKind.RollDice, state.Units[0].TypeId, "actor", [new("roll-dice")], false), queries));
    }

    [Fact]
    public void DoNothingLyingUnitStillStandsAndCompletesActivation()
    {
        var state = State();
        state.Physical.Figures[0] = state.Physical.Figures[0] with { Posture = Posture.Lying };
        state.AssignOrder(state.Controllers[0].Token, OrderKind.DoNothing);
        var start = GameEngine.StartRound(state, new Dice(), false);
        Assert.Equal(Posture.Upright, start.State.Physical.Figures.Single(f => f.Id == "actor").Posture);
        Assert.Contains(start.Events, occurrence => occurrence.Kind == "ActivationCompleted" && occurrence.UnitId == "actor");
    }

    [Fact]
    public void HoldOverridesOrdinaryMoveButRetainsAttackAndDisplacerSwap()
    {
        var state = State(enemy: new(1, 1));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.HoldPosition);
        var provider = new OrderAwareAutomatedProvider();
        var queries = new GameplayQueries(state);
        var move = Movement(state, new Candidate("1,0", new(1, 0), [new(0, 1), new(1, 1)], Kind: ActivationChoiceKind.Move));
        Assert.Null(provider.Choose(move, queries));
        var act = new DecisionRequest(DecisionKind.Act, state.Units[0].TypeId, "actor",
            [new("attack:enemy", Action: UnitAction.NormalAttack, TargetId: "enemy")], true);
        Assert.Equal("attack:enemy", provider.Choose(act, queries));

    }

    [Fact]
    public void MoveToUsesTraversableDistanceProgressArrivalBlockedAndDeterministicTies()
    {
        var wall = new Edge(new(1, 0), new(2, 0), EdgeKind.Wall);
        var state = State(actor: new(1, 1), board: new Board(5, 3, [wall]));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.MoveTo, new(3, 0));
        var request = Movement(state,
            new("top", new(1, 0), [new(1, 1), new(1, 0)], Kind: ActivationChoiceKind.Move),
            new("right", new(2, 1), [new(1, 1), new(2, 1)], Kind: ActivationChoiceKind.Move),
            new("bottom", new(1, 2), [new(1, 1), new(1, 2)], Kind: ActivationChoiceKind.Move));
        var provider = new OrderAwareAutomatedProvider();
        Assert.Equal("right", provider.Choose(request, new GameplayQueries(state)));

        state.AssignOrder(state.Controllers[0].Token, OrderKind.MoveTo, new(1, 1));
        Assert.Null(provider.Choose(request, new GameplayQueries(state)));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.MoveTo, new(4, 2));
        var impassableGoal = state.Physical.Board with { Terrain = [new(new(4, 2), TerrainKind.Tree)] };
        state.Physical = state.Physical with { Board = impassableGoal };
        Assert.Equal("right", provider.Choose(request, new GameplayQueries(state)));

        var unreachable = State(actor: new(1, 1), board: new Board(5, 3,
        [
            new(new(3, 2), new(4, 2), EdgeKind.Wall),
            new(new(4, 1), new(4, 2), EdgeKind.Wall)
        ]));
        unreachable.AssignOrder(unreachable.Controllers[0].Token, OrderKind.MoveTo, new(4, 2));
        Assert.Null(provider.Choose(Movement(unreachable,
            new Candidate("right", new(2, 1), [new(1, 1), new(2, 1)], Kind: ActivationChoiceKind.Move)),
            new GameplayQueries(unreachable)));

        var tie = State(actor: new(1, 1), board: new Board(5, 3,
            [new(new(1, 1), new(2, 1), EdgeKind.Wall)]));
        tie.AssignOrder(tie.Controllers[0].Token, OrderKind.MoveTo, new(3, 1));
        var tied = Movement(tie,
            new("bottom", new(2, 2), [new(1, 1), new(1, 2), new(2, 2)], Kind: ActivationChoiceKind.Move),
            new("top", new(2, 0), [new(1, 1), new(1, 0), new(2, 0)], Kind: ActivationChoiceKind.Move));
        Assert.Equal("top", provider.Choose(tied, new GameplayQueries(tie)));
    }

    [Fact]
    public void MoveToMakesDeterministicProgressForPassableTwoByTwoAnchor()
    {
        var state = State(UnitType.RedDragon(), actor: new(0, 1), enemy: new(6, 4), board: new Board(7, 5, []));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.MoveTo, new(4, 1));
        var request = Movement(state,
            new("down", new(0, 2), [new(0, 1), new(0, 2)], Kind: ActivationChoiceKind.Move),
            new("right", new(2, 1), [new(0, 1), new(1, 1), new(2, 1)], Kind: ActivationChoiceKind.Move));

        Assert.Equal("right", new OrderAwareAutomatedProvider().Choose(request, new GameplayQueries(state)));
    }

    [Fact]
    public void MoveToRetainsDisplacerDemonSwapPriority()
    {
        var demon = State(UnitType.DisplacerDemon(), enemy: new(1, 1));
        demon.AssignOrder(demon.Controllers[0].Token, OrderKind.MoveTo, new(5, 3));
        var swap = UnitType.DisplacerDemon().BonusActions.Single(a => a.Swap is not null);
        var activation = new DecisionRequest(DecisionKind.Activation, demon.Units[0].TypeId, "actor",
            [new("swap", TargetId: "enemy", Kind: ActivationChoiceKind.BonusAction, BonusAction: swap),
             new("stay", Kind: ActivationChoiceKind.Stay)], false);

        Assert.Equal("swap", new OrderAwareAutomatedProvider().Choose(activation, new GameplayQueries(demon)));
    }

    [Fact]
    public void DoNothingDoesNotSuppressCommittedMandatoryDoorRoll()
    {
        var state = State(UnitType.Troll(), actor: new(0, 0), enemy: new(4, 0),
            board: new Board(5, 1, [new(new(1, 0), new(2, 0), EdgeKind.ClosedDoor)]));
        var started = GameEngine.StartRound(state, new Dice(), false);
        var moved = GameEngine.Advance(started.State, new Choice("1,0"), new Dice(), false);
        var door = moved.NextInput!.Candidates.Single(candidate => candidate.TryOpenDoor is not null);
        var committed = GameEngine.Advance(moved.State, new Choice(door.Key), new Dice(), false);
        Assert.NotNull(committed.State.DoorInProgress);
        Assert.Equal(DecisionKind.RollDice, committed.NextInput!.Kind);

        committed.State.AssignOrder(committed.State.Controllers[0].Token, OrderKind.DoNothing);
        var resolved = GameEngine.Advance(committed.State, new OrderAwareAutomatedProvider(), new Dice(), false);

        Assert.Contains(resolved.Events, occurrence => occurrence.Kind == "DiceRolled");
        Assert.Contains(resolved.Events, occurrence => occurrence.Kind == "DoorOpeningAttemptResolved");
        Assert.Null(resolved.State.DoorInProgress);
    }
    [Fact]
    public void MoveToRetainsGoblinBackAwayFollowUp()
    {
        var state = State(UnitType.Goblin(), actor: new(1, 1), enemy: new(1, 2));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.MoveTo, new(5, 1));
        var request = Movement(state,
            new("safe", new(1, 0), [new(1, 1), new(1, 0)], Kind: ActivationChoiceKind.Move),
            new("ordered", new(2, 1), [new(1, 1), new(2, 1)], Kind: ActivationChoiceKind.Move)) with { IsMoveAfterAttack = true };
        var queries = new GameplayQueries(state);
        Assert.Equal(new DefaultAutomatedProvider().Choose(request, queries),
            new OrderAwareAutomatedProvider().Choose(request, queries));
        Assert.Equal("safe", new OrderAwareAutomatedProvider().Choose(request, queries));
    }

    [Fact]
    public void PreviewUsesSameEffectiveOrderAsLiveProvider()
    {
        var state = State(enemy: new(4, 1));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.HoldPosition);
        var started = GameEngine.StartRound(state, new Dice(), false).State;
        var preview = GameEngine.PreviewDefaultBehaviorActivation(started, "actor");
        Assert.Equal("stay", preview.Steps[0].CandidateKey);
        Assert.Equal("stay", new OrderAwareAutomatedProvider().Choose(started.Pending!, new GameplayQueries(started)));
    }
}