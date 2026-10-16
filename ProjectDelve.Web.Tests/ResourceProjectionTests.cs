using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ResourceProjectionTests
{
    [Fact]
    public void ResourceScenarioPreservesAuthoredMarkersAndRestartsFresh()
    {
        var definition = WebScenarioFixtures.Resources();
        var state = GameEngine.CreateGame(WebScenarioFixtures.Resources());
        Assert.NotEmpty(state.Resources);
        Assert.Equal(definition.Resources, state.Resources);
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal(state.Resources, GameEngine.CreateGame(decoded).Resources);
        state.Resources.Clear();
        Assert.Equal(definition.Resources, GameEngine.CreateGame(WebScenarioFixtures.Resources()).Resources);
    }

    [Fact]
    public void BrowserAndGambitDeliverAuthoritativeResourcesAndCapability()
    {
        var state = GameEngine.CreateGame(WebScenarioFixtures.Resources());
        var observed = GambitProjection.Observation(state);
        Assert.Equal(state.Resources, observed.Resources);
        state.Resources.Clear();
        Assert.Equal(WebScenarioFixtures.Resources().Resources, observed.Resources);
        var browser = new GameResponse(1, new(GameEngine.CreateGame(WebScenarioFixtures.Resources()), [], null), false, "fixture-resources");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(browser));
        Assert.Equal(observed.Resources.Count, json.RootElement.GetProperty("Result").GetProperty("State").GetProperty("Resources").GetArrayLength());
        var carrierSetup = GameEngine.CreateGame(Scenario.Define(Scenario.Map(2, 2),
            [Scenario.Group(UnitTypeIds.Zombie, "red", ControllerKind.Human, Scenario.At(0, 0, unitId: "carrier"))]));
        Assert.Contains(BrowserProjection.Cards(carrierSetup)["carrier"].Entries, entry => entry.Content.Id == "transport-resource");
    }
}
