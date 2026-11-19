using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ExpeditionConstructionIntegrationTests
{
    [Fact]
    public void DenseMixedConstructionRetainsOrdinaryAccessAndBuildingLoot()
    {
        const ulong seed = 17;
        var plan = ExpeditionWorld.Initial(seed);
        for (var i = 1; i < 10; i++) plan = ExpeditionWorld.Next(seed, plan);
        var content = ContentBundle.Canonical();
        var settings = ExpeditionWorld.Configure(seed);
        var profile = ExpeditionWorldProfile.CreateConstructionV1(settings.NaturalTerrain);
        var recipe = ExpeditionWorld.RecipeFor(plan, settings);
        var execution = ExpeditionRegionGeometry.Execute(recipe, plan.EntrySeam, plan.ExitSeam, plan.ExitArea,
            plan.LocalSeed, content, profile: profile);
        var context = execution.Context;
        var scenario = context.Materialize();
        var state = GameEngine.CreateGame(scenario, content);
        Assert.True(PlayReadyScenarioSetup.Validate(scenario, state).Valid);
        Assert.True(context.Expedition!.HasOccupiedAccess(state, context.Deployment!));
        Assert.NotEmpty(state.Units);
        var profiles = context.Sites.Select(site => site.ConstructionProfile).ToHashSet();
        Assert.Contains(SiteConstructionProfile.Cottage, profiles);
        Assert.Contains(SiteConstructionProfile.PrimitiveHut, profiles);
        Assert.Contains(SiteConstructionProfile.VillageCenter, profiles);
        var homes = context.Sites.Where(site => site.ConstructionProfile is SiteConstructionProfile.Cottage or SiteConstructionProfile.PrimitiveHut)
            .SelectMany(site => site.Buildings).ToArray();
        var reachable = PlayReadyScenarioSetup.ReachableFromDeployment(scenario, state);
        Assert.Contains(context.SiteLootMarkers, marker => homes.Any(home =>
            marker.Id.StartsWith(home.Id + "/exploration-loot-", StringComparison.Ordinal)));
        Assert.All(context.SiteLootMarkers, marker => Assert.Contains(marker.Position.Y * 60 + marker.Position.X, reachable));

        var other = ExpeditionWorld.Next(seed, plan);
        _ = ExpeditionWorld.Generate(other, content, settings, profile);
        Assert.Equal(ScenarioDefinitionJson.ToJson(scenario),
            ScenarioDefinitionJson.ToJson(ExpeditionWorld.Generate(plan, content, settings, profile)));
    }

    [Fact]
    public void WildernessRuinAndLandmarkRemainPlayableWithInitialMonsters()
    {
        const ulong seed = 42;
        var plan = ExpeditionWorld.Initial(seed);
        for (var i = 1; i < 11; i++) plan = ExpeditionWorld.Next(seed, plan);
        var content = ContentBundle.Canonical();
        var settings = ExpeditionWorld.Configure(seed);
        var profile = ExpeditionWorldProfile.CreateConstructionV2(settings.NaturalTerrain);
        var context = ExpeditionRegionGeometry.Execute(ExpeditionWorld.RecipeFor(plan, settings),
            plan.EntrySeam, plan.ExitSeam, plan.ExitArea, plan.LocalSeed, content, profile: profile).Context;
        var scenario = context.Materialize();
        var state = GameEngine.CreateGame(scenario, content);
        Assert.NotEmpty(state.Units);
        Assert.DoesNotContain(context.Sites, site => site.Kind == GeneratedSiteKind.Settlement);
        var ruin = Assert.Single(context.Sites, site => site.Kind == GeneratedSiteKind.Ruin);
        var landmark = Assert.Single(context.Sites, site => site.Kind == GeneratedSiteKind.Landmark);
        Assert.Equal(SiteConstructionProfile.RuinedCottage, ruin.ConstructionProfile);
        Assert.Equal(SiteConstructionProfile.IsolatedLandmark, landmark.ConstructionProfile);
        Assert.True(PlayReadyScenarioSetup.Validate(scenario, state).Valid);
        Assert.True(context.Expedition!.HasOccupiedAccess(state, context.Deployment!));
        var reached = PlayReadyScenarioSetup.ReachableFromDeployment(scenario, state);
        var home = Assert.Single(ruin.Buildings);
        var reward = Assert.Single(context.SiteLootMarkers, marker =>
            marker.Id.StartsWith(home.Id + "/exploration-loot-", StringComparison.Ordinal));
        var rewardCell = reward.Position.Y * 60 + reward.Position.X;
        Assert.Contains(rewardCell, Assert.Single(home.Rooms).Usable.Indices(60));
        Assert.Contains(rewardCell, reached);
        Assert.All(landmark.Exterior.Indices(60), cell => Assert.Contains(cell, reached));
        Assert.DoesNotContain(context.SiteLootMarkers, marker => marker.Id.StartsWith(landmark.Id + "/", StringComparison.Ordinal));

        var other = ExpeditionWorld.Next(seed, plan);
        _ = ExpeditionWorld.Generate(other, content, settings, profile);
        Assert.Equal(ScenarioDefinitionJson.ToJson(scenario),
            ScenarioDefinitionJson.ToJson(ExpeditionWorld.Generate(plan, content, settings, profile)));
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(16, false)]
    public void WildernessOutpostsUseOrdinaryRoomPopulationAndExplorationLoot(int visit, bool occupied)
    {
        const ulong seed = 42;
        var plan = ExpeditionWorld.Initial(seed);
        for (var i = 1; i < visit; i++) plan = ExpeditionWorld.Next(seed, plan);
        var content = ContentBundle.Canonical();
        var settings = ExpeditionWorld.Configure(seed);
        var profile = ExpeditionWorldProfile.Create(settings.NaturalTerrain);
        var context = ExpeditionRegionGeometry.Execute(ExpeditionWorld.RecipeFor(plan, settings),
            plan.EntrySeam, plan.ExitSeam, plan.ExitArea, plan.LocalSeed, content, profile: profile).Context;
        var scenario = context.Materialize();
        var state = GameEngine.CreateGame(scenario, content);

        Assert.DoesNotContain(context.Sites, site => site.Kind == GeneratedSiteKind.Settlement);
        var outpost = Assert.Single(context.Sites, site => site.Kind == GeneratedSiteKind.FortifiedOutpost);
        var building = Assert.Single(outpost.Buildings);
        var room = Assert.Single(building.Rooms);
        var gate = Assert.Single(outpost.Entrances);
        Assert.Equal(EdgeKind.ClosedDoor, gate.Kind);
        Assert.True(PlayReadyScenarioSetup.Validate(scenario, state).Valid);
        var thresholds = ExpeditionGeometryPlan.OutpostDoorCells(context.Sites).ToArray();
        Assert.True(context.Expedition!.HasOccupiedAccess(state, context.Deployment!, thresholds));
        var occupancy = state.Units.SelectMany(unit => FootprintGeometry.OccupiedCells(state, unit.Id))
            .Select(cell => cell.Y * 60 + cell.X).ToHashSet();
        Assert.All(thresholds, cell => Assert.DoesNotContain(cell, occupancy));
        Assert.Equal(occupied, room.Usable.Indices(60).Any(occupancy.Contains));
        var population = Assert.Single(context.Population!.SiteStatistics, site => site.SourceId == building.Id);
        Assert.Equal("Building", population.Kind);
        Assert.Equal(occupied, population.AcceptedUnits > 0);
        var loot = Assert.Single(context.SiteLootMarkers, marker =>
            marker.Id.StartsWith(building.Id + "/exploration-loot-", StringComparison.Ordinal));
        Assert.Contains(loot.Position.Y * 60 + loot.Position.X, room.Usable.Indices(60));

        if (occupied)
        {
            _ = ExpeditionWorld.Generate(ExpeditionWorld.Next(seed, plan), content, settings, profile);
            Assert.Equal(ScenarioDefinitionJson.ToJson(scenario),
                ScenarioDefinitionJson.ToJson(ExpeditionWorld.Generate(plan, content, settings, profile)));
        }
    }

    [Fact]
    public void ContentBrowserKeepsVillageCenterpieceEligibilityInAuthoredDefinitions()
    {
        var entry = Assert.Single(ContentBrowserCatalog.Canonical().Entries,
            e => e.Family == "terrain" && e.Id == "gravel-with-well");
        Assert.True(entry.AuthoredDefinition!.Value.GetProperty("villageCenterpiece").GetBoolean());
    }

    [Fact]
    public void ContentBrowserKeepsLandmarkEligibilityInAuthoredDefinitions()
    {
        var entry = Assert.Single(ContentBrowserCatalog.Canonical().Entries,
            e => e.Family == "terrain" && e.Id == "gravel-with-statue");
        Assert.True(entry.AuthoredDefinition!.Value.GetProperty("landmark").GetBoolean());
    }
}
