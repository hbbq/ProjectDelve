using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

// Detached benchmark, deliberately outside the solution. Run sequentially after tests:
// dotnet run --project tools/WorldGenerationBenchmark --artifacts-path .world-benchmark
foreach (var (width, height) in new[] { (30, 24), (50, 50), (120, 100) })
{
    var recipe = new WorldGenerationRecipe(new(-17, -13, width, height), new(), 0x123456789ABCDEF0);
    for (var i = 0; i < 3; i++) _ = WorldGenerator.Generate(recipe);
    var generations = new List<double>(); var allocations = new List<long>();
    var sampling = new List<double>(); var validation = new List<double>();
    var materializations = new List<double>(); var materializationAllocations = new List<long>();
    var projections = new List<double>(); var projectionAllocations = new List<long>();
    GeneratedWorld world = null!;
    for (var i = 0; i < 7; i++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        world = WorldGenerator.Generate(recipe);
        generations.Add(watch.Elapsed.TotalMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        sampling.Add(world.Diagnostics.SamplingMilliseconds); validation.Add(world.Diagnostics.MaterializationMilliseconds);
        before = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
        _ = GameEngine.CreateGame(world.Definition);
        materializations.Add(watch.Elapsed.TotalMilliseconds); materializationAllocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        var context = new WorldGenerationContext(recipe.World, recipe.Window); context.SampleNaturalTerrain();
        before = GC.GetAllocatedBytesForCurrentThread(); watch.Restart(); _ = context.Materialize();
        projections.Add(watch.Elapsed.TotalMilliseconds); projectionAllocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
    }
    var json = ScenarioDefinitionJson.ToJson(world.Definition);
    var transport = ScenarioDefinitionTransport.Encode(world.Definition);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        width, height, runtime = Environment.Version.ToString(), configuration =
#if DEBUG
            "Debug",
#else
            "Release",
#endif
        samples = 7, warmups = 3,
        generationMs = Median(generations), generationBytes = Median(allocations),
        materializationMs = Median(materializations), materializationBytes = Median(materializationAllocations),
        projectionMs = Median(projections), projectionBytes = Median(projectionAllocations),
        samplingMs = Median(sampling), validationAndSerializationMs = Median(validation),
        sparseOverrides = world.Definition.Board.Cells.Count,
        jsonBytes = Encoding.UTF8.GetByteCount(json), transportCharacters = transport.Length,
        coverage = world.Diagnostics.ActualCoverage, world.Fingerprint
    }));
}

static T Median<T>(IEnumerable<T> values) where T : IComparable<T> => values.Order().ElementAt(3);
