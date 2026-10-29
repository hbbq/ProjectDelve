using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class StrongholdGenerationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(42UL)]
    [InlineData(ulong.MaxValue)]
    public void CompleteRecipeAndTransportReproduceInitialScenario(ulong seed)
    {
        var generated = DungeonGenerator.Generate(new GenerationCode(Profile: "OS", Seed: seed));
        Assert.Equal(generated.Fingerprint, DungeonGenerator.Generate(generated.Code.ToString()).Fingerprint);
        var json = ScenarioDefinitionJson.ToJson(generated.Definition);
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(generated.Definition))));
        Assert.Equal("OS", generated.Diagnostics.Profile);
        Assert.NotNull(generated.Diagnostics.SitePlan);
        AssertGeometry(generated);
    }

    [Fact]
    public void CorpusVariesMissionShapesAndMeetsRouteLootAndPlacementContracts()
    {
        HashSet<string> plans = []; HashSet<string> unusual = [];
        var count = 0; var mostAttempts = 0; var mostEnemies = 0; var largestMultiplier = 0;
        foreach (var dimensions in new[] { (18, 18), (24, 20), (30, 24), (42, 30), (50, 50), (18, 50), (50, 18) })
        for (ulong seed = 0; seed < 12; seed++)
        {
            var code = new GenerationCode(Width: dimensions.Item1, Height: dimensions.Item2, Profile: "OS", Seed: seed,
                Openness: 1 + (int)(seed % 3), Terrain: (int)(seed % 4), Treasure: (int)(seed % 4),
                Density: 1 + (int)(seed % 3), Difficulty: 1 + (int)(seed % 5), Objective: seed % 2 == 0 ? "OR2" : "ORA");
            GeneratedDungeon generated;
            try { generated = DungeonGenerator.Generate(code); }
            catch (Exception e) { throw new Exception($"Failed {code}", e); }
            AssertGeometry(generated);
            plans.Add(generated.Diagnostics.SitePlan!);
            foreach (var unit in generated.Definition.Units.Where(u => new[] { UnitTypeIds.RedDragon, UnitTypeIds.GoblinKing, UnitTypeIds.TowerTroll }.Contains(u.UnitTypeId))) unusual.Add(unit.UnitTypeId);
            mostAttempts = Math.Max(mostAttempts, generated.Diagnostics.Attempts);
            mostEnemies = Math.Max(mostEnemies, generated.Diagnostics.Monsters);
            largestMultiplier = Math.Max(largestMultiplier, generated.Diagnostics.LootMultiplier);
            Assert.Equal(code.Treasure, generated.Diagnostics.TreasureCaches);
            count++;
        }
        Assert.Equal(3, plans.Count);
        Assert.NotEmpty(unusual);
        output.WriteLine($"{count} scenarios; plans={string.Join(", ", plans)}; max attempts={mostAttempts}; max defenders={mostEnemies}; max multiplier={largestMultiplier}; unusual={string.Join(", ", unusual)}");
    }

    [Fact]
    public void PresetRemainsLiveAndScalingPreservesEveryCardRatio()
    {
        foreach (var preset in LootPresets.All)
        {
            var generated = DungeonGenerator.Generate(new GenerationCode(Profile: "OS", Width: 42, Height: 30, Treasure: 3, LootPreset: preset.Id, Seed: 42));
            var definition = generated.Definition;
            Assert.Equal(preset.Id, definition.Loot!.Preset);
            Assert.Null(definition.Loot.Composition);
            Assert.Null(definition.LootUnitOverrides);
            Assert.True(definition.Loot.PresetMultiplier > 1);
            Assert.Equal(preset.Composition.Select(c => c with { Count = c.Count * definition.Loot.PresetMultiplier!.Value }), definition.Loot.ResolveComposition());
            Assert.Null(definition.GroundObjects);
            Assert.All(definition.Units, u => Assert.Null(u.Inventory));
            var expectedDrops = (definition.Units.Sum(u => UnitContent.Find(u.UnitTypeId)!.LootDropSuccesses) + 5) / 6;
            Assert.True(definition.Loot.ResolveCards().Count >= definition.LootMarkers!.Count + expectedDrops);
        }
    }

    [Fact]
    public void ContentSettingsDoNotChangeGeographyAndFogHidesDefenders()
    {
        var a = DungeonGenerator.Generate(new GenerationCode(Profile: "OS", Seed: 42));
        var b = DungeonGenerator.Generate(new GenerationCode(Profile: "OS", Seed: 42, Difficulty: 1, Density: 1, Treasure: 0, LootPreset: "FarOut", FogOfWar: false));
        Assert.Equal(a.Definition.Board.Cells, b.Definition.Board.Cells);
        Assert.Equal(a.Definition.Board.Edges, b.Definition.Board.Edges);
        Assert.Equal(a.Definition.Units.Take(4), b.Definition.Units.Take(4));
        var state = GameEngine.CreateGame(a.Definition);
        Assert.Contains(state.Physical.Figures, f => !FogOfWar.IsUnitObservable(state, HeroesVsMonsters.Heroes, f.Id));
        Assert.Empty(GameEngine.CreateGame(b.Definition).ExploredCells);
        Assert.Empty(b.Definition.LootMarkers!);
    }

    [Theory]
    [InlineData(0UL, 1)]
    [InlineData(42UL, 3)]
    public void GeneratedEncountersResolveThroughSeveralRoundsWithFiniteLoot(ulong seed, int difficulty)
    {
        // This is an execution smoke test, not a balance/win-rate assertion. Human Heroes will
        // pursue mission and loot choices that the ordinary monster-oriented provider does not.
        var generated = DungeonGenerator.Generate(new GenerationCode(Profile: "OS", Width: 18, Height: 18,
            Seed: seed, Difficulty: difficulty, FogOfWar: false, Treasure: 2, Terrain: 3));
        var state = GameEngine.CreateGame(generated.Definition);
        var random = new SimulationDice(seed); var provider = new OrderAwareAutomatedProvider();
        var decisions = 0;
        while (!state.IsCompleted && !(state.Round >= 4 && state.RoundComplete) && decisions < 700)
        {
            state = state.Round == 0 || state.RoundComplete ? GameEngine.StartRound(state, random).State :
                GameEngine.Advance(state, provider, random).State;
            Assert.True(state.LootMarkers.Count <= state.LootDeck!.DrawPile.Count);
            decisions++;
        }
        Assert.True(state.IsCompleted || state.Round >= 4 && state.RoundComplete, $"Play stalled after {decisions} decisions in Round {state.Round}.");
        output.WriteLine($"seed={seed}, difficulty={difficulty}: rounds={state.Round}, decisions={decisions}, completed={state.IsCompleted}, living Heroes={state.Units.Count(u => u.SideId == HeroesVsMonsters.Heroes && u.CurrentHp > 0)}, living defenders={state.Units.Count(u => u.SideId == HeroesVsMonsters.Monsters && u.CurrentHp > 0)}");
    }

    private sealed class SimulationDice(ulong seed) : IRandomProvider
    {
        private readonly Pcg32 random = new(seed, 17);
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[random.Next(bag.Count)];
        public AttackFace RollAttackDie() => random.Next(2) == 0 ? AttackFace.Hit : AttackFace.Miss;
        public DefenceFace RollDefenceDie() => random.Next(3) == 0 ? DefenceFace.Block : DefenceFace.Miss;
        public int RollD6() => 1 + random.Next(6);
        public int RollD8() => 1 + random.Next(8);
    }

    private static void AssertGeometry(GeneratedDungeon generated)
    {
        var definition = generated.Definition; var diagnostics = generated.Diagnostics;
        var state = GameEngine.CreateGame(definition);
        var distances = DungeonGenerator.VerifyConnectivity(definition);
        Assert.Equal(4, definition.Units.Count(u => u.SideId == HeroesVsMonsters.Heroes));
        Assert.True(diagnostics.DestinationDistance >= Math.Min(definition.Board.Width, definition.Board.Height) / 3);
        Assert.InRange(diagnostics.AlternateRouteDistance, diagnostics.DestinationDistance, diagnostics.DestinationDistance * 3);
        Assert.Contains(definition.Board.Cells, c => c.Terrain == TerrainKind.StoneFloor);
        Assert.Contains(definition.Board.Cells, c => c.Terrain == TerrainKind.Tree);
        Assert.Contains(definition.Board.Edges, e => e.Kind == EdgeKind.Wall);
        Assert.Contains(definition.Board.Edges, e => e.Kind == EdgeKind.ClosedDoor);
        Assert.All(definition.Board.Cells.Where(c => c.Terrain == TerrainKind.Lava), c => Assert.False(c.CoalGeneration));
        Assert.All(definition.LootMarkers!, marker => Assert.Contains(marker.Position, distances.Keys));
        Assert.All(definition.LootMarkers!.GroupBy(m => m.Position), cache => Assert.True(cache.Count() >= 2));
        var heroStart = definition.Units[0].Anchor;
        Assert.All(definition.Units.Where(u => u.SideId == HeroesVsMonsters.Monsters), u => Assert.True(distances[u.Anchor] >= 6));
        Assert.Equal(definition.Orders!.Count, definition.Orders.Select(o => (o.UnitTypeId, o.SideId)).Distinct().Count());
        // Engine validates initial footprints; independently ensure no cache starts under any footprint.
        var occupied = definition.Units.SelectMany(u => FootprintGeometry.OccupiedCells(UnitContent.Find(u.UnitTypeId)!.Footprint, u.Anchor)).ToHashSet();
        Assert.All(definition.LootMarkers!, marker => Assert.DoesNotContain(marker.Position, occupied));
        foreach (var u in definition.Units.Where(u => UnitContent.Find(u.UnitTypeId)!.Footprint == Footprint.TwoByTwo))
        {
            var queries = new GameplayQueries(state);
            Assert.True(queries.MovementPathsFrom(u.UnitId!, u.Anchor).Count > 1);
        }
    }
}
