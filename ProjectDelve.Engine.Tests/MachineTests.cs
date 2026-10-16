using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MachineTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "worker") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static Machine Press(string id, Cell cell, int coal = 0) => new(id, cell, MachineTypes.DiamondPress)
        { StoredResources = [new(ResourceTypes.Coal, coal)] };
    private static ScenarioDefinition Fixture(int coal = 3) => new()
    {
        Board = new() { Width = 7, Height = 5 },
        Machines = [Press("press", new(2, 1), coal), Press("other-press", new(2, 2), 1)],
        UnitTypeIds = ["worker", UnitTypeIds.Geomancer, UnitTypeIds.Grunt],
        UnitTypes = [UnitType.Define("worker", "Worker fixture", UnitAuthoring.Stats(3, 0, 0, 1, 1),
            UnitAuthoring.CantAttack(), UnitAuthoring.GiveResource(), UnitAuthoring.Operate(), UnitAuthoring.TransportResource())],
        Units = [new("worker", "blue", new(1, 1), UnitId: "actor"),
            new(UnitTypeIds.Geomancer, "blue", new(0, 2), UnitId: "collector"),
            new(UnitTypeIds.Grunt, "red", new(6, 4))],
        Agency = [new("worker", "blue", ControllerKind.Human), new(UnitTypeIds.Geomancer, "blue", ControllerKind.Human),
            new(UnitTypeIds.Grunt, "red", ControllerKind.Human)]
    };
    private static EngineResult Start(ScenarioDefinition? definition = null) =>
        Ready(GameEngine.StartRound(GameEngine.CreateGame(definition ?? Fixture()), new Dice(), false));
    private static EngineResult Ready(EngineResult result) => result.State.CurrentUnitId == "actor" && !result.State.MoveDone ? Pick(result, "stay") : result;
    private static EngineResult Pick(EngineResult result, string key) => GameEngine.Advance(result.State, new Choice(key), new Dice(), false);
    private static Machine Machine(GameState state, string id = "press") => state.Physical.Board.Machines.Single(m => m.Id == id);

    [Fact]
    public void OperateConsumesOnlyTargetStoreAndOutputsAtomicallyAtOperator()
    {
        var start = Start();
        var result = Pick(start, "operate:press");
        Assert.Equal(3, Machine(start.State).StoredQuantity("coal"));
        Assert.Equal(0, Machine(result.State).StoredQuantity("coal"));
        Assert.Equal(1, Machine(result.State, "other-press").StoredQuantity("coal"));
        Assert.Equal(new Resource(ResourceTypes.Diamond, new(1, 1)), Assert.Single(result.State.Resources));
        Assert.Equal(1, result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "MachineOperated").StateAfter.ActionsUsedThisActivation);
        Assert.Contains(result.Events, e => e.Kind == "ActionUsed" && e.ActionId == "operate");
        var operated = Assert.Single(result.Events.Where(e => e.Kind == "MachineOperated"));
        Assert.Equal("press", operated.MachineId);
        Assert.Equal(new MachineResource(ResourceTypes.Coal, 3), Assert.Single(operated.ConsumedResources));
        foreach (var step in result.ResolutionSteps)
        {
            var after = Machine(step.StateAfter).StoredQuantity("coal") == 0;
            Assert.Equal(after, step.StateAfter.Resources.Any(r => r.Type == ResourceTypes.Diamond));
        }
        Assert.DoesNotContain(result.NextInput!.Candidates, c => c.Action == UnitAction.Operate);
        result.State.Physical.Board.Machines.Clear();
        Assert.Equal(2, result.ResolutionSteps.Last().StateAfter.Physical.Board.Machines.Count);
    }

    [Fact]
    public void GiveUsesIndividualCapacityAndOrdinaryActionWithoutUnitStorage()
    {
        var definition = Fixture(2) with { Resources = [new(ResourceTypes.Coal, new(1, 1))] };
        var start = Start(definition);
        Assert.Null(start.State.Types.Single(t => t.Id == "worker").ResourceStorage);
        Assert.Equal(2, start.NextInput!.Candidates.Count(c => c.Action == UnitAction.GiveResource));
        var given = Pick(start, "give-resource-machine:press");
        Assert.Empty(given.State.Resources);
        Assert.Equal(3, Machine(given.State).StoredQuantity("coal"));
        Assert.Equal(1, Machine(given.State, "other-press").StoredQuantity("coal"));
        Assert.Empty(given.State.StoredResources);
        Assert.Single(start.State.Resources);
        var step = given.ResolutionSteps.Single(s => given.Events[s.EventIndex].Kind == "ResourceStored");
        Assert.Empty(step.StateAfter.Resources);
        Assert.Equal(3, Machine(step.StateAfter).StoredQuantity("coal"));
        Assert.DoesNotContain(given.NextInput!.Candidates, c => c.Action == UnitAction.Operate);
        var full = Start(definition with { Machines = [Press("press", new(2, 1), 3)] });
        Assert.DoesNotContain(full.NextInput!.Candidates, c => c.Action == UnitAction.GiveResource);
        var wrong = Start(definition with { Resources = [new(ResourceTypes.Diamond, new(1, 1))] });
        Assert.DoesNotContain(wrong.NextInput!.Candidates, c => c.MachineId is not null);
        Assert.Contains(wrong.NextInput!.Candidates, c => c.TargetId == "collector");
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false, false)]
    [InlineData(EdgeKind.ClosedDoor, false, false)]
    [InlineData(EdgeKind.WallWithWindow, false, true)]
    [InlineData(EdgeKind.OpenDoor, true, true)]
    [InlineData(EdgeKind.None, true, true)]
    public void MachineCanBorderAnyEdgeButOperateRequiresPassableEdge(EdgeKind edge, bool canOperate, bool canGive)
    {
        var definition = Fixture() with { Board = new() { Width = 7, Height = 5,
            Edges = [new(new(1, 1), EdgeDirection.Right, edge)] } };
        var start = Start(definition);
        Assert.Equal(canOperate, start.NextInput!.Candidates.Any(c => c.Key == "operate:press"));
        var give = Start(definition with { Machines = [Press("press", new(2, 1), 2)],
            Resources = [new(ResourceTypes.Coal, new(1, 1))] });
        Assert.Equal(canGive, give.NextInput!.Candidates.Any(c => c.Key == "give-resource-machine:press"));
        var board = start.State.Physical.Board;
        Assert.True(EdgeReplacementRules.CanReplace(start.State, new(new(1, 1), new(2, 1), EdgeKind.Wall)));
        Assert.Equal(edge, board.EdgeBetween(new(1, 1), new(2, 1)));
    }

    [Theory]
    [InlineData("inputs")]
    [InlineData("occupied-output")]
    [InlineData("no-action")]
    [InlineData("distant")]
    [InlineData("lying")]
    public void UnavailableOperationPreservesInputsAndOutput(string reason)
    {
        var definition = Fixture(reason == "inputs" ? 2 : 3);
        if (reason == "occupied-output") definition = definition with { Resources = [new(ResourceTypes.Coal, new(1, 1))] };
        if (reason == "no-action") definition = definition with { UnitTypes = [definition.UnitTypes![0] with { Actions = UnitAction.None }] };
        if (reason == "distant") definition = definition with { Units = [definition.Units[0] with { Anchor = new(0, 0) }, .. definition.Units.Skip(1)] };
        if (reason == "lying") definition = definition with { Units = [definition.Units[0] with { Posture = Posture.Lying }, .. definition.Units.Skip(1)] };
        var start = Start(definition);
        Assert.DoesNotContain(start.NextInput?.Candidates ?? [], c => c.Action == UnitAction.Operate);
        Assert.Equal(reason == "inputs" ? 2 : 3, Machine(start.State).StoredQuantity("coal"));
        Assert.Equal(definition.Resources?.Count ?? 0, start.State.Resources.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MachineBlocksPlacementMovementAndLosWhilePhaseOnlyChangesTraversal(bool phase)
    {
        var state = GameEngine.CreateGame(Fixture());
        if (phase) state.Types[0] = state.Types[0] with { Phase = new() };
        var paths = MovementRules.FindPaths(state, "actor", new(1, 1), 2);
        Assert.DoesNotContain(new Cell(2, 1), paths.Keys);
        Assert.Equal(phase, paths.ContainsKey(new(3, 1)));
        Assert.Throws<ArgumentException>(() => state.PlaceUnit("worker", "overlap", "blue", new(2, 1)));
        Assert.False(SpatialRules.CanPlaceUnit(state, Footprint.TwoByTwo, new(2, 0)));
        Assert.False(AttackRules.HasGeometricLineOfSight(state.Physical.Board, new(1, 1), new(3, 1)));
        Assert.False(AttackRules.HasGeometricLineOfSight(state.Physical.Board, new(1, 1), new(2, 1)));
        Assert.True(AttackRules.HasGeometricLineOfSight(state.Physical.Board, new(0, 0), new(2, 0)));
    }

    [Fact]
    public void ScenarioAndStateSerializationPreserveEachMachineAndOperation()
    {
        var definition = Fixture();
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var original = GameEngine.CreateGame(decoded);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(original))!;
        Assert.Equal(3, Machine(restored).StoredQuantity("coal"));
        Assert.Equal(1, Machine(restored, "other-press").StoredQuantity("coal"));
        Assert.Equal(new Cell(2, 1), Machine(restored).Position);
        Assert.Equal("diamond-press", Machine(restored).Type.Id);
        var start = Ready(GameEngine.StartRound(restored, new Dice(), false));
        Assert.Equal(ResourceTypes.Diamond, Pick(start, "operate:press").State.Resources.Single().Type);
        Assert.Equal(3, Machine(original).StoredQuantity("coal"));
        Assert.Equal(3, GameEngine.CreateGame(definition).Physical.Board.Machines[0].StoredQuantity("coal"));
    }

    [Fact]
    public void InvalidMachineStateIsRejected()
    {
        var definition = Fixture();
        var press = definition.Machines![0];
        foreach (var machines in new List<Machine>[] {
            [press, press], [press, press with { Id = "different" }],
            [press with { Position = new(7, 1) }], [press with { Id = "actor" }],
            [press with { Position = new(1, 1) }],
            [press with { StoredResources = [new(ResourceTypes.Coal, 4)] }],
            [press with { StoredResources = [new(ResourceTypes.Coal, -1)] }],
            [press with { StoredResources = [new(ResourceTypes.Diamond, 1)] }],
            [press with { Type = press.Type with { Operation = press.Type.Operation with { OutputQuantity = 2 } } }]
        }) Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { Machines = machines }));
    }

    [Fact]
    public void StaleOperateAndGiveChoicesCannotPartiallyMutateState()
    {
        var start = Start();
        start.State.PlaceResource(ResourceTypes.Coal, new(1, 1));
        Assert.Throws<ArgumentException>(() => Pick(start, "operate:press"));
        Assert.Equal(3, Machine(start.State).StoredQuantity("coal"));
        Assert.Equal(ResourceTypes.Coal, Assert.Single(start.State.Resources).Type);
        start = Start(Fixture(2) with { Resources = [new(ResourceTypes.Coal, new(1, 1))] });
        start.State.Physical.Board.Machines[0] = Press("press", new(2, 1), 3);
        Assert.Throws<ArgumentException>(() => Pick(start, "give-resource-machine:press"));
        Assert.Single(start.State.Resources);
        Assert.Equal(3, Machine(start.State).StoredQuantity("coal"));
    }

    [Fact]
    public void GeomancerCollectsDiamondIntoSharedStorageAndCannotAttackOrSummon()
    {
        var type = UnitRoster.Geomancer();
        Assert.Equal((2, 0, 0, 3, 1), (type.Mov, type.Rng, type.Atk, type.Def, type.Hp));
        Assert.Equal(UnitAction.PickupResource, type.Actions);
        Assert.Equal(UnitBehavior.Flee, type.Behaviors);
        Assert.Null(type.SummonAdjacent);
        var fixture = Fixture();
        fixture = fixture with { UnitTypes = [fixture.UnitTypes![0] with { TransportResource = null }] };
        var result = Pick(Start(fixture), "operate:press");
        // Finish the first round. The operator can vacate its output Cell on its next Move.
        while (!result.State.RoundComplete) result = Pick(result, "stay");
        result = GameEngine.StartRound(result.State, new Dice(), false);
        result = Pick(result, "0,1");
        result = Pick(result, "1,1");
        result = Pick(result, "pickup-resource");
        Assert.Empty(result.State.Resources);
        Assert.Equal(1, result.State.StoredQuantity("blue", UnitTypeIds.Geomancer, "diamond"));
        Assert.Equal(0, Machine(result.State).StoredQuantity("coal"));
    }
}

