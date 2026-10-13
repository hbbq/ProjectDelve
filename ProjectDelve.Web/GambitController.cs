using ProjectDelve.Engine;
using System.Text.Json.Serialization;

namespace ProjectDelve.Web;

public sealed record GambitBinding(string SessionId, string BindingId, string SideId, string DecisionId,
    long Revision, string AttemptId, long CursorStart, long CursorEnd);
public sealed record GambitRequest(int SchemaVersion, GambitBinding Binding, SideObjective Objective,
    string Status, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GameCompletion? Completion, GambitDecision Request, GambitObservation Observation,
    IReadOnlyList<GambitEvent> Events)
{
    public string ObjectiveMeaning => "Eliminate Enemies: no living hostile Units remain on the board; ProjectDelve evaluates achievement at completed activation and fully resolved stable transition boundaries.";
}
public sealed record GambitStatus(int SchemaVersion, string SessionId, string BindingId, string? SideId,
    string Status, GameCompletion? Completion, GambitBinding? Request)
{
    public SideObjective? Objective { get; init; }
    public string ObjectiveMeaning => "Eliminate Enemies: no living hostile Units remain on the board; achievement is evaluated by ProjectDelve at completed activation and fully resolved stable transition boundaries.";
}
public sealed record GambitSubmission(string SubmissionId, GambitBinding Binding, string? CandidateKey, string? Commentary = null);
public sealed record GambitReceipt(string SubmissionId, string Status, string? Error = null);
public sealed record GambitError(GambitBinding Binding, string? Error = null);
public sealed record ControllerStatus(string BindingId, string? SideId, string Status, string? Error, bool Available)
{
    public bool CanRetry { get; init; }
    public bool CanCancel { get; init; }
}

public sealed partial class PlaytestGame
{
    public GameResponse SetupScenario(long revision, string id, Dictionary<string,string>? controllers) => Legacy(new() { ExpectedRevision = revision, Operation = "scenario", ScenarioId = id, Controllers = controllers });
    public GameResponse SetupImport(long revision, string transport, Dictionary<string,string>? controllers) => Legacy(new() { ExpectedRevision = revision, Operation = "scenario/import", Transport = transport, Controllers = controllers });
    public GameResponse SetupRestart(long revision, Dictionary<string,string>? controllers) => Legacy(new() { ExpectedRevision = revision, Operation = "restart", Controllers = controllers });
    private Dictionary<string, string> controllerBindings = [];
    private readonly List<RulesEvent> committed = [];
    private long acknowledgedCursor, historyOffset;
    private bool historyOverflow;
    private GambitRequest? gambitRequest;
    private bool gambitAdmitted;
    private string? controllerError;
    private DateTimeOffset lastControllerContact;
    private bool lastPublishedAvailability;
    private async Task MonitorControllerReadiness()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
                lock(gate)
                {
                    if (!disposed && GambitSide is not null && lastPublishedAvailability && !ControllerAvailable)
                        Publish("boundary", Response(state, status == "waiting-human" ? state.Pending : null));
                }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private bool RecoverableControllerAttempt => gambitRequest is not null && !busy && !resetQueued && !disposed && !state.IsCompleted && !historyOverflow;
    private ControllerStatus? CurrentControllerStatus => GambitSide is null ? null : new("gambit", GambitSide, status, controllerError, ControllerAvailable)
    {
        CanRetry = RecoverableControllerAttempt && status == "controller-error",
        CanCancel = RecoverableControllerAttempt && status is "waiting-external" or "controller-error"
    };
    private bool ControllerAvailable => DateTimeOffset.UtcNow - lastControllerContact < TimeSpan.FromSeconds(15);
    internal void ControllerContact()
    {
        lock(gate)
        {
            var wasAvailable = ControllerAvailable; lastControllerContact = DateTimeOffset.UtcNow;
            if (!wasAvailable) Publish("boundary", Response(state, status == "waiting-human" ? state.Pending : null));
        }
    }
    private readonly Dictionary<string, (GambitSubmission Submission, GambitReceipt Receipt)> receipts = [];
    private readonly Queue<string> receiptOrder = [];
    private readonly int historyLimit;
    private readonly int payloadLimit;
    private string? GambitSide => controllerBindings.FirstOrDefault(p => p.Value == "Gambit").Key;
    private bool IsGambit(DecisionRequest request) => request.Token is { } token && controllerBindings.GetValueOrDefault(token.SideId) == "Gambit";
    private bool UsesExternal(DecisionRequest request) => IsGambit(request) || HasExternal;
    private IReadOnlyDictionary<string, string> ControllersFor(GameState current) => controllerBindings.Count > 0 ? new Dictionary<string,string>(controllerBindings)
        : current.Controllers.GroupBy(c => c.Token.SideId).ToDictionary(g => g.Key, g => g.All(c => c.Controller == ControllerKind.Human) ? "Human" : "Default Behavior");
    private void ApplyControllers(GameState created, IReadOnlyDictionary<string,string>? supplied)
    {
        var assignments = supplied ?? (controllerBindings.Count == 0 ? null : controllerBindings);
        if (assignments is null) return;
        var sides = created.Objectives.Select(o => o.SideId).ToHashSet();
        if (assignments.Count != sides.Count || assignments.Keys.Any(s => !sides.Contains(s)) || assignments.Values.Any(v => v is not ("Human" or "Default Behavior" or "Gambit")))
            throw Error(400, "Assign an available controller to every scenario Side.");
        if (assignments.Values.Count(v => v == "Gambit") > 1 || assignments.Values.Contains("Gambit") && (sides.Count != 2 || assignments.Values.Any(v => v == "Default Behavior")))
            throw Error(400, "Gambit supports one Side against Human controllers.");
        controllerBindings = assignments.ToDictionary(p => p.Key, p => p.Value);
        created.Controllers = created.Controllers.Select(c => c with { Controller = controllerBindings[c.Token.SideId] == "Human" ? ControllerKind.Human : ControllerKind.Automated }).ToList();
        if (GambitSide is not null) autoChoose = false;
    }
    private void PublishGambitRequest()
    {
        gambitRequest = null; gambitAdmitted = false;
        if (resetQueued || state.IsCompleted || GambitSide is null) return;
        if (historyOffset + committed.Count - acknowledgedCursor > historyLimit)
        { historyOverflow = true; status = "controller-error"; controllerError = "Controller history limit exceeded. Restart the match."; return; }
        if (state.Pending is not { } pending || !IsGambit(pending)) return;
        var observation = GambitProjection.Observation(state);
        var binding = new GambitBinding(sessionId, "gambit", pending.Token!.SideId, decisionId!, revision,
            Guid.NewGuid().ToString("N"), acknowledgedCursor, historyOffset + committed.Count);
        var candidate = new GambitRequest(1, binding, state.Objectives.Single(o => o.SideId == binding.SideId),
            "waiting-external", null, GambitProjection.Decision(pending), observation,
            committed.Skip((int)(acknowledgedCursor - historyOffset)).Select(GambitProjection.Event).ToArray());
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(candidate, Json).Length > payloadLimit)
        { historyOverflow = true; status = "controller-error"; controllerError = "Controller observation limit exceeded. Restart the match."; return; }
        gambitRequest = candidate; controllerError = null;
    }
    public GambitStatus ControllerState()
    {
        lock (gate) return new(1, sessionId, "gambit", GambitSide, status, Detach(state.Completion), gambitRequest?.Binding) { Objective = state.Objectives.FirstOrDefault(o => o.SideId == GambitSide) };
    }
    public GambitRequest? ControllerRequest()
    {
        lock (gate) return status == "waiting-external" && !busy && !resetQueued && !state.IsCompleted ? Detach(gambitRequest) : null;
    }
    private void CheckBinding(GambitBinding binding)
    {
        if (disposed || busy || resetQueued || state.IsCompleted || status != "waiting-external" || gambitRequest?.Binding != binding)
            throw Error(409, "The controller request is no longer available.");
    }
    public object AdmitController(GambitBinding binding)
    {
        lock (gate) { CheckBinding(binding); gambitAdmitted = true; return new { admitted = true, binding }; }
    }
    public GambitReceipt SubmitController(GambitSubmission submission)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(submission.SubmissionId) || submission.SubmissionId.Length > 256) throw Error(400, "Supply a valid submission ID.");
            if (receipts.TryGetValue(submission.SubmissionId, out var previous))
            {
                if (previous.Submission != submission) throw Error(409, "Submission ID already has a different payload.");
                return previous.Receipt;
            }
            try
            {
                CheckBinding(submission.Binding);
                if (!gambitAdmitted) throw Error(409, "Admit the controller request before submitting.");
                ValidateKey(state.Pending!, submission.CandidateKey); NormalizeCommentary(submission.Commentary);
            }
            catch (PlaytestRequestException error)
            {
                var rejected = new GambitReceipt(submission.SubmissionId, "rejected", error.StatusCode == 409 ? "stale-request" : "invalid-selection");
                RememberReceipt(submission, rejected);
                if (gambitRequest?.Binding == submission.Binding && !state.IsCompleted && !resetQueued) PauseController("invalid-decision");
                return rejected;
            }
            var receipt = new GambitReceipt(submission.SubmissionId, "queued"); RememberReceipt(submission, receipt);
            busy = true;
            work.Writer.TryWrite(new(new GameCommand { Operation = "decision", SessionId = submission.Binding.SessionId,
                ExpectedRevision = submission.Binding.Revision, DecisionId = submission.Binding.DecisionId,
                CandidateKey = submission.CandidateKey, Commentary = submission.Commentary }, null)
                { ExternalDecisionId = submission.Binding.DecisionId, GambitSubmission = submission });
            return receipt;
        }
    }
    private void RememberReceipt(GambitSubmission submission, GambitReceipt receipt)
    {
        receipts[submission.SubmissionId] = (submission, receipt); receiptOrder.Enqueue(submission.SubmissionId);
        while (receiptOrder.Count > CommandRetention) receipts.Remove(receiptOrder.Dequeue());
    }
    private void FinishReceipt(GambitSubmission submission, string outcome, string? error = null)
    {
        if (receipts.ContainsKey(submission.SubmissionId)) receipts[submission.SubmissionId] = (submission, new(submission.SubmissionId, outcome, error));
    }
    public GambitReceipt Receipt(string id) { lock (gate) return receipts.TryGetValue(id, out var prior) ? prior.Receipt : new(id, "unknown"); }
    private void PauseController(string reason = "controller-failure")
    {
        status = "controller-error";
        controllerError = reason switch
        {
            "model-unavailable" => "Gambit's model is unavailable or rate limited. Retry or cancel the controller attempt.",
            "invalid-decision" => "Gambit returned an invalid decision. Retry or cancel the controller attempt.",
            _ => "Gambit controller failed. Retry or cancel the controller attempt."
        };
        gambitAdmitted = false; busy = false;
        Publish("boundary", Response(state, null));
    }
    public object ReportControllerError(GambitError error)
    {
        lock (gate)
        {
            CheckBinding(error.Binding);
            PauseController(error.Error is "model-unavailable" or "invalid-decision" or "controller-failure"
                ? error.Error : "controller-failure");
            return new { accepted = true };
        }
    }
    private static GameState SanitizeState(GameState source)
    {
        var copy = Detach(source);
        if (copy.WorldDeck is not null) copy.WorldDeck.DrawPile = [];
        copy.Bag = copy.Bag.OrderBy(t => t.SideId).ThenBy(t => t.TypeId).ToList();
        return copy;
    }
    // Browser presentation includes deliberate decision commentary from every player.
    // Resident observations use GambitObservation's independent public-state allowlist.
    internal GameResponse BrowserResponse(GameResponse source)
    {
        var result = source.Result;
        return source with { Result = result with { State = SanitizeState(result.State),
            ResolutionSteps = result.ResolutionSteps.Select(s => s with { StateAfter = SanitizeState(s.StateAfter) }).ToList() },
            PresentationOverride = source.Presentation };
    }
    internal GameUpdate BrowserUpdate(GameUpdate source) => source with
    {
        Snapshot = BrowserResponse(source.Snapshot),
        Error = source.Error is null ? null : "Game resolution failed. Restart or retry as indicated."
    };
}
