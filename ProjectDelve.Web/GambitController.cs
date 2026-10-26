using ProjectDelve.Engine;
using System.Text.Json.Serialization;

namespace ProjectDelve.Web;

public sealed record GambitBinding(string SessionId, string BindingId, string SideId, string DecisionId,
    long Revision, string AttemptId, long CursorStart, long CursorEnd);
public sealed record GambitRequest(int SchemaVersion, GambitBinding Binding, SideObjective Objective,
    string Status, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GameCompletion? Completion, GambitDecision Request, GambitObservation Observation,
    IReadOnlyList<GambitEvent> Events, IReadOnlyList<DefaultBehaviorPreview> DefaultBehaviorPreviews)
{
    public string ObjectiveMeaning => Objective is null ? "" : $"{ObjectivePresentation.Name(Objective)}: {ObjectivePresentation.Meaning(Objective)} ProjectDelve evaluates achievement at completed activation and fully resolved stable transition boundaries.";
}
public sealed record GambitStatus(int SchemaVersion, string SessionId, string BindingId, string? SideId,
    string Status, GameCompletion? Completion, GambitBinding? Request)
{
    public SideObjective? Objective { get; init; }
    public string ObjectiveMeaning => Objective is null ? "" : $"{ObjectivePresentation.Name(Objective)}: {ObjectivePresentation.Meaning(Objective)} ProjectDelve evaluates achievement at completed activation and fully resolved stable transition boundaries.";
}
public enum GambitActivationControl { Direct, Delegate }
public sealed record GambitSubmission(string SubmissionId, GambitBinding Binding, string? CandidateKey,
    string? Commentary = null, GambitActivationControl ActivationControl = GambitActivationControl.Direct);
public sealed record GambitReceipt(string SubmissionId, string Status, string? Error = null);
public sealed record GambitError(GambitBinding Binding, string? Error = null);
public sealed record ControllerStatus(string BindingId, string? SideId, string Status, string? Error, bool Available)
{
    public bool CanRetry { get; init; }
    public bool CanCancel { get; init; }
}

public sealed partial class PlaytestGame
{
    public GameResponse SetupScenario(long revision, string id, Dictionary<string,string>? controllers, List<ControllerSelection>? assignments = null) => Legacy(new() { ExpectedRevision = revision, Operation = "scenario", ScenarioId = id, Controllers = controllers, ControllerAssignments = assignments });
    public GameResponse SetupImport(long revision, string transport, Dictionary<string,string>? controllers, List<ControllerSelection>? assignments = null) => Legacy(new() { ExpectedRevision = revision, Operation = "scenario/import", Transport = transport, Controllers = controllers, ControllerAssignments = assignments });
    public GameResponse SetupRestart(long revision, Dictionary<string,string>? controllers, List<ControllerSelection>? assignments = null) => Legacy(new() { ExpectedRevision = revision, Operation = "restart", Controllers = controllers, ControllerAssignments = assignments });
    private Dictionary<ActivationToken, string> controllerBindings = [];
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
    private DelegatedActivation? delegatedActivation;
    private sealed record DelegatedActivation(string SessionId, string UnitId, ActivationToken Token);
    private string? GambitSide => controllerBindings.FirstOrDefault(p => p.Value == "Gambit").Key?.SideId;
    private bool IsGambit(DecisionRequest request) => request.Token is { } token && controllerBindings.GetValueOrDefault(token) == "Gambit";
    private bool IsDelegated(GameState current, DecisionRequest request) => delegatedActivation is { } delegation &&
        delegation.SessionId == sessionId && request.Token == delegation.Token && current.CurrentUnitId == delegation.UnitId &&
        (request.Roll is { } roll ? roll.OwnerUnitId == delegation.UnitId : request.UnitId == delegation.UnitId);
    private bool UsesExternal(GameState current, DecisionRequest request) => !IsDelegated(current, request) && (IsGambit(request) || HasExternal);
    private IReadOnlyDictionary<string, string> ControllersFor(GameState current) => ControllerAssignmentsFor(current)
        .GroupBy(c => c.SideId).ToDictionary(g => g.Key, g => g.Select(c => c.Controller).Distinct().Count() == 1 ? g.First().Controller : "Mixed");
    private IReadOnlyList<ControllerSelection> ControllerAssignmentsFor(GameState current) => current.Controllers
        .Select(c => new ControllerSelection(c.Token.TypeId, c.Token.SideId,
            controllerBindings.GetValueOrDefault(c.Token, c.Controller == ControllerKind.Human ? "Human" : "Default Behavior"))).ToArray();
    private void ApplyControllers(GameState created, IReadOnlyDictionary<string,string>? supplied, IReadOnlyList<ControllerSelection>? explicitAssignments = null)
    {
        if (supplied is not null && explicitAssignments is not null)
            throw Error(400, "Supply either Side controllers or Unit Type + Side controller assignments, not both.");
        var tokens = created.Controllers.Select(c => c.Token).ToArray();
        Dictionary<ActivationToken, string> assignments;
        if (explicitAssignments is not null)
        {
            assignments = [];
            foreach (var item in explicitAssignments)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.TypeId) || string.IsNullOrWhiteSpace(item.SideId) || string.IsNullOrWhiteSpace(item.Controller))
                    throw Error(400, "Controller assignments require Unit Type, Side and controller values.");
                var token = new ActivationToken(item.TypeId, item.SideId);
                if (!assignments.TryAdd(token, item.Controller))
                    throw Error(400, "Controller assignments must not contain duplicate Unit Type + Side entries.");
            }
            if (assignments.Count != tokens.Length || assignments.Keys.Any(token => !tokens.Contains(token)) || tokens.Any(token => !assignments.ContainsKey(token)))
                throw Error(400, "Assign an available controller to every authored Unit Type + Side.");
        }
        else if (supplied is not null)
        {
            var sides = tokens.Select(token => token.SideId).Distinct().ToArray();
            if (supplied.Count != sides.Length || supplied.Keys.Any(side => !sides.Contains(side)))
                throw Error(400, "Assign an available controller to every scenario Side.");
            assignments = tokens.ToDictionary(token => token, token => supplied[token.SideId]);
        }
        else if (controllerBindings.Count > 0 && tokens.All(controllerBindings.ContainsKey) && controllerBindings.Keys.All(tokens.Contains))
            assignments = new(controllerBindings);
        else
            assignments = created.Controllers.ToDictionary(c => c.Token, c => c.Controller == ControllerKind.Human ? "Human" : "Default Behavior");
        if (assignments.Values.Any(value => value is not ("Human" or "Default Behavior" or "Gambit")))
            throw Error(400, "Choose Human, Default Behavior or Gambit for every controller assignment.");
        var gambitSides = assignments.Where(pair => pair.Value == "Gambit").Select(pair => pair.Key.SideId).Distinct().ToArray();
        if (gambitSides.Length > 1)
            throw Error(400, "All Gambit controller assignments must belong to one Side.");
        controllerBindings = assignments;
        created.Controllers = created.Controllers.Select(c => c with { Controller = assignments[c.Token] == "Human" ? ControllerKind.Human : ControllerKind.Automated }).ToList();
        if (GambitSide is not null) autoChoose = false;
    }

    private void SynchronizeInheritedControllerBindings(GameState previous, EngineResult result)
    {
        var additions = result.State.Controllers.Where(c => !controllerBindings.ContainsKey(c.Token)).ToArray();
        if (additions.Length == 0) return;
        var updated = new Dictionary<ActivationToken, string>(controllerBindings);
        foreach (var assignment in additions)
        {
            var created = result.State.Units.FirstOrDefault(unit => unit.TypeId == assignment.Token.TypeId && unit.SideId == assignment.Token.SideId);
            var occurrence = created is null ? null : result.Events.LastOrDefault(e => e.Kind == "UnitCreated" && e.UnitId == created.Id);
            var source = occurrence?.SourceUnitId is null ? null : result.State.Units.FirstOrDefault(unit => unit.Id == occurrence.SourceUnitId)
                ?? previous.Units.FirstOrDefault(unit => unit.Id == occurrence.SourceUnitId);
            var inherited = source is null ? null : updated.GetValueOrDefault(new ActivationToken(source.TypeId, source.SideId));
            updated[assignment.Token] = inherited ?? (assignment.Controller == ControllerKind.Human ? "Human" : "Default Behavior");
        }
        controllerBindings = updated;
    }

    private void PublishGambitRequest()
    {
        gambitRequest = null; gambitAdmitted = false;
        if (resetQueued || state.IsCompleted || GambitSide is null) return;
        if (historyOffset + committed.Count - acknowledgedCursor > historyLimit)
        { historyOverflow = true; status = "controller-error"; controllerError = "Controller history limit exceeded. Restart the match."; return; }
        if (state.Pending is not { } pending || !IsGambit(pending) || IsDelegated(state, pending)) return;
        var observation = GambitProjection.Observation(state, GambitSide!, controllerBindings);
        var binding = new GambitBinding(sessionId, "gambit", pending.Token!.SideId, decisionId!, revision,
            Guid.NewGuid().ToString("N"), acknowledgedCursor, historyOffset + committed.Count);
        var previews = pending.Kind == DecisionKind.SelectUnit
            ? pending.Candidates.Where(choice => choice.Kind == ActivationChoiceKind.SelectUnit)
                .Select(PreviewDefaultBehavior).ToArray()
            : state.CurrentUnitId is { } activeUnit && pending.Kind != DecisionKind.RollDice &&
                pending.Candidates.Count + (pending.AllowsNone ? 1 : 0) > 1
                ? [PreviewDefaultBehavior(new(activeUnit, Kind: ActivationChoiceKind.SelectUnit))]
                : [];
        var candidate = new GambitRequest(3, binding, state.Objectives.Single(o => o.SideId == binding.SideId),
            "waiting-external", null, GambitProjection.Decision(pending), observation,
            committed.Skip((int)(acknowledgedCursor - historyOffset)).Select(GambitProjection.Event).ToArray(), previews);
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(candidate, GambitJson.Options).Length > payloadLimit)
        { historyOverflow = true; status = "controller-error"; controllerError = "Controller observation limit exceeded. Restart the match."; return; }
        gambitRequest = candidate; controllerError = null;
    }
    private DefaultBehaviorPreview PreviewDefaultBehavior(Candidate choice)
    {
        try { return GameEngine.PreviewDefaultBehaviorActivation(state, choice.Key); }
        catch (Exception) { return new(choice.Key, true, [], DefaultBehaviorPreviewStopReason.NoDecision); }
    }
    public GambitStatus ControllerState()
    {
        lock (gate) return new(3, sessionId, "gambit", GambitSide, status, Detach(state.Completion), gambitRequest?.Binding) { Objective = state.Objectives.FirstOrDefault(o => o.SideId == GambitSide) };
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
                var pending = state.Pending!;
                if (!Enum.IsDefined(submission.ActivationControl)) throw Error(400, "Choose Direct or Delegate activation control.");
                if (submission.ActivationControl == GambitActivationControl.Direct || pending.Kind == DecisionKind.SelectUnit)
                    ValidateKey(pending, submission.CandidateKey);
                else if (submission.CandidateKey is not null || pending.Kind == DecisionKind.RollDice ||
                    pending.UnitId is null || state.CurrentUnitId != pending.UnitId ||
                    pending.Candidates.Count + (pending.AllowsNone ? 1 : 0) <= 1)
                    throw Error(400, "Mid-activation delegation requires no candidate and a current multi-choice Unit decision.");
                NormalizeCommentary(submission.Commentary);
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
        var copy = FogOfWar.ViewingSide(source) is { } side ? FogOfWar.Observe(source, side) : Detach(source);
        if (copy.WorldDeck is not null) copy.WorldDeck.DrawPile = [];
        copy.Bag = copy.Bag.OrderBy(t => t.SideId).ThenBy(t => t.TypeId).ToList();
        return copy;
    }
    // Browser presentation includes deliberate decision commentary from every player.
    // Resident observations use GambitObservation's independent public-state allowlist.
    internal GameResponse BrowserResponse(GameResponse source)
    {
        var result = source.Result;
        var side = FogOfWar.ViewingSide(result.State);
        var presentation = source.Presentation;
        if (side is not null) result = BrowserProjection.Observe(result, side);
        var snapshots = source.Result.ResolutionSteps.ToDictionary(s => s.EventIndex, s => s.StateAfter);
        int ProjectedIndex(int original) => side is null ? original : source.Result.Events.Take(original).Select((e, index) =>
            FogOfWar.ObserveEvent(snapshots.GetValueOrDefault(index) ?? source.Result.State, side, e)).Count(e => e is not null);
        return source with { Result = result with { State = SanitizeState(result.State),
            ResolutionSteps = result.ResolutionSteps.Select(s => s with { StateAfter = SanitizeState(s.StateAfter) }).ToList() },
            // Host commentary was observed at decision time, before its first event.
            Commentaries = source.Commentaries.Select(c => c with { EventIndex = ProjectedIndex(c.EventIndex) }).ToArray(),
            Controllers = side is null ? source.Controllers : source.Controllers.Where(p => p.Key == side).ToDictionary(),
            ControllerAssignments = side is null ? source.ControllerAssignments : source.ControllerAssignments.Where(c => c.SideId == side).ToArray(),
            PresentationOverride = presentation };
    }
    internal GameUpdate BrowserUpdate(GameUpdate source) => source with
    {
        Snapshot = BrowserResponse(source.Snapshot),
        Commentary = FogOfWar.ViewingSide(source.Snapshot.Result.State) is null ? source.Commentary : source.Commentary is { UnitId: { } id } comment &&
            FogOfWar.IsUnitObservable(source.Snapshot.Result.State, FogOfWar.ViewingSide(source.Snapshot.Result.State), id) ? comment : null,
        Error = source.Error is null ? null : "Game resolution failed. Restart or retry as indicated."
    };
}
