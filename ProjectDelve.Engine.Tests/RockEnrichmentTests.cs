using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RockEnrichmentTests
{
    [Fact]
    public void DeepCavesPreferSubstantialApproachesOverAnEnclosedPocket()
    {
        var rows = EnrichmentFixtures.Mass('R', 40);
        for (var y = 20; y < 23; y++) rows[y] = rows[y][..20] + "..." + rows[y][23..];
        var c = EnrichmentFixtures.Create(rows);
        RockEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.RockEnrichment, intensity: 3), ContentBundle.Canonical());
        var a = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        Assert.NotEmpty(c.Features);
        Assert.All(c.FeatureOpportunities, o => Assert.Equal(a.WalkableLabels[0], a.WalkableLabels[o.AnchorIndex]));
        Assert.All(c.Entrances, e => Assert.Equal(a.WalkableLabels[e.InsideIndex], a.WalkableLabels[e.OutsideIndex]));
        var terrain = new EnrichmentTerrain(c, ContentBundle.Canonical());
        var mouths = c.Entrances.Select(e => e.InsideIndex).ToHashSet();
        foreach (var i in Enumerable.Range(0, c.Samples.Length).Where(i => c.Samples[i] != c.OriginalSamples[i]))
            if (terrain.Neighbors(i).Any(j => c.OriginalSamples[j].TerrainTypeId != "solid-rock")) Assert.Contains(i, mouths);
    }

    [Fact]
    public void LargeMountainReceivesDistributedInteriorExplorationWithinOriginalCeiling()
    {
        var c = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('R', 80));
        RockEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.RockEnrichment, intensity: 3), ContentBundle.Canonical());
        var a = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        Assert.True(c.Features.Count > 5); // Old feature cap left almost the whole interior untouched.
        Assert.Equal(4, c.FeatureOpportunities.Select(o => (o.AnchorIndex % 84 < 42, o.AnchorIndex / 84 < 42)).Distinct().Count());
        Assert.True(a.Depth.Max() < c.OriginalAnalysis!.Depth.Max());
        Assert.InRange(c.Samples.Count(s => s.TerrainTypeId == "stone-floor"), 1, 6400 * 13 / 100);
        Assert.All(c.FeatureOpportunities, o => Assert.Equal(a.WalkableLabels[0], a.WalkableLabels[o.AnchorIndex]));
        Assert.InRange(c.RoutingVisitedCells, 1, 2_000_000);
    }

    [Theory]
    [InlineData(30, 24, 0, 0, 1UL)]
    [InlineData(60, 50, -17, -13, 42UL)]
    public void FineMixedEnrichmentPreservesThePreviouslyLostDungeon(int width, int height, int x, int y, ulong seed)
    {
        var recipe = new WorldGenerationRecipe(new(x,y,width,height), new(35,20,35,FeatureScale.Fine,WaterForm.Mixed), seed,
            new(Enabled:false,Intensity:3,Forest:false,Rock:false,Water:false,Grassland:false),new());
        Assert.Single(WorldGenerator.Generate(recipe).Context.Sites.Where(s => s.MountainDungeon is not null));
        var enriched = WorldGenerator.Generate(recipe with { Enrichment = new(Enabled:true,Intensity:3,Forest:false,Rock:true,Water:false,Grassland:false) });
        Assert.Single(enriched.Context.Sites.Where(s => s.MountainDungeon is not null));
    }

    [Fact]
    public void RockMassGainsBufferedChamberWithAnOrdinaryEntrance()
    {
        var c = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('R'));
        RockEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.RockEnrichment), ContentBundle.Canonical());
        Assert.NotEmpty(c.Features); Assert.NotEmpty(c.Entrances);
        var current = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        foreach (var room in c.FeatureOpportunities)
        {
            Assert.Equal("cave-chamber", room.Kind); Assert.InRange(room.UsableArea, 7, 40);
            Assert.Equal(current.WalkableLabels[0], current.WalkableLabels[room.AnchorIndex]);
        }
        foreach (var entry in c.Entrances) Assert.Equal(current.WalkableLabels[entry.OutsideIndex], current.WalkableLabels[entry.InsideIndex]);
        Assert.InRange(c.Samples.Count(s => s.TerrainTypeId == "solid-rock"), 360, 399);
        Assert.Equal(400, c.OriginalSamples.Count(s => s.TerrainTypeId == "solid-rock"));
    }

    [Fact]
    public void ExcavationIsCumulativeAndDoesNotBreachWaterOrUnrelatedNaturalCavities()
    {
        var rows = EnrichmentFixtures.Mass('R', 30);
        rows[12] = rows[12][..13] + "WW.." + rows[12][17..];
        var c = EnrichmentFixtures.Create(rows);
        for (var repeat = 0; repeat < 3; repeat++) RockEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.RockEnrichment, "rock-" + repeat, 3), ContentBundle.Canonical());
        var terrain = new EnrichmentTerrain(c, ContentBundle.Canonical());
        var edits = Enumerable.Range(0, c.Samples.Length).Where(i => c.Samples[i] != c.OriginalSamples[i]).ToArray();
        Assert.NotEmpty(edits); Assert.True(edits.Length <= 896 * 13 / 100);
        foreach (var i in edits)
        {
            Assert.Equal("solid-rock", c.OriginalSamples[i].TerrainTypeId);
            Assert.DoesNotContain(terrain.Neighbors(i), j => c.OriginalSamples[j].TerrainTypeId == "water");
        }
        Assert.All(Enumerable.Range(0, c.Samples.Length).Where(i => c.OriginalSamples[i].TerrainTypeId != "solid-rock"), i => Assert.Equal(c.OriginalSamples[i], c.Samples[i]));
    }

    [Fact]
    public void EnclosedOrThinRockDoesNotInventEntrances()
    {
        var c = EnrichmentFixtures.Create(Enumerable.Repeat(new string('R', 30), 30).ToArray());
        RockEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.RockEnrichment), ContentBundle.Canonical());
        Assert.Empty(c.Features); Assert.Empty(c.Entrances);
    }

    [Fact]
    public void MultipleChambersAndStageOutputsAreReproducible()
    {
        var rows = EnrichmentFixtures.Mass('R', 38);
        var a = EnrichmentFixtures.Create(rows); var b = EnrichmentFixtures.Create(rows);
        var step = EnrichmentFixtures.Step(WorldStepKind.RockEnrichment, intensity: 3);
        RockEnricher.Apply(a, step, ContentBundle.Canonical()); RockEnricher.Apply(b, step, ContentBundle.Canonical());
        Assert.True(a.Features.Count >= 2);
        Assert.Equal(a.Samples, b.Samples); Assert.Equal(JsonSerializer.Serialize(a.Features), JsonSerializer.Serialize(b.Features));
        var current = TerrainAnalyzer.Analyze(a.Window, a.Samples);
        Assert.All(a.FeatureOpportunities, o => Assert.Equal(current.WalkableLabels[0], current.WalkableLabels[o.AnchorIndex]));
    }
}
