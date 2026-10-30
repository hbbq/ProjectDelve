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
        "abilityGrants": [{ "id": "first-wave", "abilityId": "local-limited-wave" }, { "id": "second-wave", "abilityId": "local-limited-wave" }] }],
      "abilityDefinitions": [
        { "id": "local-limited-wave", "name": "Handwritten Prayer", "timing": "Action", "usage": { "maxUses": 2 }, "mechanic": { "kind": "holyWave" } },
        { "id": "local-wave", "name": "Unexpected Silence", "timing": "Action", "mechanic": { "kind": "holyWave" } }
      ],
      "equipmentTypes": [{ "id": "local-reliquary", "name": "Local Reliquary", "grants": [{ "id": "chant", "abilityId": "local-wave" }] }],
      "itemTypes": [{ "id": "local-scroll", "name": "Local Scroll", "grants": [{ "id": "read", "abilityId": "local-wave" }] }],
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
        Assert.Equal(2, restored.AbilityDefinitions!.Count);
        Assert.Equal("Unexpected Silence", restored.AbilityDefinitions.Single(a => a.Id == "local-wave").Name);
        Assert.IsType<HolyWaveMechanic>(restored.AbilityDefinitions[0].Mechanic);
        Assert.Equal(2, restored.AbilityDefinitions[0].Usage.MaxUses);
        Assert.Equal(2, Assert.Single(restored.UnitTypes!).AbilityGrants.Length);
        Assert.Equal("local-wave", Assert.Single(Assert.Single(restored.ItemTypes!).Grants).AbilityId);

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
        Assert.Equal("Unexpected Silence", first.AbilityDefinitions.Single(a => a.Id == "local-wave").Name);
        Assert.Equal("Another Meaning", second.AbilityDefinitions.Single(a => a.Id == "local-wave").Name);
        second.AbilityDefinitions.Clear();
        second.ItemTypes.Clear();
        second.LootCardDefinitions.Clear();
        Assert.Contains(first.AbilityDefinitions, a => a.Id == "local-wave");
        Assert.Contains(first.ItemTypes, i => i.Id == "local-scroll");
        Assert.Contains(first.LootCardDefinitions, c => c.Id == "local-scroll-card");
        Assert.DoesNotContain(AbilityCatalog.Resolve(null), a => a.Id == "local-wave");
        Assert.DoesNotContain(ItemCatalog.Resolve(null), i => i.Id == "local-scroll");

        var copy = first.Copy();
        copy.AbilityDefinitions.Clear(); copy.ItemTypes.Clear(); copy.LootCardDefinitions.Clear();
        Assert.Contains(first.AbilityDefinitions, a => a.Id == "local-wave");
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
        Assert.Equal("Unexpected Silence", restored.AbilityDefinitions.Single(a => a.Id == "local-wave").Name);
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
        var ability = Assert.Single(arrival.NextInput!.Candidates.Where(c => c.GrantedAbility?.SourceInstanceId == drawnId));
        Assert.Equal("Unexpected Silence", ability.GrantedAbility!.Definition.Name);
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
    [InlineData("unit-ability")]
    [InlineData("equipment-ability")]
    [InlineData("item-ability")]
    [InlineData("loot-item")]
    [InlineData("deck-card")]
    [InlineData("owned-item")]
    [InlineData("ground-item")]
    [InlineData("unknown-source-card")]
    [InlineData("mismatched-source-card")]
    [InlineData("duplicate-ability")]
    [InlineData("duplicate-grant")]
    [InlineData("wrong-timing")]
    [InlineData("equipment-charges")]
    public void InvalidReferencesAndUnsupportedCombinationsUseNormalCreationValidation(string invalid)
    {
        var node = JsonNode.Parse(LocalContent)!;
        switch (invalid)
        {
            case "unit-ability": node["unitTypes"]![0]!["abilityGrants"]![0]!["abilityId"] = "missing"; break;
            case "equipment-ability": node["equipmentTypes"]![0]!["grants"]![0]!["abilityId"] = "missing"; break;
            case "item-ability": node["itemTypes"]![0]!["grants"]![0]!["abilityId"] = "missing"; break;
            case "loot-item": node["lootCardDefinitions"]![0]!["itemTypeId"] = "missing"; break;
            case "deck-card": node["loot"]!["composition"]![0]!["lootCardId"] = "missing"; break;
            case "owned-item": node["units"]![0]!["inventory"]![1]!["itemTypeId"] = "missing"; break;
            case "ground-item": node["groundObjects"]![0]!["object"]!["itemTypeId"] = "missing"; break;
            case "unknown-source-card": node["units"]![0]!["inventory"]![1]!["lootCardId"] = "missing"; break;
            case "mismatched-source-card": node["units"]![0]!["inventory"]![0]!["lootCardId"] = "local-scroll-card"; break;
            case "duplicate-ability": node["abilityDefinitions"]!.AsArray().Add(node["abilityDefinitions"]![0]!.DeepClone()); break;
            case "duplicate-grant": node["unitTypes"]![0]!["abilityGrants"]![1]!["id"] = "first-wave"; break;
            case "wrong-timing": node["abilityDefinitions"]![1]!["timing"] = "FreeAction"; break;
            case "equipment-charges": node["equipmentTypes"]![0]!["grants"]![0]!["abilityId"] = "local-limited-wave"; break;
        }
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(node.ToJsonString()));
    }

    [Fact]
    public void UnknownMechanicDiscriminatorIsRejectedAsData()
    {
        Assert.Throws<JsonException>(() => ScenarioDefinitionJson.FromJson(LocalContent.Replace("holyWave", "executeScript")));
    }
}
