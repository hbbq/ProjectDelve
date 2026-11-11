using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

internal static class SettlementInvestigation
{
    internal static void Run(string? directory)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        List<object> records = [];
        foreach (var (width, height) in new[] { (30, 24), (60, 50), (80, 80), (120, 120) })
        foreach (var seed in new ulong[] { 1, 2, 3, 4 })
        {
            var recipe = new WorldGenerationRecipe(new(Width: width, Height: height), new(10, 4, 5), seed,
                new(), new(Campsites: 1, Settlements: 2), new(ThroughRoutes: 2, Profile: RouteProfileKind.Road),
                new(PopulationDensity.Medium, 4));
            _ = WorldGenerator.Generate(recipe);
            List<GeneratedWorld> runs = []; List<double> elapsed = [];
            for (var sample = 0; sample < 3; sample++)
            {
                var clock = Stopwatch.StartNew(); runs.Add(WorldGenerator.Generate(recipe)); elapsed.Add(clock.Elapsed.TotalMilliseconds);
            }
            var world = runs[^1];
            if (runs.Any(r => r.Fingerprint != world.Fingerprint)) throw new InvalidOperationException("Settlement generation was not deterministic.");
            var validation = Stopwatch.StartNew(); _ = GameEngine.CreateGame(world.Definition); validation.Stop();
            var sites = world.Context.Sites.Where(s => s.Kind == GeneratedSiteKind.Settlement).ToArray();
            var record = new
            {
                width, height, seed, warmups = 1, samples = 3, world.Code, world.Fingerprint,
                totalMs = Median(elapsed), civilizationMs = Median(runs.Select(r => r.Metrics.Stages.Single(s => s.Stage == WorldStepKind.Civilization).Milliseconds)),
                constructionValidationMs = Median(runs.Select(r => r.Metrics.ConstructionValidationMilliseconds)),
                scenarioValidationMs = validation.Elapsed.TotalMilliseconds, world.Metrics.ConstructionValidations,
                world.Context.CivilizationStats, acceptedSettlements = sites.Length, rejectedSettlements = 2 - sites.Length,
                families = sites.GroupBy(s => s.SettlementLayout!.Family).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                routeAttachments = world.Context.RouteNetwork?.SiteAttachments.Where(a => sites.Any(s => s.Id == a.SiteId)).ToArray(),
                connectionRequests = sites.Sum(s => s.ConnectionRequests.Count),
                failures = world.Context.PlanningFailures,
                layouts = sites.Select(s => new { s.Id, s.SettlementLayout, s.PopulationIntent })
            };
            records.Add(record);
            Console.WriteLine(JsonSerializer.Serialize(new { width, height, seed, record.totalMs, record.civilizationMs, record.acceptedSettlements, record.families }));
            if (directory is not null)
            {
                var payload = new { world.Code, world.Window, world.VisualSeed, world.Fingerprint, world.Preview, world.Metrics, world.Diagnostics,
                    definition = JsonSerializer.Deserialize<JsonElement>(ScenarioDefinitionJson.ToJson(world.Definition)),
                    terrainDefinitions = ContentBundle.Canonical().Terrain, transport = ScenarioDefinitionTransport.Encode(world.Definition) };
                File.WriteAllText(Path.Combine(directory, $"{width}x{height}-seed-{seed}.response.json"), JsonSerializer.Serialize(payload, json));
            }
        }
        if (directory is not null) File.WriteAllText(Path.Combine(directory, "settlement-performance.json"), JsonSerializer.Serialize(records, json));
    }
    private static double Median(IEnumerable<double> values) => values.Order().ElementAt(1);
}
