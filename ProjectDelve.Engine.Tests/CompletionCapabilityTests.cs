using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CompletionCapabilityTests
{
    [Fact]
    public void CampsitePublishesEveryUsableContactRatherThanOnlyPreferredPorts()
    {
        var board = Board(7, 5, (10, TerrainKind.Tree), (16, TerrainKind.Tree), (18, TerrainKind.Tree), (24, TerrainKind.Tree));
        Reserve(board, "camp", 17);
        var site = Site("camp", GeneratedSiteKind.Campsite, 7, [17]);
        var snapshot = CompletionCapabilities.Capture(board, [site], new HashSet<int> { 10, 16, 18, 24 }, Deployment(7));
        Assert.Null(snapshot.Error); Assert.True(snapshot.Complete);
        Assert.Equal(new[] { 10, 16, 18, 24 }, snapshot.Conversions.Select(c => c.Index));
        Assert.Equal(4, snapshot.Interfaces.Count(i => i.InsideIndex == 17));
        Assert.All(snapshot.Interfaces, i => Assert.Equal("camp", i.ExactOwnerId));
        Assert.DoesNotContain(17, snapshot.WritableCells);
        Assert.Empty(site.AccessPorts);
        var proof = ConstructionConnectivity.Prove(snapshot, [0], [17]);
        Assert.Equal(ConstructionProofStatus.Found, proof.Status);
        Assert.True(ConstructionConnectivity.TryPrepare(proof, board, new(), out var patch, out var error), error);
        Assert.All(patch!.TerrainChanges, c => Assert.Contains(c.Index, new[] { 10, 16, 18, 24 }));
        Assert.Empty(patch.Reservations);
    }

    [Fact]
    public void ExactPublishedApronIsWritableWhileDeploymentAndDungeonShellAreImmutable()
    {
        var board = Board(7, 5, (17, TerrainKind.SolidRock), (19, TerrainKind.Tree));
        Reserve(board, "dungeon", 17, 18, 19);
        Reserve(board, "hero-deployment", 0, 1);
        var site = Site("dungeon", GeneratedSiteKind.MountainDungeon, 7, [17, 18]) with
        {
            Exterior = Area("exterior", 7, 18),
            MountainDungeon = new(MountainDungeonProfile.TunnelNetwork, [], Area("corridors", 7, 18), Area("shell", 7, 17),
                Area("access", 7, 18), Area("lava", 7), [], 20, 17, 2),
            AccessPorts = [new("port", "dungeon", 18, 19, CardinalSide.East, Area("approach", 7, 18), 1,
                new("dungeon", [18, 19], [19, 17], [new(18, EdgeDirection.Right)]))]
        };
        var snapshot = CompletionCapabilities.Capture(board, [site], new HashSet<int> { 17, 19 }, Deployment(7));
        Assert.Null(snapshot.Error); Assert.True(snapshot.Complete);
        Assert.Equal(19, Assert.Single(snapshot.Conversions).Index);
        Assert.Contains(19, snapshot.WritableCells);
        Assert.DoesNotContain(17, snapshot.WritableCells);
        Assert.DoesNotContain(0, snapshot.WritableCells);
        Assert.DoesNotContain(1, snapshot.WritableCells);
        Assert.DoesNotContain(snapshot.Interfaces, i => i.InsideIndex == 17);
        Assert.Equal("solid-rock", snapshot.MaximalBoard.TerrainDefinitionAt(new(3, 2)).Id);
        Assert.Contains(snapshot.Interfaces, i => i.InsideIndex == 0 && i.OutsideIndex == 7 && i.ExactOwnerId == "hero-deployment");
    }

    [Fact]
    public void ExistingNorthSouthProtectedInteriorIsReadOnlyTransit()
    {
        var board = Board(1, 5);
        Reserve(board, "private-building", 1, 2, 3);
        var interior = Area("interior", 1, 1, 2, 3);
        var building = new BuildingInfo("private-building", "empty", interior,
            [new("room", "empty", interior, interior, [], 0)], [], new(3, 3, true, false, 0), []);
        var site = Site("settlement", GeneratedSiteKind.Settlement, 1, []) with { Buildings = [building] };
        var snapshot = CompletionCapabilities.Capture(board, [site], new HashSet<int>(), Deployment(1, 0, 4));
        Assert.Null(snapshot.Error); Assert.True(snapshot.Complete);
        Assert.All(new[] { 1, 2, 3 }, i => Assert.DoesNotContain(i, snapshot.WritableCells));
        var proof = ConstructionConnectivity.Prove(snapshot, [0], [4]);
        Assert.Equal(ConstructionProofStatus.Found, proof.Status);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, proof.Path);
        Assert.Empty(proof.Actions);
    }

    [Fact]
    public void NaturalProvenanceIsRequiredAndUnknownOwnerOpeningsRemainIncomplete()
    {
        var board = Board(7, 5, (17, TerrainKind.Tree), (19, TerrainKind.Tree));
        var noProvenance = CompletionCapabilities.Capture(board, [], new HashSet<int>(), Deployment(7));
        Assert.Null(noProvenance.Error); Assert.Empty(noProvenance.Conversions);
        Reserve(board, "unpublished-owner", 18);
        var unknown = CompletionCapabilities.Capture(board, [], new HashSet<int> { 17, 19 }, Deployment(7));
        Assert.Null(unknown.Error); Assert.False(unknown.Complete);
        Assert.Empty(unknown.Conversions);
        Assert.Empty(unknown.Interfaces);
        Assert.Equal("tree", board.TerrainAt(17));
    }

    [Fact]
    public void AcceptedBuildingPortalAllowsOnlyItsExactReadOnlyPreparationContact()
    {
        var board = Board(7, 5, (10, TerrainKind.SolidRock), (16, TerrainKind.Tree),
            (18, TerrainKind.SolidRock), (24, TerrainKind.SolidRock));
        var door = EdgeKey.Between(16, 17, 7);
        var draft = board.BeginDraft(new(0, 0, 7, 5), "house", new());
        draft.SetEdge(door, EdgeKind.ClosedDoor); draft.Reserve([17]);
        Assert.True(board.TryCommit(draft.BuildPatch(), out var error), error);
        var interior = Area("house", 7, 17);
        var portal = new SitePortal("house/door", "house", 17, 16, door, EdgeKind.ClosedDoor);
        var house = new BuildingInfo("house", "empty", interior,
            [new("room", "empty", interior, interior, [portal.Id], 0)], [portal], new(1, 1, true, false, 0), []);
        var site = Site("settlement", GeneratedSiteKind.Settlement, 7, []) with { Buildings = [house] };
        var snapshot = CompletionCapabilities.Capture(board, [site], new HashSet<int> { 16 }, Deployment(7));
        Assert.Null(snapshot.Error); Assert.True(snapshot.Complete);
        Assert.Equal(16, Assert.Single(snapshot.Conversions).Index);
        var contact = Assert.Single(snapshot.Interfaces);
        Assert.Equal((17, 16, "house"), (contact.InsideIndex, contact.OutsideIndex, contact.ExactOwnerId));
        Assert.DoesNotContain(17, snapshot.WritableCells);
        Assert.DoesNotContain(door, snapshot.WritableEdges);
        var proof = ConstructionConnectivity.Prove(snapshot, [0], [17]);
        Assert.Equal(ConstructionProofStatus.Found, proof.Status);
        Assert.Equal(16, Assert.Single(proof.Actions).Index);
        Assert.True(ConstructionConnectivity.TryPrepare(proof, board, new(), out var patch, out error), error);
        Assert.Equal(16, Assert.Single(patch!.TerrainChanges).Index);
        Assert.Empty(patch.EdgeChanges);
        Assert.Empty(patch.Reservations);
        Assert.Equal(EdgeKind.ClosedDoor, board.EdgeAt(door));
    }

    [Fact]
    public void UnknownReservationsWithoutAnyConversionContactCannotCertifyImpossibility()
    {
        var board = Board(1, 5, (2, TerrainKind.Tree));
        Reserve(board, "unpublished-owner", 4);
        var snapshot = CompletionCapabilities.Capture(board, [], new HashSet<int>(), Deployment(1));
        Assert.Null(snapshot.Error); Assert.False(snapshot.Complete);
        Assert.Equal(ConstructionProofStatus.Incomplete, ConstructionConnectivity.Prove(snapshot, [0], [4]).Status);
        var protectedBoard = new PlanningBoard(board.Export(), ContentBundle.Canonical(), new HashSet<int> { 4 });
        var immutable = CompletionCapabilities.Capture(protectedBoard, [], new HashSet<int>(), Deployment(1));
        Assert.Null(immutable.Error); Assert.True(immutable.Complete);
        Assert.Equal(ConstructionProofStatus.Impossible, ConstructionConnectivity.Prove(immutable, [0], [4]).Status);
    }

    [Fact]
    public void NonEntranceBuildingContactDoesNotPermitNaturalPreparation()
    {
        var board = Board(7, 5, (16, TerrainKind.Tree));
        Reserve(board, "house", 17);
        var interior = Area("house", 7, 17);
        var house = new BuildingInfo("house", "empty", interior, [], [], new(1, 1, true, false, 0), []);
        var site = Site("settlement", GeneratedSiteKind.Settlement, 7, []) with { Buildings = [house] };
        var snapshot = CompletionCapabilities.Capture(board, [site], new HashSet<int> { 16 }, Deployment(7));
        Assert.Null(snapshot.Error); Assert.True(snapshot.Complete);
        Assert.Empty(snapshot.Conversions);
        Assert.DoesNotContain(snapshot.Interfaces, i => i.InsideIndex == 17);
    }

    private static PlanningBoard Board(int width, int height, params (int Index, TerrainKind Kind)[] terrain) => new(new()
    {
        Board = new() { Width = width, Height = height, DefaultTerrain = TerrainKind.Grass,
            Cells = terrain.Select(t => new CellDefinition(new(t.Index % width, t.Index / width), t.Kind)).ToList() },
        UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
    }, ContentBundle.Canonical());
    private static void Reserve(PlanningBoard board, string owner, params int[] cells)
    {
        var draft = board.BeginDraft(new(0, 0, board.Width, board.Height), owner, new());
        draft.Reserve(cells);
        Assert.True(board.TryCommit(draft.BuildPatch(), out var error), error);
    }
    private static GeneratedArea Area(string id, int width, params int[] cells) => GeneratedArea.From(id, cells, width);
    private static HeroDeployment Deployment(int width, int inside = 0, int outside = 1) => new(1,
        Area("deployment", width, inside), Area("core", width, inside), [inside], inside, outside, 0);
    private static SiteInfo Site(string id, GeneratedSiteKind kind, int width, int[] footprint) => new(id, kind,
        Area(id + "/footprint", width, footprint), Area(id + "/exterior", width, footprint), [], [],
        new(footprint.Length, footprint.Length, true, false, 0), [], [], [], null, PreparationSummary.Empty);
}
