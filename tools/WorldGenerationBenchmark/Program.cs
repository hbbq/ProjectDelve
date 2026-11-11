using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using ProjectDelve.Web;

// Detached, package-free benchmark, deliberately outside the solution. Run sequentially:
// dotnet run --project tools/WorldGenerationBenchmark -c Release --artifacts-path .world-benchmark -- --fixtures .playwright-cli/large-map-baseline
const int warmups = 3, samples = 7, lookupPasses = 3;
string? fixtures = null;
var enrichment = false;
var civilization = false;
var routes = false;
var population = false;
var settlements = false;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--enrichment") { enrichment = true; continue; }
    if (args[i] == "--civilization") { civilization = true; continue; }
    if (args[i] == "--routes") { routes = true; continue; }
    if (args[i] == "--population") { population = true; continue; }
    if (args[i] == "--settlements") { settlements = true; continue; }
    if (args[i] != "--fixtures" || ++i == args.Length)
        throw new ArgumentException("Usage: WorldGenerationBenchmark [--settlements|--population|--routes|--civilization|--enrichment] [--fixtures directory]");
    fixtures = Path.GetFullPath(args[i]);
}
if (fixtures is not null) Directory.CreateDirectory(fixtures);
if (enrichment) { EnrichmentInvestigation.Run(fixtures); return; }
if (civilization) { CivilizationInvestigation.Run(fixtures); return; }
if (routes) { RoutesInvestigation.Run(fixtures); return; }
if (population) { PopulationInvestigation.Run(fixtures); return; }
if (settlements) { SettlementInvestigation.Run(fixtures); return; }
var httpJson = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
};
var canonical = ContentBundle.Canonical();
var cases = new List<(int Width, int Height, string Preset, NaturalTerrainSettings Settings)>();
foreach (var (width, height) in new[] { (30, 24), (50, 50), (120, 100), (120, 120) })
    cases.Add((width, height, "Default", new()));
foreach (var (width, height) in new[] { (120, 100), (120, 120) })
foreach (var preset in WorldGenerationPresets.All.Where(p => p.Id is "Grassland" or "Woodland" or "LakeCountry" or "RockyWilds"))
    cases.Add((width, height, preset.Id, preset.Settings));

// Emit every fixture before profiling so browser inspection can use captured baselines.
var worlds = new List<(WorldGenerationRecipe Recipe, GeneratedWorld World)>();
foreach (var (width, height, preset, settings) in cases)
{
    var recipe = new WorldGenerationRecipe(new(-17, -13, width, height), settings, 0x123456789ABCDEF0);
    var world = WorldGenerator.Generate(recipe);
    worlds.Add((recipe, world));
    if (fixtures is null) continue;
    var stem = $"{width}x{height}-{preset}";
    var catalog = ScenarioContentReferences.ScenarioCatalog(canonical, world.Definition).Definitions;
    var state = GameEngine.CreateGame(world.Definition);
    var response = new
    {
        definition = JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(world.Definition)),
        terrainDefinitions = catalog.Terrain, world.Code,
        transport = ScenarioDefinitionTransport.Encode(world.Definition),
        world.VisualSeed, world.Window, world.Fingerprint, world.Diagnostics
    };
    File.WriteAllText(Path.Combine(fixtures, stem + ".response.json"), JsonSerializer.Serialize(response, httpJson));
    File.WriteAllText(Path.Combine(fixtures, stem + ".state.json"), JsonSerializer.Serialize(state, httpJson));
    Console.Error.WriteLine($"Fixture {stem}: {fixtures}");
}
Console.Error.WriteLine("Fixtures complete; warm median profiling begins.");

for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
{
    var (width, height, preset, _) = cases[caseIndex];
    var (recipe, initialWorld) = worlds[caseIndex];
    var world = initialWorld;
    var sampling = new List<double>(); var validation = new List<double>();
    var generation = Measure(() =>
    {
        world = WorldGenerator.Generate(recipe);
        sampling.Add(world.Metrics.SamplingMilliseconds);
        validation.Add(world.Metrics.FinalizationMilliseconds);
    });
    var materialization = Measure(() => { _ = GameEngine.CreateGame(world.Definition); });
    var context = new WorldGenerationContext(recipe.World, recipe.Window); context.SampleNaturalTerrain();
    var projection = Measure(() => { _ = context.Materialize(); });
    // Shared typed catalog composition/validation boundary, excluding Engine creation.
    var contentValidation = Measure(() => { _ = ScenarioContentReferences.ScenarioCatalog(canonical, world.Definition); });
    var json = ScenarioDefinitionJson.ToJson(world.Definition);
    var transport = ScenarioDefinitionTransport.Encode(world.Definition);
    var jsonSerialization = Measure(() => { _ = ScenarioDefinitionJson.ToJson(world.Definition); });
    // Public readers deliberately include authoritative Engine validation.
    var jsonDeserialization = Measure(() => { _ = ScenarioDefinitionJson.FromJson(json); });
    var transportEncode = Measure(() => { _ = ScenarioDefinitionTransport.Encode(world.Definition); });
    var transportDecode = Measure(() => { _ = ScenarioDefinitionTransport.Decode(transport); });
    var board = GameEngine.CreateGame(world.Definition).Physical.Board;
    var visualSeed = BoardAppearance.DeriveSeed("benchmark", board);
    var appearance = Measure(() => { _ = BoardAppearance.DeriveSeed("benchmark", board); });
    long lookupChecksum = 0;
    var lookups = Measure(() =>
    {
        long sum = 0;
        for (var pass = 0; pass < lookupPasses; pass++)
        for (var y = 0; y < board.Height; y++)
        for (var x = 0; x < board.Width; x++)
            sum += board.TerrainDefinitionAt(new(x, y)).Id.Length + (int)board.TerrainAt(new(x, y));
        lookupChecksum = sum;
    });
    var result = JsonSerializer.Serialize(new
    {
        width, height, preset, code = recipe.ToString(), runtime = Environment.Version.ToString(), configuration =
#if DEBUG
            "Debug",
#else
            "Release",
#endif
        samples, warmups,
        generationMs = generation.Milliseconds, generationBytes = generation.Bytes,
        materializationMs = materialization.Milliseconds, materializationBytes = materialization.Bytes,
        projectionMs = projection.Milliseconds, projectionBytes = projection.Bytes,
        samplingMs = Median(sampling.Skip(warmups)), validationAndSerializationMs = Median(validation.Skip(warmups)),
        contentValidationMs = contentValidation.Milliseconds, contentValidationBytes = contentValidation.Bytes,
        jsonSerializationMs = jsonSerialization.Milliseconds, jsonSerializationBytes = jsonSerialization.Bytes,
        jsonDeserializationMs = jsonDeserialization.Milliseconds, jsonDeserializationBytes = jsonDeserialization.Bytes,
        transportEncodeMs = transportEncode.Milliseconds, transportEncodeBytes = transportEncode.Bytes,
        transportDecodeMs = transportDecode.Milliseconds, transportDecodeBytes = transportDecode.Bytes,
        appearanceMs = appearance.Milliseconds, appearanceBytes = appearance.Bytes,
        denseLookupsMs = lookups.Milliseconds, denseLookupsBytes = lookups.Bytes,
        lookupPasses, lookupChecksum, ordinaryVisualSeed = visualSeed,
        sparseOverrides = world.Definition.Board.Cells.Count,
        jsonBytes = Encoding.UTF8.GetByteCount(json), transportCharacters = transport.Length,
        coverage = world.Diagnostics.ActualCoverage, world.Fingerprint
    });
    Console.WriteLine(result);
    if (fixtures is not null) File.AppendAllText(Path.Combine(fixtures, "benchmark.jsonl"), result + Environment.NewLine);
}

static (double Milliseconds, long Bytes) Measure(Action action)
{
    for (var i = 0; i < warmups; i++) action();
    var elapsed = new List<double>(); var allocated = new List<long>();
    for (var i = 0; i < samples; i++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        action();
        elapsed.Add(watch.Elapsed.TotalMilliseconds); allocated.Add(GC.GetAllocatedBytesForCurrentThread() - before);
    }
    return (Median(elapsed), Median(allocated));
}

static T Median<T>(IEnumerable<T> values) where T : IComparable<T>
{
    var sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
}
