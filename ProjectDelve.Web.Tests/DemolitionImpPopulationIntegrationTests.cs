using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Web.Tests;

public sealed class DemolitionImpPopulationIntegrationTests(ITestOutputHelper output)
{
    private const string DemolitionImp = "demolition-imp";
    private static readonly Lazy<ContentBundle> PublishedContent = new(() => new CanonicalContent(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web/Content/Canonical"))).Bundle);

    private static WorldGenerationRecipe Recipe(ulong seed, bool mountain) => new(
        new(Width: 60, Height: 50), mountain ? new(15, 8, 50, FeatureScale.Broad, WaterForm.Mixed) : new(0, 0, 0), seed,
        // Population integration needs dungeon encounters; early natural cave selection
        // is independent coverage and must not determine whether this fixture has Imps.
        Enrichment: mountain ? new(Rock: false) : null, Civilization: mountain ? new() : null,
        Routes: mountain ? new(Profile: RouteProfileKind.RockPassage) : null, Population: new(PopulationDensity.High, 3));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishedImpOccursInExistingGroupsAndSurvivesQuickPlayAndTransport(bool mountain)
    {
        var content = PublishedContent.Value;
        var type = Assert.Single(content.Units, t => t.Id == DemolitionImp);
        Assert.Equal(1, type.Mechanic<Explosion>()!.Damage);
        Assert.Equal(6, type.Mechanic<BreakAdjacentObjects>()!.SuccessThreshold);
        GeneratedWorld? selected = null;
        var sawMultipleInGroup = false;
        var totalMonsters = 0; var totalBombImps = 0; var totalDemolitionImps = 0;
        // Fixed, bounded generated fixtures; no authored/playtest scenario assumptions.
        // Mountain fixtures use the fixed seeds 1..15.
        for (ulong seed = mountain ? 1UL : 0UL; seed < 16; seed++)
        {
            var world = WorldGenerator.Generate(Recipe(seed, mountain), content);
            var encounters = world.Context.Population!.Encounters.Where(e =>
                mountain ? e.Kind == "DungeonEncounter" : e.Kind is "WildernessGroup" or "LoneMonster").ToArray();
            var monsters = encounters.SelectMany(e => e.Units).ToArray();
            totalMonsters += monsters.Length;
            totalBombImps += monsters.Count(u => u.UnitTypeId == UnitTypeIds.BombImp);
            var imps = monsters.Where(u => u.UnitTypeId == DemolitionImp).ToArray();
            totalDemolitionImps += imps.Length;
            if (imps.Length == 0) continue;
            selected ??= world;
            sawMultipleInGroup |= imps.GroupBy(u => (u.SideId, u.GroupId)).Any(g => g.Count() > 1);
            output.WriteLine($"{(mountain ? "mountain" : "outdoor")} seed {seed}: {imps.Length} Demolition Imps; multiple in group: {sawMultipleInGroup}");
            Assert.Equal(imps.Length, imps.Select(u => u.UnitId).Distinct().Count());
            foreach (var imp in imps)
            {
                var encounter = Assert.Single(encounters, e => e.Units.Contains(imp));
                Assert.Contains(encounter.Agency, a => a.UnitTypeId == DemolitionImp && a.GroupId == imp.GroupId && a.SideId == imp.SideId && a.Controller == ControllerKind.Automated);
                Assert.Contains(encounter.Orders, o => o.UnitTypeId == DemolitionImp && o.GroupId == imp.GroupId);
            }
        }
        output.WriteLine($"Corpus totals: {totalMonsters} Monsters, {totalBombImps} Bomb Imps, {totalDemolitionImps} Demolition Imps.");
        Assert.NotNull(selected);
        Assert.True(sawMultipleInGroup, "The bounded corpus must exercise multiple physical Imps sharing an ordinary Group.");
        var off = WorldGenerator.Generate(WorldGenerationRecipe.Parse(selected!.Code) with { Population = new(PopulationDensity.Off) }, content);
        Assert.Empty(off.Definition.Units);
        Assert.DoesNotContain(off.Definition.UnitTypeIds, id => id == DemolitionImp);
        var original = selected.Definition;
        var json = ScenarioDefinitionJson.ToJson(original);
        var repeated = WorldGenerator.Generate(selected.Code, content);
        Assert.Equal(selected.Fingerprint, repeated.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(selected.Context.Population), JsonSerializer.Serialize(repeated.Context.Population));
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(original), null, content);
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(restored));
        var prepared = WorldQuickPlay.Prepare(restored, content);
        var game = GameEngine.CreateGame(prepared, content);
        foreach (var imp in original.Units.Where(u => u.UnitTypeId == DemolitionImp))
        {
            Assert.Contains(prepared.Units, u => u == imp);
            var unit = Assert.Single(game.Units, u => u.Id == imp.UnitId);
            Assert.Equal(DemolitionImp, unit.TypeId);
            Assert.Equal(imp.GroupId, unit.GroupId);
        }
        var saved = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(game))!;
        Assert.Equal(JsonSerializer.Serialize(game), JsonSerializer.Serialize(saved));
        Assert.Equal(JsonSerializer.Serialize(type), JsonSerializer.Serialize(Assert.Single(saved.Types, t => t.Id == DemolitionImp)));
        Assert.Equal(json, ScenarioDefinitionJson.ToJson(original));
    }

    [Fact]
    public void GeneratedImpUsesPublishedExplosionThenDestructionInDedicatedEncounter()
    {
        var content = PublishedContent.Value;
        UnitPlacement? generatedImp = null;
        for (ulong seed = 0; seed < 8 && generatedImp is null; seed++)
            generatedImp = WorldGenerator.Generate(Recipe(seed, false), content).Definition.Units.FirstOrDefault(u => u.UnitTypeId == DemolitionImp);
        Assert.NotNull(generatedImp);
        // Retain the generated physical instance and published Type; only arrange a test-owned combat fixture.
        var imp = generatedImp! with { Anchor = new(2, 2), Posture = Posture.Upright, Dormant = false };
        var publishedTypeJson = JsonSerializer.Serialize(content.Units.Single(t => t.Id == DemolitionImp));
        var internalWall = new Edge(new(2, 3), new(3, 3), EdgeKind.Wall);
        var perimeterWalls = new[]
        {
            new Edge(new(3, 1), new(4, 1), EdgeKind.Wall),
            new Edge(new(1, 3), new(1, 4), EdgeKind.Wall)
        };
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 6, Height = 5, Cells = [new(new(2, 1), TerrainKind.Tree)],
                Edges = [new(internalWall.A, EdgeDirection.Right, internalWall.Kind),
                    new(perimeterWalls[0].A, EdgeDirection.Right, EdgeKind.Wall),
                    new(perimeterWalls[1].A, EdgeDirection.Down, EdgeKind.Wall)] },
            UnitTypeIds = ["test-attacker", "test-witness", DemolitionImp],
            UnitTypes = [new("test-attacker", 0, 1, 6, 0, 5) { Unique = true }, new("test-witness", 0, 0, 0, 0, 5, Actions: UnitAction.None) { Unique = true }],
            Units = [new("test-attacker", "blue", new(1, 2), UnitId: "attacker"), imp,
                new("test-witness", "blue", new(3, 2), UnitId: "witness")],
            Agency = [new("test-attacker", "blue", ControllerKind.Human), new("test-witness", "blue", ControllerKind.Human),
                new(DemolitionImp, imp.SideId, ControllerKind.Human, imp.GroupId)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new(imp.SideId, ScenarioObjective.NoObjective)],
            Loot = LootSettings.Empty
        };
        definition = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition), null, content);
        var dice = new Dice();
        var result = GameEngine.StartRound(GameEngine.CreateGame(definition, content), dice, false);
        if (result.NextInput!.Candidates.Any(c => c.Key == "stay"))
            result = GameEngine.Advance(result.State, new Pick("stay"), dice, false);
        result = GameEngine.Advance(result.State, new Pick("attack:" + imp.UnitId), dice, false);
        var events = result.Events.ToList();
        while (result.NextInput?.Kind == DecisionKind.RollDice)
        {
            result = GameEngine.Advance(result.State, new Pick("roll-dice"), dice, false);
            events.AddRange(result.Events);
        }
        var explosion = events.FindIndex(e => e.Kind == "ExplosionDamageResolved" && e.UnitId == imp.UnitId && e.TargetId == "witness");
        var destruction = events.FindIndex(e => e.Kind == "AdjacentObjectsBreakResolved" && e.UnitId == imp.UnitId);
        Assert.True(explosion >= 0 && destruction > explosion);
        Assert.Equal(4, result.State.Units.Single(u => u.Id == "witness").CurrentHp);
        Assert.Equal("grass", result.State.Physical.Board.TerrainDefinitionAt(new(2, 1)).Id);
        Assert.Equal(EdgeKind.None, result.State.Physical.Board.EdgeBetween(internalWall.A, internalWall.B));
        Assert.All(perimeterWalls, edge => Assert.Equal(EdgeKind.Wall, result.State.Physical.Board.EdgeBetween(edge.A, edge.B)));
        var outcomes = events[destruction].BreakOutcomes;
        Assert.Contains(outcomes, o => o.Target.Edge == internalWall && o.Succeeded);
        Assert.DoesNotContain(outcomes, o => o.Target.Edge is { } edge && perimeterWalls.Contains(edge));
        Assert.Empty(result.State.PendingDefeatEffects);
        Assert.Equal(publishedTypeJson, JsonSerializer.Serialize(content.Units.Single(t => t.Id == DemolitionImp)));
    }

    private sealed class Pick(string key) : IDecisionProvider
    {
        public string Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.TypeId == "test-attacker") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Hit;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
    }
}
