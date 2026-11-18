using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Web.Tests;

public sealed class ExpeditionJourneyDiagnostic(ITestOutputHelper output)
{
    private const string OutputVariable = "PROJECTDELVE_EXPEDITION_JOURNEY_OUTPUT";
    private const string FullRegionsVariable = "PROJECTDELVE_EXPEDITION_JOURNEY_FULL_REGIONS";
    private const int Length = 24;
    private static readonly ulong[] Seeds = [17, 42, 889];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task ExportSpatialJourneys()
    {
        var directory = Environment.GetEnvironmentVariable(OutputVariable);
        if (string.IsNullOrWhiteSpace(directory)) return;
        var full = Environment.GetEnvironmentVariable(FullRegionsVariable);
        var fullRegions = full is null ? Length : int.TryParse(full, out var count) && count is >= 0 and <= Length
            ? count : throw new ArgumentException($"Set {FullRegionsVariable} to 0..{Length}.");
        Directory.CreateDirectory(directory);
        var content = ContentBundle.Canonical();
        var watch = Stopwatch.StartNew();
        var summaries = await Task.WhenAll(Seeds.Select(seed =>
            Task.Run(() => ExportSeed(directory, seed, fullRegions, content))));
        output.WriteLine($"Exported {Seeds.Length} × {Length} planned regions in {watch.Elapsed.TotalSeconds:F1}s wall clock.");
        foreach (var summary in summaries) output.WriteLine(summary);
    }

    private static string ExportSeed(string directory, ulong seed, int fullRegions, ContentBundle content)
    {
        var root = Path.Combine(directory, $"seed-{seed}");
        Directory.CreateDirectory(root);
        var baseline = ExpeditionWorld.Configure(seed);
        var profile = ExpeditionWorldProfile.CreateV1(baseline.NaturalTerrain);
        var sampler = new ExpeditionNatureSampler(seed, profile);
        var plans = new List<ExpeditionRegionPlan>(Length) { ExpeditionWorld.Initial(seed) };
        while (plans.Count < Length) plans.Add(ExpeditionWorld.Next(seed, plans[^1]));
        Assert.Equal(Length, plans.Select(plan => plan.Region).Distinct().Count());

        // Sample independently of the generation sequence, then compare each full
        // region's original samples to these exact global Cell reads.
        var sampled = plans.AsEnumerable().Reverse().ToDictionary(plan => plan.Region, plan =>
        {
            var window = ExpeditionWorld.RegionWindow(plan.Region);
            var terrain = Cells(window, (x, y) => sampler.Sample(x, y).TerrainTypeId);
            var budgets = Cells(window, sampler.CoverageAt);
            return (Terrain: terrain, Budgets: new ExpeditionCoverage(budgets.Average(c => c.Forest),
                budgets.Average(c => c.Rock), budgets.Average(c => c.Water)));
        });
        var regions = new List<object>(Length);
        var succeeded = 0;
        var milliseconds = 0d;
        (ExpeditionRegionPlan Plan, string Hash)? repeat = null;

        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            var recipe = ExpeditionWorld.RecipeFor(plan, baseline);
            if (index > 0)
            {
                var previous = plans[index - 1];
                var window = ExpeditionWorld.RegionWindow(previous.Region);
                Assert.Equal(1, Math.Abs(window.OffsetX + previous.ExitSeam.X - recipe.Window.OffsetX - plan.EntrySeam.X) +
                    Math.Abs(window.OffsetY + previous.ExitSeam.Y - recipe.Window.OffsetY - plan.EntrySeam.Y));
            }
            var (terrain, budgets) = sampled[plan.Region];
            var resolved = sampler.ResolveRegion(recipe);
            var localRecipe = resolved with { Seed = plan.LocalSeed };
            var center = new Cell(recipe.Window.OffsetX + (recipe.Window.Width - 1) / 2,
                recipe.Window.OffsetY + (recipe.Window.Height - 1) / 2);
            var fields = Enum.GetValues<ExpeditionField>().ToDictionary(field => JsonNamingPolicy.CamelCase.ConvertName(field.ToString()),
                field => sampler.FieldAt(field, center.X, center.Y));
            FinalRegion? final = null;
            string? error = null;
            object? failure = null;
            double? generationMilliseconds = null;
            if (index < fullRegions)
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    var execution = Generate(plan, recipe, content, profile);
                    Assert.Equal(terrain, execution.Context.OriginalSamples.Select(sample => sample.TerrainTypeId));
                    final = Capture(execution);
                    repeat ??= (plan, final.ScenarioSha256);
                    succeeded++;
                }
                catch (PlayReadyGenerationException defect)
                {
                    error = defect.Message;
                    failure = new { defect.Stage, defect.FailureKind, defect.BoardRevision, defect.ConfigurationJson, defect.DiagnosticsJson };
                }
                generationMilliseconds = watch.Elapsed.TotalMilliseconds;
                milliseconds += generationMilliseconds.Value;
            }
            regions.Add(new
            {
                Index = index + 1, plan.Region, Origin = new Cell(recipe.Window.OffsetX, recipe.Window.OffsetY),
                plan.EntryEdge, plan.EntrySeam, plan.ExitEdge, plan.ExitSeam, plan.NextRegion,
                LocalSeed = SeedText(plan.LocalSeed), TransitionSeed = SeedText(plan.TransitionSeed),
                RegionalRecipe = localRecipe.ToString(), RegionFieldCenter = center, Fields = fields,
                Parameters = new
                {
                    EnrichmentIntensity = resolved.Enrichment!.Intensity, resolved.Civilization!.Campsites, resolved.Civilization.Settlements,
                    resolved.Civilization.PreparationIntensity, RouteIntensity = resolved.Routes!.Intensity, resolved.Routes.ThroughRoutes,
                    resolved.Routes.Alternatives, PopulationDensity = resolved.Population!.Density, resolved.Population.Difficulty,
                    resolved.LootAbundance
                },
                SampledCoverage = budgets, BaseTerrain = terrain, BaseCoverage = Coverage(terrain), BaseTerrainCounts = Counts(terrain),
                FinalTerrain = final?.Terrain, FinalCoverage = final is null ? null : Coverage(final.Terrain),
                FinalTerrainCounts = final is null ? null : Counts(final.Terrain),
                Edges = final?.Edges, Features = final?.Features, ScenarioSha256 = final?.ScenarioSha256,
                GenerationMilliseconds = generationMilliseconds, Error = error, Failure = failure
            });
        }
        var repeatMatched = false;
        if (repeat is { } first)
        {
            var repeated = Generate(first.Plan, ExpeditionWorld.RecipeFor(first.Plan, baseline), content, profile);
            Assert.Equal(first.Hash, Fingerprint(repeated.Context.Materialize()));
            repeatMatched = true;
        }
        var path = Path.Combine(root, "journey.json");
        using var file = File.Create(path);
        JsonSerializer.Serialize(file, new
        {
            WorldSeed = SeedText(seed), BaselineRecipe = baseline.ToString(), Profile = profile, Stride = ExpeditionWorld.Stride,
            Content = "ContentBundle.Canonical() bundled typed catalog", FullRegionsRequested = fullRegions, FullRegionsSucceeded = succeeded,
            GenerationMilliseconds = milliseconds, RepeatedFirstSuccessfulScenarioMatched = repeatMatched, Regions = regions
        }, Json);
        return $"Seed {seed}: full {succeeded}/{fullRegions}, {milliseconds / 1000:F1}s sum of region timings. {path}";
    }

    private static WorldGenerationExecution Generate(ExpeditionRegionPlan plan, WorldGenerationRecipe recipe,
        ContentBundle content, ExpeditionWorldProfile profile) =>
        ExpeditionRegionGeometry.Execute(recipe, plan.EntrySeam, plan.ExitSeam, plan.ExitArea, plan.LocalSeed, content, profile: profile);

    private static FinalRegion Capture(WorldGenerationExecution execution)
    {
        var context = execution.Context;
        var scenario = context.Materialize();
        var board = context.ConstructionBoard!;
        var terrain = Enumerable.Range(0, context.OriginalSamples.Length).Select(board.TerrainAt).ToArray();
        var enriched = execution.Preview.Frames.Last(frame => frame.Stage.EndsWith("Enrichment", StringComparison.Ordinal));
        var enrichedCells = enriched.Terrain.Where((code, i) =>
            execution.Preview.TerrainPalette[code - 'A'] != context.OriginalSamples[i].TerrainTypeId).Count();
        var edges = scenario.Board.Edges.Where(edge => edge.Kind != EdgeKind.None).Select(edge =>
            new Edge(edge.Position, edge.Direction == EdgeDirection.Right
                ? edge.Position with { X = edge.Position.X + 1 } : edge.Position with { Y = edge.Position.Y + 1 }, edge.Kind)).ToArray();
        var features = new Features(context.Sites.Count, context.Sites.Count(site => site.Kind == GeneratedSiteKind.Campsite),
            context.Sites.Count(site => site.Kind == GeneratedSiteKind.Settlement), context.Sites.Sum(site => site.Buildings.Count),
            context.Sites.Count(site => site.MountainDungeon is not null), context.Routes.Count, scenario.Units.Count,
            context.SiteLootMarkers.Count, scenario.LootMarkers?.Count ?? 0, enrichedCells,
            context.Population?.Statistics.RequestedUnits ?? 0, context.Population?.Statistics.AcceptedEncounters ?? 0,
            scenario.Machines?.Count ?? 0, terrain.Where((id, i) => id != context.OriginalSamples[i].TerrainTypeId).Count());
        return new(terrain, edges, features, Fingerprint(scenario));
    }

    private static T[] Cells<T>(WorldWindow window, Func<int, int, T> read) =>
        (from y in Enumerable.Range(window.OffsetY, window.Height)
         from x in Enumerable.Range(window.OffsetX, window.Width) select read(x, y)).ToArray();

    private static ExpeditionCoverage Coverage(string[] terrain) => new(
        Percent(terrain, id => id is "tree" or "fruit-tree"), Percent(terrain, id => id == "solid-rock"),
        Percent(terrain, id => id is "water" or "wet-grass" or "rocky-water" or "stepping-stones"));

    private static double Percent(string[] terrain, Func<string, bool> match) => 100d * terrain.Count(match) / terrain.Length;
    private static Dictionary<string, int> Counts(string[] terrain) => terrain.GroupBy(id => id).OrderBy(group => group.Key, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    private static string Fingerprint(ScenarioDefinition scenario) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ScenarioDefinitionJson.ToJson(scenario))));
    private static string SeedText(ulong seed) => $"0x{seed:X16}";

    private sealed record FinalRegion(string[] Terrain, Edge[] Edges, Features Features, string ScenarioSha256);
    private sealed record Features(int Sites, int Campsites, int Settlements, int Buildings, int Dungeons, int Routes, int Monsters,
        int SiteLoot, int LootMarkers, int EnrichedCells, int RequestedMonsters, int Encounters, int Machines, int TerrainChangedCells);
}
