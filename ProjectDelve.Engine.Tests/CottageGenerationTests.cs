using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CottageGenerationTests
{
    private const int Size = 24;
    private static readonly CellBounds BoardArea = new(0, 0, Size, Size);

    [Fact]
    public void CottageOwnsOneRoomAndApronWithAUsableDoorAndOptionalRoutePort()
    {
        var board = Board();
        var draft = board.BeginDraft(BoardArea, "civilization", new());
        var request = new CottageRequest("cottage", BoardArea, new(), Furniture: true);
        var plan = Assert.IsType<Planned<SitePlan>>(CottageGenerator.Plan(request, new(17, "civilization", draft))).Plan;
        var repeat = Assert.IsType<Planned<SitePlan>>(CottageGenerator.Plan(request, new(17, "civilization", draft))).Plan;
        Assert.Equal(plan.Patch.TerrainChanges, repeat.Patch.TerrainChanges);
        Assert.Equal(plan.Patch.EdgeChanges, repeat.Patch.EdgeChanges);
        Assert.Equal(SiteConstructionProfile.Cottage, plan.Site.ConstructionProfile);
        var house = Assert.Single(plan.Site.Buildings);
        var room = Assert.Single(house.Rooms);
        var door = Assert.Single(plan.Site.Entrances);
        Assert.Equal(house.Id, door.OwnerId);
        Assert.Equal(EdgeKind.ClosedDoor, door.Kind);
        Assert.NotEmpty(plan.Site.AccessPorts);
        Assert.False(Assert.Single(plan.Site.ConnectionRequests).Required);
        Assert.Empty(board.State.Reservations);
        Assert.True(draft.Accept(plan.Patch, out var error), error);
        Assert.True(board.TryCommit(draft.BuildPatch(), out error), error);
        Assert.All(house.Footprint.Indices(Size), cell => Assert.Equal(house.Id, board.State.Reservations[cell]));
        Assert.All(plan.Site.Exterior.Indices(Size), cell =>
        {
            Assert.Equal(plan.Site.Id, board.State.Reservations[cell]);
            Assert.Equal("dirt", board.TerrainAt(cell));
        });
        var state = GameEngine.CreateGame(board.Export(), ContentBundle.Canonical());
        var physicalBoard = state.Physical.Board;
        var closed = AccessibilityAnalysis.Capture(physicalBoard);
        var inside = Position(door.InsideIndex);
        var outside = Position(door.OutsideIndex);
        Assert.False(closed.CanStep(ConstructionCapabilities.OrdinaryLand, outside, inside));
        var opened = AccessibilityAnalysis.Capture(physicalBoard with
        { Edges = physicalBoard.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() });
        Assert.True(opened.CanStep(ConstructionCapabilities.OrdinaryLand, outside, inside));
        Assert.Contains(physicalBoard.Edges, e => e.Kind == EdgeKind.Wall);
        var declarations = RequiredLocations.ForSite(plan.Site, "civilization", physicalBoard);
        Assert.Contains(declarations, location => location.SourceId == door.Id);
        Assert.Contains(declarations, location => location.SourceId == room.Id);
        var reachable = MandatoryConnectivityRepair.ActualReachable(physicalBoard, [door.OutsideIndex]);
        Assert.All(room.Usable.Indices(Size), cell => Assert.Contains(cell, reachable));
    }

    [Fact]
    public void NarrowAndProtectedPlotsCannotLeakPreparationOrReservations()
    {
        var board = Board();
        var protectedPlot = new CellBounds(6, 6, 6, 6);
        var occupied = SiteGeometry.Cells(protectedPlot, Size).ToArray();
        var protectedDraft = board.BeginDraft(BoardArea, "expedition/endpoints", new());
        protectedDraft.Reserve(occupied);
        Assert.True(board.TryCommit(protectedDraft.BuildPatch(), out var error), error);
        var draft = board.BeginDraft(BoardArea, "civilization", new());
        var scope = new GenerationScope(17, "civilization", draft);
        Assert.IsType<Rejected<SitePlan>>(CottageGenerator.Plan(new("protected", protectedPlot, new()), scope));
        var narrow = new CellBounds(0, 0, 5, 24);
        Assert.IsType<Rejected<SitePlan>>(CottageGenerator.Plan(new("narrow", narrow, new()), scope));
        var dry = new CellBounds(0, 0, 6, 6);
        Assert.IsType<Rejected<SitePlan>>(CottageGenerator.Plan(new("unprepared", dry, new(MaxChangedCells: 0)), scope));
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
