using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

internal static class PopulationInvestigation
{
    internal static void Run(string? directory)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        List<object> records = [];
        foreach (var (width, height) in new[] { (30, 24), (60, 50), (120, 100), (120, 120) })
        {
            var recipe = new WorldGenerationRecipe(new(-17, -13, width, height), new(10, 4, 5), 12345,
                new(), new(Campsites: 4, Settlements: 1), new(ThroughRoutes: 2, Profile: RouteProfileKind.Road),
                new(PopulationDensity.High, Difficulty: 5));
            _ = WorldGenerator.Generate(recipe);
            List<GeneratedWorld> runs = []; List<double> total = [];
            for (var i = 0; i < 3; i++)
            {
                var clock = Stopwatch.StartNew(); runs.Add(WorldGenerator.Generate(recipe)); total.Add(clock.Elapsed.TotalMilliseconds);
            }
            var world = runs[^1];
            if (runs.Any(w => w.Fingerprint != world.Fingerprint)) throw new InvalidOperationException("Population benchmark is not deterministic.");
            var stats = world.Context.Population!.Statistics;
            if (stats.Work > PopulationGenerator.MaxWork || stats.Attempts > PopulationGenerator.MaxAttempts ||
                stats.Units > PopulationGenerator.MaxUnits || stats.Groups > PopulationGenerator.MaxGroups)
                throw new InvalidOperationException("Population exceeded its hard limits.");
            var record = new
            {
                width, height, warmups = 1, samples = 3, world.Code,
                generationMs = Median(total), populationMs = Median(runs.Select(w => w.Metrics.Stages.Single(s => s.Stage == WorldStepKind.Population).Milliseconds)),
                populationAllocatedBytes = Median(runs.Select(w => w.Metrics.Stages.Single(s => s.Stage == WorldStepKind.Population).AllocatedBytes)),
                statistics = stats, kinds = world.Context.Population.Encounters.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count()),
                previewBytes = JsonSerializer.SerializeToUtf8Bytes(world.Preview, json).Length,
                previewRawEstimateBytes = world.Preview.EstimatedRetainedBytes,
                scenarioBytes = System.Text.Encoding.UTF8.GetByteCount(ScenarioDefinitionJson.ToJson(world.Definition)), world.Fingerprint
            };
            records.Add(record); Console.WriteLine(JsonSerializer.Serialize(record, json));
        }
        if (directory is not null) File.WriteAllText(Path.Combine(directory, "population-performance.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions(json) { WriteIndented = true }));
    }
    private static T Median<T>(IEnumerable<T> source) where T : IComparable<T> { var values = source.Order().ToArray(); return values[values.Length / 2]; }
}
