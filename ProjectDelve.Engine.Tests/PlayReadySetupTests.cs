using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PlayReadySetupTests
{
    private static ScenarioDefinition Scenario(int capacity = 5)
    {
        var core = (from y in Enumerable.Range(0, 2) from x in Enumerable.Range(0, 4) select new Cell(x, y)).ToList();
        var cells = core.Concat(Enumerable.Range(0, 4).Select(x => new Cell(x, 2))).ToList();
        return new()
        {
            Board = new() { Width = 8, Height = 5, DefaultTerrain = TerrainKind.Grass },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty,
            Setup = new(1, new(capacity, cells, core, cells.ToList(), new(3, 0), new(4, 0), 2 * capacity,
                Math.Max(4, (3 * capacity + 1) / 2)), [])
        };
    }

    [Fact]
    public void JsonAndDelveTransportPreserveDeclarationsWithoutAddingRuntimeState()
    {
        var scenario = Scenario();
        scenario.Setup!.RequiredLocations.Add(new("test/ordinary", "test", "ordinary", ScenarioAccessClass.Ordinary,
            null, [new("usable", ScenarioAccessCoverage.All, [new(5, 0)])]));
        var json = ScenarioDefinitionJson.ToJson(scenario);
        Assert.DoesNotContain("preparation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("witness", json, StringComparison.OrdinalIgnoreCase);
        var imported = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(scenario));
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(imported));
        Assert.True(PlayReadyScenarioSetup.Validate(imported).Valid);
        Assert.DoesNotContain("setup", JsonSerializer.Serialize(GameEngine.CreateGame(imported)), StringComparison.OrdinalIgnoreCase);
        var legacy = scenario with { Setup = null };
        Assert.DoesNotContain("setup", ScenarioDefinitionJson.ToJson(legacy));
        Assert.Null(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(legacy)).Setup);
        Assert.Equal("Legacy", PlayReadyScenarioSetup.Validate(legacy).Status);
    }

    [Fact]
    public void ImportedEditedGeometryIsEngineValidButFailsFreshSetupValidation()
    {
        var scenario = Scenario();
        scenario = scenario with { Board = scenario.Board with { Cells = [new(new(0, 0), TerrainKind.SolidRock)] } };
        var imported = ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario));
        Assert.NotNull(GameEngine.CreateGame(imported));
        var result = PlayReadyScenarioSetup.Validate(imported);
        Assert.Equal("Invalid", result.Status);
        Assert.Contains(result.Diagnostics, d => d.Path == "setup.deployment");
    }

    [Theory]
    [InlineData(ScenarioAccessCoverage.Any, ScenarioAccessClass.Ordinary, true)]
    [InlineData(ScenarioAccessCoverage.All, ScenarioAccessClass.Ordinary, false)]
    [InlineData(ScenarioAccessCoverage.All, ScenarioAccessClass.DeliberateSpecial, true)]
    public void RequiredAccessRetainsAnyAllAndExplicitSpecialSemantics(ScenarioAccessCoverage coverage, ScenarioAccessClass access, bool valid)
    {
        var scenario = Scenario();
        scenario.Board.Edges.AddRange([new(new(6, 4), EdgeDirection.Right, EdgeKind.Wall), new(new(7, 3), EdgeDirection.Down, EdgeKind.Wall)]);
        scenario.Setup!.RequiredLocations.Add(new("test/place", "test", "place", access,
            access == ScenarioAccessClass.DeliberateSpecial ? "Deliberately isolated test area" : null,
            [new("usable", coverage, [new(5, 0), new(7, 4)])]));
        Assert.Equal(valid, PlayReadyScenarioSetup.Validate(scenario).Valid);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(int.MaxValue)]
    public void MaliciousCapacityProducesDiagnosticWithoutOverflow(int capacity)
    {
        var scenario = Scenario();
        scenario = scenario with { Setup = scenario.Setup! with { Deployment = scenario.Setup.Deployment with { Capacity = capacity } } };
        Assert.False(PlayReadyScenarioSetup.Validate(scenario).Valid);
    }

    [Fact]
    public void InvalidTransportShapeIsDiagnosedBeforeIndexingOrMaterialization()
    {
        var scenario = Scenario();
        var deployment = scenario.Setup!.Deployment;
        foreach (var malformed in new[]
                 {
                     deployment with { Cells = null! },
                     deployment with { PlacementOrder = [] },
                     deployment with { Departure = new(int.MaxValue, int.MaxValue) },
                     deployment with { Cells = [new(0, 0), new(0, 0)] }
                 })
            Assert.False(PlayReadyScenarioSetup.Validate(scenario with { Setup = scenario.Setup with { Deployment = malformed } }).Valid);
        scenario.Setup.RequiredLocations.Add(null!);
        Assert.False(PlayReadyScenarioSetup.Validate(scenario).Valid);
    }

    [Theory]
    [InlineData("cells")]
    [InlineData("maneuverCore")]
    [InlineData("placementOrder")]
    [InlineData("departureFrom")]
    [InlineData("departure")]
    [InlineData("positions")]
    public void ImportedNullCellReferencesProduceInvalidDiagnostics(string field)
    {
        var scenario = Scenario();
        scenario.Setup!.RequiredLocations.Add(new("test/ordinary", "test", "ordinary", ScenarioAccessClass.Ordinary, null,
            [new("usable", ScenarioAccessCoverage.All, [new(5, 0)])]));
        var json = JsonNode.Parse(ScenarioDefinitionJson.ToJson(scenario))!;
        if (field == "positions") json["setup"]!["requiredLocations"]![0]!["groups"]![0]![field]![0] = null;
        else if (field is "departure" or "departureFrom") json["setup"]!["deployment"]![field] = null;
        else json["setup"]!["deployment"]![field]![0] = null;
        var imported = ScenarioDefinitionJson.FromJson(json.ToJsonString());
        var result = PlayReadyScenarioSetup.Validate(imported);
        Assert.Equal("Invalid", result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void NewlyAddedIsolatedMachineRequiresAccessUnlessExplicitlySpecial()
    {
        var scenario = Scenario() with { Machines = [new("isolated-press", new(6, 3), MachineTypes.DiamondPress)] };
        scenario = scenario with { Board = scenario.Board with { Cells =
            (from y in Enumerable.Range(2, 3) from x in Enumerable.Range(5, 3)
             where x != 6 || y != 3
             select new CellDefinition(new(x, y), TerrainKind.SolidRock)).ToList() } };
        Assert.Empty(scenario.Setup!.RequiredLocations);
        Assert.Contains(PlayReadyScenarioSetup.Validate(scenario).Diagnostics, d => d.LocationId == "interactive/machine/isolated-press");
        scenario.Setup.RequiredLocations.Add(new("interactive/machine/isolated-press", "test", "isolated-press",
            ScenarioAccessClass.DeliberateSpecial, "Requires deliberate special access", [new("machine", ScenarioAccessCoverage.Any, [new(5, 2)])]));
        Assert.True(PlayReadyScenarioSetup.Validate(scenario).Valid);
    }

    [Fact]
    public void NewlyAddedUnreachableLootCannotHideBehindMissingDeclaration()
    {
        var scenario = Scenario() with { Loot = PopulationLoot.FromPreset("Classic"), LootMarkers = [new("isolated-loot", new(7, 4))] };
        scenario.Board.Edges.AddRange([new(new(6, 4), EdgeDirection.Right, EdgeKind.Wall), new(new(7, 3), EdgeDirection.Down, EdgeKind.Wall)]);
        var result = PlayReadyScenarioSetup.Validate(scenario);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.LocationId == "interactive/loot/isolated-loot" && d.Positions!.Contains(new Cell(7, 4)));
    }

    [Fact]
    public void MachineOperatingAccessIsRecomputedFromCurrentEngineGeometry()
    {
        var scenario = Scenario() with { Machines = [new("test-press", new(6, 3), MachineTypes.DiamondPress)] };
        var declared = RequiredLocations.ForInteractives(GameEngine.CreateGame(scenario), "interactive");
        var machine = Assert.Single(declared);
        scenario.Setup!.RequiredLocations.Add(new(machine.Id, "civilization", machine.SourceId, ScenarioAccessClass.Ordinary,
            null, [new("machine", ScenarioAccessCoverage.Any, [new(5, 2)])]));
        Assert.True(PlayReadyScenarioSetup.Validate(scenario).Valid);
        // The stale declaration remains reachable, while every real operating position is blocked.
        scenario = scenario with { Board = scenario.Board with { Cells =
            (from y in Enumerable.Range(2, 3) from x in Enumerable.Range(5, 3)
             where (x != 6 || y != 3) && (x != 5 || y != 2)
             select new CellDefinition(new(x, y), TerrainKind.SolidRock)).ToList() } };
        var result = PlayReadyScenarioSetup.Validate(scenario);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.LocationId == machine.Id);
    }

    [Fact]
    public void MachineAnyCannotUseAnUndeclaredOperatingPosition()
    {
        var scenario = Scenario() with { Machines = [new("press", new(6, 3), MachineTypes.DiamondPress)] };
        scenario.Setup!.RequiredLocations.Add(new("interactive/machine/press", "civilization", "press", ScenarioAccessClass.Ordinary,
            null, [new("machine", ScenarioAccessCoverage.Any, [new(5, 2)])]));
        Assert.True(PlayReadyScenarioSetup.Validate(scenario).Valid);
        scenario = scenario with { Board = scenario.Board with { Cells =
            [new(new(6, 2), TerrainKind.SolidRock), new(new(5, 3), TerrainKind.SolidRock)] } };
        var state = GameEngine.CreateGame(scenario);
        var operating = MachineGeometry.PotentialOperatingPositions(state.Physical.Board, scenario.Machines![0]);
        Assert.DoesNotContain(new Cell(5, 2), operating);
        Assert.Contains(new Cell(7, 3), operating);
        Assert.False(PlayReadyScenarioSetup.Validate(scenario, state).Valid);
    }

    [Fact]
    public void MachineAllRetainsEveryDeclaredPositionObligation()
    {
        var scenario = Scenario() with { Machines = [new("press", new(6, 3), MachineTypes.DiamondPress)] };
        scenario.Setup!.RequiredLocations.Add(new("interactive/machine/press", "civilization", "press", ScenarioAccessClass.Ordinary,
            null, [new("machine", ScenarioAccessCoverage.All, [new(5, 3), new(7, 4)])]));
        Assert.True(PlayReadyScenarioSetup.Validate(scenario).Valid);
        scenario.Board.Edges.AddRange([new(new(6, 4), EdgeDirection.Right, EdgeKind.Wall), new(new(7, 3), EdgeDirection.Down, EdgeKind.Wall)]);
        var state = GameEngine.CreateGame(scenario);
        var reached = PlayReadyScenarioSetup.ReachableFromDeployment(scenario, state);
        Assert.Contains(3 * 8 + 5, reached);
        Assert.DoesNotContain(4 * 8 + 7, reached);
        Assert.False(PlayReadyScenarioSetup.Validate(scenario, state).Valid);
    }

    [Fact]
    public void SpecialInteractiveExceptionMustRetainItsActualSourceIdentity()
    {
        var scenario = Scenario() with { Machines = [new("press", new(6, 3), MachineTypes.DiamondPress)] };
        scenario.Setup!.RequiredLocations.Add(new("interactive/machine/press", "fixture", "wrong-source",
            ScenarioAccessClass.DeliberateSpecial, "Deliberate special-access test", [new("access", ScenarioAccessCoverage.Any, [new(5, 2)])]));
        var result = PlayReadyScenarioSetup.Validate(scenario);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.LocationId == "interactive/machine/press" && d.Message.Contains("identity"));
    }

    [Fact]
    public void MinimaCannotBeWeakenedAndCoreMustHaveOpenWidthTwoTranslations()
    {
        var scenario = Scenario();
        var weak = scenario with { Setup = scenario.Setup! with { Deployment = scenario.Setup.Deployment with { MinimumCore = 1 } } };
        Assert.False(PlayReadyScenarioSetup.Validate(weak).Valid);
        scenario.Board.Edges.AddRange(Enumerable.Range(0, 2).Select(y => new EdgeDefinition(new(1, y), EdgeDirection.Right, EdgeKind.ClosedDoor)));
        Assert.False(PlayReadyScenarioSetup.Validate(scenario).Valid);
    }

    [Theory]
    [InlineData(1)] [InlineData(5)] [InlineData(8)]
    public void GeneratedPublishIsDeterministicAndRevalidatesCapacity(int capacity)
    {
        var recipe = new WorldGenerationRecipe(new(0, 0, 12, 12), new(100, 0, 0), 123);
        var first = WorldGenerator.Generate(recipe with { HeroCapacity = capacity });
        var second = WorldGenerator.Generate(recipe with { HeroCapacity = capacity });
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(capacity, first.Definition.Setup!.Deployment.Capacity);
        Assert.True(PlayReadyScenarioSetup.Validate(first.Definition).Valid);
        Assert.Equal(5, WorldGenerator.Generate(recipe).Definition.Setup!.Deployment.Capacity);
    }

    [Fact]
    public void OccupancyInvalidatesDeploymentButNotOrdinaryGeographicAccess()
    {
        var scenario = Scenario();
        var state = GameEngine.CreateGame(scenario);
        state.Types.Add(UnitContent.Find(UnitTypeIds.Grunt)!);
        state.PlaceUnit(UnitTypeIds.Grunt, "test-occupant", "test", new(0, 0));
        Assert.Contains(PlayReadyScenarioSetup.Validate(scenario, state).Diagnostics, d => d.Path == "setup.deployment");
        var other = GameEngine.CreateGame(scenario);
        other.Types.Add(UnitContent.Find(UnitTypeIds.Grunt)!);
        other.PlaceUnit(UnitTypeIds.Grunt, "test-occupant", "test", new(5, 0));
        scenario.Setup!.RequiredLocations.Add(new("test/location", "test", "location", ScenarioAccessClass.Ordinary, null,
            [new("all", ScenarioAccessCoverage.All, [new(5, 0)])]));
        Assert.True(PlayReadyScenarioSetup.Validate(scenario, other).Valid);
    }
}
