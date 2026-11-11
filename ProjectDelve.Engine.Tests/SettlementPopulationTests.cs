using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SettlementPopulationTests
{
    [Fact]
    public void GeneratedDoorConcealsInteriorUntilOpenedAndExplorationSurvivesSave()
    {
        var world = WorldGenerator.Generate(new WorldGenerationRecipe(new(Width: 40, Height: 35), new(0, 0, 0), 42,
            Civilization: new(Campsites: 0)));
        var portal = world.Context.Sites.SelectMany(s => s.Buildings).First().Portals.Single(p => p.Id.EndsWith("/entrance"));
        var outside = new Cell(portal.OutsideIndex % 40, portal.OutsideIndex / 40);
        var inside = new Cell(portal.InsideIndex % 40, portal.InsideIndex / 40);
        var definition = world.Definition with
        {
            UnitTypeIds = [UnitTypeIds.Barbarian],
            Units = [new(UnitTypeIds.Barbarian, "explorers", outside, UnitId: "scout")],
            Agency = [new(UnitTypeIds.Barbarian, "explorers", ControllerKind.Human)], FogOfWarSides = ["explorers"]
        };
        var state = GameEngine.CreateGame(definition);
        Assert.False(FogOfWar.IsExplored(state, "explorers", inside));
        state.Round = 1; state.ActiveToken = new(UnitTypeIds.Barbarian, "explorers"); state.CurrentUnitId = "scout";
        state.Pending = new(DecisionKind.Activation, UnitTypeIds.Barbarian, "scout", [], false) { Token = state.ActiveToken };
        var opened = GameEngine.Advance(state, new OpenDoor($"ability:open-door:{outside.X},{outside.Y}:{inside.X},{inside.Y}"), new Dice(), false);
        Assert.True(FogOfWar.IsExplored(opened.State, "explorers", inside));
        var saved = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(opened.State))!;
        FogOfWar.Refresh(saved);
        Assert.True(FogOfWar.IsExplored(saved, "explorers", inside));
    }

    [Fact]
    public void SettlementOccupationCanBeVacantIndependentOrCompoundWithoutChangingGeometry()
    {
        bool vacant = false, individual = false, compound = false, partiallyVacant = false;
        var monsterTypes = new HashSet<string>();
        for (ulong seed = 1; seed <= 24; seed++)
        {
            var recipe = new WorldGenerationRecipe(new(Width: 40, Height: 35), new(0, 0, 0), seed,
                Civilization: new(Campsites: 0, Settlements: 1), Population: new(PopulationDensity.High, 4));
            var world = WorldGenerator.Generate(recipe);
            var site = Assert.Single(world.Context.Sites);
            Assert.NotNull(site.PopulationIntent);
            var encounters = world.Context.Population!.Encounters;
            var whole = encounters.Where(e => e.SourceId == site.Id).ToArray();
            var houses = encounters.Where(e => site.Buildings.Any(b => b.Id == e.SourceId)).ToArray();
            Assert.False(whole.Length > 0 && houses.Length > 0);
            vacant |= whole.Length == 0 && houses.Length == 0;
            individual |= houses.Length > 1;
            compound |= whole.Length > 0;
            partiallyVacant |= houses.Length > 0 && houses.Length < site.Buildings.Count;
            foreach (var e in whole.Concat(houses))
            {
                monsterTypes.UnionWith(e.Units.Select(u => u.UnitTypeId));
                Assert.All(e.Units, u => Assert.True(u.Dormant));
            }
            var protectedAccess = site.SettlementLayout!.Access.Indices(40).ToHashSet();
            var types = ContentBundle.Canonical().Units.ToDictionary(t => t.Id);
            Assert.All(world.Definition.Units.SelectMany(u => FootprintGeometry.OccupiedCells(types[u.UnitTypeId].Footprint, u.Anchor)),
                cell => Assert.DoesNotContain(cell.Y * 40 + cell.X, protectedAccess));
            Assert.All(encounters.Where(e => e.Kind is "LoneMonster" or "WildernessGroup").SelectMany(e => e.Units)
                .SelectMany(u => FootprintGeometry.OccupiedCells(types[u.UnitTypeId].Footprint, u.Anchor)),
                cell => Assert.False(site.Footprint.Contains(cell.Y * 40 + cell.X, 40)));
            var empty = WorldGenerator.Generate(recipe with { Population = new(PopulationDensity.Off) });
            Assert.Empty(empty.Definition.Units);
            Assert.Equal(JsonSerializer.Serialize(empty.Definition.Board), JsonSerializer.Serialize(world.Definition.Board));
        }
        Assert.True(vacant); Assert.True(individual); Assert.True(compound); Assert.True(partiallyVacant);
        Assert.True(monsterTypes.Count > 1);
    }

    [Fact]
    public void LargeNaturalOccupantsCannotOverhangVacantSettlementGeometry()
    {
        var content = ContentBundle.Canonical();
        var pool = HeroesVsMonsters.Pool.Select(p => p.UnitTypeId).ToHashSet();
        content = content with { Units = [.. content.Units.Select(t => pool.Contains(t.Id) ? t with { Footprint = Footprint.TwoByTwo } : t)] };
        var world = WorldGenerator.Generate(new WorldGenerationRecipe(new(Width: 40, Height: 35), new(0, 0, 0), 12,
            Civilization: new(Campsites: 0), Population: new(PopulationDensity.High, 5)), content);
        var site = Assert.Single(world.Context.Sites);
        var natural = world.Context.Population!.Encounters.Where(e => e.Kind is "LoneMonster" or "WildernessGroup").ToArray();
        Assert.NotEmpty(natural);
        var types = content.Units.ToDictionary(t => t.Id);
        Assert.All(natural.SelectMany(e => e.Units).SelectMany(u => FootprintGeometry.OccupiedCells(types[u.UnitTypeId].Footprint, u.Anchor)),
            cell => Assert.False(site.Footprint.Contains(cell.Y * 40 + cell.X, 40)));
        _ = GameEngine.CreateGame(world.Definition, content);
    }

    [Fact]
    public void SettlementScenarioAndSaveRoundtripPreserveConcreteBuildingsAndOccupants()
    {
        var recipe = new WorldGenerationRecipe(new(Width: 40, Height: 35), new(0, 0, 0), 42,
            Civilization: new(Campsites: 0), Population: new(PopulationDensity.High, 4));
        var world = WorldGenerator.Generate(recipe);
        Assert.NotEmpty(world.Context.Sites.SelectMany(s => s.Buildings));
        var repeated = WorldGenerator.Generate(world.Code);
        Assert.Equal(world.Fingerprint, repeated.Fingerprint);
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(world.Definition));
        Assert.Equal(ScenarioDefinitionJson.ToJson(world.Definition), ScenarioDefinitionJson.ToJson(restored));
        var state = GameEngine.CreateGame(restored);
        var saved = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(state.Units, saved.Units); Assert.Equal(state.Orders, saved.Orders);
        Assert.Equal(JsonSerializer.Serialize(state.Physical.Board), JsonSerializer.Serialize(saved.Physical.Board));
    }

    private sealed class OpenDoor(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Block;
        public int RollD6() => 1;
    }
}
