using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class GrasslandEnrichmentTests
{
    [Fact]
    public void OpenGroundGainsSpacedClustersAndKeepsBroadConnectedSpace()
    {
        var c = EnrichmentFixtures.Create(Enumerable.Repeat(new string('.', 60), 50).ToArray());
        GrasslandEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.GrasslandEnrichment), ContentBundle.Canonical());
        Assert.NotEmpty(c.Features); Assert.All(c.Features, f => Assert.True(f.ChangedCells >= 3));
        Assert.Contains(c.Features, f => f.Kind == "dirt-patch");
        Assert.Contains(c.Features, f => f.Kind is "tree-pocket" or "rock-outcrop");
        var current = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        Assert.Equal(1, current.Connectivity.ComponentCount);
        Assert.True(current.Connectivity.WalkableArea >= 2955);
        Assert.InRange(c.Samples.Count(s => s.TerrainTypeId == "grass"), 2820, 2999);
        Assert.All(c.FeatureOpportunities, o => Assert.True(current.WalkableLabels[o.AnchorIndex] >= 0));
    }

    [Fact]
    public void ExistingNeckAndEnrichmentEntrancesAreProtected()
    {
        var rows = Enumerable.Range(0, 30).Select(y => new string('.', 20) + (y == 15 ? ".." : "RR") + new string('.', 20)).ToArray();
        var c = EnrichmentFixtures.Create(rows);
        var anchor = 15 * 42 + 20;
        c.Entrances.Add(new("entry", "fixture", anchor, anchor + 1, 1));
        GrasslandEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.GrasslandEnrichment, intensity: 3), ContentBundle.Canonical());
        Assert.Equal("grass", c.Samples[anchor].TerrainTypeId); Assert.Equal("grass", c.Samples[anchor + 1].TerrainTypeId);
        Assert.Equal(1, TerrainAnalyzer.Analyze(c.Window, c.Samples).Connectivity.ComponentCount);
        Assert.All(Enumerable.Range(0, c.Samples.Length).Where(i => c.OriginalSamples[i].TerrainTypeId == "solid-rock"), i => Assert.Equal(c.OriginalSamples[i], c.Samples[i]));
    }

    [Fact]
    public void WaterCrossingCandidatesKeepBothBanksPassableAfterDecoration()
    {
        for (ulong seed = 0; seed < 16; seed++)
        {
            var rows = Enumerable.Repeat(new string('.', 25) + "WW" + new string('.', 25), 40).ToArray();
            var c = EnrichmentFixtures.Create(rows, seed);
            WaterEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.WaterEnrichment), ContentBundle.Canonical());
            c.CurrentAnalysis = TerrainAnalyzer.Analyze(c.Window, c.Samples);
            GrasslandEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.GrasslandEnrichment, intensity: 3), ContentBundle.Canonical());
            var terrain = new EnrichmentTerrain(c, ContentBundle.Canonical());
            Assert.NotEmpty(c.FeatureOpportunities.Where(o => o.OtherBankIndex.HasValue));
            foreach (var crossing in c.FeatureOpportunities.Where(o => o.OtherBankIndex.HasValue))
            { Assert.True(terrain.Passable(crossing.AnchorIndex)); Assert.True(terrain.Passable(crossing.OtherBankIndex!.Value)); }
        }
    }

    [Fact]
    public void FullRecipeHasSevenInspectableStagesAndDeterministicFinalTopology()
    {
        var recipe = new WorldGenerationRecipe(new(-60, -50, 120, 120), new(30, 20, 25, FeatureScale.Broad, WaterForm.Mixed), 12345, new());
        var a = WorldGenerator.Generate(recipe); var b = WorldGenerator.Generate(recipe);
        Assert.Equal(7, a.Preview.Frames.Count); Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(a.Preview), JsonSerializer.Serialize(b.Preview));
        Assert.Equal(14400, a.Context.CurrentAnalysis!.FormationLabels.Length);
        Assert.Equal(a.Preview.Frames[^1].WalkableComponents, a.Context.CurrentAnalysis.WalkableLabels);
        Assert.Empty(a.Preview.Frames[^1].ChangedCells);
        Assert.True(a.Preview.EstimatedRetainedBytes < 4_000_000);
        Assert.All(a.Preview.Frames, f => Assert.Equal(14400, f.Terrain.Length));
        Assert.All(a.Context.Features, f => Assert.Contains(a.Context.OriginalAnalysis!.Formations, source => source.Id == f.FormationId));
    }

    [Fact]
    public void OptionalFamiliesAndRepeatedStepsHaveDistinctStableIdentity()
    {
        var recipe = new WorldGenerationRecipe(new(Width: 50, Height: 50), new(0, 0, 0), 42, new(Forest: false, Rock: false, Water: false));
        var plan = WorldGenerationPlan.FromRecipe(recipe); Assert.Equal(4, plan.Steps.Count);
        var first = WorldGenerationPipeline.Execute(plan);
        var steps = plan.Steps.Take(3).Concat([new WorldGenerationStep("grassland-second", WorldStepKind.GrasslandEnrichment,
            new EnrichmentStepParameters(), ["grassland"]), plan.Steps[^1] with { DependsOn = ["grassland-second"] }]).ToArray();
        var repeated = WorldGenerationPipeline.Execute(plan with { Steps = steps });
        Assert.Equal(first.Preview.Frames.Take(3).Select(f => f.Terrain), repeated.Preview.Frames.Take(3).Select(f => f.Terrain));
        Assert.Equal(repeated.Context.Features.Count, repeated.Context.Features.Select(f => f.Id).Distinct().Count());
        var natural = WorldGenerator.Generate(recipe with { Enrichment = new(Enabled: false) });
        Assert.Equal(3, natural.Preview.Frames.Count); Assert.Empty(natural.Context.Features);
    }
}
