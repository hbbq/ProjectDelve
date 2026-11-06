using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ThroughRouteTests
{
    internal static WorldGenerationRecipe Recipe(int width = 50, int height = 50, RouteProfileKind profile = RouteProfileKind.MinorPath) =>
        new(new(-17, -13, width, height), new(0, 0, 0), 42, null, null, new(ThroughRoutes: 2, Profile: profile, ConnectSites: false, Alternatives: 0));
    [Theory]
    [InlineData(50, 50)]
    [InlineData(120, 100)]
    [InlineData(120, 120)]
    public void ThroughRoutesCrossTheWindowWithNoSitesAndFormSharedNetwork(int width, int height)
    {
        var world = WorldGenerator.Generate(Recipe(width, height));
        Assert.Empty(world.Context.Sites); Assert.Equal(2, world.Context.Routes.Count);
        Assert.Single(world.Context.RouteNetwork!.Topology.Components); Assert.NotEmpty(world.Context.RouteNetwork.Junctions);
        Assert.NotEmpty(world.Context.RouteNetwork.SharedSegments);
        foreach (var route in world.Context.Routes)
        {
            Assert.Equal(RouteEndpointKind.Boundary, route.Start.Kind); Assert.Equal(RouteEndpointKind.Boundary, route.End.Kind);
            Assert.NotEqual(route.Start.Side, route.End.Side);
            Assert.Equal(route.Start.AnchorIndex % width - 17, route.Start.WorldX);
            var indices = route.Geometry.LocalIndices(width, world.Window).ToArray();
            Assert.Equal(route.Start.AnchorIndex, indices[0]); Assert.Equal(route.End.AnchorIndex, indices[^1]);
            Assert.All(indices, i => Assert.True(world.Context.ConstructionBoard!.Passable(i)));
        }
        Assert.Single(world.Preview.Frames[^1].ConstructionBatches);
        _ = GameEngine.CreateGame(world.Definition);
    }
    [Fact]
    public void RoutesRepeatExactlyAndAddingLaterThroughRequestDoesNotReseedEarlierRoute()
    {
        var recipe = Recipe(); var a = WorldGenerator.Generate(recipe); var b = WorldGenerator.Generate(recipe);
        Assert.Equal(a.Fingerprint, b.Fingerprint); Assert.Equal(JsonSerializer.Serialize(a.Preview), JsonSerializer.Serialize(b.Preview));
        var one = WorldGenerator.Generate(recipe with { Routes = recipe.Routes! with { ThroughRoutes = 1 } });
        Assert.Equal(JsonSerializer.Serialize(one.Context.Routes[0]), JsonSerializer.Serialize(a.Context.Routes[0]));
        var starts = Enumerable.Range(0, 8).Select(seed => WorldGenerator.Generate(recipe with { Seed = (ulong)seed }).Context.Routes[0].Start.AnchorIndex).Distinct();
        Assert.True(starts.Count() >= 5);
    }
    [Fact]
    public void SingleColumnUsesSeparatedNorthSouthBoundaryPorts()
    {
        var recipe = Recipe(1, 25) with { Routes = new(ThroughRoutes: 1, ConnectSites: false, Alternatives: 0) };
        var world = WorldGenerator.Generate(recipe); var route = Assert.Single(world.Context.Routes);
        Assert.Equal(CardinalSide.North, route.Start.Side); Assert.Equal(CardinalSide.South, route.End.Side);
        Assert.All(route.Geometry.Runs, run => Assert.Equal(CardinalSide.South, run.Direction));
        Assert.Equal(25, route.Stats.Length);
    }
    [Fact]
    public void WaterPreservationReportsUnresolvedRatherThanClaimingAnAcceptedRoute()
    {
        var world = WorldGenerator.Generate(Recipe() with { NaturalTerrain = new(0, 100, 0) });
        Assert.Empty(world.Context.Routes); Assert.All(world.Context.RouteIntents, i => Assert.Equal(RouteIntentStatus.Unresolved, i.Status));
        Assert.All(world.Context.Samples, s => Assert.Equal("water", s.TerrainTypeId)); Assert.Empty(world.Context.Construction);
    }
}
