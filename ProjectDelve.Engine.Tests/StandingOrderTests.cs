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

    [Fact]
    public void AttackMatchesExactAttackActionsAndFallsBackOnlyAfterTargetDisappears()
    {
        var wizard = State(UnitType.Wizard(), enemy: new(3, 1));
        wizard.AssignOrder(wizard.Controllers[0].Token, OrderKind.Attack, target: OrderTarget.ForUnit("enemy"));
        var request = new DecisionRequest(DecisionKind.Act, wizard.Units[0].TypeId, "actor",
        [
            new("telekinesis:enemy", Action: UnitAction.Telekinesis, TargetId: "enemy"),
            new("fireball:2,1", new(2, 1), Action: UnitAction.Fireball) { TargetIds = ["enemy"] }
        ], true);
        var provider = new OrderAwareAutomatedProvider();
        Assert.Equal("fireball:2,1", provider.Choose(request, new GameplayQueries(wizard)));

        wizard.Units[1] = wizard.Units[1] with { CurrentHp = 0 };
        Assert.Equal(new DefaultAutomatedProvider().Choose(request, new GameplayQueries(wizard)),
            provider.Choose(request, new GameplayQueries(wizard)));
    }

    [Fact]
    public void AttackGroupDynamicallyIncludesNewUnitsAndLivingUnreachableTargetStays()
    {
        var actor = UnitType.Grunt() with { Actions = UnitAction.None };
        var state = State(actor, enemy: new(5, 1));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.Attack,
            target: OrderTarget.ForGroup("enemy", "blue"));
        state.PlaceUnit("enemy", "later", "blue", new(4, 2));
        Assert.Equal(new[] { "enemy", "later" },
            new GameplayQueries(state).EligibleAttackOrderTargets("actor", state.Orders[0].Target!));

        var move = Movement(state,
            new Candidate("toward", new(1, 1), [new(0, 1), new(1, 1)], Kind: ActivationChoiceKind.Move));
        Assert.Null(new OrderAwareAutomatedProvider().Choose(move, new GameplayQueries(state)));
    }

    [Fact]
    public void AttackCompletesOrdinaryMoveBeforeChoosingMatchingAttack()
    {
        var state = State(enemy: new(1, 1));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.Attack, target: OrderTarget.ForUnit("enemy"));
        var activation = new DecisionRequest(DecisionKind.Activation, state.Units[0].TypeId, "actor",
        [
            new("move", new(0, 0), [new(0, 1), new(0, 0)], Kind: ActivationChoiceKind.Move),
            new("stay", Kind: ActivationChoiceKind.Stay),
            new("attack:enemy", Action: UnitAction.NormalAttack, TargetId: "enemy", Kind: ActivationChoiceKind.Action)
        ], false);

        Assert.Equal("stay", new OrderAwareAutomatedProvider().Choose(activation, new GameplayQueries(state)));
    }

    [Fact]
    public void AttackApproachRecognizesLegalZeroDieFireball()
    {
        var wizard = UnitType.Wizard() with { Atk = 0, Actions = UnitAction.Fireball };
        var state = State(wizard, enemy: new(3, 1));
        var queries = new GameplayQueries(state);

        Assert.True(queries.CanAttackAnyFrom("actor", new(0, 1), ["enemy"]));
    }

    [Fact]
    public void SideTargetsIncludeLaterUnitsWhileRemovedSpecificTargetNeverRetargets()
    {
        var state = State();
        var token = state.Controllers[0].Token;
        state.AssignOrder(token, OrderKind.Attack, target: OrderTarget.ForSide("blue"));
        state.PlaceUnit("enemy", "later", "blue", new(4, 2));
        var queries = new GameplayQueries(state);
        Assert.Equal(new[] { "enemy", "later" },
            queries.EligibleAttackOrderTargets("actor", state.OrderFor(token).Target!));

        state.AssignOrder(token, OrderKind.Attack, target: OrderTarget.ForUnit("enemy"));
        state.Units[1] = state.Units[1] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(figure => figure.Id == "enemy");
        Assert.Empty(new GameplayQueries(state).EligibleAttackOrderTargets("actor", state.OrderFor(token).Target!));
    }

    [Fact]
    public void AttackRetainsDragonPreferenceAndDoesNotClassifyHolyWaveAsAttack()
    {
        var dragon = State(UnitType.RedDragon(), actor: new(0, 0), enemy: new(2, 0), board: new Board(7, 4, []));
        dragon.PlaceUnit("enemy", "other", "blue", new(4, 0));
        dragon.AssignOrder(dragon.Controllers[0].Token, OrderKind.Attack, target: OrderTarget.ForSide("blue"));
        var actions = new DecisionRequest(DecisionKind.Act, dragon.Units[0].TypeId, "actor",
        [
            new("attack:enemy", Action: UnitAction.NormalAttack, TargetId: "enemy"),
            new("claw:enemy", Action: UnitAction.ClawAttack, TargetId: "enemy"),
            new Candidate("breath", Action: UnitAction.FireBreath) { TargetIds = ["enemy", "other"] }
        ], true);
        Assert.Equal("breath", new OrderAwareAutomatedProvider().Choose(actions, new GameplayQueries(dragon)));

        var cleric = State(UnitType.Cleric(), enemy: new(1, 1));
        cleric.AssignOrder(cleric.Controllers[0].Token, OrderKind.Attack, target: OrderTarget.ForUnit("enemy"));
        var wave = new DecisionRequest(DecisionKind.Act, cleric.Units[0].TypeId, "actor",
            [new Candidate("wave", Action: UnitAction.HolyWave) { TargetIds = ["enemy"] }], true);
        Assert.Null(new OrderAwareAutomatedProvider().Choose(wave, new GameplayQueries(cleric)));
    }

    [Fact]
    public void ProtectTracksCurrentPositionMakesProgressAndFallsBackAfterDefeat()
    {
        var state = State(actor: new(0, 1), enemy: new(5, 1));
        state.Units[1] = state.Units[1] with { SideId = "red" };
        state.AssignOrder(state.Controllers[0].Token, OrderKind.Protect, target: OrderTarget.ForUnit("enemy"));
        var move = Movement(state,
            new("down", new(0, 2), [new(0, 1), new(0, 2)], Kind: ActivationChoiceKind.Move),
            new("right", new(1, 1), [new(0, 1), new(1, 1)], Kind: ActivationChoiceKind.Move));
        var provider = new OrderAwareAutomatedProvider();
        Assert.Equal("right", provider.Choose(move, new GameplayQueries(state)));

        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new(0, 2) };
        Assert.Null(provider.Choose(move, new GameplayQueries(state)));
        state.Units[1] = state.Units[1] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAt(1);
        Assert.Equal(new DefaultAutomatedProvider().Choose(move, new GameplayQueries(state)),
            provider.Choose(move, new GameplayQueries(state)));
    }

    [Fact]
    public void RetreatUsesStrictSharedFleeRankingAndCandidateKeyTieBreak()
    {
        var state = State(actor: new(2, 1), enemy: new(4, 1), board: new Board(6, 4, []));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.Retreat);
        var move = Movement(state,
            new("z-left", new(1, 1), [new(2, 1), new(1, 1)], Kind: ActivationChoiceKind.Move),
            new("a-left", new(1, 1), [new(2, 1), new(1, 1)], Kind: ActivationChoiceKind.Move));
        Assert.Equal("a-left", new OrderAwareAutomatedProvider().Choose(move, new GameplayQueries(state)));

        state.Physical.Figures[1] = state.Physical.Figures[1] with { Position = new(5, 1) };
        var noSafer = Movement(state,
            new Candidate("closer", new(3, 1), [new(2, 1), new(3, 1)], Kind: ActivationChoiceKind.Move));
        Assert.Null(new OrderAwareAutomatedProvider().Choose(noSafer, new GameplayQueries(state)));
    }

    [Fact]
    public void RetreatStaysWithoutHostilesAndMatchesSharedFleeChoice()
    {
        var fleeing = UnitType.Grunt() with { Behaviors = UnitBehavior.Flee };
        var state = State(fleeing, actor: new(2, 1), enemy: new(4, 1), board: new Board(6, 4, []));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.Retreat);
        var move = new DecisionRequest(DecisionKind.Move, fleeing.Id, "actor",
        [
            new("left", new(1, 1), [new(2, 1), new(1, 1)]),
            new("up", new(2, 0), [new(2, 1), new(2, 0)])
        ], true);
        Assert.Equal(new DefaultAutomatedProvider().Choose(move, new GameplayQueries(state)),
            new OrderAwareAutomatedProvider().Choose(move, new GameplayQueries(state)));

        state.Units[1] = state.Units[1] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAt(1);
        Assert.Null(new OrderAwareAutomatedProvider().Choose(move, new GameplayQueries(state)));
    }

    [Fact]
    public void InteractWithChoosesOnlyCanonicalOrderedDoorAndFallsBackWhenOpen()
    {
        var ordered = new Edge(new(1, 0), new(2, 0), EdgeKind.ClosedDoor);
        var other = new Edge(new(0, 1), new(1, 1), EdgeKind.ClosedDoor);
        var state = State(UnitType.Troll(), actor: new(1, 0), enemy: new(4, 0),
            board: new Board(5, 2, [ordered, other]));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.InteractWith,
            target: OrderTarget.ForDoor(ordered.B, ordered.A));
        var act = new DecisionRequest(DecisionKind.Act, state.Units[0].TypeId, "actor",
        [
            new("try-other", Door: other, TryOpenDoor: new(2)),
            new("try-ordered", Door: ordered, TryOpenDoor: new(2))
        ], true);
        var provider = new OrderAwareAutomatedProvider();
        Assert.Equal("try-ordered", provider.Choose(act, new GameplayQueries(state)));

        state.Physical.Board.Edges[0] = ordered with { Kind = EdgeKind.OpenDoor };
        Assert.Equal(new DefaultAutomatedProvider().Choose(act, new GameplayQueries(state)),
            provider.Choose(act, new GameplayQueries(state)));
    }

    [Fact]
    public void InteractWithChoosesFreeOpeningApproachesAndFallsBackWhenUnsupportedOrUnreachable()
    {
        var door = new Edge(new(3, 0), new(4, 0), EdgeKind.ClosedDoor);
        var hero = State(UnitType.Barbarian(), actor: new(3, 0), enemy: new(4, 1), board: new Board(5, 2, [door]));
        hero.AssignOrder(hero.Controllers[0].Token, OrderKind.InteractWith, target: OrderTarget.ForDoor(door.A, door.B));
        var free = new DecisionRequest(DecisionKind.Activation, hero.Units[0].TypeId, "actor",
            [new("open", Door: door, Kind: ActivationChoiceKind.FreeAction, FreeAction: UnitFreeAction.OpenDoor),
             new("stay", Kind: ActivationChoiceKind.Stay)], false);
        Assert.Equal("open", new OrderAwareAutomatedProvider().Choose(free, new GameplayQueries(hero)));

        hero.Physical.Figures[0] = hero.Physical.Figures[0] with { Position = new(0, 0) };
        var approach = Movement(hero,
            new("down", new(0, 1), [new(0, 0), new(0, 1)], Kind: ActivationChoiceKind.Move),
            new("right", new(1, 0), [new(0, 0), new(1, 0)], Kind: ActivationChoiceKind.Move));
        Assert.Equal("right", new OrderAwareAutomatedProvider().Choose(approach, new GameplayQueries(hero)));

        var unsupported = State(actor: new(0, 0), enemy: new(4, 1), board: new Board(5, 2, [door]));
        unsupported.AssignOrder(unsupported.Controllers[0].Token, OrderKind.InteractWith,
            target: OrderTarget.ForDoor(door.A, door.B));
        Assert.Equal(new DefaultAutomatedProvider().Choose(approach, new GameplayQueries(unsupported)),
            new OrderAwareAutomatedProvider().Choose(approach, new GameplayQueries(unsupported)));

        var blockedDoor = new Edge(new(3, 0), new(4, 0), EdgeKind.ClosedDoor);
        var blocked = State(UnitType.Troll(), actor: new(0, 0), enemy: new(4, 1), board: new Board(5, 2,
        [
            blockedDoor,
            new(new(1, 0), new(2, 0), EdgeKind.Wall),
            new(new(1, 1), new(2, 1), EdgeKind.Wall)
        ]));
        blocked.AssignOrder(blocked.Controllers[0].Token, OrderKind.InteractWith,
            target: OrderTarget.ForDoor(blockedDoor.A, blockedDoor.B));
        Assert.Equal(new DefaultAutomatedProvider().Choose(approach, new GameplayQueries(blocked)),
            new OrderAwareAutomatedProvider().Choose(approach, new GameplayQueries(blocked)));
    }

    [Fact]
    public void AttackPreviewMatchesLiveOrderedChoice()
    {
        var state = State(enemy: new(4, 1));
        state.AssignOrder(state.Controllers[0].Token, OrderKind.Attack, target: OrderTarget.ForUnit("enemy"));
        var started = GameEngine.StartRound(state, new Dice(), false).State;
        var preview = GameEngine.PreviewDefaultBehaviorActivation(started, "actor");

        Assert.Equal(new OrderAwareAutomatedProvider().Choose(started.Pending!, new GameplayQueries(started)),
            preview.Steps[0].CandidateKey);
    }
}
