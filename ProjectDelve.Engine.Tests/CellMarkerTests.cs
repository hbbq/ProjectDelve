using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CellMarkerTests
{
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException();
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException();
        public int RollD6() => throw new InvalidOperationException();
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private static ScenarioDefinition Fixture(string actor = UnitTypeIds.DisplacerDemon) => new()
    {
        Board = new() { Width = 7, Height = 4 },
        UnitTypeIds = [actor, UnitTypeIds.Grunt],
        Units = [new(actor, "blue", new(0, 1), UnitId: "actor"),
            new(UnitTypeIds.Grunt, "red", new(6, 3), UnitId: "enemy")],
        Agency = [new(actor, "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        CellMarkers = [new(CellMarkerTypes.BananaPeel, new(2, 1))]
    };
    private static EngineResult Start(ScenarioDefinition? definition = null) =>
        GameEngine.StartRound(GameEngine.CreateGame(definition ?? Fixture()), new Dice(), false);
    private static EngineResult Pick(EngineResult result, string key) =>
        GameEngine.Advance(result.State, new Choice(key), new Dice(), false);

    [Fact]
    public void ScenarioAndRuntimeSerializationPreserveMarkersAndCopiesAreDetached()
    {
        var definition = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(Fixture()));
        var state = GameEngine.CreateGame(definition);
        Assert.Equal(Fixture().CellMarkers, state.CellMarkers);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state.CellMarkers, restored.CellMarkers);
        var copy = state.Copy(); copy.CellMarkers.Clear();
        Assert.Single(state.CellMarkers);
        Assert.Empty(GameEngine.CreateGame(Fixture() with { CellMarkers = null }).CellMarkers);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(7, 1)]
    [InlineData(0, 1)] // Unit
    [InlineData(6, 3)] // Unit
    public void InvalidScenarioPlacementIsRejectedWithMarkerPath(int x, int y)
    {
        var error = Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Fixture() with
            { CellMarkers = [new(CellMarkerTypes.BananaPeel, new(x, y))] }));
        Assert.Equal("cellMarkers[0]", ScenarioValidationContext.PathOf(error));
    }

    [Fact]
    public void PlacementAllowsResourcesAndEveryPassableTerrainButRejectsOccupancyAndUnsupportedTypes()
    {
        foreach (var terrain in Enum.GetValues<TerrainKind>().Where(t => t.Passable()))
        {
            var d = Fixture() with { Board = Fixture().Board with
                { Cells = [new(new(2, 1), terrain, terrain == TerrainKind.Rail ? RailDirection.East : null)] },
                Resources = [new(ResourceTypes.Coal, new(2, 1))] };
            Assert.Single(GameEngine.CreateGame(d).CellMarkers);
        }
        var state = GameEngine.CreateGame(Fixture());
        Assert.Throws<ArgumentException>(() => state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(2, 1)));
        Assert.Throws<ArgumentException>(() => state.PlaceCellMarker(CellMarkerTypes.BananaPeel, new(0, 1)));
        Assert.Throws<ArgumentException>(() => state.PlaceCellMarker(new("unknown", "Unknown"), new(3, 1)));
        foreach (var terrain in Enum.GetValues<TerrainKind>().Where(t => !t.Passable()))
            Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Fixture() with
                { Board = Fixture().Board with { Cells = [new(new(2, 1), terrain)] } }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Fixture() with
            { Machines = [new("press", new(2, 1), MachineTypes.DiamondPress)] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Fixture() with
            { CellMarkers = [new(CellMarkerTypes.BananaPeel, new(2, 1)), new(CellMarkerTypes.BananaPeel, new(2, 1))] }));
    }

    [Fact]
    public void CanonicalPathsCanEndOnPeelButMustDetourAroundItIncludingUnboundedQueries()
    {
        var start = Start();
        Assert.Contains(start.NextInput!.Candidates, c => c.Destination == new Cell(2, 1));
        Assert.DoesNotContain(start.NextInput.Candidates, c => c.Destination == new Cell(3, 1)); // detour costs 5, MOV 3
        var paths = new GameplayQueries(start.State).MovementPathsFrom("actor", new(0, 1));
        Assert.Equal(6, paths[new(3, 1)].Count);
        Assert.All(paths.Values, path => Assert.DoesNotContain(new Cell(2, 1), path.Skip(1).SkipLast(1)));
        Assert.True(start.State.Physical.Board.TerrainAt(new(2, 1)).Passable());
        Assert.True(AttackRules.HasGeometricLineOfSight(start.State.Physical.Board, new(0, 1), new(3, 1)));
    }

    [Fact]
    public void AtomicMoveTransportsResourceThenConsumesPeelLaysDownAndCompletesActivationOnce()
    {
        var d = Fixture(UnitTypeIds.Zombie) with { Resources = [new(ResourceTypes.Coal, new(0, 1))] };
        var start = Start(d);
        var end = Pick(start, "2,1");
        Assert.Equal(new[] { "MovementCompleted", "CellMarkerConsumed", "PostureChanged", "ActivationCompleted" },
            end.Events.Take(4).Select(e => e.Kind));
        Assert.Equal(new[] { new Cell(0, 1), new(1, 1), new(2, 1) }, end.Events[0].Path);
        var arrived = end.ResolutionSteps[0].StateAfter;
        Assert.Single(arrived.CellMarkers); Assert.True(arrived.IsUpright("actor"));
        Assert.Equal(new(2, 1), Assert.Single(arrived.Resources).Position);
        Assert.Empty(end.ResolutionSteps[1].StateAfter.CellMarkers);
        Assert.True(end.ResolutionSteps[1].StateAfter.IsUpright("actor"));
        Assert.False(end.State.IsUpright("actor")); Assert.Empty(end.State.CellMarkers);
        Assert.Single(end.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "actor");
        Assert.NotEqual("actor", end.NextInput?.UnitId);
        Assert.Single(start.State.CellMarkers); // original and earlier event snapshots stay detached
    }

    [Fact]
    public void ExistingOccupantDoesNotSlipOnActivationStartOrStay()
    {
        var state = GameEngine.CreateGame(Fixture() with { CellMarkers = null });
        // Runtime/restored state can contain a marker beneath an existing figure.
        state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, new(0, 1)));
        var start = GameEngine.StartRound(state, new Dice(), false);
        Assert.True(start.State.IsUpright("actor")); Assert.Single(start.State.CellMarkers);
        var stayed = Pick(start, "stay");
        Assert.Single(stayed.State.CellMarkers); Assert.True(stayed.State.IsUpright("actor"));
        Assert.DoesNotContain(stayed.Events, e => e.Kind == "CellMarkerConsumed");
    }

    [Fact]
    public void RunRailsStopsAfterItsSecondStepAndCompletesExactlyOnce()
    {
        var d = Fixture(UnitTypeIds.Locomotive) with { Board = Fixture().Board with
            { Cells = Enumerable.Range(0, 7).Select(x => new CellDefinition(new(x, 1), TerrainKind.Rail, RailDirection.East)).ToList() } };
        var result = Start(d);
        Assert.Equal(new(2, 1), result.State.Physical.Figures.Single(f => f.Id == "actor").Position);
        Assert.Equal(2, result.Events.Count(e => e.Kind == "UnitRepositioned"));
        Assert.False(result.State.IsUpright("actor")); Assert.Empty(result.State.CellMarkers);
        Assert.Equal(new[] { "UnitRepositioned", "CellMarkerConsumed", "PostureChanged", "ActivationCompleted" },
            result.Events.SkipWhile(e => e.Kind != "CellMarkerConsumed").Prepend(result.Events.Last(e => e.Kind == "UnitRepositioned")).Take(4).Select(e => e.Kind));
        Assert.Single(result.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "actor");
    }

    [Fact]
    public void DisplaceConsumesOnActualArrivalButLeavesActorsActivationAndZeroStepUntouched()
    {
        var d = Fixture() with { Units = [new(UnitTypeIds.DisplacerDemon, "blue", new(0, 1), UnitId: "actor"),
            new(UnitTypeIds.Grunt, "red", new(1, 1), UnitId: "enemy")] };
        var start = Start(d);
        var zero = start.NextInput!.Candidates.Single(c => c.BonusAction?.Displace is not null && c.Destination == new Cell(1, 1));
        var stayed = Pick(start, zero.Key);
        Assert.Single(stayed.State.CellMarkers); Assert.True(stayed.State.IsUpright("enemy"));
        var move = start.NextInput.Candidates.Single(c => c.BonusAction?.Displace is not null && c.Destination == new Cell(2, 1));
        var end = Pick(start, move.Key);
        Assert.False(end.State.IsUpright("enemy")); Assert.Empty(end.State.CellMarkers);
        Assert.Equal("actor", end.State.CurrentUnitId);
        Assert.Equal(new[] { "ActionUsed", "UnitRepositioned", "CellMarkerConsumed", "PostureChanged" }, end.Events.Select(e => e.Kind));
    }

    [Fact]
    public void SwapResolvesBothArrivalsAndOnlyCompletesTheActiveUnit()
    {
        var d = Fixture() with { Units = [new(UnitTypeIds.DisplacerDemon, "blue", new(0, 1), UnitId: "actor"),
            new(UnitTypeIds.Grunt, "red", new(1, 1), UnitId: "enemy")], CellMarkers = null };
        var state = GameEngine.CreateGame(d);
        state.CellMarkers = [new(CellMarkerTypes.BananaPeel, new(0, 1)), new(CellMarkerTypes.BananaPeel, new(1, 1))];
        var start = GameEngine.StartRound(state, new Dice(), false);
        var swap = start.NextInput!.Candidates.Single(c => c.BonusAction?.Swap is not null);
        var result = Pick(start, swap.Key);
        Assert.Equal(new[] { "ActionUsed", "PlacesSwapped", "CellMarkerConsumed", "PostureChanged",
            "CellMarkerConsumed", "PostureChanged", "ActivationCompleted" }, result.Events.Take(7).Select(e => e.Kind));
        Assert.Empty(result.State.CellMarkers);
        Assert.All(new[] { "actor", "enemy" }, id => Assert.Contains(result.Events, e => e.Kind == "PostureChanged" && e.UnitId == id && e.Posture == Posture.Lying));
        Assert.Single(result.Events, e => e.Kind == "ActivationCompleted" && e.UnitId == "actor");
    }

    [Fact]
    public void TelekinesisIsNotAnArrivalAndDoesNotConsumeAMarkerBeneathItsTarget()
    {
        var d = Fixture(UnitTypeIds.Wizard) with { CellMarkers = null,
            Units = [new(UnitTypeIds.Wizard, "blue", new(0, 1), UnitId: "actor"),
                new(UnitTypeIds.Grunt, "red", new(2, 1), UnitId: "enemy")] };
        var state = GameEngine.CreateGame(d);
        state.CellMarkers.Add(new(CellMarkerTypes.BananaPeel, new(2, 1)));
        var start = GameEngine.StartRound(state, new Dice(), false);
        start = Pick(start, "stay");
        var action = start.NextInput!.Candidates.Single(c => c.Action == UnitAction.Telekinesis);
        var result = Pick(start, action.Key);
        Assert.Single(result.State.CellMarkers); Assert.False(result.State.IsUpright("enemy"));
        Assert.DoesNotContain(result.Events, e => e.Kind == "CellMarkerConsumed");
        Assert.Equal("actor", result.State.CurrentUnitId);
    }

    [Fact]
    public void NewlySummonedUnitArrivesAndSlipsWithoutEndingSummonersActivation()
    {
        var d = Fixture(UnitTypeIds.Geomancer) with { CellMarkers = [new(CellMarkerTypes.BananaPeel, new(1, 1))] };
        var start = Start(d);
        start = Pick(start, "stay");
        var action = start.NextInput!.Candidates.Single(c => c.Action == UnitAction.SummonWorker && c.Destination == new Cell(1, 1));
        var result = Pick(start, action.Key);
        var created = Assert.Single(result.Events, e => e.Kind == "UnitCreated");
        Assert.Equal(Posture.Upright, created.Posture);
        Assert.False(result.State.IsUpright(created.UnitId!)); Assert.Empty(result.State.CellMarkers);
        Assert.True(result.State.IsUpright("actor"));
        Assert.Equal("actor", result.ResolutionSteps[3].StateAfter.CurrentUnitId);
        Assert.Equal(new[] { "ActionUsed", "UnitCreated", "CellMarkerConsumed", "PostureChanged" }, result.Events.Take(4).Select(e => e.Kind));
    }
}

