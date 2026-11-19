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

/// <summary>Opt-in measurement, not an assertion about mutable canonical content balance.</summary>
public sealed class ExpeditionIntroCalibration(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void ExportSeededComparisons()
    {
        var directory = Environment.GetEnvironmentVariable("PROJECTDELVE_INTRO_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var count = ReadInt("PROJECTDELVE_INTRO_SEED_COUNT", 100, 1, 200);
        var start = ReadInt("PROJECTDELVE_INTRO_SEED_START", 0, 0, 1000000);
        var indices = (Environment.GetEnvironmentVariable("PROJECTDELVE_INTRO_INDICES") ?? "0")
            .Split(',').Select(int.Parse).Distinct().ToArray();
        Assert.All(indices, i => Assert.InRange(i, 0, 100));
        var variant = Environment.GetEnvironmentVariable("PROJECTDELVE_INTRO_VARIANT") ?? "baseline";
        Assert.Contains(variant, new[] { "baseline", "intro", "both" });
        var variants = variant == "both" ? new[] { "baseline", "intro" } : [variant];
        var recipes = Environment.GetEnvironmentVariable("PROJECTDELVE_INTRO_EXTREMES") == "1"
            ? new[] { "default", "off", "low", "high" } : ["default"];
        var catalog = Environment.GetEnvironmentVariable("PROJECTDELVE_INTRO_CATALOG") ?? "csharp";
        Assert.Contains(catalog, new[] { "csharp", "published" });
        var content = catalog == "published"
            ? new CanonicalContent(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../ProjectDelve.Web/Content/Canonical"))).Bundle
            : ContentBundle.Canonical();
        var timer = Stopwatch.StartNew();
        var rows = 0;
        // One durable file per sample allows interruption/restart without losing completed measurements.
        foreach (var number in Enumerable.Range(start, count))
        {
            var seed = (ulong)number;
            var plan = ExpeditionWorld.Initial(seed);
            for (var index = 0; index <= indices.Max(); index++, plan = ExpeditionWorld.Next(seed, plan))
            {
                if (!indices.Contains(index)) continue;
                foreach (var name in recipes)
                foreach (var currentVariant in variants)
                {
                    var path = Path.Combine(directory, $"{currentVariant}-{seed}-{index}-{name}.json");
                    if (File.Exists(path)) continue;
                    var baseline = ExpeditionWorld.Configure(seed);
                    baseline = name switch
                    {
                        "off" => baseline with { Population = new(PopulationDensity.Off, 1) },
                        "low" => baseline with { Population = new(PopulationDensity.Low, 1) },
                        "high" => baseline with { Population = new(PopulationDensity.High, 5) },
                        _ => baseline
                    };
                    var profile = ExpeditionWorldProfile.Create(baseline.NaturalTerrain);
                    if (currentVariant == "baseline") profile = profile with { PopulationVersion = null };
                    var recipe = ExpeditionWorld.RecipeFor(plan, baseline);
                    var watch = Stopwatch.StartNew();
                    object sample;
                    try
                    {
                        var execution = ExpeditionRegionGeometry.Execute(recipe, plan.EntrySeam, plan.ExitSeam,
                            plan.ExitArea, plan.LocalSeed, content, profile: profile);
                        sample = new { Seed = seed, Index = index, Recipe = name, Variant = currentVariant, Catalog = catalog,
                            Input = recipe.ToString(), Profile = profile, Error = (string?)null,
                            Milliseconds = watch.Elapsed.TotalMilliseconds, Metrics = Measure(execution, content, plan, recipe, profile) };
                    }
                    catch (PlayReadyGenerationException error)
                    {
                        sample = new { Seed = seed, Index = index, Recipe = name, Variant = currentVariant, Catalog = catalog,
                            Input = recipe.ToString(), Profile = profile, Error = error.Message,
                            error.Stage, error.FailureKind, Milliseconds = watch.Elapsed.TotalMilliseconds };
                    }
                    var temporary = path + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(sample, Json));
                    File.Move(temporary, path);
                    rows++;
                    output.WriteLine($"{currentVariant} seed={seed} index={index} recipe={name}: {watch.Elapsed.TotalSeconds:F2}s");
                }
            }
        }
        output.WriteLine($"Wrote {rows} samples in {timer.Elapsed.TotalSeconds:F1}s to {directory}");
    }

    private static object Measure(WorldGenerationExecution execution, ContentBundle content, ExpeditionRegionPlan plan,
        WorldGenerationRecipe recipe, ExpeditionWorldProfile profile)
    {
        var context = execution.Context;
        var scenario = context.Materialize();
        var population = context.Population;
        var units = scenario.Units.Where(u => u.SideId == HeroesVsMonsters.Monsters).ToArray();
        var threats = PopulationComposition.Pool(content.Units.Select(u => u.Id)).ToDictionary(p => p.UnitTypeId, p => p.Threat);
        // Red Dragon has no Pool rating: report it separately and conservatively count it in the 5+ bucket.
        threats[UnitTypeIds.RedDragon] = 5;
        int Threat(UnitPlacement u) => threats.TryGetValue(u.UnitTypeId, out var value) ? value :
            throw new InvalidOperationException($"No generation threat metadata for {u.UnitTypeId}.");
        int Distance(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        var concentrations = units.Select(u => units.Where(other => Distance(u.Anchor, other.Anchor) <= 6).Sum(Threat)).ToArray();
        var encounters = population?.Encounters ?? [];
        var normal = new ExpeditionNatureSampler(recipe.Seed!.Value, profile).ResolveRegion(recipe).Population!;
        var formation = context.Deployment!.PlacementOrder.Take(5).Select(i => new Cell(i % scenario.Board.Width, i / scenario.Board.Width)).ToArray();
        int ArrivalDistance(UnitPlacement unit) => FootprintGeometry.OccupiedCells(
            content.Units.Single(t => t.Id == unit.UnitTypeId).Footprint, unit.Anchor)
            .Min(cell => formation.Min(hero => Distance(cell, hero)));
        return new
        {
            NormalPopulation = normal,
            AdjustedPopulation = ExpeditionPopulationIntroduction.For(profile, recipe.Window)?.Adjust(normal) ?? normal,
            Hostiles = units.Length, TotalThreat = units.Sum(Threat),
            MeanThreat = units.Length == 0 ? 0 : units.Average(Threat),
            Threat4Plus = units.Count(u => Threat(u) >= 4),
            UnratedRedDragonsCountedAs5Plus = units.Count(u => u.UnitTypeId == UnitTypeIds.RedDragon),
            Ranged = units.Count(u => content.Units.Single(t => t.Id == u.UnitTypeId).Rng > 1),
            MaximumLocalThreatRadius6 = concentrations.DefaultIfEmpty().Max(),
            MaximumEncounterThreat = encounters.Select(e => e.Units.Sum(Threat)).DefaultIfEmpty().Max(),
            MaximumAlertComponentUnits = population?.AlertComponents.Select(c => c.UnitIds.Count).DefaultIfEmpty().Max() ?? 0,
            NearArrivalThreatRadius10 = units.Where(u => Distance(u.Anchor, plan.EntrySeam) <= 10).Sum(Threat),
            NearestHostileFootprintDistance = units.Length == 0 ? (int?)null : units.Min(ArrivalDistance),
            HostilesWithin8OfFormation = units.Count(u => ArrivalDistance(u) <= 8),
            ThreatWithin8OfFormation = units.Where(u => ArrivalDistance(u) <= 8).Sum(Threat),
            RangedWithin8OfFormation = units.Count(u => ArrivalDistance(u) <= 8 && content.Units.Single(t => t.Id == u.UnitTypeId).Rng > 1),
            ArrivalExposurePoints = ExpeditionPopulationIntroduction.ArrivalCost(units,
                content.Units.ToDictionary(t => t.Id), formation),
            LootMarkers = scenario.LootMarkers?.Count ?? 0,
            population?.Statistics, Encounters = encounters.Count,
            Types = units.GroupBy(u => u.UnitTypeId).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            ScenarioSha256 = Hash(ScenarioDefinitionJson.ToJson(scenario)),
            OriginalTerrainSha256 = Hash(string.Join('\n', context.OriginalSamples.Select(s => s.TerrainTypeId))),
            GeographySha256 = Hash(JsonSerializer.Serialize(new { scenario.Board, context.Sites, context.Routes, context.Deployment }, Json))
        };
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static int ReadInt(string name, int fallback, int min, int max) =>
        Environment.GetEnvironmentVariable(name) is not { } text ? fallback :
        int.TryParse(text, out var value) && value >= min && value <= max ? value :
        throw new ArgumentException($"{name} must be {min}..{max}.");
}
