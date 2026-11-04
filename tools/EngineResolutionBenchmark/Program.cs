using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ProjectDelve.Engine;

// dotnet run --project tools/EngineResolutionBenchmark -c Release --artifacts-path .playwright-cli
// Fixed investigation fixture: 30x30 implicit floor, MOV 3/RNG 1/ATK 1/DEF 0/HP 1,
// ten Default Behavior monsters, one human Unit, blue-only FoW, seed 7241.
// Add -- --obstacles for a deterministic 35%-density interior rock fixture.
var initial = Fixture(args.Contains("--obstacles", StringComparer.Ordinal));
var cold = Stopwatch.StartNew();
Resolve(initial);
cold.Stop();
for (var i = 0; i < 5; i++) Resolve(initial);
// Allow background tiered compilation triggered by warmups to finish before the
// faster runs' short sample window. This is harness setup, never gameplay timing.
Thread.Sleep(1000);
var samples = new List<(double Total, double Decision, long Bytes)>();
var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
for (var i = 0; i < 15; i++)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var watch = Stopwatch.StartNew();
    var result = Resolve(initial);
    watch.Stop();
    samples.Add((watch.Elapsed.TotalMilliseconds, result.DecisionMs, GC.GetAllocatedBytesForCurrentThread() - allocated));
}
var gcCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - collections[g]).ToArray();
var check = Resolve(initial, trace: true);
Console.WriteLine(JsonSerializer.Serialize(new
{
    runtime = Environment.Version.ToString(), serverGC = System.Runtime.GCSettings.IsServerGC,
    obstacleCells = initial.Physical.Board.Terrain.Count,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
    coldMs = cold.Elapsed.TotalMilliseconds, warmups = 5, samples = samples.Count,
    medianMs = samples.Select(s => s.Total).Order().ElementAt(7),
    minMs = samples.Min(s => s.Total), maxMs = samples.Max(s => s.Total),
    decisionMedianMs = samples.Select(s => s.Decision).Order().ElementAt(7),
    allocatedMedian = samples.Select(s => s.Bytes).Order().ElementAt(7),
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

static Measurement Resolve(GameState state, bool trace = false)
{
    var random = new SeededRandom();
    var provider = new TimedProvider();
    var result = GameEngine.StartRound(state, random);
    var entries = trace ? new List<string>() : null;
    var events = 0; var completed = 0; var advances = 0;
    while (true)
    {
        events += result.Events.Count;
        completed += result.Events.Count(e => e.Kind == "ActivationCompleted" && e.UnitId?.StartsWith("monster-") == true);
        entries?.Add(JsonSerializer.Serialize(result));
        if (result.NextInput is not { Token.SideId: "red" }) break;
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
sealed class SeededRandom : IRandomProvider
{
    private readonly Random random = new(7241);
    public int Calls;
    public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag)
    {
        Calls++;
        return bag.FirstOrDefault(t => t.SideId == "red") ?? bag[0];
    }
    public AttackFace RollAttackDie() { Calls++; return random.Next(2) == 0 ? AttackFace.Hit : AttackFace.Miss; }
    public DefenceFace RollDefenceDie() { Calls++; return random.Next(2) == 0 ? DefenceFace.Block : DefenceFace.Miss; }
    public int RollD6() { Calls++; return random.Next(1, 7); }
}
