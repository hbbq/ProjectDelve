using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using ProjectDelve.Web;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class GameUpdateTests
{
    private static GameState Setup(ControllerKind first = ControllerKind.Human)
    {
        var type = new UnitType("ordinary", 1, 1, 2, 1, 1);
        return new()
        {
            Physical = new(new(4, 1, []), [new("one", new(0, 0)), new("two", new(2, 0))]),
            Types = [type], Units = [type.CreateUnit("one", "amber"), type.CreateUnit("two", "violet")],
            Controllers = [new(new(type.Id, "amber"), first), new(new(type.Id, "violet"), ControllerKind.Human)]
        };
    }
    private sealed class Dice : IRandomProvider
    {
        public ManualResetEventSlim? DrawEntered, DrawRelease;
        public bool ThrowDraw, ThrowRoll;
        public int Draws, Rolls;
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag)
        {
            Interlocked.Increment(ref Draws); DrawEntered?.Set();
            if (DrawRelease is not null && !DrawRelease.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test draw barrier timed out.");
            if (ThrowDraw) throw new InvalidOperationException("test random failure");
            return bag[0];
        }
        public AttackFace RollAttackDie() { Interlocked.Increment(ref Rolls); if (ThrowRoll) throw new InvalidOperationException("test attack failure"); return AttackFace.Miss; }
        public DefenceFace RollDefenceDie() { Interlocked.Increment(ref Rolls); return DefenceFace.Miss; }
        public int RollD6() => 6;
    }
    private static async Task<GameUpdate> Next(GameSubscription subscription, Func<GameUpdate, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var update = await subscription.Reader.ReadAsync(timeout.Token);
            if (predicate(update)) return update;
        }
    }
    private static GameCommand Command(GameUpdate baseline, string operation, string? key = null) => new()
    {
        CommandId = Guid.NewGuid().ToString("N"), SessionId = baseline.SessionId, ExpectedRevision = baseline.Revision,
        DecisionId = baseline.DecisionId, Operation = operation, CandidateKey = key
    };

    [Fact]
    public async Task FirstEventAndSnapshotAreReadableWhileEngineCallIsBlocked()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var dice = new Dice { DrawEntered = entered, DrawRelease = release };
        using var game = new PlaytestGame(dice, Setup());
        using var updates = game.Subscribe(null);
        var baseline = await Next(updates, u => u.Kind == "baseline");
        var command = Command(baseline, "round");
        try
        {
            Assert.True(game.Accept(command).Accepted);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var emission = await Next(updates, u => u.Kind == "event");
            Assert.Equal("resolving", emission.Status);
            Assert.Single(emission.Snapshot.Result.Events); Assert.Single(emission.Snapshot.Result.ResolutionSteps);
            Assert.Equal(0, emission.Snapshot.Result.ResolutionSteps[0].EventIndex);
            Assert.Null(emission.Snapshot.Result.NextInput); Assert.Null(emission.Snapshot.Presentation.Decision);
            Assert.Equal(1, game.Snapshot().Result.State.Round);
            Assert.Equal(0, game.Snapshot().Revision); Assert.Empty(game.Snapshot().Result.Events);
            Assert.Equal(game.Accept(command), game.Accept(command));
            Assert.Equal(1, dice.Draws);
            Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Accept(command with { Operation = "preferences" })).StatusCode);
        }
        finally { release.Set(); }
        var boundary = await Next(updates, u => u.Status == "waiting-human");
        Assert.Equal(1, boundary.Revision); Assert.NotNull(boundary.DecisionId);
        Assert.Equal(command.CommandId, boundary.CommandId);
    }

    [Fact]
    public async Task ResetDuringEngineCallFinishesCallThenSkipsAutomationAndRotatesSession()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var dice = new Dice { DrawEntered = entered, DrawRelease = release };
        using var game = new PlaytestGame(dice, Setup(ControllerKind.Automated));
        using var updates = game.Subscribe(null);
        var baseline = await Next(updates, u => u.Kind == "baseline");
        var first = Command(baseline, "round");
        var reset = Command(baseline, "restart");
        try
        {
            game.Accept(first); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            game.Accept(reset);
        }
        finally { release.Set(); }
        var replacement = await Next(updates, u => u.Kind == "baseline" && u.SessionId != baseline.SessionId);
        Assert.Equal(reset.CommandId, replacement.CommandId); Assert.Equal(0, replacement.Snapshot.Result.State.Round);
        Assert.Equal(0, dice.Rolls); Assert.Equal(1, dice.Draws);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Accept(Command(baseline, "round"))).StatusCode);
        Assert.Equal(game.Accept(reset).CommandId, reset.CommandId);
    }

    [Fact]
    public async Task ReplayHasNoHandoffGapAndInvalidCursorsReceiveCurrentBaseline()
    {
        using var game = new PlaytestGame(new Dice(), Setup(), replayCapacity: 4);
        game.StartRound(0);
        using var live = game.Subscribe(null);
        var baseline = await Next(live, u => u.Kind == "baseline");
        var cursor = $"{baseline.SessionId}:{baseline.Sequence}";
        game.SetRelevanceAutoChoice(baseline.Revision, false);
        var first = await Next(live, _ => true);
        using var replay = game.Subscribe(cursor);
        var replayFirst = await Next(replay, _ => true);
        Assert.Equal(first.Sequence, replayFirst.Sequence);
        Assert.Equal(first.Kind, replayFirst.Kind);
        var replayBoundary = await Next(replay, u => u.Status == "waiting-human");
        var refreshed = game.SetRelevanceAutoChoice(replayBoundary.Revision, true);
        var liveBoundary = await Next(replay, u => u.Status == "waiting-human" && u.Revision == refreshed.Revision);
        Assert.True(liveBoundary.Sequence > replayBoundary.Sequence);
        Assert.NotEqual(baseline.DecisionId, replayBoundary.DecisionId);
        foreach (var invalid in new[] { "nonsense", $"{baseline.SessionId}:999999", $"{baseline.SessionId}:0", "old:2" })
        {
            using var recovered = game.Subscribe(invalid);
            var update = await Next(recovered, _ => true);
            Assert.Equal("baseline", update.Kind); Assert.Equal(refreshed.Revision, update.Revision);
            Assert.Empty(update.Snapshot.Result.Events);
        }
    }

    [Fact]
    public void UnconsumedSubscriberCannotDelayProgression()
    {
        using var game = new PlaytestGame(new Dice(), Setup(), replayCapacity: 2);
        using var slow = game.Subscribe(null);
        var completed = game.StartRound(0);
        Assert.Equal(1, completed.Revision);
        Assert.Equal(1, game.Snapshot().Revision);
        while (slow.Reader.TryRead(out _)) { }
        Assert.True(slow.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task FaultAfterPublishedEventsRecoversCommittedCheckpointWithoutRetry()
    {
        var dice = new Dice { ThrowDraw = true };
        using var game = new PlaytestGame(dice, Setup());
        using var updates = game.Subscribe(null);
        var baseline = await Next(updates, _ => true);
        game.Accept(Command(baseline, "round"));
        var emission = await Next(updates, u => u.Kind == "event");
        Assert.Equal(1, emission.Snapshot.Result.State.Round);
        var fault = await Next(updates, u => u.Kind == "fault");
        Assert.Equal("faulted", fault.Status); Assert.Equal("test random failure", fault.Error);
        Assert.Equal(0, fault.Snapshot.Result.State.Round); Assert.Equal(0, fault.Revision);
        Assert.Null(fault.DecisionId); Assert.Null(fault.Snapshot.Result.NextInput); Assert.Equal(1, dice.Draws);
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Accept(Command(fault, "round"))).StatusCode);
        game.Accept(Command(fault, "restart"));
        var reset = await Next(updates, u => u.Kind == "baseline");
        Assert.Equal("idle", reset.Status);
    }

    [Fact]
    public async Task ExternalWaitPublishesBoundaryAndResumesSameSessionUsingSuppliedKey()
    {
        var called = new TaskCompletionSource<ExternalDecisionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var game = new PlaytestGame(new Dice(), Setup(ControllerKind.Automated),
            (context, _) => { called.TrySetResult(context); return answer.Task; });
        using var updates = game.Subscribe(null);
        var baseline = await Next(updates, _ => true);
        game.Accept(Command(baseline, "round"));
        var waiting = await Next(updates, u => u.Status == "waiting-external");
        var context = await called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(waiting.DecisionId, context.DecisionId);
        Assert.Null(waiting.Snapshot.Result.NextInput); Assert.Null(waiting.Snapshot.Presentation.Decision);
        var originalUnits = game.Snapshot().Result.State.Units.Count;
        context.State.Units.Clear();
        Assert.Equal(originalUnits, game.Snapshot().Result.State.Units.Count);
        answer.SetResult(context.Request.Candidates.Single(c => c.Kind == ActivationChoiceKind.Stay).Key);
        var continued = await Next(updates, u => u.Kind == "event" && u.Sequence > waiting.Sequence);
        Assert.Equal(waiting.SessionId, continued.SessionId);
        var nextWait = await Next(updates, u => u.Status == "waiting-human" && u.Revision > waiting.Revision);
        Assert.NotEqual(waiting.DecisionId, nextWait.DecisionId);
    }

    [Fact]
    public async Task ResetInvalidatesExternalCompletionEvenWhenSelectorIgnoresCancellation()
    {
        var called = new TaskCompletionSource<ExternalDecisionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var game = new PlaytestGame(new Dice(), Setup(ControllerKind.Automated),
            (context, _) => { called.TrySetResult(context); return answer.Task; });
        using var updates = game.Subscribe(null);
        var baseline = await Next(updates, _ => true);
        game.Accept(Command(baseline, "round"));
        var waiting = await Next(updates, u => u.Status == "waiting-external");
        var context = await called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        game.Accept(Command(waiting, "restart"));
        var reset = await Next(updates, u => u.Kind == "baseline");
        answer.SetResult(context.Request.Candidates[0].Key);
        // A serialized subsequent preference command establishes that the reset session continues normally.
        var preference = game.SetRelevanceAutoChoice(reset.Revision, false);
        Assert.Equal(0, preference.Result.State.Round);
        Assert.Equal(reset.Revision + 1, preference.Revision);
        Assert.Equal(reset.SessionId, (await Next(updates, u => u.Revision == preference.Revision)).SessionId);
    }

    [Fact]
    public async Task PublicSnapshotMutationsDoNotTouchEngineAndInvalidInputDoesNotPublish()
    {
        using var game = new PlaytestGame(new Dice(), Setup());
        var started = game.StartRound(0);
        using var updates = game.Subscribe(null);
        var baseline = await Next(updates, _ => true);
        started.Result.State.Units.Clear(); started.Result.NextInput!.Candidates.Clear();
        var copy = game.Snapshot(); copy.Result.State.Units.Clear(); copy.Result.NextInput!.Candidates.Clear();
        Assert.Equal(2, game.Snapshot().Result.State.Units.Count);
        Assert.NotEmpty(game.Snapshot().Result.NextInput!.Candidates);
        var invalid = Command(baseline, "decision", "forged");
        Assert.Equal(400, Assert.Throws<PlaytestRequestException>(() => game.Accept(invalid)).StatusCode);
        Assert.Equal(started.Revision, game.Snapshot().Revision);
        Assert.False(updates.Reader.TryRead(out _));
        Assert.Equal(409, Assert.Throws<PlaytestRequestException>(() => game.Accept(Command(baseline, "decision", "stay") with { DecisionId = "old" })).StatusCode);
    }

    [Fact]
    public async Task LaterAutomaticCallFaultReturnsToLastCommittedBoundary()
    {
        var dice = new Dice { ThrowRoll = true };
        using var game = new PlaytestGame(dice, Setup(ControllerKind.Automated));
        using var updates = game.Subscribe(null);
        var baseline = await Next(updates, _ => true);
        game.Accept(Command(baseline, "round"));
        var movement = await Next(updates, u => u.Kind == "event" && u.Snapshot.Result.Events[0].Kind == "MovementCompleted");
        Assert.Equal(1, movement.Snapshot.Result.State.Round);
        var fault = await Next(updates, u => u.Kind == "fault");
        Assert.Equal("test attack failure", fault.Error);
        Assert.Equal(0, fault.Snapshot.Result.State.Round);
        Assert.Equal(new Cell(0, 0), fault.Snapshot.Result.State.Physical.Figures.Single(f => f.Id == "one").Position);
        Assert.Equal(1, dice.Rolls);
    }

    [Fact]
    public async Task DisposalClosesSubscribersWithoutWaitingForExternalCancellationCallbacks()
    {
        using var release = new ManualResetEventSlim();
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var game = new PlaytestGame(new Dice(), Setup(ControllerKind.Automated), (_, cancellation) =>
        {
            cancellation.Register(() => release.Wait(TimeSpan.FromSeconds(10)));
            called.TrySetResult();
            return new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        });
        using var updates = game.Subscribe(null);
        var baseline = await Next(updates, _ => true);
        game.Accept(Command(baseline, "round"));
        await called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Run(game.Dispose).WaitAsync(TimeSpan.FromSeconds(2));
            while (updates.Reader.TryRead(out _)) { }
            await updates.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(503, Assert.Throws<PlaytestRequestException>(() => game.Accept(Command(baseline, "restart"))).StatusCode);
        }
        finally { release.Set(); }
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task HttpSseFlushesIncrementallyAndDisconnectDoesNotCancelAcceptedCommand()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var dice = new Dice { DrawEntered = entered, DrawRelease = release };
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
        await using var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root], dice, Setup());
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var stream = await client.GetAsync("/api/game/updates", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType!.MediaType);
        Assert.True(stream.Headers.CacheControl!.NoStore);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync());
        async Task<GameUpdate> Read(StreamReader source)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string? id = null, eventType = null;
            while (true)
            {
                var line = await source.ReadLineAsync(timeout.Token);
                Assert.NotNull(line);
                if (line.StartsWith("id: ")) id = line[4..];
                if (line.StartsWith("event: ")) eventType = line[7..];
                if (line.StartsWith("data: "))
                {
                    var update = JsonSerializer.Deserialize<GameUpdate>(line[6..], Json)!;
                    Assert.Equal("update", eventType); Assert.Equal($"{update.SessionId}:{update.Sequence}", id);
                    return update;
                }
            }
        }
        var baseline = await Read(reader);
        try
        {
            using var accepted = await client.PostAsJsonAsync("/api/game/commands", Command(baseline, "round"));
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            GameUpdate update; do { update = await Read(reader); } while (update.Kind != "event");
            Assert.Equal("resolving", update.Status);
            stream.Dispose();
        }
        finally { release.Set(); }
        using var after = app.Services.GetRequiredService<PlaytestGame>().Subscribe(null);
        var stable = await Next(after, u => u.Status == "waiting-human");
        Assert.Equal(1, stable.Revision);
        using var replayRequest = new HttpRequestMessage(HttpMethod.Get, "/api/game/updates");
        replayRequest.Headers.Add("Last-Event-ID", $"{baseline.SessionId}:{baseline.Sequence}");
        using var replayResponse = await client.SendAsync(replayRequest, HttpCompletionOption.ResponseHeadersRead);
        using var replayReader = new StreamReader(await replayResponse.Content.ReadAsStreamAsync());
        var replayed = await Read(replayReader);
        Assert.Equal(baseline.Sequence + 1, replayed.Sequence); Assert.Equal("boundary", replayed.Kind);
        using var recoveryRequest = new HttpRequestMessage(HttpMethod.Get, "/api/game/updates");
        recoveryRequest.Headers.Add("Last-Event-ID", "old-session:4");
        using var recoveryResponse = await client.SendAsync(recoveryRequest, HttpCompletionOption.ResponseHeadersRead);
        using var recoveryReader = new StreamReader(await recoveryResponse.Content.ReadAsStreamAsync());
        var recovered = await Read(recoveryReader);
        Assert.Equal("baseline", recovered.Kind); Assert.Equal(stable.Sequence, recovered.Sequence);
        Assert.Equal(stable.Revision, recovered.Revision); Assert.Empty(recovered.Snapshot.Result.Events);
        replayResponse.Dispose(); recoveryResponse.Dispose();
        await app.StopAsync();
    }
}
