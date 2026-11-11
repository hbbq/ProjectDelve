using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

internal static class RoutesInvestigation
{
    internal static void Run(string? directory)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        List<object> results = [];
        foreach (var (width, height) in new[] { (50, 50), (120, 100), (120, 120) })
        foreach (var preset in WorldGenerationPresets.All)
            Profile($"{width}x{height}-{preset.Id}", new(new(-17, -13, width, height), preset.Settings, 0x123456789ABCDEF0, new(), new(),
                preset.Routes with { ThroughRoutes = ContentDensity.Medium, Alternatives = ContentDensity.Medium, WaterCrossings = preset.Id is "LakeCountry" or "RiverValley" }));
        Profile("Mixed", new(new(-1, -69, 120, 100), new(35, 20, 30), 991, new(), new(), new(ThroughRoutes: ContentDensity.Medium, Profile: RouteProfileKind.Road, Alternatives: ContentDensity.Medium, WaterCrossings: true)));
        Profile("ThroughOnly", new(new(-51, -1, 120, 100), new(75, 0, 10), 14, new(), null, new(ThroughRoutes: ContentDensity.Medium, Profile: RouteProfileKind.ForestTrail, Alternatives: ContentDensity.Medium)));
        Profile("RockPassages", new(new(-17, -13, 50, 50), new(0, 0, 95), 24, new(), null, new(ThroughRoutes: ContentDensity.Medium, Profile: RouteProfileKind.RockPassage, Alternatives: ContentDensity.Medium)));
        Profile("Narrow", new(new(-121, -1, 21, 55), new(35, 10, 15), 991, new(), new(), new(Profile: RouteProfileKind.ForestTrail)));
        Profile("SiteNetworkOnly", new(new(0, 0, 60, 60), new(15, 4, 5), 42, new(), new(), new(ThroughRoutes: ContentDensity.None, Alternatives: ContentDensity.Low)));
        if (directory is not null) File.WriteAllText(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions(json) { WriteIndented = true }));

        void Profile(string name, WorldGenerationRecipe recipe)
        {
            for (var i = 0; i < 3; i++) _ = WorldGenerator.Generate(recipe);
            List<GeneratedWorld> worlds = []; List<double> elapsed = []; List<long> allocations = [];
            for (var i = 0; i < 5; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
                worlds.Add(WorldGenerator.Generate(recipe)); elapsed.Add(clock.Elapsed.TotalMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            }
            var final = worlds[^1]; var network = final.Context.RouteNetwork!;
            var payload = new { final.Code, final.Window, final.VisualSeed, final.Fingerprint, final.Preview, final.Metrics, final.Diagnostics,
                definition = JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(final.Definition)),
                terrainDefinitions = ContentBundle.Canonical().Terrain, transport = ScenarioDefinitionTransport.Encode(final.Definition) };
            var record = new
            {
                name, recipe.Window.Width, recipe.Window.Height, final.Code, samples = 5, warmups = 3,
                generationMs = Median(elapsed), allocatedBytes = Median(allocations),
                stages = final.Metrics.Stages.Select(s => new { s.StepId, milliseconds = Median(worlds.Select(w => w.Metrics.Stages.Single(t => t.StepId == s.StepId).Milliseconds)),
                    allocatedBytes = Median(worlds.Select(w => w.Metrics.Stages.Single(t => t.StepId == s.StepId).AllocatedBytes)) }).ToArray(),
                planningMs = Median(worlds.Select(w => w.Metrics.RouteSearchMilliseconds)), materializationMs = Median(worlds.Select(w => w.Metrics.RouteMaterializationMilliseconds)),
                validationMs = Median(worlds.Select(w => w.Metrics.ConstructionValidationMilliseconds)), final.Metrics.ConstructionValidations,
                responsePayloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, json).Length,
                previewPayloadBytes = JsonSerializer.SerializeToUtf8Bytes(final.Preview, json).Length,
                previewRawEstimateBytes = final.Preview.EstimatedRetainedBytes, previewRetainedManagedBytes = Retained(recipe),
                network.Statistics, network.Alternatives, sites = final.Context.Sites.Count, junctions = network.Junctions.Count, sharedSegments = network.SharedSegments.Count,
                unresolved = network.Intents.Count(i => i.Status == RouteIntentStatus.Unresolved), remote = network.Intents.Count(i => i.Status == RouteIntentStatus.DeliberatelyRemote),
                final.Fingerprint
            };
            results.Add(record); Console.WriteLine(JsonSerializer.Serialize(record, json));
            if (directory is not null) File.WriteAllText(Path.Combine(directory, name + ".response.json"), JsonSerializer.Serialize(payload, json));
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static WorldPreview Preview(WorldGenerationRecipe recipe) => WorldGenerator.Generate(recipe).Preview;
    [MethodImpl(MethodImplOptions.NoInlining)] private static long Retained(WorldGenerationRecipe recipe)
    {
        var before = GC.GetTotalMemory(true); var preview = Preview(recipe); var delta = GC.GetTotalMemory(true) - before; GC.KeepAlive(preview); return Math.Max(0, delta);
    }
    private static T Median<T>(IEnumerable<T> source) where T : IComparable<T> { var sorted = source.Order().ToArray(); return sorted[sorted.Length / 2]; }
}
