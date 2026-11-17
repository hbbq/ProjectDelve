using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ConstructionCapabilityTests
{
    private static PlanningBoard Board(int width = 5, int height = 1, params (int Index, TerrainKind Kind)[] terrain)
    {
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = width, Height = height, DefaultTerrain = TerrainKind.Grass,
                Cells = terrain.Select(t => new CellDefinition(new(t.Index % width, t.Index / width), t.Kind)).ToList() },
            Units = [], UnitTypeIds = [], Agency = [], Loot = LootSettings.Empty
        };
        return new(scenario, ContentBundle.Canonical());
    }
    private static ConstructionCapabilities Capture(PlanningBoard board, params ConstructionConversion[] conversions) =>
        ConstructionCapabilities.Capture(board, "construction", conversions, conversionsComplete: true, interfacesComplete: true);
    private static ConstructionProof Proof(ConstructionCapabilities snapshot, int to = 4, int budget = 100000) =>
        ConstructionConnectivity.Prove(snapshot, [0], [to], budget);
    private static void Commit(PlanningBoard board, Action<BoardDraft> action, string owner = "site")
    {
        var draft = board.BeginDraft(new(0, 0, board.Width, board.Height), owner, new()); action(draft);
        Assert.True(board.TryCommit(draft.BuildPatch(), out var error), error);
    }

    [Fact]
    public void UnreservedTerrainDoesNotImplicitlyAuthorizeAConversion()
    {
        var board = Board(5, 1, (2, TerrainKind.Tree));
        var proof = Proof(Capture(board));
        Assert.Equal(ConstructionProofStatus.Impossible, proof.Status);
        Assert.Contains(proof.Frontier, b => b.ToIndex == 2 && b.Reason.Contains("declared conversion"));
        Assert.Equal("tree", board.TerrainAt(2));
    }

    [Fact]
    public void JointlyBlockingConversionsProduceDeterministicReplayableWitness()
    {
        var board = Board(5, 1, (1, TerrainKind.Tree), (2, TerrainKind.SolidRock), (3, TerrainKind.Water));
        var snapshot = Capture(board, new(3, "water", "stepping-stones"), new(2, "solid-rock", "stone-floor"), new(1, "tree", "grass"));
        var proof = Proof(snapshot);
        Assert.Equal(ConstructionProofStatus.Found, proof.Status);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, proof.Path);
        Assert.Equal(new[] { 1, 2, 3 }, proof.Actions.Select(a => a.Index));
        Assert.True(ConstructionConnectivity.TryPrepare(proof, board, new(), out var patch, out var error), error);
        Assert.Empty(patch!.EdgeChanges); Assert.Empty(patch.Reservations);
        Assert.True(board.TryCommit(patch, out error), error);
        Assert.Equal("stepping-stones", board.TerrainAt(3));
    }

    [Fact]
    public void MissingCompletenessOrWorkNeverProvesImpossible()
    {
        var board = Board(5, 1, (2, TerrainKind.Tree));
        Assert.Equal(ConstructionProofStatus.Incomplete, Proof(ConstructionCapabilities.Capture(board, "construction", [])).Status);
        Assert.Equal(ConstructionProofStatus.Incomplete, Proof(Capture(board), budget: 0).Status);
    }

    [Fact]
    public void IncompatibleAndUnsupportedDeclarationsAreConfigurationErrors()
    {
        var board = Board(5, 1, (2, TerrainKind.Tree));
        Assert.Equal(ConstructionProofStatus.Error, Proof(Capture(board, new(2, "tree", "grass"), new(2, "tree", "stone-floor"))).Status);
        Assert.Equal(ConstructionProofStatus.Error, Proof(Capture(board, new ConstructionConversion(2, "tree", "water"))).Status);
        Assert.Equal(ConstructionProofStatus.Error, Proof(Capture(board, new ConstructionConversion(2, "solid-rock", "grass"))).Status);
    }

    [Fact]
    public void StaleSnapshotsAndWitnessesCannotPrepareOrOverwriteNewerGeometry()
    {
        var board = Board(5, 1, (2, TerrainKind.Tree)); var snapshot = Capture(board, new ConstructionConversion(2, "tree", "grass"));
        var witness = Proof(snapshot);
        Commit(board, d => d.SetTerrain(0, "stone-floor"));
        Assert.Equal(ConstructionProofStatus.Error, Proof(snapshot).Status);
        Assert.False(ConstructionConnectivity.TryPrepare(witness, board, new(), out _, out var error));
        Assert.Contains("Stale", error); Assert.Equal("tree", board.TerrainAt(2));
    }

    [Fact]
    public void CapturesDetachDeclarationsGrantsAndReturnedBoards()
    {
        var board = Board(5, 1, (2, TerrainKind.Tree)); var declarations = new List<ConstructionConversion> { new(2, "tree", "grass") };
        var snapshot = ConstructionCapabilities.Capture(board, "construction", declarations, conversionsComplete: true, interfacesComplete: true);
        declarations.Clear(); snapshot.MaximalBoard.Terrain.Clear(); snapshot.CurrentBoard.Edges.Add(new(new(0, 0), new(1, 0), EdgeKind.Wall));
        Assert.Equal(ConstructionProofStatus.Found, Proof(snapshot).Status);
        Assert.Equal("tree", snapshot.TerrainAt(2)); Assert.Equal("tree", board.TerrainAt(2));
    }

    [Fact]
    public void ReadOnlyProtectedInteriorIsTransitWithoutRouteConstructionPermission()
    {
        var board = Board(1, 5); Commit(board, d => d.Reserve([1, 2, 3]), "private-interior");
        var snapshot = Capture(board); var proof = Proof(snapshot);
        Assert.Equal(ConstructionProofStatus.Found, proof.Status); Assert.Empty(proof.Actions);
        Assert.DoesNotContain(2, snapshot.WritableCells);
        Assert.True(ConstructionConnectivity.TryPrepare(proof, board, new(), out var patch, out _));
        Assert.Empty(patch!.Reservations); Assert.Empty(patch.TerrainChanges);
    }

    [Fact]
    public void ExactOwnerGrantDoesNotMatchPrefixAndProtectsPrivateTerrain()
    {
        var board = Board(5, 1, (2, TerrainKind.Tree)); Commit(board, d => d.Reserve([2]), "site/room");
        Assert.Equal(ConstructionProofStatus.Error, Proof(Capture(board, new ConstructionConversion(2, "tree", "grass"))).Status);
        var snapshot = ConstructionCapabilities.Capture(board, "construction", [], [new("site", [2], [2], [])],
            conversionsComplete: true, interfacesComplete: true);
        Assert.Equal(ConstructionProofStatus.Error, Proof(snapshot).Status);
        Assert.Equal("site/room", Capture(board).ReservationOwner(2));
    }

    [Fact]
    public void ConversionsCannotCreateAnUnpublishedSiteBoundaryOpening()
    {
        var board = Board(3, 1, (1, TerrainKind.Tree)); Commit(board, d => d.Reserve([2]), "site");
        Assert.Equal(ConstructionProofStatus.Error, Proof(Capture(board, new ConstructionConversion(1, "tree", "grass")), 2).Status);
        var snapshot = ConstructionCapabilities.Capture(board, "construction", [new(1, "tree", "grass")],
            [new("site", [2], [], [])], [new("west", 2, 1, "site")], true, true);
        Assert.Equal(ConstructionProofStatus.Found, Proof(snapshot, 2).Status);
        Assert.DoesNotContain(2, snapshot.WritableCells);
    }

    [Fact]
    public void StructuralWallsRemainImmutableEvenWithAnEdgeGrant()
    {
        var board = Board(); Commit(board, d => { d.Reserve([2]); d.SetEdge(new(1, EdgeDirection.Right), EdgeKind.Wall); }, "site");
        var snapshot = ConstructionCapabilities.Capture(board, "construction", [],
            [new("site", [2], [], [new(1, EdgeDirection.Right)])], conversionsComplete: true, interfacesComplete: true);
        var proof = Proof(snapshot);
        Assert.Equal(ConstructionProofStatus.Impossible, proof.Status);
        Assert.DoesNotContain(new EdgeKey(1, EdgeDirection.Right), snapshot.WritableEdges);
        Assert.Contains(proof.Frontier, f => f.Reason == "Immutable structural Edge");
    }

    [Fact]
    public void NormallyClosedDoorsArePotentialOnlyAndAreNeverOpenedByPreparation()
    {
        var board = Board(); Commit(board, d => d.SetEdge(new(1, EdgeDirection.Right), EdgeKind.ClosedDoor));
        var snapshot = Capture(board); var proof = Proof(snapshot);
        Assert.Equal(ConstructionProofStatus.Found, proof.Status);
        Assert.Equal(EdgeKind.ClosedDoor, snapshot.MaximalBoard.Edges.Single().Kind);
        Assert.True(ConstructionConnectivity.TryPrepare(proof, board, new(), out var patch, out _));
        Assert.Empty(patch!.EdgeChanges);
        Assert.Equal(EdgeKind.ClosedDoor, board.EdgeAt(new(1, EdgeDirection.Right)));
    }

    [Fact]
    public void MachinesBlockButFiguresDoNotChangeGeographicConnectivity()
    {
        var source = Board().Export();
        var machine = new PlanningBoard(source with { Machines = [new("press", new(2, 0), MachineTypes.DiamondPress)] }, ContentBundle.Canonical());
        var proof = Proof(Capture(machine));
        Assert.Equal(ConstructionProofStatus.Impossible, proof.Status);
        Assert.Contains(proof.Frontier, f => f.Reason == "Immutable Machine");
        Assert.DoesNotContain(2, Capture(machine).WritableCells);
        var figures = new PlanningBoard(source with
        {
            UnitTypeIds = [UnitTypeIds.Grunt], Units = [new(UnitTypeIds.Grunt, "red", new(2, 0))],
            Agency = [new(UnitTypeIds.Grunt, "red", ControllerKind.Human)]
        }, ContentBundle.Canonical());
        Assert.Equal(ConstructionProofStatus.Found, Proof(Capture(figures)).Status);
    }

    [Fact]
    public void InterfaceCannotInventADoorThroughAWallAndEmptyEndpointsAreErrors()
    {
        var board = Board(); Commit(board, d => { d.Reserve([2]); d.SetEdge(new(1, EdgeDirection.Right), EdgeKind.Wall); });
        var snapshot = ConstructionCapabilities.Capture(board, "construction", [],
            [new("site", [2], [], [])], [new("illegal-port", 2, 1, "site")], true, true);
        Assert.Equal(ConstructionProofStatus.Error, Proof(snapshot).Status);
        Assert.Equal(ConstructionProofStatus.Error, ConstructionConnectivity.Prove(Capture(board), [], [4]).Status);
        Assert.Equal(ConstructionProofStatus.Error, ConstructionConnectivity.Prove(Capture(board), [0], []).Status);
    }

    [Fact]
    public void ForgedWitnessCannotSkipBlockedSteps()
    {
        var board = Board(5, 1, (2, TerrainKind.Tree));
        var proof = Proof(Capture(board, new ConstructionConversion(2, "tree", "grass"))) with { Path = [0, 4], Actions = [] };
        Assert.False(ConstructionConnectivity.TryPrepare(proof, board, new(), out _, out var error));
        Assert.Contains("legal ordinary-land path", error);
        Assert.Equal("tree", board.TerrainAt(2));
    }

    private static SiteInfo SyntheticDungeon(int width, int shellIndex)
    {
        GeneratedArea Area(string id, params int[] cells) => GeneratedArea.From(id, cells, width);
        return new("dungeon", GeneratedSiteKind.MountainDungeon, Area("footprint", shellIndex), Area("exterior"),
            [], [], new(0, 0, true, false, 0), [], [], [], null, PreparationSummary.Empty)
        {
            MountainDungeon = new(MountainDungeonProfile.TunnelNetwork, [], Area("corridors"),
                Area("dungeon/protected-shell", shellIndex), Area("access"), Area("lava"), [], 1, shellIndex, 0)
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitDungeonShellProtectionSurvivesSameOwnerAndForeignWriteGrant(bool sameOwner)
    {
        var board = Board(5, 1, (2, TerrainKind.SolidRock)); Commit(board, d => d.Reserve([2]), "dungeon");
        var protections = ConstructionProtections.FromSites([SyntheticDungeon(board.Width, 2)], board.Width);
        var snapshot = ConstructionCapabilities.Capture(board, sameOwner ? "dungeon" : "construction",
            [new(2, "solid-rock", "stone-floor")], sameOwner ? [] : [new("dungeon", [2], [2], [])],
            conversionsComplete: true, interfacesComplete: true, protections: protections);
        var proof = Proof(snapshot);
        Assert.Equal(ConstructionProofStatus.Error, proof.Status);
        Assert.Contains("rock shell", proof.Detail); Assert.DoesNotContain(2, snapshot.WritableCells);
        Assert.DoesNotContain(new EdgeKey(1, EdgeDirection.Right), snapshot.WritableEdges);
        Assert.Equal("dungeon/protected-shell", snapshot.CellProtection(2)!.SourceId);
        Assert.Equal("solid-rock", board.TerrainAt(2));
    }

    [Fact]
    public void ImmutableProtectionRetainsReadOnlyTransitAndReportsBlockedSource()
    {
        var board = Board(5, 1, (2, TerrainKind.SolidRock));
        var protections = ConstructionProtections.FromSites([SyntheticDungeon(board.Width, 2)], board.Width);
        var snapshot = ConstructionCapabilities.Capture(board, "construction", [], conversionsComplete: true,
            interfacesComplete: true, protections: protections);
        var proof = Proof(snapshot);
        Assert.Equal(ConstructionProofStatus.Impossible, proof.Status);
        Assert.True(proof.RegionComplete); Assert.Equal(new[] { 0, 1 }, proof.Reachable);
        Assert.Contains(proof.Frontier, f => f.OwnerId == "dungeon" && f.SourceId == "dungeon/protected-shell");
        var floorBoard = Board();
        var floorProtection = new ConstructionProtections([new(2, "structure", "Protected floor", "construction")],
            [new(new(1, EdgeDirection.Right), "structure", "Protected interface", "construction")]);
        var floor = ConstructionCapabilities.Capture(floorBoard, "construction", [], protections: floorProtection);
        Assert.Equal(ConstructionProofStatus.Found, Proof(floor).Status);
        Assert.DoesNotContain(2, floor.WritableCells); Assert.DoesNotContain(new EdgeKey(1, EdgeDirection.Right), floor.WritableEdges);
    }

    [Fact]
    public void PositiveWorkBudgetCanReturnOnlyAConservativePartialRegion()
    {
        var snapshot = Capture(Board()); var partial = Proof(snapshot, budget: 1);
        Assert.Equal(ConstructionProofStatus.Incomplete, partial.Status); Assert.Equal(1, partial.WorkUsed);
        Assert.False(partial.RegionComplete); Assert.Equal(new[] { 0, 1 }, partial.Reachable);
        var witness = Proof(snapshot);
        Assert.Equal(ConstructionProofStatus.Found, witness.Status); Assert.False(witness.RegionComplete);
        var blocked = Capture(Board(3, 2, (1, TerrainKind.Tree)));
        var starts = new List<int> { 0 }; var goals = new List<int> { 5 };
        var frontier = ConstructionConnectivity.Prove(blocked, starts, goals, 1);
        starts.Clear(); goals.Clear();
        Assert.Equal(ConstructionProofStatus.Incomplete, frontier.Status);
        Assert.Contains(frontier.Frontier, f => f.FromIndex == 0 && f.ToIndex == 1);
        Assert.Equal(new[] { 0 }, frontier.Starts); Assert.Equal(new[] { 5 }, frontier.Goals);
    }

    [Fact]
    public void MutableGrantCollectionsAreDetachedBeforeWitnessReplay()
    {
        var board = Board(5, 1, (2, TerrainKind.Tree)); Commit(board, d => d.Reserve([1, 2, 3]), "site");
        var traverse = new List<int> { 2 }; var writable = new List<int> { 2 };
        var edges = new List<EdgeKey> { new(2, EdgeDirection.Right) };
        var snapshot = ConstructionCapabilities.Capture(board, "construction", [new(2, "tree", "grass")],
            [new("site", traverse, writable, edges)], conversionsComplete: true, interfacesComplete: true);
        traverse.Clear(); writable.Clear(); edges.Clear();
        Assert.Contains(2, snapshot.Grants.Single().TraverseCells);
        var proof = Proof(snapshot); Assert.Equal(ConstructionProofStatus.Found, proof.Status);
        Assert.True(ConstructionConnectivity.TryPrepare(proof, board, new(), out var patch, out var error), error);
        Assert.Single(patch!.TerrainChanges); Assert.Empty(patch.Reservations);
    }

    [Fact]
    public void WitnessCannotBePreparedAgainstAnUnrelatedBoardAtTheSameRevision()
    {
        var board = Board(5, 1, (2, TerrainKind.Tree)); var unrelated = Board(5, 1, (2, TerrainKind.Tree));
        var proof = Proof(Capture(board, new ConstructionConversion(2, "tree", "grass")));
        Assert.False(ConstructionConnectivity.TryPrepare(proof, unrelated, new(), out _, out var error));
        Assert.Contains("unrelated", error); Assert.Equal("tree", unrelated.TerrainAt(2));
    }
}
