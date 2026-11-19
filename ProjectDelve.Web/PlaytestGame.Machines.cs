using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed partial class PlaytestGame
{
    private EquipmentAllocation? equipmentDraft;

    public EquipmentStationPreviewResponse PreviewEquipmentStation(EquipmentStationPreviewRequest request)
    {
        ValidateAllocationShape(request.Allocation);
        GameState boundary;
        EquipmentAllocation? draft;
        lock (gate)
        {
            RequirePreviewBoundary(request);
            boundary = stable.Result.State;
            draft = equipmentDraft;
        }
        var preview = GameEngine.PreviewEquipmentStation(boundary, request.Allocation ?? draft);
        lock (gate)
        {
            RequirePreviewBoundary(request);
            if (!ReferenceEquals(draft, equipmentDraft)) throw Error(409, "The saved draft changed. Preview it again.");
            return new(sessionId, revision, decisionId!, preview, draft);
        }
    }

    private void RequirePreviewBoundary(EquipmentStationPreviewRequest request)
    {
        if (disposed) throw Error(503, "The game host is stopping.");
        if (request.SessionId != sessionId || request.ExpectedRevision != revision)
            throw Error(409, "The game changed. Refresh before previewing again.");
        if (busy || resetQueued || expeditionOperationQueued)
            throw Error(409, "The game is still resolving.");
        RequireMachineDecision(request.DecisionId);
    }

    // The browser represents the host's current viewing Side when FoW is enabled.
    // A full-information table also requires the pending actor's human agency.
    private void RequireMachineDecision(string? requestedDecision)
    {
        var boundary = stable.Result.State;
        if (requestedDecision is null || requestedDecision != decisionId ||
            status != "waiting-human" || stable.Result.NextInput is not { Kind: DecisionKind.MachineInteraction } pending ||
            boundary.MachineInteraction is not { } interaction)
            throw Error(409, "No matching player Machine interaction is pending.");
        if (boundary.ControllerFor(pending) != ControllerKind.Human || pending.Token?.SideId != interaction.SideId ||
            FogOfWar.ViewingSide(boundary) is { } side && side != interaction.SideId)
            throw Error(403, "This Machine interaction belongs to another Side or controller.");
    }

    private static void ValidateAllocationShape(EquipmentAllocation? allocation)
    {
        if (allocation is { Assignments.IsDefault: true }) throw Error(400, "Supply an array of Equipment and Item assignments.");
    }

    private static bool EquivalentAllocations(EquipmentAllocation? left, EquipmentAllocation? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.Assignments.OrderBy(a => a?.ObjectId, StringComparer.Ordinal).ThenBy(a => a?.UnitId, StringComparer.Ordinal)
            .SequenceEqual(right.Assignments.OrderBy(a => a?.ObjectId, StringComparer.Ordinal).ThenBy(a => a?.UnitId, StringComparer.Ordinal));
    }

    private void RecoverAllocationFailure(Work item, EquipmentAllocationException error)
    {
        RefreshExpeditionMetadata();
        lock (gate)
        {
            busy = resetQueued;
            status = resetQueued ? "resolving" : StatusFor(state);
            decisionId = resetQueued ? null : item.PriorDecisionId;
            stable = Detach(Response(state, status == "waiting-human" ? state.Pending : null));
            Publish("error", stable, item.Request.CommandId, "The proposed allocation is invalid.", owned: true,
                equipmentDiagnostics: error.Diagnostics);
        }
    }
}
