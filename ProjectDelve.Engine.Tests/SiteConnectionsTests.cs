using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SiteConnectionsTests
{
    private static WorldGenerationRecipe Recipe(int through = 1) => ThroughRouteTests.Recipe(120, 100) with
    { Civilization = new(), Routes = new(ThroughRoutes: through == 0 ? ContentDensity.None : ContentDensity.Low, ConnectSites: true, Alternatives: ContentDensity.None) };
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PublishedAccessConnectsSitesWithoutChangingTheirStructuresOrInternalPaths(int through)
    {
        var world = WorldGenerator.Generate(Recipe(through));
        Assert.Equal(world.Context.CampsiteDensity!.Accepted + world.Context.SettlementDensity!.Accepted, world.Context.Sites.Count); Assert.NotEmpty(world.Context.Sites); Assert.True(world.Context.Routes.Any(r => r.Purpose == RoutePurpose.SiteConnection), JsonSerializer.Serialize(world.Context.PlanningFailures));
        var civil = world.Preview.Frames.Single(f => f.StepId == "civilization"); var final = world.Preview.Frames[^1];
        foreach (var site in world.Context.Sites)
        {
            Assert.NotEmpty(site.AccessPorts);
            if (site.ConnectionRequests.Count == 0)
                Assert.All(site.AccessPorts, p => Assert.True(world.Context.ConstructionBoard!.IsRouteCorridor(p.OutwardIndex)));
            foreach (var i in site.Footprint.Indices(world.Window.Width)) Assert.Equal(civil.Terrain[i], final.Terrain[i]);
            foreach (var port in site.AccessPorts)
            {
                Assert.True(site.Exterior.Contains(port.AnchorIndex, world.Window.Width));
                Assert.False(site.Footprint.Contains(port.OutwardIndex, world.Window.Width));
                Assert.Equal(site.Id, port.Permission.ExactOwnerId);
                Assert.All(port.Permission.WritableCells, i => Assert.False(site.Footprint.Contains(i, world.Window.Width)));
                Assert.All(port.Permission.TraverseCells, i => Assert.Equal(port.OwnerId, world.Context.ConstructionBoard!.State.Reservations[i]));
            }
            Assert.All(site.ConnectionRequests, request => Assert.Contains(world.Context.RouteIntents, i => i.Id == request.Id));
        }
        Assert.Equal(JsonSerializer.Serialize(civil.Edges), JsonSerializer.Serialize(final.Edges));
        Assert.NotEmpty(world.Context.RouteNetwork!.SiteAttachments);
        Assert.All(world.Context.RouteNetwork.SiteAttachments, a => { Assert.True(a.OrdinaryOpenAccess); Assert.True(a.DoorsOpenableAccess); Assert.True(a.NetworkComponent >= 0); });
        Assert.Equal(world.Context.ConstructionBoard!.Revision, world.Context.RouteNetwork.Topology.BoardRevision);
    }

    [Fact]
    public void ServerBatchesUseTheirOwnPrecedingCheckpointAndOperationRanges()
    {
        var world = WorldGenerator.Generate(Recipe()); var frames = world.Preview.Frames;
        var batches = frames[^1].ConstructionBatches; Assert.True(batches.Count >= 3);
        foreach (var batch in batches)
        {
            var frame = frames.Single(f => f.StepId == batch.StepId); var checkpoint = frames.Single(f => f.StepId == batch.CheckpointStepId);
            Assert.Equal(checkpoint.Construction.Count, batch.FirstOperation); Assert.Equal(frame.Construction.Count, batch.EndOperation);
            var terrain = checkpoint.Terrain.Select(c => world.Preview.TerrainPalette[c - 'A']).ToArray();
            var edges = checkpoint.Edges.ToDictionary(e => new EdgeKey(e.Position.Y * world.Window.Width + e.Position.X, e.Direction), e => e.Kind);
            foreach (var operation in frame.Construction.Skip(batch.FirstOperation).Take(batch.EndOperation - batch.FirstOperation))
            {
                foreach (var change in operation.TerrainChanges) { Assert.Equal(change.Before, terrain[change.Index]); terrain[change.Index] = change.After; }
                foreach (var change in operation.EdgeChanges) { Assert.Equal(change.Before, edges.GetValueOrDefault(change.Key, EdgeKind.None)); edges[change.Key] = change.After; }
            }
            Assert.Equal(frame.Terrain.Select(c => world.Preview.TerrainPalette[c - 'A']), terrain);
            Assert.All(frame.Edges, e => Assert.Equal(e.Kind, edges[new(e.Position.Y * world.Window.Width + e.Position.X, e.Direction)]));
        }
    }
    [Fact]
    public void MultipleExplicitRequestsRetainDistinctRouteAndOperationIdentities()
    {
        var world = WorldGenerator.Generate(Recipe() with { Routes = new(ConnectSites: false, Alternatives: ContentDensity.None) });
        var context = world.Context; var source = context.Sites[0]; Assert.True(source.AccessPorts.Count >= 2);
        for (var i = 0; i < context.Sites.Count; i++) context.Sites[i] = context.Sites[i] with { ConnectionRequests = [] };
        context.Sites[0] = source with { ConnectionRequests = source.AccessPorts.Take(2).Select(p => new SiteConnectionRequest(p.Id + "/required", p.Id, true)).ToArray() };
        RouteNetworkGenerator.Apply(context, new("two-requests", WorldStepKind.RouteNetwork, new RouteNetworkStepParameters(RouteNetworkMode.SiteConnections, new()), ["civilization"]), ContentBundle.Canonical());
        var branches = context.Routes.Where(r => r.Purpose == RoutePurpose.SiteConnection).ToArray();
        Assert.Equal(2, branches.Length); Assert.Equal(2, branches.Select(r => r.Id).Distinct().Count());
        Assert.Equal(context.Construction.Count, context.Construction.Select(o => o.Id).Distinct().Count());
    }

    [Fact]
    public void HubConnectionsSatisfyOnlyThePortActuallyReached()
    {
        var world = WorldGenerator.Generate(Recipe(0) with { Routes = null }); var context = world.Context;
        var hub = context.Sites[0]; var branch = context.Sites[1];
        for (var i = 0; i < context.Sites.Count; i++) context.Sites[i] = context.Sites[i] with { ConnectionRequests = [] };
        context.Sites[0] = hub with { ConnectionRequests = hub.AccessPorts.Take(2).Select(p => new SiteConnectionRequest(p.Id + "/required", p.Id, true)).ToArray() };
        context.Sites[1] = branch with { ConnectionRequests = branch.ConnectionRequests.Select(r => r with { Required = true }).ToArray() };
        RouteNetworkGenerator.Apply(context, new("hub-links", WorldStepKind.RouteNetwork, new RouteNetworkStepParameters(RouteNetworkMode.SiteConnections, new()), ["civilization"]), ContentBundle.Canonical());
        Assert.Contains(context.RouteIntents, i => i.Id == context.Sites[0].ConnectionRequests[0].Id && i.Status == RouteIntentStatus.Accepted);
        Assert.Contains(context.RouteIntents, i => i.Id == context.Sites[0].ConnectionRequests[1].Id && i.Status == RouteIntentStatus.Unresolved);
    }

    [Fact]
    public void AutomaticAccessPrefersUsableGroundOverWaterFacingPorts()
    {
        // Exercise the surface-Site publisher directly: natural generation can now
        // also place a dungeon, whose anchored access needs no surface route request.
        var footprint = Enumerable.Range(2, 3).SelectMany(y => Enumerable.Range(2, 3).Select(x => y * 7 + x)).ToArray();
        var exterior = footprint.Where(i => i != 24).ToArray();
        var groundApproach = 5 * 7 + 3;
        var board = new PlanningBoard(new ScenarioDefinition
        {
            Board = new()
            {
                Width = 7, Height = 7, DefaultTerrain = TerrainKind.Water,
                Cells = footprint.Append(groundApproach).Select(i => new CellDefinition(new(i % 7, i / 7), TerrainKind.Grass)).ToList()
            },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        }, ContentBundle.Canonical());
        var draft = board.BeginDraft(new(0, 0, 7, 7), "surface-site", new());
        var site = new SiteInfo("surface-site", GeneratedSiteKind.Campsite, GeneratedArea.From("footprint", footprint, 7),
            GeneratedArea.From("exterior", exterior, 7), [],
            [new("entrance", "surface-site", 24, 17, EdgeKey.Between(24, 17, 7), EdgeKind.None)],
            new(9, 9, true, true, 0), [], [], [], null, PreparationSummary.Empty);
        var published = SiteRouteAccess.Publish(site, draft, new(PreferredApproach: CardinalSide.North));
        Assert.Contains(published.AccessPorts, p => !draft.Passable(p.OutwardIndex));
        var request = Assert.Single(published.ConnectionRequests);
        var selected = Assert.Single(published.AccessPorts.Where(p => p.Id == request.PortId));
        Assert.Equal(groundApproach, selected.OutwardIndex);
        Assert.True(draft.Passable(selected.OutwardIndex));
    }

    [Fact]
    public void ExplicitRemoteSiteIsNotUsedAsAnIndependentNetworkHub()
    {
        var world = WorldGenerator.Generate(Recipe(0) with { Routes = null }); var context = world.Context;
        for (var i = 0; i < context.Sites.Count; i++) context.Sites[i] = context.Sites[i] with
        { ConnectionRequests = context.Sites[i].ConnectionRequests.Select(r => r with { DeliberatelyRemote = true }).ToArray() };
        RouteNetworkGenerator.Apply(context, new("connections", WorldStepKind.RouteNetwork, new RouteNetworkStepParameters(RouteNetworkMode.SiteConnections, new()), ["civilization"]), ContentBundle.Canonical());
        Assert.Empty(context.Routes); Assert.All(context.RouteIntents, i => Assert.Equal(RouteIntentStatus.DeliberatelyRemote, i.Status));
    }
}
