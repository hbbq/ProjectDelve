using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class TerrainScenarioTests
{
    private const string Json = """
    {
      "terrainTypes": [
        { "id": "unfamiliar-17", "name": "Velvet Horizon", "passable": false, "blocksLos": false,
          "presentation": { "recipe": "not-an-installed-recipe" } },
        { "id": "paper", "name": "Paper", "passable": true, "blocksLos": false }
      ],
      "board": { "width": 4, "height": 3,
        "cells": [{ "position": { "x": 2, "y": 1 }, "terrainTypeId": "unfamiliar-17" }] },
      "unitTypeIds": ["wizard-type"],
      "units": [{ "unitTypeId": "wizard-type", "unitId": "wizard", "sideId": "blue", "anchor": { "x": 0, "y": 1 } }],
      "agency": [{ "unitTypeId": "wizard-type", "sideId": "blue", "controller": "Human" }],
      "objectives": [{ "sideId": "blue", "objective": "NoObjective" }],
      "loot": { "composition": [] }
    }
    """;

    [Fact]
    public void LocalDefinitionsAndReferencesSurviveScenarioTransportAndRuntimeSave()
    {
        var definition = ScenarioDefinitionJson.FromJson(Json);
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(restored));
        var state = GameEngine.CreateGame(restored);
        var saved = JsonSerializer.Serialize(state);
        var resumed = JsonSerializer.Deserialize<GameState>(saved)!;
        GameEngine.ValidateScenario(resumed);
        Assert.Equal(saved, JsonSerializer.Serialize(resumed));
        var terrain = resumed.Physical.Board.TerrainDefinitionAt(new(2, 1));
        Assert.Equal("Velvet Horizon", terrain.Name);
        Assert.False(terrain.Passable);
        Assert.False(terrain.BlocksLos);
        Assert.Equal("not-an-installed-recipe", terrain.Presentation.Recipe);
        Assert.Equal("stone-floor", resumed.Physical.Board.TerrainDefinitionAt(new(0, 0)).Id);
        Assert.DoesNotContain(TerrainCatalog.All, t => t.Id == "unfamiliar-17");
        Assert.DoesNotContain("\"terrain\"", ScenarioDefinitionJson.ToJson(definition));
    }

    [Fact]
    public void TypedDefaultMaterializesEveryCellAndExplicitLegacyOverrideWins()
    {
        var definition = ScenarioDefinitionJson.FromJson(Json);
        definition = definition with { Board = definition.Board with {
            DefaultTerrainTypeId = "paper", Cells = [new(new(3, 2), TerrainKind.Tree)] } };
        var board = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition))).Physical.Board;
        Assert.Equal(12, board.Terrain.Count);
        Assert.Equal("paper", board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.Equal("tree", board.TerrainDefinitionAt(new(3, 2)).Id);
    }

    [Fact]
    public void CanonicalRosterHasEquivalentJsonRepresentation()
    {
        var json = JsonSerializer.Serialize(TerrainCatalog.All);
        var loaded = JsonSerializer.Deserialize<ImmutableArray<TerrainDefinition>>(json);
        TerrainCatalog.Validate(loaded);
        Assert.Equal(json, JsonSerializer.Serialize(loaded));
        var board = new Board(3, 1, []) { TerrainTypes = loaded,
            Terrain = [new(new(0, 0)) { TypeId = "lava" }, new(new(1, 0)) { TypeId = "asphalt" }] };
        Assert.False(board.TerrainDefinitionAt(new(0, 0)).Passable);
        Assert.False(board.TerrainDefinitionAt(new(0, 0)).AllowsResourceMarkers);
        Assert.Contains(TerrainCategories.RacingGround, board.TerrainDefinitionAt(new(1, 0)).Categories);
    }

    [Theory]
    [InlineData("unknown-cell")]
    [InlineData("unknown-default")]
    [InlineData("duplicate")]
    [InlineData("canonical-shadow")]
    [InlineData("legacy-spoof")]
    [InlineData("rail-direction")]
    [InlineData("coal-toggle")]
    public void InvalidResolvedTerrainIsRejected(string failure)
    {
        var node = JsonNode.Parse(Json)!;
        switch (failure)
        {
            case "unknown-cell": node["board"]!["cells"]![0]!["terrainTypeId"] = "absent"; break;
            case "unknown-default": node["board"]!["defaultTerrainTypeId"] = "absent"; break;
            case "duplicate": node["terrainTypes"]!.AsArray().Add(node["terrainTypes"]![0]!.DeepClone()); break;
            case "canonical-shadow": node["terrainTypes"]![0]!["id"] = "lava"; break;
            case "legacy-spoof": node["terrainTypes"]![0]!["legacyKind"] = "Lava"; break;
            case "rail-direction": node["board"]!["cells"]![0]!["direction"] = "North"; break;
            case "coal-toggle": node["board"]!["cells"]![0]!["coalGeneration"] = false; break;
        }
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(node.ToJsonString()));
    }

    [Theory]
    [InlineData("missing-property")]
    [InlineData("unknown-property")]
    [InlineData("invalid-boolean")]
    [InlineData("missing-reference")]
    [InlineData("mixed-reference")]
    [InlineData("null-reference")]
    [InlineData("numeric-reference")]
    public void MalformedTerrainAuthoringDoesNotSilentlyDefault(string failure)
    {
        var node = JsonNode.Parse(Json)!;
        switch (failure)
        {
            case "missing-property": node["terrainTypes"]![0]!.AsObject().Remove("passable"); break;
            case "unknown-property": node["terrainTypes"]![0]!["produce"] = "coal"; break;
            case "invalid-boolean": node["terrainTypes"]![0]!["blocksLos"] = "yes"; break;
            case "missing-reference": node["board"]!["cells"]![0]!.AsObject().Remove("terrainTypeId"); break;
            case "mixed-reference": node["board"]!["cells"]![0]!["terrain"] = "Grass"; break;
            case "null-reference": node["board"]!["cells"]![0]!["terrainTypeId"] = null; break;
            case "numeric-reference": node["board"]!["cells"]![0]!["terrainTypeId"] = 17; break;
        }
        Assert.Throws<JsonException>(() => ScenarioDefinitionJson.FromJson(node.ToJsonString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void TypedCanonicalLavaKeepsLegacyPlacementSetting(bool? coal)
    {
        var definition = ScenarioDefinitionJson.FromJson(Json);
        definition = definition with { Board = definition.Board with { Cells = [
            new(new(2, 1), TerrainKind.StoneFloor) { TerrainTypeId = "lava", CoalGeneration = coal }] } };
        var board = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition))).Physical.Board;
        Assert.Equal(TerrainKind.Lava, board.TerrainAt(new(2, 1)));
        Assert.Equal(coal, Assert.Single(board.Terrain).CoalGeneration);
        Assert.False(board.TerrainDefinitionAt(new(2, 1)).Passable);
    }
}
