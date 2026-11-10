using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ProjectDelve.Engine;
using ProjectDelve.Web;

internal static class Program
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Dice Random = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private static readonly MethodInfo Subscribe = typeof(PlaytestGame).GetMethod("Subscribe", HiddenInstance)!;
    private static readonly MethodInfo BrowserUpdate = typeof(PlaytestGame).GetMethod("BrowserUpdate", HiddenInstance)!;
    private static readonly MethodInfo BrowserResponse = typeof(PlaytestGame).GetMethod("BrowserResponse", HiddenInstance)!;
    private static readonly MethodInfo Detach = typeof(PlaytestGame).GetMethod("Detach", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(GameState));
    private static int warmups, samples;
    private static string fixture = "";
    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));

    private static int Main(string[] args)
    {
        try
        {
            var options = new Dictionary<string, string>();
            for (var i = 0; i < args.Length; i += 2)
            {
                if (args[i] == "--help") { Help(); return 0; }
                if (i + 1 == args.Length || args[i] is not ("--fixture" or "--mode" or "--samples" or "--warmups" or "--delve" or "--export"))
                    throw new ArgumentException("Expected named option and value; use --help.");
                options.Add(args[i], args[i + 1]);
            }
            samples = int.Parse(options.GetValueOrDefault("--samples", "5"));
            warmups = int.Parse(options.GetValueOrDefault("--warmups", "1"));
            if (samples < 1 || warmups < 0) throw new ArgumentException("Require samples >= 1 and warmups >= 0.");
            var selected = options.GetValueOrDefault("--fixture", "small");
            var mode = options.GetValueOrDefault("--mode", "all");
            if (selected is not ("small" or "distant100" or "distant442" or "all")) throw new ArgumentException("Unknown fixture.");
            if (mode is not ("import" or "preparation" or "action" or "pipeline" or "observation" or "dormant" or "all")) throw new ArgumentException("Unknown mode.");
            if (options.ContainsKey("--delve") && mode is not ("import" or "preparation"))
                throw new ArgumentException("--delve requires --mode import or preparation.");
            if (options.ContainsKey("--export") && mode != "import")
                throw new ArgumentException("--export requires --mode import.");
            if (options.ContainsKey("--export") && selected == "all") throw new ArgumentException("--export requires one fixture.");
            Write(new { kind = "environment", runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
                serverGC = System.Runtime.GCSettings.IsServerGC, configuration = typeof(GameState).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
                engine = typeof(GameState).Assembly.Location, engineMvid = typeof(GameState).Module.ModuleVersionId,
                webMvid = typeof(PlaytestGame).Module.ModuleVersionId, samples, warmups,
                percentile = "nearest-rank", allocation = "GC.GetTotalAllocatedBytes(precise:true), process-wide", preparePath = PreparePath });
            foreach (var name in selected == "all" ? new[] { "small", "distant100", "distant442" } : new[] { selected })
            {
                fixture = name;
                var definition = Fixture(name, UnitTypeIds.Beastmaster);
                if (options.TryGetValue("--delve", out var path))
                {
                    definition = ScenarioDefinitionTransport.Decode(File.ReadAllText(path));
                    fixture = "saved:" + Path.GetFileName(path);
                }
                if (mode is "import" or "all") Import(definition, options.GetValueOrDefault("--export"));
                if (mode == "preparation") Import(definition, null, preparationOnly: true);
                if (mode is "action" or "all") Actions(definition);
                if (mode == "pipeline") Pipeline(definition);
                if (mode is "observation" or "all") Observation(definition);
                if (mode == "dormant")
                {
                    var rogue = Fixture(name, UnitTypeIds.Rogue);
                    Measure("engine.quietRogueRound", () => GameEngine.CreateGame(rogue), s => GameEngine.StartRound(s, Random, false));
                }
                if (options.ContainsKey("--delve")) break;
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Help() => Console.WriteLine("WebPublicationBenchmark --fixture small|distant100|distant442|all --mode import|preparation|action|pipeline|observation|dormant|all --samples N --warmups N [--delve path] [--export path]");
    private static string PreparePath => typeof(WorldQuickPlay).GetMethod("PrepareValidated", BindingFlags.Static | BindingFlags.NonPublic) is null
        ? "FromJson+Prepare" : "Materialize+PrepareValidated";

    // Reflection deliberately keeps this harness compilable against frozen Phase 1 assemblies.
    private static ScenarioDefinition Prepare(string raw, ContentBundle content)
    {
        var prepare = typeof(WorldQuickPlay).GetMethod("PrepareValidated", BindingFlags.Static | BindingFlags.NonPublic);
        Action<ScenarioDefinition> limits = AuthoringLimits.Default.CheckScenario;
        if (prepare is null) return WorldQuickPlay.Prepare(ScenarioDefinitionJson.FromJson(raw, limits, content), content);
        var materialize = typeof(ScenarioDefinitionJson).GetMethod("Materialize", BindingFlags.Public | BindingFlags.Static)!;
        var value = materialize.Invoke(null, [raw, limits, content])!;
        return (ScenarioDefinition)prepare.Invoke(null, [value, content])!;
    }

    private static void Import(ScenarioDefinition definition, string? export, bool preparationOnly = false)
    {
        var raw = ScenarioDefinitionJson.ToJson(definition);
        var content = ContentBundle.Canonical();
        var prepared = Measure("import.prepareEndpoint", () => Prepare(raw, content));
        var transport = Measure("import.encodeTransport", () => ScenarioDefinitionTransport.Encode(prepared));
        if (preparationOnly)
        {
            Measure("import.createPrepared", () => GameEngine.CreateGame(prepared));
            return;
        }
        Measure("import.legacyHost", () => { var host = new PlaytestGame(Random); return new LegacyContext(host, host.Snapshot().Revision); },
            c => c.Host.StartTransportScenario(c.Revision, transport));
        MeasureStream("import.stream", () => new PlaytestGame(Random), host => new()
        { Operation = "scenario/import", Transport = transport }, "baseline");
        using var projectionHost = new PlaytestGame(Random);
        var stream = Stream(projectionHost, new() { Operation = "scenario/import", Transport = transport }, "baseline");
        StopHost(projectionHost);
        Project("import", projectionHost, stream.Updates);
        if (export is not null)
        {
            var response = (GameResponse)BrowserResponse.Invoke(projectionHost, [stream.Updates[^1].Snapshot])!;
            File.WriteAllBytes(export, JsonSerializer.SerializeToUtf8Bytes(response, Json));
            Write(new { kind = "export", fixture, path = Path.GetFullPath(export) });
        }
    }

    private static GameState BeforeMove(ScenarioDefinition definition)
    {
        var state = GameEngine.CreateGame(definition);
        state.Round = 1; state.ActiveToken = state.Units[0].Token; state.CurrentUnitId = "actor";
        state.ResolvingTokenUnitIds = ["actor"]; state.Bag = [state.Units[1].Token];
        return GameEngine.RefreshChoices(state, Random, false).State;
    }
    private static GameState BeforeSummon(ScenarioDefinition definition)
    {
        var state = BeforeMove(definition);
        var stay = state.Pending!.Candidates.First(c => c.Kind == ActivationChoiceKind.Stay);
        return GameEngine.Advance(state, new Choice(stay.Key), Random, false).State;
    }
    private static GameCommand Decision(PlaytestGame host, bool summon)
    {
        var pending = host.Snapshot().Result.NextInput!;
        var candidate = summon
            ? pending.Candidates.First(c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic { UnitTypeId: UnitTypeIds.Raptor })
            : pending.Candidates.First(c => c.Kind == ActivationChoiceKind.Stay);
        return new() { Operation = "decision", CandidateKey = candidate.Key };
    }
    private static void Actions(ScenarioDefinition definition)
    {
        foreach (var summon in new[] { false, true })
        {
            var stage = summon ? "action.raptor" : "action.stay";
            PlaytestGame Setup() => Host(summon ? BeforeSummon(definition) : BeforeMove(definition));
            MeasureStream(stage, Setup, h => Decision(h, summon), "boundary");
            using var projectionHost = Setup();
            var stream = Stream(projectionHost, Decision(projectionHost, summon), "boundary");
            StopHost(projectionHost);
            Project(stage, projectionHost, stream.Updates);
        }
    }
    private static PlaytestGame Host(GameState state)
    {
        var constructor = typeof(PlaytestGame).GetConstructors(HiddenInstance).Single(c => c.GetParameters().Any(p => p.Name == "initialState"));
        var arguments = constructor.GetParameters().Select(p => p.Name switch
        {
            "random" => (object)Random, "initialState" => state, "replayCapacity" => 1024, _ => p.DefaultValue
        }).ToArray();
        return (PlaytestGame)constructor.Invoke(arguments);
    }

    private static void Pipeline(ScenarioDefinition definition)
    {
        foreach (var summon in new[] { false, true })
        {
            Measure(summon ? "pipeline.raptor" : "pipeline.stay", () =>
            {
                var host = Host(summon ? BeforeSummon(definition) : BeforeMove(definition));
                return PrepareStream(host, Decision(host, summon), "boundary", true);
            }, context =>
            {
                var stream = ExecuteStream(context);
                var bytes = 0;
                foreach (var update in stream.Updates)
                {
                    var projected = (GameUpdate)BrowserUpdate.Invoke(context.Host, [update])!;
                    bytes += JsonSerializer.SerializeToUtf8Bytes(projected, Json).Length;
                }
                return new { publicationCount = stream.Updates.Count, bytes,
                    stream.AdmissionMs, stream.AcceptedToReadyMs, stream.TotalServiceMs };
            }, result => result);
        }
    }

    private sealed record StreamResult(List<GameUpdate> Updates, double AdmissionMs, double AcceptedToReadyMs, double TotalServiceMs);
    private static StreamResult Stream(PlaytestGame host, GameCommand request, string finalKind)
    {
        using var context = PrepareStream(host, request, finalKind, false);
        return ExecuteStream(context);
    }
    private static StreamContext PrepareStream(PlaytestGame host, GameCommand request, string finalKind, bool ownsHost)
    {
        var subscription = (IDisposable)Subscribe.Invoke(host, [null])!;
        var reader = (ChannelReader<GameUpdate>)subscription.GetType().GetProperty("Reader")!.GetValue(subscription)!;
        var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        // Drain initial baseline and capture all caller keys outside the service timer.
        var initial = reader.ReadAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
        if (initial.Kind != "baseline") throw new InvalidOperationException("Missing subscription baseline.");
        while (reader.TryRead(out _)) { }
        var snapshot = host.Snapshot();
        request = request with { CommandId = Guid.NewGuid().ToString("N"), SessionId = snapshot.SessionId,
            ExpectedRevision = snapshot.Revision, DecisionId = initial.DecisionId };
        return new(host, request, finalKind, reader, subscription, timeout, ownsHost);
    }
    private static StreamResult ExecuteStream(StreamContext context)
    {
        var (host, request, finalKind, reader, _, timeout, _) = context;
        var updates = new List<GameUpdate>();
        var started = Stopwatch.GetTimestamp();
        var acceptance = host.Accept(request);
        var accepted = Stopwatch.GetTimestamp();
        if (!acceptance.Accepted) throw new InvalidOperationException("Command was not accepted.");
        while (true)
        {
            var update = reader.ReadAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            updates.Add(update);
            if (update.Error is not null || update.Status == "faulted") throw new InvalidOperationException(update.Error ?? "Faulted publication.");
            if (update.CommandId == request.CommandId && update.Kind == finalKind && update.Revision == request.ExpectedRevision + 1)
            {
                var finished = Stopwatch.GetTimestamp();
                return new(updates, Stopwatch.GetElapsedTime(started, accepted).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(accepted, finished).TotalMilliseconds, Stopwatch.GetElapsedTime(started, finished).TotalMilliseconds);
            }
        }
    }
    private static void MeasureStream(string stage, Func<PlaytestGame> setup, Func<PlaytestGame, GameCommand> command, string finalKind)
        => Measure(stage, () => { var host = setup(); return PrepareStream(host, command(host), finalKind, true); },
            ExecuteStream, result => new
            { result.AdmissionMs, result.AcceptedToReadyMs, result.TotalServiceMs, publicationCount = result.Updates.Count,
                kinds = result.Updates.Select(u => u.Kind).ToArray(), finalStatus = result.Updates[^1].Status });
    private sealed record StreamContext(PlaytestGame Host, GameCommand Command, string FinalKind,
        ChannelReader<GameUpdate> Reader, IDisposable Subscription, CancellationTokenSource Timeout, bool OwnsHost) : IDisposable
    { public void Dispose() { Subscription.Dispose(); Timeout.Dispose(); if (OwnsHost) StopHost(Host); } }
    private sealed record LegacyContext(PlaytestGame Host, long Revision) : IDisposable
    { public void Dispose() => StopHost(Host); }

    // Publication makes the response ready before Execute has necessarily returned.
    // Join after the timed interval so its remaining work cannot overlap later samples.
    private static void StopHost(PlaytestGame host)
    {
        host.Dispose();
        var runner = (Task)typeof(PlaytestGame).GetField("runner", HiddenInstance)!.GetValue(host)!;
        runner.GetAwaiter().GetResult();
    }

    private static void Project(string stage, PlaytestGame host, List<GameUpdate> updates)
    {
        var projected = Measure<GameUpdate[]>(stage + ".browserUpdateProjection", () => updates.Select(u => (GameUpdate)BrowserUpdate.Invoke(host, [u])!).ToArray(),
            u => new { publicationCount = u.Length });
        Measure<byte[][]>(stage + ".browserUpdateSerialize", () => projected.Select(u => JsonSerializer.SerializeToUtf8Bytes(u, Json)).ToArray(),
            bytes => new { publicationCount = bytes.Length, bytes = bytes.Sum(b => b.Length) });
    }
    private static void Observation(ScenarioDefinition definition)
    {
        var state = GameEngine.CreateGame(definition);
        Measure("observation.detachState", () => (GameState)Detach.Invoke(null, [state])!);
        var observed = Measure("observation.observe", () => FogOfWar.Observe(state, FogOfWar.ViewingSide(state)));
        Measure<byte[]>("observation.serializeObservedState", () => JsonSerializer.SerializeToUtf8Bytes(observed, Json), bytes => new { bytes = bytes.Length });
    }

    private static T Measure<T>(string stage, Func<T> operation, Func<T, object>? detail = null)
        => Measure(stage, () => 0, _ => operation(), detail);
    private static T Measure<TSetup, T>(string stage, Func<TSetup> setup, Func<TSetup, T> operation, Func<T, object>? detail = null)
    {
        var times = new List<double>(); var allocations = new List<double>();
        T result = default!;
        for (var run = -warmups; run < samples; run++)
        {
            var context = setup();
            try
            {
                var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
                var allocated = GC.GetTotalAllocatedBytes(true); var started = Stopwatch.GetTimestamp();
                result = operation(context);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
                var collections = new[] { GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2 };
                Write(new { kind = "sample", fixture, stage, sample = run, warmup = run < 0,
                    firstInvocation = run == -warmups, elapsedMs = elapsed, allocatedBytes = bytes, collections, detail = detail?.Invoke(result) });
                if (run >= 0) { times.Add(elapsed); allocations.Add(bytes); }
            }
            finally { if (context is IDisposable disposable) disposable.Dispose(); }
        }
        double Percentile(List<double> values, double p) => values.Order().ElementAt((int)Math.Ceiling(values.Count * p) - 1);
        Write(new { kind = "summary", fixture, stage, samples,
            p50Ms = Percentile(times, .5), p95Ms = Percentile(times, .95),
            p50AllocatedBytes = Percentile(allocations, .5), p95AllocatedBytes = Percentile(allocations, .95) });
        return result;
    }

    private static ScenarioDefinition Fixture(string name, string actor)
    {
        var size = name == "small" ? 12 : 120;
        var count = name switch { "small" => 0, "distant100" => 100, _ => 442 };
        List<UnitPlacement> units = [new(actor, "blue", new(2, 2), UnitId: "actor"),
            new(UnitTypeIds.Grunt, "red", new(8, 8), UnitId: "near", Dormant: true)];
        for (var i = 0; i < count; i++) units.Add(new(UnitTypeIds.Grunt, "red", new(15 + i % 100, 15 + i / 100), UnitId: "d" + i, Dormant: true));
        return new()
        {
            Board = new() { Width = size, Height = size }, UnitTypeIds = [actor, UnitTypeIds.Grunt], Units = units,
            Agency = [new(actor, "blue", ControllerKind.Human), new(UnitTypeIds.Grunt, "red", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
            FogOfWarSides = ["blue"], Loot = LootSettings.Empty
        };
    }
    private sealed class Choice(string key) : IDecisionProvider
    { public string Choose(DecisionRequest request, IGameplayQueries queries) => key; }
    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag.FirstOrDefault(t => t.SideId == "blue") ?? bag[0];
        public AttackFace RollAttackDie() => AttackFace.Miss;
        public DefenceFace RollDefenceDie() => DefenceFace.Miss;
        public int RollD6() => 1;
        public int RollD8() => 1;
        public int WorldShuffleIndex(int max) => 0;
    }
}
