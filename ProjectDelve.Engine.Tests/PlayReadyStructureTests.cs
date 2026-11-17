using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PlayReadyStructureTests
{
    private static PlanningBoard Board() => new(new ScenarioDefinition
    {
        Board = new() { Width = 5, Height = 5, DefaultTerrain = TerrainKind.Grass },
        UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
    }, ContentBundle.Canonical());
    private static void Structure(PlanningBoard board, string owner, IEnumerable<int> rocks, IEnumerable<int>? passage = null)
    {
        var solid = rocks.ToArray(); var open = (passage ?? []).ToArray();
        var draft = board.BeginDraft(new(0, 0, 5, 5), owner, new());
        foreach (var i in solid) draft.SetTerrain(i, "solid-rock");
        draft.Reserve(solid.Concat(open));
        Assert.True(board.TryCommit(draft.BuildPatch(), out var error), error);
    }
    private static ConstructionProof Proof(PlanningBoard board) => ConstructionConnectivity.Prove(
        ConstructionCapabilities.Capture(board, "completion", [], conversionsComplete: true, interfacesComplete: true), [2], [22]);

    [Fact]
    public void ProtectedNorthSouthPassageCarriesTransitWithoutConstructionOwnership()
    {
        var board = Board(); Structure(board, "rock-gate", [10, 11, 13, 14], [12]);
        var proof = Proof(board);
        Assert.Equal(ConstructionProofStatus.Found, proof.Status); Assert.Contains(12, proof.Path);
        Assert.Empty(proof.Actions);
        Assert.True(ConstructionConnectivity.TryPrepare(proof, board, new(), out var patch, out _));
        Assert.Empty(patch!.TerrainChanges); Assert.Empty(patch.Reservations);
    }

    [Fact]
    public void AdjacentIndependentStructuresCanRetainALegalPassage()
    {
        var board = Board(); Structure(board, "west", [10, 11], [12]); Structure(board, "east", [13, 14]);
        var proof = Proof(board);
        Assert.Equal(ConstructionProofStatus.Found, proof.Status); Assert.Contains(12, proof.Path);
        Assert.Equal("west", board.State.Reservations[12]); Assert.Equal("east", board.State.Reservations[13]);
    }

    [Fact]
    public void TwoIndividuallyPassablePlacementsCanJointlyCloseTheOnlyConnection()
    {
        var board = Board(); Structure(board, "west", [10, 11, 12]);
        Assert.Equal(ConstructionProofStatus.Found, Proof(board).Status);
        Structure(board, "east", [13, 14]);
        var proof = Proof(board);
        Assert.Equal(ConstructionProofStatus.Impossible, proof.Status);
        Assert.Contains(proof.Frontier, f => f.OwnerId == "west");
        Assert.Contains(proof.Frontier, f => f.OwnerId == "east");
        var eastOnly = Board(); Structure(eastOnly, "east", [13, 14]);
        Assert.Equal(ConstructionProofStatus.Found, Proof(eastOnly).Status);
    }
}
