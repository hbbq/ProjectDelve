using System.Reflection;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class OrderPlanningDispatchTests
{
    // These fixtures belong to this test; no authored scenario layout is a contract.
    private static GameState State(UnitType? type = null)
    {
        type ??= new("actor-type", 2, 1, 2, 1, 1);
        var other = new UnitType("other", 1, 1, 1, 1, 1);
        return new()
        {
            Physical = new(new Board(6, 5, []),
                [new("actor", new(1, 2)), new("friend", new(4, 2)), new("enemy", new(4, 3))]),
            Types = [type, other],
            Units = [type.CreateUnit("actor", "red"), other.CreateUnit("friend", "red"), other.CreateUnit("enemy", "blue")],
            Controllers = [new(new(type.Id, "red"), ControllerKind.Human),
                new(new(other.Id, "red"), ControllerKind.Human), new(new(other.Id, "blue"), ControllerKind.Human)],
            Objectives = [new("red"), new("blue")]
        };
    }

    private static void Assign(GameState state, OrderKind kind)
    {
        if (kind == OrderKind.InteractWith)
            state.Physical.Board.Edges.Add(new(new(4, 1), new(5, 1), EdgeKind.ClosedDoor));
        if (kind == OrderKind.FollowRoute)
        {
            state.Waypoints.Add(new("goal", new(5, 2, 1, 1)));
            state.Routes.Add(new("route", ["goal"]));
        }
        state.AssignOrder(state.Controllers[0].Token, kind, kind == OrderKind.MoveTo ? new(5, 2) : null,
            kind == OrderKind.Attack ? OrderTarget.ForUnit("enemy") :
            kind == OrderKind.Protect ? OrderTarget.ForUnit("friend") :
            kind == OrderKind.InteractWith ? OrderTarget.ForDoor(new(4, 1), new(5, 1)) : null,
            kind == OrderKind.FollowRoute ? new("route") : null);
    }

    private static DecisionRequest Movement(GameState state, DecisionKind kind = DecisionKind.Activation) => new(
        kind, state.Units[0].TypeId, "actor",
        [new("up", new(1, 1), [new(1, 2), new(1, 1)], Kind: ActivationChoiceKind.Move),
         new("right", new(2, 2), [new(1, 2), new(2, 2)], Kind: ActivationChoiceKind.Move),
         new("down", new(1, 3), [new(1, 2), new(1, 3)], Kind: ActivationChoiceKind.Move)],
        kind == DecisionKind.Move);

    private static DecisionRequest WithStay(DecisionRequest request) => request.Kind == DecisionKind.Activation
        ? request with { Candidates = [.. request.Candidates, new("stay", Kind: ActivationChoiceKind.Stay)] }
        : request;

    // Forward all real queries while rejecting Normal's general tactical movement scoring.
    public class NoNormalMovementQueries : DispatchProxy
    {
        public IGameplayQueries Inner = null!;
        public int ForbiddenCalls;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name is nameof(IGameplayQueries.CanAttackHostileFrom) or
                nameof(IGameplayQueries.DistanceToNearestAttackableHostileFrom) ||
                method.Name == nameof(IGameplayQueries.DistanceToAttackPositionFrom) && args!.Length == 3)
            {
                ForbiddenCalls++;
                throw new InvalidOperationException("Normal movement scoring must not run for this Order.");
            }
            return method.Invoke(Inner, args);
        }
    }

    private static (IGameplayQueries Queries, NoNormalMovementQueries Proxy) Guard(GameState state)
    {
        var queries = DispatchProxy.Create<IGameplayQueries, NoNormalMovementQueries>();
        var proxy = (NoNormalMovementQueries)queries;
        proxy.Inner = new GameplayQueries(state);
        return (queries, proxy);
    }

    [Theory]
    [InlineData(OrderKind.HoldPosition, DecisionKind.Activation, "stay")]
    [InlineData(OrderKind.MoveTo, DecisionKind.Activation, "right")]
    [InlineData(OrderKind.Protect, DecisionKind.Activation, "right")]
    [InlineData(OrderKind.HoldPosition, DecisionKind.Move, null)]
    [InlineData(OrderKind.MoveTo, DecisionKind.Move, "right")]
    [InlineData(OrderKind.Protect, DecisionKind.Move, "right")]
    public void EffectiveOrderSkipsDiscardedNormalScoring(OrderKind order, DecisionKind kind, string? expected)
    {
        var state = State();
        Assign(state, order);
        var guarded = Guard(state);
        Assert.Equal(expected, new OrderAwareAutomatedProvider().Choose(WithStay(Movement(state, kind)), guarded.Queries));
        Assert.Equal(0, guarded.Proxy.ForbiddenCalls);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("upright")]
    [InlineData("equipment-door")]
    [InlineData("boost")]
    [InlineData("reshape")]
    [InlineData("swap")]
    public void HigherPriorityChoicesStillPrecedeOrderedMovement(string priority)
    {
        var behavior = priority == "reshape" ? UnitBehavior.PreferReshapeRoom :
            priority == "swap" ? UnitBehavior.SwapThenAttackThenDisplace : UnitBehavior.None;
        var state = State(new("actor-type", 2, 1, 2, 1, 1, Behaviors: behavior));
        Assign(state, OrderKind.HoldPosition);
        var candidate = new Candidate(priority, TargetId: "enemy", Kind: ActivationChoiceKind.BonusAction);
        AbilityMechanic mechanic = priority switch
        {
            "upright" => new AdjacentUnitPostureChangeMechanic(RelationshipSelector.Friendlies, Posture.Upright),
            "equipment-door" => new OpenDoor(), "boost" => new SelfExtraMovesMechanic(1),
            "reshape" => new ReshapeRoom(), "swap" => new Swap(), _ => new ExtraActionsMechanic(1)
        };
        if (priority == "object") candidate = candidate with { ObjectId = "physical-object", Kind = ActivationChoiceKind.FreeAction };
        else candidate = candidate with
        {
            Ability = new(new("test", "Test", AbilityTiming.BonusAction, mechanic),
                priority == "equipment-door" ? AbilitySourceKind.Equipment : AbilitySourceKind.Unit,
                "source", "instance"),
            FreeAction = priority == "equipment-door" ? UnitFreeAction.OpenDoor : null
        };
        var request = WithStay(Movement(state));
        request = request with { Candidates = [.. request.Candidates, candidate] };
        var guarded = Guard(state);
        Assert.Equal(priority, new OrderAwareAutomatedProvider().Choose(request, guarded.Queries));
        Assert.Equal(priority, new FrozenOrderAwareAutomatedProvider().Choose(request, new GameplayQueries(state)));
        Assert.Equal(0, guarded.Proxy.ForbiddenCalls);
    }

    [Fact]
    public void WorkerAndSpecialFollowUpRetainNormalDispatch()
    {
        var worker = State(UnitType.Worker());
        Assign(worker, OrderKind.MoveTo);
        var request = WithStay(Movement(worker));
        Assert.Equal(new DefaultAutomatedProvider().Choose(request, new GameplayQueries(worker)),
            new OrderAwareAutomatedProvider().Choose(request, new GameplayQueries(worker)));
        Assign(worker, OrderKind.DoNothing);
        request = request with { Candidates = [.. request.Candidates, new("end", Kind: ActivationChoiceKind.EndTurn)] };
        Assert.Equal("end", new OrderAwareAutomatedProvider().Choose(request, new GameplayQueries(worker)));

        var goblin = State(UnitType.Goblin());
        Assign(goblin, OrderKind.MoveTo);
        request = Movement(goblin, DecisionKind.Move) with { IsMoveAfterAttack = true };
        Assert.Equal(new DefaultAutomatedProvider().Choose(request, new GameplayQueries(goblin)),
            new OrderAwareAutomatedProvider().Choose(request, new GameplayQueries(goblin)));
    }

    [Theory]
    [InlineData("extra-action")]
    [InlineData("summon")]
    [InlineData("displace")]
    public void NonMovementActivationRetainsEstablishedAbilityPreferences(string priority)
    {
        var behavior = priority == "summon" ? UnitBehavior.PreferSummonWorker :
            priority == "displace" ? UnitBehavior.SwapThenAttackThenDisplace : UnitBehavior.None;
        var state = State(new("actor-type", 2, 1, 2, 1, 1, Behaviors: behavior));
        Assign(state, OrderKind.HoldPosition);
        AbilityMechanic mechanic = priority switch
        {
            "extra-action" => new ExtraActionsMechanic(1),
            "summon" => new AdjacentUnitSummonMechanic(UnitTypeIds.Worker, Posture.Upright, 3),
            _ => new Displace(1)
        };
        var candidate = new Candidate(priority, new(3, 3), TargetId: "enemy", Kind: ActivationChoiceKind.BonusAction)
        {
            Ability = new(new("test", "Test", AbilityTiming.BonusAction, mechanic), AbilitySourceKind.Unit,
                "actor-type", "actor")
        };
        var request = new DecisionRequest(DecisionKind.Activation, state.Units[0].TypeId, "actor",
            [candidate, new("end", Kind: ActivationChoiceKind.EndTurn)], false);
        Assert.Equal(priority, new FrozenOrderAwareAutomatedProvider().Choose(request, new GameplayQueries(state)));
        Assert.Equal(priority, new OrderAwareAutomatedProvider().Choose(request, new GameplayQueries(state)));
    }

    [Fact]
    public void FrozenProvidersAgreeAcrossOrdersBehaviorsAndCandidateOrder()
    {
        UnitBehavior[] behaviors = [UnitBehavior.None, UnitBehavior.Flee, UnitBehavior.MaximizeAttackDistance,
            UnitBehavior.ApproachThroughClosedDoors, UnitBehavior.PreferHostileAdjacency,
            UnitBehavior.ProtectWithoutAdjacency, UnitBehavior.SeekDiamond,
            UnitBehavior.Flee | UnitBehavior.PreferHostileAdjacency, UnitBehavior.Worker];
        OrderKind[] orders = [OrderKind.Normal, OrderKind.DoNothing, OrderKind.HoldPosition,
            OrderKind.MoveTo, OrderKind.Protect, OrderKind.Attack, OrderKind.Retreat,
            OrderKind.FollowRoute, OrderKind.InteractWith];
        foreach (var behavior in behaviors)
        foreach (var order in orders)
        foreach (var kind in new[] { DecisionKind.Activation, DecisionKind.Move })
        foreach (var reverse in new[] { false, true })
        {
            var state = State(new("actor-type", 2, 2, 2, 1, 1, Behaviors: behavior));
            Assign(state, order);
            var request = WithStay(Movement(state, kind));
            if (reverse) request = request with { Candidates = request.Candidates.AsEnumerable().Reverse().ToList() };
            Assert.Equal(new FrozenOrderAwareAutomatedProvider().Choose(request, new GameplayQueries(state)),
                new OrderAwareAutomatedProvider().Choose(request, new GameplayQueries(state)));
            Assert.Equal(new FrozenDefaultAutomatedProvider().Choose(request, new GameplayQueries(state)),
                new DefaultAutomatedProvider().Choose(request, new GameplayQueries(state)));
        }
    }

    private sealed class SeededRandom(int seed) : IRandomProvider
    {
        private readonly Random random = new(seed);
        public List<int> Draws { get; } = [];
        private int Next(int max) { var value = random.Next(max); Draws.Add(value); return value; }
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[Next(bag.Count)];
        public AttackFace RollAttackDie() => Next(2) == 0 ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => Next(2) == 0 ? DefenceFace.Block : DefenceFace.Miss;
        public int RollD6() => Next(6) + 1;
    }

    private static string Trace(IDecisionProvider provider, OrderKind order, int seed)
    {
        var state = State(UnitType.Goblin());
        Assign(state, order);
        var random = new SeededRandom(seed);
        var result = TestGame.StartRound(state, random, false);
        var trace = new List<string>();
        for (var step = 0; step < 120; step++)
        {
            trace.Add(JsonSerializer.Serialize(new { result.State, result.NextInput, result.Events, result.ResolutionSteps }));
            if (result.NextInput is null) return JsonSerializer.Serialize(new { trace, random.Draws });
            // Include explicit dice continuations and every authoritative candidate in the trace.
            result = GameEngine.Advance(result.State, provider, random, false);
        }
        throw new InvalidOperationException("Test-owned round did not finish within 120 decisions.");
    }

    [Theory]
    [InlineData(OrderKind.Normal, 1729)]
    [InlineData(OrderKind.HoldPosition, 61)]
    [InlineData(OrderKind.MoveTo, 101)]
    [InlineData(OrderKind.Attack, 209)]
    [InlineData(OrderKind.Protect, 346)]
    [InlineData(OrderKind.Retreat, 442)]
    public void FixedSeedFullRoundTraceMatchesFrozenProviders(OrderKind order, int seed) =>
        Assert.Equal(Trace(new FrozenOrderAwareAutomatedProvider(), order, seed),
            Trace(new OrderAwareAutomatedProvider(), order, seed));
}
