using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class AsphaltProjectionTests
{
    [Fact]
    public void AsphaltRemainsAuthoritativeInBrowserPlaybackAndGambit()
    {
        var state = GameEngine.CreateGame(WebScenarioFixtures.Combat());
        state.Physical.Board.Terrain.Clear();
        state.Physical.Board.Terrain.Add(new(new(0, 0), TerrainKind.Asphalt));
        var result = new EngineResult(state, [], null) { ResolutionSteps = [new(0, state)] };
        var browser = BrowserProjection.Create(result);
        Assert.NotEmpty(browser.ResolutionSteps);
        var observation = GambitProjection.Observation(state);
        var tile = Assert.Single(observation.Board.Terrain);
        Assert.Equal(TerrainKind.Asphalt, tile.Kind);
        Assert.Null(tile.Direction);
        Assert.Contains("a=Asphalt", observation.AsciiBoard);
        Assert.Contains("\"kind\":\"Asphalt\"", JsonSerializer.Serialize(observation, GambitJson.Options));
        Assert.NotEqual(BoardAppearance.DeriveSeed("asphalt-fixture", state.Physical.Board),
            BoardAppearance.DeriveSeed("asphalt-fixture", state.Physical.Board with { Terrain = [] }));
    }
}
