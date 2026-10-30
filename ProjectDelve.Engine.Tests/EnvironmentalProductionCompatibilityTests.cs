using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EnvironmentalProductionCompatibilityTests
{
    private static ScenarioDefinition Fixture(params CellDefinition[] cells) => new()
    {
        Board = new() { Width = 8, Height = 4, Cells = [.. cells] },
        UnitTypeIds = [UnitTypeIds.Wizard],
        Units = [new(UnitTypeIds.Wizard, "blue", new(0, 0), UnitId: "observer")],
        Agency = [new(UnitTypeIds.Wizard, "blue", ControllerKind.Human)],
        Objectives = [new("blue", ScenarioObjective.NoObjective)],
        Loot = LootSettings.Empty
    };

    private sealed class Dice : IRandomProvider
    {
        public int Calls { get; private set; }
        public int RollD8() => ++Calls % 8 + 1;
        public int RollD6() => throw new InvalidOperationException("Production requires d8.");
        public int WorldShuffleIndex(int exclusiveMax) => exclusiveMax - 1;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }

    [Theory]
    [InlineData(null, null, "coal-lava", 2)]
    [InlineData(null, true, "coal-lava", 2)]
    [InlineData(null, false, "ordinary-lava", 0)]
    [InlineData("lava", null, "coal-lava", 2)]
    [InlineData("lava", true, "coal-lava", 2)]
    [InlineData("lava", false, "ordinary-lava", 0)]
    [InlineData("ordinary-lava", null, "ordinary-lava", 0)]
    [InlineData("coal-lava", null, "coal-lava", 2)]
    public void LegacyImportsAndExplicitTypesPreserveMeaningAcrossDelve1(
        string? typeId, bool? coal, string resolvedId, int rolls)
    {
        var definition = Fixture(new CellDefinition(new(3, 2), typeId is null ? TerrainKind.Lava : TerrainKind.StoneFloor)
            { TerrainTypeId = typeId, CoalGeneration = coal });
        var json = ScenarioDefinitionJson.ToJson(definition);
        var imported = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(ScenarioDefinitionJson.FromJson(json)));
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(imported));
        var state = GameEngine.CreateGame(imported);
        var terrain = state.Physical.Board.TerrainDefinitionAt(new(3, 2));
        Assert.Equal(resolvedId, terrain.Id);
        Assert.False(terrain.Passable);
        Assert.False(terrain.BlocksLos);
        Assert.False(terrain.AllowsResourceMarkers);
        Assert.Equal("Lava", terrain.Presentation.Recipe);
        var dice = new Dice();
        var result = GameEngine.StartRound(state, dice, false);
        Assert.Equal(rolls, dice.Calls);
        Assert.Equal(rolls, result.Events.Count(e => e.Kind == "LavaCoalRolled"));
        Assert.Equal(coal, Assert.Single(result.State.Physical.Board.Terrain).CoalGeneration);
    }

    [Theory]
    [InlineData("ordinary-lava", false)]
    [InlineData("ordinary-lava", true)]
    [InlineData("coal-lava", false)]
    [InlineData("coal-lava", true)]
    [InlineData("fruit-tree", false)]
    public void LegacyCoalSwitchIsRejectedOnNewTypedReferences(string typeId, bool coal)
    {
        var definition = Fixture(new CellDefinition(new(3, 2), TerrainKind.StoneFloor)
            { TerrainTypeId = typeId, CoalGeneration = coal });
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(definition)));
    }

    [Theory]
    [InlineData(null, TerrainKind.Lava, "coal-lava")]
    [InlineData("lava", TerrainKind.StoneFloor, "coal-lava")]
    [InlineData("ordinary-lava", TerrainKind.StoneFloor, "ordinary-lava")]
    [InlineData("coal-lava", TerrainKind.StoneFloor, "coal-lava")]
    public void DefaultLavaResolvesWithoutChangingSparseStoneFloorOverride(string? typeId, TerrainKind legacy, string expected)
    {
        var original = Fixture(new CellDefinition(new(0, 0), TerrainKind.StoneFloor));
        original = original with { Board = original.Board with { DefaultTerrain = legacy, DefaultTerrainTypeId = typeId } };
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(original)));
        Assert.Equal(expected, state.Physical.Board.TerrainDefinitionAt(new(7, 3)).Id);
        Assert.Equal("stone-floor", state.Physical.Board.TerrainDefinitionAt(new(0, 0)).Id);
        Assert.Equal(32, state.Physical.Board.Terrain.Count);
    }

    // Recreate the previous Terrain Catalog milestone's runtime format. Tests own
    // this fixture rather than depending on mutable shipped scenario content.
    private static JsonObject VersionOneSave(GameState state)
    {
        var root = JsonSerializer.SerializeToNode(state)!.AsObject();
        var board = root["Physical"]!["Board"]!.AsObject();
        board.Remove("TerrainContentVersion");
        var definitions = board["TerrainTypes"]!.AsArray();
        for (var i = definitions.Count - 1; i >= 0; i--)
        {
            var definition = definitions[i]!.AsObject();
            var id = definition["Id"]!.GetValue<string>();
            if (id == "ordinary-lava") { definitions.RemoveAt(i); continue; }
            if (id == "coal-lava") { definition["Id"] = "lava"; definition["Name"] = "Lava"; }
            definition.Remove("Production");
            definition.Remove("LegacyProductionIdentity");
        }
        return root;
    }

    [Fact]
    public void OldRuntimeCatalogMigratesBothLavaMeaningsAndFruitWithoutChangingResolution()
    {
        var original = GameEngine.CreateGame(Fixture(
            new(new(2, 1), TerrainKind.Lava),
            new(new(4, 1), TerrainKind.StoneFloor) { TerrainTypeId = "lava", CoalGeneration = false },
            new(new(6, 1), TerrainKind.FruitTree)));
        var restored = JsonSerializer.Deserialize<GameState>(VersionOneSave(original).ToJsonString())!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(2, restored.Physical.Board.TerrainContentVersion);
        Assert.Equal("coal-lava", restored.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.Null(restored.Physical.Board.TerrainDefinitionAt(new(4, 1)).Production);
        Assert.Equal(ResourceTypes.Fruit.Id, Assert.IsType<DirectionalResourceProduction>(
            restored.Physical.Board.TerrainDefinitionAt(new(6, 1)).Production).ResourceTypeId);
        var expectedDice = new Dice();
        var actualDice = new Dice();
        var expected = GameEngine.StartRound(original, expectedDice, false);
        var actual = GameEngine.StartRound(restored, actualDice, false);
        Assert.Equal(4, actualDice.Calls);
        Assert.Equal(expectedDice.Calls, actualDice.Calls);
        Assert.Equal(JsonSerializer.Serialize(expected.Events), JsonSerializer.Serialize(actual.Events));
        Assert.Equal(JsonSerializer.Serialize(expected.State), JsonSerializer.Serialize(actual.State));
        var saved = JsonSerializer.Serialize(restored);
        Assert.Equal(saved, JsonSerializer.Serialize(JsonSerializer.Deserialize<GameState>(saved)));
    }

    [Theory]
    [InlineData("fruit-tree")]
    [InlineData("lava")]
    public void OldObservedCatalogMigratesOnlyDefinitionsItContained(string legacyId)
    {
        var original = GameEngine.CreateGame(Fixture(new CellDefinition(new(2, 1),
            legacyId == "lava" ? TerrainKind.Lava : TerrainKind.FruitTree)));
        var root = VersionOneSave(original);
        var definitions = root["Physical"]!["Board"]!["TerrainTypes"]!.AsArray();
        for (var i = definitions.Count - 1; i >= 0; i--)
            if (definitions[i]!["Id"]!.GetValue<string>() is var id && id != "stone-floor" && id != legacyId)
                definitions.RemoveAt(i);
        var restored = JsonSerializer.Deserialize<GameState>(root.ToJsonString())!;
        GameEngine.ValidateScenario(restored);
        Assert.NotNull(restored.Physical.Board.TerrainDefinitionAt(new(2, 1)).Production);
        Assert.Equal(legacyId == "lava" ? 3 : 2, restored.Physical.Board.TerrainTypes.Length);
        Assert.DoesNotContain(restored.Physical.Board.TerrainTypes, t => t.Id == "asphalt");
        Assert.DoesNotContain(restored.Physical.Board.TerrainTypes, t => t.Id == "solid-rock");
    }

    [Fact]
    public void PreCatalogRuntimeSaveStillUsesCanonicalCompatibilityRules()
    {
        var root = VersionOneSave(GameEngine.CreateGame(Fixture(new CellDefinition(new(3, 2), TerrainKind.Lava))));
        root["Physical"]!["Board"]!.AsObject().Remove("TerrainTypes");
        var restored = JsonSerializer.Deserialize<GameState>(root.ToJsonString())!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal("coal-lava", restored.Physical.Board.TerrainDefinitionAt(new(3, 2)).Id);
    }

    [Fact]
    public void ExplicitNewVersionDoesNotSilentlyRepairInvalidCanonicalProduction()
    {
        var root = JsonSerializer.SerializeToNode(GameEngine.CreateGame(Fixture()))!;
        var definitions = root["Physical"]!["Board"]!["TerrainTypes"]!.AsArray();
        definitions.Single(d => d!["Id"]!.GetValue<string>() == "fruit-tree")!.AsObject().Remove("Production");
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(JsonSerializer.Deserialize<GameState>(root.ToJsonString())!));
        root["Physical"]!["Board"]!["TerrainContentVersion"] = 999;
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<GameState>(root.ToJsonString()));
    }

    [Theory]
    [InlineData("passable")]
    [InlineData("blank-name")]
    public void LegacyCatalogMigrationDoesNotWeakenCanonicalValidation(string failure)
    {
        var root = VersionOneSave(GameEngine.CreateGame(Fixture()));
        var lava = root["Physical"]!["Board"]!["TerrainTypes"]!.AsArray()
            .Single(d => d!["Id"]!.GetValue<string>() == "lava")!;
        if (failure == "passable") lava["Passable"] = true;
        else lava["Name"] = " ";
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<GameState>(root.ToJsonString()));
    }
}
