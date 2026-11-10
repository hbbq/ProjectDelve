using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PopulationLootTests
{
    [Theory]
    [InlineData("Classic")]
    [InlineData("Fun")]
    [InlineData("Experimental")]
    [InlineData("FarOut")]
    [InlineData("All")]
    public void ProvisionRepeatsLiveCompositionForEveryMarkerAndExpectedDrop(string preset)
    {
        var content = ContentBundle.Canonical();
        var settings = new LootSettings { Preset = preset };
        var rows = settings.ResolveComposition(content.LootCards, content.LootPresets);
        var size = rows.Sum(r => r.Count);
        var provisioned = PopulationLoot.Provision(settings, size + 1, size, content);
        Assert.Equal(preset, provisioned.Preset); Assert.Null(provisioned.Composition);
        Assert.Equal(3, provisioned.PresetMultiplier);
        Assert.Equal(rows.Select(r => r with { Count = r.Count * 3 }), provisioned.ResolveComposition(content.LootCards, content.LootPresets));
        var definition = Scenario(provisioned) with
        { LootMarkers = Enumerable.Range(0, size + 1).Select(i => new LootMarker($"cache-{i}", new(1, 1))).ToList() };
        var state = GameEngine.CreateGame(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition)));
        Assert.Equal(size + 1, state.LootMarkers.Count);
        Assert.Equal(size * 3, state.LootDeck!.DrawPile.Count);
        state.LootMarkers.Clear(); state.LootDeck.DiscardPile.AddRange(state.LootDeck.DrawPile); state.LootDeck.DrawPile.Clear();
        var resumed = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Empty(resumed.LootDeck!.DrawPile); Assert.Equal(size * 3, resumed.LootDeck.DiscardPile.Count);
    }

    [Fact]
    public void AllProvisionUsesEffectiveLocalCardsAndPreservesLargerExistingSupply()
    {
        var content = ContentBundle.Canonical();
        content = content with { LootCards = [.. content.LootCards, new("fixture-local-reward", "Fixture reward") { Immediate = new HealCollectorMechanic(2) }] };
        var settings = new LootSettings { Preset = "All", PresetMultiplier = 4 };
        var provisioned = PopulationLoot.Provision(settings, content.LootCards.Length * 2, 1, content);
        Assert.Equal(4, provisioned.PresetMultiplier);
        Assert.Equal(4, provisioned.ResolveComposition(content.LootCards, content.LootPresets).Single(r => r.LootCardId == "fixture-local-reward").Count);
        Assert.Null(settings.Composition);
    }

    [Fact]
    public void CustomAndEmptyDecksAreNotExpandedAndPresetBoundsRemainValid()
    {
        var content = ContentBundle.Canonical();
        foreach (var settings in new[] { LootSettings.Empty, new LootSettings { Composition = [new(LootCardKind.LuckyBoots, 2)] } })
            Assert.Equal(settings, PopulationLoot.Provision(settings, 80, 90, content));
        Assert.Throws<ArgumentException>(() => PopulationLoot.Provision(new() { Preset = "Classic" }, int.MaxValue, int.MaxValue, content));
        var supplied = PopulationLoot.Provision(new() { Preset = "Classic" }, 500, 90, content);
        Assert.InRange(supplied.PresetMultiplier!.Value, 1, 100);
        Assert.InRange(supplied.ResolveComposition(content.LootCards, content.LootPresets).Sum(r => r.Count), 1, 6000);
        Assert.Throws<ArgumentException>(() => PopulationLoot.Provision(LootSettings.Empty, -1, 0, content));
    }

    [Theory]
    [InlineData("Empty")]
    [InlineData("Classic")]
    [InlineData("Fun")]
    [InlineData("Experimental")]
    [InlineData("FarOut")]
    [InlineData("All")]
    public void WorldRecipeRoundTripsAndMaterializesTheSelectedPresetBeforePopulation(string preset)
    {
        var recipe = new WorldGenerationRecipe(new(0, 0, 12, 10), new(0, 0, 0), 42,
            Population: new(PopulationDensity.Off), LootPreset: preset);
        Assert.Contains("-MOff-D3-O" + preset + "-S", recipe.ToString());
        Assert.Equal(recipe, WorldGenerationRecipe.Parse(recipe.ToString()));
        var world = WorldGenerator.Generate(recipe);
        if (preset == "Empty") Assert.Empty(world.Definition.Loot!.ResolveComposition());
        else { Assert.Equal(preset, world.Definition.Loot!.Preset); Assert.Null(world.Definition.Loot.Composition); }
        Assert.Empty(world.Definition.LootMarkers ?? []);
        var old = recipe.ToString().Replace("-O" + preset, "");
        Assert.Equal("Empty", WorldGenerationRecipe.Parse(old).LootPreset);
        Assert.Throws<ArgumentException>(() => (recipe with { LootPreset = "Unknown" }).Validate());
    }

    private static ScenarioDefinition Scenario(LootSettings settings) => new()
    {
        Board = new() { Width = 3, Height = 3 }, UnitTypeIds = [], Units = [], Agency = [], Loot = settings
    };
}
