using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WaterEnrichmentTests
{
    [Fact]
    public void IslandsPreserveOriginalShoreAndRemainDisconnectedFromMainland()
    {
        var c = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('W', 30));
        WaterEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.WaterEnrichment), ContentBundle.Canonical());
        Assert.NotEmpty(c.Features); Assert.Empty(c.Entrances);
        var current = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        foreach (var island in c.FeatureOpportunities.Where(o => o.Kind == "island"))
        {
            Assert.True(island.RequiresConnection);
            Assert.NotEqual(current.WalkableLabels[0], current.WalkableLabels[island.AnchorIndex]);
            Assert.InRange(island.UsableArea, 6, 30);
        }
        var edits = Enumerable.Range(0, c.Samples.Length).Where(i => c.Samples[i] != c.OriginalSamples[i]).ToArray();
        Assert.InRange(edits.Length, 6, 45);
        Assert.All(edits, i => { Assert.Equal("water", c.OriginalSamples[i].TerrainTypeId); Assert.True(c.OriginalAnalysis!.Depth[i] >= 2); });
        Assert.Equal(900, c.OriginalSamples.Count(s => s.TerrainTypeId == "water"));
    }

    [Fact]
    public void NarrowWaterwayOffersBankPairsWithoutConstructingABridge()
    {
        var rows = Enumerable.Repeat(".....WW.....", 20).ToArray(); var c = EnrichmentFixtures.Create(rows);
        WaterEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.WaterEnrichment), ContentBundle.Canonical());
        Assert.Equal(c.OriginalSamples, c.Samples); Assert.Empty(c.Entrances);
        var candidates = c.FeatureOpportunities.Where(o => o.Kind == "bridge-site").ToArray();
        Assert.NotEmpty(candidates);
        Assert.All(candidates, o => { Assert.Equal(2, o.WaterSpan); Assert.NotNull(o.OtherBankIndex); Assert.True(o.RequiresConnection); Assert.Equal(0, o.UsableArea); });
    }

    [Fact]
    public void RepeatedWaterStepsKeepCumulativeBudgetAndReproduce()
    {
        var rows = EnrichmentFixtures.Mass('W', 35); var a = EnrichmentFixtures.Create(rows); var b = EnrichmentFixtures.Create(rows);
        for (var i = 0; i < 3; i++)
        {
            var step = EnrichmentFixtures.Step(WorldStepKind.WaterEnrichment, "water-" + i, 3);
            WaterEnricher.Apply(a, step, ContentBundle.Canonical()); WaterEnricher.Apply(b, step, ContentBundle.Canonical());
        }
        Assert.Equal(a.Samples, b.Samples); Assert.Equal(JsonSerializer.Serialize(a.FeatureOpportunities), JsonSerializer.Serialize(b.FeatureOpportunities));
        Assert.InRange(a.Samples.Count(s => s.TerrainTypeId == "water"), 1225 - 1225 * 7 / 100, 1224);
        Assert.Equal(a.Features.Count, a.Features.Select(f => f.Id).Distinct().Count());
    }
}
