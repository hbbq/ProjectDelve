using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

internal static class EnrichmentInvestigation
{
    internal static void Run(string? directory)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        List<object> results = [];
        foreach (var (width, height) in new[] { (50, 50), (120, 100), (120, 120) })
        foreach (var preset in WorldGenerationPresets.All)
        {
            var recipe = new WorldGenerationRecipe(new(-17, -13, width, height), preset.Settings, 0x123456789ABCDEF0, new());
            for (var i = 0; i < 3; i++) _ = WorldGenerator.Generate(recipe);
            var worlds = new List<GeneratedWorld>(); var elapsed = new List<double>(); var allocated = new List<long>();
            for (var i = 0; i < 7; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
                var world = WorldGenerator.Generate(recipe); elapsed.Add(clock.Elapsed.TotalMilliseconds);
                allocated.Add(GC.GetAllocatedBytesForCurrentThread() - before); worlds.Add(world);
            }
            var final = worlds[^1]; var payload = Response(final);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, json).Length;
            var previewBytes = JsonSerializer.SerializeToUtf8Bytes(final.Preview, json).Length;
            var stageSamples = final.Metrics.Stages.ToDictionary(s => s.StepId,
                s => worlds.Select(w => w.Metrics.Stages.Single(measurement => measurement.StepId == s.StepId)).ToArray());
            worlds.Clear();
            var retainedHeap = MeasureRetainedPreview(recipe);
            GC.KeepAlive(final);
            var record = new
            {
                preset = preset.Id, width, height, code = final.Code, samples = 7, warmups = 3,
                generationMs = Median(elapsed), allocatedBytes = Median(allocated),
                stages = final.Metrics.Stages.Select(stage => new
                {
                    stage.StepId, milliseconds = Median(worldsForStage(stage.StepId).Select(s => s.Milliseconds)),
                    allocatedBytes = Median(worldsForStage(stage.StepId).Select(s => s.AllocatedBytes))
                }).ToArray(),
                previewPayloadBytes = previewBytes, responsePayloadBytes = bytes,
                previewRawDataEstimateBytes = final.Preview.EstimatedRetainedBytes, previewRetainedManagedBytes = retainedHeap,
                finalizationMs = final.Metrics.FinalizationMilliseconds,
                beforeWalkable = final.Preview.Frames[0].Diagnostics.WalkableArea,
                afterWalkable = final.Preview.Frames[^1].Diagnostics.WalkableArea,
                beforeLargest = final.Preview.Frames[0].Diagnostics.LargestComponentArea,
                afterLargest = final.Preview.Frames[^1].Diagnostics.LargestComponentArea,
                featureKinds = final.Context.Features.GroupBy(f => f.Kind).ToDictionary(g => g.Key, g => g.Count()),
                routingSearches = final.Context.RoutingSearches, routingVisitedCells = final.Context.RoutingVisitedCells,
                final.Fingerprint
            };
            // Measurements are captured separately from reproducible stage diagnostics.
            results.Add(record); Console.WriteLine(JsonSerializer.Serialize(record, json));
            if (directory is not null) File.WriteAllText(Path.Combine(directory, $"{width}x{height}-{preset.Id}.response.json"), JsonSerializer.Serialize(payload, json));
            IEnumerable<WorldStageMeasurement> worldsForStage(string id) => stageSamples[id];
        }
        if (directory is not null)
        {
            File.WriteAllText(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions(json) { WriteIndented = true }));
            // The original fixed-seed gallery remains part of the evaluation corpus.
            using var gallery = JsonDocument.Parse(File.ReadAllText("assets/previews/world-generator-v1.json"));
            foreach (var entry in gallery.RootElement.EnumerateArray())
            {
                var recipe = WorldGenerationRecipe.Parse(entry.GetProperty("code").GetString()!) with { Enrichment = new() };
                var world = WorldGenerator.Generate(recipe);
                File.WriteAllText(Path.Combine(directory, entry.GetProperty("name").GetString() + ".response.json"), JsonSerializer.Serialize(Response(world), json));
            }
        }

        object Response(GeneratedWorld world) => new
        {
            world.Code, world.Window, world.VisualSeed, world.Fingerprint, world.Preview, world.Metrics, world.Diagnostics,
            definition = JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(world.Definition)),
            terrainDefinitions = ContentBundle.Canonical().Terrain, transport = ScenarioDefinitionTransport.Encode(world.Definition)
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WorldPreview RetainPreview(WorldGenerationRecipe recipe) => WorldGenerator.Generate(recipe).Preview;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureRetainedPreview(WorldGenerationRecipe recipe)
    {
        var baseline = GC.GetTotalMemory(true);
        var preview = RetainPreview(recipe);
        var delta = GC.GetTotalMemory(true) - baseline;
        GC.KeepAlive(preview); return Math.Max(0, delta);
    }
    private static T Median<T>(IEnumerable<T> values) where T : IComparable<T> { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
}
