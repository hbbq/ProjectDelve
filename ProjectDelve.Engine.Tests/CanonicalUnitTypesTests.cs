using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CanonicalUnitTypesTests
{
    [Fact]
    public void EnumerationAndScenarioResolutionUseTheSameRegistry()
    {
        var types = CanonicalUnitTypes.All.ToArray();
        var ids = typeof(UnitTypeIds).GetFields().Select(field => (string)field.GetRawConstantValue()!).ToArray();
        Assert.Equal(ids.Order(), types.Select(type => type.Id).Order());
        foreach (var type in types)
        {
            var resolved = UnitContent.Find(type.Id)!;
            Assert.Equal((type.Id, type.DisplayName, type.Hp, type.Footprint),
                (resolved.Id, resolved.DisplayName, resolved.Hp, resolved.Footprint));
            Assert.NotSame(type, resolved);
        }
        Assert.Null(CanonicalUnitTypes.Find("unknown"));
        Assert.Equal(4, FootprintGeometry.OccupiedCells(CanonicalUnitTypes.Find(UnitTypeIds.RedDragon)!.Footprint, new(0, 0)).Count);
    }

    [Fact]
    public void AuthoringPathsComeFromTheExistingCreationFailure()
    {
        var definition = Scenario.Define(Scenario.Map(5, 5),
            [Scenario.Group(UnitTypeIds.Grunt, "side", ControllerKind.Human, Scenario.At(1, 1), Scenario.At(1, 1))]);
        var error = Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
        Assert.Equal("units[1]", ScenarioValidationContext.PathOf(error));
        Assert.Equal("Invalid Unit placement.", error.Message);
        var loadedError = Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition)));
        Assert.Equal("units[1]", ScenarioValidationContext.PathOf(loadedError));
    }

    [Fact]
    public void HostsCanRejectBeforeCreationWithoutChangingEngineBoardRules()
    {
        var definition = Scenario.Define(Scenario.Map(51, 1), []);
        Assert.Equal(51, GameEngine.CreateGame(definition).Physical.Board.Width);
        var rejected = false;
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition), draft =>
        {
            rejected = true; throw new ArgumentException("Host policy");
        }));
        Assert.True(rejected);
    }
}
