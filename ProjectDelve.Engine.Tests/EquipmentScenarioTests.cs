using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class EquipmentScenarioTests
{
    private static ScenarioDefinition Example() => new()
    {
        Board = new() { Width = 4, Height = 3 },
        UnitTypeIds = ["local-collector"],
        UnitTypes = [new("local-collector", 3, 1, 1, 0, 2) { CanHaveEquipment = true, Unique = true }],
        Units = [new("local-collector", "blue", new(0, 0), UnitId: "bearer")
            { Inventory = [new("owned-17", "amber-sandals")] }],
        Agency = [new("local-collector", "blue", ControllerKind.Human)],
        EquipmentTypes = [new("amber-sandals", "Sandals of a Very Different Name")
            { Modifiers = [new(Stat.Mov, 2), new(Stat.Def, 1)], Capabilities = [EquipmentCapability.Phase] }],
        GroundObjects = [new(new("ground-23", "amber-sandals"), new(2, 0))],
        Loot = new() { Composition = [new("amber-sandals", 2)] }
    };

    [Fact]
    public void JsonCanDeclareEquipmentReferencesWithoutAnyLootCardKind()
    {
        const string json = """
        {
          "board": { "width": 3, "height": 2 },
          "unitTypeIds": ["collector"],
          "unitTypes": [{ "id": "collector", "mov": 2, "rng": 1, "atk": 1, "def": 1, "hp": 1, "canHaveEquipment": true }],
          "equipmentTypes": [{ "id": "silvery-object", "name": "Unfamiliar Equipment", "modifiers": [{ "stat": "Mov", "amount": 1 }],
            "abilities": [{ "id": "wake", "name": "Wake Up", "mechanic": "StandAdjacentFriendlyLyingUnit" }] }],
          "units": [{ "unitTypeId": "collector", "sideId": "blue", "anchor": { "x": 0, "y": 0 }, "unitId": "bearer",
            "inventory": [{ "id": "owned", "equipmentTypeId": "silvery-object" }] }],
          "agency": [{ "unitTypeId": "collector", "sideId": "blue", "controller": "Human" }],
          "loot": { "composition": [{ "equipmentTypeId": "silvery-object", "count": 1 }] },
          "groundObjects": [{ "object": { "id": "ground", "equipmentTypeId": "silvery-object" }, "position": { "x": 2, "y": 1 } }]
        }
        """;
        var scenario = ScenarioDefinitionJson.FromJson(json);
        var state = GameEngine.CreateGame(scenario);
        Assert.Equal(3, state.EffectiveMovOf("bearer"));
        Assert.Equal("silvery-object", Assert.Single(state.LootDeck!.DrawPile).EquipmentTypeId);
        Assert.Equal("silvery-object", Assert.Single(state.GroundObjects).Object.EquipmentTypeId);
        Assert.Equal("Wake Up", Assert.Single(Assert.Single(scenario.EquipmentTypes!).Abilities).Name);
    }

    [Fact]
    public void LocalEquipmentAndUnitDefinitionsSurviveJsonAndTransportTogether()
    {
        var authored = Example();
        var json = ScenarioDefinitionJson.ToJson(authored);
        var loaded = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(
            ScenarioDefinitionJson.FromJson(json)));
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(loaded));
        Assert.Equal("local-collector", Assert.Single(loaded.UnitTypes!).Id);
        var equipment = Assert.Single(loaded.EquipmentTypes!);
        Assert.Equal("amber-sandals", equipment.Id);
        Assert.Equal("Sandals of a Very Different Name", equipment.Name);
        Assert.Equal(new[] { new EquipmentStatModifier(Stat.Mov, 2), new EquipmentStatModifier(Stat.Def, 1) }, equipment.Modifiers);
        var state = GameEngine.CreateGame(loaded);
        Assert.Equal(5, state.EffectiveMovOf("bearer"));
        Assert.Equal(1, state.EffectiveDefOf("bearer"));
        Assert.Equal("owned-17", Assert.Single(state.Units[0].Inventory).Id);
        Assert.Equal("ground-23", Assert.Single(state.GroundObjects).Object.Id);
        Assert.All(state.LootDeck!.DrawPile, card => Assert.Equal("amber-sandals", card.EquipmentTypeId));
    }

    [Fact]
    public void LocalDefinitionsAreScopedAndIndependentFromCanonicalContent()
    {
        var firstDefinition = Example();
        var secondDefinition = Example() with
        {
            EquipmentTypes = [new("amber-sandals", "Another Scenario's Sandals")
                { Modifiers = [new(Stat.Mov, 4)] }]
        };
        var first = GameEngine.CreateGame(firstDefinition);
        var second = GameEngine.CreateGame(secondDefinition);
        Assert.Equal(5, first.EffectiveMovOf("bearer"));
        Assert.Equal(7, second.EffectiveMovOf("bearer"));
        second.EquipmentTypes.Clear();
        Assert.Equal(5, first.EffectiveMovOf("bearer"));
        Assert.DoesNotContain(EquipmentCatalog.Resolve(null), e => e.Id == "amber-sandals");
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(firstDefinition with { EquipmentTypes = null }));
        var canonical = EquipmentCatalog.Resolve(null)[0];
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(firstDefinition with
            { EquipmentTypes = [canonical with { Name = "Attempted replacement" }] }));
    }

    [Fact]
    public void LocalContentSurvivesStateSaveAndCopyWithoutSharingMutableCatalogs()
    {
        var state = GameEngine.CreateGame(Example());
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(5, restored.EffectiveMovOf("bearer"));
        Assert.Equal(state.Units[0].Inventory.ToArray(), restored.Units[0].Inventory.ToArray());
        Assert.Equal(state.LootDeck!.DrawPile, restored.LootDeck!.DrawPile);
        Assert.Equal(state.GroundObjects, restored.GroundObjects);
        var copied = state.Copy();
        copied.EquipmentTypes.Clear();
        Assert.Equal(5, state.EffectiveMovOf("bearer"));
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("unknown-capability")]
    [InlineData("duplicate-type")]
    [InlineData("unknown-reference")]
    public void JsonUsesTheSameValidationAsAuthoredContent(string invalid)
    {
        var node = JsonNode.Parse(ScenarioDefinitionJson.ToJson(Example()))!;
        if (invalid == "missing-id") node["equipmentTypes"]![0]!["id"] = "";
        if (invalid == "unknown-capability") node["equipmentTypes"]![0]!["capabilities"]![0] = "Teleport";
        if (invalid == "duplicate-type") node["equipmentTypes"]!.AsArray().Add(node["equipmentTypes"]![0]!.DeepClone());
        if (invalid == "unknown-reference") node["units"]![0]!["inventory"]![0]!["equipmentTypeId"] = "not-declared";
        if (invalid == "unknown-capability")
            Assert.Throws<JsonException>(() => ScenarioDefinitionJson.FromJson(node.ToJsonString()));
        else
            Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(node.ToJsonString()));
    }
}
