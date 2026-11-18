using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using ProjectDelve.Web;

// Paired runs use identical recipes and this executable, with the two captured
// Engine/Generation DLL sets. No generator metadata is added to gameplay state.
internal static class PassageQualityInvestigation
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    internal static void Run(string[] args)
    {
        if (args is ["--self-test"]) { SelfTest(); return; }
        if (args.Length != 1) throw new ArgumentException("Usage: --passage-quality <output-directory> | --passage-quality --self-test");
        var directory = Path.GetFullPath(args[0]); Directory.CreateDirectory(directory);
        var content = ContentBundle.Canonical();
        var cases = (from size in new[] { (W: 30, H: 24), (W: 60, H: 50) }
                     from preset in WorldGenerationPresets.All.Where(p => p.Id is "Woodland" or "RockyWilds" or "LakeCountry" or "RiverValley")
                     from seed in new ulong[] { 1, 2, 3, 13, 42, 99 }
                     select (preset.Id, Recipe: new WorldGenerationRecipe(new(-17, -13, size.W, size.H), preset.Settings,
                         seed, preset.Enrichment, preset.Civilization, preset.Routes, preset.Population, "Classic"))).ToArray();
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new
        {
            runtime = Environment.Version.ToString(), cases = cases.Select(c => new { preset = c.Id, recipe = c.Recipe.ToString() }),
            assemblies = new[] { typeof(WorldGenerator).Assembly, typeof(GameEngine).Assembly }.Select(a => new
            { a.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) }),
            method = new
            {
                graph = "Engine static ordinary 1x1 Land geometry, Machines block, figures ignored, Closed Doors open only on detached measurement board",
                thin = "passable Cell belonging to no traversable 2x2 square (four internal cardinal Edges)",
                chain = "connected component of thin Cells having degree exactly 2 in the full potential-access graph; long means >=8 Cells",
                substantial = "removing the Cell leaves at least two connected pieces each >=20 Cells in its original component",
                bottleneckRun = "connected run of substantial thin degree-2 Cells; long means >=8 Cells",
                clearance = "counts of thin Cells and traversable 2x2 participation in declared route corridors, Building exterior rings and outside portal Cells",
                disturbance = "final materialized terrain differs from original natural sample; counts grouped by original terrain",
                validation = "Engine CreateGame, published QuickPlay Prepare and Engine CreateGame; errors retained per recipe"
            }
        }, Json));
        using var writer = new StreamWriter(Path.Combine(directory, "samples.jsonl"));
        foreach (var (name, recipe) in cases)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var world = WorldGenerator.Generate(recipe, content);
                var state = GameEngine.CreateGame(world.Definition, content);
                var playReady = PlayReadyScenarioSetup.Validate(world.Definition, state);
                if (playReady.Status != "Valid")
                    throw new InvalidOperationException(string.Join(", ", playReady.Diagnostics.Select(d => d.Message)));
                var prepared = WorldQuickPlay.Prepare(world.Definition, content);
                _ = GameEngine.CreateGame(prepared, content);
                var board = state.Physical.Board;
                var open = board with { Edges = board.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() };
                var width = board.Width; var height = board.Height; var count = width * height;
                var analysis = AccessibilityAnalysis.Capture(open);
                var profile = new AccessibilityProfile(Footprint.OneByOne, [MovementKinds.Land], 1);
                Cell CellAt(int i) => new(i % width, i / width);
                var pass = Enumerable.Range(0, count).Select(i => analysis.CanStop(profile, CellAt(i))).ToArray();
                var graph = Enumerable.Range(0, count).Select(i => pass[i] ? Neighbors(i, width, height)
                    .Where(j => pass[j] && open.EdgeBetween(CellAt(i), CellAt(j)).Passable()).ToArray() : []).ToArray();
                var metrics = Measure(graph, width, height, pass);
                var thin = metrics.Thin; var substantial = metrics.Substantial;
                object Clearance(IEnumerable<int> source)
                {
                    var cells = source.Distinct().Where(i => i >= 0 && i < count).ToArray();
                    return new { cells = cells.Length, passable = cells.Count(i => pass[i]), thin = cells.Count(i => thin[i]),
                        twoByTwo = cells.Count(i => pass[i] && !thin[i]), substantial = cells.Count(i => substantial[i]) };
                }
                var sites = world.Context.Sites;
                var siteClearance = sites.Select(s => new
                {
                    s.Id, s.Kind, exterior = Clearance(s.Exterior.Indices(width)),
                    approach = Clearance(s.Entrances.Select(p => p.OutsideIndex).Concat(s.AccessPorts.Select(p => p.OutwardIndex)))
                }).ToArray();
                var buildings = sites.SelectMany(s => s.Buildings).Select(b =>
                {
                    var footprint = b.Footprint.Indices(width).ToHashSet();
                    return new { b.Id, area = footprint.Count, rooms = b.Rooms.Count,
                        exterior = Clearance(footprint.SelectMany(i => Neighbors(i, width, height)).Where(i => !footprint.Contains(i))),
                        approach = Clearance(b.Portals.Where(p => !footprint.Contains(p.OutsideIndex)).Select(p => p.OutsideIndex)) };
                }).ToArray();
                var disturbance = Enumerable.Range(0, count).Where(i => board.TerrainDefinitionAt(CellAt(i)).Id != world.Context.OriginalSamples[i].TerrainTypeId)
                    .GroupBy(i => world.Context.OriginalSamples[i].TerrainTypeId).ToDictionary(g => g.Key, g => g.Count());
                var enrichment = world.Preview.Frames.Where(f => f.Stage is "ForestEnrichment" or "RockEnrichment")
                    .ToDictionary(f => f.Stage, f => f.ChangedCells.Length);
                var repair = world.Context.GetType().GetProperty("MandatoryRepair", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(world.Context);
                var record = new
                {
                    name, recipe = world.Code, world.Fingerprint, width, height, milliseconds = watch.Elapsed.TotalMilliseconds,
                    walkable = pass.Count(p => p), thin = thin.Count(p => p), metrics.LongChains, metrics.MaxChain,
                    substantialArticulations = substantial.Count(p => p), metrics.LongBottleneckRuns, metrics.MaxBottleneckRun,
                    routes = world.Context.RouteNetwork?.Routes.Select(r => new { r.Id, r.Purpose, r.RealizedWidth, r.VerifiedFootprint,
                        clearance = Clearance(r.Corridor.Indices(width)), r.Preparation }),
                    buildings, siteClearance, sites = sites.Count, dungeons = sites.Count(s => s.MountainDungeon is not null),
                    dungeonRooms = sites.Sum(s => s.MountainDungeon?.Rooms.Count ?? 0), disturbance, enrichment,
                    repair = repair is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(repair, repair.GetType(), Json),
                    validation = playReady.Status, failures = world.Context.PlanningFailures.Select(f => new { f.Generator, f.Code, f.Reason })
                };
                writer.WriteLine(JsonSerializer.Serialize(record, Json));
                Console.Error.WriteLine($"{name} {width}x{height} seed {recipe.Seed}: thin {record.thin}, chains {metrics.LongChains}, substantial {record.substantialArticulations}");
            }
            catch (Exception ex)
            {
                writer.WriteLine(JsonSerializer.Serialize(new { name, recipe = recipe.ToString(), error = ex.ToString() }, Json));
                Console.Error.WriteLine($"ERROR {name} seed {recipe.Seed}: {ex.Message}");
            }
            writer.Flush();
        }
    }

    internal sealed record Topology(bool[] Thin, bool[] Substantial, int LongChains, int MaxChain, int LongBottleneckRuns, int MaxBottleneckRun);
    internal static Topology Measure(int[][] graph, int width, int height, bool[]? passable = null)
    {
        var count = graph.Length;
        var square = new bool[count];
        for (var y = 0; y + 1 < height; y++)
        for (var x = 0; x + 1 < width; x++)
        {
            var a = y * width + x; var b = a + 1; var c = a + width; var d = c + 1;
            if (graph[a].Contains(b) && graph[a].Contains(c) && graph[b].Contains(d) && graph[c].Contains(d))
                square[a] = square[b] = square[c] = square[d] = true;
        }
        var thin = Enumerable.Range(0, count).Select(i => (passable?[i] ?? graph[i].Length > 0) && !square[i]).ToArray();
        // Iterative Tarjan traversal avoids dependence on process stack depth on large crops.
        var discovery = new int[count]; var low = new int[count]; var sizes = new int[count];
        var parent = Enumerable.Repeat(-1, count).ToArray(); var next = new int[count];
        var parts = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        var substantial = new bool[count]; var clock = 0;
        for (var root = 0; root < count; root++)
        {
            if (discovery[root] != 0 || graph[root].Length == 0) continue;
            List<int> component = []; var stack = new Stack<int>(); stack.Push(root);
            discovery[root] = low[root] = ++clock; sizes[root] = 1; component.Add(root);
            while (stack.TryPeek(out var v))
            {
                if (next[v] < graph[v].Length)
                {
                    var u = graph[v][next[v]++];
                    if (discovery[u] == 0)
                    {
                        parent[u] = v; discovery[u] = low[u] = ++clock; sizes[u] = 1;
                        component.Add(u); stack.Push(u);
                    }
                    else if (u != parent[v]) low[v] = Math.Min(low[v], discovery[u]);
                }
                else
                {
                    stack.Pop(); var p = parent[v];
                    if (p < 0) continue;
                    sizes[p] += sizes[v]; low[p] = Math.Min(low[p], low[v]);
                    if (low[v] >= discovery[p]) parts[p].Add(sizes[v]);
                }
            }
            foreach (var v in component)
            {
                var remainder = component.Count - 1 - parts[v].Sum();
                substantial[v] = parts[v].Count(s => s >= 20) + (remainder >= 20 ? 1 : 0) >= 2;
            }
        }
        int[] Runs(Func<int, bool> include)
        {
            var visited = new bool[count]; List<int> lengths = [];
            for (var i = 0; i < count; i++)
            {
                if (visited[i] || !include(i)) continue;
                var queue = new Queue<int>(); queue.Enqueue(i); visited[i] = true; var length = 0;
                while (queue.TryDequeue(out var v))
                {
                    length++;
                    foreach (var u in graph[v]) if (!visited[u] && include(u)) { visited[u] = true; queue.Enqueue(u); }
                }
                lengths.Add(length);
            }
            return lengths.ToArray();
        }
        var chains = Runs(i => thin[i] && graph[i].Length == 2);
        var bottlenecks = Runs(i => substantial[i] && thin[i] && graph[i].Length == 2);
        return new(thin, substantial, chains.Count(n => n >= 8), chains.DefaultIfEmpty().Max(),
            bottlenecks.Count(n => n >= 8), bottlenecks.DefaultIfEmpty().Max());
    }

    private static IEnumerable<int> Neighbors(int i, int width, int height)
    {
        if (i >= width) yield return i - width;
        if (i % width > 0) yield return i - 1;
        if (i % width + 1 < width) yield return i + 1;
        if (i + width < width * height) yield return i + width;
    }

    private static void SelfTest()
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        int[][] Graph(int w, int h, Func<int, bool> pass) => Enumerable.Range(0, w * h)
            .Select(i => pass(i) ? Neighbors(i, w, h).Where(pass).ToArray() : []).ToArray();
        var square = Measure(Graph(4, 4, _ => true), 4, 4);
        Check(!square.Thin.Any(v => v) && !square.Substantial.Any(v => v), "Open square has no thin Cells or cuts");
        var corridor = Measure(Graph(60, 1, _ => true), 60, 1);
        Check(corridor.LongChains == 1 && corridor.MaxChain == 58, "Degree-two chain excludes both endpoints");
        Check(corridor.Substantial.Count(v => v) == 20 && corridor.MaxBottleneckRun == 20, "Both cut pieces must have at least 20 Cells");
        // Two 5x5 rooms connected by a 10-Cell corridor; room door Cells are cuts but participate in room squares.
        var rooms = Measure(Graph(20, 5, i => i % 20 < 5 || i % 20 >= 15 || i / 20 == 2), 20, 5);
        Check(rooms.MaxChain == 10 && rooms.MaxBottleneckRun == 10, "Room/corridor interaction");
        var cycle = Graph(8, 8, i => i / 8 is 0 or 7 || i % 8 is 0 or 7);
        Check(!Measure(cycle, 8, 8).Substantial.Any(v => v), "Cycle has no articulation");
        Check(Measure([[]], 1, 1, [true]).Thin[0], "Isolated passable Cell is thin");
        var separated = Graph(61, 1, i => i != 30);
        Check(!Measure(separated, 61, 1).Substantial.Any(v => v), "Other components cannot inflate a cut's pieces");
        var brokenSquare = Graph(2, 2, _ => true);
        brokenSquare[0] = brokenSquare[0].Where(i => i != 1).ToArray();
        brokenSquare[1] = brokenSquare[1].Where(i => i != 0).ToArray();
        Check(Measure(brokenSquare, 2, 2).Thin.All(v => v), "Blocked internal Edge prevents 2x2 clearance");
        Console.WriteLine("Passage quality self-test passed (rooms, corridor, room connection, cycle, isolated Cell, disconnected components, blocked Edge).");
    }
}
