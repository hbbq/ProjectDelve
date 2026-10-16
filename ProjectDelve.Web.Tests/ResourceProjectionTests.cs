using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ResourceProjectionTests
{
    [Fact]
    public void PlaytestScenarioAddsCoalWithoutChangingOriginalSetupAndRestartsFresh()
    {
        var original = PlaytestScenarios.Definition("wizard-doors");
        var definition = PlaytestScenarios.Definition("resource-transport");
        Assert.Null(original.Resources);
        Assert.Equal(ScenarioDefinitionJson.ToJson(original),
            ScenarioDefinitionJson.ToJson(definition with { Resources = null }));
        var state = PlaytestScenarios.Create("resource-transport");
        Assert.Equal(6, state.Resources.Count);
        Assert.All(state.Resources, resource => Assert.Equal(ResourceTypes.Coal, resource.Type));
        Assert.All(state.Units.Where(unit => unit.TypeId == UnitTypeIds.Zombie), unit =>
            Assert.Contains(state.Resources, resource => resource.Position == state.Physical.Figures.Single(f => f.Id == unit.Id).Position));
        var decoded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal(state.Resources, GameEngine.CreateGame(decoded).Resources);
        state.Resources.Clear();
        Assert.Equal(6, PlaytestScenarios.Create("resource-transport").Resources.Count);
        Assert.Empty(PlaytestScenarios.Create("wizard-doors").Resources);
    }

    [Fact]
    public void BrowserAndGambitDeliverAuthoritativeResourcesAndCapability()
    {
        var state = PlaytestScenarios.Create("resource-transport");
        var observed = GambitProjection.Observation(state);
        Assert.Equal(state.Resources, observed.Resources);
        state.Resources.Clear();
        Assert.Equal(6, observed.Resources.Count);
        var browser = new GameResponse(1, new(PlaytestScenarios.Create("resource-transport"), [], null), false, "resource-transport");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(browser));
        Assert.Equal(6, json.RootElement.GetProperty("Result").GetProperty("State").GetProperty("Resources").GetArrayLength());
        var zombieId = browser.Result.State.Units.First(unit => unit.TypeId == UnitTypeIds.Zombie).Id;
        Assert.Contains(browser.Presentation.Cards[zombieId].Entries, entry => entry.Content.Id == "transport-resource");
    }
}
