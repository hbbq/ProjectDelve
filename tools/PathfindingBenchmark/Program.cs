using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using ProjectDelve.Web;

AppDomain.CurrentDomain.UnhandledException += (_, e) => { Console.Error.WriteLine(e.ExceptionObject); Environment.Exit(1); };

// Timing, profiling and full serialization traces must be separate runs. No live host is accessed.
string? Option(string key) => Array.IndexOf(args, key) is var i && i >= 0 ? args[i + 1] : null;
var samples = int.Parse(Option("--samples") ?? "10");
var warmups = int.Parse(Option("--warmups") ?? "3");
var seed = int.Parse(Option("--seed") ?? "1");
var filter = Option("--filter") ?? "";
var traceOnly = args.Contains("--trace-only");
var export = Option("--export");
var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, json));
Write(new { runtime = Environment.Version.ToString(), Environment.ProcessorCount, serverGC = System.Runtime.GCSettings.IsServerGC,
    module = typeof(GameState).Assembly.ManifestModule.ModuleVersionId, seed, warmups, samples });
var cases = new List<(string Name, GameState State)>();
if (Option("--delve") is { } delve)
{
    var content = ContentBundle.Canonical();
    if (Option("--canonical") is { } directory)
    {
        var canonical = new CanonicalContent(directory);
        content = Option("--workspace") is { } workspace ? new ContentWorkspace(workspace, () => canonical.Bundle).Resolve().Definitions : canonical.Bundle;
    }
    var transport = File.ReadAllText(delve).Trim();
    Write(new { fixtureSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(transport))) });
    var definition = ScenarioDefinitionTransport.Decode(transport, null, content);
    cases.Add(("exact-patrol", GameEngine.CreateGame(definition, content)));
    var normal = definition with { Orders = definition.Orders!.Select(o => o.Kind == OrderKind.PatrolRoute ? o with { Kind = OrderKind.Normal, Route = null } : o).ToList() };
    cases.Add(("exact-normal", GameEngine.CreateGame(normal, content)));
}
if (Option("--states") is { } states)
    foreach (var path in Directory.GetFiles(states, "*.state.json").Order(StringComparer.Ordinal))
        cases.Add((Path.GetFileName(path).Replace(".state.json", ""), JsonSerializer.Deserialize<GameState>(File.ReadAllText(path), json)!));
if (args.Contains("--worlds"))
    foreach (var (w, h) in new[] { (30, 24), (60, 50), (120, 120) })
    {
        // Generated natural terrain, with benchmark-owned placements and Orders.
        // This deliberately does not substitute for the exact saved DELVE1 fixture.
        var world = WorldGenerator.Generate(new WorldGenerationRecipe(new(0, 0, w, h), new NaturalTerrainSettings(), 0x123456789ABCDEF0));
        foreach (var distance in new[] { "near", "far", "unreachable" })
        foreach (var order in new[] { "patrol", "normal" })
            cases.Add(($"world-{w}x{h}-{order}-{distance}", WorldFixture(world.Definition.Board, order, distance)));
    }
foreach (var (name, state) in cases.Where(c => c.Name.Contains(filter, StringComparison.Ordinal)))
{
    if (export is not null) { Directory.CreateDirectory(export); File.WriteAllText(Path.Combine(export, name + ".state.json"), JsonSerializer.Serialize(state, json)); }
    Write(new { fixture = name, width = state.Physical.Board.Width, height = state.Physical.Board.Height, units = state.Units.Count });
    if (!traceOnly)
    {
        for (var i = 0; i < warmups; i++) Resolve(state, false, false);
        Thread.Sleep(1000); // JIT settling, outside measurement.
        for (var i = 0; i < samples; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread(); var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var sw = Stopwatch.StartNew(); var result = Resolve(state, false, false); sw.Stop();
            Write(new { fixture = name, sample = i, ms = sw.Elapsed.TotalMilliseconds, result.ProviderMs, bytes = GC.GetAllocatedBytesForCurrentThread() - before,
                gc = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - gc[g]).ToArray(), result.Choices });
        }
    }
    var trace = Resolve(state, true, false);
    var continued = Resolve(state, true, true);
    if (trace.Digest != continued.Digest) throw new InvalidOperationException($"Save/continue changed {name}.");
    Write(new { fixture = name, trace.Digest, trace.Draws, trace.Choices, trace.Events, trace.Decisions, continuationMatches = true });
}

Run Resolve(GameState initial, bool trace, bool roundTrip)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    void Record(object value) { if (trace) hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(value, json)); }
    var dice = new Dice(seed); var provider = new TimedProvider();
    var decisions = new List<string>(); var events = 0;
    EngineResult Capture(EngineResult r)
    {
        Record(r); events += r.Events.Count;
        // Include event-time FoW observations, not just the final authoritative state.
        if (trace) foreach (var step in r.ResolutionSteps) foreach (var side in step.StateAfter.FogOfWarSides)
            Record(FogOfWar.Observe(step.StateAfter, side));
        return roundTrip ? r with { State = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(r.State, json), json)! } : r;
    }
    EngineResult Automated(EngineResult r)
    {
        var guard = 0;
        while (r.NextInput is { } input && r.State.ControllerFor(input) == ControllerKind.Automated)
        {
            if (++guard > 500) throw new InvalidOperationException("Resolution did not reach a boundary.");
            r = Capture(GameEngine.Advance(r.State, provider, dice));
        }
        return r;
    }
    var result = Automated(Capture(GameEngine.StartRound(initial, dice)));
    // Full first Beastmaster activation plus the following Round Start for exact fixture.
    if (initial.Units.Any(u => u.TypeId == UnitTypeIds.Beastmaster))
    {
        var guard = 0;
        while (result.NextInput is { } input && result.State.ControllerFor(input) == ControllerKind.Human && guard++ < 10)
        {
            if (input.UnitId is { } id && result.State.Units.Single(u => u.Id == id).TypeId != UnitTypeIds.Beastmaster) break;
            var choice = input.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.Stay) ??
                input.Candidates.FirstOrDefault(c => c.Kind == ActivationChoiceKind.EndTurn) ??
                (input.Kind == DecisionKind.SelectUnit ? input.Candidates.First() : throw new InvalidOperationException("Expected Stay/End Turn."));
            decisions.Add(choice.Key);
            result = Automated(Capture(GameEngine.Advance(result.State, new Submitted(choice.Key), dice)));
            if (choice.Kind == ActivationChoiceKind.EndTurn) break;
        }
        if (result.State.RoundComplete) result = Automated(Capture(GameEngine.StartRound(result.State, dice)));
    }
    Record(provider.Choices); Record(decisions); Record(dice.Calls);
    return new(provider.Ticks * 1000.0 / Stopwatch.Frequency, provider.Choices, events, decisions, dice.Calls,
        trace ? Convert.ToHexStringLower(hash.GetHashAndReset()) : null);
}

GameState WorldFixture(BoardDefinition source, string order, string distance)
{
    var w = source.Width; var h = source.Height;
    var cells = source.Cells.ToList();
    if (distance == "unreachable")
    {
        cells.RemoveAll(c => c.Position.X == w / 2);
        cells.AddRange(Enumerable.Range(0, h).Select(y => new CellDefinition(new(w / 2, y), TerrainKind.SolidRock)));
    }
    var terrain = cells.ToDictionary(c => c.Position, c => c.TerrainTypeId is { } id ? TerrainCatalog.Find(TerrainCatalog.All, id) : TerrainCatalog.All.Single(t => t.LegacyKind == c.Terrain));
    var defaultTerrain = source.DefaultTerrainTypeId is { } defaultId ? TerrainCatalog.Find(TerrainCatalog.All, defaultId) : TerrainCatalog.All.Single(t => t.LegacyKind == source.DefaultTerrain);
    bool Passable(Cell p) => p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h && terrain.GetValueOrDefault(p, defaultTerrain).Passable;
    IEnumerable<Cell> Neighbors(Cell p) => new[] { new Cell(p.X, p.Y - 1), new(p.X - 1, p.Y), new(p.X + 1, p.Y), new(p.X, p.Y + 1) };
    var unvisited = Enumerable.Range(0, h).SelectMany(y => Enumerable.Range(0, w).Select(x => new Cell(x, y))).Where(Passable).ToHashSet();
    var components = new List<List<Cell>>();
    while (unvisited.Count > 0)
    {
        var first = unvisited.OrderBy(p => p.Y).ThenBy(p => p.X).First(); var component = new List<Cell>(); var queue = new Queue<Cell>(); queue.Enqueue(first); unvisited.Remove(first);
        while (queue.TryDequeue(out var p)) { component.Add(p); foreach (var next in Neighbors(p)) if (unvisited.Remove(next)) queue.Enqueue(next); }
        components.Add(component);
    }
    var region = components.Where(c => distance != "unreachable" || c.All(p => p.X < w / 2)).OrderByDescending(c => c.Count).First();
    var actor = region.OrderBy(p => p.Y).ThenBy(p => p.X).First();
    int D(Cell p) => Math.Abs(p.X - actor.X) + Math.Abs(p.Y - actor.Y);
    var target = distance == "near" ? region.Where(p => D(p) >= 5).OrderBy(D).ThenBy(p => p.Y).ThenBy(p => p.X).First() :
        (distance == "unreachable" ? components.SelectMany(c => c).Where(p => p.X > w / 2) : region).OrderByDescending(D).ThenBy(p => p.Y).ThenBy(p => p.X).First();
    var d = new ScenarioDefinition {
        Board = source with { Cells = cells }, UnitTypeIds = ["benchmark-actor", "benchmark-target"],
        UnitTypes = [new("benchmark-actor", 3, 1, 1, 0, 1), new("benchmark-target", 0, 0, 0, 0, 1)],
        Units = [new("benchmark-actor", "red", actor, UnitId: "actor"), new("benchmark-target", "blue", target, UnitId: "target")],
        Agency = [new("benchmark-actor", "red", ControllerKind.Automated), new("benchmark-target", "blue", ControllerKind.Human)],
        Objectives = [new("red", ScenarioObjective.NoObjective), new("blue", ScenarioObjective.NoObjective)], Loot = LootSettings.Empty,
        Waypoints = [new("goal", new(target.X, target.Y, 1, 1))], Routes = [new("route", ["goal"], RouteMode.Loop)],
        Orders = order == "patrol" ? [new("benchmark-actor", "red", OrderKind.FollowRoute, Route: new("route"))] : []
    };
    GameState state;
    try { state = GameEngine.CreateGame(d); }
    catch { Console.Error.WriteLine($"Fixture {w}x{h} {order} {distance}: actor={actor} target={target} default={source.DefaultTerrain}/{source.DefaultTerrainTypeId} cells={JsonSerializer.Serialize(cells.Where(c => c.Position == actor || c.Position == target))}"); throw; }
    state.ActivationTypeOrder = ["benchmark-actor", "benchmark-target"]; return state;
}
sealed record Run(double ProviderMs, List<string> Choices, int Events, List<string> Decisions, List<string> Draws, string? Digest);
sealed class Submitted(string key) : IDecisionProvider { public string Choose(DecisionRequest r, IGameplayQueries q) => key; }
sealed class TimedProvider : IDecisionProvider
{
    readonly OrderAwareAutomatedProvider provider = new(); public long Ticks; public List<string> Choices = [];
    public string? Choose(DecisionRequest r, IGameplayQueries q) { var start = Stopwatch.GetTimestamp(); var key = provider.Choose(r, q); Ticks += Stopwatch.GetTimestamp() - start; Choices.Add($"{r.UnitId}:{r.Kind}:{key}"); return key; }
}
sealed class Dice(int seed) : IRandomProvider
{
    readonly Random random = new(seed); public List<string> Calls = [];
    public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) { var token = bag[random.Next(bag.Count)]; Calls.Add("token:" + JsonSerializer.Serialize(token)); return token; }
    int Roll(int max) { var n = random.Next(max); Calls.Add($"d{max}:{n}"); return n; }
    public AttackFace RollAttackDie() => Roll(6) < 3 ? AttackFace.Hit : AttackFace.Miss;
    public DefenceFace RollDefenceDie() => Roll(6) < 2 ? DefenceFace.Block : DefenceFace.Miss;
    public int RollD6() => Roll(6) + 1; public int RollD8() => Roll(8) + 1; public int WorldShuffleIndex(int max) => Roll(max);
}
