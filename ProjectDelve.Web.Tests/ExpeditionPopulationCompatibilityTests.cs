using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class ExpeditionPopulationCompatibilityTests
{
    [Theory]
    [InlineData(17UL, 0)]
    [InlineData(42UL, 1)]
    [InlineData(17UL, 4)]
    [InlineData(42UL, 5)]
    public void IntroPopulationKeepsPinnedGeographyAndBecomesExactlyLegacyAtRegionFour(ulong seed, int index)
    {
        var content = ContentBundle.Canonical();
        var settings = ExpeditionWorld.Configure(seed);
        var profile = ExpeditionWorldProfile.Create(settings.NaturalTerrain);
        Assert.Equal(ExpeditionWorldProfile.CurrentPopulationVersion, profile.PopulationVersion);
        var legacy = profile with { PopulationVersion = null };
        var plan = ExpeditionWorld.Initial(seed);
        for (var i = 0; i < index; i++) plan = ExpeditionWorld.Next(seed, plan);
        Assert.Equal(index, Math.Abs(plan.Region.X) + Math.Abs(plan.Region.Y));

        WorldGenerationContext Generate(ExpeditionRegionPlan region, ExpeditionWorldProfile policy) =>
            ExpeditionRegionGeometry.Execute(ExpeditionWorld.RecipeFor(region, settings),
                region.EntrySeam, region.ExitSeam, region.ExitArea, region.LocalSeed, content, profile: policy).Context;

        var current = Generate(plan, profile);
        var previous = Generate(plan, legacy);
        var scenario = current.Materialize();
        var oldScenario = previous.Materialize();
        Assert.Equal(JsonSerializer.Serialize(oldScenario.Board), JsonSerializer.Serialize(scenario.Board));
        // The final Setup also registers Loot, whose legal locations depend on occupation.
        static string Geography(ScenarioSetup setup) => JsonSerializer.Serialize(new
        {
            setup.Deployment,
            Required = setup.RequiredLocations.Where(l => !l.Id.StartsWith("interactive/loot/", StringComparison.Ordinal))
        });
        Assert.Equal(Geography(oldScenario.Setup!), Geography(scenario.Setup!));
        Assert.Equal(JsonSerializer.Serialize(oldScenario.Machines), JsonSerializer.Serialize(scenario.Machines));
        Assert.Equal(JsonSerializer.Serialize(previous.Sites), JsonSerializer.Serialize(current.Sites));
        Assert.Equal(JsonSerializer.Serialize(previous.Routes), JsonSerializer.Serialize(current.Routes));
        Assert.NotEmpty(scenario.Machines!);
        var state = GameEngine.CreateGame(scenario, content);
        Assert.True(PlayReadyScenarioSetup.Validate(scenario, state).Valid);
        Assert.True(current.Expedition!.HasOccupiedAccess(state, current.Deployment!,
            ExpeditionGeometryPlan.OutpostDoorCells(current.Sites)));
        if (index < 2)
        {
            Assert.InRange(ArrivalPoints(scenario, content), 0, index == 0 ? 2 : 3);
            Assert.NotEqual(ScenarioDefinitionJson.ToJson(oldScenario), ScenarioDefinitionJson.ToJson(scenario));
        }
        else
            Assert.Equal(ScenarioDefinitionJson.ToJson(oldScenario), ScenarioDefinitionJson.ToJson(scenario));

        // Interleaving another region must not affect generation or consume a shared stream.
        if (index == 0)
        {
            _ = Generate(ExpeditionWorld.Next(seed, plan), profile);
            Assert.Equal(ScenarioDefinitionJson.ToJson(scenario),
                ScenarioDefinitionJson.ToJson(Generate(plan, profile).Materialize()));
        }
    }

    [Fact]
    public void AlreadyLowDifficultyOneStillNeedsLocalPlacementProtection()
    {
        const ulong seed = 2;
        var settings = ExpeditionWorld.Configure(seed) with { Population = new(PopulationDensity.Low, 1) };
        var plan = ExpeditionWorld.Initial(seed);
        var content = ContentBundle.Canonical();
        var current = ExpeditionWorldProfile.Create(settings.NaturalTerrain);
        Assert.Equal(settings.Population, new ExpeditionNatureSampler(seed, current).ResolveRegion(settings).Population);
        var ordinary = ExpeditionWorld.Generate(plan, content, settings, current with { PopulationVersion = null });
        var protectedOpening = ExpeditionWorld.Generate(plan, content, settings, current);
        Assert.True(ArrivalPoints(ordinary, content) > 2);
        Assert.InRange(ArrivalPoints(protectedOpening, content), 0, 2);
        Assert.True(protectedOpening.Units.Count > 2);
    }

    private static int ArrivalPoints(ScenarioDefinition scenario, ContentBundle content)
    {
        var formation = scenario.Setup!.Deployment.PlacementOrder.Take(5).ToArray();
        var cost = 0;
        foreach (var monster in scenario.Units)
        {
            var type = content.Units.Single(t => t.Id == monster.UnitTypeId);
            var distance = FootprintGeometry.OccupiedCells(type.Footprint, monster.Anchor)
                .Min(cell => formation.Min(hero => Math.Abs(hero.X - cell.X) + Math.Abs(hero.Y - cell.Y)));
            if (distance <= Math.Max(8, type.Rng + 3)) cost += type.Rng > 1 ? 2 : 1;
        }
        return cost;
    }
}
