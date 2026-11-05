using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldGenerationPipelineTests
{
    private static WorldGenerationRecipe Recipe() => new(new(-12, -8, 40, 30), new(), 12345, new());

    [Fact]
    public void EnrichmentControlsRoundtripEffectiveSettings()
    {
        foreach (var enabled in new[] { false, true })
        foreach (var intensity in new[] { 1, 2, 3 })
        {
            var recipe = Recipe() with { Enrichment = new(enabled, intensity, false, true, false, true) };
            Assert.Equal(recipe, WorldGenerationRecipe.Parse(recipe.ToString()));
        }
        Assert.Throws<ArgumentException>(() => (Recipe() with { Enrichment = new(Intensity: 4) }).Validate());
        Assert.Throws<FormatException>(() => WorldGenerationRecipe.Parse(Recipe().ToString().Replace("B1111", "B1112")));
    }

    [Fact]
    public void AnalysisStagesAreReadOnlyAndIntermediateResultsReproduce()
    {
        var a = WorldGenerator.Generate(Recipe()); var b = WorldGenerator.Generate(Recipe());
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(a.Diagnostics), JsonSerializer.Serialize(b.Diagnostics));
        Assert.Equal(JsonSerializer.Serialize(a.Preview), JsonSerializer.Serialize(b.Preview));
        Assert.All(a.Preview.Frames.Where(f => f.Stage.Contains("Analysis")), f => Assert.Empty(f.ChangedCells));
        Assert.Equal(a.Context.OriginalAnalysis!.Formations.Select(f => f.Id), a.Preview.Formations.Select(f => f.Id));
        Assert.Equal(a.Window.Width * a.Window.Height, a.Context.OriginalSamples.Length);
        a.Context.Samples[0] = a.Context.Samples[0] with { TerrainTypeId = "water" };
        Assert.Equal(b.Fingerprint, WorldGenerator.Generate(Recipe()).Fingerprint);
    }

    [Fact]
    public void PlanRejectsMissingForwardDuplicateAndInvalidDependencies()
    {
        var plan = WorldGenerationPlan.FromRecipe(Recipe());
        Assert.Throws<ArgumentException>(() => (plan with { Steps = plan.Steps.Reverse().ToArray() }).Validate());
        Assert.Throws<ArgumentException>(() => (plan with { Steps = [plan.Steps[0], plan.Steps[0]] }).Validate());
        Assert.Throws<ArgumentException>(() => (plan with { Steps = [plan.Steps[0], plan.Steps[1] with { DependsOn = ["missing"] }] }).Validate());
        Assert.Throws<ArgumentException>(() => (plan with { Steps = [plan.Steps[0], plan.Steps[1] with { Parameters = new EnrichmentStepParameters() }] }).Validate());
        Assert.Throws<ArgumentException>(() => (plan with { Steps = [plan.Steps[0], plan.Steps[1] with { Id = "bad:id" }] }).Validate());
    }

    [Fact]
    public void SeedsDependOnIdentityNotPosition()
    {
        Assert.Equal(WorldStepSeed.Derive(1, "forest", WorldStepKind.ForestEnrichment), WorldStepSeed.Derive(1, "forest", WorldStepKind.ForestEnrichment));
        Assert.NotEqual(WorldStepSeed.Derive(1, "forest", WorldStepKind.ForestEnrichment), WorldStepSeed.Derive(1, "forest-again", WorldStepKind.ForestEnrichment));
        Assert.NotEqual(WorldStepSeed.Derive(1, "forest", WorldStepKind.ForestEnrichment), WorldStepSeed.Derive(1, "forest", WorldStepKind.RockEnrichment));
    }

    [Fact]
    public void InsertingReadOnlyStepDoesNotReseedOrChangeForestOutput()
    {
        var recipe = Recipe() with { NaturalTerrain = new(70, 8, 8), Enrichment = new(Rock: false, Water: false, Grassland: false) };
        var plan = WorldGenerationPlan.FromRecipe(recipe);
        var baseline = WorldGenerationPipeline.Execute(plan);
        var inserted = new WorldGenerationStep("extra-inspection", WorldStepKind.PostEnrichmentAnalysis, new AnalysisStepParameters(), ["formations"]);
        var steps = plan.Steps.Take(2).Concat([inserted]).Concat(plan.Steps.Skip(2).Select(s => s.Kind == WorldStepKind.ForestEnrichment ? s with { DependsOn = [inserted.Id] } : s)).ToArray();
        var changed = WorldGenerationPipeline.Execute(plan with { Steps = steps });
        Assert.Equal(baseline.Preview.Frames.Single(f => f.StepId == "forest").Terrain, changed.Preview.Frames.Single(f => f.StepId == "forest").Terrain);
        Assert.Equal(baseline.Preview.Frames.Single(f => f.StepId == "forest").Diagnostics, changed.Preview.Frames.Single(f => f.StepId == "forest").Diagnostics);
    }

    [Fact]
    public void RepeatedReadOnlyStagesAndOptionalPostAnalysisAreSupported()
    {
        var plan = WorldGenerationPlan.FromRecipe(Recipe());
        var a = WorldGenerationPipeline.Execute(plan with { Steps = plan.Steps.Take(2).ToArray() });
        var b = WorldGenerationPipeline.Execute(plan with { Steps = [.. plan.Steps.Take(2),
            new("inspect-again", WorldStepKind.PostEnrichmentAnalysis, new AnalysisStepParameters(), ["formations"])] });
        Assert.Equal(a.Preview.Frames.Select(f => f.Terrain), b.Preview.Frames.Take(2).Select(f => f.Terrain));
        Assert.Empty(b.Preview.Frames[^1].ChangedCells);
    }
}
