using ProjectDelve.Engine;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace ProjectDelve.Web;

// Only the runner touches executable state; the gate protects detached publication and reservations.
public sealed partial class PlaytestGame : IDisposable
{
    private readonly object gate = new();
    private readonly IRandomProvider random;
    private readonly DefaultAutomatedProvider automated = new();
    private readonly Func<ExternalDecisionContext, CancellationToken, Task<string?>>? external;
    private readonly Func<ExternalDecisionContext, CancellationToken, Task<ExternalDecisionSelection>>? externalSelection;
    private bool HasExternal => external is not null || externalSelection is not null;
    private readonly Channel<Work> work = Channel.CreateUnbounded<Work>(new() { SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task runner;
    private readonly Task readiness;
    private readonly Queue<GameUpdate> journal = new();
    private readonly HashSet<Channel<GameUpdate>> subscribers = [];
    private readonly Dictionary<string, (GameCommand Request, CommandAcceptance Ack)> commands = [];
    private readonly int capacity;
    private readonly Queue<string> commandOrder = new();
    private const int CommandRetention = 1024;
    private GameState state;
    private GameResponse stable;
    private GameUpdate published;
    private string scenarioId = PlaytestScenarios.DefaultId;
    private ScenarioDefinition definition = PlaytestScenarios.Definition(PlaytestScenarios.DefaultId);
    private string sessionId = Guid.NewGuid().ToString("N");
    private string? decisionId;
    private CancellationTokenSource? externalWait;
    private long revision, sequence;
    private bool autoChoose = true, busy, resetQueued, disposed;
    private string status = "idle";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    public PlaytestGame(IRandomProvider random) : this(random, PlaytestScenarios.Create(PlaytestScenarios.DefaultId)) { }
    internal PlaytestGame(IRandomProvider random, GameState initialState,
        Func<ExternalDecisionContext, CancellationToken, Task<string?>>? external = null, int replayCapacity = 256,
        Func<ExternalDecisionContext, CancellationToken, Task<ExternalDecisionSelection>>? externalSelection = null, IReadOnlyDictionary<string,string>? controllers = null, int historyLimit = 16384, int payloadLimit = 8 * 1024 * 1024)
    {
        this.historyLimit = historyLimit; this.payloadLimit = payloadLimit;
        this.random = random; this.external = external; this.externalSelection = externalSelection; capacity = Math.Max(1, replayCapacity);
        state = Detach(initialState); ApplyControllers(state, controllers); status = StatusFor(state);
        decisionId = state.Pending is null ? null : Guid.NewGuid().ToString("N");
        PublishGambitRequest();
        stable = Detach(Response(state, status == "waiting-human" ? state.Pending : null));
        published = new(sessionId, 0, "baseline", status, revision, decisionId, stable);
        runner = Task.Run(Run);
        readiness = Task.Run(MonitorControllerReadiness);
    }
    public GameResponse Snapshot()
    {
        GameResponse view;
        lock (gate) view = published.Snapshot with { ControllerStatus = CurrentControllerStatus };
        return Detach(view with { Result = new(view.Result.State, [], view.Result.NextInput), Commentaries = [] });
    }
    public GameResponse StartScenario(long revision, string id) => Legacy(new() { ExpectedRevision = revision, Operation = "scenario", ScenarioId = id });
    public GameResponse StartTransportScenario(long revision, string transport) => Legacy(new() { ExpectedRevision = revision, Operation = "scenario/import", Transport = transport });
    public GameResponse Restart(long revision) => Legacy(new() { ExpectedRevision = revision, Operation = "restart" });
    public GameResponse StartRound(long revision) => Legacy(new() { ExpectedRevision = revision, Operation = "round" });
    public GameResponse Decide(long revision, string? key, string? commentary = null) => Legacy(new() { ExpectedRevision = revision, Operation = "decision", CandidateKey = key, Commentary = commentary });
    public GameResponse SetRelevanceAutoChoice(long revision, bool enabled) => Legacy(new() { ExpectedRevision = revision, Operation = "preferences", AutoChooseSingleRelevantChoice = enabled });

    public CommandAcceptance Accept(GameCommand request)
    {
        request = Detach(request);
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(request.CommandId) || request.CommandId.Length > 128) throw Error(400, "Supply a command ID of at most 128 characters.");
            if (commands.TryGetValue(request.CommandId, out var prior))
            {
                if (!EquivalentCommand(prior.Request, request)) throw Error(409, "Command ID was already used with a different request.");
                return prior.Ack;
            }
            if (request.SessionId != sessionId) throw Error(409, "The game session changed. Refresh before choosing again.");
            var item = Reserve(request, false);
            var ack = new CommandAcceptance(request.CommandId, sessionId, true);
            commands.Add(request.CommandId, (request, ack));
            commandOrder.Enqueue(request.CommandId);
            while (commandOrder.Count > CommandRetention) commands.Remove(commandOrder.Dequeue());
            work.Writer.TryWrite(item);
            return ack;
        }
    }
    private static bool EquivalentCommand(GameCommand left, GameCommand right) =>
        (left with { Controllers = null }) == (right with { Controllers = null }) &&
        (left.Controllers is null ? right.Controllers is null : right.Controllers is not null &&
            left.Controllers.Count == right.Controllers.Count && left.Controllers.All(p => right.Controllers.GetValueOrDefault(p.Key) == p.Value));
    private GameResponse Legacy(GameCommand request)
    {
        Work item;
        lock (gate)
        {
            request = Detach(request with { SessionId = sessionId, DecisionId = decisionId });
            item = Reserve(request, true); work.Writer.TryWrite(item);
        }
        return item.Completion.Task.GetAwaiter().GetResult();
    }
    private Work Reserve(GameCommand request, bool legacy)
    {
        if (disposed) throw Error(503, "The game host is stopping.");
        if (request.ExpectedRevision != revision) throw Error(409, "The game changed. Refresh before choosing again.");
        var reset = request.Operation is "restart" or "scenario" or "scenario/import";
        if (historyOverflow && !reset) throw Error(409, "Controller history limit exceeded. Restart the match.");
        if (resetQueued || (busy && !reset)) throw Error(409, "The game is still resolving.");
        if (status == "faulted" && !reset) throw Error(409, "Restart the game after the resolution fault.");
        ScenarioDefinition? replacement = null;
        switch (request.Operation)
        {
            case "controller/retry":
                if (status != "controller-error" || state.IsCompleted || gambitRequest is null) throw Error(409, "No recoverable controller attempt is pending.");
                break;
            case "controller/cancel":
                if (status is not ("waiting-external" or "controller-error") || gambitRequest is null || state.IsCompleted) throw Error(409, "No controller attempt is pending.");
                break;
            case "round":
                if (stable.Result.State.IsCompleted) throw Error(409, "The game is complete. Restart or choose a scenario to play again.");
                if (stable.Result.State.Round != 0 && !stable.Result.State.RoundComplete) throw Error(409, "The current round is still active.");
                break;
            case "decision":
                if (status != "waiting-human" || stable.Result.NextInput is not { } pending) throw Error(409, "No player decision is pending.");
                if (!legacy && request.DecisionId != decisionId) throw Error(409, "The pending decision changed.");
                ValidateKey(pending, request.CandidateKey); NormalizeCommentary(request.Commentary); break;
            case "preferences":
                if (GambitSide is not null && request.AutoChooseSingleRelevantChoice == true) throw Error(400, "Gambit requires all legal alternatives to remain available.");
                if (request.AutoChooseSingleRelevantChoice is null) throw Error(400, "Supply the auto-choice preference.");
                if (status == "waiting-external") throw Error(409, "An external decision is pending.");
                break;
            case "scenario":
                if (!PlaytestScenarios.Catalog.Any(s => s.Id == request.ScenarioId)) throw Error(400, "Choose an available scenario.");
                replacement = PlaytestScenarios.Definition(request.ScenarioId!); break;
            case "scenario/import": replacement = Decode(request.Transport); break;
            case "restart": break;
            default: throw Error(400, "Choose a supported operation.");
        }
        if (reset)
        {
            var preview = GameEngine.CreateGame(replacement ?? definition);
            if (request.Controllers is not null)
            {
                var oldBindings = controllerBindings; var oldAuto = autoChoose;
                try { ApplyControllers(preview, request.Controllers); }
                finally { controllerBindings = oldBindings; autoChoose = oldAuto; }
            }
            gambitRequest = null; gambitAdmitted = false;
            resetQueued = true; decisionId = null;
            // Cancellation callbacks belong to external adapters and must run outside this gate.
            var waiting = externalWait; externalWait = null;
            if (waiting is not null) _ = waiting.CancelAsync().ContinueWith(_ => waiting.Dispose(), TaskScheduler.Default);
        }
        busy = true;
        return new(request, replacement);
    }
    private static ScenarioDefinition Decode(string? transport)
    {
        try { return ScenarioDefinitionTransport.Decode(transport ?? "", DesignerApi.CheckHostLimits); }
        catch (DesignerBoardLimitException e) { throw Error(400, e.Message); }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidDataException or JsonException)
        { throw Error(400, "Could not load scenario. Paste a valid DELVE1 scenario string."); }
    }
    private static void ValidateKey(DecisionRequest request, string? key)
    {
        if (key is null ? !request.AllowsNone : !request.Candidates.Any(c => c.Key == key))
            throw Error(400, "Choose one of the supplied candidates, or none when allowed.");
    }

    private async Task Run()
    {
        try
        {
            await foreach (var item in work.Reader.ReadAllAsync(lifetime.Token))
            {
                lock (gate) { if (disposed) { item.Completion.TrySetCanceled(); break; } }
                if (item.ExternalDecisionId is not null)
                    lock (gate)
                    {
                        if (item.Request.SessionId != sessionId || item.ExternalDecisionId != decisionId || resetQueued || status != "waiting-external")
                        { if (item.GambitSubmission is { } stale) FinishReceipt(stale, "rejected", "stale-request"); item.Completion.TrySetCanceled(); continue; }
                        if (item.GambitSubmission is { } bound && gambitRequest?.Binding != bound.Binding)
                        { FinishReceipt(bound, "rejected", "stale-request"); item.Completion.TrySetCanceled(); continue; }
                        busy = true;
                    }
                try { item.Completion.TrySetResult(Execute(item)); }
                catch (Exception error)
                {
                    lock (gate)
                    {
                        if (item.GambitSubmission is { } failed) FinishReceipt(failed, "rejected", "engine-fault");
                        state = Detach(stable.Result.State);
                        autoChoose = stable.AutoChooseSingleRelevantChoice;
                        status = "faulted"; decisionId = null; busy = resetQueued;
                        Publish("fault", Response(state, null), item.Request.CommandId, error.Message);
                    }
                    item.Completion.TrySetException(error);
                    _ = item.Completion.Task.Exception; // Accepted commands have no waiting HTTP response.
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { while (work.Reader.TryRead(out var item)) item.Completion.TrySetCanceled(); }
    }
    private GameResponse Execute(Work item)
    {
        var request = item.Request;
        if (request.Operation is "restart" or "scenario" or "scenario/import")
        {
            var replacement = item.Definition ?? definition;
            var created = GameEngine.CreateGame(replacement);
            lock (gate)
            {
                if (request.Controllers is null && request.Operation is "scenario" or "scenario/import") controllerBindings = [];
                ApplyControllers(created, request.Controllers);
                state = created; definition = replacement; committed.Clear(); acknowledgedCursor = historyOffset = 0; gambitRequest = null; controllerError = null; gambitAdmitted = false; historyOverflow = false;
                scenarioId = request.Operation == "scenario" ? request.ScenarioId! : request.Operation == "scenario/import" ? "imported" : scenarioId;
                sessionId = Guid.NewGuid().ToString("N"); sequence = 0; journal.Clear(); revision++;
                status = "idle"; decisionId = null; busy = resetQueued = false;
                stable = Detach(Response(state, null)); Publish("baseline", stable, request.CommandId);
                return Detach(stable);
            }
        }
        if (request.Operation is "controller/retry" or "controller/cancel")
        {
            lock (gate)
            {
                busy = false; gambitAdmitted = false;
                if (request.Operation == "controller/retry")
                {
                    status = "waiting-external"; controllerError = null;
                    gambitRequest = gambitRequest! with { Binding = gambitRequest.Binding with { AttemptId = Guid.NewGuid().ToString("N") } };
                }
                else { PauseController(); gambitRequest = gambitRequest! with { Binding = gambitRequest.Binding with { AttemptId = Guid.NewGuid().ToString("N") } }; }
                stable = Detach(Response(state, null)); Publish("boundary", stable, request.CommandId); return Detach(stable);
            }
        }
        var submittedDecisionId = request.DecisionId ?? decisionId ?? Guid.NewGuid().ToString("N");
        var commentaries = new List<DecisionCommentary>();
        var eventCount = 0;
        void Comment(DecisionRequest pending, string? key, string? text, string id, GameState current)
        {
            ValidateKey(pending, key);
            text = NormalizeCommentary(text);
            if (text is null) return;
            var unitId = pending.Kind == DecisionKind.SelectUnit ? key : pending.UnitId;
            var side = pending.Token?.SideId ?? current.Units.Single(u => u.Id == unitId).SideId;
            var comment = new DecisionCommentary(id, unitId, current.ControllerFor(pending), side, pending.Kind, key, text, eventCount);
            commentaries.Add(comment);
            lock (gate) Publish("commentary", Response(current, null), request.CommandId, commentary: comment with { EventIndex = 0 });
        }
        lock (gate) { status = "resolving"; decisionId = null; Publish("boundary", Response(state, null), request.CommandId); }
        void Observe(RulesEvent occurrence, ResolutionStep step)
        {
            eventCount++;
            var emission = new EngineResult(step.StateAfter, [occurrence], null) { ResolutionSteps = [step with { EventIndex = 0 }] };
            lock (gate) Publish("event", new(revision, emission, autoChoose, scenarioId), request.CommandId);
        }
        EngineResult result;
        if (request.Operation == "round") result = GameEngine.StartRound(state, random, autoChoose, Observe);
        else if (request.Operation == "preferences")
        {
            autoChoose = request.AutoChooseSingleRelevantChoice!.Value;
            result = GameEngine.RefreshChoices(state, random, autoChoose, Observe);
        }
        else
        {
            if (item.ExternalError is not null) throw item.ExternalError;
            ValidateKey(state.Pending!, request.CandidateKey);
            result = GameEngine.Advance(state, new CommentaryDecisionProvider(new SubmittedDecisionProvider(request.CandidateKey),
                (pending, key, queries) => Comment(pending, key, request.Commentary, submittedDecisionId, state)), random, autoChoose, Observe);
        }
        var events = new List<RulesEvent>(result.Events);
        var steps = new List<ResolutionStep>(result.ResolutionSteps);
        while (result.NextInput is { } pending && result.State.ControllerFor(pending) == ControllerKind.Automated)
        {
            lock (gate) { if (resetQueued || disposed) break; }
            if (IsGambit(pending))
            {
                var count = pending.Candidates.Count + (pending.AllowsNone ? 1 : 0);
                if (count == 0) throw new InvalidOperationException("No legal controller alternatives.");
                if (pending.Kind != DecisionKind.RollDice && count != 1) break;
                var forcedKey = pending.Candidates.FirstOrDefault()?.Key;
                result = GameEngine.Advance(result.State, new SubmittedDecisionProvider(forcedKey), random, false, Observe);
                steps.AddRange(result.ResolutionSteps.Select(s => s with { EventIndex = s.EventIndex + events.Count }));
                events.AddRange(result.Events); continue;
            }
            if (HasExternal) break;
            var decisionState = result.State;
            result = GameEngine.Advance(decisionState, new CommentaryDecisionProvider(automated,
                (choice, key, queries) => Comment(choice, key, AutomatedDecisionCommentary.For(choice, key, queries, decisionState),
                    Guid.NewGuid().ToString("N"), decisionState)), random, autoChoose, Observe);
            steps.AddRange(result.ResolutionSteps.Select(s => s with { EventIndex = s.EventIndex + events.Count }));
            events.AddRange(result.Events);
        }
        ExternalDecisionContext? context = null; CancellationToken cancellation = default;
        GameResponse response;
        lock (gate)
        {
            if (item.GambitSubmission is { } applied)
            { acknowledgedCursor = applied.Binding.CursorEnd;
                var acknowledged = (int)(acknowledgedCursor - historyOffset);
                committed.RemoveRange(0, acknowledged); historyOffset = acknowledgedCursor;
                FinishReceipt(applied, "applied"); }
            if (GambitSide is not null) committed.AddRange(Detach(events));
            state = result.State; revision++; status = resetQueued ? "resolving" : StatusFor(state);
            decisionId = state.Pending is null || resetQueued ? null : Guid.NewGuid().ToString("N");
            PublishGambitRequest();
            busy = resetQueued;
            stable = Detach(Response(state, status == "waiting-human" ? state.Pending : null));
            Publish("boundary", stable, request.CommandId);
            response = new(revision, result with { Events = events, ResolutionSteps = steps }, autoChoose, scenarioId) { Commentaries = commentaries };
            if (status == "waiting-external" && state.Pending is { } externalPending && !IsGambit(externalPending))
            {
                externalWait?.Dispose(); externalWait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                cancellation = externalWait.Token;
                context = new(sessionId, decisionId!, revision, Detach(state.Pending!), Detach(state));
            }
        }
        // Invocation itself may be synchronous/slow: it also runs outside the single writer.
        if (context is not null) _ = Task.Run(() => SelectExternal(context, cancellation, request.CommandId));
        return Detach(response with { SessionId = sessionId, Controllers = ControllersFor(state), ControllerStatus = stable.ControllerStatus });
    }
    private async Task SelectExternal(ExternalDecisionContext context, CancellationToken cancellation, string? commandId)
    {
        string? key = null, commentary = null; Exception? error = null;
        try
        {
            if (externalSelection is not null)
            {
                var selection = await externalSelection(context, cancellation);
                key = selection.CandidateKey; commentary = selection.Commentary;
            }
            else key = await external!(context, cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        catch (Exception caught) { error = caught; }
        lock (gate)
        {
            if (disposed || cancellation.IsCancellationRequested || context.SessionId != sessionId || context.DecisionId != decisionId || resetQueued) return;
            work.Writer.TryWrite(new(new GameCommand { Operation = "decision", SessionId = context.SessionId,
                ExpectedRevision = context.Revision, DecisionId = context.DecisionId, CandidateKey = key, Commentary = commentary, CommandId = commandId }, null)
                { ExternalDecisionId = context.DecisionId, ExternalError = error });
        }
    }
    private string StatusFor(GameState current) => current.IsCompleted ? "completed" : current.Pending is null ? "idle"
        : current.ControllerFor(current.Pending) == ControllerKind.Human ? "waiting-human"
        : UsesExternal(current.Pending) ? "waiting-external" : "resolving";
    private GameResponse Response(GameState current, DecisionRequest? pending) => new(revision, new(current, [], pending), autoChoose, scenarioId)
        { SessionId = sessionId, Controllers = ControllersFor(current), ControllerStatus = CurrentControllerStatus };
    private void Publish(string kind, GameResponse snapshot, string? commandId = null, string? error = null, DecisionCommentary? commentary = null)
    {
        lastPublishedAvailability = ControllerAvailable;
        snapshot = snapshot with { SessionId = sessionId, Controllers = ControllersFor(snapshot.Result.State), ControllerStatus = CurrentControllerStatus };
        published = new(sessionId, ++sequence, kind, status, revision, decisionId, Detach(snapshot), commandId, error, commentary);
        journal.Enqueue(published); while (journal.Count > capacity) journal.Dequeue();
        foreach (var subscriber in subscribers.ToArray())
            if (!subscriber.Writer.TryWrite(published)) { subscribers.Remove(subscriber); subscriber.Writer.TryComplete(); }
    }
    internal GameSubscription Subscribe(string? cursor)
    {
        lock (gate)
        {
            if (disposed) throw Error(503, "The game host is stopping.");
            var channel = Channel.CreateBounded<GameUpdate>(new BoundedChannelOptions(capacity + 1)
                { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
            var colon = cursor?.LastIndexOf(':') ?? -1; long after = -1;
            var retained = colon > 0 && cursor![..colon] == sessionId && long.TryParse(cursor[(colon + 1)..], out after)
                && after >= 0 && after <= sequence && (journal.Count == 0 ? after == sequence : after >= journal.Peek().Sequence - 1);
            if (retained) foreach (var update in journal.Where(u => u.Sequence > after)) channel.Writer.TryWrite(update);
            else
            {
                var view = published.Snapshot with { Result = new(published.Snapshot.Result.State, [], published.Snapshot.Result.NextInput), Commentaries = [] };
                channel.Writer.TryWrite(published with { Kind = "baseline", Snapshot = view, Commentary = null });
            }
            subscribers.Add(channel);
            return new(channel.Reader, () => { lock (gate) { subscribers.Remove(channel); channel.Writer.TryComplete(); } });
        }
    }
    internal static T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    private static PlaytestRequestException Error(int code, string message) => new(code, message);
    public void Dispose()
    {
        CancellationTokenSource? waiting;
        lock (gate)
        {
            if (disposed) return; disposed = true; waiting = externalWait; externalWait = null;
            work.Writer.TryComplete();
            foreach (var subscriber in subscribers) subscriber.Writer.TryComplete(); subscribers.Clear();
        }
        _ = lifetime.CancelAsync();
        if (waiting is not null) _ = waiting.CancelAsync().ContinueWith(_ => waiting.Dispose(), TaskScheduler.Default);
        _ = readiness;
        _ = runner; // Never await an uncooperative external controller during shutdown.
    }
    private sealed class Work(GameCommand request, ScenarioDefinition? definition)
    {
        public GameCommand Request { get; } = request;
        public ScenarioDefinition? Definition { get; } = definition;
        public string? ExternalDecisionId { get; init; }
        public Exception? ExternalError { get; init; }
        public GambitSubmission? GambitSubmission { get; init; }
        public TaskCompletionSource<GameResponse> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private static string? NormalizeCommentary(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > 280) throw Error(400, "Decision commentary must be at most 280 characters.");
        return text;
    }
    private sealed class CommentaryDecisionProvider(IDecisionProvider provider,
        Action<DecisionRequest, string?, IGameplayQueries> selected) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries)
        {
            var key = provider.Choose(request, queries);
            ValidateKey(request, key);
            selected(request, key, queries);
            return key;
        }
    }
    private sealed class SubmittedDecisionProvider(string? key) : IDecisionProvider
    { public string? Choose(DecisionRequest request, IGameplayQueries queries) => key; }
}
public sealed class PlaytestRequestException(int statusCode, string message) : Exception(message)
{ public int StatusCode { get; } = statusCode; }
