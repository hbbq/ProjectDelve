using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ThreeViewportFixtureTests
{
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
