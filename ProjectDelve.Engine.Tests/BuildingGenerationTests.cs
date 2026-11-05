using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BuildingGenerationTests
{
    [Fact]
    public void RailFloorAndInventedExteriorPortalsAreRejectedBeforePlacement()
    {
        var (_, scope) = Context();
        var rail = Assert.IsType<Rejected<BuildingPlan>>(BuildingGenerator.Plan(Request() with { Requirements = new(FloorTypeId: "rail") }, scope));
        Assert.Equal(PlacementFailureCode.InvalidContent, rail.Failure.Code); Assert.Equal(0, scope.Budget.Attempts);
        var edge = EdgeKey.Between(2 * 16 + 2, 1 * 16 + 2, 16);
        scope.Draft.SetEdge(edge, EdgeKind.Wall);
        var portal = new SitePortal("fake", "test", 2 * 16 + 2, 1 * 16 + 2, edge, EdgeKind.ClosedDoor);
        var room = Assert.IsType<Rejected<RoomLayoutPlan>>(RoomLayoutGenerator.Plan(new("rooms", new(2, 2, 8, 7), ExteriorPortals: [portal]), scope));
        Assert.Equal(PlacementFailureCode.EntranceConstraint, room.Failure.Code); Assert.Equal(0, scope.Budget.Attempts);
    }
    private static ScenarioDefinition Scenario(TerrainKind terrain = TerrainKind.Grass) => new()
    {
        Board = new() { Width = 16, Height = 16, DefaultTerrain = terrain },
        UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
    };
    private static (PlanningBoard Board, GenerationScope Scope) Context(ulong seed = 1, ScenarioDefinition? scenario = null, PlanningBudget? budget = null)
    {
        var board = new PlanningBoard(scenario ?? Scenario(), ContentBundle.Canonical());
        return (board, new(seed, "test", board.BeginDraft(new(0, 0, 16, 16), "test", budget ?? new())));
    }
    private static BuildingRequest Request(int rooms = 2, BuildingEntranceRequirement? entrance = null) =>
        new("house", new(2, 2, 10, 11), new(RoomCount: rooms, Entrance: entrance));
    private static BuildingPlan Plan(BuildingRequest request, GenerationScope scope) => Assert.IsType<Planned<BuildingPlan>>(BuildingGenerator.Plan(request, scope)).Plan;
    private static ScenarioDefinition Commit(PlanningBoard board, GenerationScope scope, BuildingPlan plan)
    {
        Assert.True(scope.Draft.Accept(plan.Patch, out var error), error);
        Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
        var scenario = board.Export(); _ = GameEngine.CreateGame(scenario); return scenario;
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void IndependentBuildingHasExactRoomsWallsDoorsAndConnectedUsableGround(int count)
    {
        var (board, scope) = Context((ulong)count); var plan = Plan(Request(count), scope);
        Assert.Equal(count, plan.Building.Rooms.Count);
        var scenario = Commit(board, scope, plan); var footprint = plan.Building.Footprint.Indices(16).ToHashSet();
        Assert.Equal(footprint.Count, plan.Building.Rooms.Sum(r => r.Interior.Area));
        Assert.Equal(footprint.Count, plan.Building.Rooms.SelectMany(r => r.Interior.Indices(16)).Distinct().Count());
        Assert.All(plan.Building.Rooms, room =>
        {
            Assert.True(room.Interior.Area >= 6); Assert.True(room.Usable.Area < room.Interior.Area);
            Assert.NotEmpty(room.PortalIds); Assert.All(room.PortalIds, p => Assert.Contains(plan.Building.Portals, portal => portal.Id == p));
            Assert.All(room.Usable.Indices(16), i => Assert.True(board.Passable(i)));
        });
        var entrance = Assert.Single(plan.Building.Portals.Where(p => !footprint.Contains(p.OutsideIndex)));
        Assert.Equal(EdgeKind.ClosedDoor, entrance.Kind); Assert.True(board.Passable(entrance.InsideIndex)); Assert.True(board.Passable(entrance.OutsideIndex));
        var seen = Flood(board, entrance.OutsideIndex); Assert.All(plan.Building.Rooms.SelectMany(r => r.Usable.Indices(16)), i => Assert.Contains(i, seen));
        foreach (var i in footprint) foreach (var j in board.Neighbors(i).Where(j => !footprint.Contains(j)))
        {
            var key = EdgeKey.Between(i, j, 16);
            Assert.Equal(key == entrance.Edge ? EdgeKind.ClosedDoor : EdgeKind.Wall, board.EdgeAt(key));
        }
        Assert.True(plan.Building.Access.InternallyConnected);
        Assert.Equal(plan.Building.Access.UsableCells, plan.Building.Access.ConnectedCells);
        Assert.NotEmpty(scenario.Board.Edges);
    }

    [Fact]
    public void HardEntranceUsesTheExteriorCellAndRequestedSide()
    {
        var (board, scope) = Context(); var exterior = 2 * 16 + 5;
        var plan = Plan(Request(3, new(CardinalSide.North, exterior, 0)), scope);
        var entrance = plan.Building.Portals.Single(p => p.Id.EndsWith("/entrance"));
        Assert.Equal(exterior, entrance.OutsideIndex); Assert.Equal(exterior + 16, entrance.InsideIndex);
        Commit(board, scope, plan);
        var (_, impossibleScope) = Context();
        var rejected = Assert.IsType<Rejected<BuildingPlan>>(BuildingGenerator.Plan(Request(2, new(CardinalSide.North, 0, 0)), impossibleScope));
        Assert.Equal(PlacementFailureCode.EntranceConstraint, rejected.Failure.Code); Assert.Equal(0, impossibleScope.Budget.Attempts);
    }

    [Theory]
    [InlineData(TerrainKind.Tree)] [InlineData(TerrainKind.SolidRock)] [InlineData(TerrainKind.Water)]
    public void PreparationCreatesFloorAndApronThroughAuthorizedNaturalTerrain(TerrainKind terrain)
    {
        var (board, scope) = Context(scenario: Scenario(terrain)); var plan = Plan(Request(3), scope);
        Assert.NotEmpty(plan.Patch.TerrainChanges); Commit(board, scope, plan);
        Assert.All(plan.Building.Portals, p => { Assert.True(board.Passable(p.InsideIndex)); Assert.True(board.Passable(p.OutsideIndex)); });
        var (_, disabled) = Context(scenario: Scenario(terrain));
        var rejected = Assert.IsType<Rejected<BuildingPlan>>(BuildingGenerator.Plan(Request() with { Preparation = new(false, false, false) }, disabled));
        Assert.Equal(PlacementFailureCode.PreparationLimit, rejected.Failure.Code); Assert.Empty(disabled.Draft.BuildPatch().TerrainChanges);
    }

    [Fact]
    public void BuildingsVaryProcedurallyAndSameSeedIsStable()
    {
        var fingerprints = new HashSet<string>();
        for (ulong seed = 0; seed < 16; seed++)
        {
            var (_, scope) = Context(seed); var plan = Plan(Request(3), scope);
            var signature = JsonSerializer.Serialize(new { plan.Building, plan.Patch.TerrainChanges, plan.Patch.EdgeChanges });
            fingerprints.Add(signature);
            var (_, repeated) = Context(seed); var again = Plan(Request(3), repeated);
            Assert.Equal(signature, JsonSerializer.Serialize(new { again.Building, again.Patch.TerrainChanges, again.Patch.EdgeChanges }));
        }
        Assert.True(fingerprints.Count >= 8);
    }

    [Fact]
    public void AcceptedBuildingCanBeDiscardedWithItsParentWithoutRefundingBudget()
    {
        var (board, scope) = Context(); var plan = Plan(Request(), scope);
        Assert.True(scope.Draft.Accept(plan.Patch, out _)); Assert.NotEmpty(scope.Draft.BuildPatch().EdgeChanges);
        Assert.Empty(board.Export().Board.Edges); Assert.Equal("grass", board.TerrainAt(plan.Building.Footprint.Indices(16).First()));
        Assert.True(scope.Budget.Attempts >= 2); Assert.True(scope.Budget.Work > 0); Assert.Equal(0, board.Revision);
        Assert.Throws<ArgumentException>(() => scope.Draft.SetTerrain(plan.Building.Footprint.Indices(16).First(), "tree"));
    }

    [Fact]
    public void InvalidContentAndImpossibleDimensionsRejectBeforeAttemptingSpatialWork()
    {
        var (_, scope) = Context();
        var badContent = Assert.IsType<Rejected<BuildingPlan>>(BuildingGenerator.Plan(Request() with { Requirements = new(FloorTypeId: "missing") }, scope));
        Assert.Equal(PlacementFailureCode.InvalidContent, badContent.Failure.Code); Assert.Equal(0, scope.Budget.Attempts);
        Assert.IsType<Rejected<BuildingPlan>>(BuildingGenerator.Plan(Request() with { Requirements = new(MinWidth: 9, MaxWidth: 5) }, scope));
        Assert.Equal(0, scope.Budget.Attempts);
        var (_, exhausted) = Context(budget: new(0, 200000));
        Assert.Equal(PlacementFailureCode.BudgetExhausted, Assert.IsType<Rejected<BuildingPlan>>(BuildingGenerator.Plan(Request(), exhausted)).Failure.Code);
    }

    [Fact]
    public void LocalTypedFloorAndFurnitureUseTheSharedCatalogAndSurviveExport()
    {
        var local = Scenario() with
        {
            TerrainTypes = [new("test-floor", "Test Floor", true, false)],
            TerrainAssemblies = [new("test-furniture", "Test Furniture", "test-floor") { Components = [new("table", "table")] }]
        };
        var (board, scope) = Context(scenario: local);
        var plan = Plan(Request() with { Requirements = new(FloorTypeId: "test-floor", FurnitureTypeId: "test-furniture") }, scope);
        var export = Commit(board, scope, plan);
        Assert.Single(export.TerrainAssemblies!); Assert.Single(export.TerrainTypes!);
        Assert.Contains(export.Board.Cells, c => c.TerrainTypeId == "test-furniture");
        Assert.DoesNotContain(TerrainCatalog.All, t => t.Id == "test-floor");
    }

    [Fact]
    public void LargePlotSearchIsBoundedAndStillFindsAnExactEntrance()
    {
        var scenario = Scenario() with { Board = new() { Width = 120, Height = 120, DefaultTerrain = TerrainKind.Grass } };
        var board = new PlanningBoard(scenario, ContentBundle.Canonical()); var budget = new PlanningBudget();
        var scope = new GenerationScope(55, "large", board.BeginDraft(new(0, 0, 120, 120), "large", budget));
        var exact = 60 * 120 + 60;
        var plan = Plan(new("house", new(1, 1, 118, 118), new(RoomCount: 3, Entrance: new(CardinalSide.North, exact))), scope);
        Assert.Equal(exact, plan.Building.Portals.Single(p => p.Id.EndsWith("/entrance")).OutsideIndex);
        Assert.True(budget.Work < 10000); Assert.True(scope.Draft.Accept(plan.Patch, out _)); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out _));
    }

    [Fact]
    public void ExplicitEdgePreparationCanReplacePriorFeaturesWithoutWriteConflicts()
    {
        var source = Scenario();
        source.Board.Edges.AddRange(Enumerable.Range(2, 11).SelectMany(y => Enumerable.Range(2, 9).Select(x => new EdgeDefinition(new(x, y), EdgeDirection.Right, EdgeKind.Wall))));
        var (board, scope) = Context(scenario: source);
        var plan = Plan(Request(3) with { Preparation = new(ReplaceExistingEdges: true) }, scope);
        Commit(board, scope, plan);
    }

    [Fact]
    public void FurnitureDoesNotChangePartitionOrDoorRandomStreams()
    {
        var (_, furnished) = Context(99); var (_, empty) = Context(99);
        var a = Plan(Request(3), furnished); var b = Plan(Request(3) with { Requirements = new(RoomCount: 3, Furniture: false) }, empty);
        Assert.Equal(JsonSerializer.Serialize(a.Patch.EdgeChanges), JsonSerializer.Serialize(b.Patch.EdgeChanges));
        Assert.Equal(JsonSerializer.Serialize(a.Building.Footprint), JsonSerializer.Serialize(b.Building.Footprint));
    }

    [Fact]
    public void IndependentRoomsRespectExistingEdgeReplacementPolicy()
    {
        var source = Scenario();
        source.Board.Edges.AddRange(Enumerable.Range(2, 7).Select(y => new EdgeDefinition(new(5, y), EdgeDirection.Right, EdgeKind.Wall)));
        var (board, scope) = Context(scenario: source);
        var request = new RoomLayoutRequest("rooms", new(2, 2, 8, 7), 3);
        var failed = Assert.IsType<Rejected<RoomLayoutPlan>>(RoomLayoutGenerator.Plan(request, scope));
        Assert.Equal(PlacementFailureCode.ReservationConflict, failed.Failure.Code); Assert.Equal(0, scope.Budget.Attempts);
        var result = Assert.IsType<Planned<RoomLayoutPlan>>(RoomLayoutGenerator.Plan(request with { ReplaceExistingEdges = true }, scope));
        Assert.True(scope.Draft.Accept(result.Plan.Patch, out _)); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out var error), error);
        Assert.Equal(3, result.Plan.Rooms.Count);
        var reached = Flood(board, result.Plan.Rooms[0].Usable.Indices(board.Width).First());
        Assert.All(result.Plan.Rooms.SelectMany(r => r.Usable.Indices(board.Width)), i => Assert.Contains(i, reached));
    }

    [Fact]
    public void RoomPlannerWorksIndependentlyAndTraceReplaysEveryAcceptedEdit()
    {
        var (board, scope) = Context(); var before = board.Export();
        var result = Assert.IsType<Planned<RoomLayoutPlan>>(RoomLayoutGenerator.Plan(new("rooms", new(2, 2, 8, 7), 3), scope)).Plan;
        Assert.Equal(3, result.Rooms.Count); Assert.True(scope.Draft.Accept(result.Patch, out _));
        Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out var error), error);
        var (_, buildingScope) = Context(); var building = Plan(Request(3), buildingScope);
        var terrain = Enumerable.Repeat("grass", 256).ToArray(); var edges = new Dictionary<EdgeKey, EdgeKind>();
        var operationIds = new HashSet<string> { buildingScope.Path };
        foreach (var operation in building.Operations)
        {
            Assert.Contains(operation.ParentId!, operationIds); Assert.True(operationIds.Add(operation.Id));
            foreach (var change in operation.TerrainChanges) { Assert.Equal(change.Before, terrain[change.Index]); terrain[change.Index] = change.After; }
            foreach (var change in operation.EdgeChanges) { Assert.Equal(change.Before, edges.GetValueOrDefault(change.Key, EdgeKind.None)); edges[change.Key] = change.After; }
        }
        foreach (var change in building.Patch.TerrainChanges) Assert.Equal(change.After, terrain[change.Index]);
        foreach (var change in building.Patch.EdgeChanges) Assert.Equal(change.After, edges[change.Key]);
        Assert.Empty(before.Board.Edges);
    }

    private static HashSet<int> Flood(PlanningBoard board, int start)
    {
        HashSet<int> seen = [start]; Queue<int> queue = new(); queue.Enqueue(start);
        while (queue.TryDequeue(out var i)) foreach (var j in board.Neighbors(i))
            if (board.Passable(j) && (board.EdgeAt(EdgeKey.Between(i, j, board.Width)).Passable() || board.EdgeAt(EdgeKey.Between(i, j, board.Width)) == EdgeKind.ClosedDoor) && seen.Add(j)) queue.Enqueue(j);
        return seen;
    }
}
