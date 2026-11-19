using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record GameCommand
{
    public string? CommandId { get; init; }
    public string? SessionId { get; init; }
    public long ExpectedRevision { get; init; }
    public string? DecisionId { get; init; }
    public required string Operation { get; init; }
    public string? CandidateKey { get; init; }
    public string? Commentary { get; init; }
    public string? ScenarioId { get; init; }
    public string? Transport { get; init; }
    public string? GenerationCode { get; init; }
    public string? ExpeditionId { get; init; }
    public string? ExpeditionName { get; init; }
    public string? ExpeditionSeed { get; init; }
    public string? ExpeditionRecipe { get; init; }
    public string? TransitionId { get; init; }
    public EquipmentAllocation? EquipmentAllocation { get; init; }
    public Dictionary<string,string>? Controllers { get; init; }
    public List<ControllerSelection>? ControllerAssignments { get; init; }
    public bool? AutoChooseSingleRelevantChoice { get; init; }
}
public sealed record ControllerSelection(string TypeId, string SideId, string Controller, string? GroupId = null);
public sealed record CommandAcceptance(string CommandId, string SessionId, bool Accepted);
public sealed record GameUpdate(string SessionId, long Sequence, string Kind, string Status,
    long Revision, string? DecisionId, GameResponse Snapshot, string? CommandId = null, string? Error = null,
    DecisionCommentary? Commentary = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<EquipmentAllocationDiagnostic>? EquipmentDiagnostics { get; init; }
}
public sealed record EquipmentStationPreviewRequest(string SessionId, long ExpectedRevision, string DecisionId,
    EquipmentAllocation? Allocation = null);
public sealed record EquipmentStationPreviewResponse(string SessionId, long Revision, string DecisionId,
    EquipmentStationPreview Preview, EquipmentAllocation? SavedDraft);
// Presentation metadata, separate from rules events and game state.
public sealed record DecisionCommentary(string DecisionId, string? UnitId, ControllerKind Controller,
    string SideId, DecisionKind DecisionKind, string? CandidateKey, string Text, int EventIndex);
internal sealed record ExternalDecisionSelection(string? CandidateKey, string? Commentary = null);
// Host-only adapter seam; decisions remain validated by the engine.
internal sealed record ExternalDecisionContext(string SessionId, string DecisionId, long Revision,
    DecisionRequest Request, GameState State);
internal sealed class GameSubscription(ChannelReader<GameUpdate> reader, Action dispose) : IDisposable
{
    public ChannelReader<GameUpdate> Reader { get; } = reader;
    public void Dispose() => dispose();
}

// Public expedition status contains no hidden region geometry or generation plans.
public sealed record ExpeditionPlayView(string Id, string Name, string Seed, ExpeditionRegion Region,
    int CompletedRegions, DateTimeOffset SavedAt, int ExitCount, int ExitRequired,
    string? TransitionId, bool HasUnsavedChanges);
