using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Web;

internal static class Program
{
    private static readonly Dice Random = new();
    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web)
        { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static readonly MethodInfo SummonCandidates = typeof(SummoningRules).GetMethod("Candidates",
        BindingFlags.Static | BindingFlags.NonPublic, [typeof(GameState), typeof(Unit)])!;
    private static readonly MethodInfo WakingUnits = typeof(DormantRules).GetMethod("WakingUnits",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static int warmups = 1;
    private static int samples = 5;
    private static string fixture = "small";

    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value));

    private static int Main(string[] args)
    {
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i += 2)
            {
                if (args[i] == "--help") { Help(); return 0; }
                if (i + 1 == args.Length || args[i] is not ("--fixture" or "--mode" or "--warmups" or "--samples" or "--delve"))
                    throw new ArgumentException("Expected named option and value; use --help.");
                options.Add(args[i], args[i + 1]);
            }
            warmups = int.Parse(options.GetValueOrDefault("--warmups", "1"));
            samples = int.Parse(options.GetValueOrDefault("--samples", "5"));
            if (warmups < 0 || samples < 1) throw new ArgumentException("Require warmups >= 0 and samples >= 1.");
            var selected = options.GetValueOrDefault("--fixture", "small");
            var mode = options.GetValueOrDefault("--mode", "summon");
            if (selected is not ("small" or "distant100" or "distant442" or "all")) throw new ArgumentException("Unknown fixture.");
            if (mode is not ("summon" or "candidates" or "create" or "dormant" or "quick" or "all")) throw new ArgumentException("Unknown mode.");
            if (options.ContainsKey("--delve") && mode != "quick") throw new ArgumentException("--delve requires --mode quick.");
            Write(new { kind = "environment", runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
                serverGC = System.Runtime.GCSettings.IsServerGC, configuration = typeof(GameState).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
                engine = typeof(GameState).Assembly.Location, engineMvid = typeof(GameState).Module.ModuleVersionId,
                warmups, samples, percentile = "nearest-rank", allocation = "GC.GetTotalAllocatedBytes(precise:true), process-wide" });
            foreach (var name in selected == "all" ? new[] { "small", "distant100", "distant442" } : new[] { selected })
            {
                fixture = name;
                var (size, count) = name switch { "small" => (12, 0), "distant100" => (120, 100), _ => (120, 442) };
                var definition = Fixture(size, count, UnitTypeIds.Beastmaster);
                if (mode is "create" or "all") Measure("create", () => GameEngine.CreateGame(definition));
                if (mode is "summon" or "all") Summon(definition);
                if (mode == "candidates")
                {
                    var state = GameEngine.CreateGame(definition);
                    state.MoveDone = true;
                    var actor = state.Units.Single(u => u.Id == "actor");
                    var candidates = Measure("summonCandidates", () =>
                        ((IEnumerable<Candidate>)SummonCandidates.Invoke(null, [state, actor])!).ToArray());
                    Write(new { kind = "result", fixture, candidates = candidates.Length });
                }
                if (mode is "dormant" or "all") Dormant(Fixture(size, count, UnitTypeIds.Rogue));
                if (mode is "quick" or "all")
                {
                    var quickDefinition = options.TryGetValue("--delve", out var path)
                        ? ScenarioDefinitionTransport.Decode(File.ReadAllText(path)) : definition;
                    if (options.ContainsKey("--delve")) fixture = "saved:" + Path.GetFileName(options["--delve"]);
                    Quick(quickDefinition);
                    if (options.ContainsKey("--delve")) break;
                }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Help() => Console.WriteLine("PlacementQueryBenchmark --fixture small|distant100|distant442|all --mode summon|candidates|create|dormant|quick|all --warmups N --samples N [--delve path]");

    // Setup, output and disposal are outside the timed/allocation interval. A zero-warmup
    // process reports its first invocation; this is not a measurement of process startup.
    private static T Measure<T>(string stage, Func<T> operation) => Measure(stage, () => 0, _ => operation());

    private static T Measure<TSetup, T>(string stage, Func<TSetup> setup, Func<TSetup, T> operation)
    {
        T result = default!;
        var times = new List<double>();
        var allocations = new List<long>();
        for (var run = -warmups; run < samples; run++)
        {
            var context = setup();
            try
            {
                var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
                var allocated = GC.GetTotalAllocatedBytes(true);
                var started = Stopwatch.GetTimestamp();
                result = operation(context);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
                if (run < 0) continue;
                times.Add(elapsed); allocations.Add(bytes);
                Write(new { kind = "sample", fixture, stage, run, ms = elapsed, allocatedBytes = bytes,
                    gc = new[] { GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2 } });
            }
            finally { (context as IDisposable)?.Dispose(); }
        }
        times.Sort(); allocations.Sort();
        var p50 = (int)Math.Ceiling(samples * .5) - 1;
        var p95 = (int)Math.Ceiling(samples * .95) - 1;
        Write(new { kind = "summary", fixture, stage, samples,
            ms = new { p50 = times[p50], p95 = times[p95], min = times[0], max = times[^1] },
            allocatedBytes = new { p50 = allocations[p50], p95 = allocations[p95], min = allocations[0], max = allocations[^1] } });
        return result;
    }

    private static void Summon(ScenarioDefinition definition)
    {
        var state = GameEngine.CreateGame(definition);
        state.Round = 1;
        state.ActiveToken = state.Units[0].Token;
        state.CurrentUnitId = "actor";
        state.ResolvingTokenUnitIds = ["actor"];
        state.Bag = [state.Units[1].Token];
        var before = GameEngine.RefreshChoices(state, Random, false).State;
        var stay = before.Pending!.Candidates.First(c => c.Kind == ActivationChoiceKind.Stay);
        var moved = Measure("postStayAdvance", () => GameEngine.Advance(before, new Choice(stay.Key), Random, false));
        var actor = moved.State.Units.Single(u => u.Id == "actor");
        var candidates = Measure("summonCandidates", () =>
            ((IEnumerable<Candidate>)SummonCandidates.Invoke(null, [moved.State, actor])!).ToArray());
        var summon = moved.State.Pending!.Candidates.First(c => c.Ability?.Definition.Mechanic is AdjacentUnitSummonMechanic { UnitTypeId: UnitTypeIds.Raptor });
        var summoned = Measure("summonAdvance", () => GameEngine.Advance(moved.State, new Choice(summon.Key), Random, false));
        Write(new { kind = "result", fixture, candidates = candidates.Length, postStayCandidates = moved.State.Pending.Candidates.Count,
            summonedUnits = summoned.State.Units.Count, summonEvents = summoned.Events.Count });
    }

    private static void Dormant(ScenarioDefinition definition)
    {
        var state = GameEngine.CreateGame(definition);
        Measure("quietRoundRogue", () => GameEngine.StartRound(state, Random, false));
        var quiet = Measure("dormantProximity", () => WakingUnits.Invoke(null, [state, null]));
        var actor = state.Physical.Figures.FindIndex(f => f.Id == "actor");
        state.Physical.Figures[actor] = state.Physical.Figures[actor] with
        { Position = definition.Board.Width < 15 ? new(7, 8) : new(14, 15) };
        Measure("dormantAlertChain", () => WakingUnits.Invoke(null, [state, null]));
        var alerted = Measure("roundWithAlert", () => GameEngine.StartRound(state, Random, false));
        Write(new { kind = "result", fixture, quietWoken = ((IReadOnlyList<string>)quiet!).Count,
            alertWoken = alerted.Events.Count(e => e.Kind == "UnitWoken") });
    }

    private static void Quick(ScenarioDefinition definition)
    {
        var raw = ScenarioDefinitionJson.ToJson(definition);
        var parsed = Measure("quick.parseValidate", () => ScenarioDefinitionJson.FromJson(raw));
        var prepared = Measure("quick.prepare", () => WorldQuickPlay.Prepare(parsed, ContentBundle.Canonical()));
        var transport = Measure("quick.encode", () => ScenarioDefinitionTransport.Encode(prepared));
        Measure("quick.createPrepared", () => GameEngine.CreateGame(prepared));
        var imported = Measure("quick.webImport", () => new PlaytestGame(Random),
            host => host.StartTransportScenario(host.Snapshot().Revision, transport));
        var browser = typeof(PlaytestGame).GetMethod("BrowserResponse", BindingFlags.NonPublic | BindingFlags.Instance)!;
        using var projectionHost = new PlaytestGame(Random);
        // BrowserResponse uses the host's imported content/state, so initialize this host
        // outside the measured projection operation as well.
        imported = projectionHost.StartTransportScenario(projectionHost.Snapshot().Revision, transport);
        var response = Measure("quick.browserProjection", () => (GameResponse)browser.Invoke(projectionHost, [imported])!);
        var bytes = Measure("quick.serialize", () => JsonSerializer.SerializeToUtf8Bytes(response, ResponseJson));
        Write(new { kind = "result", fixture, units = definition.Units.Count, importBytes = bytes.Length });
    }

    private static ScenarioDefinition Fixture(int size, int count, string actor)
    {
        List<UnitPlacement> units = [new(actor, "blue", new(2, 2), UnitId: "actor"),
            new(UnitTypeIds.Grunt, "red", new(8, 8), UnitId: "near", Dormant: true)];
        for (var i = 0; i < count; i++)
            units.Add(new(UnitTypeIds.Grunt, "red", new(15 + i % 100, 15 + i / 100), UnitId: "d" + i, Dormant: true));
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
