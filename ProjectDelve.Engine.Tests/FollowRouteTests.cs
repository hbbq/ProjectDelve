using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class FollowRouteTests
{
    private sealed class Dice : IRandomProvider
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
    private static readonly ActivationToken Token = new("runner", "blue");
    private static FollowRoute Route(RouteMode mode = RouteMode.Once) => new([new(2, 0, 1, 3), new(5, 0, 2, 3)], mode);
    internal static ScenarioDefinition Fixture(FollowRoute? route = null, bool multiple = false, UnitType? type = null) => new()
    {
        Board = new() { Width = 8, Height = 3, DefaultTerrain = TerrainKind.Asphalt },
        UnitTypeIds = ["runner"], UnitTypes = [type ?? UnitRoster.Zombie("runner") with { Mov = 6 }],
        Units = multiple ? [new("runner", "blue", new(0, 0), UnitId: "a"), new("runner", "blue", new(0, 2), UnitId: "b")]
            : [new("runner", "blue", new(0, 1), UnitId: "a")],
        Agency = [new("runner", "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        Orders = [new("runner", "blue", OrderKind.FollowRoute, Route: route ?? Route())]
    };
    private static EngineResult Start(ScenarioDefinition definition) => GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);
    private static EngineResult Step(EngineResult result, string? key) => GameEngine.Advance(result.State, new Pick(key), new Dice(), false);
    private static string? Choose(EngineResult result) => new OrderAwareAutomatedProvider().Choose(result.NextInput!, new GameplayQueries(result.State));

    [Fact]
    public void ValidationSerializationAndDetachedProgress()
    {
        var route = Route(RouteMode.PingPong) with { CurrentIndex = 1, Direction = -1 };
        var state = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(Fixture(route))));
        Assert.Equal(route.Waypoints.ToArray(), state.Orders[0].Route!.Waypoints.ToArray());
        Assert.Equal(-1, state.Orders[0].Route!.Direction);
        var copy = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        copy.AssignOrder(Token, OrderKind.Normal);
        Assert.Single(state.Orders);
        foreach (var invalid in new[] { route with { Waypoints = [] }, route with { CurrentIndex = 2 },
            route with { Direction = 0 }, route with { Mode = (RouteMode)99 },
            route with { Waypoints = [new(-1, 0, 1, 1)] }, route with { Waypoints = [new(0, 0, 0, 1)] },
            route with { Waypoints = [new(7, 0, 2, 1)] }, route with { Waypoints = [new(0, 0, int.MaxValue, 1)] } })
            Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Fixture(invalid)));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(Token, OrderKind.FollowRoute));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(Token, OrderKind.FollowRoute, new(1, 1), route: route));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(Token, OrderKind.HoldPosition, route: route));
    }

    [Theory]
    [InlineData(RouteMode.Once, new[] { 0, 1, -1 })]
    [InlineData(RouteMode.Loop, new[] { 0, 1, 0, 1, 0 })]
    [InlineData(RouteMode.PingPong, new[] { 0, 1, 0, 1, 0 })]
    public void TraversalModes(RouteMode mode, int[] expected)
    {
        FollowRoute? route = Route(mode);
        foreach (var index in expected) { Assert.Equal(index, route?.CurrentIndex ?? -1); if (route is not null) route = RouteRules.Next(route); }
    }

    [Fact]
    public void PingPongReversesWithoutRepeatingEndpointsAndSingleAreaPersists()
    {
        FollowRoute? route = new([new(0, 0, 1, 1), new(1, 0, 1, 1), new(2, 0, 1, 1)], RouteMode.PingPong);
        foreach (var index in new[] { 0, 1, 2, 1, 0, 1 }) { Assert.Equal(index, route!.CurrentIndex); route = RouteRules.Next(route); }
        foreach (var mode in new[] { RouteMode.Loop, RouteMode.PingPong })
        {
            var single = new FollowRoute([new(0, 0, 1, 1)], mode);
            Assert.Equal(single, RouteRules.Next(single));
        }
    }

    [Fact]
    public void OnlyOrdinaryEndpointCountsAndStayCompletesOnce()
    {
        var result = Step(Start(Fixture()), "6,1"); // Pass through both areas, finish only in the second.
        Assert.Equal(0, result.State.Orders[0].Route!.CurrentIndex);
        result = Step(Start(Fixture()), "2,1");
        Assert.Equal(1, result.State.Orders[0].Route!.CurrentIndex);
        Assert.Single(result.Events, e => e.Kind == "RouteAdvanced");
        var overlap = new FollowRoute([new(0, 0, 8, 3), new(0, 0, 8, 3)], RouteMode.Once);
        result = Step(Start(Fixture(overlap)), "stay");
        Assert.Equal(1, result.State.Orders[0].Route!.CurrentIndex); // At most one per Move, even overlapping.
        var terminal = overlap with { CurrentIndex = 1 };
        result = Step(Start(Fixture(terminal)), "stay");
        Assert.Empty(result.State.Orders);
        Assert.Contains(result.Events, e => e.Kind == "RouteCompleted");
    }

    [Fact]
    public void SharedProgressUpdatesBeforeTheNextUnitsDecision()
    {
        var result = Step(Start(Fixture(multiple: true)), "a");
        Assert.Equal("2,0", Choose(result));
        result = Step(result, "2,0");
        if (result.NextInput?.UnitId == "a") result = Step(result, "end");
        if (result.NextInput?.Kind == DecisionKind.SelectUnit) result = Step(result, "b");
        Assert.Equal(1, result.State.Orders[0].Route!.CurrentIndex);
        var choice = result.NextInput!.Candidates.Single(c => c.Key == Choose(result));
        Assert.True(RouteRules.Contains(Route().Waypoints[1], choice.Destination!));
        result = Step(result, choice.Key);
        Assert.Empty(result.State.Orders);
    }

    [Theory]
    [InlineData(RouteMode.Once, 0, 1)]
    [InlineData(RouteMode.Loop, 1, 1)]
    [InlineData(RouteMode.PingPong, 1, 1)]
    [InlineData(RouteMode.PingPong, 1, -1)]
    public void LookaheadFollowsTheActualSequence(RouteMode mode, int index, int direction)
    {
        var route = new FollowRoute([new(0, 0, 2, 3), new(3, 0, 2, 3)], mode, index, direction);
        var type = UnitRoster.Zombie("runner") with { Mov = 6, Behaviors = UnitBehavior.RouteLookahead };
        var definition = Fixture(route, type: type) with { Units = [new("runner", "blue", new(2, 1), UnitId: "a")] };
        var result = Start(definition);
        var chosen = result.NextInput!.Candidates.Single(c => c.Key == Choose(result));
        // At index 0, exit toward the second area; at the far endpoint, return toward the first.
        Assert.Equal(index == 0 ? new Cell(1, 1) : new Cell(3, 1), chosen.Destination);
    }

    [Fact]
    public void TerminalOnceUsesShortestMoveThenBoardOrder()
    {
        var route = new FollowRoute([new(2, 0, 1, 3)], RouteMode.Once);
        var result = Start(Fixture(route, type: UnitRoster.Zombie("runner") with { Behaviors = UnitBehavior.RouteLookahead }));
        Assert.Equal("2,1", Choose(result));
        Assert.Equal(Choose(result), Choose(result));
    }

    [Fact]
    public void LookaheadUsesTraversableDistanceAndCanonicalTies()
    {
        var route = new FollowRoute([new(3, 1, 1, 3), new(7, 2, 1, 1)], RouteMode.Loop);
        var definition = Fixture(route, type: UnitRoster.Zombie("runner") with
            { Mov = 5, Behaviors = UnitBehavior.RouteLookahead }) with
        {
            Board = new() { Width = 8, Height = 5, Cells = [new(new(4, 2), TerrainKind.Water)] },
            Units = [new("runner", "blue", new(0, 2), UnitId: "a")]
        };
        // Reaching (3,2) takes fewer steps but needs a detour to the next area.
        // The equally good northern/southern exit lines use deterministic top-left order.
        Assert.Equal("3,1", Choose(Start(definition)));
        var ordinary = definition with { UnitTypes = [definition.UnitTypes![0] with { Behaviors = UnitBehavior.None }] };
        Assert.Equal("3,2", Choose(Start(ordinary)));
    }

    [Fact]
    public void UnreachableRouteStaysAndClosedDoorPlanningRetainsZombieConvention()
    {
        var definition = Fixture(new([new(7, 1, 1, 1)])) with
        {
            Board = new() { Width = 8, Height = 1, Edges = [new(new(2, 0), EdgeDirection.Right, EdgeKind.ClosedDoor)] },
            Units = [new("runner", "blue", new(0, 0), UnitId: "a")],
            Orders = [new("runner", "blue", OrderKind.FollowRoute, Route: new([new(7, 0, 1, 1)]))]
        };
        Assert.Equal("2,0", Choose(Start(definition)));
        Assert.Equal("stay", Choose(Start(definition with
            { UnitTypes = [definition.UnitTypes![0] with { Behaviors = UnitBehavior.None }] })));
    }

    [Fact]
    public void WorkersRetainTheirEstablishedOrderInterpretation()
    {
        var definition = Fixture(type: UnitRoster.Worker("runner")) with
        {
            Resources = [new(ResourceTypes.Coal, new(0, 1))],
            Machines = [new("press", new(0, 0), MachineTypes.DiamondPress)]
        };
        var result = Start(definition);
        var queries = new GameplayQueries(result.State);
        Assert.Equal(new DefaultAutomatedProvider().Choose(result.NextInput!, queries), Choose(result));
        var idle = Start(definition with { Orders = [new("runner", "blue", OrderKind.DoNothing)] });
        Assert.Equal("stay", Choose(idle));
    }

    [Fact]
    public void PlanningIgnoresPeelButActualMoveStopsAndSlipsAtIt()
    {
        var route = new FollowRoute([new(7, 1, 1, 1)]);
        var definition = Fixture(route) with
        {
            Board = new() { Width = 8, Height = 1, DefaultTerrain = TerrainKind.Asphalt },
            Units = [new("runner", "blue", new(0, 0), UnitId: "a")],
            Orders = [new("runner", "blue", OrderKind.FollowRoute, Route: new([new(7, 0, 1, 1)]))],
            CellMarkers = [new(CellMarkerTypes.BananaPeel, new(2, 0))]
        };
        var result = Start(definition);
        Assert.Equal("2,0", Choose(result));
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Destination == new Cell(3, 0));
        result = Step(result, "2,0");
        Assert.Equal(Posture.Lying, result.State.Physical.Figures.Single().Posture);
        Assert.Empty(result.State.CellMarkers);
        Assert.Equal(0, result.State.Orders[0].Route!.CurrentIndex);
    }

    [Fact]
    public void LargeUnitDestinationOverlapCountsButMoveAfterAttackDoesNot()
    {
        var definition = Fixture(new([new(2, 1, 1, 1)]), type: UnitRoster.RedDragon("runner") with { Mov = 2 }) with
            { Units = [new("runner", "blue", new(0, 0), UnitId: "a")] };
        var result = Step(Start(definition), "1,0");
        Assert.Empty(result.State.Orders);
        var state = GameEngine.CreateGame(Fixture(new([new(1, 1, 1, 1)])));
        state.ActiveToken = Token;
        state.CurrentUnitId = "a";
        state.MoveAfterAttackAllowance = 1;
        state.MoveDone = true;
        // Resolve the real post-Attack opportunity through the public continuation boundary.
        state.Pending = new(DecisionKind.Move, "runner", "a", [new("1,1", new(1, 1), [new(0, 1), new(1, 1)])], true, IsMoveAfterAttack: true) { Token = Token };
        result = GameEngine.Advance(state, new Pick("1,1"), new Dice(), false);
        Assert.Single(result.State.Orders);
    }

    [Fact]
    public void SlippingAtWaypointAdvancesOnceBeforeEndingActivation()
    {
        var definition = Fixture(new([new(2, 1, 1, 1), new(2, 1, 1, 1)], RouteMode.Loop)) with
            { CellMarkers = [new(CellMarkerTypes.BananaPeel, new(2, 1))] };
        var result = Step(Start(definition), "2,1");
        Assert.Equal(1, result.State.Orders.Single().Route!.CurrentIndex);
        Assert.Single(result.Events, e => e.Kind == "RouteAdvanced");
        Assert.Contains(result.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "a");
        Assert.False(result.State.IsUpright("a"));
    }
}
