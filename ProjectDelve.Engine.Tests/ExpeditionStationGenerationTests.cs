using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ExpeditionStationGenerationTests
{
    private static readonly ContentBundle Content = ContentBundle.Canonical();
    private static WorldGenerationRecipe Recipe(NaturalTerrainSettings nature, ulong seed = 17) =>
        new(new(0, 0, 60, 60), nature, seed, new(), new(Enabled: false),
            new(Enabled: false), new(PopulationDensity.Off));

    [Theory]
    [InlineData(0, 0, 0, 0, 5)]
    [InlineData(0, 100, 0, 1, 5)]
    [InlineData(100, 0, 0, 2, 5)]
    [InlineData(0, 0, 100, 3, 5)]
    [InlineData(0, 0, 0, 0, 6)]
    [InlineData(35, 12, 15, 1, 6)]
    public void ArrivalStationHasImmediateGeometricAccessWithoutConsumingDeploymentOrEndpoints(
        int forest, int water, int rock, int edge, int capacity)
    {
        var recipe = Recipe(new(forest, water, rock)) with { HeroCapacity = capacity, Window = new(60, 0, 60, 60) };
        Cell[] entries = [new(0, 30), new(30, 0), new(59, 30), new(30, 59)];
        Cell[] exits = [new(59, 20), new(20, 59), new(0, 20), new(20, 0)];
        ObjectiveArea[] pads = [new(57, 19, 2, 3), new(19, 57, 3, 2), new(1, 19, 2, 3), new(19, 1, 3, 2)];
        var execution = ExpeditionRegionGeometry.Execute(recipe, entries[edge], exits[edge], pads[edge],
            123, Content, profile: ExpeditionWorldProfile.Create(recipe.NaturalTerrain));
        var context = execution.Context;
        var definition = context.Materialize(); var state = GameEngine.CreateGame(definition, Content);
        var station = Assert.Single(definition.Machines!, m => m.Id == ExpeditionStationGenerator.ArrivalId);
        Assert.Equal(MachineTypes.EquipmentStation, station.Type);
        var actor = definition.Setup!.Deployment.PlacementOrder[0];
        Assert.Equal(capacity, definition.Setup.Deployment.Capacity);
        Assert.True(definition.Setup.Deployment.Cells.Count >= 2 * capacity);
        Assert.True(definition.Setup.Deployment.ManeuverCore.Count >= (3 * capacity + 1) / 2);
        Assert.True(MachineRules.GeometricallyOperable(state.Physical.Board, actor, station));
        Assert.DoesNotContain(station.Position, definition.Setup.Deployment.Cells);
        Assert.DoesNotContain(station.Position, definition.Setup.Deployment.PlacementOrder);
        Assert.DoesNotContain(station.Position.Y * 60 + station.Position.X, context.Expedition!.EndpointIndices);
        Assert.NotEqual(context.Deployment!.DepartureIndex, station.Position.Y * 60 + station.Position.X);
        Assert.True(PlayReadyScenarioSetup.Validate(definition, state).Valid);
        Assert.True(context.Expedition.HasOccupiedAccess(state, context.Deployment));
        Assert.Empty(context.Completion!.ActualFailures(state));
        Assert.True(context.ConstructionBoard!.IsReserved(station.Position.Y * 60 + station.Position.X));
        var repeat = ExpeditionRegionGeometry.Generate(recipe, entries[edge], exits[edge], pads[edge],
            123, Content, ExpeditionWorldProfile.Create(recipe.NaturalTerrain));
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(repeat));
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(
            ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition))));
    }

    [Fact]
    public void InitialRegionOmitsGuaranteedStationWhileEverySuccessorDirectionRetainsIt()
    {
        var localSeed = Enumerable.Range(0, 1200).Select(i => (ulong)i)
            .First(i => !ExpeditionStationGenerator.RequestsAdditional(i));
        var recipe = Recipe(new(0, 0, 0));
        foreach (var (x, y) in new[] { (0, 0), (60, 0), (-60, 0), (0, 60), (0, -60), (120, -60) })
        {
            var regional = recipe with { Window = new(x, y, 60, 60) };
            var context = ExpeditionRegionGeometry.Execute(regional, new(0, 30), new(59, 20), new(57, 19, 2, 3),
                localSeed, Content, profile: ExpeditionWorldProfile.Create(recipe.NaturalTerrain)).Context;
            var definition = context.Materialize();
            var stations = (definition.Machines ?? []).Where(m => m.Type.Operation.EquipmentStation is not null);
            if (x == 0 && y == 0)
            {
                Assert.Empty(stations);
                Assert.DoesNotContain(context.Construction, c => c.Id == ExpeditionStationGenerator.ArrivalId);
                Assert.DoesNotContain(context.PopulationProtectedAreas, a => a.Id == ExpeditionStationGenerator.ArrivalId);
            }
            else Assert.Equal(ExpeditionStationGenerator.ArrivalId, Assert.Single(stations).Id);
            var state = GameEngine.CreateGame(definition, Content);
            Assert.True(PlayReadyScenarioSetup.Validate(definition, state).Valid);
            Assert.True(context.Expedition!.HasOccupiedAccess(state, context.Deployment!));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    public void OptionalOpportunityIsRareDeterministicAndUsesCompletionSafeOpenPads(int offsetX)
    {
        var requests = Enumerable.Range(0, 1200).Select(i => (ulong)i)
            .Where(ExpeditionStationGenerator.RequestsAdditional).ToArray();
        Assert.InRange(requests.Length, 65, 140);
        Assert.Equal(new ulong[] { 5, 11 }, requests.Take(2));
        var recipe = Recipe(new(0, 0, 0)) with { Window = new(offsetX, 0, 60, 60) };
        foreach (var localSeed in requests.Take(2))
        {
            var context = ExpeditionRegionGeometry.Execute(recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3),
                localSeed, Content, profile: ExpeditionWorldProfile.Create(recipe.NaturalTerrain)).Context;
            var station = Assert.Single(context.Materialize().Machines!, m => m.Id == ExpeditionStationGenerator.AdditionalId);
            // Pin the placement stream where the first candidate in the opening is legal.
            if (offsetX == 0)
                Assert.Equal(localSeed == 5 ? new Cell(32, 6) : new Cell(43, 36), station.Position);
            var board = context.ConstructionBoard!;
            Assert.False(board.IsReserved(station.Position.Y * 60 + station.Position.X));
            Assert.False(board.IsRouteCorridor(station.Position.Y * 60 + station.Position.X));
            Assert.True(context.Expedition!.HasOccupiedAccess(GameEngine.CreateGame(context.Materialize()), context.Deployment!));
            var repeat = ExpeditionRegionGeometry.Generate(recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3),
                localSeed, Content, ExpeditionWorldProfile.Create(recipe.NaturalTerrain));
            Assert.Equal(ScenarioDefinitionJson.ToJson(context.Materialize()), ScenarioDefinitionJson.ToJson(repeat));
        }
        var noRequest = Enumerable.Range(0, 1200).Select(i => (ulong)i).First(i => !ExpeditionStationGenerator.RequestsAdditional(i));
        var noExtra = ExpeditionRegionGeometry.Generate(recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3),
            noRequest, Content, ExpeditionWorldProfile.Create(recipe.NaturalTerrain));
        if (offsetX == 0) Assert.DoesNotContain(noExtra.Machines ?? [], m => m.Type.Operation.EquipmentStation is not null);
        else Assert.Equal(ExpeditionStationGenerator.ArrivalId,
            Assert.Single(noExtra.Machines!, m => m.Type.Operation.EquipmentStation is not null).Id);
    }

    [Fact]
    public void OlderPinnedProfilesAndOrdinaryWld1DoNotGainStations()
    {
        var recipe = Recipe(new(0, 0, 0));
        var legacy = ExpeditionWorldProfile.Create(recipe.NaturalTerrain) with { StationVersion = null };
        var definition = ExpeditionRegionGeometry.Generate(recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3), 123, Content, legacy);
        Assert.DoesNotContain(definition.Machines ?? [], m => m.Type.Operation.EquipmentStation is not null);
        Assert.DoesNotContain(WorldGenerator.Generate(recipe).Definition.Machines ?? [], m => m.Type.Operation.EquipmentStation is not null);
        Assert.Throws<ArgumentException>(() => (legacy with { StationVersion = "future" }).Validate());
    }

    [Fact]
    public void DisconnectedOpenIslandIsOmittedWithoutPlacingAnUnreachableStation()
    {
        var localSeed = Enumerable.Range(0, 1200).Select(i => (ulong)i)
            .First(ExpeditionStationGenerator.RequestsAdditional);
        var recipe = Recipe(new(0, 0, 0));
        var context = ExpeditionRegionGeometry.Execute(recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3),
            localSeed, Content).Context;
        var board = context.ConstructionBoard!;
        var draft = board.BeginDraft(new(0, 0, 60, 60), "test/islands", new(maxWork: 100000));
        for (var i = 0; i < 3600; i++)
            if (draft.CanWrite(i)) draft.SetTerrain(i,
                i % 60 is >= 40 and <= 50 && i / 60 is >= 5 and <= 15 ? "grass" : "water");
        Assert.True(board.TryCommit(draft.BuildPatch(), out var error), error);
        var island = Enumerable.Range(5, 11).SelectMany(y => Enumerable.Range(40, 11).Select(x => y * 60 + x)).ToArray();
        Assert.All(island, i => Assert.True(board.Passable(i)));
        Assert.All(island, i => Assert.DoesNotContain(i, context.Completion!.InteractivePlacementDomain()));
        var step = new WorldGenerationStep("test/stations", WorldStepKind.MandatoryConnectivity,
            new AnalysisStepParameters(), []);
        ExpeditionStationGenerator.PlaceAdditional(context, step, board);
        Assert.Empty(board.Export().Machines ?? []);
        Assert.Contains(context.Completion!.Diagnostics, d => d.CandidateId == ExpeditionStationGenerator.AdditionalId &&
            d.Status == CompletionStatus.Omitted);
        Assert.All(island, i => Assert.Equal("grass", board.TerrainAt(i)));
    }

    [Fact]
    public void InitialMonstersCannotCutAllOptionalStationApproachesWhileLeavingTheExitReachable()
    {
        var localSeed = Enumerable.Range(0, 1200).Select(i => (ulong)i)
            .First(ExpeditionStationGenerator.RequestsAdditional);
        var recipe = Recipe(new(0, 0, 0));
        var context = ExpeditionRegionGeometry.Execute(recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3),
            localSeed, Content, profile: ExpeditionWorldProfile.Create(recipe.NaturalTerrain)).Context;
        var definition = context.Materialize();
        var station = Assert.Single(definition.Machines!, m => m.Id == ExpeditionStationGenerator.AdditionalId);
        var approaches = MachineGeometry.PotentialOperatingPositions(GameEngine.CreateGame(definition).Physical.Board, station);
        Assert.Equal(8, approaches.Length);
        const string type = "test-station-blocker";
        definition = definition with
        {
            UnitTypeIds = [type], UnitTypes = [new(type, 1, 1, 1, 1, 1)],
            Agency = [new(type, "blockers", ControllerKind.Automated)],
            Units = approaches.Select((cell, i) => new UnitPlacement(type, "blockers", cell, UnitId: "blocker-" + i)).ToList()
        };
        var state = GameEngine.CreateGame(definition);
        var legacyAccess = new ExpeditionGeometryPlan(recipe.Window, 5, new(0, 30), new(59, 20), new(57, 19, 2, 3), localSeed);
        Assert.True(legacyAccess.HasOccupiedAccess(state, context.Deployment!));
        Assert.False(context.Expedition!.HasOccupiedAccess(state, context.Deployment!));
    }
}
