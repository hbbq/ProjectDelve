using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

if (args.Contains("--vision")) { VisionBenchmark.Run(); return; }
if (args.Contains("--vision-fixtures")) { VisionBenchmark.WriteFixtures(); return; }

// dotnet run --project tools/EngineResolutionBenchmark -c Release --artifacts-path .playwright-cli
// Fixed investigation fixture: 30x30 implicit floor, MOV 3/RNG 1/ATK 1/DEF 0/HP 1,
// ten Default Behavior monsters, one human Unit, blue-only FoW, seed 7241.
// Add -- --obstacles for a deterministic 35%-density interior rock fixture.
// --stronghold reproduces the 50x50 investigation with compiled canonical content.
// --state <snapshot.json> uses a detached GameState with its captured content instead.
string? Option(string key) => Array.IndexOf(args, key) is var i && i >= 0 ? args[i + 1] : null;
var warmups = int.Parse(Option("--warmups") ?? "5");
var sampleCount = int.Parse(Option("--samples") ?? "15");
if (warmups < 0 || sampleCount < 1) throw new ArgumentException("Invalid sample counts.");
var statePath = Option("--state");
var generated = args.Contains("--stronghold", StringComparer.Ordinal);
var legacySchedule = statePath is null && !generated;
var snapshotJson = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
var initial = statePath is not null ? JsonSerializer.Deserialize<GameState>(File.ReadAllText(statePath), snapshotJson)! :
    generated ? GameEngine.CreateGame(DungeonGenerator.Generate("DNG2-OS-50x50-O2-T2-N3-L3-B2-OR2-F1-QAll-S6DCCA420DCFB55FB").Definition) :
    Fixture(args.Contains("--obstacles", StringComparer.Ordinal));
var cold = Stopwatch.StartNew();
Resolve(initial, legacySchedule);
cold.Stop();
for (var i = 0; i < warmups; i++) Resolve(initial, legacySchedule);
// Allow background tiered compilation triggered by warmups to finish before the
// faster runs' short sample window. This is harness setup, never gameplay timing.
Thread.Sleep(1000);
var samples = new List<(double Total, double Decision, long Bytes)>();
var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
for (var i = 0; i < sampleCount; i++)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var watch = Stopwatch.StartNew();
    var result = Resolve(initial, legacySchedule);
    watch.Stop();
    samples.Add((watch.Elapsed.TotalMilliseconds, result.DecisionMs, GC.GetAllocatedBytesForCurrentThread() - allocated));
}
var gcCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - collections[g]).ToArray();
var check = Resolve(initial, legacySchedule, trace: true);
Console.WriteLine(JsonSerializer.Serialize(new
{
    runtime = Environment.Version.ToString(), serverGC = System.Runtime.GCSettings.IsServerGC,
    width = initial.Physical.Board.Width, height = initial.Physical.Board.Height,
    units = initial.Units.Count, terrainEntries = initial.Physical.Board.Terrain.Count,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
    coldMs = cold.Elapsed.TotalMilliseconds, warmups, samples = samples.Count,
    medianMs = samples.Select(s => s.Total).Order().ElementAt(samples.Count / 2),
    minMs = samples.Min(s => s.Total), maxMs = samples.Max(s => s.Total),
    decisionMedianMs = samples.Select(s => s.Decision).Order().ElementAt(samples.Count / 2),
    allocatedMedian = samples.Select(s => s.Bytes).Order().ElementAt(samples.Count / 2),
    gcCollections,
    check.Decisions, check.MonstersCompleted, check.Events, check.TraceDigest, check.RandomCalls
}));

static GameState Fixture(bool obstacles)
{
    var state = new GameState
    {
        Physical = new(new Board(30, 30, []), [new("hero", new(1, 1))]),
        Types = [new("monster", 3, 1, 1, 0, 1), new("hero", 0, 1, 1, 0, 1)],
        Units = [new("hero", "hero", "blue", 1)], ResourceDefinitions = [.. ResourceTypes.All],
        Controllers = [new(new("monster", "red"), ControllerKind.Automated), new(new("hero", "blue"), ControllerKind.Human)],
        FogOfWarSides = ["blue"]
    };
    for (var i = 0; i < 10; i++)
    {
        state.Units.Add(new("monster-" + i, "monster", "red", 1));
        state.Physical.Figures.Add(new("monster-" + i, new(28 - i % 5 * 2, 28 - i / 5 * 2)));
    }
    if (obstacles)
        for (var y = 3; y < 27; y++)
        for (var x = 3; x < 27; x++)
            if ((37 * x + 13 * y) % 100 < 35 && !state.Physical.Figures.Any(f => f.Position == new Cell(x, y)))
                state.Physical.Board.Terrain.Add(new(new(x, y), TerrainKind.SolidRock));
    FogOfWar.Refresh(state);
    return state;
}

static Measurement Resolve(GameState state, bool legacySchedule, bool trace = false)
{
    var random = new SeededRandom(state, legacySchedule);
    var provider = new TimedProvider();
    var result = GameEngine.StartRound(state, random);
    var entries = trace ? new List<string>() : null;
    var events = 0; var completed = 0; var advances = 0;
    while (true)
    {
        events += result.Events.Count;
        completed += result.Events.Count(e => e.Kind == "ActivationCompleted" && state.Units.Any(u => u.Id == e.UnitId &&
            state.ControllerFor(new ActivationToken(u.TypeId, u.SideId)) == ControllerKind.Automated));
        entries?.Add(JsonSerializer.Serialize(result));
        if (result.NextInput is not { } input || result.State.ControllerFor(input) != ControllerKind.Automated) break;
        result = GameEngine.Advance(result.State, provider, random);
        advances++;
    }
    return new(provider.Ticks * 1000.0 / Stopwatch.Frequency, advances, completed, events,
        entries is null ? null : Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', entries)))), random.Calls);
}
sealed record Measurement(double DecisionMs, int Decisions, int MonstersCompleted, int Events, string? TraceDigest, int RandomCalls);
sealed class TimedProvider : IDecisionProvider
{
    private readonly OrderAwareAutomatedProvider inner = new();
    public long Ticks;
    public string? Choose(DecisionRequest request, IGameplayQueries queries)
    {
        var start = Stopwatch.GetTimestamp();
        try { return inner.Choose(request, queries); }
        finally { Ticks += Stopwatch.GetTimestamp() - start; }
    }
}
sealed class SeededRandom(GameState initial, bool legacySchedule) : IRandomProvider
{
    private readonly Random random = new(7241);
    public int Calls;
    public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag)
    {
        Calls++;
        // Preserve the original 30x30 schedule; generated fixtures use investigation's
        // fixed-seed automated-first draws, stopping before a human decision.
        if (legacySchedule)
            return bag.FirstOrDefault(t => t.SideId == "red") ?? bag[0];
        var automated = bag.Where(t => initial.ControllerFor(t) == ControllerKind.Automated).ToArray();
        var choices = automated.Length > 0 ? automated : bag.ToArray();
        return choices[random.Next(choices.Length)];
    }
    public AttackFace RollAttackDie() { Calls++; return random.Next(2) == 0 ? AttackFace.Hit : AttackFace.Miss; }
    public DefenceFace RollDefenceDie() { Calls++; return random.Next(2) == 0 ? DefenceFace.Block : DefenceFace.Miss; }
    public int RollD6() { Calls++; return random.Next(1, 7); }
    public int RollD8() { Calls++; return random.Next(1, 9); }
    public int WorldShuffleIndex(int exclusiveMax) { Calls++; return random.Next(exclusiveMax); }
}
