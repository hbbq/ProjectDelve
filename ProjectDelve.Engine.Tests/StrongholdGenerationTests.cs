using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class StrongholdGenerationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, "Generated start is impassable.")]
    [InlineData(1, "Generated floor is disconnected.")]
    public void ConnectivityVerifierUsesTypedObstructionsInsteadOfLegacyFallback(int fireX, string error)
    {
        var definition = Scenario.Define(Scenario.Map(3, 1,
                cells: [new(new(fireX, 0), TerrainKind.StoneFloor) { TerrainTypeId = "campfire" }]),
            [Scenario.Group(UnitTypeIds.Wizard, HeroesVsMonsters.Heroes, ControllerKind.Human, Scenario.At(0, 0))]);
        Assert.Equal(error, Assert.Throws<InvalidOperationException>(() => DungeonGenerator.VerifyConnectivity(definition)).Message);
    }

    [Fact]
    public void ConnectivityVerifierResolvesTypedDefaultTerrain()
    {
        var board = Scenario.Map(3, 1, cells: [Scenario.Tile(0, 0, TerrainKind.Grass), Scenario.Tile(1, 0, TerrainKind.Grass)])
            with { DefaultTerrainTypeId = "campfire" };
        var definition = Scenario.Define(board,
            [Scenario.Group(UnitTypeIds.Wizard, HeroesVsMonsters.Heroes, ControllerKind.Human, Scenario.At(0, 0))]);
        var distances = DungeonGenerator.VerifyConnectivity(definition);
        Assert.Equal(new[] { new Cell(0, 0), new Cell(1, 0) }, distances.Keys);
    }

    [Theory]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F0-QClassic-S0000000000000000", "DEC4D7AEC2F8EC2C")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F0-QClassic-S0000000000000001", "E4F0B56DC91F7C07")]
    [InlineData("DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F0-QClassic-S0000000000000002", "9555E84506A01C0E")]
    public void CampfireRegressionRecipesPreserveDeterministicOutput(string recipe, string fingerprint) =>
        Assert.Equal(fingerprint, DungeonGenerator.Generate(recipe).Fingerprint);

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
        var campfires = 0; var mapsWithCampfires = 0; var mostCampfires = 0;
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
            Assert.Equal(generated.Fingerprint, DungeonGenerator.Generate(code).Fingerprint);
            var fireCount = generated.Definition.Board.Cells.Count(c => c.TerrainTypeId == "campfire");
            campfires += fireCount;
            if (fireCount > 0) mapsWithCampfires++;
            mostCampfires = Math.Max(mostCampfires, fireCount);
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
        Assert.True(campfires > 0, "The seed corpus must exercise canonical Campfire placement.");
        output.WriteLine($"{count} scenarios; plans={string.Join(", ", plans)}; max attempts={mostAttempts}; max defenders={mostEnemies}; max multiplier={largestMultiplier}; unusual={string.Join(", ", unusual)}");
        output.WriteLine($"Campfires: {campfires} across {count} maps; {mapsWithCampfires} maps contain fires; max per map={mostCampfires}.");
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
        var reachable = ApproachRules.Distances(state.Physical.Board, definition.Units[0].Anchor, closedDoorsTraversable: true);
        Assert.Equal(reachable.Keys.OrderBy(c => c.Y).ThenBy(c => c.X), distances.Keys.OrderBy(c => c.Y).ThenBy(c => c.X));
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
        Assert.Null(definition.TerrainTypes);
        Assert.Null(definition.FixedComponentTypes);
        Assert.Null(definition.TerrainAssemblies);
        var fires = definition.Board.Cells.Where(c => c.TerrainTypeId == "campfire").ToArray();
        Assert.InRange(fires.Length, 0, 4);
        Assert.All(diagnostics.Rooms, room => Assert.True(fires.Count(fire => room.Contains(fire.Position)) <= 1));
        foreach (var fire in fires)
        {
            Assert.Equal(TerrainCatalog.Find(TerrainCatalog.All, "campfire"), state.Physical.Board.TerrainDefinitionAt(fire.Position));
            Assert.DoesNotContain(fire.Position, reachable.Keys);
            Assert.DoesNotContain(fire.Position, occupied);
            Assert.DoesNotContain(definition.LootMarkers!, marker => marker.Position == fire.Position);
            Assert.False(diagnostics.Rooms[diagnostics.StartRoom].Contains(fire.Position));
            Assert.False(diagnostics.Rooms[diagnostics.DestinationRoom].Contains(fire.Position));
            Assert.Single(diagnostics.Rooms.Where(room => room.Contains(fire.Position)));
            Assert.DoesNotContain(fire.Position, diagnostics.Corridors.SelectMany(path => path.Cells));
            Assert.True(Manhattan(fire.Position, heroStart) >= 6);
            Assert.All(fires.Where(other => other != fire), other => Assert.True(Manhattan(fire.Position, other.Position) >= 6));
            // A fire is a feature within a usable clearing, never a plug in a passage.
            Assert.All(MovementRules.Neighbors(fire.Position), neighbor =>
            {
                Assert.True(state.Physical.Board.TerrainDefinitionAt(neighbor).Passable);
                Assert.True(state.Physical.Board.EdgeBetween(fire.Position, neighbor).Passable());
            });
            var surrounding = from dx in Enumerable.Range(-1, 3)
                              from dy in Enumerable.Range(-1, 3)
                              where dx != 0 || dy != 0
                              select new Cell(fire.Position.X + dx, fire.Position.Y + dy);
            Assert.True(surrounding.Count(c => state.Physical.Board.TerrainDefinitionAt(c).Passable) >= 6);
        }
        foreach (var u in definition.Units.Where(u => UnitContent.Find(u.UnitTypeId)!.Footprint == Footprint.TwoByTwo))
        {
            var queries = new GameplayQueries(state);
            Assert.True(queries.MovementPathsFrom(u.UnitId!, u.Anchor).Count > 1);
        }
    }

    private static int Manhattan(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
