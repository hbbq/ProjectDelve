using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class BoardPlanningTests
{
    [Fact]
    public void InvalidLegacyAndTypedCellCombinationCannotBeNormalizedIntoValidity()
    {
        var scenario = new ScenarioDefinition { Board = new() { Width = 2, Height = 2,
            Cells = [new(new(0, 0), TerrainKind.Tree) { TerrainTypeId = "grass" }] }, Units = [], UnitTypeIds = [], Agency = [] };
        Assert.Throws<ArgumentException>(() => GameEngine.CreateGame(scenario));
        Assert.Throws<ArgumentException>(() => new PlanningBoard(scenario, ContentBundle.Canonical()));
    }
    private static ScenarioDefinition Scenario(int width = 4, int height = 3) => new()
    {
        Board = new() { Width = width, Height = height, DefaultTerrain = TerrainKind.Grass },
        UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
    };
    private static PlanningBoard Board(ScenarioDefinition? scenario = null) => new(scenario ?? Scenario(), ContentBundle.Canonical());
    private static BoardDraft Draft(PlanningBoard board, string owner = "site", PlanningBudget? budget = null) =>
        board.BeginDraft(new(0, 0, board.Width, board.Height), owner, budget ?? new());

    [Fact]
    public void TerrainAndEdgesArePrivateUntilAtomicCommitAndExportsAreDetached()
    {
        var source = Scenario(); var board = Board(source); var draft = Draft(board);
        draft.SetTerrain(1, "stone-floor"); draft.SetEdge(new(0, EdgeDirection.Right), EdgeKind.ClosedDoor);
        Assert.Equal("grass", board.TerrainAt(1)); Assert.Equal(EdgeKind.None, board.EdgeAt(new(0, EdgeDirection.Right)));
        Assert.True(board.TryCommit(draft.BuildPatch(), out var error), error);
        Assert.Equal("stone-floor", board.TerrainAt(1)); Assert.Equal(1, board.Revision);
        Assert.Equal(EdgeKind.ClosedDoor, board.EdgeAt(new(0, EdgeDirection.Right)));
        var export = board.Export(); export.Board.Cells.Clear(); source.Board.Edges.Add(new(new(0, 0), EdgeDirection.Right, EdgeKind.Wall));
        Assert.Single(board.Export().Board.Cells); Assert.Single(board.Export().Board.Edges);
        _ = GameEngine.CreateGame(board.Export());
    }

    [Fact]
    public void ConflictingWritesRequireExplicitReplacement()
    {
        var board = Board(); var draft = Draft(board); var key = new EdgeKey(0, EdgeDirection.Right);
        draft.SetEdge(key, EdgeKind.Wall); draft.SetEdge(key, EdgeKind.Wall);
        Assert.Throws<ArgumentException>(() => draft.SetEdge(key, EdgeKind.ClosedDoor));
        draft.ReplaceEdge(key, EdgeKind.ClosedDoor);
        draft.SetTerrain(0, "stone-floor"); Assert.Throws<ArgumentException>(() => draft.SetTerrain(0, "tree"));
        draft.ReplaceTerrain(0, "tree");
        Assert.True(board.TryCommit(draft.BuildPatch(), out _)); Assert.False(board.Passable(0));
    }

    [Fact]
    public void SourceEdgesRequireExplicitReplacementEvenInFreshDraft()
    {
        var source = Scenario(); source.Board.Edges.Add(new(new(0, 0), EdgeDirection.Right, EdgeKind.Wall));
        var draft = Draft(Board(source)); var key = new EdgeKey(0, EdgeDirection.Right);
        Assert.Throws<ArgumentException>(() => draft.SetEdge(key, EdgeKind.ClosedDoor));
        draft.ReplaceEdge(key, EdgeKind.ClosedDoor); Assert.Equal(EdgeKind.ClosedDoor, draft.EdgeAt(key));
    }

    [Fact]
    public void GeometryAndBothEdgeEndpointsMustBeWithinAllowedArea()
    {
        var board = Board(); var draft = board.BeginDraft(new(1, 1, 2, 1), "room", new());
        draft.SetEdge(EdgeKey.Between(5, 6, 4), EdgeKind.Wall);
        Assert.Throws<ArgumentException>(() => draft.SetEdge(EdgeKey.Between(4, 5, 4), EdgeKind.Wall));
        Assert.Throws<ArgumentException>(() => draft.SetEdge(new(3, EdgeDirection.Right), EdgeKind.Wall));
        Assert.Throws<ArgumentException>(() => draft.SetEdge(new(9, EdgeDirection.Down), EdgeKind.Wall));
        Assert.Throws<ArgumentException>(() => draft.SetTerrain(0, "tree"));
        Assert.Throws<ArgumentException>(() => draft.Fork(new(0, 0, 1, 1), "child"));
        Assert.Throws<ArgumentException>(() => EdgeKey.Between(3, 4, 4));
        Assert.Equal(new EdgeKey(0, EdgeDirection.Right), EdgeKey.Between(1, 0, 4));
    }

    [Fact]
    public void StaleRootAndChildPatchesCannotOverwriteNewerState()
    {
        var board = Board(); var stale = Draft(board); var fresh = Draft(board);
        fresh.SetEdge(new(0, EdgeDirection.Right), EdgeKind.Wall); Assert.True(board.TryCommit(fresh.BuildPatch(), out _));
        stale.SetTerrain(0, "tree"); Assert.False(board.TryCommit(stale.BuildPatch(), out _));
        Assert.Equal("grass", board.TerrainAt(0)); Assert.Equal(1, board.Revision);
        var parent = Draft(board); var child = parent.Fork(new(0, 0, 2, 2), "child"); child.SetTerrain(0, "tree");
        parent.SetTerrain(1, "stone-floor"); Assert.False(parent.Accept(child.BuildPatch(), out _));
        Assert.Equal("grass", parent.TerrainAt(0));
    }

    [Fact]
    public void ChildAndLaterSiblingRequireExplicitOverrideOfEarlierWrites()
    {
        var board = Board(); var parent = Draft(board); var key = new EdgeKey(0, EdgeDirection.Right);
        parent.SetTerrain(0, "stone-floor"); parent.SetEdge(key, EdgeKind.Wall);
        var child = parent.Fork(new(0, 0, 2, 2), "room");
        Assert.Throws<ArgumentException>(() => child.SetTerrain(0, "tree"));
        Assert.Throws<ArgumentException>(() => child.SetEdge(key, EdgeKind.ClosedDoor));
        child.ReplaceEdge(key, EdgeKind.ClosedDoor); Assert.True(child.CommitToParent(out _));
        var sibling = parent.Fork(new(0, 0, 2, 2), "room");
        Assert.Throws<ArgumentException>(() => sibling.SetEdge(key, EdgeKind.Wall));
        Assert.True(board.TryCommit(parent.BuildPatch(), out _)); Assert.Equal(EdgeKind.ClosedDoor, board.EdgeAt(key));
    }

    [Fact]
    public void ChildSuccessFollowedByParentDiscardKeepsRootUntouchedAndBudgetSpent()
    {
        var board = Board(); var budget = new PlanningBudget(2, 9); var parent = Draft(board, budget: budget);
        var child = parent.Fork(new(0, 0, 2, 2), "child");
        Assert.Same(budget, child.Budget); Assert.True(child.Budget.TryAttempt()); Assert.True(child.Budget.Spend(7));
        child.SetTerrain(0, "tree"); child.Reserve([0]); Assert.True(child.CommitToParent(out _));
        Assert.Equal("tree", parent.TerrainAt(0)); Assert.Equal("grass", board.TerrainAt(0)); Assert.Equal(0, board.Revision);
        var retry = Draft(board, budget: budget); Assert.True(retry.Budget.TryAttempt()); Assert.False(budget.TryAttempt());
        Assert.False(budget.Spend(3)); Assert.Equal(7, budget.Work); Assert.Equal(2, budget.Attempts);
    }

    [Fact]
    public void CommittedReservationsAndProtectedCellsRejectOtherOwners()
    {
        var board = new PlanningBoard(Scenario(), ContentBundle.Canonical(), new HashSet<int> { 11 });
        var first = Draft(board, "first"); first.Reserve([0, 1]); Assert.True(board.TryCommit(first.BuildPatch(), out _));
        var second = Draft(board, "second");
        Assert.Throws<ArgumentException>(() => second.SetTerrain(0, "tree"));
        Assert.Throws<ArgumentException>(() => second.SetEdge(new(1, EdgeDirection.Down), EdgeKind.Wall));
        Assert.Throws<ArgumentException>(() => second.SetTerrain(11, "tree"));
        Draft(board, "first").SetTerrain(0, "tree");
    }

    [Fact]
    public void InvalidWaterWallCommitIsAtomicAndPreparedFloorAllowsWall()
    {
        var board = Board(Scenario() with { Board = Scenario().Board with { DefaultTerrain = TerrainKind.Water } });
        var invalid = Draft(board); invalid.SetTerrain(4, "grass"); invalid.SetEdge(new(0, EdgeDirection.Right), EdgeKind.Wall);
        Assert.False(board.TryCommit(invalid.BuildPatch(), out var error)); Assert.NotNull(error);
        Assert.Equal("water", board.TerrainAt(4)); Assert.Equal(0, board.Revision);
        var valid = Draft(board); valid.SetTerrain(0, "stone-floor"); valid.SetTerrain(1, "stone-floor");
        valid.SetEdge(new(0, EdgeDirection.Right), EdgeKind.Wall);
        Assert.True(board.TryCommit(valid.BuildPatch(), out error), error); Assert.True(board.Passable(0));
    }

    [Fact]
    public void TypedLocalAssembliesValidateAndPersistWithoutChangingCanonicalContent()
    {
        var board = Board(); var draft = Draft(board);
        var assembly = new TerrainAssemblyDefinition("test-table", "Test Table", "grass") { Components = [new("table", "table")] };
        draft.AddContent(new() { Assemblies = [assembly] }); draft.AddContent(new() { Assemblies = [assembly] });
        draft.SetTerrain(0, assembly.Id); Assert.False(draft.Passable(0)); Assert.Equal("grass", board.TerrainAt(0));
        Assert.True(board.TryCommit(draft.BuildPatch(), out var error), error);
        Assert.Single(board.Export().TerrainAssemblies!); Assert.False(board.Passable(0));
        Assert.DoesNotContain(TerrainCatalog.All, t => t.Id == assembly.Id);
        _ = GameEngine.CreateGame(board.Export());
        var invalid = Draft(board);
        Assert.Throws<ArgumentException>(() => invalid.AddContent(new() { Terrain = [new("grass", "Changed", true, false)] }));
        Assert.Throws<ContentCompositionException>(() => invalid.AddContent(new() { Assemblies = [new("bad-table", "Bad", "missing-surface")] }));
        Assert.DoesNotContain(invalid.Content.Terrain, t => t.Id == "bad-table");
    }

    [Fact]
    public void EdgeOnlyCommitChangesRevisionAndEngineTopology()
    {
        var board = Board(); var draft = Draft(board); draft.SetEdge(new(0, EdgeDirection.Right), EdgeKind.Wall);
        Assert.True(board.TryCommit(draft.BuildPatch(), out _)); Assert.Equal(1, board.Revision);
        Assert.Empty(draft.BuildPatch().TerrainChanges); Assert.Single(draft.BuildPatch().EdgeChanges);
        var state = GameEngine.CreateGame(board.Export());
        Assert.False(Assert.Single(state.Physical.Board.Edges).Kind.Passable());
        Assert.Equal(new[] { 1, 4 }, board.Neighbors(0));
    }

    [Fact]
    public void RoutesShareMembershipWithoutReplacingTerrainOrCreatingHardReservations()
    {
        var board = Board(); var first = Draft(board, "road-a"); first.EnableRoutePlanning();
        first.SetTerrain(0, "stone-floor"); first.ReserveRoute([0, 1], "road-a");
        Assert.False(board.IsRouteCorridor(0));
        Assert.Equal(new[] { new RouteMembership(0, "road-a"), new RouteMembership(1, "road-a") }, first.BuildPatch().RouteMemberships);
        Assert.True(board.TryCommit(first.BuildPatch(), out var error), error);
        var second = Draft(board, "road-b"); second.EnableRoutePlanning();
        Assert.True(second.CanRouteThrough(0)); Assert.False(second.CanWrite(0));
        second.SetTerrain(0, "stone-floor"); second.ReplaceTerrain(0, "stone-floor");
        Assert.Throws<ArgumentException>(() => second.ReplaceTerrain(0, "grass"));
        Assert.Throws<ArgumentException>(() => second.SetTerrain(0, "tree"));
        second.ReserveRoute([0, 2], "road-b");
        Assert.Equal(new[] { "road-a" }, board.RouteMemberships(0));
        Assert.True(board.TryCommit(second.BuildPatch(), out error), error);
        Assert.Equal(new[] { "road-a", "road-b" }, board.RouteMemberships(0));
        Assert.False(board.IsReserved(0)); Assert.Equal("stone-floor", board.TerrainAt(0));
    }

    [Fact]
    public void CivilizationDraftCannotWriteOrReserveCorridorsEvenForNoOpWrites()
    {
        var board = Board(); var route = Draft(board); route.EnableRoutePlanning(); route.ReserveRoute([0, 1], "road");
        Assert.True(board.TryCommit(route.BuildPatch(), out _));
        var civilization = Draft(board, "civilization"); Assert.False(civilization.CanWrite(0));
        Assert.Throws<ArgumentException>(() => civilization.SetTerrain(0, "grass"));
        Assert.Throws<ArgumentException>(() => civilization.ReplaceTerrain(0, "grass"));
        Assert.Throws<ArgumentException>(() => civilization.Reserve([2, 0]));
        Assert.False(civilization.IsReserved(2));
        Assert.Throws<ArgumentException>(() => civilization.SetEdge(new(0, EdgeDirection.Right), EdgeKind.None));
        var nextRoute = Draft(board); nextRoute.EnableRoutePlanning();
        Assert.Throws<ArgumentException>(() => nextRoute.ReplaceEdge(new(0, EdgeDirection.Right), EdgeKind.Wall));
        nextRoute.SetEdge(new(0, EdgeDirection.Right), EdgeKind.OpenDoor);
    }

    [Fact]
    public void ExactSiteGrantsSeparateTraversalTerrainAndEdgeAccessAndPreserveOwners()
    {
        var board = Board(); var site = Draft(board, "site"); site.Reserve([0, 1]);
        Assert.True(board.TryCommit(site.BuildPatch(), out _));
        var key = new EdgeKey(0, EdgeDirection.Right); var route = Draft(board, "road");
        route.EnableRoutePlanning([new("site", [0], [], [key])]);
        Assert.True(route.CanRouteThrough(0)); Assert.False(route.CanRouteThrough(1));
        Assert.Throws<ArgumentException>(() => route.SetTerrain(0, "grass"));
        Assert.Throws<ArgumentException>(() => route.SetEdge(key, EdgeKind.OpenDoor));
        Assert.Throws<ArgumentException>(() => route.ReserveRoute([2, 1], "road"));
        Assert.False(route.IsRouteCorridor(2));
        route.EnableRoutePlanning([new("site", [0, 1], [0], [key])]);
        route.SetTerrain(0, "stone-floor"); route.SetEdge(key, EdgeKind.OpenDoor);
        Assert.Throws<ArgumentException>(() => route.SetTerrain(1, "stone-floor"));
        Assert.Throws<ArgumentException>(() => route.Reserve([0]));
        route.ReserveRoute([0, 1, 2], "road");
        Assert.True(board.TryCommit(route.BuildPatch(), out var error), error);
        Assert.False(board.IsReserved(0, "site")); Assert.True(board.IsReserved(0, "road"));
        var fresh = Draft(board, "road"); fresh.EnableRoutePlanning();
        Assert.False(fresh.CanRouteThrough(0)); Assert.False(fresh.CanWrite(0));
    }

    [Fact]
    public void EdgeGrantsRequireEachForeignEndpointOwnerAndExactEdge()
    {
        var board = Board(); var first = Draft(board, "site"); first.Reserve([0]); Assert.True(board.TryCommit(first.BuildPatch(), out _));
        var second = Draft(board, "site/building"); second.Reserve([1]); Assert.True(board.TryCommit(second.BuildPatch(), out _));
        var key = new EdgeKey(0, EdgeDirection.Right); var route = Draft(board, "road");
        Assert.Throws<ArgumentException>(() => route.EnableRoutePlanning([new("site", [0, 1], [], [key])]));
        route.EnableRoutePlanning([new("site", [0], [], [key]), new("site/building", [1], [], [])]);
        Assert.True(route.CanRouteThrough(1)); Assert.Throws<ArgumentException>(() => route.ReplaceEdge(key, EdgeKind.OpenDoor));
        route.EnableRoutePlanning([new("site", [0], [], [key]), new("site/building", [1], [], [key])]);
        route.ReplaceEdge(key, EdgeKind.OpenDoor);
        Assert.Throws<ArgumentException>(() => route.ReplaceEdge(new(0, EdgeDirection.Down), EdgeKind.OpenDoor));
    }

    [Fact]
    public void ProtectedCellsCannotBeGrantedAndGrantInputsAreDetached()
    {
        var board = new PlanningBoard(Scenario(), ContentBundle.Canonical(), new HashSet<int> { 11 });
        var site = Draft(board, "site"); site.Reserve([0]); Assert.True(board.TryCommit(site.BuildPatch(), out _));
        var route = Draft(board); Assert.Throws<ArgumentException>(() => route.EnableRoutePlanning([new("\0protected", [11], [], [])]));
        Assert.Throws<ArgumentException>(() => route.EnableRoutePlanning([new("site", [11], [], [])]));
        var traverse = new HashSet<int> { 0 }; route.EnableRoutePlanning([new("site", traverse, [], [])]);
        traverse.Add(11); traverse.Clear(); Assert.True(route.CanRouteThrough(0)); Assert.False(route.CanRouteThrough(11));
        var child = route.Fork(new(0, 0, 4, 3), "child"); Assert.True(child.CanRouteThrough(0));
    }

    [Fact]
    public void RouteMembershipsRemainPrivateAcrossChildRollbackInvalidCommitAndStaleClaims()
    {
        var board = Board(); var parent = Draft(board); parent.EnableRoutePlanning();
        var child = parent.Fork(new(0, 0, 4, 3), "child"); child.ReserveRoute([0], "child-road");
        Assert.True(child.CommitToParent(out _)); Assert.True(parent.IsRouteCorridor(0)); Assert.False(board.IsRouteCorridor(0));
        child.ReserveRoute([0, 1], "later-child-road"); Assert.False(parent.IsRouteCorridor(1));
        Assert.Equal(new[] { "child-road" }, parent.RouteMemberships(0));
        var staleChild = parent.Fork(new(0, 0, 4, 3), "stale-child"); staleChild.ReserveRoute([3], "stale-child-road");
        parent.ReserveRoute([4], "parent-road"); Assert.False(staleChild.CommitToParent(out _));
        Assert.False(parent.IsRouteCorridor(3));
        var stale = parent.BuildPatch();
        var winner = Draft(board); winner.EnableRoutePlanning(); winner.ReserveRoute([2], "winner");
        Assert.True(board.TryCommit(winner.BuildPatch(), out _)); Assert.False(board.TryCommit(stale, out _));
        Assert.False(board.IsRouteCorridor(0)); Assert.Equal(new[] { "winner" }, board.RouteMemberships(2));

        var water = Board(Scenario() with { Board = Scenario().Board with { DefaultTerrain = TerrainKind.Water } });
        var invalid = Draft(water); invalid.EnableRoutePlanning();
        invalid.SetTerrain(4, "grass"); invalid.ReserveRoute([4], "uncommitted");
        invalid.SetEdge(new(0, EdgeDirection.Right), EdgeKind.Wall);
        Assert.False(water.TryCommit(invalid.BuildPatch(), out _));
        Assert.False(water.IsRouteCorridor(4)); Assert.Equal("water", water.TerrainAt(4));
    }
}
