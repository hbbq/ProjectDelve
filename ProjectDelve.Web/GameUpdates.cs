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
    public bool? AutoChooseSingleRelevantChoice { get; init; }
}
public sealed record CommandAcceptance(string CommandId, string SessionId, bool Accepted);
public sealed record GameUpdate(string SessionId, long Sequence, string Kind, string Status,
    long Revision, string? DecisionId, GameResponse Snapshot, string? CommandId = null, string? Error = null,
    DecisionCommentary? Commentary = null);
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
