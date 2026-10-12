using ProjectDelve.Engine;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace ProjectDelve.Web;

// Only the runner touches executable state; the gate protects detached publication and reservations.
public sealed class PlaytestGame : IDisposable
{
    private readonly object gate = new();
    private readonly IRandomProvider random;
    private readonly DefaultAutomatedProvider automated = new();
    private readonly Func<ExternalDecisionContext, CancellationToken, Task<string?>>? external;
    private readonly Channel<Work> work = Channel.CreateUnbounded<Work>(new() { SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task runner;
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
        Func<ExternalDecisionContext, CancellationToken, Task<string?>>? external = null, int replayCapacity = 256)
    {
        this.random = random; this.external = external; capacity = Math.Max(1, replayCapacity);
        state = Detach(initialState); status = StatusFor(state);
        decisionId = state.Pending is null ? null : Guid.NewGuid().ToString("N");
        stable = Detach(Response(state, status == "waiting-human" ? state.Pending : null));
        published = new(sessionId, 0, "baseline", status, revision, decisionId, stable);
        runner = Task.Run(Run);
    }
    public GameResponse Snapshot()
    {
        GameResponse view;
        lock (gate) view = published.Snapshot;
        return Detach(view with { Result = new(view.Result.State, [], view.Result.NextInput) });
    }
    public GameResponse StartScenario(long revision, string id) => Legacy(new() { ExpectedRevision = revision, Operation = "scenario", ScenarioId = id });
    public GameResponse StartTransportScenario(long revision, string transport) => Legacy(new() { ExpectedRevision = revision, Operation = "scenario/import", Transport = transport });
    public GameResponse Restart(long revision) => Legacy(new() { ExpectedRevision = revision, Operation = "restart" });
    public GameResponse StartRound(long revision) => Legacy(new() { ExpectedRevision = revision, Operation = "round" });
    public GameResponse Decide(long revision, string? key) => Legacy(new() { ExpectedRevision = revision, Operation = "decision", CandidateKey = key });
    public GameResponse SetRelevanceAutoChoice(long revision, bool enabled) => Legacy(new() { ExpectedRevision = revision, Operation = "preferences", AutoChooseSingleRelevantChoice = enabled });

    public CommandAcceptance Accept(GameCommand request)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(request.CommandId) || request.CommandId.Length > 128) throw Error(400, "Supply a command ID of at most 128 characters.");
            if (commands.TryGetValue(request.CommandId, out var prior))
            {
                if (prior.Request != request) throw Error(409, "Command ID was already used with a different request.");
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
    private GameResponse Legacy(GameCommand request)
    {
        Work item;
        lock (gate)
        {
            request = request with { SessionId = sessionId, DecisionId = decisionId };
            item = Reserve(request, true); work.Writer.TryWrite(item);
        }
        return item.Completion.Task.GetAwaiter().GetResult();
    }
    private Work Reserve(GameCommand request, bool legacy)
    {
        if (disposed) throw Error(503, "The game host is stopping.");
        if (request.ExpectedRevision != revision) throw Error(409, "The game changed. Refresh before choosing again.");
        var reset = request.Operation is "restart" or "scenario" or "scenario/import";
        if (resetQueued || (busy && !reset)) throw Error(409, "The game is still resolving.");
        if (status == "faulted" && !reset) throw Error(409, "Restart the game after the resolution fault.");
        ScenarioDefinition? replacement = null;
        switch (request.Operation)
        {
            case "round":
                if (stable.Result.State.Round != 0 && !stable.Result.State.RoundComplete) throw Error(409, "The current round is still active.");
                break;
            case "decision":
                if (status != "waiting-human" || stable.Result.NextInput is not { } pending) throw Error(409, "No player decision is pending.");
                if (!legacy && request.DecisionId != decisionId) throw Error(409, "The pending decision changed.");
                ValidateKey(pending, request.CandidateKey); break;
            case "preferences":
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
                        { item.Completion.TrySetCanceled(); continue; }
                        busy = true;
                    }
                try { item.Completion.TrySetResult(Execute(item)); }
                catch (Exception error)
                {
                    lock (gate)
                    {
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
                state = created; definition = replacement;
                scenarioId = request.Operation == "scenario" ? request.ScenarioId! : request.Operation == "scenario/import" ? "imported" : scenarioId;
                sessionId = Guid.NewGuid().ToString("N"); sequence = 0; journal.Clear(); revision++;
                status = "idle"; decisionId = null; busy = resetQueued = false;
                stable = Detach(Response(state, null)); Publish("baseline", stable, request.CommandId);
                return Detach(stable);
            }
        }
        lock (gate) { status = "resolving"; decisionId = null; Publish("boundary", Response(state, null), request.CommandId); }
        void Observe(RulesEvent occurrence, ResolutionStep step)
        {
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
            result = GameEngine.Advance(state, new SubmittedDecisionProvider(request.CandidateKey), random, autoChoose, Observe);
        }
        var events = new List<RulesEvent>(result.Events);
        var steps = new List<ResolutionStep>(result.ResolutionSteps);
        while (result.NextInput is { } pending && result.State.ControllerFor(pending) == ControllerKind.Automated)
        {
            lock (gate) { if (resetQueued || disposed) break; }
            if (external is not null) break;
            result = GameEngine.Advance(result.State, automated, random, autoChoose, Observe);
            steps.AddRange(result.ResolutionSteps.Select(s => s with { EventIndex = s.EventIndex + events.Count }));
            events.AddRange(result.Events);
        }
        ExternalDecisionContext? context = null; CancellationToken cancellation = default;
        GameResponse response;
        lock (gate)
        {
            state = result.State; revision++; status = resetQueued ? "resolving" : StatusFor(state);
            decisionId = state.Pending is null || resetQueued ? null : Guid.NewGuid().ToString("N");
            stable = Detach(Response(state, status == "waiting-human" ? state.Pending : null));
            busy = resetQueued; Publish("boundary", stable, request.CommandId);
            response = new(revision, result with { Events = events, ResolutionSteps = steps }, autoChoose, scenarioId);
            if (status == "waiting-external")
            {
                externalWait?.Dispose(); externalWait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                cancellation = externalWait.Token;
                context = new(sessionId, decisionId!, revision, Detach(state.Pending!), Detach(state));
            }
        }
        // Invocation itself may be synchronous/slow: it also runs outside the single writer.
        if (context is not null) _ = Task.Run(() => SelectExternal(context, cancellation, request.CommandId));
        return Detach(response);
    }
    private async Task SelectExternal(ExternalDecisionContext context, CancellationToken cancellation, string? commandId)
    {
        string? key = null; Exception? error = null;
        try { key = await external!(context, cancellation); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        catch (Exception caught) { error = caught; }
        lock (gate)
        {
            if (disposed || cancellation.IsCancellationRequested || context.SessionId != sessionId || context.DecisionId != decisionId || resetQueued) return;
            work.Writer.TryWrite(new(new GameCommand { Operation = "decision", SessionId = context.SessionId,
                ExpectedRevision = context.Revision, DecisionId = context.DecisionId, CandidateKey = key, CommandId = commandId }, null)
                { ExternalDecisionId = context.DecisionId, ExternalError = error });
        }
    }
    private string StatusFor(GameState current) => current.Pending is null ? "idle"
        : current.ControllerFor(current.Pending) == ControllerKind.Human ? "waiting-human"
        : external is not null ? "waiting-external" : "resolving";
    private GameResponse Response(GameState current, DecisionRequest? pending) => new(revision, new(current, [], pending), autoChoose, scenarioId);
    private void Publish(string kind, GameResponse snapshot, string? commandId = null, string? error = null)
    {
        published = new(sessionId, ++sequence, kind, status, revision, decisionId, Detach(snapshot), commandId, error);
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
                var view = published.Snapshot with { Result = new(published.Snapshot.Result.State, [], published.Snapshot.Result.NextInput) };
                channel.Writer.TryWrite(published with { Kind = "baseline", Snapshot = view });
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
        _ = runner; // Never await an uncooperative external controller during shutdown.
    }
    private sealed class Work(GameCommand request, ScenarioDefinition? definition)
    {
        public GameCommand Request { get; } = request;
        public ScenarioDefinition? Definition { get; } = definition;
        public string? ExternalDecisionId { get; init; }
        public Exception? ExternalError { get; init; }
        public TaskCompletionSource<GameResponse> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class SubmittedDecisionProvider(string? key) : IDecisionProvider
    { public string? Choose(DecisionRequest request, IGameplayQueries queries) => key; }
}
public sealed class PlaytestRequestException(int statusCode, string message) : Exception(message)
{ public int StatusCode { get; } = statusCode; }
