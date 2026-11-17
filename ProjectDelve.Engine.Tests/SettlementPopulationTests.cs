using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SettlementPopulationTests
{
    [Fact]
    public void ClosedDoorConcealsInteriorUntilOpenedAndExplorationSurvivesSave()
    {
        var outside = new Cell(2, 2); var inside = new Cell(3, 2);
        var scout = new UnitType("fixture-door-scout", Mov: 2, Rng: 0, Atk: 0, Def: 0, Hp: 1)
            .WithLocal(new("open-door", "Open Door", AbilityTiming.FreeAction, new ProjectDelve.Engine.OpenDoor()));
        var definition = new ScenarioDefinition
        {
            Board = new() { Width = 7, Height = 5, DefaultTerrain = TerrainKind.StoneFloor,
                Edges = Enumerable.Range(0, 5).Select(y => new EdgeDefinition(new(2, y), EdgeDirection.Right,
                    y == 2 ? EdgeKind.ClosedDoor : EdgeKind.Wall)).ToList() },
            UnitTypeIds = [scout.Id], UnitTypes = [scout],
            Units = [new(scout.Id, "explorers", outside, UnitId: "scout")],
            Agency = [new(scout.Id, "explorers", ControllerKind.Human)], FogOfWarSides = ["explorers"],
            Objectives = [new("explorers", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty
        };
        var state = GameEngine.CreateGame(definition);
        Assert.False(FogOfWar.IsExplored(state, "explorers", inside));
        state.Round = 1; state.ActiveToken = new(scout.Id, "explorers"); state.CurrentUnitId = "scout";
        state.Pending = new(DecisionKind.Activation, scout.Id, "scout", [], false) { Token = state.ActiveToken };
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
                Civilization: new(Campsites: ContentDensity.None, Settlements: ContentDensity.Low), Population: new(PopulationDensity.High, 4));
            var world = WorldGenerator.Generate(recipe);
            var site = Assert.Single(world.Context.Sites);
            Assert.NotNull(site.PopulationIntent);
            var encounters = world.Context.Population!.Encounters;
            var whole = encounters.Where(e => e.SourceId == site.Id).ToArray();
            var houses = encounters.Where(e => site.Buildings.Any(b => b.Id == e.SourceId)).ToArray();
            Assert.False(whole.Length > 0 && houses.Length > 0);
            if (whole.Length > 0)
            {
                var parent = world.Context.Population.SiteStatistics.Single(s => s.SourceId == site.Id);
                Assert.Equal(1, parent.RequestedEncounters);
                Assert.Equal(whole.Single().RequestedUnits, parent.RequestedUnits);
                Assert.All(world.Context.Population.SiteStatistics.Where(s => s.SiteId == site.Id && s.Kind == "Building"), member =>
                {
                    Assert.Equal("CompoundMember", member.Selection);
                    Assert.Equal(0, member.RequestedEncounters); Assert.Equal(0, member.RequestedUnits);
                });
            }
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
            Civilization: new(Campsites: ContentDensity.None, Settlements: ContentDensity.Low), Population: new(PopulationDensity.High, 5)), content);
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
            Civilization: new(Campsites: ContentDensity.None, Settlements: ContentDensity.Low), Population: new(PopulationDensity.High, 4));
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
