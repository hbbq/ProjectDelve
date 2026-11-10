using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class PopulationV2IntegrationTests
{
    [Fact]
    public void DenseQuickPlayKeepsEveryMarkerScalesAllAndFiltersHiddenPopulation()
    {
        var world = WorldGenerator.Generate(new WorldGenerationRecipe(new(Width: 60, Height: 50), new(0, 0, 0), 12345,
            Civilization: new(Campsites: 2, Settlements: 0), Population: new(PopulationDensity.High, 4), LootPreset: "Fun"));
        var before = ScenarioDefinitionJson.ToJson(world.Definition);
        var prepared = WorldQuickPlay.Prepare(world.Definition, ContentBundle.Canonical());
        Assert.Equal(world.Definition.LootMarkers, prepared.LootMarkers);
        Assert.Equal(LootPresets.AllPresetId, prepared.Loot!.Preset);
        Assert.True(prepared.Loot.PresetMultiplier > 1);
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(prepared));
        var state = GameEngine.CreateGame(restored);
        Assert.True(state.LootDeck!.DrawPile.Count >= state.LootMarkers.Count);
        var result = GameEngine.StartRound(state, new Dice(), false);
        var observation = BrowserProjection.Observe(result, HeroesVsMonsters.Heroes);
        var hiddenIds = result.State.Units.Where(u => !FogOfWar.IsUnitObservable(result.State, HeroesVsMonsters.Heroes, u.Id)).Select(u => u.Id).ToArray();
        Assert.NotEmpty(hiddenIds);
        var json = JsonSerializer.Serialize(BrowserProjection.Create(result));
        foreach (var id in hiddenIds) Assert.DoesNotContain(id, json);
        Assert.True(observation.State.Units.Count < result.State.Units.Count);
        Assert.DoesNotContain("PopulationIntent", json); Assert.DoesNotContain("AlertComponents", json);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(world.Definition));
    }

    [Fact]
    public void QuickPlayRepeatsAllRatherThanRemovingMarkersBeyondOneDeckCopy()
    {
        var original = new ScenarioDefinition
        {
            Board = new() { Width = 20, Height = 10 }, UnitTypeIds = [], Units = [], Agency = [],
            Loot = new() { Preset = "Classic", PresetMultiplier = 4 },
            LootMarkers = Enumerable.Range(0, 31).Select(i => new LootMarker("cache-" + i, new(19, 9))).ToList()
        };
        var prepared = WorldQuickPlay.Prepare(original, ContentBundle.Canonical());
        Assert.Equal(original.LootMarkers, prepared.LootMarkers);
        Assert.Equal("All", prepared.Loot!.Preset);
        Assert.True(prepared.Loot.PresetMultiplier >= 6);
        var state = GameEngine.CreateGame(prepared);
        Assert.Equal(31, state.LootMarkers.Count); Assert.True(state.LootDeck!.DrawPile.Count >= 32);
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.SideId == HeroesVsMonsters.Heroes) ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 1;
    }
}
