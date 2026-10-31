using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AllLootGenerationTests
{
    [Theory]
    [InlineData("HVM", 0UL)]
    [InlineData("HVM", 42UL)]
    [InlineData("OS", 0UL)]
    [InlineData("OS", 42UL)]
    public void AllRecipesStayLiveAndResolveExactlyOneOfEveryCardWithoutPerturbingSeededLayout(string profile, ulong seed)
    {
        var code = new GenerationCode(Profile: profile, Width: 30, Height: 24, Treasure: profile == "OS" ? 3 : 2,
            Seed: seed, LootPreset: LootPresets.AllPresetId);
        Assert.Equal(code, GenerationCode.Parse(code.ToString()));
        Assert.Contains("-QAll-S", code.ToString());
        var generated = DungeonGenerator.Generate(code);
        var replay = DungeonGenerator.Generate(generated.Code.ToString());
        Assert.Equal(generated.Fingerprint, replay.Fingerprint);
        Assert.Equal(ScenarioDefinitionJson.ToJson(generated.Definition), ScenarioDefinitionJson.ToJson(replay.Definition));
        var loot = generated.Definition.Loot!;
        Assert.Equal(LootPresets.AllPresetId, loot.Preset);
        Assert.Null(loot.Composition);
        Assert.Null(loot.PresetMultiplier);
        Assert.Equal(1, generated.Diagnostics.LootMultiplier);
        var composition = loot.ResolveComposition();
        Assert.Equal(LootCardCatalog.All.Select(card => card.Id).Order(StringComparer.Ordinal), composition.Select(row => row.LootCardId));
        Assert.All(composition, row => Assert.Equal(1, row.Count));
        var state = GameEngine.CreateGame(generated.Definition);
        Assert.Equal(composition.Count, state.LootDeck!.DrawPile.Count);
        Assert.Equal(composition.Select(row => row.LootCardId), state.LootDeck.DrawPile.Select(card => card.LootCardId));
        Assert.True(state.LootMarkers.Count <= state.LootDeck.DrawPile.Count);

        var fixedPreset = DungeonGenerator.Generate(code with { LootPreset = LootPresets.Default });
        Assert.Equal(fixedPreset.Definition.Board.Cells, generated.Definition.Board.Cells);
        Assert.Equal(fixedPreset.Definition.Board.Edges, generated.Definition.Board.Edges);
        Assert.Equal(fixedPreset.Definition.Units, generated.Definition.Units);
        Assert.Equal(fixedPreset.Diagnostics.Attempts, generated.Diagnostics.Attempts);
        var transport = ScenarioDefinitionTransport.Encode(generated.Definition);
        var restored = ScenarioDefinitionTransport.Decode(transport);
        Assert.Equal(LootPresets.AllPresetId, restored.Loot!.Preset);
        Assert.Null(restored.Loot.Composition);
        Assert.Equal(ScenarioDefinitionJson.ToJson(generated.Definition), ScenarioDefinitionJson.ToJson(restored));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void StrongholdAllCapsReservationsAcrossExistingCachesInsteadOfMultiplyingCards(int treasure)
    {
        var code = new GenerationCode(Profile: "OS", Width: 42, Height: 30, Treasure: treasure,
            Seed: 42, LootPreset: LootPresets.AllPresetId);
        var generated = DungeonGenerator.Generate(code);
        var original = DungeonGenerator.Generate(code with { LootPreset = LootPresets.Default });
        var available = generated.Definition.Loot!.ResolveComposition().Sum(row => row.Count);
        var markers = generated.Definition.LootMarkers!;
        var originalMarkers = original.Definition.LootMarkers!;
        Assert.Equal(Math.Min(available, originalMarkers.Count), markers.Count);
        Assert.All(markers, marker => Assert.Contains(marker, originalMarkers));
        Assert.Equal(Math.Min(treasure, markers.Count), generated.Diagnostics.TreasureCaches);
        Assert.Equal(generated.Diagnostics.TreasureCaches, markers.Select(marker => marker.Position).Distinct().Count());
        if (markers.Count > 0)
        {
            var caches = markers.GroupBy(marker => marker.Position).ToArray();
            var mostMarkers = caches.Max(cache => cache.Count());
            Assert.All(caches, cache => Assert.True(cache.Count() >= mostMarkers - 1 ||
                cache.Count() == originalMarkers.Count(marker => marker.Position == cache.Key),
                "A cache receives fewer markers only after its original marker supply is exhausted."));
        }
        Assert.Null(generated.Definition.Loot.PresetMultiplier);
        _ = GameEngine.CreateGame(generated.Definition);
    }
}
