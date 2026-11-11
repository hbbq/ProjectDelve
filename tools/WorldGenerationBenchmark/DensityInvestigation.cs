using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Generation;

internal static class DensityInvestigation
{
    // JSON-lines sweep. Override any axis: --sizes=30x24,60x50 --seeds=42,991 --levels=Low,VeryHigh --presets=Grassland,Woodland
    internal static void Run(string[] args)
    {
        var options = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length == 2 ? a[1] : throw new ArgumentException("Expected --name=value."));
        string[] Axis(string key, string fallback) => options.GetValueOrDefault(key, fallback).Split(',');
        var mode = options.GetValueOrDefault("--mode", "All");
        if (mode is not ("All" or "Sites" or "Through" or "Exploration")) throw new ArgumentException("Unknown density sweep mode.");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        foreach (var size in Axis("--sizes", "30x24,60x50,80x80,120x120"))
        foreach (var seed in Axis("--seeds", "42,991").Select(ulong.Parse))
        foreach (var density in Axis("--levels", "None,Low,Medium,High,VeryHigh").Select(Enum.Parse<ContentDensity>))
        foreach (var presetId in Axis("--presets", "Grassland,Woodland,RockyWilds"))
        {
            var dimensions = size.Split('x'); var width = int.Parse(dimensions[0]); var height = int.Parse(dimensions[1]);
            var preset = WorldGenerationPresets.All.Single(p => p.Id == presetId);
            var recipe = new WorldGenerationRecipe(new(Width: width, Height: height), preset.Settings, seed, preset.Enrichment,
                new(Campsites: mode is "All" or "Sites" ? density : ContentDensity.Low, Settlements: mode is "All" or "Sites" ? density : ContentDensity.Low),
                preset.Routes with { ThroughRoutes = mode is "All" or "Through" ? density : ContentDensity.Low, Alternatives = mode is "All" or "Exploration" ? density : ContentDensity.Low });
            var timer = Stopwatch.StartNew(); var world = WorldGenerator.Generate(recipe); timer.Stop();
            var sites = world.Context.Sites; var final = world.Preview.Frames[^1];
            var centers = sites.Select(s => (X: s.Footprint.Bounds.X + s.Footprint.Bounds.Width / 2.0, Y: s.Footprint.Bounds.Y + s.Footprint.Bounds.Height / 2.0)).ToArray();
            var nearest = centers.Select((a, i) => centers.Where((_, j) => i != j).Select(b => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2))).DefaultIfEmpty(0).Min()).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                width, height, seed, density, mode, preset = presetId, world.Code,
                campsites = final.CampsiteDensity, settlements = final.SettlementDensity,
                throughRoutes = final.ThroughRouteDensity, explorationLinks = final.ExplorationLinkDensity,
                families = sites.Where(s => s.SettlementLayout is not null).GroupBy(s => s.SettlementLayout!.Family).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                occupiedQuadrants = centers.Select(c => (c.X >= width / 2.0 ? 1 : 0) + (c.Y >= height / 2.0 ? 2 : 0)).Distinct().Count(),
                meanNearestSiteDistance = nearest.Length == 0 ? 0 : nearest.Average(),
                construction = world.Context.CivilizationStats, routes = world.Context.RouteNetwork?.Statistics,
                routeComponents = world.Context.RouteNetwork?.Topology.Components.Count,
                totalMilliseconds = timer.Elapsed.TotalMilliseconds, world.Metrics.Stages, world.Fingerprint
            }, json));
        }
    }
}
