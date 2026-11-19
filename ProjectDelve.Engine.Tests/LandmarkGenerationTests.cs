using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class LandmarkGenerationTests
{
    private static readonly CellBounds Area = new(0, 0, 24, 24);
    private static LandmarkRequest Request(SiteConstructionProfile profile = SiteConstructionProfile.IsolatedLandmark,
        SitePlacementPreferences? preferences = null) => new("landmark", Area, new(), profile, "grass-with-marker", preferences);

    [Fact]
    public void PassiveLandmarkHasOwnedBlockingCenterAndOrdinaryProtectedApproach()
    {
        var board = CampsiteGenerationTests.Board(width: 24, height: 24);
        var scope = CampsiteGenerationTests.Scope(board);
        var plan = Assert.IsType<Planned<SitePlan>>(LandmarkGenerator.Plan(Request(), scope)).Plan;
        var repeat = Assert.IsType<Planned<SitePlan>>(LandmarkGenerator.Plan(Request(), scope)).Plan;
        Assert.Equal(JsonSerializer.Serialize(plan.Site), JsonSerializer.Serialize(repeat.Site));
        Assert.Equal(plan.Patch.TerrainChanges, repeat.Patch.TerrainChanges);
        Assert.Empty(board.State.Reservations);
        Assert.Equal(GeneratedSiteKind.Landmark, plan.Site.Kind);
        Assert.Empty(plan.Site.Buildings);
        Assert.Empty(plan.Site.ConnectionRequests);
        Assert.Equal(8, plan.Site.Exterior.Area);
        Assert.True(scope.Draft.Accept(plan.Patch, out var error), error);
        Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
        Assert.All(plan.Site.Footprint.Indices(24), i => Assert.Equal(plan.Site.Id, board.State.Reservations[i]));
        Assert.All(plan.Site.Exterior.Indices(24), i => Assert.True(board.Passable(i)));
        var blocker = Assert.Single(plan.Site.Footprint.Indices(24).Where(i => !board.Passable(i)));
        Assert.Equal("grass-with-marker", board.TerrainAt(blocker));
        var portal = Assert.Single(plan.Site.Entrances);
        Assert.Equal(board.EdgeAt(portal.Edge), portal.Kind);
        Assert.True(board.Passable(portal.InsideIndex));
        Assert.True(board.Passable(portal.OutsideIndex));
        var reached = MandatoryConnectivityRepair.ActualReachable(GameEngine.CreateGame(board.Export()).Physical.Board, [portal.OutsideIndex]);
        Assert.All(plan.Site.Exterior.Indices(24), i => Assert.Contains(i, reached));
    }

    [Fact]
    public void RoadsideMustReallyAdjoinARouteAndPreservesItsCorridor()
    {
        var board = CampsiteGenerationTests.Board(width: 24, height: 24);
        var road = Enumerable.Range(0, 24).Select(y => y * 24 + 12).ToArray();
        // A test-owned existing route, with the same memberships used by route publication.
        foreach (var i in road) board.State.RouteCorridors.Add(i, new(StringComparer.Ordinal) { "road" });
        var scope = CampsiteGenerationTests.Scope(board);
        var plan = Assert.IsType<Planned<SitePlan>>(LandmarkGenerator.Plan(Request(SiteConstructionProfile.RoadsideMarker,
            new(RouteAnchors: road)), scope)).Plan;
        Assert.DoesNotContain(plan.Patch.Reservations, r => road.Contains(r.Index));
        Assert.DoesNotContain(plan.Patch.TerrainChanges, t => road.Contains(t.Index));
        var portal = Assert.Single(plan.Site.Entrances);
        Assert.Contains(portal.OutsideIndex, road);
        Assert.Single(plan.Site.AccessPorts);
        Assert.Empty(plan.Site.ConnectionRequests);
        Assert.IsType<Rejected<SitePlan>>(LandmarkGenerator.Plan(Request(SiteConstructionProfile.RoadsideMarker), scope));
    }

    [Fact]
    public void ProtectedOrBlockedSitesAndProhibitedPreparationDoNotMutateParent()
    {
        foreach (var ground in new[] { "solid-rock", "tree" })
        {
            var board = CampsiteGenerationTests.Board(ground, 24, 24);
            var scope = CampsiteGenerationTests.Scope(board);
            Assert.IsType<Rejected<SitePlan>>(LandmarkGenerator.Plan(Request() with { Preparation = new(ClearTrees: false, ExcavateRock: false) }, scope));
            Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
            Assert.Empty(scope.Draft.BuildPatch().Reservations);
        }
        var protectedBoard = CampsiteGenerationTests.Board(width: 24, height: 24);
        foreach (var i in Enumerable.Range(0, 24 * 24)) protectedBoard.State.Reservations.Add(i, "protected");
        var protectedScope = CampsiteGenerationTests.Scope(protectedBoard);
        Assert.IsType<Rejected<SitePlan>>(LandmarkGenerator.Plan(Request(), protectedScope));
        Assert.Empty(protectedScope.Draft.BuildPatch().Reservations);
        Assert.IsType<Rejected<SitePlan>>(LandmarkGenerator.Plan(Request() with { TerrainTypeId = "campfire" }, protectedScope));
    }

    [Fact]
    public void AuthoredEligibilitySurvivesCompositionAndJsonAndRequiresPassiveBlockingContent()
    {
        foreach (var id in new[] { "grass-with-marker", "gravel-with-statue", "stone-floor-with-obelisk" })
        {
            var terrain = TerrainRoster.All.Single(t => t.Id == id);
            Assert.True(LandmarkGenerator.IsLandmark(terrain));
            var roundtrip = JsonSerializer.Deserialize<TerrainDefinition>(JsonSerializer.Serialize(terrain))!;
            Assert.True(LandmarkGenerator.IsLandmark(roundtrip));
            Assert.Null(terrain.Production);
        }
        Assert.False(LandmarkGenerator.IsLandmark(TerrainRoster.All.Single(t => t.Id == "gravel-with-well")));
        var declaration = new TerrainAssemblyDefinition("invalid-landmark", "Invalid", "grass") { Landmark = true };
        Assert.Throws<ArgumentException>(() => TerrainAssemblyContent.Compose(declaration, TerrainRoster.All.Single(t => t.Id == "grass"), FixedComponentRoster.All));
    }
}
