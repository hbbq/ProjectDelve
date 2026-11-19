using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed partial class PlaytestGame
{
    private EquipmentAllocation? equipmentDraft;
    private string? recruitmentDraft;

    public EquipmentStationPreviewResponse PreviewEquipmentStation(EquipmentStationPreviewRequest request)
    {
        ValidateAllocationShape(request.Allocation);
        GameState boundary;
        EquipmentAllocation? draft;
        string? recruit;
        lock (gate)
        {
            RequirePreviewBoundary(request.SessionId, request.ExpectedRevision, request.DecisionId);
            boundary = stable.Result.State;
            draft = equipmentDraft;
            recruit = recruitmentDraft;
        }
        var preview = GameEngine.PreviewEquipmentStation(boundary, request.Allocation ?? draft);
        lock (gate)
        {
            RequirePreviewBoundary(request.SessionId, request.ExpectedRevision, request.DecisionId);
            if (!ReferenceEquals(draft, equipmentDraft) || recruit != recruitmentDraft) throw Error(409, "The saved draft changed. Preview it again.");
            return new(sessionId, revision, decisionId!, preview, draft) { SavedRecruitTypeId = recruit };
        }
    }

    public RecruitmentPreviewResponse PreviewRecruitment(RecruitmentPreviewRequest request)
    {
        GameState boundary;
        lock (gate)
        {
            RequirePreviewBoundary(request.SessionId, request.ExpectedRevision, request.DecisionId);
            boundary = stable.Result.State;
            if (boundary.Party?.SideId != boundary.MachineInteraction!.SideId)
                throw Error(403, "Only the Expedition Party's Side can recruit.");
        }
        var preview = GameEngine.PreviewRecruitment(boundary, request.RecruitTypeId);
        lock (gate)
        {
            RequirePreviewBoundary(request.SessionId, request.ExpectedRevision, request.DecisionId);
            return new(sessionId, revision, decisionId!, preview);
        }
    }

    private void RequirePreviewBoundary(string requestedSessionId, long expectedRevision, string requestedDecisionId)
    {
        if (disposed) throw Error(503, "The game host is stopping.");
        if (requestedSessionId != sessionId || expectedRevision != revision)
            throw Error(409, "The game changed. Refresh before previewing again.");
        if (busy || resetQueued || expeditionOperationQueued)
            throw Error(409, "The game is still resolving.");
        RequireMachineDecision(requestedDecisionId);
        if (stable.Result.State.MachineInteraction!.EquipmentStation is null)
            throw Error(409, "An Equipment Station is required for that preview.");
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
        if (allocation is { SoldObjectIds.IsDefault: true }) throw Error(400, "Supply an array of sold physical object IDs.");
        if (allocation is { UpgradedUnitIds.IsDefault: true } or { UpgradedEquipmentIds.IsDefault: true })
            throw Error(400, "Supply arrays of Unit and Equipment IDs for upgrades.");
    }

    private static bool EquivalentAllocations(EquipmentAllocation? left, EquipmentAllocation? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.Assignments.OrderBy(a => a?.ObjectId, StringComparer.Ordinal).ThenBy(a => a?.UnitId, StringComparer.Ordinal)
            .SequenceEqual(right.Assignments.OrderBy(a => a?.ObjectId, StringComparer.Ordinal).ThenBy(a => a?.UnitId, StringComparer.Ordinal)) &&
            left.SoldObjectIds.Order(StringComparer.Ordinal).SequenceEqual(right.SoldObjectIds.Order(StringComparer.Ordinal)) &&
            left.UpgradedUnitIds.Order(StringComparer.Ordinal).SequenceEqual(right.UpgradedUnitIds.Order(StringComparer.Ordinal)) &&
            left.UpgradedEquipmentIds.Order(StringComparer.Ordinal).SequenceEqual(right.UpgradedEquipmentIds.Order(StringComparer.Ordinal));
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
            Publish("error", stable, item.Request.CommandId,
                item.Request.Operation switch
                {
                    "machine/recruit" => "The recruitment proposal is invalid.",
                    "machine/service" => string.Join(" ", error.Diagnostics.Select(d => d.Message)),
                    _ => "The proposed allocation is invalid."
                }, owned: true,
                equipmentDiagnostics: error.Diagnostics);
        }
    }
}
