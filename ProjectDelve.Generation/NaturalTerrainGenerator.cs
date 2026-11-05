using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ProjectDelve.Engine;

namespace ProjectDelve.Generation;

public sealed record NaturalTerrainSample(string TerrainTypeId, double Forest, double Rock, double Openness, double Soil);

/// <summary>WLD1's stateless, absolute-coordinate sampler. No board edges, traversal order or sampled-window statistics enter its decisions.</summary>
public sealed class NaturalTerrainSampler
{
    private readonly ulong forestSeed, rockSeed, opennessSeed, soilSeed, waterSeed;
    private readonly NaturalTerrainSettings settings;
    private readonly double scale, spacing;
    public NaturalTerrainSampler(WorldDefinition world) : this(world.Validate().Seed, world.NaturalTerrain) { }
    public NaturalTerrainSampler(ulong seed, NaturalTerrainSettings settings)
    {
        this.settings = settings.Validate();
        scale = settings.Scale switch { FeatureScale.Fine => 3, FeatureScale.Broad => 8, _ => 5 };
        spacing = scale * 8;
        forestSeed = DomainSeed(seed, 1); rockSeed = DomainSeed(seed, 2);
        opennessSeed = DomainSeed(seed, 3); soilSeed = DomainSeed(seed, 4); waterSeed = DomainSeed(seed, 5);
    }

    public NaturalTerrainSample Sample(int x, int y) => Sample(x, y, Features(x, y, x, y));

    internal NaturalTerrainSample Sample(int x, int y, IReadOnlyList<WaterFeature> features)
    {
        var openness = Field(opennessSeed, x, y, scale * 4);
        var forest = Field(forestSeed, x, y, scale * 2.5);
        var rock = Field(rockSeed, x, y, scale * 1.8);
        var soil = Field(soilSeed, x, y, scale * 1.25);
        var water = settings.WaterCoverage == 100 || settings.WaterCoverage > 0 && features.Any(f => f.Contains(x, y));
        // The conditional budgets preserve the requested global proportions approximately,
        // rather than counting or repairing the selected window. Water > rock > forest > ground.
        var land = 100 - settings.WaterCoverage;
        var rockAmount = land == 0 ? 0 : (double)settings.RockCoverage / land;
        var forestLand = land - settings.RockCoverage;
        var forestAmount = forestLand == 0 ? 0 : (double)settings.ForestCoverage / forestLand;
        var terrain = water ? "water" : Below(rock, rockAmount) ? "solid-rock" :
            Below(forest * .85 + openness * .15, forestAmount) ? "tree" :
            soil > .64 && openness < .62 ? "dirt" : "grass";
        return new(terrain, forest, rock, openness, soil);
    }

    private static bool Below(double field, double amount)
    {
        if (amount <= 0) return false;
        if (amount >= 1) return true;
        // Smooth value noise is concentrated near its mean. This fixed analytical
        // approximation expands its distribution without inspecting any window.
        var z = (field - .5) / .19;
        var probability = 1 / (1 + Math.Exp(-1.7 * z));
        return probability < amount;
    }

    // Each stable owner square owns one bounded shape, with independent placement,
    // rotation and shape hashes. Owner squares never clip their features.
    internal IReadOnlyList<WaterFeature> Features(int left, int top, int right, int bottom)
    {
        if (settings.WaterCoverage is 0 or 100) return [];
        List<WaterFeature> result = [];
        var minX = (long)Math.Floor(left / spacing) - 3;
        var maxX = (long)Math.Floor(right / spacing) + 3;
        var minY = (long)Math.Floor(top / spacing) - 3;
        var maxY = (long)Math.Floor(bottom / spacing) + 3;
        for (var gy = minY; gy <= maxY; gy++)
        for (var gx = minX; gx <= maxX; gx++)
        {
            var h = Hash(waterSeed, gx, gy);
            double R(int domain) => Unit(Mix(h + (ulong)domain));
            var form = settings.WaterForm == WaterForm.Auto ? (R(1) < .5 ? WaterForm.Lakes : WaterForm.River) : settings.WaterForm;
            var river = form == WaterForm.River || form == WaterForm.Mixed && R(1) < .5;
            var requested = settings.WaterCoverage / 100.0;
            // Reduce the number of independently owned waterways at low amounts,
            // rather than reducing physical ribbons to disconnected sub-cell dots.
            var minimum = river ? .22 : .06;
            if (R(8) >= Math.Min(1, requested / minimum)) continue;
            var p = Math.Max(minimum, requested);
            var radius = spacing * Math.Sqrt(-Math.Log(1 - p) / Math.PI);
            var angle = R(4) * Math.PI * 2;
            var cx = (gx + R(2)) * spacing;
            var cy = (gy + R(3)) * spacing;
            var aspect = .65 + R(5) * .7;
            var a = river ? spacing * (.65 + .2 * R(6)) : radius * aspect;
            var b = river ? spacing * (-Math.Log(1 - p)) / 3.1 : radius / aspect;
            // A conservative reach of three owner squares includes every possible
            // lake shoreline and river bend, including signed-axis crossings.
            var reach = a + b + spacing * .2;
            if (cx + reach < left || cx - reach > right || cy + reach < top || cy - reach > bottom) continue;
            result.Add(new(gx, gy, cx, cy, Math.Cos(angle), Math.Sin(angle), a, b, R(7) * Math.PI * 2, river));
        }
        return result;
    }

    internal sealed record WaterFeature(long OwnerX, long OwnerY, double X, double Y, double Cos, double Sin,
        double A, double B, double Phase, bool River)
    {
        public bool Contains(double x, double y)
        {
            var dx = x - X; var dy = y - Y;
            if (Math.Abs(dx) > A + B + A * .3 || Math.Abs(dy) > A + B + A * .3) return false;
            var u = dx * Cos + dy * Sin;
            var v = -dx * Sin + dy * Cos;
            if (River)
            {
                // Rounded, tapered finite waterways. Their ends and widths belong
                // to the world feature and cannot be relocated by a crop.
                var t = Math.Clamp(u / A, -1, 1);
                var bend = A * .17 * Math.Sin(t * 4.5 + Phase) + A * .045 * Math.Sin(t * 10 + Phase);
                var width = B * (.8 + .2 * Math.Sin(t * 6 + Phase));
                var end = Math.Max(0, Math.Abs(u) - A);
                return end * end + (v - bend) * (v - bend) <= width * width;
            }
            var nx = u / A; var ny = v / B;
            var theta = Math.Atan2(ny, nx);
            var shore = 1 + .12 * Math.Sin(theta * 3 + Phase) + .06 * Math.Sin(theta * 7 - Phase);
            return nx * nx + ny * ny < shore * shore;
        }
    }

    private static double Field(ulong seed, double x, double y, double wavelength) =>
        .72 * Noise(seed, x / wavelength, y / wavelength) +
        .21 * Noise(Mix(seed), x / (wavelength * .43), y / (wavelength * .43)) +
        .07 * Noise(Mix(seed + 1), x / (wavelength * .17), y / (wavelength * .17));
    private static double Noise(ulong seed, double x, double y)
    {
        var ix = (long)Math.Floor(x); var iy = (long)Math.Floor(y);
        double Smooth(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        var tx = Smooth(x - ix); var ty = Smooth(y - iy);
        var a = Unit(Hash(seed, ix, iy)); var b = Unit(Hash(seed, ix + 1, iy));
        var c = Unit(Hash(seed, ix, iy + 1)); var d = Unit(Hash(seed, ix + 1, iy + 1));
        return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
    }
    internal static ulong DomainSeed(ulong root, ulong domain) => Mix(root ^ Mix(0x574C44314E415455UL + domain));
    private static ulong Hash(ulong seed, long x, long y) => Mix(seed ^ Mix(unchecked((ulong)x)) ^ Mix(unchecked((ulong)y) + 0xD1B54A32D192ED03UL));
    private static double Unit(ulong value) => (value >> 11) * (1.0 / (1UL << 53));
    private static ulong Mix(ulong z)
    {
        unchecked { z += 0x9E3779B97F4A7C15UL; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
    }
}

public sealed record WorldGenerationDiagnostics(IReadOnlyDictionary<string, int> TerrainCounts,
    IReadOnlyDictionary<string, double> ActualCoverage);
public sealed record GeneratedWorld(string Code, ScenarioDefinition Definition, string Fingerprint,
    WorldGenerationDiagnostics Diagnostics, string VisualSeed, WorldWindow Window)
{
    public required WorldPreview Preview { get; init; }
    public required WorldGenerationMetrics Metrics { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public required WorldGenerationContext Context { get; init; }
}

/// <summary>Mutable authoring output for one crop; never retained by the pure world sampler.</summary>
public sealed partial class WorldGenerationContext
{
    public WorldDefinition World { get; }
    public WorldWindow Window { get; }
    public NaturalTerrainSample[] Samples { get; }
    private bool sampled;
    public WorldGenerationContext(WorldDefinition world, WorldWindow window)
    {
        World = world.Validate(); Window = window.Validate();
        Samples = new NaturalTerrainSample[window.Width * window.Height];
    }
    public void SampleNaturalTerrain()
    {
        var sampler = new NaturalTerrainSampler(World);
        var features = sampler.Features(Window.OffsetX, Window.OffsetY, Window.OffsetX + Window.Width - 1, Window.OffsetY + Window.Height - 1);
        for (var y = 0; y < Window.Height; y++)
        for (var x = 0; x < Window.Width; x++)
            Samples[y * Window.Width + x] = sampler.Sample(Window.OffsetX + x, Window.OffsetY + y, features);
        sampled = true;
        OriginalSamples = (NaturalTerrainSample[])Samples.Clone();
    }
    public ScenarioDefinition Materialize()
    {
        if (!sampled) throw new InvalidOperationException("Sample natural terrain before materializing a board.");
        if (ConstructionBoard is not null) return ConstructionBoard.Export();
        List<CellDefinition> cells = [];
        for (var y = 0; y < Window.Height; y++)
        for (var x = 0; x < Window.Width; x++)
        {
            var terrain = Samples[y * Window.Width + x].TerrainTypeId;
            if (terrain != "grass") cells.Add(new(new(x, y), TerrainKind.StoneFloor) { TerrainTypeId = terrain });
        }
        return new ScenarioDefinition
        {
            Board = new() { Width = Window.Width, Height = Window.Height, DefaultTerrain = TerrainKind.Grass, DefaultTerrainTypeId = "grass", Cells = cells },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        };
    }
}

public static class WorldGenerator
{
    public static GeneratedWorld Generate(string code, ContentBundle? content = null) => Generate(WorldGenerationRecipe.Parse(code), content);
    public static GeneratedWorld Generate(WorldGenerationRecipe recipe, ContentBundle? content = null)
    {
        var completed = recipe.Complete();
        var window = completed.Window;
        var execution = WorldGenerationPipeline.Execute(WorldGenerationPlan.FromRecipe(completed), content);
        var context = execution.Context;
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (var sample in context.Samples)
        {
            var terrain = sample.TerrainTypeId;
            counts[terrain] = counts.GetValueOrDefault(terrain) + 1;
        }
        var timer = Stopwatch.StartNew();
        var definition = context.Materialize();
        _ = GameEngine.CreateGame(definition, content ?? ContentBundle.Canonical());
        var json = ScenarioDefinitionJson.ToJson(definition);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new(completed.ToString(), definition, fingerprint,
            new(counts, counts.ToDictionary(p => p.Key, p => p.Value * 100.0 / (window.Width * window.Height))),
            $"WLD1-NaturalTerrain-{NaturalTerrainSampler.DomainSeed(completed.Seed!.Value, 100):X16}", window)
        {
            Preview = execution.Preview, Context = context,
            Metrics = new(execution.Measurements, timer.Elapsed.TotalMilliseconds, execution.Preview.EstimatedRetainedBytes)
            {
                ConstructionValidationMilliseconds = context.ConstructionBoard?.ValidationMilliseconds ?? 0,
                ConstructionValidations = context.ConstructionBoard?.ValidationCount ?? 0
            }
        };
    }
}
