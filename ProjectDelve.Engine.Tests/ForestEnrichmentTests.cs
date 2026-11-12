using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ForestEnrichmentTests
{
    [Fact]
    public void EnclosedClearingConnectsToOutsideGroundRatherThanItsOwnPocket()
    {
        var rows = EnrichmentFixtures.Mass('T', 30);
        for (var y = 14; y < 19; y++) rows[y] = rows[y][..14] + "....." + rows[y][19..];
        var c = EnrichmentFixtures.Create(rows);
        var pocket = Assert.Single(c.OriginalAnalysis!.Opportunities.Where(o => o.Kind == "clearing"));
        Assert.NotEqual(c.OriginalAnalysis.WalkableLabels[0], c.OriginalAnalysis.WalkableLabels[pocket.AnchorIndex]);

        ForestEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.ForestEnrichment, intensity: 3), ContentBundle.Canonical());

        var after = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        Assert.Equal(after.WalkableLabels[0], after.WalkableLabels[pocket.AnchorIndex]);
        Assert.Contains(c.FeatureOpportunities, o => o.AnchorIndex == pocket.AnchorIndex);
        Assert.All(c.FeatureOpportunities, o => Assert.Equal(after.WalkableLabels[0], after.WalkableLabels[o.AnchorIndex]));
        var reuse = c.Features.Single(f => f.Id == c.FeatureOpportunities.Single(o => o.AnchorIndex == pocket.AnchorIndex).FeatureId);
        Assert.Contains(reuse.EntranceIndices, e => c.OriginalAnalysis.WalkableLabels[c.Entrances[e].OutsideIndex] == c.OriginalAnalysis.WalkableLabels[0]);
    }

    [Fact]
    public void DeepForestUsesSeveralInteriorRegionsWithinTheOriginalCeiling()
    {
        var c = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('T', 80));
        var before = c.OriginalAnalysis!;
        ForestEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.ForestEnrichment, intensity: 3), ContentBundle.Canonical());
        var after = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        // Conservative quality floors for this dedicated square fixture, not universal map guarantees.
        Assert.True(c.Features.Count > 6);
        Assert.True(after.Depth.Where((_, i) => c.Samples[i].TerrainTypeId == "tree").Max() < 30);
        Assert.True(after.Depth.Where((d, i) => d >= 6 && c.Samples[i].TerrainTypeId == "tree").Count() <
            before.Depth.Count(d => d >= 6) * .75);
        Assert.Equal(4, c.FeatureOpportunities.Select(o => (o.AnchorIndex % 84 >= 42, o.AnchorIndex / 84 >= 42)).Distinct().Count());
        Assert.All(c.FeatureOpportunities, o => Assert.Equal(after.WalkableLabels[0], after.WalkableLabels[o.AnchorIndex]));
        Assert.InRange(c.Features.Sum(f => f.ChangedCells), 400, 6400 * 18 / 100);
        Assert.InRange(c.Features.Count, 1, 2 + 6400 / 250);
        Assert.InRange(c.RoutingSearches, 1, (16 + (2 + 6400 / 250) * 4) * 6);
        Assert.InRange(c.RoutingVisitedCells, 1, 2_000_000);
    }

    [Fact]
    public void SeparateMeaningfulApproachRegionsBothReceiveForestFeatures()
    {
        // A Water partition keeps the smaller right region permanently separate. Neither
        // region is required to join a universal component to justify exploring its forest.
        var rows = Enumerable.Range(0, 30).Select(y => string.Concat(Enumerable.Range(0, 64).Select(x =>
            x is 35 or 36 ? 'W' : y >= 4 && y < 24 && (x >= 4 && x < 28 || x >= 40 && x < 60) ? 'T' : '.'))).ToArray();
        var c = EnrichmentFixtures.Create(rows);
        ForestEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.ForestEnrichment, intensity: 3), ContentBundle.Canonical());
        var after = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        Assert.NotEqual(after.WalkableLabels[0], after.WalkableLabels[63]);
        Assert.Contains(c.FeatureOpportunities, o => after.WalkableLabels[o.AnchorIndex] == after.WalkableLabels[0]);
        Assert.Contains(c.FeatureOpportunities, o => after.WalkableLabels[o.AnchorIndex] == after.WalkableLabels[63]);
        Assert.All(Enumerable.Range(0, c.Samples.Length).Where(i => c.OriginalSamples[i].TerrainTypeId == "water"),
            i => Assert.Equal(c.OriginalSamples[i], c.Samples[i]));
    }

    [Theory]
    [InlineData("wet-grass", true)]
    [InlineData("rocky-water", true)]
    [InlineData("water", false)]
    public void EntranceEligibilityUsesLandPermissionsAndPreservesApproachTerrain(string approach, bool accessible)
    {
        var c = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('T'));
        for (var i = 0; i < c.Samples.Length; i++)
            if (c.Samples[i].TerrainTypeId == "grass") c.OriginalSamples[i] = c.Samples[i] = c.Samples[i] with { TerrainTypeId = approach };
        c.OriginalAnalysis = c.CurrentAnalysis = TerrainAnalyzer.Analyze(c.Window, c.Samples);
        ForestEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.ForestEnrichment), ContentBundle.Canonical());
        Assert.Equal(accessible, c.Features.Count > 0);
        Assert.All(c.Entrances, e => Assert.Equal(approach, c.Samples[e.OutsideIndex].TerrainTypeId));
        Assert.All(Enumerable.Range(0, c.Samples.Length).Where(i => c.OriginalSamples[i].TerrainTypeId == approach),
            i => Assert.Equal(c.OriginalSamples[i], c.Samples[i]));
    }

    [Fact]
    public void InfeasibleFullConnectionLeavesNoPartialClearingOrMetadata()
    {
        var c = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('T'));
        // Previous steps left five Cells: enough to attempt work, but insufficient
        // for any complete clearing plus connection in this fixture.
        var core = Enumerable.Range(0, c.Samples.Length).Where(i => c.OriginalSamples[i].TerrainTypeId == "tree").Take(47).ToArray();
        foreach (var i in core) c.Samples[i] = c.Samples[i] with { TerrainTypeId = "dirt" };
        var before = c.Samples.ToArray();
        ForestEnricher.Apply(c, EnrichmentFixtures.Step(WorldStepKind.ForestEnrichment), ContentBundle.Canonical());
        Assert.True(c.RoutingSearches > 0);
        Assert.Equal(before, c.Samples); Assert.Empty(c.Features); Assert.Empty(c.Entrances); Assert.Empty(c.FeatureOpportunities);
    }

    [Fact]
    public void SecondMouthBenefitUsesActualWalkIncludingProposedMainTrail()
    {
        // The small pocket is separated from the exterior by two Tree columns.
        var c = EnrichmentFixtures.Create([".......", ".TTTTT.", ".T...T.", ".TTTTT.", "......."]);
        var geometry = new ForestGeometry(new EnrichmentTerrain(c, ContentBundle.Canonical()));
        var center = 2 * 7 + 3;
        HashSet<int> main = [2 * 7 + 1];
        // A neighboring mouth offers an existing short walk, even before committing the main trail.
        Assert.True(geometry.HasShortPath(center, 7, 8, main));
        // A useful opposite mouth saves a real detour around the whole mass.
        Assert.False(geometry.HasShortPath(center, 2 * 7 + 6, 6, main));
        Assert.True(geometry.HasShortPath(center, 2 * 7 + 6, 6, new HashSet<int>(main) { 2 * 7 + 5 }));
        Assert.Equal(c.OriginalSamples, c.Samples);
    }

    [Theory]
    [InlineData(30, 24, 1)]
    [InlineData(60, 50, 42)]
    [InlineData(120, 120, 42)]
    public void ForestOnlyRegressionCorpusIsDeterministicBudgetedAndEngineValid(int width, int height, ulong seed)
    {
        var recipe = new WorldGenerationRecipe(new(-17, -13, width, height), new(60, 8, 8), seed,
            new(Intensity: 3, Rock: false, Water: false, Grassland: false));
        var a = WorldGenerator.Generate(recipe); var b = WorldGenerator.Generate(recipe);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(a.Preview), JsonSerializer.Serialize(b.Preview));
        Assert.NotNull(GameEngine.CreateGame(a.Definition));
        Assert.Equal(ScenarioDefinitionJson.ToJson(a.Definition),
            ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(a.Definition))));
        var original = a.Context.OriginalAnalysis!;
        foreach (var group in Enumerable.Range(0, a.Context.Samples.Length).GroupBy(i => original.FormationLabels[i]))
        {
            var edits = group.Where(i => a.Context.OriginalSamples[i].TerrainTypeId != a.Context.Samples[i].TerrainTypeId).ToArray();
            Assert.InRange(edits.Length, 0, original.Formations[group.Key].CoreArea * 18 / 100);
            Assert.All(edits, i => Assert.Equal("tree", a.Context.OriginalSamples[i].TerrainTypeId));
        }
        Assert.InRange(a.Context.RoutingVisitedCells, 0, 2_000_000);
    }

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
