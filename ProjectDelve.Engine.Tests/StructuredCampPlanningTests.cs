using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class StructuredCampPlanningTests
{
    [Fact]
    public void StructuredCampPublishesPhysicalRolesAndPassableOpenPatrol()
    {
        var board = CampsiteGenerationTests.Board(); var scope = CampsiteGenerationTests.Scope(board);
        var plan = Assert.IsType<Planned<SitePlan>>(CampsiteGenerator.Plan(new("camp", scope.Draft.AllowedArea, new()), scope)).Plan;
        Assert.True(scope.Draft.Accept(plan.Patch, out var error), error);
        Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
        var intent = Assert.IsType<SitePopulationIntent>(plan.Site.PopulationIntent);
        Assert.Equal(6, intent.MinimumUnits); Assert.Equal(16, intent.MaximumUnits);
        Assert.Equal(new[] { "central", "resting", "sentry", "supplies" }, intent.Areas.Select(a => a.Role).Distinct().Order());
        Assert.Equal(2, intent.Areas.Where(a => a.Role == "resting").Select(a => a.ClusterId).Distinct().Count());
        Assert.All(intent.Areas, a =>
        {
            Assert.True(a.Area.Contains(a.AnchorIndex, board.Width));
            Assert.All(a.Area.Indices(board.Width), i => { Assert.True(board.Passable(i)); Assert.True(plan.Site.Exterior.Contains(i, board.Width)); });
        });
        Assert.InRange(plan.Site.Footprint.Indices(board.Width).Count(i => board.TerrainAt(i) == "stone-floor-with-table"), 2, 4);
        Assert.NotEmpty(intent.PatrolPath);
        Assert.NotEqual(intent.PatrolPath[0], intent.PatrolPath[^1]);
        foreach (var (a, b) in intent.PatrolPath.Zip(intent.PatrolPath.Skip(1)))
        {
            Assert.True(board.Passable(a)); Assert.True(board.Passable(b));
            Assert.True(board.EdgeAt(EdgeKey.Between(a, b, board.Width)).Passable());
        }
        var resting = intent.Areas.Where(a => a.Role == "resting").ToArray();
        Assert.All(resting[0].Area.Indices(board.Width), a => Assert.All(resting[1].Area.Indices(board.Width), b =>
            Assert.True(Math.Abs(a % board.Width - b % board.Width) + Math.Abs(a / board.Width - b / board.Width) > 3)));
        Assert.True(plan.Site.Access.InternallyConnected);
        Assert.NotEmpty(plan.Site.AccessPorts);
    }

    [Fact]
    public void SeededRoadsideFamiliesAndAlertIntentsVaryWithoutBlockingTheExistingRoad()
    {
        var layouts = new HashSet<string>(); var topologies = new HashSet<CampAlertTopology>();
        for (ulong seed = 0; seed < 16; seed++)
        {
            var board = CampsiteGenerationTests.Board();
            var road = Enumerable.Range(0, board.Width).Select(x => 12 * board.Width + x).ToArray();
            var routeDraft = board.BeginDraft(new(0, 0, board.Width, board.Height), "road", new());
            routeDraft.EnableRoutePlanning();
            routeDraft.ReserveRoute(road, "through-road");
            Assert.True(board.TryCommit(routeDraft.BuildPatch(), out var error), error);
            var scope = CampsiteGenerationTests.Scope(board, seed);
            var request = new CampsiteRequest("camp", scope.Draft.AllowedArea, new(), Preferences: new(6 * board.Width + 15, RouteAnchors: road));
            var plan = Assert.IsType<Planned<SitePlan>>(CampsiteGenerator.Plan(request, scope)).Plan;
            var intent = Assert.IsType<SitePopulationIntent>(plan.Site.PopulationIntent);
            layouts.Add(intent.Layout); topologies.Add(intent.AlertTopology);
            Assert.DoesNotContain(plan.Patch.TerrainChanges, c => road.Contains(c.Index));
            Assert.DoesNotContain(plan.Patch.Reservations, c => road.Contains(c.Index));
            Assert.True(scope.Draft.Accept(plan.Patch, out error), error);
            Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
            Assert.All(road, i => { Assert.True(board.Passable(i)); Assert.True(board.IsRouteCorridor(i)); });
            foreach (var (a, b) in road.Zip(road.Skip(1))) Assert.True(board.EdgeAt(EdgeKey.Between(a, b, board.Width)).Passable());
        }
        Assert.Equal(new[] { "clearing-centered", "roadside" }, layouts.Order());
        Assert.Equal(2, topologies.Count);
    }

    [Fact]
    public void StructuredIntentAndConstructionAreDeterministicAndPrivateUntilCommitted()
    {
        SitePlan Generate()
        {
            var board = CampsiteGenerationTests.Board("tree"); var scope = CampsiteGenerationTests.Scope(board);
            var plan = Assert.IsType<Planned<SitePlan>>(CampsiteGenerator.Plan(new("camp", scope.Draft.AllowedArea, new()), scope)).Plan;
            Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
            Assert.Empty(board.Export().Board.Cells);
            return plan;
        }
        var first = Generate(); var second = Generate();
        Assert.Equal(JsonSerializer.Serialize(first.Site), JsonSerializer.Serialize(second.Site));
        Assert.Equal(JsonSerializer.Serialize(first.Operations), JsonSerializer.Serialize(second.Operations));
    }
}
