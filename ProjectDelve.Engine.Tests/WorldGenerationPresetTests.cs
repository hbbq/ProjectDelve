using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldGenerationPresetTests
{
    [Theory]
    [InlineData("Grassland", RouteProfileKind.Road, false)]
    [InlineData("Woodland", RouteProfileKind.ForestTrail, false)]
    [InlineData("LakeCountry", RouteProfileKind.MinorPath, true)]
    [InlineData("RiverValley", RouteProfileKind.MinorPath, true)]
    [InlineData("RockyWilds", RouteProfileKind.RockPassage, false)]
    public void OnlyWaterPresetsPermitCrossingsAndOtherRouteDefaultsArePreserved(
        string id, RouteProfileKind profile, bool crossings)
    {
        var preset = Assert.Single(WorldGenerationPresets.All, p => p.Id == id);
        Assert.Equal(new RouteSettings(Profile: profile, WaterCrossings: crossings), preset.Routes);
    }

    [Theory]
    [InlineData("LakeCountry")]
    [InlineData("RiverValley")]
    public void ExplicitCrossingOverrideSurvivesRecipeAndEveryRouteStage(string id)
    {
        var preset = Assert.Single(WorldGenerationPresets.All, p => p.Id == id);
        foreach (var allowed in new[] { false, true })
        {
            var recipe = new WorldGenerationRecipe(new(-7, 11, 12, 10), preset.Settings, 42,
                preset.Enrichment, preset.Civilization, preset.Routes with { WaterCrossings = allowed });
            var restored = WorldGenerationRecipe.Parse(recipe.ToString());
            Assert.Equal(recipe, restored);
            var routeSteps = WorldGenerationPlan.FromRecipe(restored).Steps.Select(s => s.Parameters)
                .OfType<RouteNetworkStepParameters>().ToArray();
            Assert.Equal(3, routeSteps.Length);
            Assert.All(routeSteps, p => Assert.Equal(allowed, p.Settings.WaterCrossings));
            Assert.True(preset.Routes.WaterCrossings);
        }
    }
}
