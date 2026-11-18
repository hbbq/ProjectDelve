using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SettlementLayoutTests
{
    [Fact]
    public void ClearanceRankingPrefersRoomBetweenBuildingsWithoutRequiringIt()
    {
        var board = CampsiteGenerationTests.Board(width: 32, height: 24);
        var draft = CampsiteGenerationTests.Scope(board).Draft;
        SettlementLayout Layout(int otherX) => ClearanceLayout(board.Width,
            new(5, 5, 6, 6), new(otherX, 5, 6, 6));
        var close = Layout(11); // Legal, distinct aprons with two Cells between shells.
        var spacious = Layout(15);
        Assert.True(SettlementLayouts.ClearanceCost(close, draft) >
            SettlementLayouts.ClearanceCost(spacious, draft));
        Assert.Empty(draft.BuildPatch().Reservations);
        Assert.Empty(draft.BuildPatch().TerrainChanges);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("solid-rock")]
    [InlineData("water")]
    public void ClearanceRankingAccountsForUntouchedNaturalObstaclesAndPreparedAprons(string terrain)
    {
        var board = CampsiteGenerationTests.Board(width: 32, height: 24);
        var draft = CampsiteGenerationTests.Scope(board).Draft;
        var layout = ClearanceLayout(board.Width, new CellBounds(8, 6, 6, 6));
        var original = SettlementLayouts.ClearanceCost(layout, draft);
        // Outside the apron: the original layout remains legal, but a wider gap is preferable.
        draft.SetTerrain(8 * board.Width + 7, terrain);
        Assert.True(SettlementLayouts.ClearanceCost(layout, draft) > original);
        draft.ReplaceTerrain(8 * board.Width + 7, "grass");
        // Preparation inside the declared apron already supplies clearance.
        draft.SetTerrain(8 * board.Width + 8, terrain);
        Assert.Equal(original, SettlementLayouts.ClearanceCost(layout, draft));
    }

    [Fact]
    public void ClearanceRankingPrefersOpenApproachesAndRespectsProtectedDoorTransit()
    {
        var board = CampsiteGenerationTests.Board(width: 32, height: 24);
        var draft = CampsiteGenerationTests.Scope(board).Draft;
        var approach = Enumerable.Range(14, 5).Select(x => 8 * board.Width + x).ToArray();
        var layout = ClearanceLayout(board.Width, new CellBounds(8, 6, 6, 6)) with { EntryPath = approach };
        var original = SettlementLayouts.ClearanceCost(layout, draft);
        var protectedCell = 7 * board.Width + 18;
        draft.SetEdge(new(protectedCell, EdgeDirection.Right), EdgeKind.ClosedDoor);
        draft.Reserve([protectedCell]);
        Assert.Equal(original, SettlementLayouts.ClearanceCost(layout, draft));
        draft.ReplaceEdge(new(protectedCell, EdgeDirection.Right), EdgeKind.Wall);
        Assert.True(SettlementLayouts.ClearanceCost(layout, draft) > original);
        Assert.Equal(EdgeKind.Wall, draft.EdgeAt(new(protectedCell, EdgeDirection.Right)));
        Assert.True(draft.IsReserved(protectedCell));
    }

    private static SettlementLayout ClearanceLayout(int width, params CellBounds[] bounds) => new(
        SettlementFamily.Farmstead,
        bounds.Select((b, i) => new SettlementLot("lot-" + i, "house", b, CardinalSide.South,
            (b.Y + b.Height - 1) * width + b.X + 2, 1, 1, [])).ToArray(),
        new("yard", "yard", [18 * width + 20]), [], [], [], new(0, 0, 32, 24), 0);

    [Fact]
    public void SettlementSearchMovesAwayFromNaturalPinchesWhileRetainingBuildingsAndAccess()
    {
        var board = CampsiteGenerationTests.Board(width: 64, height: 52);
        var original = Plan(CampsiteGenerationTests.Scope(board), new(0, 0, 64, 52), SettlementFamily.Farmstead, 4);
        var footprint = original.Site.Footprint.Indices(board.Width).ToHashSet();
        var obstacles = footprint.SelectMany(board.Neighbors).Where(i => !footprint.Contains(i)).Distinct().ToArray();
        var terrain = board.BeginDraft(new(0, 0, 64, 52), "natural-pinches", new());
        foreach (var cell in obstacles) terrain.SetTerrain(cell, "tree");
        Assert.True(board.TryCommit(terrain.BuildPatch(), out var error), error);

        var scope = CampsiteGenerationTests.Scope(board);
        var plan = Plan(scope, scope.Draft.AllowedArea, SettlementFamily.Farmstead, 4);
        Assert.Equal(4, plan.Site.Buildings.Count);
        Assert.NotEqual(original.Site.Footprint.Bounds, plan.Site.Footprint.Bounds);
        Assert.DoesNotContain(plan.Patch.TerrainChanges, change => change.Before == "tree");
        VerifyAccess(board, Commit(board, scope, plan));
        Assert.All(obstacles, cell => Assert.Equal("tree", board.TerrainAt(cell)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NarrowBuildingApronGetsAPassingSpaceOnlyOnWritableNaturalGround(bool protectedShoulder, bool foreignContact)
    {
        var board = CampsiteGenerationTests.Board(width: 12, height: 8);
        var area = new CellBounds(3, 2, 4, 3);
        var natural = board.BeginDraft(area, "existing-ground", new());
        int[] trees = [3 * 12 + 3, 3 * 12 + 4, 3 * 12 + 5, 3 * 12 + 6];
        foreach (var i in trees) natural.SetTerrain(i, "tree");
        if (protectedShoulder) natural.Reserve(trees);
        if (foreignContact) natural.Reserve(trees.Select(i => i - 12));
        Assert.True(board.TryCommit(natural.BuildPatch(), out var error), error);
        var draft = board.BeginDraft(area, "settlement", new());
        HashSet<int> apron = [4 * 12 + 4, 4 * 12 + 5];
        var skipped = SettlementGenerator.PreparePassingSpaces(draft, apron, [], apron.Order(),
            new(MaxChangedCells: 2, MaxCost: 4), PreparationSummary.Empty);
        var patch = draft.BuildPatch();
        Assert.Equal(protectedShoulder || foreignContact, skipped);
        Assert.Equal(protectedShoulder || foreignContact ? 0 : 2, patch.TerrainChanges.Count);
        Assert.All(patch.TerrainChanges, change => Assert.Contains(change.Index, trees));
        Assert.Empty(patch.EdgeChanges);
        Assert.Empty(patch.Reservations);
    }

    [Theory]
    [InlineData(SettlementFamily.MarketVillage, "market-square", "market-hall", 2)]
    [InlineData(SettlementFamily.Farmstead, "farmyard", "farmhouse", 2)]
    [InlineData(SettlementFamily.InnCompound, "service-yard", "inn", 3)]
    public void SharedSpaceFamiliesBuildTheirOwnLotsAndLeavePopulationRoom(
        SettlementFamily family, string spaceRole, string primaryPurpose, int primaryRooms)
    {
        var board = CampsiteGenerationTests.Board(width: 40, height: 36);
        var scope = CampsiteGenerationTests.Scope(board);
        var area = new CellBounds(2, 3, 36, 30);
        var site = Commit(board, scope, Plan(scope, area, family, buildingCount: 4));
        var layout = Assert.IsType<SettlementLayoutInfo>(site.SettlementLayout);
        Assert.Equal(family, layout.Family);
        Assert.Empty(layout.FallbackReasons);
        Assert.Equal(4, layout.Lots.Count);
        var space = Assert.Single(layout.PublicSpaces);
        Assert.Equal(spaceRole, space.Role);
        Assert.True(space.Area.Area >= 20);
        Assert.NotEmpty(space.Area.Indices(board.Width).Except(layout.Access.Indices(board.Width)));
        var primary = Assert.Single(site.Buildings.Where(b => b.Purpose == primaryPurpose));
        Assert.InRange(primary.Rooms.Count, primaryRooms, 3);
        if (family is SettlementFamily.Farmstead or SettlementFamily.InnCompound)
        {
            Assert.All(site.Buildings.Where(b => b.Id != primary.Id), b => Assert.True(b.Footprint.Area < primary.Footprint.Area));
            Assert.Contains(site.Buildings, b => b.Rooms.Count == 1);
        }

        Assert.All(layout.Lots, lot =>
        {
            var building = Assert.Single(site.Buildings.Where(b => b.Id == lot.BuildingId));
            Assert.Equal(lot.Purpose, building.Purpose);
            Assert.Equal(lot.Bounds.Width - 2, building.Footprint.Bounds.Width);
            Assert.Equal(lot.Bounds.Height - 2, building.Footprint.Bounds.Height);
        });
        Assert.All(site.Footprint.Indices(board.Width), cell => AssertInBounds(cell, area, board.Width));
        var intent = Assert.IsType<SitePopulationIntent>(site.PopulationIntent);
        Assert.Equal(new[] { "central", "resting", "sentry", "supplies" }, intent.Areas.Select(a => a.Role).Distinct().Order());
        Assert.All(intent.Areas, a =>
        {
            Assert.Contains(a.AnchorIndex, a.Area.Indices(board.Width));
            Assert.All(a.Area.Indices(board.Width), cell =>
            {
                Assert.True(board.Passable(cell));
                Assert.True(site.Footprint.Contains(cell, board.Width));
                Assert.False(layout.Access.Contains(cell, board.Width));
            });
        });
        VerifyAccess(board, site);
    }

    [Theory]
    [InlineData("horizontal")]
    [InlineData("vertical")]
    [InlineData("bend")]
    [InlineData("fork")]
    public void RoadsideFrontageFollowsProtectedRouteGeometry(string shape)
    {
        var board = CampsiteGenerationTests.Board(width: 46, height: 42);
        var width = board.Width;
        var area = new CellBounds(0, 0, board.Width, board.Height);
        int[] Horizontal(int y, int from, int to) => Enumerable.Range(from, to - from + 1).Select(x => y * width + x).ToArray();
        int[] Vertical(int x, int from, int to) => Enumerable.Range(from, to - from + 1).Select(y => y * width + x).ToArray();
        var road = (shape switch
        {
            "horizontal" => Horizontal(20, 3, 42),
            "vertical" => Vertical(22, 2, 39),
            "bend" => Horizontal(20, 3, 24).Concat(Vertical(24, 20, 39)),
            _ => Horizontal(20, 3, 42).Concat(Vertical(24, 20, 39))
        }).ToHashSet();
        var route = board.BeginDraft(area, "existing-road", new());
        route.EnableRoutePlanning();
        foreach (var cell in road) route.SetTerrain(cell, "dirt");
        route.ReserveRoute(road, "existing-road");
        Assert.True(board.TryCommit(route.BuildPatch(), out var error), error);

        var scope = CampsiteGenerationTests.Scope(board);
        var plan = Plan(scope, area, SettlementFamily.RoadsideHamlet, 4, new(RouteAnchors: road.Order().ToArray()));
        Assert.DoesNotContain(plan.Patch.TerrainChanges, c => road.Contains(c.Index));
        Assert.DoesNotContain(plan.Patch.Reservations, c => road.Contains(c.Index));
        Assert.DoesNotContain(plan.Patch.EdgeChanges, c => road.Contains(c.Key.Index) ||
            road.Contains(c.Key.Index + (c.Key.Direction == EdgeDirection.Right ? 1 : width)));
        Assert.Empty(plan.Patch.RouteMemberships);

        var site = Commit(board, scope, plan);
        var layout = Assert.IsType<SettlementLayoutInfo>(site.SettlementLayout);
        Assert.Equal(SettlementFamily.RoadsideHamlet, layout.Family);
        Assert.Equal("roadside-green", Assert.Single(layout.PublicSpaces).Role);
        Assert.Equal(4, site.Entrances.Count);
        Assert.Equal(4, site.AccessPorts.Count);
        Assert.Empty(site.ConnectionRequests);
        Assert.False(site.Access.RequiresExternalConnection);
        Assert.All(site.Entrances, portal =>
        {
            Assert.Contains(portal.OutsideIndex, road);
            Assert.True(site.Exterior.Contains(portal.InsideIndex, width));
            Assert.Contains(site.AccessPorts, port => port.AnchorIndex == portal.InsideIndex && port.OutwardIndex == portal.OutsideIndex);
            Assert.True(layout.Access.Contains(portal.OutsideIndex, width));
        });
        if (shape == "horizontal")
        {
            Assert.Contains(site.Entrances, portal => portal.InsideIndex < portal.OutsideIndex);
            Assert.Contains(site.Entrances, portal => portal.InsideIndex > portal.OutsideIndex);
        }
        if (shape == "vertical")
        {
            Assert.Contains(site.Entrances, portal => portal.InsideIndex % width < portal.OutsideIndex % width);
            Assert.Contains(site.Entrances, portal => portal.InsideIndex % width > portal.OutsideIndex % width);
        }
        Assert.All(road, cell =>
        {
            Assert.Equal("dirt", board.TerrainAt(cell));
            Assert.Equal("existing-road", Assert.Single(board.RouteMemberships(cell)));
            Assert.False(site.Footprint.Contains(cell, width));
            Assert.False(site.Exterior.Contains(cell, width));
        });
        VerifyAccess(board, site);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstrainedFrontageUsesConnectedArmsAtABendOrFork(bool fork)
    {
        var board = CampsiteGenerationTests.Board("tree", width: 46, height: 42);
        var width = board.Width;
        var area = new CellBounds(0, 0, board.Width, board.Height);
        var junction = 20 * width + 24;
        var road = Enumerable.Range(3, fork ? 40 : 22).Select(x => 20 * width + x)
            .Concat(Enumerable.Range(21, 19).Select(y => y * width + 24)).ToArray();
        var route = board.BeginDraft(area, "existing-road", new());
        route.EnableRoutePlanning();
        foreach (var cell in road) route.SetTerrain(cell, "dirt");
        route.ReserveRoute(road, "existing-road");
        List<CellBounds> pockets = [new(8, 9, 10, 11), new(25, 29, 11, 10)];
        if (fork) pockets.Add(new(31, 9, 10, 11));
        foreach (var pocket in pockets)
        foreach (var y in Enumerable.Range(pocket.Y, pocket.Height))
        foreach (var x in Enumerable.Range(pocket.X, pocket.Width))
            route.SetTerrain(y * width + x, "grass");
        Assert.True(board.TryCommit(route.BuildPatch(), out var error), error);

        var scope = CampsiteGenerationTests.Scope(board);
        // Actual board route membership must supply the arms even when preferences omit them.
        var request = new SettlementRequest("frontage", area, new(ClearTrees: false),
            new(BuildingCount: pockets.Count, Family: SettlementFamily.RoadsideHamlet),
            fork ? new(RouteAnchors: [junction]) : null);
        var plan = Assert.IsType<Planned<SitePlan>>(SettlementGenerator.Plan(request, scope)).Plan;
        var site = Commit(board, scope, plan);
        var access = site.SettlementLayout!.Access.Indices(width).ToHashSet();
        Assert.Contains(junction, access);
        Assert.Contains(junction - 1, access);
        Assert.Contains(junction + width, access);
        if (fork) Assert.Contains(junction + 1, access);
        Assert.Equal(pockets.Count, site.Entrances.Count);
        Assert.All(pockets, pocket => Assert.Contains(site.Entrances, portal =>
            portal.InsideIndex % width >= pocket.X && portal.InsideIndex % width < pocket.X + pocket.Width &&
            portal.InsideIndex / width >= pocket.Y && portal.InsideIndex / width < pocket.Y + pocket.Height));
        Assert.All(plan.Patch.TerrainChanges, change => Assert.Equal("grass", change.Before));
        VerifyAccess(board, site);
    }

    [Fact]
    public void FarmhouseRoomCountsVaryWithoutOutbuildingsOutgrowingIt()
    {
        HashSet<int> roomCounts = [];
        for (ulong seed = 0; seed < 10; seed++)
        {
            var board = CampsiteGenerationTests.Board(width: 40, height: 36);
            var scope = CampsiteGenerationTests.Scope(board, seed);
            var plan = Plan(scope, scope.Draft.AllowedArea, SettlementFamily.Farmstead, 4);
            var farmhouse = Assert.Single(plan.Site.Buildings.Where(b => b.Purpose == "farmhouse"));
            roomCounts.Add(farmhouse.Rooms.Count);
            Assert.All(plan.Site.Buildings.Where(b => b.Id != farmhouse.Id),
                outbuilding => Assert.True(outbuilding.Footprint.Area < farmhouse.Footprint.Area));
        }
        Assert.Equal(new[] { 2, 3 }, roomCounts.Order());
    }

    [Theory]
    [InlineData("rail")]
    [InlineData("marked-ground")]
    public void PassableFixedTerrainDoesNotBecomeAConstructionSurface(string type)
    {
        var scenario = CampsiteGenerationTests.Board(width: 30, height: 30).Export();
        scenario = scenario with
        {
            FixedComponentTypes = [new("marker", "Fixture Marker", false, false)],
            TerrainAssemblies = [new("marked-ground", "Marked Ground", "grass") { Components = [new("mark", "marker")] }]
        };
        for (var y = 0; y < scenario.Board.Height; y++)
        for (var x = 0; x < scenario.Board.Width; x++)
            scenario.Board.Cells.Add(type == "rail"
                ? new(new(x, y), TerrainKind.Rail, RailDirection.East)
                : new(new(x, y), TerrainKind.StoneFloor) { TerrainTypeId = type });
        var board = new PlanningBoard(scenario, ContentBundle.Canonical());
        var scope = CampsiteGenerationTests.Scope(board);
        Assert.True(board.Passable(0));
        Assert.Equal(type, board.TerrainAt(0));

        var result = Assert.IsType<Rejected<SitePlan>>(SettlementGenerator.Plan(new("fixed", scope.Draft.AllowedArea, new(),
            new(Family: SettlementFamily.Farmstead)), scope));
        Assert.Equal(PlacementFailureCode.NoFootprint, result.Failure.Code);
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
        Assert.Empty(scope.Draft.BuildPatch().Reservations);
    }

    [Fact]
    public void SmallAreaRequiresExplicitReductionAndRecordsWhy()
    {
        var board = CampsiteGenerationTests.Board(width: 26, height: 24);
        var scope = CampsiteGenerationTests.Scope(board);
        var area = new CellBounds(4, 4, 14, 14);
        var request = new SettlementRequest("farm", area, new(), new(BuildingCount: 4, Family: SettlementFamily.Farmstead));
        Assert.IsType<Rejected<SitePlan>>(SettlementGenerator.Plan(request, scope));
        Assert.Empty(scope.Draft.BuildPatch().Reservations);

        var result = Assert.IsType<Planned<SitePlan>>(SettlementGenerator.Plan(request with
        {
            Requirements = request.Requirements! with { AllowReducedBuildingCount = true }
        }, scope));
        Assert.InRange(result.Plan.Site.Buildings.Count, 2, 3);
        Assert.Contains(result.Plan.Site.SettlementLayout!.FallbackReasons, reason => reason.Contains("Reduced from 4"));
        Assert.All(result.Plan.Patch.Reservations, reservation => AssertInBounds(reservation.Index, area, board.Width));
        VerifyAccess(board, Commit(board, scope, result.Plan));
    }

    [Fact]
    public void GeometrySearchHonorsSharedWorkAllowance()
    {
        var board = CampsiteGenerationTests.Board();
        var allowance = board.Width * board.Height + 1;
        var scope = CampsiteGenerationTests.Scope(board, budget: new PlanningBudget(maxAttempts: 100, maxWork: allowance));
        var result = Assert.IsType<Rejected<SitePlan>>(SettlementGenerator.Plan(new("bounded", scope.Draft.AllowedArea, new()), scope));
        Assert.Equal(PlacementFailureCode.BudgetExhausted, result.Failure.Code);
        Assert.Equal(allowance, scope.Budget.Work);
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
    }

    private static SitePlan Plan(GenerationScope scope, CellBounds area, SettlementFamily family, int buildingCount,
        SitePlacementPreferences? preferences = null) =>
        Assert.IsType<Planned<SitePlan>>(SettlementGenerator.Plan(new("village", area, new(),
            new(BuildingCount: buildingCount, Family: family), preferences), scope)).Plan;

    private static SiteInfo Commit(PlanningBoard board, GenerationScope scope, SitePlan plan)
    {
        Assert.True(scope.Draft.Accept(plan.Patch, out var error), error);
        Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
        return plan.Site;
    }

    private static void VerifyAccess(PlanningBoard board, SiteInfo site)
    {
        var access = site.SettlementLayout!.Access.Indices(board.Width).ToHashSet();
        var start = site.Entrances[0].OutsideIndex;
        Assert.Contains(start, access);
        HashSet<int> seen = [start];
        Queue<int> queue = new();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var cell))
        {
            Assert.True(board.Passable(cell));
            foreach (var neighbor in board.Neighbors(cell).Where(access.Contains))
            {
                var edge = board.EdgeAt(EdgeKey.Between(cell, neighbor, board.Width));
                if ((edge.Passable() || edge == EdgeKind.ClosedDoor) && seen.Add(neighbor)) queue.Enqueue(neighbor);
            }
        }
        Assert.Equal(access.Order(), seen.Order());
        Assert.All(site.Buildings.SelectMany(b => b.Portals).Concat(site.Entrances), p =>
        {
            Assert.Contains(p.InsideIndex, seen);
            Assert.Contains(p.OutsideIndex, seen);
        });
        Assert.All(site.Buildings.SelectMany(b => b.Rooms), r => Assert.Contains(r.Usable.Indices(board.Width), seen.Contains));
    }

    private static void AssertInBounds(int cell, CellBounds bounds, int width)
    {
        Assert.InRange(cell % width, bounds.X, bounds.X + bounds.Width - 1);
        Assert.InRange(cell / width, bounds.Y, bounds.Y + bounds.Height - 1);
    }
}
