using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LavaTests
{
    private sealed class Dice(params int[] rolls) : IRandomProvider
    {
        public int Calls { get; private set; }
        public int RollD6() => throw new InvalidOperationException("Lava must use d8 rolls.");
        public int RollD8() => rolls[Calls++ % rolls.Length];
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    private static ScenarioDefinition Fixture(params Cell[] lava) => new()
    {
        Board = new() { Width = 8, Height = 7,
            Cells = lava.Select(c => new CellDefinition(c, TerrainKind.Lava)).ToList() },
        UnitTypeIds = [UnitTypeIds.Worker, UnitTypeIds.Geomancer, UnitTypeIds.Troll, UnitTypeIds.Grunt],
        Units = [new(UnitTypeIds.Worker, "blue", new(0, 0), UnitId: "worker"),
            new(UnitTypeIds.Geomancer, "blue", new(4, 1), UnitId: "geo"),
            new(UnitTypeIds.Grunt, "red", new(7, 6), UnitId: "enemy")],
        Agency = [new(UnitTypeIds.Worker, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Geomancer, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Troll, "blue", ControllerKind.Automated),
            new(UnitTypeIds.Grunt, "red", ControllerKind.Automated)],
        Orders = [new(UnitTypeIds.Grunt, "red", OrderKind.DoNothing)]
    };

    [Theory]
    [InlineData(1, 2, 1)] [InlineData(2, 3, 1)] [InlineData(3, 3, 2)] [InlineData(4, 3, 3)]
    [InlineData(5, 2, 3)] [InlineData(6, 1, 3)] [InlineData(7, 1, 2)] [InlineData(8, 1, 1)]
    public void DirectionsRunClockwiseFromNorthAndDuplicateRollsCreateOnlyOneCoal(int roll, int x, int y)
    {
        var dice = new Dice(roll);
        var previous = GameEngine.CreateGame(Fixture(new Cell(2, 2)));
        var result = GameEngine.StartRound(previous, dice, false);
        Assert.Equal(new Cell(x, y), Assert.Single(result.State.Resources).Position);
        Assert.All(result.State.Resources, r => Assert.Equal(ResourceTypes.Coal, r.Type));
        Assert.Equal(2, dice.Calls);
        Assert.Empty(previous.Resources);
        var snapshots = result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind == "ResourceCreated").ToArray();
        Assert.Single(snapshots);
        Assert.Equal(new[] { "RoundStarted", "LavaCoalRolled", "ResourceCreated", "LavaCoalRolled", "ActivationBagPopulated" },
            result.Events.Take(5).Select(e => e.Kind));
        for (var i = 0; i < snapshots.Length; i++)
        {
            Assert.Equal(i + 1, snapshots[i].StateAfter.Resources.Count);
            Assert.Empty(snapshots[i].StateAfter.Bag);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(7)]
    public void InvalidDestinationsAreNotReplacedAndOccupiedCellsQualify(int blockedDirection)
    {
        var definition = Fixture(new Cell(1, 1));
        definition = definition with
        {
            Board = definition.Board with { Cells = [.. definition.Board.Cells,
                new(new(1, 0), TerrainKind.Water), new(new(2, 0), TerrainKind.StoneFloorWithTable)] },
            Machines = [new("press", new(2, 1), MachineTypes.DiamondPress)],
            Resources = [new(ResourceTypes.Diamond, new(0, 1))]
        };
        var dice = new Dice(blockedDirection, 8);
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), dice, false);
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(0, 0) && r.Type == ResourceTypes.Coal);
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(0, 1) && r.Type == ResourceTypes.Diamond);
        Assert.DoesNotContain(result.State.Resources, r => new[] { new Cell(1, 0), new(2, 0), new(2, 1), new(1, 1) }.Contains(r.Position));
        Assert.Equal(2, result.State.Resources.Count);
        Assert.Equal(2, dice.Calls);
    }

    [Theory]
    [InlineData(EdgeKind.Wall, false)]
    [InlineData(EdgeKind.ClosedDoor, false)]
    [InlineData(EdgeKind.OpenDoor, true)]
    [InlineData(EdgeKind.WallWithWindow, true)]
    public void EligibilityUsesGeometricLosIncludingEdgesAndCorners(EdgeKind edge, bool visible)
    {
        var definition = Fixture(new Cell(2, 2));
        definition = definition with { Board = definition.Board with
        { Edges = [new(new(2, 2), EdgeDirection.Right, edge), new(new(2, 2), EdgeDirection.Down, edge)] },
            Resources = [new(ResourceTypes.Diamond, new(1, 1)), new(ResourceTypes.Diamond, new(2, 1)),
                new(ResourceTypes.Diamond, new(3, 1)), new(ResourceTypes.Diamond, new(1, 2)),
                new(ResourceTypes.Diamond, new(1, 3)), new(ResourceTypes.Diamond, new(2, 3))] };
        var state = GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(3, 4), false).State;
        Assert.Equal(visible, state.Resources.Any(r => r.Position == new Cell(3, 2)));
        Assert.Equal(visible, state.Resources.Any(r => r.Position == new Cell(3, 3)));
    }

    [Fact]
    public void MultipleSourcesRollIndependentlyInTopLeftOrderAndReevaluateSharedSpace()
    {
        var dice = new Dice(3, 4, 7, 5, 1, 2);
        var definition = Fixture(new Cell(0, 3), new(2, 0), new(0, 0));
        definition = definition with { Units = [definition.Units[0] with { Anchor = new(0, 2) }, .. definition.Units.Skip(1)] };
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), dice, false);
        Assert.Equal(6, dice.Calls);
        Assert.Equal(new[] { new Cell(0, 0), new(0, 0), new(2, 0), new(2, 0), new(0, 3), new(0, 3) },
            result.Events.Where(e => e.Kind == "LavaCoalRolled").Select(e => e.Cell));
        Assert.Equal(new[] { new Cell(1, 0), new(1, 1), new(2, 1), new(0, 2), new(1, 2) }, result.State.Resources.Select(r => r.Position));
        Assert.Equal(result.State.Resources.Count, result.State.Resources.Select(r => r.Position).Distinct().Count());
    }

    [Fact]
    public void WorldCardsAndConsequencesFinishBeforeLavaAndBagPopulation()
    {
        var definition = Fixture(new Cell(2, 2));
        definition = definition with { WorldEffects = new(2, 1), Board = definition.Board with
            { Edges = [new(new(2, 2), EdgeDirection.Right, EdgeKind.ClosedDoor)] } };
        var previous = GameEngine.CreateGame(definition);
        previous.WorldDeck = new() { DrawPile = [new("open", WorldEffect.OpenSesame), new("wind", WorldEffect.SecondWind)] };
        var result = GameEngine.StartRound(previous, new Dice(3, 4), false);
        var kinds = result.Events.Select(e => e.Kind).ToList();
        Assert.True(kinds.LastIndexOf("WorldCardDiscarded") < kinds.IndexOf("LavaCoalRolled"));
        Assert.True(kinds.LastIndexOf("ResourceCreated") < kinds.IndexOf("ActivationBagPopulated"));
        Assert.Contains(result.State.Resources, r => r.Position == new Cell(3, 2));
        Assert.All(result.ResolutionSteps.Where(s => result.Events[s.EventIndex].Kind == "ResourceCreated"), s => Assert.Empty(s.StateAfter.Bag));
    }

    [Theory]
    [InlineData(0, 0, 1)] [InlineData(0, 0, 2)] [InlineData(0, 0, 6)] [InlineData(0, 0, 7)] [InlineData(0, 0, 8)]
    [InlineData(7, 6, 2)] [InlineData(7, 6, 3)] [InlineData(7, 6, 4)] [InlineData(7, 6, 5)] [InlineData(7, 6, 6)]
    public void OffBoardAttemptsProduceNothingAndNeverReroll(int x, int y, int direction)
    {
        var definition = Fixture(new Cell(x, y));
        definition = definition with { Units = [definition.Units[0] with { Anchor = new(0, 2) },
            definition.Units[1], definition.Units[2] with { Anchor = new(7, 4) }] };
        var dice = new Dice(direction);
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), dice, false);
        Assert.Equal(2, dice.Calls);
        Assert.Empty(result.State.Resources);
        Assert.Equal(2, result.Events.Count(e => e.Kind == "LavaCoalRolled"));
        Assert.DoesNotContain(result.Events, e => e.Kind == "ResourceCreated");
    }

    [Theory]
    [InlineData(TerrainKind.Lava)] [InlineData(TerrainKind.Tree)]
    [InlineData(TerrainKind.Water)] [InlineData(TerrainKind.StoneFloorWithTable)]
    public void ImpassableDestinationNeverReceivesCoal(TerrainKind terrain)
    {
        var definition = Fixture(new Cell(2, 2));
        definition = definition with { Board = definition.Board with
            { Cells = [.. definition.Board.Cells, new(new(2, 1), terrain)] } };
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), new Dice(1), false);
        Assert.DoesNotContain(result.State.Resources, r => r.Position == new Cell(2, 1));
    }

    [Fact]
    public void ReplayingTheSameD8SequenceReproducesEventsStateAndPlaybackSnapshots()
    {
        var initial = GameEngine.CreateGame(Fixture(new Cell(2, 2), new(4, 2)));
        var first = GameEngine.StartRound(initial, new Dice(1, 3, 7, 8), false);
        var second = GameEngine.StartRound(initial, new Dice(1, 3, 7, 8), false);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(second));
        Assert.Equal(new[] { new Cell(2, 1), new(3, 2), new(3, 1) }, first.State.Resources.Select(r => r.Position));
        Assert.Equal(new[] { 1, 3, 7, 8 }, first.Events.Where(e => e.Kind == "LavaCoalRolled").Select(e => e.DieRoll!.Value));
        var counts = first.ResolutionSteps.Where(s => first.Events[s.EventIndex].Kind is "LavaCoalRolled" or "ResourceCreated")
            .Select(s => s.StateAfter.Resources.Count);
        Assert.Equal(new[] { 0, 1, 1, 2, 2, 2, 3 }, counts);
        Assert.Empty(initial.Resources);
    }

    [Fact]
    public void LavaRoundTripsAndRejectsUnitAndResourcePlacement()
    {
        var definition = Fixture(new Cell(0, 0));
        Assert.ThrowsAny<ArgumentException>(() => GameEngine.CreateGame(definition));
        definition = Fixture(new Cell(2, 2));
        var loaded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(
            ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition))));
        var state = GameEngine.CreateGame(loaded);
        Assert.Equal(TerrainKind.Lava, state.Physical.Board.TerrainAt(new(2, 2)));
        Assert.Throws<ArgumentException>(() => state.PlaceResource(ResourceTypes.Coal, new(2, 2)));
        Assert.ThrowsAny<ArgumentException>(() => GameEngine.CreateGame(definition with { Resources = [new(ResourceTypes.Coal, new(2, 2))] }));
    }

    [Theory]
    [InlineData(0)] [InlineData(9)]
    public void RejectsInvalidD8WithoutChangingPreviousState(int roll)
    {
        var state = GameEngine.CreateGame(Fixture(new Cell(2, 2)));
        Assert.Throws<ArgumentException>(() => GameEngine.StartRound(state, new Dice(roll), false));
        Assert.Empty(state.Resources);
        Assert.Equal(0, state.Round);
    }

    [Fact]
    public void FullySurroundedLavaStillRollsWithoutCreatingResources()
    {
        var definition = Fixture(new Cell(2, 2));
        definition = definition with { Board = definition.Board with { Cells = [.. definition.Board.Cells,
            .. Enumerable.Range(1, 3).SelectMany(y => Enumerable.Range(1, 3)
                .Where(x => x != 2 || y != 2).Select(x => new CellDefinition(new(x, y), TerrainKind.Water)))] } };
        var dice = new Dice(6);
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition), dice, false);
        Assert.Equal(2, dice.Calls);
        Assert.Empty(result.State.Resources);
        Assert.DoesNotContain(result.Events, e => e.Kind == "ResourceCreated");
    }

    [Fact]
    public void DefaultLavaTerrainGeneratesFromEveryUnspecifiedCell()
    {
        var definition = Fixture();
        definition = definition with { Board = new() { Width = 8, Height = 7, DefaultTerrain = TerrainKind.Lava,
            Cells = definition.Units.Select(u => new CellDefinition(u.Anchor, TerrainKind.StoneFloor)).ToList() } };
        var loaded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        var dice = new Dice(1);
        var result = GameEngine.StartRound(GameEngine.CreateGame(loaded), dice, false);
        Assert.Equal(2 * (8 * 7 - definition.Units.Count), dice.Calls);
        Assert.Equal(2, result.State.Resources.Count);
        Assert.All(result.State.Resources, r => Assert.Equal(TerrainKind.StoneFloor, result.State.Physical.Board.TerrainAt(r.Position)));
    }

    [Fact]
    public void WorldDefeatConsequencesFinishBeforeLavaAndRemovedGroupsDoNotEnterBag()
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 8, Height = 7, Cells = [new(new(2, 2), TerrainKind.Lava)] },
            WorldEffects = new(1, 1),
            UnitTypeIds = [UnitTypeIds.BombImp, UnitTypeIds.Worker, UnitTypeIds.Troll, UnitTypeIds.Grunt],
            UnitTypes = [UnitRoster.Troll() with { Hp = 4, Unique = true }],
            Units = [new(UnitTypeIds.BombImp, "blue", new(0, 0)), new(UnitTypeIds.Worker, "blue", new(1, 0)),
                new(UnitTypeIds.Troll, "blue", new(0, 1)), new(UnitTypeIds.Grunt, "red", new(7, 6))],
            Agency = [new(UnitTypeIds.BombImp, "blue", ControllerKind.Human), new(UnitTypeIds.Worker, "blue", ControllerKind.Human),
                new(UnitTypeIds.Troll, "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Human)]
        };
        var state = GameEngine.CreateGame(definition);
        state.WorldDeck = new() { DrawPile = [new("repel", WorldEffect.Repulsion)] };
        var result = GameEngine.StartRound(state, new Dice(1), false);
        var kinds = result.Events.Select(e => e.Kind).ToList();
        Assert.Contains("ExplosionDamageResolved", kinds);
        Assert.True(kinds.LastIndexOf("ExplosionDamageResolved") < kinds.IndexOf("LavaCoalRolled"));
        var bag = result.ResolutionSteps.Single(s => result.Events[s.EventIndex].Kind == "ActivationBagPopulated").StateAfter.Bag;
        Assert.Equal(2, bag.Count);
        Assert.Contains(new(UnitTypeIds.Troll, "blue"), bag);
        Assert.Contains(new(UnitTypeIds.Grunt, "red"), bag);
    }

    [Fact]
    public void RenewableCoalFeedsAutonomousWorkersPressAndGeomancer()
    {
        var definition = Fixture(new Cell(1, 3)) with { Machines = [new("press", new(4, 3), MachineTypes.DiamondPress)] };
        var state = GameEngine.CreateGame(definition);
        var dice = new Dice(1, 2, 3, 4, 5, 6, 7, 8);
        var provider = new OrderAwareAutomatedProvider();
        List<RulesEvent> events = [];
        for (var round = 0; round < 25 && !events.Any(e => e.Kind == "UnitCreated" && e.TypeId == UnitTypeIds.Troll); round++)
        {
            var result = GameEngine.StartRound(state, dice, false);
            events.AddRange(result.Events);
            for (var step = 0; step < 300 && !result.State.RoundComplete && !result.State.IsCompleted; step++)
            {
                Assert.NotNull(result.NextInput);
                result = GameEngine.Advance(result.State, provider, dice, false);
                events.AddRange(result.Events);
            }
            state = result.State;
            if (state.IsCompleted) break;
            Assert.True(state.RoundComplete);
        }
        Assert.Contains(events, e => e.Kind == "ResourceCreated");
        Assert.Contains(events, e => e.Kind == "ResourceStored" && e.MachineId == "press");
        Assert.Contains(events, e => e.Kind == "MachineOperated");
        Assert.Contains(events, e => e.Kind == "ResourceStored" && e.ResourceType == ResourceTypes.Diamond);
        Assert.Contains(events, e => e.Kind == "UnitCreated" && e.TypeId == UnitTypeIds.Worker);
        Assert.Contains(events, e => e.Kind == "UnitCreated" && e.TypeId == UnitTypeIds.Troll);
        Assert.True(events.Count(e => e.Kind == "LavaCoalRolled") > 1);
    }
}
