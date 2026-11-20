using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class ExpeditionCreditsGenerationTests(ITestOutputHelper output)
{
    private static readonly ContentBundle Content = ContentBundle.Canonical();
    private static readonly WorldGenerationRecipe Recipe = new(new(0, 0, 60, 60), new(0, 0, 0), 17,
        new(Enabled: false), new(Enabled: false), new(Enabled: false), new(PopulationDensity.Low));

    [Theory]
    [InlineData(17UL, 5)]
    [InlineData(42UL, 6)]
    [InlineData(889UL, 6)]
    public void IndependentStagePreservesEverythingExceptAddedPiles(ulong seed, int capacity)
    {
        var profile = ExpeditionWorldProfile.Create(Recipe.NaturalTerrain) with
        { CreditsSettings = new(OpenGroundChance: 100) };
        var current = Execute(seed, profile, capacity);
        var previous = Execute(seed, profile with { CreditsVersion = null, CreditsSettings = null }, capacity);
        var definition = current.Materialize();
        var baseline = previous.Materialize();
        Assert.NotEmpty(definition.CreditsPiles!);
        Assert.Equal(ScenarioDefinitionJson.ToJson(baseline),
            ScenarioDefinitionJson.ToJson(definition with { CreditsPiles = baseline.CreditsPiles }));
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(Execute(seed, profile, capacity).Materialize()));
        Assert.Equal(JsonSerializer.Serialize(previous.Sites), JsonSerializer.Serialize(current.Sites));
        Assert.Equal(JsonSerializer.Serialize(previous.Construction), JsonSerializer.Serialize(current.Construction));
        var state = GameEngine.CreateGame(definition, Content);
        Assert.True(PlayReadyScenarioSetup.Validate(definition, state).Valid);
        Assert.Empty(current.Completion!.ActualFailures(state));
        Assert.True(current.Expedition!.HasOccupiedAccess(state, current.Deployment!));
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(
            ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition))));
    }

    [Fact]
    public void PopulatedConstructedRegionPreservesEarlierGenerationEndToEnd()
    {
        var recipe = new WorldGenerationRecipe(new(0, 0, 60, 60), new(), 17, new(), new(), new(), new());
        var profile = ExpeditionWorldProfile.Create(recipe.NaturalTerrain);
        WorldGenerationContext Generate(ExpeditionWorldProfile p) => ExpeditionRegionGeometry.Execute(recipe,
            new(0, 30), new(59, 20), new(57, 19, 2, 3), 17, Content, profile: p).Context;
        var before = Generate(profile with { CreditsVersion = null, CreditsSettings = null });
        var after = Generate(profile);
        var original = before.Materialize();
        var result = after.Materialize();
        Assert.NotEmpty(before.Sites);
        Assert.NotEmpty(original.Units);
        Assert.NotEmpty(original.Machines!);
        Assert.NotEmpty(original.LootMarkers!);
        Assert.Equal(ScenarioDefinitionJson.ToJson(original), ScenarioDefinitionJson.ToJson(result with { CreditsPiles = original.CreditsPiles }));
        Assert.Equal(JsonSerializer.Serialize(before.Sites), JsonSerializer.Serialize(after.Sites));
        Assert.Equal(JsonSerializer.Serialize(before.Construction), JsonSerializer.Serialize(after.Construction));
        var state = GameEngine.CreateGame(result, Content);
        var occupied = state.Units.SelectMany(u => FootprintGeometry.OccupiedCells(state, u.Id)).ToHashSet();
        Assert.All(result.CreditsPiles ?? [], p => Assert.DoesNotContain(p.Position, occupied));
        Assert.True(PlayReadyScenarioSetup.Validate(result, state).Valid);
    }

    [Fact]
    public void DungeonTreasureAndRoomOwnershipProduceDistinctOpportunities()
    {
        var site = Site("test-dungeon", GeneratedSiteKind.MountainDungeon, [20 * 60 + 20, 20 * 60 + 21]);
        var treasure = GeneratedArea.From("treasure", [20 * 60 + 20], 60);
        var room = new RoomInfo("shared-room", "chamber", site.Footprint, site.Footprint, [], 0);
        site = site with
        {
            MountainDungeon = new(MountainDungeonProfile.ChamberComplex, [room], site.Footprint,
                site.Footprint, site.Footprint, GeneratedArea.From("no-lava", [], 60), [treasure], 2, 1220, 1),
            Buildings = [new("building", "dungeon", site.Footprint, [room], [], site.Access, [])],
            PopulationIntent = new("test", [new("supplies", "supplies", treasure, 1220, "test")], [], 0, 0, CampAlertTopology.Connected)
        };
        var locations = ExpeditionCreditsGenerator.Locations([site], 60, new()).ToArray();
        Assert.Equal(2, locations.Length);
        Assert.Equal([1220], Assert.Single(locations, l => l.Kind == "treasure").Cells);
        Assert.Equal([1221], Assert.Single(locations, l => l.Kind == "dungeon").Cells);
    }

    [Fact]
    public void DedicatedLocationsFavorTreasureAndValuableSitesWithSmallPilesAndRareWindfalls()
    {
        var context = Fixture();
        AddFixtureSites(context);
        Dictionary<string, int> counts = [];
        Dictionary<int, int> amounts = [];
        var settings = new ExpeditionCreditsSettings();
        for (var seed = 0; seed < 2000; seed++)
        {
            context.ExpeditionCreditsPlacements.Clear();
            context.Expedition = Plan((ulong)seed, settings);
            ExpeditionCreditsGenerator.Place(context);
            foreach (var pile in context.ExpeditionCreditsPlacements)
            {
                counts[pile.Kind] = counts.GetValueOrDefault(pile.Kind) + 1;
                amounts[pile.Amount] = amounts.GetValueOrDefault(pile.Amount) + 1;
                if (pile.Kind == "open-ground") Assert.Equal(settings.SmallAmount, pile.Amount);
                Assert.Contains(pile.Position.Y * 60 + pile.Position.X, context.Completion!.InteractivePlacementDomain());
            }
        }
        Assert.InRange(counts["treasure"], 1250, 1550);
        Assert.InRange(counts["ruin"], 650, 950);
        Assert.InRange(counts["camp"], 450, 750);
        Assert.InRange(counts["open-ground"], 130, 280);
        Assert.True(counts["treasure"] > counts["ruin"] && counts["ruin"] > counts["camp"] && counts["camp"] > counts["open-ground"]);
        Assert.True(amounts[3] > amounts[9] * 3 && amounts[9] > amounts[25] * 5);
        output.WriteLine("Piles across 2000 independent local seeds: " + JsonSerializer.Serialize(counts));
        output.WriteLine("Amounts: " + JsonSerializer.Serialize(amounts));
    }

    [Fact]
    public void CreditsCanShareLootAndDoNotConsumeFiniteLootSupply()
    {
        var context = Fixture();
        var cell = new Cell(30, 30);
        context.SiteLootMarkers.Add(new("test-shared-loot", cell));
        context.Sites.Add(Site("test-shared", GeneratedSiteKind.Ruin, [30 * 60 + 30]));
        context.Expedition = Plan(17, new(RuinChance: 100, OpenGroundChance: 0));
        var before = context.Materialize();
        ExpeditionCreditsGenerator.Place(context);
        var after = context.Materialize();
        Assert.Equal(cell, Assert.Single(after.CreditsPiles!).Position);
        Assert.Equal(ScenarioDefinitionJson.ToJson(before), ScenarioDefinitionJson.ToJson(after with { CreditsPiles = before.CreditsPiles }));
        Assert.Equal(cell, Assert.Single(after.LootMarkers!, m => m.Id == "test-shared-loot").Position);
    }

    [Fact]
    public void LegacyOrdinaryAndDisabledPoliciesLeaveBoardsAloneAndSettingsArePinned()
    {
        var profile = ExpeditionWorldProfile.CreateV1(Recipe.NaturalTerrain);
        Assert.Empty(Execute(17, profile).Materialize().CreditsPiles ?? []);
        Assert.Empty(WorldGenerator.Generate(Recipe).Definition.CreditsPiles ?? []);
        var configured = ExpeditionWorldProfile.Create(Recipe.NaturalTerrain) with { CreditsSettings = new(MaximumPiles: 0) };
        Assert.Empty(Execute(17, configured).Materialize().CreditsPiles ?? []);
        Assert.Equal(configured, JsonSerializer.Deserialize<ExpeditionWorldProfile>(JsonSerializer.Serialize(configured))!.Validate());
        Assert.Throws<ArgumentException>(() => (configured with { CreditsVersion = "future" }).Validate());
        Assert.Throws<ArgumentException>(() => new ExpeditionCreditsSettings(LargeChance: 90, MediumChance: 20).Validate());
        Assert.Throws<ArgumentException>(() => (configured with { CreditsSettings = null }).Validate());
    }

    private static WorldGenerationContext Execute(ulong seed, ExpeditionWorldProfile profile, int capacity = 5) =>
        ExpeditionRegionGeometry.Execute(Recipe with { HeroCapacity = capacity }, new(0, 30), new(59, 20), new(57, 19, 2, 3), seed, Content, profile: profile).Context;

    private static WorldGenerationContext Fixture() => ExpeditionRegionGeometry.Execute(
        Recipe with { Population = new(PopulationDensity.Off) }, new(0, 30), new(59, 20), new(57, 19, 2, 3), 17,
        Content, profile: ExpeditionWorldProfile.CreateV1(Recipe.NaturalTerrain)).Context;

    private static ExpeditionGeometryPlan Plan(ulong seed, ExpeditionCreditsSettings settings) =>
        new(Recipe.Window, 5, new(0, 30), new(59, 20), new(57, 19, 2, 3), seed) { CreditsSettings = settings };

    private static SiteInfo Site(string id, GeneratedSiteKind kind, int[] cells)
    {
        var area = GeneratedArea.From(id, cells, 60);
        return new(id, kind, area, area, [], [], new(cells.Length, cells.Length, true, false, 0), [], [], [], null, PreparationSummary.Empty);
    }

    private static void AddFixtureSites(WorldGenerationContext context)
    {
        // Deliberately authored test metadata, independent of mutable playtest scenarios.
        var treasure = Site("test-treasure", GeneratedSiteKind.MountainDungeon, [20 * 60 + 20]);
        var area = treasure.Footprint;
        context.Sites.Add(treasure with { MountainDungeon = new(MountainDungeonProfile.ChamberComplex, [],
            area, area, area, GeneratedArea.From("no-lava", [], 60), [area], 1, 20 * 60 + 20, 1) });
        context.Sites.Add(Site("test-ruin", GeneratedSiteKind.Ruin, [25 * 60 + 25]));
        context.Sites.Add(Site("test-camp", GeneratedSiteKind.Campsite, [30 * 60 + 30]));
    }
}
