using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldGenerationTests
{
    [Theory]
    [InlineData(120, 100)]
    [InlineData(100, 120)]
    [InlineData(120, 120)]
    public void LargeRectangularWindowsReproduceAndOverlapAcrossSignedAxes(int width, int height)
    {
        var recipe = Recipe(new(-60, -50, width, height));
        var whole = WorldGenerator.Generate(recipe);
        var reproduced = WorldGenerator.Generate(WorldGenerationRecipe.Parse(whole.Code));
        Assert.Equal(whole.Fingerprint, reproduced.Fingerprint);
        Assert.Equal(ScenarioDefinitionJson.ToJson(whole.Definition), ScenarioDefinitionJson.ToJson(
            ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(whole.Definition))));
        var adjacent = WorldGenerator.Generate(recipe with { Window = new(-19, -23, width, height) });
        var a = whole.Definition.Board.Cells.ToDictionary(c => c.Position, c => c.TerrainTypeId);
        var b = adjacent.Definition.Board.Cells.ToDictionary(c => c.Position, c => c.TerrainTypeId);
        Assert.Equal(whole.VisualSeed, adjacent.VisualSeed);
        for (var y = -23; y < height - 50; y++)
        for (var x = -19; x < width - 60; x++)
            Assert.Equal(a.GetValueOrDefault(new(x + 60, y + 50), whole.Definition.Board.DefaultTerrainTypeId),
                b.GetValueOrDefault(new(x + 19, y + 23), adjacent.Definition.Board.DefaultTerrainTypeId));
    }

    private static WorldGenerationRecipe Recipe(WorldWindow? window = null, NaturalTerrainSettings? settings = null, ulong seed = 0x123456789ABCDEF0) =>
        new(window ?? new(-17, -13, 30, 24), settings ?? new(), seed);
    private static string Terrain(ScenarioDefinition definition, int x, int y) =>
        definition.Board.Cells.FirstOrDefault(c => c.Position == new Cell(x, y))?.TerrainTypeId ?? definition.Board.DefaultTerrainTypeId!;

    [Fact]
    public void RecipesAreCanonicalStrictCultureIndependentAndCompleteOnce()
    {
        var original = Recipe(new(int.MinValue, -456, 120, 100));
        Assert.Equal(original, WorldGenerationRecipe.Parse(original.ToString()));
        var complete = (original with { Seed = null }).Complete();
        Assert.Equal(complete, complete.Complete());
        Assert.Equal(original.Prefix, complete.Prefix);
        Assert.EndsWith("-S123456789ABCDEF0", WorldGenerationRecipe.Parse(original.Prefix + "-S123456789abcdef0").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("WLD2-30x24-X0-Y0-F35-W12-R15-KBalanced-HAuto")]
    [InlineData("WLD1-030x24-X0-Y0-F35-W12-R15-KBalanced-HAuto")]
    [InlineData("WLD1-30x24-X0-Y0-F35-W12-R15-KBalanced-HAuto-S123")]
    [InlineData("WLD1-30x24-X2147483648-Y0-F35-W12-R15-KBalanced-HAuto")]
    public void InvalidRecipesAreRejected(string text) => Assert.Throws<FormatException>(() => WorldGenerationRecipe.Parse(text));

    [Fact]
    public void InvalidCoverageAndEnumsAndOverflowAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new NaturalTerrainSettings(90, 12, 0).Validate());
        Assert.Throws<ArgumentException>(() => new NaturalTerrainSettings(-1, 0, 0).Validate());
        Assert.Throws<ArgumentException>(() => new NaturalTerrainSettings(Scale: (FeatureScale)99).Validate());
        Assert.Throws<ArgumentException>(() => new WorldWindow(int.MaxValue, 0, 2, 1).Validate());
    }

    [Theory]
    [InlineData(FeatureScale.Fine, WaterForm.Lakes)]
    [InlineData(FeatureScale.Balanced, WaterForm.River)]
    [InlineData(FeatureScale.Broad, WaterForm.Mixed)]
    [InlineData(FeatureScale.Balanced, WaterForm.Auto)]
    public void NegativeAxisPartitionsAndOverlapsExactlyMatchLargerWorld(FeatureScale scale, WaterForm form)
    {
        var recipe = Recipe(new(-40, -30, 80, 60), new(30, 20, 20, scale, form));
        var whole = WorldGenerator.Generate(recipe);
        foreach (var w in new[] { new WorldWindow(-40, -30, 40, 30), new(0, -30, 40, 30), new(-40, 0, 40, 30), new(0, 0, 40, 30), new(-7, -9, 19, 17) })
        {
            var part = WorldGenerator.Generate(recipe with { Window = w });
            Assert.Equal(whole.VisualSeed, part.VisualSeed);
            for (var y = 0; y < w.Height; y++)
            for (var x = 0; x < w.Width; x++)
                Assert.Equal(Terrain(whole.Definition, x + w.OffsetX + 40, y + w.OffsetY + 30), Terrain(part.Definition, x, y));
        }
    }

    [Fact]
    public void SamplingOrderSeedAndSettingsDoNotLeakMutableState()
    {
        var sampler = new NaturalTerrainSampler(123, new());
        var points = Enumerable.Range(-20, 41).Select(n => (X: n, Y: n * 3)).ToArray();
        var expected = points.Select(p => sampler.Sample(p.X, p.Y)).ToArray();
        Assert.Equal(expected.Reverse(), points.Reverse().Select(p => sampler.Sample(p.X, p.Y)));
        Assert.NotEqual(expected, points.Select(p => new NaturalTerrainSampler(124, new()).Sample(p.X, p.Y)).ToArray());
        var changed = new NaturalTerrainSampler(123, new(0, 0, 0));
        for (var i = 0; i < points.Length; i++)
        {
            var sample = changed.Sample(points[i].X, points[i].Y);
            Assert.Equal(expected[i].Soil, sample.Soil);
            Assert.Equal(expected[i].Openness, sample.Openness);
        }
    }

    [Theory]
    [InlineData(0, 0, 0, null)]
    [InlineData(100, 0, 0, "tree")]
    [InlineData(0, 100, 0, "water")]
    [InlineData(0, 0, 100, "solid-rock")]
    public void ZeroAndDenseBudgetsHaveExplicitSemantics(int forest, int water, int rock, string? expected)
    {
        var world = WorldGenerator.Generate(Recipe(settings: new(forest, water, rock)));
        for (var y = 0; y < 24; y++)
        for (var x = 0; x < 30; x++)
            if (expected is null) Assert.Contains(Terrain(world.Definition, x, y), new[] { "grass", "dirt" });
            else Assert.Equal(expected, Terrain(world.Definition, x, y));
        Assert.Equal(720, world.Diagnostics.TerrainCounts.Values.Sum());
        Assert.Equal(100, world.Diagnostics.ActualCoverage.Values.Sum(), 8);
    }

    [Fact]
    public void ExportIsOrdinarySparseScenarioAndEditsCannotMutateWorld()
    {
        var recipe = Recipe();
        var world = WorldGenerator.Generate(recipe);
        Assert.Empty(world.Definition.Units); Assert.Empty(world.Definition.UnitTypeIds); Assert.Empty(world.Definition.Agency);
        Assert.Null(world.Definition.Objectives); Assert.Null(world.Definition.FogOfWarSides);
        Assert.Empty(world.Definition.Loot!.Composition!);
        Assert.Equal("grass", world.Definition.Board.DefaultTerrainTypeId);
        Assert.DoesNotContain(world.Definition.Board.Cells, c => c.TerrainTypeId == "grass");
        var transported = ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(world.Definition));
        Assert.Equal(ScenarioDefinitionJson.ToJson(world.Definition), ScenarioDefinitionJson.ToJson(transported));
        world.Definition.Board.Cells.Clear();
        var regenerated = WorldGenerator.Generate(recipe);
        Assert.Equal(world.Fingerprint, regenerated.Fingerprint);
        Assert.NotEmpty(regenerated.Definition.Board.Cells);
        Assert.Equal(world.VisualSeed, WorldGenerator.Generate(recipe with { NaturalTerrain = new(0, 0, 0) }).VisualSeed);
    }

    [Theory]
    [InlineData(WaterForm.Lakes)]
    [InlineData(WaterForm.River)]
    public void WaterFeaturesSurviveTinyCropsAndIncreasingAmount(WaterForm form)
    {
        var low = new NaturalTerrainSampler(723, new(0, 15, 0, WaterForm: form));
        var high = new NaturalTerrainSampler(723, new(0, 35, 0, WaterForm: form));
        var found = 0;
        for (var y = -35; y < 35; y += 2)
        for (var x = -35; x < 35; x += 2)
        {
            if (low.Sample(x, y).TerrainTypeId != "water") continue;
            found++;
            Assert.Equal("water", high.Sample(x, y).TerrainTypeId);
            // A one-cell crop cannot contain the owner geometry; feature discovery
            // must still agree with sampling the unbounded world.
            var cropped = WorldGenerator.Generate(Recipe(new(x, y, 1, 1), new(0, 15, 0, WaterForm: form), 723));
            Assert.Equal("water", Terrain(cropped.Definition, 0, 0));
            if (found >= 4) return;
        }
        Assert.True(found >= 4);
    }

    [Fact]
    public void DetachedLargeWindowAndPresetsUseSameValidatedPipeline()
    {
        Assert.Equal(5, WorldGenerationPresets.All.Count);
        foreach (var preset in WorldGenerationPresets.All) preset.Settings.Validate();
        var result = WorldGenerator.Generate(Recipe(new(-60, -50, 120, 100)));
        Assert.Equal(12000, result.Diagnostics.TerrainCounts.Values.Sum());
        Assert.NotNull(GameEngine.CreateGame(result.Definition));
    }

    [Theory]
    [InlineData(FeatureScale.Fine, 1)]
    [InlineData(FeatureScale.Fine, 5)]
    [InlineData(FeatureScale.Balanced, 1)]
    [InlineData(FeatureScale.Balanced, 5)]
    [InlineData(FeatureScale.Broad, 1)]
    [InlineData(FeatureScale.Broad, 5)]
    public void IndividualSparseWaterwaysAreFourConnectedRibbons(FeatureScale scale, int coverage)
    {
        var sampler = new NaturalTerrainSampler(723, new(0, coverage, 0, scale, WaterForm.River));
        var features = sampler.Features(-1000, -1000, 1000, 1000).Take(4).ToArray();
        Assert.Equal(4, features.Length);
        foreach (var feature in features)
        {
            var reach = feature.A + feature.B + feature.A * .3;
            HashSet<Cell> cells = [];
            for (var y = (int)Math.Floor(feature.Y - reach); y <= Math.Ceiling(feature.Y + reach); y++)
            for (var x = (int)Math.Floor(feature.X - reach); x <= Math.Ceiling(feature.X + reach); x++)
                if (feature.Contains(x, y)) cells.Add(new(x, y));
            Assert.True(cells.Count >= 10);
            var first = cells.First();
            Queue<Cell> queue = new(); queue.Enqueue(first); cells.Remove(first);
            while (queue.TryDequeue(out var cell))
                foreach (var next in new[] { new Cell(cell.X - 1, cell.Y), new(cell.X + 1, cell.Y), new(cell.X, cell.Y - 1), new(cell.X, cell.Y + 1) })
                    if (cells.Remove(next)) queue.Enqueue(next);
            Assert.Empty(cells);
        }
    }

    [Fact]
    public void TinyWindowDiscoversAnIntersectingFeatureWhoseOriginIsOutsideIt()
    {
        var sampler = new NaturalTerrainSampler(723, new(0, 25, 0, FeatureScale.Broad, WaterForm.Lakes));
        foreach (var feature in sampler.Features(-100, -100, 100, 100))
        {
            var x = (int)Math.Floor(feature.X + feature.Cos * feature.A * .7);
            var y = (int)Math.Floor(feature.Y + feature.Sin * feature.A * .7);
            if (!feature.Contains(x, y) || feature.X >= x && feature.X < x + 1 && feature.Y >= y && feature.Y < y + 1) continue;
            Assert.Contains(sampler.Features(x, y, x, y), f => f.OwnerX == feature.OwnerX && f.OwnerY == feature.OwnerY);
            Assert.Equal("water", sampler.Sample(x, y).TerrainTypeId);
            var crop = WorldGenerator.Generate(Recipe(new(x, y, 1, 1), new(0, 25, 0, FeatureScale.Broad, WaterForm.Lakes), 723));
            Assert.Equal("water", Terrain(crop.Definition, 0, 0));
            return;
        }
        Assert.Fail("The fixed test world must contain an intersecting lake with an external origin.");
    }

    [Fact]
    public void Wld1RecipePinsItsAuthoredTerrainOutput()
    {
        Assert.Equal("8A138E613F11A5B51930DC48EA77158DA69EB916CF12110B9C1481EEF885AD9A",
            WorldGenerator.Generate(Recipe(new(-17, -13, 50, 50))).Fingerprint);
    }

    [Theory]
    [InlineData(int.MinValue, int.MinValue, WaterForm.Lakes)]
    [InlineData(int.MaxValue - 39, int.MaxValue - 39, WaterForm.River)]
    [InlineData(int.MinValue, int.MaxValue - 39, WaterForm.Mixed)]
    public void ExtremeSignedCoordinatesAndDenseWaterDiscoverIdenticalFeatures(int left, int top, WaterForm form)
    {
        var settings = new NaturalTerrainSettings(0, 99, 0, FeatureScale.Broad, form);
        var context = new WorldGenerationContext(new(723, settings), new(left, top, 40, 40));
        context.SampleNaturalTerrain();
        var sampler = new NaturalTerrainSampler(723, settings);
        foreach (var origin in new[] { (X: 0, Y: 0), (X: 20, Y: 20) })
        {
            var part = new WorldGenerationContext(new(723, settings), new(left + origin.X, top + origin.Y, 20, 20));
            part.SampleNaturalTerrain();
            for (var y = 0; y < 20; y++) for (var x = 0; x < 20; x++)
            {
                var expected = context.Samples[(origin.Y + y) * 40 + origin.X + x];
                Assert.Equal(expected, part.Samples[y * 20 + x]);
                Assert.Equal(expected, sampler.Sample(left + origin.X + x, top + origin.Y + y));
            }
        }
    }

    [Fact]
    public void ContextOwnsMutableOutputsAndWorldIdentityExcludesTheWindow()
    {
        var recipe = Recipe();
        Assert.Equal(recipe.World, (recipe with { Window = new(800, -900, 10, 10) }).World);
        var context = new WorldGenerationContext(recipe.World, recipe.Window);
        Assert.Throws<InvalidOperationException>(() => context.Materialize());
        context.SampleNaturalTerrain();
        var original = new NaturalTerrainSampler(recipe.World).Sample(recipe.Window.OffsetX, recipe.Window.OffsetY);
        Assert.Equal(original, context.Samples[0]);
        context.Samples[0] = original with { TerrainTypeId = "water" };
        Assert.Equal("water", Terrain(context.Materialize(), 0, 0));
        Assert.Equal(original, new NaturalTerrainSampler(recipe.World).Sample(recipe.Window.OffsetX, recipe.Window.OffsetY));
        context.SampleNaturalTerrain();
        Assert.Equal(original, context.Samples[0]);
    }
}
