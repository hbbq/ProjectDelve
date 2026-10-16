using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorkerBehaviorTests
{
    private sealed class Dice(string? preferredSide = null) : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "porter" && (preferredSide is null || t.SideId == preferredSide)) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private static Machine Press(Cell cell, int coal = 0, string id = "press") => new(id, cell, MachineTypes.DiamondPress)
        { StoredResources = [new(ResourceTypes.Coal, coal)] };
    private static ScenarioDefinition Fixture() => new()
    {
        Board = new() { Width = 8, Height = 5 },
        UnitTypeIds = ["porter", UnitTypeIds.Geomancer, UnitTypeIds.Grunt],
        // Deliberately a different Unit Type, with an Attack that the Behavior must ignore.
        UnitTypes = [UnitRoster.Worker("porter") with { Actions = UnitAction.NormalAttack | UnitAction.GiveResource | UnitAction.Operate }],
        Units = [new("porter", "blue", new(0, 2), UnitId: "actor"),
            new(UnitTypeIds.Geomancer, "blue", new(7, 2), UnitId: "collector"),
            new(UnitTypeIds.Grunt, "red", new(7, 4), UnitId: "observer")],
        Agency = [new("porter", "blue", ControllerKind.Automated), new(UnitTypeIds.Geomancer, "blue", ControllerKind.Human),
            new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        Orders = [new(UnitTypeIds.Grunt, "red", OrderKind.DoNothing)],
        Machines = [Press(new(4, 2))]
    };
    private static EngineResult Start(ScenarioDefinition definition) =>
        GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(), false);
    private static string? Choice(EngineResult result) => new OrderAwareAutomatedProvider().Choose(result.NextInput!, new GameplayQueries(result.State));
    private static EngineResult Step(EngineResult result) => GameEngine.Advance(result.State, new OrderAwareAutomatedProvider(), new Dice(), false);
    private static (GameState State, List<RulesEvent> Events) Activate(ScenarioDefinition definition)
    {
        var result = Start(definition);
        List<RulesEvent> events = [.. result.Events];
        for (var i = 0; i < 40 && !events.Any(e => e.Kind == "ActivationCompleted" && e.UnitId == "actor"); i++)
        {
            result = Step(result);
            events.AddRange(result.Events);
        }
        Assert.Contains(events, e => e.Kind == "ActivationCompleted" && e.UnitId == "actor");
        return (result.State, events);
    }

    [Fact]
    public void CanonicalWorkerDeclaresStatsAndIndependentMechanics()
    {
        var worker = CanonicalUnitTypes.Find(UnitTypeIds.Worker)!;
        Assert.Equal((4, 0, 0, 2, 1), (worker.Mov, worker.Rng, worker.Atk, worker.Def, worker.Hp));
        Assert.Equal(UnitAction.GiveResource | UnitAction.Operate, worker.Actions);
        Assert.NotNull(worker.TransportResource);
        Assert.Null(worker.ResourceStorage);
        Assert.Equal(UnitBehavior.Worker, worker.Behaviors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeliversToMachineOrFriendlyStorage(bool diamond)
    {
        var definition = Fixture() with { Units = [Fixture().Units[0], Fixture().Units[1] with { Anchor = new(3, 4) }, Fixture().Units[2]], Resources = [new(diamond ? ResourceTypes.Diamond : ResourceTypes.Coal, new(0, 2))] };
        var (state, events) = Activate(definition);
        Assert.Empty(state.Resources);
        Assert.Contains(events, e => e.Kind == "ResourceStored" && e.UnitId == "actor");
        Assert.Equal(diamond ? 0 : 1, state.Physical.Board.Machines[0].StoredQuantity("coal"));
        Assert.Equal(diamond ? 1 : 0, state.StoredQuantity("blue", UnitTypeIds.Geomancer, "diamond"));
    }

    [Fact]
    public void DeliverableCargoIsNotAbandonedOnAnotherResource()
    {
        var start = Start(Fixture() with { Resources = [new(ResourceTypes.Coal, new(0, 2)),
            new(ResourceTypes.Diamond, new(3, 1)), new(ResourceTypes.Diamond, new(3, 2)),
            new(ResourceTypes.Diamond, new(3, 3))] });
        var selected = start.NextInput!.Candidates.Single(c => c.Key == Choice(start));
        Assert.DoesNotContain(start.State.Resources, r => r.Position == selected.Destination);
        var definition = Fixture();
        var (state, _) = Activate(definition with { UnitTypes = [definition.UnitTypes![0] with { Mov = 6 }], Resources = start.State.Resources });
        Assert.Equal(1, state.Physical.Board.Machines[0].StoredQuantity("coal"));
        Assert.Equal(3, state.Resources.Count);
    }

    [Fact]
    public void OperatesReadyAdjacentMachineBeforeSeekingResources()
    {
        var (state, events) = Activate(Fixture() with { Machines = [Press(new(1, 2), 3)],
            Resources = [new(ResourceTypes.Diamond, new(0, 0))] });
        Assert.Contains(events, e => e.Kind == "MachineOperated");
        Assert.Equal(0, state.Physical.Board.Machines[0].StoredQuantity("coal"));
        Assert.Contains(state.Resources, r => r.Position == new Cell(0, 2) && r.Type == ResourceTypes.Diamond);
    }

    [Fact]
    public void SeeksNearestUsefulResourceWithTopLeftTieBreak()
    {
        var definition = Fixture() with { Resources = [new(ResourceTypes.Coal, new(0, 3)),
            new(ResourceTypes.Coal, new(0, 1)), new(new("junk", "Junk"), new(1, 2))] };
        var start = Start(definition);
        Assert.Equal(new Cell(0, 1), start.NextInput!.Candidates.Single(c => c.Key == Choice(start)).Destination);
        Assert.Equal(Choice(start), Choice(start));
    }

    [Fact]
    public void UnusableCargoCanBeReplacedAndRemainsOnItsOriginalCell()
    {
        var junk = new ResourceType("junk", "Junk");
        var result = Step(Start(Fixture() with { Resources = [new(junk, new(0, 2)), new(ResourceTypes.Coal, new(1, 2))] }));
        Assert.Equal(new Cell(1, 2), result.State.Physical.Figures.Single(f => f.Id == "actor").Position);
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(0, 2) && r.Type == junk);
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(1, 2) && r.Type == ResourceTypes.Coal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoresResourcesWithOccupiedOrBlockedDestinations(bool hostile)
    {
        var definition = Fixture();
        definition = definition with { UnitTypeIds = definition.UnitTypeIds,
            Units = [.. definition.Units, new(UnitTypeIds.Grunt, hostile ? "red" : "blue", new(1, 2))],
            Agency = hostile ? definition.Agency : [.. definition.Agency, new(UnitTypeIds.Grunt, "blue", ControllerKind.Human)],
            Resources = [new(ResourceTypes.Coal, new(1, 2)), new(ResourceTypes.Coal, new(0, 4))] };
        var start = Start(definition);
        Assert.Equal(new Cell(0, 4), start.NextInput!.Candidates.Single(c => c.Key == Choice(start)).Destination);
    }

    [Fact]
    public void InaccessibleRecipientsMakeCargoUselessAndBlockedResourcesAreIgnored()
    {
        var definition = Fixture() with { Board = new() { Width = 8, Height = 5,
            Edges = Enumerable.Range(0, 5).Select(y => new EdgeDefinition(new(2, y), EdgeDirection.Right, EdgeKind.Wall)).ToList() },
            Resources = [new(ResourceTypes.Coal, new(0, 2)), new(ResourceTypes.Diamond, new(4, 0))] };
        Assert.Equal("stay", Choice(Start(definition)));
        Assert.DoesNotContain(Activate(definition).Events, e => e.Kind is "ResourceStored" or "MachineOperated");
    }

    [Fact]
    public void SeeksReadyMachineWhenThereIsNoUsefulResource()
    {
        var (state, events) = Activate(Fixture() with { Machines = [Press(new(6, 2), 3)] });
        Assert.Equal(new Cell(4, 2), state.Physical.Figures.Single(f => f.Id == "actor").Position);
        Assert.DoesNotContain(events, e => e.Kind == "MachineOperated");
    }

    [Fact]
    public void UselessResourcesAndUnavailableOperationsLeadToStayAndSkip()
    {
        var definition = Fixture() with { Machines = [], Resources = [new(ResourceTypes.Coal, new(0, 2))] };
        var (state, events) = Activate(definition);
        Assert.Equal(new Cell(0, 2), state.Physical.Figures.Single(f => f.Id == "actor").Position);
        Assert.DoesNotContain(events, e => e.Kind == "ActionUsed");
    }

    [Fact]
    public void MultipleWorkersReevaluateCapacityAndCompleteEntireActivationsInSequence()
    {
        var definition = Fixture();
        definition = definition with { Machines = [Press(new(2, 2), 2)],
            Units = [definition.Units[0] with { Anchor = new(1, 1) },
                new("porter", "blue", new(1, 3), UnitId: "second"), definition.Units[1], definition.Units[2]],
            Resources = [new(ResourceTypes.Coal, new(1, 1)), new(ResourceTypes.Coal, new(1, 3))] };
        var result = Start(definition);
        List<RulesEvent> events = [.. result.Events];
        for (var i = 0; i < 30 && !events.Any(e => e.Kind == "ActivationCompleted" && e.UnitId == "second"); i++)
        {
            result = Step(result);
            events.AddRange(result.Events);
            if (result.State.CurrentUnitId == "second")
                Assert.Contains(events, e => e.Kind == "ActivationCompleted" && e.UnitId == "actor");
        }
        Assert.Equal(3, result.State.Physical.Board.Machines[0].StoredQuantity("coal"));
        Assert.Single(events.Where(e => e.Kind == "ResourceStored"));
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(1, 3));
        Assert.Contains(events, e => e.Kind == "ActivationCompleted" && e.UnitId == "second");
    }

    [Fact]
    public void ProductionCycleEmergesAcrossRoundsWithoutSpecialDecisions()
    {
        var definition = Fixture();
        definition = definition with { Units = [definition.Units[0],
                new("porter", "blue", new(0, 0), UnitId: "second"),
                new("porter", "blue", new(0, 4), UnitId: "third"), definition.Units[1], definition.Units[2]],
            Resources = [new(ResourceTypes.Coal, new(1, 0)), new(ResourceTypes.Coal, new(1, 2)),
                new(ResourceTypes.Coal, new(1, 4))] };
        var state = GameEngine.CreateGame(definition);
        List<RulesEvent> events = [];
        for (var round = 0; round < 12 && state.StoredQuantity("blue", UnitTypeIds.Geomancer, "diamond") == 0; round++)
        {
            var result = GameEngine.StartRound(state, new Dice(), false);
            events.AddRange(result.Events);
            for (var step = 0; step < 80 && !result.State.RoundComplete; step++)
            {
                result = Step(result);
                events.AddRange(result.Events);
            }
            Assert.True(result.State.RoundComplete);
            state = result.State;
        }
        Assert.Equal(1, state.StoredQuantity("blue", UnitTypeIds.Geomancer, "diamond"));
        Assert.Equal(3, events.Count(e => e.Kind == "ResourceStored" && e.MachineId == "press"));
        Assert.Single(events.Where(e => e.Kind == "MachineOperated"));
        Assert.Contains(events, e => e.Kind == "ResourceStored" && e.TargetId == "collector" &&
            e.ResourceType == ResourceTypes.Diamond);
        Assert.Empty(state.Resources);
    }

    [Fact]
    public void BehaviorDoesNotGrantMissingResourceActionsOrTransport()
    {
        var definition = Fixture();
        var start = Start(definition with { UnitTypes = [definition.UnitTypes![0] with {
            Actions = UnitAction.None, TransportResource = null }],
            Machines = [Press(new(1, 2), 3)], Resources = [new(ResourceTypes.Coal, new(0, 1))] });
        Assert.Equal("stay", Choice(start));
    }

    [Theory]
    [InlineData("blue", true)]
    [InlineData("red", false)]
    public void UnitDeliveryUsesActualGiveLegalityIncludingSide(string recipientSide, bool canGive)
    {
        var definition = Fixture();
        definition = definition with {
            Units = [definition.Units[0], definition.Units[1] with { SideId = recipientSide, Anchor = new(1, 2) }, definition.Units[2]],
            Agency = [definition.Agency[0], new(UnitTypeIds.Geomancer, recipientSide, ControllerKind.Human), definition.Agency[2]],
            Resources = [new(ResourceTypes.Diamond, new(0, 2))] };
        var start = Step(Start(definition)); // Stay before the ordinary Action.
        Assert.Equal(canGive, start.NextInput!.Candidates.Any(c => c.TargetId == "collector" && c.Action == UnitAction.GiveResource));
        var (state, events) = Activate(definition);
        Assert.Equal(canGive ? 1 : 0, state.StoredQuantity(recipientSide, UnitTypeIds.Geomancer, "diamond"));
        Assert.Equal(canGive, events.Any(e => e.Kind == "ResourceStored"));
        Assert.Equal(canGive ? 0 : 1, state.Resources.Count);
    }

    [Theory]
    [InlineData("blue", "actor")]
    [InlineData("red", "red-worker")]
    public void DifferentSidesSupplyAndOperateTheSameNeutralMachine(string operatorSide, string operatorId)
    {
        var definition = Fixture();
        definition = definition with {
            Machines = [Press(new(3, 2), 1)],
            Units = [definition.Units[0] with { Anchor = new(2, 1) },
                new("porter", "red", new(2, 3), UnitId: "red-worker"), definition.Units[1], definition.Units[2]],
            Agency = [.. definition.Agency, new("porter", "red", ControllerKind.Automated)],
            Resources = [new(ResourceTypes.Coal, new(2, 1)), new(ResourceTypes.Coal, new(2, 3))] };
        var state = GameEngine.CreateGame(definition);
        List<RulesEvent> events = [];
        for (var round = 0; round < 2; round++)
        {
            var result = GameEngine.StartRound(state, new Dice(operatorSide), false);
            for (var step = 0; step < 60 && !result.State.RoundComplete; step++)
            {
                result = GameEngine.Advance(result.State, new OrderAwareAutomatedProvider(), new Dice(operatorSide), false);
                events.AddRange(result.Events);
            }
            Assert.True(result.State.RoundComplete);
            state = result.State;
        }
        Assert.Contains(events, e => e.Kind == "ResourceStored" && e.MachineId == "press" && e.UnitId == "actor");
        Assert.Contains(events, e => e.Kind == "ResourceStored" && e.MachineId == "press" && e.UnitId == "red-worker");
        Assert.Contains(events, e => e.Kind == "MachineOperated" && e.MachineId == "press" && e.UnitId == operatorId);
        Assert.Equal(0, state.Physical.Board.Machines[0].StoredQuantity("coal"));
        Assert.Contains(state.Resources, r => r.Type == ResourceTypes.Diamond);
    }

    [Fact]
    public void ClosedDoorAndOccupiedOperatingCellsDoNotCreateWorkLegality()
    {
        var definition = Fixture() with { Board = new() { Width = 8, Height = 1,
            Edges = [new(new(2, 0), EdgeDirection.Right, EdgeKind.ClosedDoor)] },
            Machines = [Press(new(3, 0), 3)], Units = [new("porter", "blue", new(0, 0), UnitId: "actor"),
                new(UnitTypeIds.Geomancer, "blue", new(5, 0), UnitId: "collector"),
                new(UnitTypeIds.Grunt, "red", new(7, 0), UnitId: "observer")] };
        Assert.Equal("stay", Choice(Start(definition)));
        Assert.DoesNotContain(Activate(definition).Events, e => e.Kind == "MachineOperated");
    }

    [Fact]
    public void NearestResourceUsesTraversablePathLengthRatherThanGeometricDistance()
    {
        var definition = Fixture() with { Board = new() { Width = 8, Height = 5,
            Edges = [new(new(0, 2), EdgeDirection.Down, EdgeKind.Wall)] },
            Resources = [new(ResourceTypes.Coal, new(0, 3)), new(ResourceTypes.Coal, new(0, 0))] };
        var start = Start(definition);
        Assert.Equal(new Cell(0, 0), start.NextInput!.Candidates.Single(c => c.Key == Choice(start)).Destination);
    }

    [Fact]
    public void ReadyMachinesUseTopLeftTargetBeforeStableIdentity()
    {
        var definition = Fixture() with { Machines = [Press(new(1, 3), 3, "a"), Press(new(1, 1), 3, "z")] };
        var start = Step(Start(definition));
        Assert.Equal("operate:z", Choice(start));
    }

    [Fact]
    public void FullRecipientStoreMakesCargoReplaceable()
    {
        var definition = Fixture() with { Resources = [new(ResourceTypes.Diamond, new(0, 2)),
            new(ResourceTypes.Coal, new(1, 2))],
            StoredResources = [new("blue", UnitTypeIds.Geomancer, ResourceTypes.Diamond, 1)] };
        var result = Step(Start(definition));
        Assert.Equal(new Cell(1, 2), result.State.Physical.Figures.Single(f => f.Id == "actor").Position);
        Assert.Contains(result.State.Resources, r => r.Type == ResourceTypes.Diamond && r.Position == new Cell(0, 2));
    }

    [Fact]
    public void WorkerPolicySkipsEvenWhenAnAttackIsLegal()
    {
        var definition = Fixture();
        var start = Step(Start(definition with { Machines = [],
            UnitTypes = [definition.UnitTypes![0] with { Rng = 1, Atk = 2 }],
            Units = [definition.Units[0], definition.Units[1], definition.Units[2] with { Anchor = new(1, 2) }] }));
        Assert.Contains(start.NextInput!.Candidates, c => c.Action == UnitAction.NormalAttack);
        Assert.Equal("end-turn", Choice(start));
    }

    [Fact]
    public void ReusedBehaviorCanSeekResourceForLegalGiveWithoutTransportCapability()
    {
        var definition = Fixture();
        var (state, events) = Activate(definition with { UnitTypes = [definition.UnitTypes![0] with { TransportResource = null }],
            Machines = [Press(new(2, 2))], Resources = [new(ResourceTypes.Coal, new(1, 2))] });
        Assert.Contains(events, e => e.Kind == "ResourceStored");
        Assert.Equal(1, state.Physical.Board.Machines[0].StoredQuantity("coal"));
    }

    [Theory]
    [InlineData(OrderKind.Normal)]
    [InlineData(OrderKind.DoNothing)]
    [InlineData(OrderKind.HoldPosition)]
    [InlineData(OrderKind.MoveTo)]
    [InlineData(OrderKind.Attack)]
    [InlineData(OrderKind.Protect)]
    [InlineData(OrderKind.Retreat)]
    [InlineData(OrderKind.InteractWith)]
    public void OnlyDoNothingSuppressesWork(OrderKind kind)
    {
        var start = Start(Fixture() with { Resources = [new(ResourceTypes.Coal, new(0, 2))] });
        var order = new GroupOrder(new("porter", "blue"), kind, Destination: kind == OrderKind.MoveTo ? new(0, 0) : null,
            Target: kind switch { OrderKind.Attack => OrderTarget.ForSide("red"),
                OrderKind.Protect => OrderTarget.ForUnit("collector"),
                OrderKind.InteractWith => OrderTarget.ForDoor(new(0, 0), new(1, 0)), _ => null });
        start.State.Orders.Add(order);
        Assert.Equal(kind == OrderKind.DoNothing ? "stay" : Choice(Start(Fixture() with {
            Resources = [new(ResourceTypes.Coal, new(0, 2))] })), Choice(start));
    }
}
