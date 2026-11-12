using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DirtTerrainTests
{
    [Fact]
    public void CanonicalDirtIsAnOrdinaryGrassEquivalentWithoutALegacyIdentity()
    {
        var dirt = TerrainCatalog.Find(TerrainCatalog.All, "dirt");
        var grass = TerrainCatalog.Find(TerrainCatalog.All, "grass");
        Assert.Equal(grass.Passable, dirt.Passable);
        Assert.Equal(grass.BlocksLos, dirt.BlocksLos);
        Assert.Equal(grass.AllowsResourceMarkers, dirt.AllowsResourceMarkers);
        Assert.Equal(new[] { TerrainCategories.Land }, dirt.Categories);
        Assert.Null(dirt.Production);
        Assert.Null(dirt.LegacyKind);
        Assert.Equal("Dirt", dirt.Presentation.Recipe);
    }

    [Fact]
    public void TypedDirtSupportsMovementPlacementLosResourcesAndPortableScenarios()
    {
        var definition = Scenario.Define(Scenario.Map(6, 1,
            [new(new(2, 0), TerrainKind.StoneFloor) { TerrainTypeId = "dirt" }]),
            [Scenario.Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, Scenario.At(0, 0)),
             Scenario.Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, Scenario.At(5, 0))]);
        var state = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition)));
        var actor = state.Units.First(u => u.SideId == "blue").Id;
        var paths = MovementRules.FindPaths(state, actor, new(0, 0));
        Assert.Contains(new Cell(2, 0), paths.Keys);
        Assert.Contains(new Cell(3, 0), paths.Keys);
        Assert.True(SpatialRules.CanPlaceUnit(state, new Cell(2, 0)));
        Assert.True(AttackRules.HasGeometricLineOfSight(state.Physical.Board, new(0, 0), new(5, 0)));
        Assert.True(ResourceRules.CanCreateAt(state, new(2, 0)));
        Assert.Equal("dirt", state.Physical.Board.TerrainDefinitionAt(new(2, 0)).Id);
    }
}
