using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ForestEnrichmentTests
{
    [Fact]
    public void DenseForestGainsClearingAndTraversableTrailWithoutChangingNaturalSource()
    {
        var c = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('T')); var source = c.OriginalSamples.ToArray();
        ForestEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.ForestEnrichment), ContentBundle.Canonical());
        Assert.NotEmpty(c.Features); Assert.NotEmpty(c.Entrances);
        var analysis = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        Assert.True(analysis.Connectivity.WalkableArea > c.OriginalAnalysis!.Connectivity.WalkableArea);
        Assert.Equal(source, c.OriginalSamples);
        Assert.InRange(c.Samples.Count(s => s.TerrainTypeId == "tree"), 400 * 87 / 100, 399);
        Assert.Contains(c.Samples, s => s.TerrainTypeId == "dirt");
        Assert.Contains(c.FeatureOpportunities, o => o.Kind == "clearing" && o.UsableArea >= 5);
        foreach (var entry in c.Entrances)
        {
            Assert.True(analysis.WalkableLabels[entry.InsideIndex] >= 0);
            Assert.Equal(analysis.WalkableLabels[entry.OutsideIndex], analysis.WalkableLabels[entry.InsideIndex]);
            Assert.Equal(1, Math.Abs(entry.InsideIndex % 24 - entry.OutsideIndex % 24) + Math.Abs(entry.InsideIndex / 24 - entry.OutsideIndex / 24));
        }
        foreach (var o in c.FeatureOpportunities) Assert.Equal(analysis.WalkableLabels[0], analysis.WalkableLabels[o.AnchorIndex]);
    }

    [Fact]
    public void RepeatedStepsSharePreservationBudgetAndNeverOverwriteWaterOrRock()
    {
        var rows = EnrichmentFixtures.Mass('T'); rows[4] = rows[4][..5] + "WR" + rows[4][7..];
        var c = EnrichmentFixtures.Create(rows);
        for (var repeat = 0; repeat < 4; repeat++) ForestEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.ForestEnrichment, "forest-" + repeat, 3), ContentBundle.Canonical());
        var forest = c.OriginalAnalysis!.Formations.Single(f => f.Kind == NaturalFormationKind.Forest);
        var edits = Enumerable.Range(0, c.Samples.Length).Count(i => c.Samples[i] != c.OriginalSamples[i]);
        Assert.InRange(edits, 1, forest.CoreArea * 18 / 100);
        Assert.Equal("water", c.Samples[4 * 24 + 5].TerrainTypeId); Assert.Equal("solid-rock", c.Samples[4 * 24 + 6].TerrainTypeId);
        Assert.Equal(c.Features.Count, c.Features.Select(f => f.Id).Distinct().Count());
    }

    [Fact]
    public void LongAxisAlignedTrailsCanBendWithoutChangingTerrainDuringPlanning()
    {
        var c = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('T'));
        var terrain = new EnrichmentTerrain(c, ContentBundle.Canonical());
        var path = terrain.Meander(10 * 24 + 3, 10 * 24 + 20, _ => true, _ => 10, new Pcg32(42, 54));
        Assert.NotNull(path); Assert.Contains(path!, i => i / 24 != 10);
        Assert.Equal(c.OriginalSamples, c.Samples);
    }

    [Fact]
    public void ForestWithoutExteriorGroundIsNotGivenAFabricatedEntrance()
    {
        var c = EnrichmentFixtures.Create(Enumerable.Repeat(new string('T', 30), 30).ToArray());
        ForestEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.ForestEnrichment), ContentBundle.Canonical());
        Assert.Empty(c.Features); Assert.Empty(c.Entrances); Assert.Equal(c.OriginalSamples, c.Samples);
    }

    [Fact]
    public void ForestStageIsDeterministicAndIndependentOfLaterStages()
    {
        var recipe = new WorldGenerationRecipe(new(-17, -13, 80, 70), new(70, 8, 8), 12345, new(Rock: false, Water: false, Grassland: false));
        var a = WorldGenerator.Generate(recipe); var b = WorldGenerator.Generate(recipe);
        Assert.Equal(JsonSerializer.Serialize(a.Preview), JsonSerializer.Serialize(b.Preview));
        var plan = WorldGenerationPlan.FromRecipe(recipe);
        var shortened = WorldGenerationPipeline.Execute(plan with { Steps = plan.Steps.Take(plan.Steps.Count - 1).ToArray() });
        Assert.Equal(a.Preview.Frames.Take(shortened.Preview.Frames.Count).Select(f => f.Terrain), shortened.Preview.Frames.Select(f => f.Terrain));
        Assert.Contains(a.Preview.Frames, f => f.Stage == "ForestEnrichment" && f.ChangedCells.Length > 0);
        Assert.All(a.Context.Features, f => Assert.Contains(a.Context.OriginalAnalysis!.Formations, source => source.Id == f.FormationId));
    }
}
