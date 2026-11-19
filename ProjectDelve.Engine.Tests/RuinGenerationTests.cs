using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RuinGenerationTests
{
    private const int Size = 30;
    private static readonly CellBounds Area = new(0, 0, Size, Size);

    [Theory]
    [InlineData(SiteConstructionProfile.RuinedCottage)]
    [InlineData(SiteConstructionProfile.CollapsedBuilding)]
    [InlineData(SiteConstructionProfile.RuinedHamlet)]
    public void RuinsPublishConnectedInitialGapsRemainingWallsAndOwnedApproaches(SiteConstructionProfile profile)
    {
        var board = Board();
        var draft = board.BeginDraft(Area, "civilization", new());
        var request = new RuinRequest("ruin", Area, new(), profile);
        var plan = Assert.IsType<Planned<SitePlan>>(RuinGenerator.Plan(request, new(17, "civilization", draft))).Plan;
        var repeat = Assert.IsType<Planned<SitePlan>>(RuinGenerator.Plan(request, new(17, "civilization", draft))).Plan;
        Assert.Equal(plan.Patch.TerrainChanges, repeat.Patch.TerrainChanges);
        Assert.Equal(plan.Patch.EdgeChanges, repeat.Patch.EdgeChanges);
        Assert.Equal(plan.Patch.Reservations, repeat.Patch.Reservations);
        Assert.Equal(GeneratedSiteKind.Ruin, plan.Site.Kind);
        Assert.Equal(profile, plan.Site.ConstructionProfile);
        if (profile == SiteConstructionProfile.RuinedHamlet) Assert.InRange(plan.Site.Buildings.Count, 2, 3);
        else Assert.Single(plan.Site.Buildings);
        Assert.True(plan.Site.Access.InternallyConnected);
        Assert.Equal(plan.Site.Access.UsableCells, plan.Site.Access.ConnectedCells);
        Assert.NotEmpty(plan.Site.AccessPorts);
        Assert.Empty(board.State.Reservations);
        Assert.Empty(draft.BuildPatch().TerrainChanges);
        Assert.True(draft.Accept(plan.Patch, out var error), error);
        Assert.True(board.TryCommit(draft.BuildPatch(), out error), error);
        var state = GameEngine.CreateGame(board.Export(), ContentBundle.Canonical());
        var geometry = AccessibilityAnalysis.Capture(state.Physical.Board);
        Assert.Contains(state.Physical.Board.Edges, edge => edge.Kind == EdgeKind.Wall);
        var exterior = plan.Site.Exterior.Indices(Size).ToHashSet();
        Assert.Contains(exterior, cell => board.TerrainAt(cell) == "grass");
        Assert.Contains(exterior, cell => board.TerrainAt(cell) == "gravel");
        Assert.All(exterior, cell => Assert.Equal(plan.Site.Id, board.State.Reservations[cell]));
        foreach (var building in plan.Site.Buildings)
        {
            Assert.All(building.Footprint.Indices(Size), cell =>
            {
                Assert.DoesNotContain(cell, exterior);
                Assert.Equal(building.Id, board.State.Reservations[cell]);
            });
            var gaps = building.Portals.Where(portal => exterior.Contains(portal.OutsideIndex)).ToArray();
            Assert.Equal(3, gaps.Length);
            Assert.DoesNotContain(plan.Operations.SelectMany(operation => operation.EdgeChanges),
                change => gaps.Any(gap => gap.Edge == change.Key) && change.After != EdgeKind.None);
            Assert.All(gaps, gap =>
            {
                Assert.Equal(EdgeKind.None, gap.Kind);
                Assert.Equal(EdgeKind.None, board.EdgeAt(gap.Edge));
                Assert.Equal(building.Id, gap.OwnerId);
                Assert.True(geometry.CanStep(ConstructionCapabilities.OrdinaryLand, Position(gap.OutsideIndex), Position(gap.InsideIndex)));
                Assert.Contains(building.Rooms, room => room.PortalIds.Contains(gap.Id));
            });
        }
        var root = plan.Site.Entrances[0].OutsideIndex;
        var reachable = MandatoryConnectivityRepair.ActualReachable(state.Physical.Board, [root]);
        Assert.All(plan.Site.Footprint.Indices(Size).Where(board.Passable), cell => Assert.Contains(cell, reachable));
        var declarations = RequiredLocations.ForSite(plan.Site, "civilization", state.Physical.Board);
        Assert.All(plan.Site.Buildings.SelectMany(building => building.Rooms), room =>
            Assert.Contains(declarations, location => location.SourceId == room.Id));
        if (profile == SiteConstructionProfile.CollapsedBuilding) Assert.Equal(2, Assert.Single(plan.Site.Buildings).Rooms.Count);
        if (profile == SiteConstructionProfile.RuinedHamlet)
        {
            Assert.InRange(plan.Site.Buildings.Count, 2, 3);
            Assert.True(plan.Site.Footprint.Area < plan.Site.Footprint.Bounds.Width * plan.Site.Footprint.Bounds.Height);
        }
    }

    [Fact]
    public void AnOptionalFurnishingRemnantKeepsTheCottageRoomUsable()
    {
        var board = Board();
        var draft = board.BeginDraft(Area, "civilization", new());
        var seed = Enumerable.Range(0, 16).Select(seed => (ulong)seed).First(seed =>
            new GenerationScope(seed, "civilization", draft).Child("ruin", draft).Random("furnishing-remnant").Next(3) == 0);
        var plan = Assert.IsType<Planned<SitePlan>>(RuinGenerator.Plan(new("ruin", Area, new(), SiteConstructionProfile.RuinedCottage),
            new(seed, "civilization", draft))).Plan;
        var furnishing = Assert.Single(plan.Patch.TerrainChanges, change => change.After == "stone-floor-with-table");
        var room = Assert.Single(Assert.Single(plan.Site.Buildings).Rooms);
        Assert.Contains(furnishing.Index, room.Interior.Indices(Size));
        Assert.DoesNotContain(furnishing.Index, room.Usable.Indices(Size));
        Assert.True(plan.Site.Access.InternallyConnected);
    }

    [Fact]
    public void FailedRuinsLeaveNoPartialPreparationOrOwnership()
    {
        var board = Board();
        var occupiedArea = new CellBounds(5, 5, 7, 7);
        var occupied = SiteGeometry.Cells(occupiedArea, Size).ToArray();
        var protection = board.BeginDraft(Area, "expedition/endpoints", new());
        protection.Reserve(occupied);
        Assert.True(board.TryCommit(protection.BuildPatch(), out var error), error);
        var draft = board.BeginDraft(Area, "civilization", new());
        var scope = new GenerationScope(17, "civilization", draft);
        Assert.IsType<Rejected<SitePlan>>(RuinGenerator.Plan(new("occupied", occupiedArea, new(), SiteConstructionProfile.RuinedCottage), scope));
        Assert.IsType<Rejected<SitePlan>>(RuinGenerator.Plan(new("narrow", new(0, 0, 5, 5), new(), SiteConstructionProfile.RuinedCottage), scope));
        Assert.IsType<Rejected<SitePlan>>(RuinGenerator.Plan(new("unprepared", Area, new(MaxChangedCells: 0), SiteConstructionProfile.RuinedHamlet), scope));
        Assert.Empty(draft.BuildPatch().TerrainChanges);
        Assert.Empty(draft.BuildPatch().EdgeChanges);
        Assert.Empty(draft.BuildPatch().Reservations);
        Assert.All(occupied, cell => Assert.Equal("expedition/endpoints", board.State.Reservations[cell]));
    }

    private static Cell Position(int index) => new(index % Size, index / Size);
    private static PlanningBoard Board() => new(new ScenarioDefinition
    {
        Board = new() { Width = Size, Height = Size, DefaultTerrain = TerrainKind.Grass },
        UnitTypeIds = [], Units = [], Agency = []
    }, ContentBundle.Canonical());
}
