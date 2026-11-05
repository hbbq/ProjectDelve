using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldEnrichmentCorpusTests
{
    [Fact]
    public void SeedsProduceBranchesPassesChainsAndDifferentIrregularFootprints()
    {
        var forests = new HashSet<string>(); var rocks = new HashSet<string>(); var islands = new HashSet<string>();
        var branched = false; var pass = false; var chain = false;
        for (ulong seed = 0; seed < 24; seed++)
        {
            var forest = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('T', 30), seed);
            ForestEnricher.Apply(forest, EnrichmentFixtures.Step(WorldStepKind.ForestEnrichment, intensity: 3), ContentBundle.Canonical());
            forests.Add(string.Join(',', forest.Samples.Select(s => s.TerrainTypeId)));
            branched |= forest.Features.Any(f => f.EntranceIndices.Count > 1);
            var rock = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('R', 30), seed);
            RockEnricher.Apply(rock, EnrichmentFixtures.Step(WorldStepKind.RockEnrichment, intensity: 3), ContentBundle.Canonical());
            rocks.Add(string.Join(',', rock.Samples.Select(s => s.TerrainTypeId)));
            pass |= rock.Features.Any(f => f.Kind == "cave-with-mountain-pass");
            var water = EnrichmentFixtures.Create(EnrichmentFixtures.Mass('W', 35), seed);
            WaterEnricher.Apply(water, EnrichmentFixtures.Step(WorldStepKind.WaterEnrichment, intensity: 3), ContentBundle.Canonical());
            islands.Add(string.Join(',', water.Samples.Select(s => s.TerrainTypeId)));
            chain |= water.Features.Any(f => f.Kind == "island-chain");
        }
        Assert.True(branched); Assert.True(pass); Assert.True(chain);
        Assert.True(forests.Count > 20); Assert.True(rocks.Count > 20); Assert.True(islands.Count > 20);
    }

    [Theory]
    [InlineData(120, 100, -60, -50)]
    [InlineData(120, 120, -17, -13)]
    [InlineData(45, 70, -1, -69)]
    public void PresetCorpusPreservesNaturalInputAndReportsExactStageChanges(int width, int height, int x, int y)
    {
        foreach (var preset in WorldGenerationPresets.All)
        {
            var recipe = new WorldGenerationRecipe(new(x, y, width, height), preset.Settings, 42, new());
            var world = WorldGenerator.Generate(recipe);
            var sampler = new NaturalTerrainSampler(recipe.World);
            for (var i = 0; i < world.Context.Samples.Length; i++)
                Assert.Equal(sampler.Sample(x + i % width, y + i / width), world.Context.OriginalSamples[i]);
            for (var stage = 1; stage < world.Preview.Frames.Count; stage++)
            {
                var before = world.Preview.Frames[stage - 1]; var after = world.Preview.Frames[stage];
                Assert.Equal(Enumerable.Range(0, width * height).Where(i => before.Terrain[i] != after.Terrain[i]), after.ChangedCells);
                Assert.Equal(after.ChangedCells.Length, after.Diagnostics.ChangedCells);
                foreach (var e in after.Entrances) Assert.True(after.WalkableComponents[e.InsideIndex] >= 0 && after.WalkableComponents[e.OutsideIndex] >= 0);
                foreach (var o in after.Opportunities) Assert.Equal(after.WalkableComponents[o.AnchorIndex], o.WalkableComponent);
            }
            Assert.All(world.Context.FeatureOpportunities, o => Assert.Equal(world.Context.CurrentAnalysis!.WalkableLabels[o.AnchorIndex], o.WalkableComponent));
            Assert.Equal(ScenarioDefinitionJson.ToJson(world.Definition), ScenarioDefinitionJson.ToJson(ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(world.Definition))));
        }
    }

    [Fact]
    public void DependencyGraphCannotHideAnEarlierTerrainWriter()
    {
        var recipe = new WorldGenerationRecipe(new(), new(), 12345, new());
        var plan = WorldGenerationPlan.FromRecipe(recipe);
        var steps = plan.Steps.ToArray(); steps[3] = steps[3] with { DependsOn = ["formations"] };
        Assert.Throws<ArgumentException>(() => (plan with { Steps = steps }).Validate());
    }
}
