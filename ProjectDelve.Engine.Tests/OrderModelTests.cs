using System.Text.Json;
using Xunit;
using static ProjectDelve.Engine.Scenario;

namespace ProjectDelve.Engine.Tests;

public sealed class OrderModelTests
{
    private static ScenarioDefinition Definition(params InitialGroupOrder[] orders) => Define(
        Map(6, 3, edges: [Edge(2, 1, EdgeDirection.Right, EdgeKind.ClosedDoor)]),
        [
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(0, 1, unitId: "actor")),
            Group(UnitTypeIds.Goblin, "red", ControllerKind.Human, At(1, 2, unitId: "ally")),
            Group(UnitTypeIds.Rogue, "blue", ControllerKind.Human, At(5, 1, unitId: "hero")),
            Group(UnitTypeIds.SkeletonArcher, "blue", ControllerKind.Human)
        ], orders: orders);

    [Fact]
    public void AuthoredIdsAreCanonicalAndGeneratedIdsAvoidAllAuthoredCollisions()
    {
        var definition = Define(Map(5, 2), [
            Group(UnitTypeIds.Grunt, "red", ControllerKind.Human,
                At(0, 0), At(1, 0, unitId: "scenario-unit-1"), At(2, 0))
        ]);

        var state = GameEngine.CreateGame(definition);

        Assert.Equal(["scenario-unit-2", "scenario-unit-1", "scenario-unit-3"], state.Units.Select(unit => unit.Id));
        Assert.Equal(state.Units.Select(unit => unit.Id), state.Physical.Figures.Select(figure => figure.Id));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with
        {
            Units = [definition.Units[0] with { UnitId = "duplicate" }, definition.Units[1] with { UnitId = "duplicate" }]
        }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with
        {
            Units = [definition.Units[0] with { UnitId = " " }]
        }));
    }

    [Fact]
    public void SpecificUnitTargetsRoundTripCopyAndNeverRebind()
    {
        var definition = Definition(
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack, Target: OrderTarget.ForUnit("hero")),
            new(UnitTypeIds.Goblin, "red", OrderKind.Protect, Target: OrderTarget.ForUnit("ally")));
        var transported = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var state = GameEngine.CreateGame(transported);
        var attack = state.OrderFor(new(UnitTypeIds.Grunt, "red"));
        var protect = state.OrderFor(new(UnitTypeIds.Goblin, "red"));

        Assert.Equal(OrderTarget.ForUnit("hero"), attack.Target);
        Assert.Equal(OrderTarget.ForUnit("ally"), protect.Target);
        Assert.Equal(state.Orders, state.Copy().Orders);
        Assert.Equal(state.Orders, JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!.Orders);

        var hero = state.Units.Single(unit => unit.Id == "hero");
        state.Units[state.Units.IndexOf(hero)] = hero with { CurrentHp = 0 };
        state.Physical.Figures.RemoveAll(figure => figure.Id == "hero");
        state.PlaceUnit(UnitTypeIds.SkeletonArcher, "replacement", "blue", new(4, 0));
        Assert.Equal("hero", state.OrderFor(new(UnitTypeIds.Grunt, "red")).Target!.UnitId);
        GameEngine.ValidateScenario(state);
        Assert.Equal("hero", JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!
            .OrderFor(new(UnitTypeIds.Grunt, "red")).Target!.UnitId);
    }

    [Fact]
    public void GroupAndSideTargetsRemainDynamicScopes()
    {
        var definition = Definition(
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack,
                Target: OrderTarget.ForGroup(UnitTypeIds.SkeletonArcher, "blue")),
            new(UnitTypeIds.Goblin, "red", OrderKind.Attack, Target: OrderTarget.ForSide("blue")));
        var state = GameEngine.CreateGame(definition);

        Assert.Equal(new ActivationToken(UnitTypeIds.SkeletonArcher, "blue"),
            state.OrderFor(new(UnitTypeIds.Grunt, "red")).Target!.Group);
        Assert.Equal("blue", state.OrderFor(new(UnitTypeIds.Goblin, "red")).Target!.SideId);
        Assert.DoesNotContain(state.Units, unit => unit.TypeId == UnitTypeIds.SkeletonArcher);
        state.PlaceUnit(UnitTypeIds.SkeletonArcher, "later-summon", "blue", new(4, 0));
        Assert.Equal(new ActivationToken(UnitTypeIds.SkeletonArcher, "blue"),
            state.OrderFor(new(UnitTypeIds.Grunt, "red")).Target!.Group);
    }

    [Fact]
    public void DoorTargetsAreCanonicalAndTransportWithoutDoorState()
    {
        var target = OrderTarget.ForDoor(new(3, 1), new(2, 1));
        var definition = Definition(new InitialGroupOrder(UnitTypeIds.Grunt, "red", OrderKind.InteractWith, Target: target));
        var json = ScenarioDefinitionJson.ToJson(definition);
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(json));

        Assert.Equal(new DoorTarget(new(2, 1), new(3, 1)), target.Door);
        Assert.Equal(target, state.OrderFor(new(UnitTypeIds.Grunt, "red")).Target);
        Assert.DoesNotContain("ClosedDoor", JsonSerializer.Serialize(target));

        state.Physical.Board.Edges[0] = state.Physical.Board.Edges[0] with { Kind = EdgeKind.OpenDoor };
        GameEngine.ValidateScenario(state);
        Assert.Equal(target, JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!
            .OrderFor(new(UnitTypeIds.Grunt, "red")).Target);
    }

    [Fact]
    public void ScenarioRejectsInvalidTargetRelationshipsAndPayloads()
    {
        InitialGroupOrder[] invalid =
        [
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack, Target: OrderTarget.ForUnit("ally")),
            new(UnitTypeIds.Grunt, "red", OrderKind.Protect, Target: OrderTarget.ForUnit("hero")),
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack, Target: OrderTarget.ForUnit("missing")),
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack, Target: OrderTarget.ForGroup(UnitTypeIds.Grunt, "red")),
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack, Target: OrderTarget.ForGroup(UnitTypeIds.Goblin, "blue")),
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack, Target: OrderTarget.ForSide("red")),
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack, Target: OrderTarget.ForSide("missing")),
            new(UnitTypeIds.Grunt, "red", OrderKind.Attack),
            new(UnitTypeIds.Grunt, "red", OrderKind.Retreat, Target: OrderTarget.ForSide("blue")),
            new(UnitTypeIds.Grunt, "red", OrderKind.MoveTo, new(2, 0), OrderTarget.ForSide("blue")),
            new(UnitTypeIds.Grunt, "red", OrderKind.InteractWith,
                Target: new(OrderTargetKind.Door, Door: new(new(3, 1), new(2, 1)))),
            new(UnitTypeIds.Grunt, "red", OrderKind.InteractWith, Target: OrderTarget.ForDoor(new(0, 0), new(1, 0))),
            new(UnitTypeIds.Grunt, "red", OrderKind.Protect,
                Target: new(OrderTargetKind.Unit, UnitId: "ally", SideId: "red"))
        ];

        foreach (var order in invalid)
            Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Definition(order)));
    }

    [Fact]
    public void StateAssignmentAcceptsAllNewShapesAndRejectsMalformedOnes()
    {
        var state = GameEngine.CreateGame(Definition());
        var token = new ActivationToken(UnitTypeIds.Grunt, "red");
        state.AssignOrder(token, OrderKind.Attack, target: OrderTarget.ForUnit("hero"));
        state.AssignOrder(token, OrderKind.Protect, target: OrderTarget.ForUnit("ally"));
        state.AssignOrder(token, OrderKind.Retreat);
        state.AssignOrder(token, OrderKind.InteractWith, target: OrderTarget.ForDoor(new(2, 1), new(3, 1)));
        Assert.Equal(OrderKind.InteractWith, state.OrderFor(token).Kind);

        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Attack));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Retreat,
            target: OrderTarget.ForSide("blue")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Protect,
            target: OrderTarget.ForSide("red")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Attack,
            target: OrderTarget.ForUnit("ally")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Attack,
            target: OrderTarget.ForUnit("missing")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Protect,
            target: OrderTarget.ForUnit("hero")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Protect,
            target: OrderTarget.ForUnit("missing")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Attack,
            target: OrderTarget.ForGroup(UnitTypeIds.Grunt, "red")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Attack,
            target: OrderTarget.ForGroup(UnitTypeIds.Goblin, "blue")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Attack,
            target: OrderTarget.ForSide("red")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.Attack,
            target: OrderTarget.ForSide("missing")));
        Assert.Throws<ArgumentException>(() => state.AssignOrder(token, OrderKind.InteractWith,
            target: OrderTarget.ForDoor(new(0, 0), new(1, 0))));
    }
}