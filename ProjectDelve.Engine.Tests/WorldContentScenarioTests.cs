using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldContentScenarioTests
{
    private const string LocalContent = """
    {
      "worldCardDefinitions": [
        { "id": "local-breeze", "name": "A Different Breeze", "lifecycle": "Continuous",
          "modifiers": [{ "target": "Mov", "amount": 2 }, { "target": "Def", "amount": -1 }] },
        { "id": "local-rain", "name": "Unexpected Rain", "lifecycle": "Immediate",
          "immediate": { "kind": "healDamagedUnits", "amount": 2 } }
      ],
      "worldEffects": { "cardsPerRound": 2, "cycling": 2,
        "composition": [{ "typeId": "local-breeze", "count": 2 }, { "typeId": "local-rain", "count": 1 }] },
      "board": { "width": 4, "height": 3 },
      "unitTypeIds": ["local-observer"],
      "unitTypes": [{ "id": "local-observer", "mov": 3, "rng": 1, "atk": 1, "def": 1, "hp": 4, "unique": true }],
      "units": [{ "unitTypeId": "local-observer", "sideId": "blue", "anchor": { "x": 1, "y": 1 },
        "unitId": "observer", "initialHp": 1 }],
      "agency": [{ "unitTypeId": "local-observer", "sideId": "blue", "controller": "Human" }],
      "objectives": [{ "sideId": "blue", "objective": "NoObjective" }],
      "loot": { "composition": [] }
    }
    """;

    [Fact]
    public void LocalCardsAndCompositionRoundTripThroughJsonAndDelve1()
    {
        var authored = ScenarioDefinitionJson.FromJson(LocalContent);
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(authored));
        Assert.Equal(ScenarioDefinitionJson.ToJson(authored), ScenarioDefinitionJson.ToJson(restored));
        Assert.Equal(2, restored.WorldCardDefinitions!.Count);
        var state = GameEngine.CreateGame(restored);
        Assert.Equal(new[] { "local-breeze", "local-breeze", "local-rain" }, state.WorldDeck!.DrawPile.Select(c => c.TypeId));
        Assert.Equal(3, state.WorldDeck.DrawPile.Select(c => c.Id).Distinct().Count());
        Assert.Equal("All Units have MOV +2. All Units have DEF -1.", state.WorldCardDefinitions.Single(c => c.Id == "local-breeze").Description);
    }

    [Fact]
    public void LocalDefinitionsStayScopedToTheirGameAndCopy()
    {
        var first = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(LocalContent));
        var second = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(LocalContent.Replace("A Different Breeze", "Another Meaning")));
        Assert.Equal("A Different Breeze", first.WorldCardDefinitions.Single(c => c.Id == "local-breeze").Name);
        Assert.Equal("Another Meaning", second.WorldCardDefinitions.Single(c => c.Id == "local-breeze").Name);
        second.WorldCardDefinitions.Clear();
        var copy = first.Copy();
        copy.WorldCardDefinitions.Clear();
        Assert.Contains(first.WorldCardDefinitions, c => c.Id == "local-breeze");
        Assert.DoesNotContain(WorldCardCatalog.Resolve(null), c => c.Id == "local-breeze");
    }

    [Fact]
    public void SaveIncludesResolvedDefinitionsAndPhysicalCards()
    {
        var original = GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(LocalContent));
        var saved = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<GameState>(saved)!;
        GameEngine.ValidateScenario(restored);
        Assert.Equal(saved, JsonSerializer.Serialize(restored));
        Assert.Equal(original.WorldDeck!.DrawPile, restored.WorldDeck!.DrawPile);
    }

    [Fact]
    public void ExplicitEmptyCompositionIsNotTheCanonicalDeck()
    {
        var authored = ScenarioDefinitionJson.FromJson(LocalContent);
        var empty = GameEngine.CreateGame(authored with { WorldEffects = authored.WorldEffects! with { Composition = [] } });
        Assert.Empty(empty.WorldDeck!.DrawPile);
        var canonical = GameEngine.CreateGame(authored with { WorldEffects = authored.WorldEffects! with { Composition = null } });
        Assert.Equal(40, canonical.WorldDeck!.DrawPile.Count);
        Assert.DoesNotContain(canonical.WorldDeck.DrawPile, c => c.TypeId == "local-breeze");
    }

    [Theory]
    [InlineData("unknown-card")]
    [InlineData("negative-count")]
    [InlineData("duplicate-definition")]
    [InlineData("canonical-shadow")]
    [InlineData("wrong-lifecycle")]
    [InlineData("invalid-amount")]
    public void InvalidLocalContentIsRejectedAtTheNormalScenarioBoundary(string invalid)
    {
        var node = JsonNode.Parse(LocalContent)!;
        switch (invalid)
        {
            case "unknown-card": node["worldEffects"]!["composition"]![0]!["typeId"] = "absent"; break;
            case "negative-count": node["worldEffects"]!["composition"]![0]!["count"] = -1; break;
            case "duplicate-definition": node["worldCardDefinitions"]!.AsArray().Add(node["worldCardDefinitions"]![0]!.DeepClone()); break;
            case "canonical-shadow": node["worldCardDefinitions"]![0]!["id"] = "haste"; break;
            case "wrong-lifecycle": node["worldCardDefinitions"]![1]!["lifecycle"] = "Continuous"; break;
            case "invalid-amount": node["worldCardDefinitions"]![1]!["immediate"]!["amount"] = -1; break;
        }
        Assert.Throws<ArgumentException>(() => ScenarioDefinitionJson.FromJson(node.ToJsonString()));
    }

    [Fact]
    public void UnknownMechanicsAreRejectedAsData()
        => Assert.Throws<JsonException>(() => ScenarioDefinitionJson.FromJson(LocalContent.Replace("healDamagedUnits", "executeScript")));
}
