using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed partial class PlaytestGame
{
    private readonly ExpeditionCatalog? expeditions;
    private readonly ILogger<PlaytestGame>? logger;
    private ExpeditionRunner? expedition;
    private bool expeditionOperationQueued;
    // Publication never acquires the Expedition runner's lock: Engine callbacks
    // already hold that lock when they publish event frames through the game gate.
    private ExpeditionPlayView? expeditionView;

    private void ValidateExpeditionCommand(GameCommand request)
    {
        if (expeditions is null) throw Error(503, "Expedition storage is unavailable.");
        if (request.Operation == "expedition/create")
        {
            try { ExpeditionCatalog.ValidateCreate(request.ExpeditionName, request.ExpeditionSeed, request.ExpeditionRecipe); }
            catch (ExpeditionInputException error) { throw Error(400, error.Message); }
        }
        if (request.Operation == "expedition/resume" &&
            (request.ExpeditionId is null || !ExpeditionStore.IsValidId(request.ExpeditionId)))
            throw Error(400, "Select a saved Expedition from the list.");
        if (request.Controllers is not null || request.ControllerAssignments is not null)
            throw Error(400, "Expeditions use their saved controller assignments.");
        if (request.Operation is "expedition/save" or "expedition/leave" or "expedition/continue" && expedition is null)
            throw Error(409, "Open an Expedition first.");
        if (request.Operation == "expedition/continue")
        {
            if (string.IsNullOrWhiteSpace(request.TransitionId)) throw Error(400, "Supply the completed region's transition ID.");
            // Continue validates current and previously committed transition IDs
            // on the runner, outside the publication gate.
        }
    }

    private GameResponse? ExecuteExpedition(Work item)
    {
        var request = item.Request;
        if (request.Operation == "expedition/save")
        {
            expedition!.Save();
            RefreshExpeditionMetadata();
            lock (gate)
            {
                busy = expeditionOperationQueued = false;
                stable = Detach(Response(state, status == "waiting-human" ? state.Pending : null));
                Publish("boundary", stable, request.CommandId, owned: true);
                return item.Legacy ? Detach(stable) : null;
            }
        }

        // Commit the source before opening/generating anything that could replace it.
        expedition?.Save();
        ExpeditionRunner? replacement;
        GameState created;
        ScenarioDefinition scenario;
        switch (request.Operation)
        {
            case "expedition/create":
                replacement = expeditions!.Create(request.ExpeditionName, request.ExpeditionSeed, request.ExpeditionRecipe);
                created = replacement.Snapshot(); scenario = replacement.Scenario;
                break;
            case "expedition/resume":
                replacement = expeditions!.Open(request.ExpeditionId);
                created = replacement.Snapshot(); scenario = replacement.Scenario;
                break;
            case "expedition/continue":
                replacement = expedition!;
                var source = replacement.Head.ActiveCheckpoint;
                replacement.Continue(request.TransitionId!);
                // A duplicate receipt acknowledges the original transition without
                // replacing the current session or replaying the successor setup.
                if (source == replacement.Head.ActiveCheckpoint)
                {
                    RefreshExpeditionMetadata();
                    lock (gate)
                    {
                        busy = expeditionOperationQueued = false;
                        stable = Detach(Response(state, status == "waiting-human" ? state.Pending : null));
                        Publish("boundary", stable, request.CommandId, owned: true);
                        return item.Legacy ? Detach(stable) : null;
                    }
                }
                created = replacement.Snapshot(); scenario = replacement.Scenario;
                break;
            case "expedition/leave":
                replacement = null;
                scenario = PlaytestScenarios.Definition(PlaytestScenarios.DefaultId);
                created = GameEngine.CreateGame(scenario, NewGameContent);
                break;
            default: throw new InvalidOperationException("Unsupported Expedition operation.");
        }

        var restoredAutoChoose = replacement?.AutoChoose ?? true;
        var replacementView = CreateExpeditionView(replacement);
        lock (gate)
        {
            var waiting = externalWait; externalWait = null;
            if (waiting is not null) _ = waiting.CancelAsync().ContinueWith(_ => waiting.Dispose(), TaskScheduler.Default);
            expedition = replacement; expeditionView = replacementView;
            controllerBindings = []; autoChoose = restoredAutoChoose;
            // Saved Engine controller assignments are restored exactly. Browser
            // controller adapters are session state and cannot rewrite those rules.
            ApplyControllers(created, null);
            state = created; definition = scenario;
            scenarioId = replacement is null ? PlaytestScenarios.DefaultId : "expedition";
            generationCode = null;
            visualSeed = replacementView is null ? BoardAppearance.DeriveSeed(scenarioId, created.Physical.Board)
                : FormattableString.Invariant($"expedition-board-v1:{replacementView.Seed}:{replacementView.Region.X},{replacementView.Region.Y}");
            committed.Clear(); acknowledgedCursor = historyOffset = 0;
            gambitRequest = null; controllerError = null; gambitAdmitted = false;
            delegatedActivation = null; historyOverflow = false;
            sessionId = Guid.NewGuid().ToString("N"); sequence = 0; journal.Clear(); revision++;
            status = StatusFor(state);
            decisionId = state.Pending is null ? null : Guid.NewGuid().ToString("N");
            busy = resetQueued = expeditionOperationQueued = false;
            PublishGambitRequest();
            stable = Detach(Response(state, status == "waiting-human" ? state.Pending : null));
            Publish("baseline", stable, request.CommandId, owned: true);
            ScheduleRestoredExternal(request.CommandId);
            ScheduleRestoredAutomation(request.CommandId);
            return item.Legacy ? Detach(stable) : null;
        }
    }

    private void RefreshExpeditionMetadata()
    {
        var view = CreateExpeditionView(expedition);
        lock (gate) expeditionView = view;
    }

    private static ExpeditionPlayView? CreateExpeditionView(ExpeditionRunner? owner)
    {
        if (owner is null) return null;
        var head = owner.Head;
        var plan = owner.Region;
        var canContinue = owner.CanContinue;
        var dirty = owner.HasUnsavedChanges;
        return new ExpeditionPlayView(head.Id, head.DisplayName ?? head.Id, $"0x{head.WorldSeed:X16}",
            plan.Region, head.Transitions.Length + (canContinue ? 1 : 0), head.SavedAtUtc ?? DateTimeOffset.UnixEpoch,
            0, 2, canContinue ? plan.TransitionId : null, dirty);
    }

    private ExpeditionPlayView? ExpeditionView(GameState current)
    {
        if (expeditionView is not { } view) return null;
        var objective = current.Objectives.First(o => o.SideId == ExpeditionParty.SideId);
        return view with { ExitCount = GameEngine.ObjectiveQualifiedCount(current, objective), ExitRequired = objective.Count };
    }

    private bool IsExpeditionFailure(Work item, Exception error) =>
        error is not (OperationCanceledException or OutOfMemoryException) &&
        (item.Request.Operation.StartsWith("expedition/", StringComparison.Ordinal) || error is ExpeditionOperationException ||
         expedition is not null && item.Request.Operation is "scenario" or "scenario/import");

    private void RecoverExpeditionFailure(Work item, Exception error)
    {
        ExpeditionDiagnostics.LogFailure(logger, LogLevel.Error, error, "Expedition command {Operation} failed.", item.Request.Operation);
        // A failed automatic terminal save still owns the newly resolved boundary
        // and RNG. Never roll that result back and repeat a consumed dice decision.
        if (expedition is not null) state = expedition.Snapshot();
        RefreshExpeditionMetadata();
        var message = error switch
        {
            ExpeditionInputException input => input.Message,
            ExpeditionOperationException failure => ExpeditionFailureMessage(failure, item.Request.Operation == "expedition/create"),
            _ => "The Expedition operation failed. Your current game is retained. Check storage and retry."
        };
        lock (gate)
        {
            var replacementStillQueued = resetQueued && (item.Request.Operation is "round" or "decision" or "preferences" || item.RestoredAutomation);
            busy = resetQueued = replacementStillQueued;
            expeditionOperationQueued = false;
            if (item.Request.Operation is "round" or "decision" or "preferences" || item.RestoredAutomation)
            {
                revision++;
                if (item.GambitSubmission is { } applied) FinishReceipt(applied, "applied");
            }
            status = replacementStillQueued ? "resolving" : StatusFor(state);
            decisionId = state.Pending is null || replacementStillQueued ? null : item.PriorDecisionId ?? Guid.NewGuid().ToString("N");
            PublishGambitRequest();
            stable = Detach(Response(state, status == "waiting-human" ? state.Pending : null));
            Publish("error", stable, item.Request.CommandId, message, owned: true);
            ScheduleRestoredExternal(item.Request.CommandId);
        }
    }

    private static string ExpeditionFailureMessage(ExpeditionOperationException failure, bool creating)
    {
        var region = creating ? "first" : "next";
        var retry = creating ? "Start new Expedition" : "Continue";
        var action = failure.Stage switch
        {
            ExpeditionFailureStage.SourceCheckpoint => "could not save the current region. Check the save location and retry Save",
            ExpeditionFailureStage.ContentRefresh or ExpeditionFailureStage.PartySetup => $"could not prepare compatible Content for the {region} region. Restore compatible Content and retry {retry}",
            ExpeditionFailureStage.RegionGeneration => $"could not generate the {region} region. Retry {retry} with the same seed",
            _ => "could not commit the saved region. Check the save location and retry the operation"
        };
        return $"Expedition {action}. Your current game is retained. World seed {ExpeditionCatalog.FormatSeed(failure.WorldSeed)}.";
    }

    // Publish the exact restored boundary first. Default automation resumes as a
    // separate queued resolution, without refreshing or changing the saved choice.
    private void ScheduleRestoredAutomation(string? commandId)
    {
        if (status != "resolving" || state.Pending is not { } pending ||
            state.ControllerFor(pending) != ControllerKind.Automated || UsesExternal(state, pending)) return;
        busy = true;
        work.Writer.TryWrite(new(new GameCommand { Operation = "automation/resume", SessionId = sessionId,
            ExpectedRevision = revision, DecisionId = decisionId, CommandId = commandId }, null)
            { RestoredAutomation = true, PriorDecisionId = decisionId });
    }

    // Resume consumes no Engine decisions or RNG. External adapters can be
    // contacted again only for the exact authoritative decision we restored.
    private void ScheduleRestoredExternal(string? commandId)
    {
        if (status != "waiting-external" || state.Pending is null || IsGambit(state.Pending)) return;
        var waiting = externalWait;
        if (waiting is not null) _ = waiting.CancelAsync().ContinueWith(_ => waiting.Dispose(), TaskScheduler.Default);
        externalWait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var cancellation = externalWait.Token;
        var context = new ExternalDecisionContext(sessionId, decisionId!, revision, Detach(state.Pending), Detach(state));
        _ = Task.Run(() => SelectExternal(context, cancellation, commandId));
    }
}
