using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ProjectDelve.Generation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectDelve.Engine.Tests;

public sealed class ExpeditionSpatialCorpusTests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [17, 42, 889];
    private static readonly ContentBundle Content = ContentBundle.Canonical();

    [Fact]
    public void CompatibleCatalogPresentationAndNewLootDoNotAffectBaseNature()
    {
        var profile = ExpeditionWorldProfile.Create(new());
        var sampler = new ExpeditionNatureSampler(889, profile);
        var window = new WorldWindow(-30, 30, 12, 12);
        var world = new WorldDefinition(889, profile.Nature);
        var original = new WorldGenerationContext(world, window);
        original.SampleExpeditionNature(sampler, Content);
        var dirt = Content.Terrain.Single(t => t.Id == "dirt");
        var current = Content with
        {
            Terrain = Content.Terrain.Replace(dirt, dirt with { Name = "Current Trail Soil", Presentation = new("current-soil") }),
            LootCards = Content.LootCards.Add(new("current-second-wind", "Current Second Wind") { ItemTypeId = "second-wind" })
        };
        var regenerated = new WorldGenerationContext(world, window);
        regenerated.SampleExpeditionNature(new ExpeditionNatureSampler(889, profile), current);
        Assert.Equal(original.OriginalSamples, regenerated.OriginalSamples);
    }

    [Fact]
    public void OverlappingCropsAndReversedCropOrderProduceIdenticalWorldCells()
    {
        foreach (var seed in Seeds)
        {
            var profile = ExpeditionWorldProfile.Create(new());
            var sampler = new ExpeditionNatureSampler(seed, profile);
            WorldWindow[] windows = [new(-90, -90, 120, 120), new(-60, -60, 60, 60), new(-30, -30, 90, 90)];
            WorldGenerationContext Crop(WorldWindow window)
            {
                var context = new WorldGenerationContext(new(seed, profile.Nature), window);
                context.SampleExpeditionNature(sampler, Content);
                return context;
            }
            var crops = windows.Select(Crop).ToArray();
            foreach (var window in windows.Reverse())
            {
                var crop = Crop(window);
                Assert.Equal(crops.Single(c => c.Window == window).OriginalSamples, crop.OriginalSamples);
                foreach (var other in crops)
                for (var y = Math.Max(window.OffsetY, other.Window.OffsetY); y < Math.Min(window.OffsetY + window.Height, other.Window.OffsetY + other.Window.Height); y++)
                for (var x = Math.Max(window.OffsetX, other.Window.OffsetX); x < Math.Min(window.OffsetX + window.Width, other.Window.OffsetX + other.Window.Width); x++)
                    Assert.Equal(crop.OriginalSamples[(y - window.OffsetY) * window.Width + x - window.OffsetX],
                        other.OriginalSamples[(y - other.Window.OffsetY) * other.Window.Width + x - other.Window.OffsetX]);
            }
        }
    }

    [Theory]
    [InlineData(0, 0, 0, "grass")]
    [InlineData(100, 0, 0, "tree")]
    [InlineData(0, 100, 0, "water")]
    [InlineData(0, 0, 100, "solid-rock")]
    public void SaturatedAndDisabledNatureRemainExplicitAtExtremeCoordinates(int forest, int water, int rock, string expected)
    {
        var sampler = new ExpeditionNatureSampler(42, ExpeditionWorldProfile.Create(new(forest, water, rock)));
        foreach (var x in new[] { int.MinValue, -61, -60, -1, 0, 59, 60, int.MaxValue })
        foreach (var y in new[] { int.MinValue, -60, 0, 60, int.MaxValue })
        {
            var coverage = sampler.CoverageAt(x, y);
            Assert.Equal(forest, coverage.Forest); Assert.Equal(rock, coverage.Rock); Assert.Equal(water, coverage.Water);
            var sample = sampler.Sample(x, y);
            // A zero-category world still has ordinary grass/dirt variation.
            if (forest + water + rock == 0) Assert.Contains(sample.TerrainTypeId, new[] { "grass", "dirt" });
            else Assert.Equal(expected, sample.TerrainTypeId);
        }
    }

    [Fact]
    public void PinnedFieldsHaveReproducibleSignedCoordinateSamplesAndIndependentOrder()
    {
        var profile = ExpeditionWorldProfile.Create(new());
        var sampler = new ExpeditionNatureSampler(889, profile);
        Cell[] points = [new(0, 0), new(-1, -1), new(-60, 60), new(59, -61),
            new(12345, -67890), new(int.MinValue, int.MaxValue), new(int.MaxValue, int.MinValue)];
        var values = points.ToDictionary(p => p, p => Snapshot(sampler, p));
        foreach (var p in points.Reverse())
            Assert.Equal(values[p], Snapshot(new ExpeditionNatureSampler(889, profile), p));
        foreach (var p in points)
        {
            var sample = sampler.Sample(p.X, p.Y);
            Assert.All(new[] { sample.Forest, sample.Rock, sample.Openness, sample.Soil }, v => Assert.InRange(v, 0, 1));
            Assert.All(Enum.GetValues<ExpeditionField>(), field => Assert.InRange(sampler.FieldAt(field, p.X, p.Y), 0, 1));
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", points.Select(p => values[p])))));
        output.WriteLine($"nature/field golden seed=889: {fingerprint}");
        Assert.Equal("0B3094575B36539667C3A2DA0067B300742424B393EB12A22938C75FFF4A1FE7", fingerprint);
    }

    [Fact]
    public void NaturalEdgesAndFieldGradientsDoNotAcquireSixtyCellRegionBoundaries()
    {
        foreach (var seed in Seeds)
        {
            var sampler = new ExpeditionNatureSampler(seed, ExpeditionWorldProfile.Create(new()));
            var timer = Stopwatch.StartNew();
            // Six by six regions, including both signed axes; compare all direct
            // cardinal neighbors rather than equating different world cells.
            const int size = 360, origin = -180;
            var terrain = new string[size * size];
            var fields = Enum.GetValues<ExpeditionField>();
            var sampled = fields.ToDictionary(f => f, _ => new double[size * size]);
            var forestCoverage = new double[size * size];
            var rockCoverage = new double[size * size];
            var waterCoverage = new double[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var i = y * size + x;
                terrain[i] = sampler.Sample(origin + x, origin + y).TerrainTypeId;
                foreach (var f in fields) sampled[f][i] = sampler.FieldAt(f, origin + x, origin + y);
                var coverage = sampler.CoverageAt(origin + x, origin + y);
                forestCoverage[i] = coverage.Forest;
                rockCoverage[i] = coverage.Rock;
                waterCoverage[i] = coverage.Water;
            }
            var seam = new List<(int A, int B)>(); var interior = new List<(int A, int B)>();
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var i = y * size + x;
                if (x > 0) ((origin + x) % 60 == 0 ? seam : interior).Add((i - 1, i));
                if (y > 0) ((origin + y) % 60 == 0 ? seam : interior).Add((i - size, i));
            }
            double Changes(List<(int A, int B)> pairs) => pairs.Average(p => terrain[p.A] == terrain[p.B] ? 0.0 : 1);
            var seamChanges = Changes(seam); var innerChanges = Changes(interior);
            output.WriteLine($"seed={seed}: raw terrain seam/intra transitions={seamChanges:F5}/{innerChanges:F5}, pairs={seam.Count}/{interior.Count}, sample={timer.Elapsed.TotalSeconds:F2}s");
            Assert.InRange(Math.Abs(seamChanges - innerChanges), 0, .10);
            foreach (var f in fields)
            {
                var v = sampled[f];
                var seamGradient = seam.Average(p => Math.Abs(v[p.A] - v[p.B]));
                var innerGradient = interior.Average(p => Math.Abs(v[p.A] - v[p.B]));
                var shortDelta = MeanDelta(v, size, 4); var longDelta = MeanDelta(v, size, 120);
                output.WriteLine($"  {f}: seam/intra |gradient|={seamGradient:F6}/{innerGradient:F6}; lag4/120={shortDelta:F5}/{longDelta:F5}; range={v.Min():F4}..{v.Max():F4}");
                Assert.InRange(Math.Abs(seamGradient - innerGradient), 0, .02);
                Assert.True(longDelta > shortDelta * 2, $"{seed}/{f}: nearby cells should correlate more strongly than distant cells.");
                Assert.True(v.Max() - v.Min() > .10, $"{seed}/{f}: field must have meaningful spatial variation.");
            }
            foreach (var (name, coverage) in new[] { ("Forest", forestCoverage), ("Rock", rockCoverage), ("Water", waterCoverage) })
                output.WriteLine($"  {name} coverage points, mean lag 4/120: {MeanDelta(coverage, size, 4):F2}/{MeanDelta(coverage, size, 120):F2}");
            foreach (var (a, b) in new[] { (ExpeditionField.Civilization, ExpeditionField.Difficulty),
                (ExpeditionField.Civilization, ExpeditionField.Loot), (ExpeditionField.Difficulty, ExpeditionField.Loot),
                (ExpeditionField.Forest, ExpeditionField.Civilization) })
            {
                var correlation = Pearson(sampled[a], sampled[b]);
                output.WriteLine($"  {a}/{b} Pearson r={correlation:F3}");
                Assert.InRange(correlation, -.95, .95); // Nearby samples are spatially correlated, but different channels are not aliases.
            }
        }
    }

    [Fact]
    public void ResolvedRegionalControlsVaryIndependentlyAndRespectExplicitDisabledOptions()
    {
        const ulong seed = 42;
        var nature = new NaturalTerrainSettings();
        var baseline = new WorldGenerationRecipe(new(0, 0, 60, 60), nature, seed, new(), new(), new(), new());
        var sampler = new ExpeditionNatureSampler(seed, ExpeditionWorldProfile.Create(nature));
        var regions = (from y in Enumerable.Range(-8, 17) from x in Enumerable.Range(-8, 17)
                       select sampler.ResolveRegion(baseline with { Window = new(x * 60, y * 60, 60, 60) })).ToArray();
        var combinations = regions.Select(r => (r.Civilization!.Settlements, r.Population!.Density, r.Population.Difficulty, r.LootAbundance)).Distinct().ToArray();
        Assert.Contains(combinations, c => c.Settlements >= ContentDensity.Medium && c.Difficulty == 2 && c.LootAbundance == LootAbundance.Plentiful);
        Assert.Contains(combinations, c => c.Density == PopulationDensity.High && c.LootAbundance == LootAbundance.Sparse);
        Assert.True(regions.Select(r => r.Enrichment!.Intensity).Distinct().Count() > 1);
        Assert.True(regions.Select(r => r.Routes!.Intensity).Distinct().Count() > 1);
        Assert.All(regions, r => Assert.Equal(baseline.LootPreset, r.LootPreset));
        output.WriteLine($"Seed {seed}, {regions.Length} region centers: {combinations.Length} distinct settlement/population/difficulty/loot combinations.");

        var disabled = baseline with
        {
            Enrichment = new(Enabled: false), Civilization = new(Campsites: ContentDensity.None),
            Routes = new(Enabled: false), Population = new(PopulationDensity.Off), LootPreset = "Empty"
        };
        var resolved = sampler.ResolveRegion(disabled);
        Assert.False(resolved.Enrichment!.Enabled);
        Assert.Equal(ContentDensity.None, resolved.Civilization!.Campsites);
        Assert.False(resolved.Routes!.Enabled);
        Assert.Equal(PopulationDensity.Off, resolved.Population!.Density);
        Assert.Equal("Empty", resolved.LootPreset);
        var constant = new ExpeditionNatureSampler(seed, ExpeditionWorldProfile.Create(nature,
            new() { CoverageVariation = 0, RegionalVariation = 0 }));
        Assert.Equal(nature.ForestCoverage, constant.CoverageAt(-480, 421).Forest);
        Assert.Equal(baseline, constant.ResolveRegion(baseline));
    }

    [Fact]
    public void FullNeighboringRegionsRetainSampleOverlapOccupiedAccessAndConstructionQuality()
    {
        var sites = 0; var routes = 0; var units = 0; var loot = 0; var detours = 0; var deadEnds = 0;
        foreach (var seed in new ulong[] { 17, 42 })
        {
            var nature = seed == 17 ? new NaturalTerrainSettings(10, 4, 5, FeatureScale.Broad, WaterForm.Lakes)
                : new NaturalTerrainSettings(15, 8, 50, FeatureScale.Broad, WaterForm.Mixed);
            // Keep the geography and occupation corpus on its existing construction policy.
            var profile = ExpeditionWorldProfile.CreateCivilizationV1(nature);
            var sampler = new ExpeditionNatureSampler(seed, profile);
            var generated = new List<WorldGenerationContext>();
            WorldGenerationRecipe? firstRecipe = null;
            foreach (var regionX in new[] { -1, 0 })
            {
                var recipe = new WorldGenerationRecipe(new(regionX * 60, -60, 60, 60), nature, seed,
                    new(), new(Campsites: ContentDensity.High, Settlements: ContentDensity.High),
                    new(ThroughRoutes: ContentDensity.Medium, Alternatives: ContentDensity.Medium, WaterCrossings: true),
                    new(PopulationDensity.High, 4));
                firstRecipe ??= recipe;
                var watch = Stopwatch.StartNew();
                var context = ExpeditionRegionGeometry.Execute(recipe, new(0, 30), new(59, 30), new(57, 29, 2, 3),
                    (ulong)(123 + regionX), Content, profile: profile).Context;
                generated.Add(context);
                var definition = context.Materialize();
                var state = GameEngine.CreateGame(definition, Content);
                var validation = PlayReadyScenarioSetup.Validate(definition, state);
                Assert.True(validation.Valid, string.Join("; ", validation.Diagnostics.Select(d => d.Message)));
                // Every original cell, including the crop perimeter, is sampled
                // from the same immutable world field before local enrichment.
                for (var y = 0; y < 60; y++)
                for (var x = 0; x < 60; x++)
                    Assert.Equal(sampler.Sample(regionX * 60 + x, -60 + y), context.OriginalSamples[y * 60 + x]);
                var occupied = definition.Units.SelectMany(u => FootprintGeometry.OccupiedCells(
                    Content.Units.Single(t => t.Id == u.UnitTypeId).Footprint, u.Anchor)).ToHashSet();
                var geometry = AccessibilityAnalysis.Capture(state.Physical.Board with
                { Edges = state.Physical.Board.Edges.Select(e => e.Kind == EdgeKind.ClosedDoor ? e with { Kind = EdgeKind.OpenDoor } : e).ToList() });
                var distances = Distances(geometry, new(0, 30), occupied);
                foreach (var c in definition.Setup!.RequiredLocations.Where(l => l.Id.StartsWith("expedition/", StringComparison.Ordinal))
                             .SelectMany(l => l.Groups).SelectMany(g => g.Positions))
                    Assert.True(distances.ContainsKey(c), $"seed={seed} region=({regionX},-1): occupied route fails at {c}");
                Assert.DoesNotContain(definition.Setup.Deployment.Cells, occupied.Contains);
                foreach (var route in context.Routes)
                {
                    var path = route.Geometry.LocalIndices(60, recipe.Window).Distinct().Select(i => new Cell(i % 60, i / 60)).ToArray();
                    Assert.NotEmpty(path);
                    Assert.All(path, c => Assert.True(geometry.CanStop(ConstructionCapabilities.OrdinaryLand, c)));
                    for (var i = 1; i < path.Length; i++) Assert.True(geometry.CanStep(ConstructionCapabilities.OrdinaryLand, path[i - 1], path[i]));
                }
                var distance = distances[new(59, 30)];
                var ends = distances.Keys.Count(c => Neighbors(c).Count(n => distances.ContainsKey(n) && geometry.CanStep(ConstructionCapabilities.OrdinaryLand, c, n)) == 1);
                detours += distance > 59 ? 1 : 0; deadEnds += ends;
                sites += context.Sites.Count; routes += context.Routes.Count; units += definition.Units.Count; loot += context.SiteLootMarkers.Count;
                Assert.All(context.Sites, s => Assert.NotNull(s.PopulationIntent));
                output.WriteLine($"seed={seed} region=({regionX},-1) localSeed={123 + regionX}: {watch.Elapsed.TotalSeconds:F2}s; sites={context.Sites.Count}, buildings={context.Sites.Sum(s => s.Buildings.Count)}, dungeons={context.Sites.Count(s => s.MountainDungeon is not null)}, routes={context.Routes.Count}, units={definition.Units.Count}, SiteLoot={context.SiteLootMarkers.Count}, occupied distance={distance}, deadEnds={ends}");
            }
            var a = generated[0]; var b = generated[1];
            var repeated = ExpeditionRegionGeometry.Execute(firstRecipe!, new(0, 30), new(59, 30), new(57, 29, 2, 3),
                122, Content, profile: profile).Context;
            Assert.Equal(ScenarioDefinitionJson.ToJson(a.Materialize()), ScenarioDefinitionJson.ToJson(repeated.Materialize()));
            var rawDifferences = Enumerable.Range(0, 60).Count(y => a.OriginalSamples[y * 60 + 59].TerrainTypeId != b.OriginalSamples[y * 60].TerrainTypeId);
            var finalDifferences = Enumerable.Range(0, 60).Count(y => a.Samples[y * 60 + 59].TerrainTypeId != b.Samples[y * 60].TerrainTypeId);
            output.WriteLine($"seed={seed}: direct neighboring seam raw/final differences={rawDifferences}/60,{finalDifferences}/60 (local construction is not a seamlessness promise)");
            WriteVisual(seed, generated);
        }
        Assert.True(sites > 0); Assert.True(routes > 0); Assert.True(units > 0); Assert.True(loot > 0);
        Assert.True(detours > 0); Assert.True(deadEnds > 0);
    }

    private static string Snapshot(ExpeditionNatureSampler sampler, Cell p)
    {
        var sample = sampler.Sample(p.X, p.Y); var coverage = sampler.CoverageAt(p.X, p.Y);
        string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        return $"{p.X},{p.Y}:{sample.TerrainTypeId}:" + string.Join(',', new[] { sample.Forest, sample.Rock, sample.Openness, sample.Soil,
            coverage.Forest, coverage.Rock, coverage.Water }.Select(F)) + ":" +
            string.Join(',', Enum.GetValues<ExpeditionField>().Select(f => F(sampler.FieldAt(f, p.X, p.Y))));
    }

    private static double MeanDelta(double[] values, int size, int lag)
    {
        double total = 0; var count = 0;
        for (var y = 0; y < size; y += 3)
        for (var x = 0; x < size - lag; x += 3)
        { total += Math.Abs(values[y * size + x] - values[y * size + x + lag]); count++; }
        return total / count;
    }

    private static double Pearson(double[] left, double[] right)
    {
        var leftMean = left.Average(); var rightMean = right.Average();
        double covariance = 0, leftVariance = 0, rightVariance = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var a = left[i] - leftMean; var b = right[i] - rightMean;
            covariance += a * b; leftVariance += a * a; rightVariance += b * b;
        }
        return covariance / Math.Sqrt(leftVariance * rightVariance);
    }

    private static Dictionary<Cell, int> Distances(AccessibilityAnalysis geometry, Cell start, HashSet<Cell> occupied)
    {
        Dictionary<Cell, int> result = new() { [start] = 0 }; Queue<Cell> pending = new([start]);
        while (pending.TryDequeue(out var current))
            foreach (var next in Neighbors(current))
                if (!occupied.Contains(next) && !result.ContainsKey(next) && geometry.CanStep(ConstructionCapabilities.OrdinaryLand, current, next))
                { result[next] = result[current] + 1; pending.Enqueue(next); }
        return result;
    }
    private static IEnumerable<Cell> Neighbors(Cell c) => [new(c.X - 1, c.Y), new(c.X + 1, c.Y), new(c.X, c.Y - 1), new(c.X, c.Y + 1)];

    private void WriteVisual(ulong seed, IReadOnlyList<WorldGenerationContext> regions)
    {
        // Explicit opt-in artifacts keep normal test runs free of filesystem side effects.
        var directory = Environment.GetEnvironmentVariable("PROJECTDELVE_SPATIAL_CORPUS_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 600 660'><rect width='600' height='660' fill='white'/><style>text{font:14px sans-serif}</style>");
        svg.Append($"<text x='10' y='20'>Seed {seed}, regions (-1,-1)/(0,-1): raw nature / final construction</text>");
        for (var panel = 0; panel < 2; panel++)
        for (var region = 0; region < regions.Count; region++)
        for (var y = 0; y < 60; y++)
        for (var x = 0; x < 60; x++)
        {
            var terrain = (panel == 0 ? regions[region].OriginalSamples : regions[region].Samples)[y * 60 + x].TerrainTypeId;
            var color = terrain switch { "grass" => "#a6c973", "dirt" => "#b5986d", "tree" => "#295a37", "solid-rock" => "#626575", "water" => "#417fb1", "wet-grass" => "#74aaa1", "rocky-water" => "#536e8d", "stone-floor" => "#c5bdaa", _ => "#ddaf88" };
            svg.Append($"<rect x='{(region * 60 + x) * 5}' y='{30 + panel * 315 + y * 5}' width='5' height='5' fill='{color}'/>");
        }
        svg.Append("<path d='M300 30V330 M300 345V645' stroke='#e33756' stroke-width='1' stroke-dasharray='3 3'/></svg>");
        var path = Path.Combine(directory, $"expedition-spatial-{seed}.svg"); File.WriteAllText(path, svg.ToString());
        output.WriteLine($"visual: {path}");
    }
}
