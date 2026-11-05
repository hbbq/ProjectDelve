using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

internal static class CivilizationInvestigation
{
    internal static void Run(string? directory)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        List<object> results = [];
        foreach (var (width, height) in new[] { (50, 50), (120, 100), (120, 120) })
        foreach (var preset in WorldGenerationPresets.All)
            Profile($"{width}x{height}-{preset.Id}", new(new(-17, -13, width, height), preset.Settings, 0x123456789ABCDEF0, new(), new()));
        foreach (var (name, terrain) in new[] { ("ExcavatedRock", new NaturalTerrainSettings(0, 0, 100)), ("ArtificialIslands", new(0, 100, 0)),
            ("DenseWoodland", new(100, 0, 0)), ("Mixed", new(40, 20, 30)) })
            Profile(name, new(new(-1, -69, 60, 50), terrain, 0xDE1FE, new(), new(PreparationIntensity: 3)));
        Profile("NarrowEdge", new(new(-121, -1, 21, 55), new(20, 10, 20), 991, new(), new(PreparationIntensity: 3)));
        if (directory is not null) File.WriteAllText(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions(json) { WriteIndented = true }));

        void Profile(string name, WorldGenerationRecipe recipe)
        {
            for (var i = 0; i < 3; i++) _ = WorldGenerator.Generate(recipe);
            List<GeneratedWorld> worlds = []; List<double> elapsed = []; List<long> allocations = [];
            for (var i = 0; i < 7; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
                worlds.Add(WorldGenerator.Generate(recipe)); elapsed.Add(clock.Elapsed.TotalMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            }
            var final = worlds[^1];
            var payload = new { final.Code, final.Window, final.VisualSeed, final.Fingerprint, final.Preview, final.Metrics, final.Diagnostics,
                definition = JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(final.Definition)),
                terrainDefinitions = ContentBundle.Canonical().Terrain, transport = ScenarioDefinitionTransport.Encode(final.Definition) };
            var record = new
            {
                name, recipe.Window.Width, recipe.Window.Height, final.Code, samples = 7, warmups = 3,
                generationMs = Median(elapsed), allocatedBytes = Median(allocations),
                stages = final.Metrics.Stages.Select(s => new { s.StepId,
                    milliseconds = Median(worlds.Select(w => w.Metrics.Stages.Single(t => t.StepId == s.StepId).Milliseconds)),
                    allocatedBytes = Median(worlds.Select(w => w.Metrics.Stages.Single(t => t.StepId == s.StepId).AllocatedBytes)) }).ToArray(),
                validationMs = Median(worlds.Select(w => w.Metrics.ConstructionValidationMilliseconds)), final.Metrics.ConstructionValidations,
                finalizationMs = Median(worlds.Select(w => w.Metrics.FinalizationMilliseconds)),
                responsePayloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, json).Length,
                previewPayloadBytes = JsonSerializer.SerializeToUtf8Bytes(final.Preview, json).Length,
                previewRawEstimateBytes = final.Preview.EstimatedRetainedBytes,
                previewRetainedManagedBytes = Retained(recipe),
                final.Context.CivilizationStats, failures = final.Context.PlanningFailures,
                operations = final.Context.Construction.Count, final.Fingerprint
            };
            results.Add(record); Console.WriteLine(JsonSerializer.Serialize(record, json));
            if (directory is not null) File.WriteAllText(Path.Combine(directory, name + ".response.json"), JsonSerializer.Serialize(payload, json));
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WorldPreview Preview(WorldGenerationRecipe recipe) => WorldGenerator.Generate(recipe).Preview;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Retained(WorldGenerationRecipe recipe)
    {
        var before = GC.GetTotalMemory(true); var preview = Preview(recipe); var bytes = GC.GetTotalMemory(true) - before;
        GC.KeepAlive(preview); return Math.Max(0, bytes);
    }
    private static T Median<T>(IEnumerable<T> source) where T : IComparable<T> { var values = source.Order().ToArray(); return values[values.Length / 2]; }
}
