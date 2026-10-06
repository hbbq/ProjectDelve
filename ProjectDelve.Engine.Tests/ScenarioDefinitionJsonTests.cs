using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectDelve.Engine;
using Xunit;
using static ProjectDelve.Engine.Scenario;

namespace ProjectDelve.Engine.Tests;

public sealed class ScenarioDefinitionJsonTests
{
    private static ScenarioDefinition Example() => Define(
        Map(6, 5, defaultTerrain: TerrainKind.Grass,
            cells: [Tile(4, 3, TerrainKind.Tree), Tile(5, 4, TerrainKind.StoneFloor)],
            edges: [Edge(1, 2, EdgeDirection.Right, EdgeKind.ClosedDoor),
                Edge(3, 2, EdgeDirection.Down, EdgeKind.OpenDoor)]),
        [Group(UnitTypeIds.Grunt, "side / amber 17", ControllerKind.Human, At(0, 0)),
            Group(UnitTypeIds.Grunt, "violet-99", ControllerKind.Automated, At(2, 0)),
            Group(UnitTypeIds.Wizard, "side / amber 17", ControllerKind.Human, At(0, 2, Posture.Lying, initialHp: 2)),
            Group(UnitTypeIds.Goblin, "violet-99", ControllerKind.Automated)],
        unitTypeIds: [UnitTypeIds.Goblin, UnitTypeIds.Wizard, UnitTypeIds.Grunt]);

    [Fact]
    public void ConcreteDefinitionRoundTripPreservesOrderDefaultsOverridesAndPlacements()
    {
        var authored = Example();
        var json = ScenarioDefinitionJson.ToJson(authored);
        var loaded = ScenarioDefinitionJson.FromJson(json);
        Assert.Equal(authored.UnitTypeIds, loaded.UnitTypeIds);
        Assert.Equal((6, 5, TerrainKind.Grass), (loaded.Board.Width, loaded.Board.Height, loaded.Board.DefaultTerrain));
        Assert.Equal(authored.Board.Cells, loaded.Board.Cells);
        Assert.Equal(authored.Board.Edges, loaded.Board.Edges);
        Assert.Equal(authored.Units, loaded.Units);
        Assert.Equal(authored.Agency, loaded.Agency);
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(loaded));

        var state = GameEngine.CreateGame(loaded);
        Assert.Equal(loaded.UnitTypeIds, state.Types.Select(t => t.Id));
        Assert.DoesNotContain(state.Units, u => u.TypeId == UnitTypeIds.Goblin);
        Assert.Equal(new[] { "side / amber 17", "violet-99", "side / amber 17" }, state.Units.Select(u => u.SideId));
        Assert.Equal(2, state.Units[2].CurrentHp);
        Assert.Equal(Posture.Lying, state.Physical.Figures[2].Posture);
        Assert.Equal(ControllerKind.Human, state.ControllerFor(new ActivationToken(UnitTypeIds.Grunt, "side / amber 17")));
        Assert.Equal(ControllerKind.Automated, state.ControllerFor(new ActivationToken(UnitTypeIds.Grunt, "violet-99")));
        Assert.Equal(TerrainKind.Grass, state.Physical.Board.TerrainAt(new(0, 0)));
        Assert.Equal(TerrainKind.Tree, state.Physical.Board.TerrainAt(new(4, 3)));
        Assert.Equal(EdgeKind.ClosedDoor, state.Physical.Board.EdgeBetween(new(1, 2), new(2, 2)));
        Assert.Equal(EdgeKind.OpenDoor, state.Physical.Board.EdgeBetween(new(3, 2), new(3, 3)));
    }

    [Fact]
    public void JsonContainsConcreteDataAndSymbolicEnums()
    {
        using var doc = JsonDocument.Parse(ScenarioDefinitionJson.ToJson(Example()));
        var root = doc.RootElement;
        Assert.Equal(new[] { "board", "unitTypeIds", "units", "agency" }, root.EnumerateObject().Select(p => p.Name));
        var board = root.GetProperty("board");
        Assert.Equal(new[] { "width", "height", "defaultTerrain", "cells", "edges" }, board.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Grass", board.GetProperty("defaultTerrain").GetString());
        Assert.Equal("Tree", board.GetProperty("cells")[0].GetProperty("terrain").GetString());
        Assert.Equal("Right", board.GetProperty("edges")[0].GetProperty("direction").GetString());
        Assert.Equal("ClosedDoor", board.GetProperty("edges")[0].GetProperty("kind").GetString());
        Assert.Equal("Down", board.GetProperty("edges")[1].GetProperty("direction").GetString());
        Assert.Equal("OpenDoor", board.GetProperty("edges")[1].GetProperty("kind").GetString());
        Assert.Equal("Upright", root.GetProperty("units")[0].GetProperty("posture").GetString());
        Assert.Equal("Lying", root.GetProperty("units")[2].GetProperty("posture").GetString());
        Assert.Equal("Human", root.GetProperty("agency")[0].GetProperty("controller").GetString());
        Assert.Equal("Automated", root.GetProperty("agency")[1].GetProperty("controller").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    public void OptionalInitialHpOmitsOnlyNullAndPreservesStartingHp(int? initialHp)
    {
        var definition = Define(Map(3, 3), [
            Group(UnitTypeIds.Wizard, "blue", ControllerKind.Human, At(0, 0, initialHp: initialHp))
        ]);
        var json = ScenarioDefinitionJson.ToJson(definition);
        using var doc = JsonDocument.Parse(json);
        var placement = doc.RootElement.GetProperty("units")[0];
        if (initialHp is { } hp)
            Assert.Equal(hp, placement.GetProperty("initialHp").GetInt32());
        else
            Assert.False(placement.TryGetProperty("initialHp", out _));
        Assert.Equal("Upright", placement.GetProperty("posture").GetString());
        Assert.Equal("StoneFloor", doc.RootElement.GetProperty("board").GetProperty("defaultTerrain").GetString());

        var loaded = ScenarioDefinitionJson.FromJson(json);
        Assert.Equal(initialHp, Assert.Single(loaded.Units).InitialHp);
        var state = GameEngine.CreateGame(loaded);
        Assert.Equal(initialHp ?? Assert.Single(state.Types).Hp, Assert.Single(state.Units).CurrentHp);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void InvalidJsonFailsClearly(string json) =>
        Assert.Throws<JsonException>(() => ScenarioDefinitionJson.FromJson(json));

    [Theory]
    [InlineData("enum-number")]
    [InlineData("enum-name")]
    [InlineData("missing-position")]
    public void InvalidJsonValuesFailDuringParsing(string invalid)
    {
        var data = JsonNode.Parse(ScenarioDefinitionJson.ToJson(Example()))!;
        if (invalid == "enum-number") data["board"]!["defaultTerrain"] = 0;
        if (invalid == "enum-name") data["units"]![0]!["posture"] = "Flying";
        if (invalid == "missing-position") data["board"]!["cells"]![0]!.AsObject().Remove("position");
        Assert.Throws<JsonException>(() => ScenarioDefinitionJson.FromJson(data.ToJsonString()));
    }

    [Theory]
    [InlineData("null-board")]
    [InlineData("dimensions")]
    [InlineData("duplicate-edge")]
    [InlineData("unknown-type")]
    [InlineData("agency")]
    [InlineData("overlap")]
    [InlineData("outside")]
    [InlineData("impassable")]
    [InlineData("hp")]
    public void ParsedModelsStillUseExistingScenarioValidation(string invalid)
    {
        var definition = Example();
        var board = definition.Board;
        definition = invalid switch
        {
            "null-board" => definition with { Board = null! },
            "dimensions" => definition with { Board = board with { Width = 0 } },
            "duplicate-edge" => definition with { Board = board with { Edges = [board.Edges[0], board.Edges[0]] } },
            "unknown-type" => definition with { UnitTypeIds = [.. definition.UnitTypeIds, "unknown"] },
            "agency" => definition with { Agency = [] },
            "overlap" => definition with { Units = [.. definition.Units, definition.Units[0]] },
            "outside" => definition with { Units = [definition.Units[0] with { Anchor = new(6, 0) }] },
            "impassable" => definition with { Units = [definition.Units[0] with { Anchor = new(4, 3) }] },
            "hp" => definition with { Units = [definition.Units[2] with { InitialHp = 99 }] },
            _ => throw new InvalidOperationException(invalid)
        };
        var expected = Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition));
        var data = JsonNode.Parse(ScenarioDefinitionJson.ToJson(definition))!;
        // Exercise an explicit JSON null; ToJson now omits null properties when writing.
        if (invalid == "null-board") data["board"] = null;
        var actual = Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(data.ToJsonString()));
        Assert.Equal(expected.Message, actual.Message);
    }
}
