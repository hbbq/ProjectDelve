using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class DensityScalingTests
{
    [Theory]
    [InlineData(30, 24, 1, 2)]
    [InlineData(60, 50, 3, 4)]
    [InlineData(80, 80, 3, 6)]
    [InlineData(120, 120, 5, 8)]
    public void TargetsScaleByDimensionAndDensityWithExplicitCaps(int width, int height, int lowCamps, int mediumThrough)
    {
        var window = new WorldWindow(Width: width, Height: height);
        Assert.Equal(lowCamps, DensityScaling.Campsites(window, ContentDensity.Low));
        Assert.Equal(mediumThrough, DensityScaling.ThroughRoutes(window, ContentDensity.Medium));
        int previousCamps = -1, previousSettlements = -1, previousThrough = -1, previousLinks = -1;
        foreach (var density in Enum.GetValues<ContentDensity>())
        {
            var camps = DensityScaling.Campsites(window, density);
            var settlements = DensityScaling.Settlements(window, density);
            var through = DensityScaling.ThroughRoutes(window, density);
            var links = DensityScaling.ExplorationLinks(window, density);
            Assert.InRange(camps, 0, 32); Assert.InRange(settlements, 0, 24);
            Assert.InRange(through, 0, 12); Assert.InRange(links, 0, 16);
            Assert.True(camps >= previousCamps && settlements >= previousSettlements && through >= previousThrough && links >= previousLinks);
            if (density == ContentDensity.None) Assert.Equal(0, camps + settlements + through + links);
            previousCamps = camps; previousSettlements = settlements; previousThrough = through; previousLinks = links;
        }
    }

    [Fact]
    public void ExplorationDensityDoesNotSaturateOrdinaryLargeBoardOpportunityCounts()
    {
        var window = new WorldWindow(Width: 120, Height: 120);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Enum.GetValues<ContentDensity>().Select(d => DensityScaling.ExplorationLinks(window, d)));
    }

    [Fact]
    public void EveryDensityRoundtripsAndOldAbsoluteCountsAreRejected()
    {
        foreach (var density in Enum.GetValues<ContentDensity>())
        {
            var recipe = Recipe(30, 24) with { Civilization = new(Campsites: density, Settlements: density), Routes = new(ThroughRoutes: density, Alternatives: density) };
            Assert.Equal(recipe, WorldGenerationRecipe.Parse(recipe.ToString()));
            Assert.Contains("-N" + density + "-V" + density, recipe.ToString());
        }
        Assert.Throws<FormatException>(() => WorldGenerationRecipe.Parse(Recipe(30, 24).ToString().Replace("-NLow", "-N1")));
        Assert.Throws<ArgumentException>(() => new CivilizationSettings(Campsites: (ContentDensity)99).Validate());
        Assert.Throws<ArgumentException>(() => new RouteSettings(Alternatives: (ContentDensity)99).Validate());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 30)]
    [InlineData(30, 24)]
    public void BoundedRetriesReportShortfallsAndNeverPublishPartialSites(int width, int height)
    {
        var recipe = Recipe(width, height) with { Civilization = new(Campsites: ContentDensity.VeryHigh, Settlements: ContentDensity.VeryHigh), Routes = null };
        var world = WorldGenerator.Generate(recipe);
        var frame = world.Preview.Frames[^1];
        foreach (var allocation in new[] { frame.CampsiteDensity!, frame.SettlementDensity! })
        {
            Assert.InRange(allocation.Accepted, 0, allocation.Requested);
            Assert.Equal(allocation.Requested - allocation.Accepted, allocation.Omitted);
            Assert.Equal(allocation.Requested, allocation.Attempted);
        }
        Assert.InRange(world.Context.CivilizationStats!.Attempts, 0, 512);
        Assert.InRange(world.Context.CivilizationStats.Work, 0, 2_000_000);
        Assert.Equal(frame.CampsiteDensity!.Accepted + frame.SettlementDensity!.Accepted, world.Context.Sites.Count);
        Assert.All(world.Context.Sites, site => Assert.True(site.Access.InternallyConnected));
        _ = GameEngine.CreateGame(world.Definition);
    }

    [Fact]
    public void UnsupportedBoundaryWindowDoesNotCountPreflightAsSubmittedRoute()
    {
        var world = WorldGenerator.Generate(Recipe(1, 1) with { Civilization = null });
        var allocation = world.Preview.Frames[^1].ThroughRouteDensity!;
        Assert.Equal(1, allocation.Requested); Assert.Equal(0, allocation.Attempted);
        Assert.Equal(0, allocation.Accepted); Assert.Equal(1, allocation.Omitted);
        var intent = Assert.Single(world.Context.RouteIntents);
        Assert.Equal(RouteIntentStatus.Unresolved, intent.Status);
        Assert.Contains("too short", intent.Reason);
        Assert.Empty(world.Context.Routes);
    }

    [Fact]
    public void ExplorationWithoutNetworkReportsUnattemptedOmissionReason()
    {
        var world = WorldGenerator.Generate(Recipe(30, 24) with
        {
            Civilization = null,
            Routes = new(ThroughRoutes: ContentDensity.None, ConnectSites: false, Alternatives: ContentDensity.High)
        });
        var allocation = world.Preview.Frames[^1].ExplorationLinkDensity!;
        Assert.Equal(1, allocation.Requested); Assert.Equal(0, allocation.Attempted);
        Assert.Equal(0, allocation.Accepted); Assert.Equal(1, allocation.Omitted);
        Assert.Contains("No useful exploration candidates", allocation.Reason);
        Assert.Empty(world.Context.RouteIntents);
    }

    [Fact]
    public void SeedVariesGeometryWhileTargetsRepeatAndPopulationDoesNotChangeConstruction()
    {
        var recipe = Recipe(30, 24);
        var a = WorldGenerator.Generate(recipe); var b = WorldGenerator.Generate(recipe);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(a.Preview), JsonSerializer.Serialize(b.Preview));
        var other = WorldGenerator.Generate(recipe with { Seed = 43 });
        Assert.NotEqual(a.Fingerprint, other.Fingerprint);
        Assert.Equal(a.Context.CampsiteDensity!.Requested, other.Context.CampsiteDensity!.Requested);
        var populated = WorldGenerator.Generate(recipe with { Population = new(PopulationDensity.High, 5) });
        Assert.Equal(JsonSerializer.Serialize(a.Definition.Board), JsonSerializer.Serialize(populated.Definition.Board));
        Assert.Equal(a.Context.CampsiteDensity, populated.Context.CampsiteDensity);
        Assert.Equal(a.Context.SettlementDensity, populated.Context.SettlementDensity);
        var restored = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(populated.Definition));
        Assert.Equal(ScenarioDefinitionJson.ToJson(populated.Definition), ScenarioDefinitionJson.ToJson(restored));
    }

    private static WorldGenerationRecipe Recipe(int width, int height) => new(new(Width: width, Height: height), new(0, 0, 0), 42,
        Civilization: new(), Routes: new(ConnectSites: false, Alternatives: ContentDensity.None));
}
