using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class GravelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultAndOverrideRoundTripAndAllowOccupation(bool asDefault)
    {
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 3, Height = 2,
                DefaultTerrain = asDefault ? TerrainKind.Gravel : TerrainKind.StoneFloor,
                Cells = [new(new(0, 0), TerrainKind.Gravel)] },
            UnitTypeIds = [UnitTypeIds.Grunt],
            Units = [new(UnitTypeIds.Grunt, "blue", new(0, 0))],
            Agency = [new(UnitTypeIds.Grunt, "blue", ControllerKind.Human)]
        };
        var loaded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(
            ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition))));
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(loaded));
        var state = GameEngine.CreateGame(loaded);
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(TerrainKind.Gravel, restored.Physical.Board.TerrainAt(new(0, 0)));
        Assert.Null(restored.Physical.Board.Terrain.Single(t => t.Position == new Cell(0, 0)).Direction);
        Assert.Equal(asDefault ? TerrainKind.Gravel : TerrainKind.StoneFloor, restored.Physical.Board.TerrainAt(new(2, 1)));
        Assert.Single(restored.Physical.Figures);
        var directional = loaded with { Board = loaded.Board with { Cells = [new(new(0, 0), TerrainKind.Gravel, RailDirection.East)] } };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(directional));
    }
}
