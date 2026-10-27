using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ThreeViewportFixtureTests
{
    [Fact]
    public void TabletopFixtureProjectsAllResourcePiecesAndPhysicalEdgeVariants()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var json = File.ReadAllText(Path.Combine(root, "ProjectDelve.Web.Tests", "fixtures", "tabletop-scene.json"));
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(json));
        var view = BrowserProjection.Create(new(state, [], null));
        Assert.Equal(new[] { "banana", "coal", "diamond", "fruit", "mushroom", "shell" }, state.Resources.Select(r => r.Type.Id).Order().ToArray());
        Assert.Contains(state.Physical.Board.Edges, e => e.Kind == EdgeKind.WallWithWindow);
        Assert.Equal(2, view.Figures["dragon"].CellSpan);
        Assert.Equal(Posture.Lying, state.Physical.Figures.Single(f => f.Id == "dragon").Posture);
    }

    [Fact]
    public void TabletopFogFixtureObservesWholeDragonFromOnlyOneExploredFootprintCell()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var json = File.ReadAllText(Path.Combine(root, "ProjectDelve.Web.Tests", "fixtures", "tabletop-fog.json"));
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(json));
        var view = BrowserProjection.Create(new(state, [], null));
        Assert.Equal(4, view.Figures["dragon"].OccupiedCells.Count);
        Assert.Single(view.Figures["dragon"].OccupiedCells.Where(c => FogOfWar.IsExplored(state, "blue", c)));
        Assert.DoesNotContain(FogOfWar.Observe(state, "blue").Physical.Board.Machines, m => m.Id == "press");
    }

    [Fact]
    public void BrowserArtworkFixtureLoadsAndProjectsItsAuthoredObjects()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var json = File.ReadAllText(Path.Combine(root, "ProjectDelve.Web.Tests", "fixtures", "three-scene.json"));
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(json));
        var view = BrowserProjection.Create(new(state, [], null));
        Assert.Equal(2, view.Figures["dragon"].CellSpan);
        Assert.Equal(4, view.Figures["dragon"].OccupiedCells.Count);
        Assert.Contains(state.Physical.Board.Terrain, tile => tile.Kind == TerrainKind.SolidRock);
        Assert.Contains(state.Physical.Board.Terrain, tile => tile.Kind == TerrainKind.Rail);
        Assert.Contains(view.Machines, machine => machine.Id == "press" && machine.OwnerSide == "blue");
        Assert.Single(state.Resources);
        Assert.Single(state.CellMarkers);
        Assert.Contains(view.Objectives, objective => objective.Area is not null);
    }
}
