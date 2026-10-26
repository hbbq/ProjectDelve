using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class DungeonGenerationTests(ITestOutputHelper output)
{
    private const string Prefix = "DNG1-HVM-30x20-R8-C2-D2-N2-L3-OR2-F1";
    [Fact]
    public void PcgMatchesPublishedReferenceVector()
    {
        var random = new Pcg32(42, 54);
        Assert.Equal(new uint[] { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e },
            Enumerable.Range(0, 6).Select(_ => random.NextUInt()));
    }
    [Fact]
    public void CodesAreStrictCanonicalAndCompleteOnlyOnce()
    {
        var code = GenerationCode.Parse(Prefix);
        Assert.Null(code.Seed);
        var complete = code.Complete();
        Assert.Equal(complete, complete.Complete());
        Assert.Equal(complete, GenerationCode.Parse(complete.ToString()));
        Assert.Equal(Prefix, complete.Prefix);
        Assert.EndsWith("-S0123456789ABCDEF", GenerationCode.Parse(Prefix + "-S0123456789abcdef").ToString());
        Assert.Contains("-ORA-", GenerationCode.Parse(Prefix.Replace("OR2", "OR4")).ToString());
    }
    [Theory]
    [InlineData("")]
    [InlineData("DNG2-HVM-30x20-R8-C2-D2-N2-L3-OR2-F1")]
    [InlineData("DNG1-RACING-30x20-R8-C2-D2-N2-L3-OR2-F1")]
    [InlineData("DNG1-HVM-030x20-R8-C2-D2-N2-L3-OR2-F1")]
    [InlineData("DNG1-HVM-30x20-R8-C2-D2-N2-L3-OR2-F1-S123")]
    [InlineData("DNG1-HVM-30x20-R8-C2-D2-N2-L3-OR2-F1-extra")]
    [InlineData("DNG1-HVM-30x20-R8-C2-D2-N2-L3-OR0-F1")]
    public void InvalidCodesFailUsefully(string code) => Assert.Throws<FormatException>(() => GenerationCode.Parse(code));

    [Fact]
    public void ImpossibleConfigurationFailsBeforeGeneration()
    {
        var error = Assert.Throws<ArgumentException>(() => DungeonGenerator.Generate(new GenerationCode(14, 14, 24)));
        Assert.Contains("do not fit", error.Message);
    }
    [Theory]
    [InlineData(TerrainKind.Tree)]
    [InlineData(TerrainKind.SolidRock)]
    public void BlockingTerrainRetainsIdentityAndRejectsInitialPlacement(TerrainKind terrain)
    {
        var definition = Scenario.Define(Scenario.Map(3, 1, cells: [Scenario.Tile(1, 0, terrain)]),
            [Scenario.Group(UnitTypeIds.Ghost, "side", ControllerKind.Human, Scenario.At(0, 0))]);
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition));
        Assert.Equal(terrain, Assert.Single(restored.Board.Cells).Terrain);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(definition with { Units = [new(UnitTypeIds.Ghost, "side", new(1, 0))] }));
    }
    [Theory]
    [InlineData(0UL, "332D6B24509DCFC6")]
    [InlineData(1UL, "70C91AAB5843CB21")]
    [InlineData(42UL, "CF3993EFF29C5AFE")]
    [InlineData(0x0123456789abcdefUL, "AEAC63014842BC72")]
    [InlineData(ulong.MaxValue, "8E2D1157B55A6F4A")]
    public void Dng1RegressionSeeds(ulong seed, string fingerprint) =>
        Assert.Equal(fingerprint, DungeonGenerator.Generate(new GenerationCode(Seed: seed)).Fingerprint);
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(0x0123456789abcdefUL)]
    [InlineData(ulong.MaxValue)]
    public void CompleteCodesReproduceExactValidatedTransport(ulong seed)
    {
        var generated = DungeonGenerator.Generate(new GenerationCode(Seed: seed));
        var replay = DungeonGenerator.Generate(generated.Code.ToString());
        Assert.Equal(ScenarioDefinitionJson.ToJson(generated.Definition), ScenarioDefinitionJson.ToJson(replay.Definition));
        Assert.Equal(generated.Fingerprint, replay.Fingerprint);
        var transport = ScenarioDefinitionTransport.Encode(generated.Definition);
        Assert.Equal(ScenarioDefinitionJson.ToJson(generated.Definition), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(transport)));
        AssertGeometry(generated);
    }
    [Fact]
    public void ContentConfigurationDoesNotPerturbGeometryOrHeroes()
    {
        var a = DungeonGenerator.Generate(new GenerationCode(Seed: 42));
        var b = DungeonGenerator.Generate(new GenerationCode(Density: 3, Difficulty: 5, Objective: "OEE", Seed: 42));
        Assert.Equal(a.Diagnostics.Rooms, b.Diagnostics.Rooms);
        Assert.Equal(a.Definition.Board.Cells, b.Definition.Board.Cells);
        Assert.Equal(a.Definition.Board.Edges, b.Definition.Board.Edges);
        Assert.Equal(a.Definition.Units.Take(4), b.Definition.Units.Take(4));
        var c = DungeonGenerator.Generate(new GenerationCode(Difficulty: 5, Seed: 42));
        Assert.Equal(a.Diagnostics.OccupiedRooms, c.Diagnostics.OccupiedRooms);
        Assert.Equal(a.Definition.Units.Select(u => u.Anchor), c.Definition.Units.Select(u => u.Anchor));
    }
    [Theory]
    [InlineData("OEE", 0, 0)]
    [InlineData("ORA", 0, 1)]
    [InlineData("OR4", 0, 1)]
    [InlineData("OR3", 3, 2)]
    [InlineData("OR2", 2, 3)]
    [InlineData("OR1", 1, 4)]
    public void ObjectivesUseExistingAuthoritativeTypes(string objective, int reach, int defeat)
    {
        var generated = DungeonGenerator.Generate(new GenerationCode(Objective: objective, Seed: 123));
        var state = GameEngine.CreateGame(generated.Definition);
        var heroes = state.Objectives.Single(o => o.SideId == HeroesVsMonsters.Heroes);
        var monsters = state.Objectives.Single(o => o.SideId == HeroesVsMonsters.Monsters);
        Assert.Equal(objective == "OEE" ? ScenarioObjective.EliminateEnemies : ScenarioObjective.ReachArea, heroes.Objective);
        if (objective == "OEE") { Assert.Equal(ScenarioObjective.EliminateEnemies, monsters.Objective); return; }
        Assert.Equal(reach, heroes.Count); Assert.Equal(defeat, monsters.Count);
        Assert.Equal(ScenarioObjective.DefeatUnits, monsters.Objective);
        Assert.Equal(UnitSelector.SideAtStart(HeroesVsMonsters.Heroes), Assert.Single(heroes.Selectors));
        Assert.Equal(heroes.Selectors.ToArray(), monsters.Selectors.ToArray());
        Assert.Equal(4, state.ObjectiveStartUnitIds[HeroesVsMonsters.Heroes].Length);
        Assert.True(heroes.Area!.W * heroes.Area.H >= 4);
    }
    [Fact]
    public void FogConcealsTheWorldWithoutRestrictingMonsters()
    {
        var generated = DungeonGenerator.Generate(new GenerationCode(Seed: 42));
        var state = GameEngine.CreateGame(generated.Definition);
        Assert.Equal(new[] { HeroesVsMonsters.Heroes }, generated.Definition.FogOfWarSides);
        var observed = FogOfWar.Observe(state, HeroesVsMonsters.Heroes);
        Assert.True(observed.Physical.Figures.Count < state.Physical.Figures.Count);
        Assert.Contains(state.Physical.Figures, f => !FogOfWar.IsUnitObservable(state, HeroesVsMonsters.Heroes, f.Id));
        Assert.All(state.Physical.Figures, f => Assert.True(FogOfWar.IsUnitObservable(state, HeroesVsMonsters.Monsters, f.Id)));
        Assert.True(state.ExploredCells[HeroesVsMonsters.Heroes].Count < 200);
        Assert.Empty(GameEngine.CreateGame(generated.Definition with { FogOfWarSides = null }).ExploredCells);
    }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 6;
    }
    private sealed class EndTurn : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => "end-turn"; }
    private static EngineResult FinishHeroActivation(GameState state)
    {
        var hero = state.Units.First(u => u.SideId == HeroesVsMonsters.Heroes && u.CurrentHp > 0);
        state.Round = 1; state.ActiveToken = new(hero.TypeId, hero.SideId); state.CurrentUnitId = hero.Id;
        state.MoveDone = state.ActionDone = true;
        state.Pending = new(DecisionKind.Activation, hero.TypeId, hero.Id, [], false) { Token = state.ActiveToken };
        return GameEngine.Advance(state, new EndTurn(), new Dice(), false);
    }
    [Theory]
    [InlineData("OR1", 1)]
    [InlineData("OR2", 2)]
    [InlineData("OR3", 3)]
    [InlineData("ORA", 4)]
    public void GeneratedReachObjectiveWinsAtTheExistingBoundary(string objective, int count)
    {
        var state = GameEngine.CreateGame(DungeonGenerator.Generate(new GenerationCode(Objective: objective, Seed: 42)).Definition);
        var area = state.Objectives.Single(o => o.SideId == HeroesVsMonsters.Heroes).Area!;
        var destinations = (from y in Enumerable.Range(area.Y, area.H) from x in Enumerable.Range(area.X, area.W)
                            let cell = new Cell(x, y) where state.Physical.Figures.All(f => f.Position != cell) select cell).Take(count).ToArray();
        for (var i = 0; i < count; i++)
        {
            var index = state.Physical.Figures.FindIndex(f => f.Id == $"hero-{i + 1}");
            state.Physical.Figures[index] = state.Physical.Figures[index] with { Position = destinations[i] };
        }
        FogOfWar.Refresh(state);
        Assert.Equal(new[] { HeroesVsMonsters.Heroes }, FinishHeroActivation(state).State.Completion!.AchievedSideIds);
    }
    [Fact]
    public void GeneratedEliminateEnemiesStillCountsASummonedHostile()
    {
        var state = GameEngine.CreateGame(DungeonGenerator.Generate(new GenerationCode(Objective: "OEE", Seed: 42)).Definition);
        foreach (var monster in state.Units.Where(u => u.SideId == HeroesVsMonsters.Monsters).ToArray())
        {
            state.DefeatedUnitIds.Add(monster.Id);
            state.Units[state.Units.FindIndex(u => u.Id == monster.Id)] = monster with { CurrentHp = 0 };
            state.Physical.Figures.RemoveAll(f => f.Id == monster.Id);
        }
        if (!state.Types.Any(t => t.Id == UnitTypeIds.Goblin)) state.Types.Add(UnitRoster.Goblin());
        var cell = state.Physical.Board.Terrain.First(t => t.Kind.Passable() && state.Physical.Figures.All(f => f.Position != t.Position)).Position;
        state.PlaceUnit(UnitTypeIds.Goblin, "summoned", HeroesVsMonsters.Monsters, cell);
        Assert.Null(FinishHeroActivation(state).State.Completion);
        state.DefeatedUnitIds.Add("summoned"); state.Physical.Figures.RemoveAll(f => f.Id == "summoned");
        var summoned = state.Units.FindIndex(u => u.Id == "summoned"); state.Units[summoned] = state.Units[summoned] with { CurrentHp = 0 };
        Assert.Equal(new[] { HeroesVsMonsters.Heroes }, FinishHeroActivation(state).State.Completion!.AchievedSideIds);
    }
    [Fact]
    public void DeterministicCorpusCoversDimensionsDensitiesThreatsAndTopology()
    {
        var counts = new int[3]; var occupied = new int[3]; var meanThreat = new double[5];
        var maxAttempts = 0; var leaves = 0; var branches = 0; var totalDistance = 0; var totalExplored = 0;
        for (ulong seed = 0; seed < 24; seed++)
        {
            for (var density = 1; density <= 3; density++)
            {
                var result = DungeonGenerator.Generate(new GenerationCode(Density: density, Seed: seed)); AssertGeometry(result);
                counts[density - 1] += result.Diagnostics.Monsters; occupied[density - 1] += result.Diagnostics.OccupiedRooms;
                maxAttempts = Math.Max(maxAttempts, result.Diagnostics.Attempts);
            }
            for (var difficulty = 1; difficulty <= 5; difficulty++)
            {
                var result = DungeonGenerator.Generate(new GenerationCode(Difficulty: difficulty, Seed: seed));
                var threats = result.Definition.Units.Skip(4).Select(u => HeroesVsMonsters.Pool.Single(m => m.UnitTypeId == u.UnitTypeId).Threat);
                meanThreat[difficulty - 1] += threats.Average();
            }
            var tree = DungeonGenerator.Generate(new GenerationCode(Connectivity: 0, Seed: seed));
            Assert.Equal(7, tree.Diagnostics.Connections.Count);
            var degrees = Enumerable.Range(0, 8).Select(i => tree.Diagnostics.Connections.Count(c => c.A == i || c.B == i)).ToArray();
            leaves += degrees.Count(d => d == 1); branches += degrees.Count(d => d >= 3);
            Assert.InRange(tree.Diagnostics.DestinationDistance, 8, 200);
            totalDistance += tree.Diagnostics.DestinationDistance;
            totalExplored += GameEngine.CreateGame(tree.Definition).ExploredCells[HeroesVsMonsters.Heroes].Count;
        }
        Assert.True(counts[0] < counts[1] && counts[1] < counts[2]);
        Assert.True(occupied[0] < occupied[1] && occupied[1] < occupied[2]);
        for (var i = 1; i < 5; i++) Assert.True(meanThreat[i] > meanThreat[i - 1]);
        Assert.InRange(meanThreat[0] / 24, 1.1, 2.5); Assert.InRange(meanThreat[4] / 24, 3.5, 4.9);
        Assert.True(leaves > 48 && branches > 12); Assert.InRange(maxAttempts, 1, DungeonGenerator.MaxAttempts);
        output.WriteLine($"24 seeds: Monster totals sparse/normal/dense={string.Join('/', counts)}; occupied rooms={string.Join('/', occupied)} of 168 each. Mean Threat by difficulty={string.Join('/', meanThreat.Select(t => (t / 24).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}.");
        output.WriteLine($"Tree layouts: {leaves} leaf rooms, {branches} branch rooms; mean destination distance={totalDistance / 24.0:F1}; initial explored fraction={totalExplored / (24.0 * 600):P1}; maximum attempts={maxAttempts}; failures=0 across 216 generated samples in the comparison.");
        foreach (var config in new[] { new GenerationCode(14,14,4,0,0), new(26,38,16,3,3), new(50,50,24,3,3), new(50,14,8,1) })
            for (ulong seed = 0; seed < 4; seed++) AssertGeometry(DungeonGenerator.Generate(config with { Seed = seed }));
    }
    [Fact]
    public void VariedSeedCorpusHasValidRoomsEdgesAndPlacements()
    {
        for (ulong seed = 100; seed < 228; seed++)
        {
            var config = (seed % 4) switch
            {
                0 => new GenerationCode(14, 14, 4, 0),
                1 => new GenerationCode(26, 38, 16, 3),
                2 => new GenerationCode(50, 26, 24, 2),
                _ => new GenerationCode(38, 14, 6, 1)
            };
            AssertGeometry(DungeonGenerator.Generate(config with { Seed = seed, Doors = (int)(seed % 4) }));
        }
    }
    private static void AssertGeometry(GeneratedDungeon result)
    {
        Assert.Equal(result.Code.Rooms, result.Diagnostics.Rooms.Count);
        var treeConnections = result.Code.Rooms - 1;
        Assert.Equal(treeConnections + Math.Min(result.Code.Connectivity, result.Code.Rooms * (result.Code.Rooms - 1) / 2 - treeConnections), result.Diagnostics.Connections.Count);
        var definition = result.Definition; var board = definition.Board;
        var distances = DungeonGenerator.VerifyConnectivity(definition);
        Assert.Equal(board.Cells.Count, distances.Count);
        Assert.Equal(board.Edges.Count, board.Edges.Select(e => (e.Position, e.Direction)).Distinct().Count());
        Assert.Equal(result.Diagnostics.Connections.Count, result.Diagnostics.Connections.Distinct().Count());
        Assert.Equal(HeroesVsMonsters.HeroTypes, definition.Units.Take(4).Select(u => u.UnitTypeId));
        Assert.Equal(definition.Units.Count, definition.Units.Select(u => u.Anchor).Distinct().Count());
        Assert.All(definition.Units, u => Assert.Contains(u.Anchor, distances.Keys));
        Assert.DoesNotContain(definition.Units.Skip(4), u => result.Diagnostics.Rooms[result.Diagnostics.StartRoom].Contains(u.Anchor));
        Assert.All(definition.Units.Skip(4), u => Assert.Contains(HeroesVsMonsters.Pool, m => m.UnitTypeId == u.UnitTypeId));
        Assert.InRange(definition.Units.Count(u => u.UnitTypeId == UnitTypeIds.Shaman), 0, 1);
        foreach (var edge in board.Edges.Where(e => e.Kind == EdgeKind.ClosedDoor))
        {
            var other = new Cell(edge.Position.X + (edge.Direction == EdgeDirection.Right ? 1 : 0), edge.Position.Y + (edge.Direction == EdgeDirection.Down ? 1 : 0));
            Assert.DoesNotContain(definition.Units, u => u.Anchor == edge.Position || u.Anchor == other);
        }
        _ = GameEngine.CreateGame(definition);
    }
}


