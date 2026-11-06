using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AreaOrderTests
{
    private static readonly ObjectiveArea Home = new(4, 3, 3, 3);
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Pick(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private static ScenarioDefinition Definition(UnitType? actor = null, Cell? start = null, Cell? enemy = null, ObjectiveArea? area = null)
    {
        actor ??= UnitRoster.Grunt("guard");
        return new()
        {
            Board = new() { Width = 22, Height = 12 }, UnitTypeIds = [actor.Id, "target"],
            UnitTypes = [actor, new("target", 0, 1, 1, 0, 1)],
            Units = [new(actor.Id, "blue", start ?? new(5, 4), UnitId: "guard"),
                new("target", "red", enemy ?? new(21, 11), UnitId: "enemy")],
            Agency = [new(actor.Id, "blue", ControllerKind.Automated), new("target", "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            Orders = [new(actor.Id, "blue", OrderKind.GuardArea, Area: area ?? Home)], Loot = LootSettings.Empty
        };
    }
    private static GameState State(ScenarioDefinition definition)
    {
        var state = GameEngine.CreateGame(definition);
        state.Round = 1; state.ActiveToken = state.Units[0].Token; state.CurrentUnitId = "guard";
        state.ResolvingTokenUnitIds = ["guard"];
        return state;
    }
    private static EngineResult Ready(GameState state) => GameEngine.RefreshChoices(state, new Dice(), false);
    private static string? Choose(GameState state)
    {
        var ready = Ready(state);
        return new OrderAwareAutomatedProvider().Choose(ready.NextInput!, new GameplayQueries(ready.State));
    }
    private static Cell Destination(GameState state)
    {
        var ready = Ready(state);
        var key = new OrderAwareAutomatedProvider().Choose(ready.NextInput!, new GameplayQueries(ready.State));
        return ready.NextInput!.Candidates.Single(c => c.Key == key).Destination ?? state.Physical.Figures.Single(f => f.Id == "guard").Position;
    }

    [Fact]
    public void AreaPayloadIsValidatedCopiedAndRoundTripsThroughSharedBoundary()
    {
        var definition = Definition();
        var restored = ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition));
        var state = GameEngine.CreateGame(restored);
        Assert.Equal(Home, Assert.Single(state.Orders).Area);
        Assert.Equal(state.Orders, state.Copy().Orders);
        Assert.Equal(state.Orders, JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!.Orders);
        var token = state.Units[0].Token;
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.GuardArea));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.GuardArea, area: new(-1, 0, 1, 1)));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.GuardArea, area: new(0, 0, int.MaxValue, 1)));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.GuardArea, new(0, 0), area: Home));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.HoldPosition, area: Home));
    }

    [Fact]
    public void IdleGuardStaysAndDisplacedGuardReturnsToWholeArea()
    {
        var state = State(Definition());
        Assert.Equal("stay", Choose(state));
        var outside = State(Definition(start: new(11, 4)));
        var chosen = Destination(outside);
        Assert.True(chosen.X < 11);
        Assert.Equal(new(8, 4), chosen);
    }

    [Fact]
    public void LargeGuardReturnsUntilEntireFootprintFitsAndUnfittableAreaStays()
    {
        var large = UnitRoster.RedDragon("guard");
        var state = State(Definition(large, new(6, 4)));
        Assert.NotEqual("stay", Choose(state));
        Assert.Equal(0, new GameplayQueries(state).FootprintDistanceToArea("guard", Destination(state), Home));
        var tooSmall = State(Definition(large, new(6, 4), area: new(4, 3, 1, 1)));
        Assert.Equal("stay", Choose(tooSmall));
        Assert.Null(new GameplayQueries(tooSmall).WholeFootprintRouteDistanceToArea("guard", new(6, 4), new(4, 3, 1, 1)));
    }

    [Fact]
    public void MeleeEngagesAtMovPlusRangeRatherThanMovAndHoldsAttackPosition()
    {
        var state = State(Definition(UnitRoster.Goblin("guard"), enemy: new(10, 4)));
        var q = new GameplayQueries(state);
        Assert.Equal(5, q.DistanceToUnitFrom("guard", new(5, 4), "enemy"));
        Assert.True(q.DistanceToUnitFrom("guard", new(5, 4), "enemy") > q.EffectiveMovOf("guard"));
        Assert.Equal(new[] { "enemy" }, AreaOrderBehavior.Threats(q, "guard", Home));
        Assert.NotEqual("stay", Choose(state));
        var adjacent = State(Definition(UnitRoster.Goblin("guard"), enemy: new(6, 4)));
        Assert.Equal("stay", Choose(adjacent));
    }

    [Fact]
    public void RangedEngagesAtSevenWhereMovOnlyThreeWouldMiss()
    {
        var state = State(Definition(UnitRoster.SkeletonArcher("guard"), enemy: new(12, 4)));
        var q = new GameplayQueries(state);
        Assert.Equal(7, q.DistanceToUnitFrom("guard", new(5, 4), "enemy"));
        Assert.Equal(7, AreaOrderBehavior.Engagement(q, "guard"));
        Assert.Equal(9, AreaOrderBehavior.Leash(q, "guard"));
        Assert.True(7 > q.EffectiveMovOf("guard"));
        var destination = Destination(state);
        Assert.True(q.CanAttackAnyFrom("guard", destination, new[] { "enemy" }));
        Assert.Equal(4, Math.Abs(12 - destination.X) + Math.Abs(4 - destination.Y));
    }

    [Fact]
    public void MeleeLeashRejectsDistantThreatThatTwiceMovWouldAllow()
    {
        var area = new ObjectiveArea(0, 4, 1, 1);
        var state = State(Definition(UnitRoster.Goblin("guard"), new(6, 4), new(8, 4), area));
        var q = new GameplayQueries(state);
        Assert.Equal(7, AreaOrderBehavior.Leash(q, "guard"));
        Assert.Equal(8, 2 * q.EffectiveMovOf("guard"));
        Assert.Empty(AreaOrderBehavior.Threats(q, "guard", area));
        Assert.Equal(new(2, 4), Destination(state));
        state.Types[0] = state.Types[0] with { Behaviors = state.Types[0].Behaviors | UnitBehavior.IgnoreLeash };
        Assert.Equal(new[] { "enemy" }, AreaOrderBehavior.Threats(new GameplayQueries(state), "guard", area));
        Assert.Equal(new(7, 4), Destination(state));
    }

    [Fact]
    public void RangedLeashPermitsThreatThatTwiceMovWouldRejectAndTurretRetainsRange()
    {
        var area = new ObjectiveArea(0, 4, 1, 1);
        var state = State(Definition(UnitRoster.SkeletonArcher("guard"), new(4, 4), new(8, 4), area));
        var q = new GameplayQueries(state);
        Assert.Equal(6, 2 * q.EffectiveMovOf("guard"));
        Assert.Equal(9, AreaOrderBehavior.Leash(q, "guard"));
        Assert.Single(AreaOrderBehavior.Threats(q, "guard", area));
        Assert.Equal("stay", Choose(state));
        state.Types[0] = state.Types[0] with { Mov = 0 };
        q = new GameplayQueries(state);
        Assert.Equal(6, AreaOrderBehavior.Leash(q, "guard"));
        Assert.Equal(4, AreaOrderBehavior.Engagement(q, "guard"));
    }

    [Fact]
    public void EffectiveEquipmentAndTurnModifiersChangeThreatBudget()
    {
        var definition = Definition(UnitRoster.Grunt("guard") with { CanHaveEquipment = true }, enemy: new(11, 4));
        definition = definition with { EquipmentTypes = [new("lens", "Lens")
        { LocalAbilities = [new("stats", "Stats", AbilityTiming.Passive, new SelfModifiersMechanic([new(Stat.Rng, 1)]))] }] };
        var state = State(definition);
        Assert.Empty(AreaOrderBehavior.Threats(new GameplayQueries(state), "guard", Home));
        state.Units[0] = state.Units[0] with { Inventory = [new("lens-1", "lens")] };
        state.ModifiersThisTurn.Add(new(Stat.Mov, 1));
        var queries = new GameplayQueries(state);
        Assert.Equal(6, AreaOrderBehavior.Engagement(queries, "guard"));
        Assert.Single(AreaOrderBehavior.Threats(queries, "guard", Home));
        Assert.NotEqual("stay", Choose(state));
    }

    [Fact]
    public void HiddenUnreachableAndAttacklessTargetsDoNotProvokeApproach()
    {
        var state = State(Definition(enemy: new(8, 4)));
        for (var y = 0; y < 12; y++) state.Physical.Board.Edges.Add(new(new(7, y), new(8, y), EdgeKind.Wall));
        state.FogOfWarSides = ["blue"]; state.ExploredCells["blue"] = [new(5, 4)];
        Assert.Empty(new GameplayQueries(state).KnownHostileUnitIds("guard"));
        Assert.Equal("stay", Choose(state));
        state.FogOfWarSides.Clear();
        Assert.Empty(AreaOrderBehavior.Threats(new GameplayQueries(state), "guard", Home));
        Assert.Equal("stay", Choose(state));
        var attackless = State(Definition(new("guard", 3, 1, 0, 0, 1), enemy: new(8, 4)));
        Assert.Empty(AreaOrderBehavior.Threats(new GameplayQueries(attackless), "guard", Home));
        Assert.Equal("stay", Choose(attackless));
    }

    [Fact]
    public void RangedGuardEngagesAcrossImpassableWaterUsingReachableAttackPosition()
    {
        var state = State(Definition(UnitRoster.SkeletonArcher("guard"), enemy: new(12, 4)));
        for (var y = 0; y < 12; y++) state.Physical.Board.Terrain.Add(new(new(9, y), TerrainKind.Water));
        var queries = new GameplayQueries(state);
        Assert.Null(queries.DistanceToUnitFrom("guard", new(5, 4), "enemy"));
        Assert.Single(AreaOrderBehavior.Threats(queries, "guard", Home));
        var destination = Destination(state);
        Assert.True(queries.CanAttackAnyFrom("guard", destination, new[] { "enemy" }));
        Assert.Equal(8, destination.X);
    }

    [Fact]
    public void FleeShamanRanksSafetyInsideAreaAndReturnsWhenOutside()
    {
        var state = State(Definition(UnitRoster.Shaman("guard"), enemy: new(8, 4)));
        var chosen = Destination(state);
        var q = new GameplayQueries(state);
        Assert.Equal(0, q.FootprintDistanceToArea("guard", chosen, Home));
        Assert.True(q.DistanceToNearestHostileFrom("guard", chosen) >= q.DistanceToNearestHostileFrom("guard", new(5, 4)));
        var outside = State(Definition(UnitRoster.Shaman("guard"), new(10, 4), new(11, 4)));
        var moved = Destination(outside);
        Assert.True(moved.X < 10);
    }

    [Fact]
    public void WorkerBypassesAreaMovementAndSpecialAbilityKeepsPriority()
    {
        var worker = State(Definition(UnitRoster.Worker("guard"), new(10, 4), new(12, 4)));
        var ready = Ready(worker);
        Assert.Equal(new DefaultAutomatedProvider().Choose(ready.NextInput!, new GameplayQueries(worker)),
            new OrderAwareAutomatedProvider().Choose(ready.NextInput!, new GameplayQueries(worker)));
        var swap = State(Definition(UnitRoster.DisplacerDemon("guard"), enemy: new(6, 4)));
        var swapReady = Ready(swap);
        var key = new OrderAwareAutomatedProvider().Choose(swapReady.NextInput!, new GameplayQueries(swap));
        Assert.IsType<Swap>(swapReady.NextInput!.Candidates.Single(c => c.Key == key).Ability!.Definition.Mechanic);
    }

    [Fact]
    public void GuardRetainsShamanSummonActionAndGoblinSpecialFollowUp()
    {
        var state = State(Definition(UnitRoster.Shaman("guard"), enemy: new(8, 4)));
        state.MoveDone = true;
        var ready = Ready(state);
        var chosen = new OrderAwareAutomatedProvider().Choose(ready.NextInput!, new GameplayQueries(ready.State));
        Assert.IsType<AdjacentUnitSummonMechanic>(ready.NextInput!.Candidates.Single(c => c.Key == chosen).Ability!.Definition.Mechanic);
        var goblin = State(Definition(UnitRoster.Goblin("guard"), enemy: new(6, 4)));
        var request = new DecisionRequest(DecisionKind.Move, "guard", "guard",
            [new("back", Destination: new(4, 4), Path: [new(5, 4), new(4, 4)])], true, IsMoveAfterAttack: true);
        Assert.Equal(new DefaultAutomatedProvider().Choose(request, new GameplayQueries(goblin)),
            new OrderAwareAutomatedProvider().Choose(request, new GameplayQueries(goblin)));
    }

    [Theory]
    [InlineData(RouteMode.Once)]
    [InlineData(RouteMode.Loop)]
    [InlineData(RouteMode.PingPong)]
    public void PatrolCombatArrivalPreservesCursorAndQuietRouteMovementResumesIt(RouteMode mode)
    {
        var definition = Definition(enemy: new(8, 4)) with
        {
            Waypoints = [new("home", Home), new("next", new(16, 3, 2, 3))],
            Routes = [new("route", ["home", "next"], mode)],
            Orders = [new("guard", "blue", OrderKind.PatrolRoute, Route: new("route"))]
        };
        var state = State(definition);
        var ready = Ready(state);
        var result = GameEngine.Advance(ready.State, new Pick("stay"), new Dice(), false);
        Assert.Equal(0, result.State.OrderFor(state.Units[0].Token).Route!.CurrentIndex);
        Assert.DoesNotContain(result.Events, e => e.Kind == "WaypointReached");
        state = State(definition);
        state.Units[1] = state.Units[1] with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(f => f.Id == "enemy");
        ready = Ready(state);
        result = GameEngine.Advance(ready.State, new OrderAwareAutomatedProvider(), new Dice(), false);
        Assert.Equal(1, result.State.OrderFor(state.Units[0].Token).Route!.CurrentIndex);
        Assert.Contains(result.Events, e => e.Kind == "RouteAdvanced");
        var saved = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(result.State))!;
        Assert.Equal(result.State.Orders, saved.Orders);
    }

    [Fact]
    public void PatrolApproachIntoWaypointDoesNotAdvanceUntilCombatEnds()
    {
        var definition = Definition(start: new(3, 4), enemy: new(7, 4)) with
        {
            Waypoints = [new("home", Home), new("next", new(16, 3, 2, 3))],
            Routes = [new("route", ["home", "next"], RouteMode.Loop)],
            Orders = [new("guard", "blue", OrderKind.PatrolRoute, Route: new("route"))]
        };
        var ready = Ready(State(definition));
        var result = GameEngine.Advance(ready.State, new OrderAwareAutomatedProvider(), new Dice(), false);
        Assert.Equal(new(6, 4), result.State.Physical.Figures.Single(f => f.Id == "guard").Position);
        var token = result.State.Units[0].Token;
        Assert.Equal(0, result.State.OrderFor(token).Route!.CurrentIndex);
        Assert.DoesNotContain(result.Events, e => e.Kind == "WaypointReached");
        var next = result.State;
        next.Units[1] = next.Units[1] with { CurrentHp = 0 };
        next.Physical.Figures.RemoveAll(f => f.Id == "enemy");
        next.MoveDone = false; next.ActionDone = false; next.Pending = null;
        ready = Ready(next);
        result = GameEngine.Advance(ready.State, new OrderAwareAutomatedProvider(), new Dice(), false);
        Assert.Equal(1, result.State.OrderFor(token).Route!.CurrentIndex);
    }

    [Theory]
    [InlineData(RouteMode.Once, -1, 1)]
    [InlineData(RouteMode.Loop, 0, 1)]
    [InlineData(RouteMode.PingPong, 0, -1)]
    public void PatrolFinalWaypointPreservesExistingRouteModeSemantics(RouteMode mode, int index, int direction)
    {
        var definition = Definition(start: new(16, 4)) with
        {
            Waypoints = [new("home", Home), new("next", new(16, 3, 2, 3))],
            Routes = [new("route", ["home", "next"], mode)],
            Orders = [new("guard", "blue", OrderKind.PatrolRoute, Route: new("route", 1))]
        };
        var ready = Ready(State(definition));
        var result = GameEngine.Advance(ready.State, new OrderAwareAutomatedProvider(), new Dice(), false);
        var order = result.State.OrderFor(result.State.Units[0].Token);
        if (index < 0) Assert.Equal(OrderKind.Normal, order.Kind);
        else
        {
            Assert.Equal(OrderKind.PatrolRoute, order.Kind);
            Assert.Equal(new("route", index, direction), order.Route);
        }
    }

    [Fact]
    public void NamedPatrolGroupsHaveIndependentCursorsAndAreaCannotBeReachedThroughWalls()
    {
        var definition = Definition() with
        {
            Waypoints = [new("home", Home), new("next", new(16, 3, 2, 3))],
            Routes = [new("route", ["home", "next"], RouteMode.Loop)],
            Orders = [new("guard", "blue", OrderKind.PatrolRoute, Route: new("route"))]
        };
        definition.Units[0] = definition.Units[0] with { GroupId = "a" };
        definition.Units.Add(new("guard", "blue", new(4, 4), UnitId: "other", GroupId: "b"));
        var ready = Ready(State(definition));
        var result = GameEngine.Advance(ready.State, new OrderAwareAutomatedProvider(), new Dice(), false);
        Assert.Equal(1, result.State.OrderFor(new("guard", "blue", "a")).Route!.CurrentIndex);
        Assert.Equal(0, result.State.OrderFor(new("guard", "blue", "b")).Route!.CurrentIndex);
        var blocked = State(Definition(start: new(10, 4)));
        for (var y = 0; y < 12; y++) blocked.Physical.Board.Edges.Add(new(new(7, y), new(8, y), EdgeKind.Wall));
        Assert.Equal("stay", Choose(blocked));
        Assert.Equal(Home, blocked.OrderFor(blocked.Units[0].Token).Area);
    }

    [Fact]
    public void PartialActivationTypeOrderStillIncludesOtherLivingTypes()
    {
        var state = GameEngine.CreateGame(Definition()); state.ActivationTypeOrder = ["guard"];
        var result = GameEngine.StartRound(state, new Dice(), false);
        Assert.Equal("guard", result.State.ActiveToken!.TypeId);
        Assert.Contains(new ActivationToken("target", "red"), result.State.Bag);
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void LargeActorAndTargetRespectWholeFootprintLeash(int targetX, bool suitable)
    {
        var area = new ObjectiveArea(0, 3, 3, 4);
        var definition = Definition(new("guard", 4, 1, 2, 0, 1) { Footprint = Footprint.TwoByTwo },
            new(3, 4), new(targetX, 4), area);
        definition.UnitTypes![1] = definition.UnitTypes[1] with { Footprint = Footprint.TwoByTwo };
        var state = State(definition);
        var queries = new GameplayQueries(state);
        Assert.Equal(targetX + 1 - 2, queries.FootprintDistanceToArea("enemy", new(targetX, 4), area));
        Assert.Equal(suitable, AreaOrderBehavior.Threats(queries, "guard", area).Count > 0);
        var destination = Destination(state);
        Assert.True(queries.FootprintDistanceToArea("guard", destination, area) <= AreaOrderBehavior.Leash(queries, "guard"));
        Assert.Equal(suitable, destination.X > 3);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void CachedReturnFieldMatchesIndependentForwardRoutes(bool large, bool phase, bool fog)
    {
        var type = new UnitType("guard", 3, 1, 1, 0, 1)
        {
            Footprint = large ? Footprint.TwoByTwo : Footprint.OneByOne,
            LocalAbilities = phase ? [new("phase", "Phase", AbilityTiming.Passive, new Phase())] : []
        };
        var state = State(Definition(type, new(10, 4)));
        for (var y = 0; y < 12; y++) state.Physical.Board.Edges.Add(new(new(7, y), new(8, y), y is 4 or 5 ? EdgeKind.ClosedDoor : EdgeKind.Wall));
        if (fog)
        {
            state.FogOfWarSides = ["blue"];
            state.ExploredCells["blue"] = Enumerable.Range(2, 7).SelectMany(y => Enumerable.Range(3, 11).Select(x => new Cell(x, y))).ToHashSet();
        }
        var queries = new GameplayQueries(state);
        var positions = MovementRules.FindPaths(state, "guard", new(10, 4), 3).Keys.ToArray();
        foreach (var closed in new[] { false, true })
        foreach (var position in positions.Reverse())
        {
            var traversal = MovementTraversal.For(state, "guard", closed);
            var forward = ApproachRules.Distances(state.Physical.Board, position, traversal: traversal, footprint: type.Footprint);
            var expected = forward.Where(p => FootprintGeometry.OccupiedCells(type.Footprint, p.Key).All(c => RouteRules.Contains(Home, c)) &&
                    SpatialRules.Fits(state.Physical.Board, type.Footprint, p.Key))
                .Select(p => (int?)p.Value).Min();
            Assert.Equal(expected, queries.WholeFootprintRouteDistanceToArea("guard", position, Home, closed));
        }
        // A separate detached snapshot must not reuse the previous board's distance field.
        state.Physical.Board.Edges.Clear();
        Assert.NotNull(new GameplayQueries(state).WholeFootprintRouteDistanceToArea("guard", new(10, 4), Home));
    }
}
