using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MountainDungeonTests
{
    private static NaturalTerrainSample[] OriginalSamples(PlanningBoard board) =>
        Enumerable.Range(0, board.Width * board.Height).Select(i => new NaturalTerrainSample(board.TerrainAt(i), .2, .3, .4, .5)).ToArray();

    private static int[] NaturalHoles(PlanningBoard board) => Enumerable.Range(0, 4).SelectMany(y =>
        Enumerable.Range(0, 5).Select(x => (7 + y * 4) * board.Width + 7 + x * 4)).ToArray();

    private static void CarveEarlierFloor(PlanningBoard board, IEnumerable<int> cells)
    {
        var earlier = board.BeginDraft(new(0, 0, board.Width, board.Height), "natural-floor", new());
        foreach (var cell in cells) earlier.ReplaceTerrain(cell, "stone-floor");
        Assert.True(board.TryCommit(earlier.BuildPatch(), out var error), error);
    }

    [Fact]
    public void AbsorptionCannotPublishANaturalFloorBranchAtTheCropBoundary()
    {
        PlanningBoard CroppedRock()
        {
            var initial = Board().Export();
            return new(initial with { Board = initial.Board with { Cells = initial.Board.Cells.Select(c =>
                c with { Position = new(c.Position.X - 4, c.Position.Y) }).ToList() } }, ContentBundle.Canonical());
        }
        var board = CroppedRock(); var samples = OriginalSamples(board);
        var analysis = TerrainAnalyzer.Analyze(new(Width: board.Width, Height: board.Height), samples);
        var holes = NaturalHoles(board).Select(i => i - 4).ToArray();
        var referenceBoard = CroppedRock(); CarveEarlierFloor(referenceBoard, holes);
        var referenceScope = Scope(referenceBoard);
        var reference = Assert.IsType<Planned<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", referenceScope.Draft.AllowedArea, new(),
            OriginalAnalysis: analysis, OriginalSamples: samples), referenceScope)).Plan;
        var roomCell = reference.Site.MountainDungeon!.Rooms.SelectMany(r => r.Interior.Indices(board.Width))
            .OrderBy(i => i % board.Width).ThenBy(i => i).First();
        var branch = Enumerable.Range(0, roomCell % board.Width + 1).Select(x => roomCell / board.Width * board.Width + x).ToArray();
        CarveEarlierFloor(board, holes.Concat(branch));
        var before = ScenarioDefinitionJson.ToJson(board.Export()); var revision = board.Revision;
        var scope = Scope(board);
        // Perforation prevents intact-only rooms, so a candidate must use compatibility.
        Assert.IsType<Rejected<SitePlan>>(MountainDungeonGenerator.Plan(new("intact-only", scope.Draft.AllowedArea, new()), scope));
        var result = MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new(),
            OriginalAnalysis: analysis, OriginalSamples: samples), scope);
        Assert.Equal(revision, board.Revision); Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export()));
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges); Assert.Empty(scope.Draft.BuildPatch().EdgeChanges);
        Assert.Empty(scope.Draft.BuildPatch().Reservations);
        if (result is Planned<SitePlan> planned)
        {
            Assert.DoesNotContain(planned.Plan.Patch.Reservations, r => r.Index == branch[0]);
            Assert.True(scope.Draft.Accept(planned.Plan.Patch, out var error), error); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
            AssertCompleteShell(board, planned.Plan);
        }
        Assert.All(branch, i => Assert.Equal("stone-floor", board.TerrainAt(i)));
    }

    [Fact]
    public void TouchingEarlierFloorBranchIsAbsorbedInsteadOfLeftAsAnExtraMouth()
    {
        var board = Board(); var samples = OriginalSamples(board);
        var analysis = TerrainAnalyzer.Analyze(new(Width: board.Width, Height: board.Height), samples);
        var referenceBoard = Board(); CarveEarlierFloor(referenceBoard, NaturalHoles(referenceBoard));
        var referenceScope = Scope(referenceBoard);
        var reference = Assert.IsType<Planned<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", referenceScope.Draft.AllowedArea, new(),
            OriginalAnalysis: analysis, OriginalSamples: samples), referenceScope)).Plan;
        var dungeon = reference.Site.MountainDungeon!;
        var roomCells = dungeon.Rooms.SelectMany(r => r.Interior.Indices(board.Width)).ToHashSet();
        var branch = dungeon.ProtectedRock.Indices(board.Width).First(i => board.Neighbors(i).Any(roomCells.Contains) &&
            board.Neighbors(i).All(j => board.TerrainAt(j) == "solid-rock"));
        CarveEarlierFloor(board, NaturalHoles(board).Append(branch));
        var scope = Scope(board);
        var plan = Assert.IsType<Planned<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new(),
            OriginalAnalysis: analysis, OriginalSamples: samples), scope)).Plan;
        Assert.True(plan.Site.MountainDungeon!.Corridors.Contains(branch, board.Width));
        Assert.DoesNotContain(plan.Patch.TerrainChanges, c => c.Index == branch);
        Assert.True(scope.Draft.Accept(plan.Patch, out var error), error); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
        AssertCompleteShell(board, plan);
    }

    [Fact]
    public void EarlierNaturalFloorIsAbsorbedWithOriginalProvenanceAndNeverRefilled()
    {
        var original = Board(); var samples = OriginalSamples(original);
        var analysis = TerrainAnalyzer.Analyze(new(Width: original.Width, Height: original.Height), samples);
        var labels = analysis.FormationLabels.ToArray();
        var holes = NaturalHoles(original);
        SitePlan Generate()
        {
            var board = Board(); CarveEarlierFloor(board, holes);
            var scope = Scope(board);
            var plan = Assert.IsType<Planned<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new(),
                OriginalAnalysis: analysis, OriginalSamples: samples), scope)).Plan;
            var reused = holes.First(i => plan.Site.MountainDungeon!.Rooms.Any(r => r.Interior.Contains(i, board.Width)));
            Assert.DoesNotContain(plan.Patch.TerrainChanges, c => holes.Contains(c.Index));
            Assert.Contains(plan.Patch.Reservations, r => r.Index == reused);
            Assert.True(scope.Draft.Accept(plan.Patch, out var error), error); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
            Assert.Equal("stone-floor", board.TerrainAt(reused));
            Assert.Equal(24 * 20 - holes.Length, plan.Site.MountainDungeon!.RockMassArea);
            AssertCompleteShell(board, plan);
            return plan;
        }
        Assert.Equal(JsonSerializer.Serialize(Generate()), JsonSerializer.Serialize(Generate()));
        Assert.Equal(labels, analysis.FormationLabels);
        Assert.All(holes, i => Assert.Equal("solid-rock", samples[i].TerrainTypeId));
    }

    [Theory]
    [InlineData("route")]
    [InlineData("machine")]
    [InlineData("foreign-site")]
    [InlineData("edge")]
    public void EarlierFloorOwnershipRoutesAndMachinesRemainProtected(string protection)
    {
        var board = Board(); var samples = OriginalSamples(board);
        var analysis = TerrainAnalyzer.Analyze(new(Width: board.Width, Height: board.Height), samples);
        var reference = Plan(Board()); var cell = reference.Site.MountainDungeon!.Rooms[0].Interior.Indices(board.Width).First();
        var earlier = board.BeginDraft(new(0, 0, board.Width, board.Height), "earlier-owner", new());
        earlier.ReplaceTerrain(cell, "stone-floor");
        if (protection == "route") { earlier.EnableRoutePlanning(); earlier.ReserveRoute([cell], "protected-route"); }
        if (protection == "machine") earlier.PlaceMachine(new("protected-machine", new(cell % board.Width, cell / board.Width), MachineTypes.Beacon));
        if (protection == "foreign-site") earlier.Reserve([cell]);
        var wall = EdgeKey.Between(cell, cell + 1, board.Width);
        if (protection == "edge") earlier.SetEdge(wall, EdgeKind.Wall);
        Assert.True(board.TryCommit(earlier.BuildPatch(), out var error), error);
        var scope = Scope(board);
        var result = MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new(),
            OriginalAnalysis: analysis, OriginalSamples: samples), scope);
        if (result is Planned<SitePlan> planned)
        {
            Assert.DoesNotContain(planned.Plan.Patch.TerrainChanges, c => c.Index == cell);
            Assert.DoesNotContain(planned.Plan.Patch.Reservations, r => r.Index == cell);
            Assert.True(scope.Draft.Accept(planned.Plan.Patch, out error), error); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
            AssertCompleteShell(board, planned.Plan);
        }
        else { Assert.Empty(scope.Draft.BuildPatch().TerrainChanges); Assert.Empty(scope.Draft.BuildPatch().Reservations); }
        Assert.Equal("stone-floor", board.TerrainAt(cell));
        if (protection == "route") Assert.Equal(new[] { "protected-route" }, board.RouteMemberships(cell));
        if (protection == "machine") Assert.Equal("protected-machine", Assert.Single(board.Export().Machines!).Id);
        if (protection == "foreign-site") Assert.True(board.IsReserved(cell));
        if (protection == "edge") Assert.Equal(EdgeKind.Wall, board.EdgeAt(wall));
    }

    [Fact]
    public void OriginalRockProvenanceCannotMergeTwoRemainingMassesThroughEarlierFloor()
    {
        var board = Board(9, 20); var samples = OriginalSamples(board);
        var analysis = TerrainAnalyzer.Analyze(new(Width: board.Width, Height: board.Height), samples);
        var cut = Enumerable.Range(4, 20).Select(y => y * board.Width + 8).ToArray();
        CarveEarlierFloor(board, cut);
        var before = ScenarioDefinitionJson.ToJson(board.Export()); var scope = Scope(board);
        Assert.IsType<Rejected<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new(),
            OriginalAnalysis: analysis, OriginalSamples: samples), scope));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export()));
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges); Assert.Empty(scope.Draft.BuildPatch().Reservations);
        Assert.All(cut, i => Assert.Equal("stone-floor", board.TerrainAt(i)));
        Assert.InRange(scope.Budget.Attempts, 0, 20);
        Assert.InRange(scope.Budget.Work, 0, scope.Budget.MaxWork);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ProvenanceNeverAuthorizesProtectedOrNonRockFloor(bool supplyProvenance, bool protect)
    {
        var original = Board(); var samples = OriginalSamples(original);
        var reference = Plan(Board()); var reused = reference.Site.MountainDungeon!.Rooms[0].Interior.Indices(original.Width).First();
        if (!protect) samples[reused] = samples[reused] with { TerrainTypeId = "grass" };
        var analysis = TerrainAnalyzer.Analyze(new(Width: original.Width, Height: original.Height), samples);
        var initial = original.Export();
        var board = new PlanningBoard(initial with { Board = initial.Board with { Cells = initial.Board.Cells
            .Select(c => c.Position.Y * original.Width + c.Position.X == reused ? c with { Terrain = TerrainKind.StoneFloor } : c).ToList() } },
            ContentBundle.Canonical(), protect ? new HashSet<int> { reused } : null);
        var scope = Scope(board);
        var result = MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new(),
            OriginalAnalysis: supplyProvenance ? analysis : null, OriginalSamples: supplyProvenance ? samples : null), scope);
        if (result is Planned<SitePlan> planned)
        {
            Assert.DoesNotContain(planned.Plan.Patch.Reservations, r => r.Index == reused);
            Assert.DoesNotContain(planned.Plan.Patch.TerrainChanges, c => c.Index == reused);
            Assert.True(scope.Draft.Accept(planned.Plan.Patch, out var error), error); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
            AssertCompleteShell(board, planned.Plan);
        }
        else { Assert.Empty(scope.Draft.BuildPatch().TerrainChanges); Assert.Empty(scope.Draft.BuildPatch().Reservations); }
        Assert.Equal("stone-floor", board.TerrainAt(reused));
    }

    private static void AssertCompleteShell(PlanningBoard board, SitePlan plan)
    {
        var dungeon = plan.Site.MountainDungeon!;
        var floor = dungeon.Rooms.SelectMany(r => r.Interior.Indices(board.Width)).Concat(dungeon.Corridors.Indices(board.Width)).ToHashSet();
        var entrance = Assert.Single(plan.Site.Entrances);
        foreach (var cell in floor)
        for (var y = cell / board.Width - 1; y <= cell / board.Width + 1; y++)
        for (var x = cell % board.Width - 1; x <= cell % board.Width + 1; x++)
        {
            Assert.InRange(x, 0, board.Width - 1); Assert.InRange(y, 0, board.Height - 1);
            var adjacent = y * board.Width + x;
            if (floor.Contains(adjacent)) continue;
            if (board.TerrainAt(adjacent) == "solid-rock") Assert.True(dungeon.ProtectedRock.Contains(adjacent, board.Width));
            else
            {
                Assert.Equal(entrance.InsideIndex, cell);
                var dx = entrance.OutsideIndex % board.Width - cell % board.Width;
                var dy = entrance.OutsideIndex / board.Width - cell / board.Width;
                Assert.True((x - cell % board.Width) * dx + (y - cell / board.Width) * dy > 0);
            }
        }
    }

    private static PlanningBoard Board(int rockWidth = 24, int rockHeight = 20, bool cave = false)
    {
        const int width = 34, height = 28;
        return new(new ScenarioDefinition
        {
            Board = new() { Width = width, Height = height, DefaultTerrain = TerrainKind.Grass,
                Cells = Enumerable.Range(4, rockHeight).SelectMany(y => Enumerable.Range(4, rockWidth).Select(x =>
                    new CellDefinition(new(x, y), TerrainKind.SolidRock))).Where(c => !cave || c.Position.X != 15 || c.Position.Y != 14).ToList() },
            Units = [], UnitTypeIds = [], Agency = [], Loot = LootSettings.Empty
        }, ContentBundle.Canonical());
    }
    private static GenerationScope Scope(PlanningBoard board, ulong seed = 42, PlanningBudget? budget = null) =>
        new(seed, "mountain-test", board.BeginDraft(new(0, 0, board.Width, board.Height), "mountain-test", budget ?? new(100, 1000000)));
    private static SitePlan Plan(PlanningBoard board, ulong seed = 42, MountainDungeonProfile? profile = null)
    {
        var revision = board.Revision; var scope = Scope(board, seed);
        var plan = Assert.IsType<Planned<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new(), Profile: profile), scope)).Plan;
        Assert.Equal(revision, board.Revision);
        Assert.True(scope.Draft.Accept(plan.Patch, out _));
        Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out var error), error);
        return plan;
    }

    [Theory]
    [InlineData(MountainDungeonProfile.TunnelNetwork)]
    [InlineData(MountainDungeonProfile.ChamberComplex)]
    [InlineData(MountainDungeonProfile.DeepPassage)]
    public void ThickRockPublishesRealRoomsConnectedFloorAndLocalIntactShell(MountainDungeonProfile profile)
    {
        var board = Board(cave: true); var plan = Plan(board, profile: profile); var site = plan.Site;
        var dungeon = Assert.IsType<MountainDungeonInfo>(site.MountainDungeon);
        Assert.Equal(profile, dungeon.Profile); Assert.InRange(dungeon.Rooms.Count, 2, 6); Assert.Empty(site.Buildings);
        Assert.True(dungeon.Rooms.Count(r => r.Interior.Area >= 15) >= 2);
        Assert.All(plan.Patch.TerrainChanges, c => Assert.Equal("solid-rock", c.Before));
        Assert.All(dungeon.Rooms, r => { Assert.InRange(Math.Min(r.Interior.Bounds.Width, r.Interior.Bounds.Height), 3, 5); Assert.InRange(Math.Max(r.Interior.Bounds.Width, r.Interior.Bounds.Height), 3, 7); Assert.True(r.TwoByTwoPlacements > 0); });
        Assert.All(dungeon.ProtectedRock.Indices(board.Width), i => Assert.Equal("solid-rock", board.TerrainAt(i)));
        Assert.InRange(dungeon.ProtectedRock.Area, 1, dungeon.RockMassArea - 1);
        Assert.True(site.Footprint.Area < dungeon.RockMassArea);
        var main = PlayableWorldComponent.Select(board); var entrance = Assert.Single(site.Entrances);
        Assert.Contains(entrance.InsideIndex, main); Assert.Contains(entrance.OutsideIndex, main);
        Assert.All(dungeon.Rooms.SelectMany(r => r.Interior.Indices(board.Width)).Where(board.Passable), i => Assert.Contains(i, main));
        Assert.All(dungeon.RequiredAccess.Indices(board.Width), i => { Assert.True(board.Passable(i)); Assert.Contains(i, main); });
        Assert.All(site.AccessPorts.SelectMany(p => p.Permission.TraverseCells), i =>
        {
            Assert.False(dungeon.ProtectedRock.Contains(i, board.Width));
            Assert.DoesNotContain(dungeon.Rooms, r => r.Interior.Contains(i, board.Width));
            Assert.False(dungeon.Corridors.Contains(i, board.Width));
        });
        // The isolated preexisting cave remains separated by actual rock, including corners.
        Assert.Equal("grass", board.TerrainAt(14 * board.Width + 15));
        foreach (var changed in plan.Patch.TerrainChanges)
        foreach (var adjacent in board.Neighbors(changed.Index))
            if (board.Passable(adjacent) && !plan.Patch.TerrainChanges.Any(c => c.Index == adjacent))
                Assert.Equal(entrance.OutsideIndex, adjacent);
        Assert.True(site.Access.TwoByTwoPlacements > 8);
        _ = GameEngine.CreateGame(board.Export());
    }

    [Theory]
    [InlineData(2, 20)]
    [InlineData(4, 20)]
    [InlineData(5, 5)]
    public void ThinOrRoomlessMassesAreOmittedWithoutAnyPublication(int width, int height)
    {
        var board = Board(width, height); var before = ScenarioDefinitionJson.ToJson(board.Export()); var scope = Scope(board);
        var failed = Assert.IsType<Rejected<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new()), scope));
        Assert.Equal(PlacementFailureCode.NoFootprint, failed.Failure.Code);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export()));
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges); Assert.Empty(scope.Draft.BuildPatch().Reservations);
    }

    [Fact]
    public void PreparationAndWorkFailuresRollbackPrivateDrafts()
    {
        foreach (var policy in new[] { new PreparationPolicy(ExcavateRock: false), new PreparationPolicy(MaxChangedCells: 1), new PreparationPolicy(MaxCost: 1) })
        {
            var board = Board(); var scope = Scope(board);
            var failed = Assert.IsType<Rejected<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, policy), scope));
            Assert.Equal(PlacementFailureCode.PreparationLimit, failed.Failure.Code);
            Assert.Empty(scope.Draft.BuildPatch().TerrainChanges); Assert.Empty(scope.Draft.BuildPatch().Reservations); Assert.Equal(0, board.Revision);
        }
        foreach (var budget in new[] { new PlanningBudget(0, 100000), new PlanningBudget(10, 0), new PlanningBudget(100, 14000) })
        {
            var board = Board(); var scope = Scope(board, budget: budget);
            var failed = Assert.IsType<Rejected<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new()), scope));
            Assert.Equal(PlacementFailureCode.BudgetExhausted, failed.Failure.Code);
            Assert.InRange(budget.Work, 0, budget.MaxWork); Assert.InRange(budget.Attempts, 0, budget.MaxAttempts);
            Assert.Empty(scope.Draft.BuildPatch().TerrainChanges); Assert.Empty(scope.Draft.BuildPatch().Reservations);
        }
    }

    [Fact]
    public void DeterminismAndSeedVariationIncludeGeometryAndProfiles()
    {
        Assert.Equal(JsonSerializer.Serialize(Plan(Board())), JsonSerializer.Serialize(Plan(Board())));
        var plans = Enumerable.Range(0, 16).Select(seed => Plan(Board(), (ulong)seed)).ToArray();
        Assert.Equal(3, plans.Select(p => p.Site.MountainDungeon!.Profile).Distinct().Count());
        Assert.True(plans.Select(p => JsonSerializer.Serialize(p.Site.MountainDungeon!.Rooms)).Distinct().Count() > 8);
        Assert.Contains(plans, p => p.Site.MountainDungeon!.Lava.Area > 0);
        foreach (var plan in plans)
        {
            var d = plan.Site.MountainDungeon!;
            Assert.All(d.Lava.Indices(34), i => Assert.False(d.RequiredAccess.Contains(i, 34)));
            Assert.All(d.TreasureAreas.SelectMany(a => a.Indices(34)), i => Assert.False(d.Lava.Contains(i, 34)));
        }
    }

    [Fact]
    public void UsedMassIsExcludedEvenWhenEarlierExcavationSplitsItsRemainder()
    {
        var board = Board(); var used = Enumerable.Range(4, 20).SelectMany(y => Enumerable.Range(y * board.Width + 4, 24)).ToHashSet();
        _ = Plan(board); var scope = Scope(board);
        var failed = Assert.IsType<Rejected<SitePlan>>(MountainDungeonGenerator.Plan(new("second", scope.Draft.AllowedArea, new(), ExcludedRockCells: used), scope));
        Assert.Equal(PlacementFailureCode.NoFootprint, failed.Failure.Code); Assert.Empty(scope.Draft.BuildPatch().Reservations);
    }

    [Fact]
    public void CorridorsContainPracticalTwoWideRunsAndOnlyEntranceBreachesTheShell()
    {
        var board = Board(); var plan = Plan(board, profile: MountainDungeonProfile.DeepPassage);
        var dungeon = plan.Site.MountainDungeon!;
        var corridors = dungeon.Corridors.Indices(board.Width).ToHashSet();
        Assert.True(SiteGeometry.TwoByTwo(Scope(board).Draft, corridors) > 0);
        var entrance = Assert.Single(plan.Site.Entrances);
        Assert.Equal(1, board.Neighbors(entrance.InsideIndex).Count(i => board.Passable(i) && i != entrance.OutsideIndex));
        Assert.All(dungeon.Lava.Indices(board.Width), i => Assert.Equal("ordinary-lava", board.TerrainAt(i)));
    }

    [Fact]
    public void RoomCapableRockWithoutAMainComponentEntranceIsOmitted()
    {
        var initial = Board().Export();
        var ring = Enumerable.Range(3, 26).SelectMany(x => new[] { new Cell(x, 3), new Cell(x, 24) })
            .Concat(Enumerable.Range(4, 20).SelectMany(y => new[] { new Cell(3, y), new Cell(28, y) }));
        var board = new PlanningBoard(initial with { Board = initial.Board with
        { Cells = initial.Board.Cells.Concat(ring.Select(p => new CellDefinition(p, TerrainKind.Water))).ToList() } }, ContentBundle.Canonical());
        var scope = Scope(board);
        var failed = Assert.IsType<Rejected<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new()), scope));
        Assert.Equal(PlacementFailureCode.EntranceConstraint, failed.Failure.Code);
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges); Assert.Empty(scope.Draft.BuildPatch().Reservations);
    }

    [Fact]
    public void ProtectedCarvedPassDividesActualRockIntoTwoIndependentDungeonMasses()
    {
        var board = Board(); var area = new CellBounds(0, 0, board.Width, board.Height);
        var route = Enumerable.Range(0, board.Height).Select(y => y * board.Width + 15).ToArray();
        var road = board.BeginDraft(area, "test-road", new()); road.EnableRoutePlanning();
        foreach (var i in route) road.ReplaceTerrain(i, "stone-floor");
        road.ReserveRoute(route, "test-road"); Assert.True(board.TryCommit(road.BuildPatch(), out var error), error);
        var first = Plan(board); var firstDungeon = first.Site.MountainDungeon!;
        var rightSide = firstDungeon.RockMassAnchor % board.Width > 15;
        var used = Enumerable.Range(4, 20).SelectMany(y => Enumerable.Range(4, 24)
            .Where(x => rightSide ? x > 15 : x < 15).Select(x => y * board.Width + x)).ToHashSet();
        var scope = Scope(board);
        var second = Assert.IsType<Planned<SitePlan>>(MountainDungeonGenerator.Plan(new("second", area, new(), ExcludedRockCells: used), scope)).Plan;
        Assert.True(scope.Draft.Accept(second.Patch, out _)); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
        Assert.NotEqual(firstDungeon.RockMassAnchor, second.Site.MountainDungeon!.RockMassAnchor);
        Assert.Equal(460, firstDungeon.RockMassArea + second.Site.MountainDungeon.RockMassArea);
        Assert.All(route, i => { Assert.Equal("stone-floor", board.TerrainAt(i)); Assert.True(board.IsRouteCorridor(i)); Assert.Equal(new[] { "test-road" }, board.RouteMemberships(i)); });
        Assert.DoesNotContain(first.Patch.TerrainChanges.Concat(second.Patch.TerrainChanges), c => route.Contains(c.Index));
    }

    [Fact]
    public void ProtectedLocalShellAndExistingWallsRejectWithoutLeakingGeometryOrReservations()
    {
        var reference = Plan(Board());
        var initial = Board().Export();
        var allowedFloor = reference.Patch.TerrainChanges.Select(c => c.Index).ToHashSet();
        var protectedRock = initial.Board.Cells.Select(c => c.Position.Y * initial.Board.Width + c.Position.X).Where(i => !allowedFloor.Contains(i)).ToHashSet();
        var protectedBoard = new PlanningBoard(initial, ContentBundle.Canonical(), protectedRock);
        var walls = initial.Board.Cells.SelectMany(c => new[] { EdgeDirection.Right, EdgeDirection.Down }
            .Where(direction => direction == EdgeDirection.Right ? c.Position.X < 27 : c.Position.Y < 23)
            .Select(direction => new EdgeDefinition(c.Position, direction, EdgeKind.Wall))).ToList();
        var walledBoard = new PlanningBoard(initial with { Board = initial.Board with { Edges = walls } }, ContentBundle.Canonical());
        foreach (var board in new[] { protectedBoard, walledBoard })
        {
            var before = ScenarioDefinitionJson.ToJson(board.Export()); var scope = Scope(board);
            Assert.IsType<Rejected<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new()), scope));
            Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export()));
            Assert.Empty(scope.Draft.BuildPatch().TerrainChanges); Assert.Empty(scope.Draft.BuildPatch().EdgeChanges); Assert.Empty(scope.Draft.BuildPatch().Reservations);
        }
    }

    [Fact]
    public void AdversarialTallThinRockIsScreenedWithinItsSurveyAllowance()
    {
        var board = new PlanningBoard(new ScenarioDefinition
        {
            Board = new() { Width = 120, Height = 120, DefaultTerrain = TerrainKind.Grass,
                Cells = Enumerable.Range(4, 112).SelectMany(y => Enumerable.Range(50, 4).Select(x => new CellDefinition(new(x, y), TerrainKind.SolidRock))).ToList() },
            Units = [], UnitTypeIds = [], Agency = [], Loot = LootSettings.Empty
        }, ContentBundle.Canonical());
        var budget = new PlanningBudget(5, 200000); var scope = Scope(board, budget: budget);
        var result = Assert.IsType<Rejected<SitePlan>>(MountainDungeonGenerator.Plan(new("dungeon", scope.Draft.AllowedArea, new()), scope));
        Assert.Equal(PlacementFailureCode.NoFootprint, result.Failure.Code); Assert.Equal(0, budget.Attempts);
        Assert.InRange(budget.Work, 1, budget.MaxWork); Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
    }

    [Theory]
    [InlineData(30, 24, ContentDensity.Low, 1)]
    [InlineData(60, 50, ContentDensity.Medium, 2)]
    [InlineData(120, 120, ContentDensity.VeryHigh, 4)]
    public void TargetUsesCappedEffectiveAreaAndMaximumExistingDensity(int width, int height, ContentDensity density, int target)
    {
        var window = new WorldWindow(Width: width, Height: height);
        Assert.Equal(target, MountainDungeonGenerator.Target(window, new(Campsites: density, Settlements: ContentDensity.None)));
        Assert.Equal(target, MountainDungeonGenerator.Target(window, new(Campsites: ContentDensity.None, Settlements: density)));
        Assert.Equal(0, MountainDungeonGenerator.Target(window, new(Campsites: ContentDensity.None, Settlements: ContentDensity.None)));
        Assert.Equal(0, MountainDungeonGenerator.Target(window, new(Enabled: false)));
    }
}
