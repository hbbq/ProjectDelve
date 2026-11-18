using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProjectDelve.Engine;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Web.Tests;

public sealed class ExpeditionClientApiTests(ITestOutputHelper output) : IDisposable
{
    private const ulong Seed = 889;
    private const string Boots = "fixture-carried-boots";
    private const string HiddenItem = "fixture-undiscovered-potion";
    private const string HiddenFailure = "private generator diagnostic at (41,49)";
    private readonly string root = Path.Combine(Path.GetTempPath(), "delve-expedition-client-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task CreationValidatesInputsAndPublishesAFullWld1Region()
    {
        await using var host = await Host.Start(root);
        var watch = Stopwatch.StartNew();
        foreach (var input in new[]
        {
            Create(" ", "123"), Create(new string('x', 81), "123"), Create("split\nname", "123"),
            Create("Bad seed", "18446744073709551616"), Create("Bad seed", "-1"), Create("Bad seed", "0xBADH"),
            Create("Bad recipe", "889", "WLD1-invalid")
        })
        {
            Assert.Contains(await host.Reject(input), new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
        }
        Assert.Empty((await host.Catalog()).Expeditions);
        Assert.Null(host.Current.Snapshot.Expedition);

        watch.Restart();
        var first = await host.Send(Create("Seed 889", "889", "Grassland"), timeout: TimeSpan.FromMinutes(2));
        output.WriteLine($"Full seed-889 Grassland creation and first browser publication: {watch.Elapsed.TotalSeconds:F2}s.");
        var view = Assert.IsType<ExpeditionPlayView>(first.Snapshot.Expedition);
        Assert.Equal("Seed 889", view.Name);
        Assert.Equal("0x0000000000000379", view.Seed);
        Assert.Equal(default, view.Region);
        Assert.Equal(2, view.ExitRequired);
        Assert.Null(view.TransitionId);
        Assert.False(view.HasUnsavedChanges);
        var store = new ExpeditionStore(root, view.Id);
        var head = store.ReadHead();
        Assert.NotNull(head.WorldProfile);
        var checkpoint = store.ReadCheckpoint(head.ActiveCheckpoint);
        Assert.Equal(60, checkpoint.Scenario.Board.Width);
        Assert.Equal(60, checkpoint.Scenario.Board.Height);
        Assert.Contains(checkpoint.State.Units, unit => unit.SideId != ExpeditionParty.SideId);
        Assert.True(first.Snapshot.Result.State.Units.Count < checkpoint.State.Units.Count);
        Assert.Equal(5, first.Snapshot.Result.State.Units.Count(unit => unit.SideId == ExpeditionParty.SideId));
        var summary = Assert.Single((await host.Catalog()).Expeditions);
        Assert.Equal(view.Id, summary.Id);
        Assert.Equal(view.SavedAt, summary.SavedAt);
    }

    [Fact]
    public async Task PendingChoiceRngAndFogSurviveSwitchAndServerRestart()
    {
        var catalog = new ExpeditionCatalog(root, ContentBundle.Canonical, generate: Fixture);
        ExpeditionPlayView first;
        ExpeditionCheckpoint saved;
        string pendingJson;
        await using (var host = await Host.Start(root, catalog))
        {
            first = (await host.Send(Create("First", "889"))).Snapshot.Expedition!;
            await host.Send(new() { Operation = "preferences", AutoChooseSingleRelevantChoice = false });
            await host.Send(new() { Operation = "round" });
            var playing = host.Current.Snapshot;
            Assert.True(playing.Expedition!.HasUnsavedChanges);
            pendingJson = Encode(Assert.IsType<DecisionRequest>(playing.Result.NextInput));
            Assert.NotEmpty(playing.Result.State.ExploredCells[ExpeditionParty.SideId]);
            Assert.DoesNotContain(HiddenItem, Encode(playing));
            var saveClock = Stopwatch.StartNew();
            await host.Send(new() { Operation = "expedition/save" });
            saveClock.Stop();
            Assert.False(host.Current.Snapshot.Expedition!.HasUnsavedChanges);
            var store = new ExpeditionStore(root, first.Id);
            saved = store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint);
            output.WriteLine($"Pending fixture Save and browser acknowledgement: {saveClock.Elapsed.TotalMilliseconds:F0}ms.");
            Assert.False(saved.AutoChooseSingleRelevantChoice);
            Assert.Contains(saved.State.GroundObjects, item => item.Object.Id == HiddenItem);
            var second = (await host.Send(Create("Second", null))).Snapshot.Expedition!;
            Assert.NotEqual(first.Id, second.Id);
            Assert.Matches("^0x[0-9A-F]{16}$", second.Seed);
            var listed = await host.Catalog();
            Assert.Equal(2, listed.Expeditions.Count);
            Assert.DoesNotContain(HiddenItem, Encode(listed));
            await host.Send(new() { Operation = "round" });
            Assert.True(host.Current.Snapshot.Expedition!.HasUnsavedChanges);
            var switched = await host.Send(new() { Operation = "expedition/resume", ExpeditionId = first.Id });
            Assert.Equal(pendingJson, Encode(switched.Snapshot.Result.NextInput));
            var secondStore = new ExpeditionStore(root, second.Id);
            Assert.NotNull(secondStore.ReadCheckpoint(secondStore.ReadHead().ActiveCheckpoint).State.Pending);
            Assert.Equal(saved.GameplayRandomState, store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint).GameplayRandomState);
        }

        await using var reopened = await Host.Start(root, catalog);
        Assert.Null(reopened.Current.Snapshot.Expedition);
        var resumeClock = Stopwatch.StartNew();
        var resumed = await reopened.Send(new() { Operation = "expedition/resume", ExpeditionId = first.Id });
        output.WriteLine($"Pending fixture Resume from a fresh host: {resumeClock.Elapsed.TotalMilliseconds:F0}ms.");
        Assert.Equal(pendingJson, Encode(resumed.Snapshot.Result.NextInput));
        Assert.False(resumed.Snapshot.AutoChooseSingleRelevantChoice);
        Assert.DoesNotContain(HiddenItem, Encode(resumed));
        var firstStore = new ExpeditionStore(root, first.Id);
        var random = new ExpeditionRandom(saved.GameplayRandomState);
        var legal = Pass(resumed.Snapshot.Result.NextInput!);
        var direct = GameEngine.Advance(saved.State, new Choice(legal.Key), random, false);
        await reopened.Send(new() { Operation = "decision", CandidateKey = legal.Key });
        await reopened.Send(new() { Operation = "expedition/leave" });
        Assert.Null(reopened.Current.Snapshot.Expedition);
        var checkpoint = firstStore.ReadCheckpoint(firstStore.ReadHead().ActiveCheckpoint);
        Assert.Equal(random.State, checkpoint.GameplayRandomState);
        Assert.Equal(Encode(direct.State), Encode(checkpoint.State));
        Assert.Equal(2, (await reopened.Catalog()).Expeditions.Count);
    }

    [Fact]
    public async Task ContinueKeepsCompletedRegionAcrossGenerationFailureAndDuplicates()
    {
        ExpeditionStore? store = null;
        ExpeditionHead? completed = null;
        var builds = 0;
        using var generatorEntered = new ManualResetEventSlim();
        using var allowFailure = new ManualResetEventSlim();
        ScenarioDefinition Generate(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
        {
            Assert.Equal(Seed, seed);
            if (plan.Region != default)
            {
                Assert.Equal(completed!.ActiveCheckpoint, store!.ReadHead().ActiveCheckpoint);
                Assert.True(store.ReadCheckpoint(completed.ActiveCheckpoint).State.IsCompleted);
                if (++builds == 1)
                {
                    generatorEntered.Set();
                    Assert.True(allowFailure.Wait(TimeSpan.FromSeconds(10)));
                    throw new InvalidOperationException(HiddenFailure);
                }
            }
            return Fixture(seed, plan, content);
        }
        var catalog = new ExpeditionCatalog(root, ContentBundle.Canonical, generate: Generate);
        await using var host = await Host.Start(root, catalog);
        var origin = (await host.Send(Create("Handoff", "889"))).Snapshot.Expedition!;
        store = new(root, origin.Id);
        var initial = store.ReadCheckpoint(store.ReadHead().ActiveCheckpoint);
        await host.Send(new() { Operation = "preferences", AutoChooseSingleRelevantChoice = false });
        var traveled = Stopwatch.StartNew();
        var steps = 0;
        for (; steps < 100 && !host.Current.Snapshot.Result.State.IsCompleted; steps++)
        {
            var result = host.Current.Snapshot.Result;
            if (result.NextInput is not { } request)
            {
                await host.Send(new() { Operation = "round" });
                continue;
            }

            var choice = AdvanceTowardExit(result.State, request, initial.Plan.ExitArea);
            await host.Send(new() { Operation = "decision", CandidateKey = choice.Key });
        }
        output.WriteLine($"Fixture completed in {steps} browser commands and {traveled.Elapsed.TotalSeconds:F2}s.");
        var sourceView = host.Current.Snapshot.Expedition!;
        Assert.NotNull(sourceView.TransitionId);
        Assert.Equal(2, sourceView.ExitCount);
        Assert.Equal(1, sourceView.CompletedRegions);
        Assert.False(sourceView.HasUnsavedChanges);
        completed = store.ReadHead();
        var source = store.ReadCheckpoint(completed.ActiveCheckpoint);
        Assert.Contains(ExpeditionParty.SideId, source.State.Completion!.AchievedSideIds);
        Assert.Empty(completed.Transitions);
        var originalCheckpoint = Encode(source);
        Assert.Contains(source.State.Units.SelectMany(unit => unit.Inventory), item => item.Id == Boots);

        var command = new GameCommand { Operation = "expedition/continue", TransitionId = sourceView.TransitionId };
        var failing = host.Send(command, kind: "error");
        try
        {
            Assert.True(await Task.Run(() => generatorEntered.Wait(TimeSpan.FromSeconds(10))));
            Assert.Equal(HttpStatusCode.Conflict, await host.Reject(new() { Operation = "scenario", ScenarioId = PlaytestScenarios.DefaultId }));
            Assert.Equal(HttpStatusCode.Conflict, await host.Reject(command));
        }
        finally { allowFailure.Set(); }
        var failed = await failing;
        Assert.NotNull(failed.Error);
        Assert.Contains("generate", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(HiddenFailure, Encode(failed));
        Assert.Equal(sourceView, failed.Snapshot.Expedition);
        Assert.Equal(completed.ActiveCheckpoint, store.ReadHead().ActiveCheckpoint);

        var requestForRetry = host.Prepare(command);
        var success = await host.Send(requestForRetry, prepared: true);
        Assert.Equal(initial.Plan.NextRegion, success.Snapshot.Expedition!.Region);
        Assert.Equal(1, success.Snapshot.Expedition.CompletedRegions);
        Assert.Null(success.Snapshot.Expedition.TransitionId);
        Assert.Null(success.Snapshot.Result.NextInput);
        Assert.False(success.Snapshot.Result.State.IsCompleted);
        Assert.Equal(2, builds);
        Assert.Equal(HttpStatusCode.Accepted, await host.PostStatus(requestForRetry));
        Assert.Equal(HttpStatusCode.Conflict, await host.PostStatus(requestForRetry with { CommandId = Guid.NewGuid().ToString("N") }));
        var head = store.ReadHead();
        var transition = Assert.Single(head.Transitions);
        Assert.Equal(completed.ActiveCheckpoint, transition.Source);
        Assert.Equal(originalCheckpoint, Encode(store.ReadCheckpoint(transition.Source)));
        Assert.Equal(completed.OriginalHeroIds.Order(), success.Snapshot.Result.State.Units.Select(unit => unit.Id).Order());
        Assert.Contains(success.Snapshot.Result.State.Units.SelectMany(unit => unit.Inventory), item => item.Id == Boots);
        Assert.DoesNotContain(HiddenItem, Encode(success));
        Assert.Equal(2, builds);
    }

    [Fact]
    public void BrowserHandoffFixtureRetainsALegalPendingEndTurn()
    {
        var catalog = new ExpeditionCatalog(root, ContentBundle.Canonical, generate: Fixture);
        var runner = catalog.Create("Near Exit Fixture", "889", "Grassland");
        var result = runner.ResolveBoundary((state, rng) => GameEngine.StartRound(state, rng, false), false);
        var pad = runner.Region.ExitArea;
        for (var step = 0; step < 100; step++)
        {
            if (result.NextInput is not { } request) { result = runner.StartRound(); continue; }
            if (request.Candidates.Any(candidate => candidate.Kind == ActivationChoiceKind.EndTurn) &&
                GameEngine.ObjectiveQualifiedCount(result.State, result.State.Objectives.Single(objective => objective.SideId == ExpeditionParty.SideId)) == 2)
                break;
            result = runner.Decide(AdvanceTowardExit(result.State, request, pad).Key);
        }

        Assert.False(result.State.IsCompleted);
        var pending = Assert.IsType<DecisionRequest>(result.NextInput);
        var end = Assert.Single(pending.Candidates, candidate => candidate.Kind == ActivationChoiceKind.EndTurn);
        var head = runner.Save();
        var saved = new ExpeditionStore(root, head.Id).ReadCheckpoint(head.ActiveCheckpoint);
        Assert.True(GameEngine.Advance(saved.State, new Choice(end.Key), new ExpeditionRandom(saved.GameplayRandomState), false).State.IsCompleted);

        // Opt-in fixture export for the reviewed data-only Edge plan.
        if (Environment.GetEnvironmentVariable("PROJECTDELVE_EXPEDITION_BROWSER_FIXTURE") is { Length: > 0 } destination)
        {
            var source = Path.Combine(root, "expedition-" + head.Id);
            var target = Path.Combine(destination, "expedition-" + head.Id);
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source, "*.json"))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            output.WriteLine($"Browser handoff fixture exported to {target}.");
        }
    }

    private static GameCommand Create(string name, string? seed, string? recipe = null) =>
        new() { Operation = "expedition/create", ExpeditionName = name, ExpeditionSeed = seed, ExpeditionRecipe = recipe };

    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static int Distance(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private static Candidate Pass(DecisionRequest request) => request.Candidates.FirstOrDefault(candidate =>
        candidate.Kind is ActivationChoiceKind.Stay or ActivationChoiceKind.EndTurn) ?? request.Candidates[0];

    private static Candidate AdvanceTowardExit(GameState state, DecisionRequest request, ObjectiveArea area)
    {
        var pickup = request.Candidates.FirstOrDefault(candidate => candidate.Key == "pickup-object:" + Boots);
        if (pickup is not null) return pickup;
        var index = ExpeditionParty.Originals.ToList().FindIndex(hero => hero.InstanceId == request.UnitId);
        if (index is < 0 or > 1) return Pass(request);
        var target = new Cell(area.X + index, area.Y);
        var from = state.Physical.Figures.Single(figure => figure.Id == request.UnitId).Position;
        var move = request.Candidates.Where(candidate => candidate.Kind == ActivationChoiceKind.Move && candidate.Destination is not null)
            .OrderBy(candidate => Distance(candidate.Destination!, target)).FirstOrDefault();
        return move is not null && Distance(move.Destination!, target) < Distance(from, target) ? move : Pass(request);
    }

    private static ScenarioDefinition Fixture(ulong seed, ExpeditionRegionPlan plan, ContentBundle content)
    {
        var area = plan.ExitArea;
        var horizontal = plan.ExitEdge is ExpeditionEdge.East or ExpeditionEdge.West;
        var cells = (from u in Enumerable.Range(0, 5) from v in Enumerable.Range(0, 2)
                     select horizontal ? new Cell(area.X + v, area.Y - 5 + u) : new Cell(area.X - 5 + u, area.Y + v)).ToList();
        var edge = cells[^1];
        var departure = horizontal ? edge with { Y = edge.Y + 1 } : edge with { X = edge.X + 1 };
        return new()
        {
            Board = new() { Width = ExpeditionWorld.Stride, Height = ExpeditionWorld.Stride },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty,
            Setup = new(1, new(5, cells, cells.Take(8).ToList(), cells, edge, departure, 10, 8), []),
            GroundObjects = plan.Region == default ?
                [new(new OwnedObject(Boots, "lucky-boots"), cells[0]),
                    new(new OwnedObject(HiddenItem) { ItemTypeId = "second-wind" }, new(30, 30))] : []
        };
    }

    private sealed class Choice(string key) : IDecisionProvider
    {
        public string? Choose(DecisionRequest request, IGameplayQueries queries) => key;
    }

    private sealed class Host(WebApplication app, HttpClient client, HttpResponseMessage stream, StreamReader reader) : IAsyncDisposable
    {
        public GameUpdate Current { get; private set; } = null!;

        public static async Task<Host> Start(string root, ExpeditionCatalog? catalog = null)
        {
            var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ProjectDelve.Web"));
            var app = PlaytestHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", contentRoot, "--Expeditions:Path", root],
                null, CourtyardFixture.Create(), expeditionCatalog: catalog);
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient { BaseAddress = new Uri(address) };
            var stream = await client.GetAsync("/api/game/updates", HttpCompletionOption.ResponseHeadersRead);
            stream.EnsureSuccessStatusCode();
            var host = new Host(app, client, stream, new(await stream.Content.ReadAsStreamAsync()));
            host.Current = await host.Read(_ => true);
            return host;
        }

        public async Task<ExpeditionCatalogView> Catalog() => (await client.GetFromJsonAsync<ExpeditionCatalogView>("/api/expeditions", Json))!;

        public GameCommand Prepare(GameCommand request) => request with
        {
            CommandId = Guid.NewGuid().ToString("N"), SessionId = Current.SessionId,
            ExpectedRevision = Current.Revision, DecisionId = Current.DecisionId
        };

        public async Task<HttpStatusCode> Reject(GameCommand request) => await PostStatus(Prepare(request));

        public async Task<HttpStatusCode> PostStatus(GameCommand request)
        {
            using var response = await client.PostAsJsonAsync("/api/game/commands", request, Json);
            return response.StatusCode;
        }

        public async Task<GameUpdate> Send(GameCommand request, string? kind = null, bool prepared = false, TimeSpan? timeout = null)
        {
            var command = prepared ? request : Prepare(request);
            using var accepted = await client.PostAsJsonAsync("/api/game/commands", command, Json);
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
            Current = await Read(update => update.CommandId == command.CommandId &&
                (update.Kind is "error" or "fault" || update.Kind is "baseline" or "boundary" && update.Status != "resolving"), timeout);
            if (kind is not null) Assert.Equal(kind, Current.Kind);
            else Assert.True(Current.Kind is "baseline" or "boundary", Current.Error);
            return Current;
        }

        private async Task<GameUpdate> Read(Func<GameUpdate, bool> match, TimeSpan? wait = null)
        {
            using var timeout = new CancellationTokenSource(wait ?? TimeSpan.FromSeconds(30));
            var received = new List<string>();
            try
            {
                while (await reader.ReadLineAsync(timeout.Token) is { } line)
                {
                    if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                    var update = JsonSerializer.Deserialize<GameUpdate>(line[6..], Json)!;
                    if (match(update)) return update;
                    received.Add($"{update.Kind}/{update.Status} ({update.CommandId})");
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            { throw new TimeoutException("No matching game update. Received " + string.Join(", ", received)); }
            throw new EndOfStreamException("The game publication stream closed before the expected update.");
        }

        public async ValueTask DisposeAsync()
        {
            reader.Dispose();
            stream.Dispose();
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
