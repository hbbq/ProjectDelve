using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LootPresetTests
{
    private static ScenarioDefinition Scenario() => new()
    {
        Board = new() { Width = 3, Height = 3 }, UnitTypeIds = [UnitTypeIds.Goblin],
        Units = [new(UnitTypeIds.Goblin, "red", new(1, 1))],
        Agency = [new(UnitTypeIds.Goblin, "red", ControllerKind.Human)]
    };

    [Theory]
    [InlineData("Classic")]
    [InlineData("Fun")]
    [InlineData("Experimental")]
    [InlineData("FarOut")]
    public void PresetRoundTripStoresOnlyLiveReferenceAndResolvesCurrentComposition(string preset)
    {
        var json = ScenarioDefinitionJson.ToJson(Scenario() with { Loot = new() { Preset = preset } });
        var loot = JsonNode.Parse(json)!["loot"]!;
        Assert.Equal(preset, loot["preset"]!.GetValue<string>());
        Assert.Null(loot["composition"]); Assert.Null(loot["cards"]);
        var loaded = ScenarioDefinitionJson.FromJson(json);
        Assert.Equal(LootPresets.Find(preset).Composition.SelectMany(r => Enumerable.Repeat(r.Kind, r.Count)),
            GameEngine.CreateGame(loaded).LootDeck!.DrawPile.Select(c => c.Kind));
    }

    [Fact]
    public void NewDefaultsAndLegacyOptOutAreDistinct()
    {
        Assert.Equal(10, GameEngine.CreateGame(Scenario()).LootDeck!.DrawPile.Count);
        var old = JsonNode.Parse(ScenarioDefinitionJson.ToJson(Scenario()))!.AsObject(); old.Remove("loot");
        Assert.Empty(GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(old.ToJsonString())).LootDeck!.DrawPile);
        old["loot"] = JsonNode.Parse("{\"cards\":[\"LuckyBoots\",\"LuckyBoots\"]}");
        var migrated = ScenarioDefinitionJson.FromJson(old.ToJsonString());
        Assert.Equal(new LootCardQuantity(LootCardKind.LuckyBoots, 2), Assert.Single(migrated.Loot!.Composition!));
        Assert.DoesNotContain("\"cards\"", ScenarioDefinitionJson.ToJson(migrated));
    }

    [Fact]
    public void CopyToCustomIsIndependentAndRulesAreNotEmbedded()
    {
        var rows = LootPresets.Find("Classic").Composition.ToList();
        var custom = new LootSettings { Composition = rows };
        rows[0] = rows[0] with { Count = 1 };
        Assert.Equal(4, LootPresets.Find("Classic").Composition[0].Count);
        var json = ScenarioDefinitionJson.ToJson(Scenario() with { Loot = custom });
        Assert.DoesNotContain("Restore 2 HP", json);
        Assert.Equal(7, GameEngine.CreateGame(ScenarioDefinitionJson.FromJson(json)).LootDeck!.DrawPile.Count);
    }

    [Fact]
    public void OverridesInheritAndExplicitZeroWinsWithoutCopyingTypes()
    {
        var scenario = Scenario();
        Assert.Equal(UnitType.Goblin().LootDropSuccesses, GameEngine.CreateGame(scenario).Types[0].LootDropSuccesses);
        Assert.True(UnitType.Goblin().LootDropSuccesses > 0);
        Assert.True(UnitType.Worker().LootDropSuccesses > 0);
        Assert.True(UnitType.Troll().LootDropSuccesses > 0);
        scenario = scenario with { LootUnitOverrides = new() { [UnitTypeIds.Goblin] = new(DropSuccesses: 0) } };
        var loaded = ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario));
        Assert.Null(loaded.UnitTypes);
        Assert.Equal(0, GameEngine.CreateGame(loaded).Types[0].LootDropSuccesses);
        scenario = scenario with { UnitTypes = [UnitType.Goblin() with { LootDropSuccesses = 6 }] };
        Assert.Equal(0, GameEngine.CreateGame(scenario).Types[0].LootDropSuccesses);
        Assert.Equal(6, GameEngine.CreateGame(scenario with { LootUnitOverrides = null }).Types[0].LootDropSuccesses);
        Assert.Equal(0, GameEngine.CreateGame(scenario with { LootUnitOverrides = null, UnitTypes = [UnitType.Goblin() with { LootDropSuccesses = 0 }] }).Types[0].LootDropSuccesses);
    }

    [Fact]
    public void ExhaustedRuntimeDeckDoesNotRefillOnResumeAndSetupReservesCards()
    {
        var state = GameEngine.CreateGame(Scenario());
        state.LootDeck!.DiscardPile.AddRange(state.LootDeck.DrawPile); state.LootDeck.DrawPile.Clear();
        var resumed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Empty(resumed.LootDeck!.DrawPile); Assert.Equal(10, resumed.LootDeck.DiscardPile.Count);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Scenario() with
        { LootMarkers = Enumerable.Range(0, 11).Select(i => new LootMarker($"m{i}", new(0, 0))).ToList() }));
    }

    [Fact]
    public void GeneratorPreservesPresetInRecipeAndScenarioWithoutChangingLayout()
    {
        var code = new GenerationCode(Seed: 42, LootPreset: "Fun");
        Assert.Equal(code, GenerationCode.Parse(code.ToString()));
        var fun = DungeonGenerator.Generate(code); var classic = DungeonGenerator.Generate(code with { LootPreset = "Classic" });
        Assert.Equal("Fun", fun.Definition.Loot!.Preset); Assert.Null(fun.Definition.Loot.Composition);
        Assert.Null(fun.Definition.LootUnitOverrides);
        Assert.Equal(classic.Definition.Board.Cells, fun.Definition.Board.Cells);
        Assert.Equal(classic.Definition.Units, fun.Definition.Units);
        Assert.Equal("Classic", GenerationCode.Parse(new GenerationCode(Seed: 42).ToString()).LootPreset);
    }

    [Fact]
    public void RejectsAmbiguousOrInvalidConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new LootSettings { Preset = "Fun", Composition = [] }.ResolveCards());
        Assert.Throws<ArgumentException>(() => new LootSettings { Preset = "Unknown" }.ResolveCards());
        Assert.Throws<ArgumentException>(() => new LootSettings { Composition = [new(LootCardKind.LuckyBoots, 0)] }.ResolveCards());
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(Scenario() with { LootUnitOverrides = new() { [UnitTypeIds.Goblin] = new(7) } }));
    }
}
