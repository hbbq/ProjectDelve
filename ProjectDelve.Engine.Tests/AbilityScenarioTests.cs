using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AbilityScenarioTests
{
    private sealed class FixedRandom : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public int LootDrawIndex(int exclusiveMax) => 0;
        public int RollD6() => 1;
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
    }
    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private const string LocalContent = """
    {
      "board": { "width": 4, "height": 3 },
      "unitTypeIds": ["local-bearer"],
      "unitTypes": [{ "id": "local-bearer", "displayName": "Local Bearer", "mov": 3, "rng": 1, "atk": 1, "def": 0, "hp": 1,
        "canHaveEquipment": true, "canHaveItems": true,
        "localAbilities": [
          { "key": "first-wave", "name": "Handwritten Prayer", "timing": "Action", "usage": {"maxUses": 2}, "mechanic": {"kind": "holyWave"} },
          { "key": "second-wave", "name": "Handwritten Prayer", "timing": "Action", "usage": {"maxUses": 2}, "mechanic": {"kind": "holyWave"} }] }],
      "equipmentTypes": [{ "id": "local-reliquary", "name": "Local Reliquary", "localAbilities": [
        { "key": "chant", "name": "Unexpected Silence", "timing": "Action", "mechanic": {"kind": "holyWave"} }] }],
      "itemTypes": [{ "id": "local-scroll", "name": "Local Scroll", "localAbilities": [
        { "key": "read", "name": "Unexpected Silence", "timing": "Action", "mechanic": {"kind": "holyWave"} }] }],
      "lootCardDefinitions": [{ "id": "local-scroll-card", "name": "Sealed Scroll", "itemTypeId": "local-scroll" }],
      "units": [{ "unitTypeId": "local-bearer", "sideId": "blue", "anchor": { "x": 0, "y": 0 }, "unitId": "bearer",
        "inventory": [{ "id": "reliquary-1", "equipmentTypeId": "local-reliquary" }, { "id": "scroll-1", "itemTypeId": "local-scroll" }, { "id": "scroll-2", "itemTypeId": "local-scroll" }] }],
      "agency": [{ "unitTypeId": "local-bearer", "sideId": "blue", "controller": "Human" }],
      "objectives": [{ "sideId": "blue", "objective": "NoObjective" }],
      "loot": { "composition": [{ "lootCardId": "local-scroll-card", "count": 2 }] },
      "lootMarkers": [{ "id": "loot-1", "position": { "x": 1, "y": 0 } }],
      "groundObjects": [{ "object": { "id": "scroll-3", "itemTypeId": "local-scroll" }, "position": { "x": 2, "y": 0 } }]
    }
    """;

    [Fact]
    public void LocalAbilityItemAndLootReferencesSurviveJsonAndPortableTransport()
    {
        var authored = ScenarioDefinitionJson.FromJson(LocalContent);
        var json = ScenarioDefinitionJson.ToJson(authored);
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(authored));
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(restored));
        var native = Assert.Single(restored.UnitTypes!).LocalAbilities;
        Assert.Equal(2, native.Length);
        Assert.IsType<HolyWaveMechanic>(native[0].Mechanic);
        Assert.Equal(2, native[0].Usage.MaxUses);
        var itemAbility = Assert.Single(Assert.Single(restored.ItemTypes!).LocalAbilities);
        Assert.Equal("read", itemAbility.Key);
        Assert.Equal("Unexpected Silence", itemAbility.Name);

        var state = GameEngine.CreateGame(restored);
        Assert.Equal(3, Assert.Single(state.Units).Inventory.Length);
        Assert.Equal(new[] { "scroll-1", "scroll-2" }, state.Units[0].Inventory.Where(o => o.ItemTypeId == "local-scroll").Select(o => o.Id));
        Assert.Equal("local-scroll", Assert.Single(state.GroundObjects).Object.ItemTypeId);
        Assert.Equal(2, state.LootDeck!.DrawPile.Count);
        Assert.Equal(2, state.LootDeck.DrawPile.Select(o => o.Id).Distinct().Count());
        Assert.All(state.LootDeck.DrawPile, o => Assert.Equal(LootCategory.Item, state.ObjectContent(o).Category));
    }

    [Fact]
    public void SameLocalIdsResolveSeparatelyInDifferentGamesAndCopiesKeepTheirOwnCatalogs()
    {
        var first = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(LocalContent));
        var second = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(LocalContent.Replace("Unexpected Silence", "Another Meaning")));
        Assert.Equal("Unexpected Silence", first.Item("local-scroll").LocalAbilities[0].Name);
        Assert.Equal("Another Meaning", second.Item("local-scroll").LocalAbilities[0].Name);
        second.ItemTypes.Clear();
        second.LootCardDefinitions.Clear();
        Assert.Contains(first.ItemTypes, i => i.Id == "local-scroll");
        Assert.Contains(first.LootCardDefinitions, c => c.Id == "local-scroll-card");
        Assert.DoesNotContain(ItemCatalog.Resolve(null), i => i.Id == "local-scroll");

        var copy = first.Copy();
        copy.ItemTypes.Clear(); copy.LootCardDefinitions.Clear();
        Assert.Contains(first.ItemTypes, i => i.Id == "local-scroll");
        Assert.Contains(first.LootCardDefinitions, c => c.Id == "local-scroll-card");
    }

    [Fact]
    public void StateSaveRetainsExactDefinitionsSourcesAndPhysicalObjectIdentities()
    {
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(LocalContent));
        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(restored));
        Assert.Equal("Unexpected Silence", restored.Item("local-scroll").LocalAbilities[0].Name);
        Assert.Equal(state.Units[0].Inventory.ToArray(), restored.Units[0].Inventory.ToArray());
        Assert.Equal(state.LootDeck!.DrawPile, restored.LootDeck!.DrawPile);
        Assert.Equal(state.GroundObjects, restored.GroundObjects);
    }

    [Fact]
    public void LocalLootCardGrantsItsReferencedItemAndConsumesThatPhysicalCopy()
    {
        var state = ReadyLocalGame();
        var drawnId = state.LootDeck!.DrawPile[0].Id;
        var arrival = GameEngine.Advance(state, new Choice("1,0"), new FixedRandom(), false);
        var drawn = Assert.Single(arrival.Events.Where(e => e.Kind == "LootCardDrawn"));
        Assert.Equal(drawnId, drawn.ObjectCard!.Id);
        Assert.Equal("local-scroll", drawn.ObjectCard.ItemTypeId);
        Assert.Equal("local-scroll-card", drawn.ObjectCard.LootCardId);
        var ability = Assert.Single(arrival.NextInput!.Candidates.Where(c => c.Ability?.SourceInstanceId == drawnId));
        Assert.Equal("Unexpected Silence", ability.Ability!.Definition.Name);
        var used = GameEngine.Advance(arrival.State, new Choice(ability.Key), new FixedRandom(), false);
        Assert.DoesNotContain(used.State.Units[0].Inventory, o => o.Id == drawnId);
        Assert.Contains(used.State.Units[0].Inventory, o => o.Id == "scroll-1");
        Assert.Contains(used.State.Units[0].Inventory, o => o.Id == "scroll-2");
        Assert.Equal(drawnId, Assert.Single(used.State.LootDeck!.DiscardPile).Id);
        Assert.False(used.State.IsUpright("bearer"));
    }

    [Fact]
    public void LocalKnownItemCanBePickedUpWhenCopiesAreAlreadyOwned()
    {
        var arrival = GameEngine.Advance(ReadyLocalGame(), new Choice("2,0"), new FixedRandom(), false);
        Assert.Contains(arrival.NextInput!.Candidates, c => c.Key == "pickup-object:scroll-3");
        var collected = GameEngine.Advance(arrival.State, new Choice("pickup-object:scroll-3"), new FixedRandom(), false);
        Assert.Empty(collected.State.GroundObjects);
        Assert.Equal(3, collected.State.Units[0].Inventory.Count(o => o.ItemTypeId == "local-scroll"));
        Assert.Equal(0, collected.State.ActionsUsedThisActivation);
    }

    private static GameState ReadyLocalGame()
    {
        var state = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(LocalContent));
        state.Round = 1;
        state.CurrentUnitId = "bearer";
        state.ActiveToken = new("local-bearer", "blue");
        state.Pending = new(DecisionKind.Activation, "local-bearer", "bearer", [], false);
        return state;
    }

    [Theory]
    [InlineData("empty-unit-key")]
    [InlineData("empty-equipment-key")]
    [InlineData("empty-item-key")]
    [InlineData("loot-item")]
    [InlineData("deck-card")]
    [InlineData("owned-item")]
    [InlineData("ground-item")]
    [InlineData("unknown-source-card")]
    [InlineData("mismatched-source-card")]
    [InlineData("duplicate-grant")]
    [InlineData("wrong-timing")]
    [InlineData("invalid-equipment-charges")]
    public void InvalidReferencesAndUnsupportedCombinationsUseNormalCreationValidation(string invalid)
    {
        var node = JsonNode.Parse(LocalContent)!;
        switch (invalid)
        {
            case "empty-unit-key": node["unitTypes"]![0]!["localAbilities"]![0]!["key"] = ""; break;
            case "empty-equipment-key": node["equipmentTypes"]![0]!["localAbilities"]![0]!["key"] = ""; break;
            case "empty-item-key": node["itemTypes"]![0]!["localAbilities"]![0]!["key"] = ""; break;
            case "loot-item": node["lootCardDefinitions"]![0]!["itemTypeId"] = "missing"; break;
            case "deck-card": node["loot"]!["composition"]![0]!["lootCardId"] = "missing"; break;
            case "owned-item": node["units"]![0]!["inventory"]![1]!["itemTypeId"] = "missing"; break;
            case "ground-item": node["groundObjects"]![0]!["object"]!["itemTypeId"] = "missing"; break;
            case "unknown-source-card": node["units"]![0]!["inventory"]![1]!["lootCardId"] = "missing"; break;
            case "mismatched-source-card": node["units"]![0]!["inventory"]![0]!["lootCardId"] = "local-scroll-card"; break;
            case "duplicate-grant": node["unitTypes"]![0]!["localAbilities"]![1]!["key"] = "first-wave"; break;
            case "wrong-timing": node["itemTypes"]![0]!["localAbilities"]![0]!["timing"] = "Passive"; break;
            case "invalid-equipment-charges": node["equipmentTypes"]![0]!["localAbilities"]![0]!["usage"] = JsonNode.Parse("{\"maxUses\": 0}"); break;
        }
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(node.ToJsonString()));
    }

    [Fact]
    public void UnknownMechanicDiscriminatorIsRejectedAsData()
    {
        Assert.Throws<JsonException>(() => ScenarioDefinitionJson.FromJson(LocalContent.Replace("holyWave", "executeScript")));
    }
}
