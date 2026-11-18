using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ThroughRouteTests
{
    internal static WorldGenerationRecipe Recipe(int width = 50, int height = 50, RouteProfileKind profile = RouteProfileKind.MinorPath) =>
        new(new(-17, -13, width, height), new(0, 0, 0), 42, null, null, new(ThroughRoutes: ContentDensity.Medium, Profile: profile, ConnectSites: false, Alternatives: ContentDensity.None));
    [Theory]
    [InlineData(50, 50)]
    [InlineData(120, 100)]
    [InlineData(120, 120)]
    public void ThroughRoutesCrossTheWindowWithNoSitesAndFormSharedNetwork(int width, int height)
    {
        var world = WorldGenerator.Generate(Recipe(width, height));
        Assert.Empty(world.Context.Sites); Assert.Equal(DensityScaling.ThroughRoutes(world.Window, ContentDensity.Medium), world.Context.Routes.Count);
        Assert.Single(world.Context.RouteNetwork!.Topology.Components); Assert.NotEmpty(world.Context.RouteNetwork.Junctions);
        Assert.NotEmpty(world.Context.RouteNetwork.SharedSegments);
        foreach (var route in world.Context.Routes)
        {
            Assert.Equal(2, route.RealizedWidth);
            Assert.Equal(2, route.VerifiedFootprint);
            Assert.Equal(RouteEndpointKind.Boundary, route.Start.Kind); Assert.Equal(RouteEndpointKind.Boundary, route.End.Kind);
            Assert.NotEqual(route.Start.Side, route.End.Side);
            Assert.Equal(route.Start.AnchorIndex % width - 17, route.Start.WorldX);
            var indices = route.Geometry.LocalIndices(width, world.Window).ToArray();
            Assert.Equal(route.Start.AnchorIndex, indices[0]); Assert.Equal(route.End.AnchorIndex, indices[^1]);
            Assert.All(indices, i => Assert.True(world.Context.ConstructionBoard!.Passable(i)));
            Assert.DoesNotContain(world.Context.Deployment!.Area.Indices(width).Append(world.Context.Deployment.DepartureIndex),
                route.Corridor.Indices(width).Contains);
        }
        Assert.Equal("Valid", PlayReadyScenarioSetup.Validate(world.Definition).Status);
        var routeBatch = Assert.Single(world.Preview.Frames[^1].ConstructionBatches, b => b.StepId == "primary-routes");
        Assert.Equal("hero-deployment", routeBatch.CheckpointStepId);
        Assert.Equal(world.Preview.Frames.Single(f => f.StepId == "hero-deployment").Construction.Count, routeBatch.FirstOperation);
        Assert.Equal(world.Preview.Frames.Single(f => f.StepId == "primary-routes").Construction.Count, routeBatch.EndOperation);
        _ = GameEngine.CreateGame(world.Definition);
    }
    [Fact]
    public void RoutesRepeatExactlyAndAddingLaterThroughRequestDoesNotReseedEarlierRoute()
    {
        var recipe = Recipe(); var a = WorldGenerator.Generate(recipe); var b = WorldGenerator.Generate(recipe);
        Assert.Equal(a.Fingerprint, b.Fingerprint); Assert.Equal(JsonSerializer.Serialize(a.Preview), JsonSerializer.Serialize(b.Preview));
        var one = WorldGenerator.Generate(recipe with { Routes = recipe.Routes! with { ThroughRoutes = ContentDensity.Low } });
        Assert.Equal(JsonSerializer.Serialize(one.Context.Routes[0]), JsonSerializer.Serialize(a.Context.Routes[0]));
        var starts = Enumerable.Range(0, 8).Select(seed => WorldGenerator.Generate(recipe with { Seed = (ulong)seed }).Context.Routes[0].Start.AnchorIndex).Distinct();
        Assert.True(starts.Count() >= 5);
    }
    [Fact]
    public void SingleColumnUsesSeparatedNorthSouthBoundaryPorts()
    {
        var recipe = Recipe(1, 25) with { Routes = new(ThroughRoutes: ContentDensity.Low, ConnectSites: false, Alternatives: ContentDensity.None) };
        var context = new WorldGenerationContext(recipe.World, recipe.Window); context.SampleNaturalTerrain();
        context.OriginalAnalysis = context.CurrentAnalysis = TerrainAnalyzer.Analyze(context.Window, context.Samples);
        RouteNetworkGenerator.Apply(context, new("routes", WorldStepKind.RouteNetwork,
            new RouteNetworkStepParameters(RouteNetworkMode.PrimaryRoutes, recipe.Routes!), ["formations"]), ContentBundle.Canonical());
        var route = Assert.Single(context.Routes);
        Assert.Equal(CardinalSide.North, route.Start.Side); Assert.Equal(CardinalSide.South, route.End.Side);
        Assert.All(route.Geometry.Runs, run => Assert.Equal(CardinalSide.South, run.Direction));
        Assert.Equal(25, route.Stats.Length);
        Assert.Equal(1, route.RealizedWidth);
        Assert.Contains("Width-two preference skipped", Assert.Single(context.RouteIntents).Reason);
    }
    [Fact]
    public void WaterPreservationReportsUnresolvedRatherThanClaimingAnAcceptedRoute()
    {
        var world = WorldGenerator.Generate(Recipe() with { NaturalTerrain = new(0, 100, 0) });
        Assert.Empty(world.Context.Routes); Assert.All(world.Context.RouteIntents, i => Assert.Equal(RouteIntentStatus.Unresolved, i.Status));
        Assert.All(world.Context.OriginalSamples, s => Assert.Equal("water", s.TerrainTypeId));
        Assert.NotNull(world.Context.Deployment);
        Assert.True(world.Context.MandatoryRepair!.Success);
        Assert.Equal("Valid", PlayReadyScenarioSetup.Validate(world.Definition).Status);
    }
}
