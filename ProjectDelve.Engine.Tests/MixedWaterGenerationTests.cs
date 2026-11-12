using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MixedWaterGenerationTests
{
    [Fact]
    public void ReducedCatalogKeepsOriginalNaturalSurfacesAndCropAgreement()
    {
        string[] ids = ["grass", "dirt", "tree", "solid-rock", "water", "stone-floor"];
        // Keep this provider self-contained: destruction references are irrelevant to sampling.
        var content = new ContentBundle { Terrain = [.. TerrainCatalog.All.Where(t => ids.Contains(t.Id))
            .Select(t => t with { DestroyedTerrainTypeId = null })] };
        var recipe = new WorldGenerationRecipe(new(-60, -60, 120, 120), new(20, 25, 15), 12345);
        var generated = WorldGenerator.Generate(recipe, content);
        Assert.All(generated.Context.Samples, s => Assert.Contains(s.TerrainTypeId, ids));
        var crop = new WorldGenerationContext(recipe.World, new(-23, -19, 20, 20));
        crop.SampleNaturalTerrain(content);
        var fullContext = new WorldGenerationContext(recipe.World, recipe.Window);
        fullContext.SampleNaturalTerrain();
        var reducedSampler = new NaturalTerrainSampler(recipe.World, content);
        var mixedCount = 0;
        for (var y = 0; y < 120; y++)
        for (var x = 0; x < 120; x++)
        {
            var full = fullContext.Samples[y * 120 + x];
            var reduced = generated.Context.Samples[y * 120 + x];
            var original = full.TerrainTypeId switch
            {
                "rocky-water" => "water",
                "wet-grass" => full.Soil > .64 && full.Openness < .62 ? "dirt" : "grass",
                _ => full.TerrainTypeId
            };
            Assert.Equal(original, reduced.TerrainTypeId);
            if (full.TerrainTypeId is "wet-grass" or "rocky-water") mixedCount++;
            if (x is >= 37 and < 57 && y is >= 41 and < 61)
            {
                Assert.Equal(reduced, crop.Samples[(y - 41) * 20 + x - 37]);
                Assert.Equal(reduced, reducedSampler.Sample(x - 60, y - 60));
            }
        }
        Assert.True(mixedCount > 0);
    }

    [Fact]
    public void AnalysisFingerprintIncludesResolvedLandscapeFamily()
    {
        var terrain = new TerrainDefinition("test-surface", "Test Surface", [MovementKinds.Land, MovementKinds.Water], false);
        var content = new ContentBundle { Terrain = [terrain] };
        NaturalTerrainSample[] samples = [new(terrain.Id, 0, 0, 0, 0)];
        var original = TerrainAnalyzer.Analyze(new(0, 0, 1, 1), samples, content);
        var changed = TerrainAnalyzer.Analyze(new(0, 0, 1, 1), samples,
            content with { Terrain = [terrain with { Categories = [TerrainCategories.Water] }] });
        Assert.Equal(original.WaterTravelLabels, changed.WaterTravelLabels);
        Assert.Equal(original.WaterConnectivity.WalkableArea, changed.WaterConnectivity.WalkableArea);
        Assert.Equal(NaturalFormationKind.Other, Assert.Single(original.Formations).Kind);
        Assert.Equal(NaturalFormationKind.WaterBody, Assert.Single(changed.Formations).Kind);
        Assert.NotEqual(original.SourceFingerprint, changed.SourceFingerprint);
    }

    [Theory]
    [InlineData(int.MinValue, int.MinValue)]
    [InlineData(int.MaxValue - 119, int.MaxValue - 119)]
    public void IntermediateWaterSamplingKeepsMixedTerrainStableAtCoordinateLimits(int left, int top)
    {
        var world = new WorldDefinition(12345, new(20, 25, 15));
        var whole = new WorldGenerationContext(world, new(left, top, 120, 120));
        var crop = new WorldGenerationContext(world, new(left + 30, top + 30, 20, 20));
        whole.SampleNaturalTerrain(); crop.SampleNaturalTerrain();
        var sampler = new NaturalTerrainSampler(world);
        Assert.Contains(whole.Samples, s => s.TerrainTypeId is "wet-grass" or "rocky-water");
        for (var y = 0; y < 20; y++)
        for (var x = 0; x < 20; x++)
        {
            var sample = crop.Samples[y * 20 + x];
            Assert.Equal(whole.Samples[(y + 30) * 120 + x + 30], sample);
            Assert.Equal(sampler.Sample(left + 30 + x, top + 30 + y), sample);
        }
    }

    [Theory]
    [InlineData("wet-grass")]
    [InlineData("rocky-water")]
    [InlineData("stepping-stones")]
    public void SiteDryPreparationRequiresWaterPermissionAndChargesWaterBudget(string terrain)
    {
        var board = new PlanningBoard(Scenario(terrain), ContentBundle.Canonical());
        var draft = board.BeginDraft(new(0, 0, 1, 1), "site-preparation", new(32, 100000));
        var prohibited = SiteGeometry.Prepare(draft, [0], "grass", new(FillWater: false));
        Assert.Equal(PlacementFailureCode.PreparationLimit, Assert.IsType<SiteGeometry.PreparationProblem>(prohibited).Code);
        Assert.Empty(draft.BuildPatch().TerrainChanges);
        var tooCheap = SiteGeometry.Prepare(draft, [0], "grass", new(FillWater: true, MaxCost: 5));
        Assert.Equal(PlacementFailureCode.PreparationLimit, Assert.IsType<SiteGeometry.PreparationProblem>(tooCheap).Code);
        Assert.Empty(draft.BuildPatch().TerrainChanges);
        Assert.Null(SiteGeometry.Prepare(draft, [0], "grass", new(FillWater: true, MaxCost: 6)));
        var summary = SiteGeometry.Summary(draft.BuildPatch());
        Assert.Equal(1, summary.WaterFilled);
        Assert.Equal(6, summary.Cost);
        Assert.Equal(terrain, board.TerrainAt(0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void PureWaterCoverageEndpointsDoNotAcquireMixedPatches(int coverage)
    {
        var context = new WorldGenerationContext(new(42, new(0, coverage, 0)), new(int.MinValue, int.MaxValue - 3, 4, 4));
        context.SampleNaturalTerrain();
        Assert.DoesNotContain(context.Samples, s => s.TerrainTypeId is "wet-grass" or "rocky-water" or "stepping-stones");
        if (coverage == 100) Assert.All(context.Samples, s => Assert.Equal("water", s.TerrainTypeId));
    }

    [Fact]
    public void NaturalMixedWaterIsSparseDeterministicAndStableAcrossSignedCrops()
    {
        var world = new WorldDefinition(12345, new(20, 25, 15));
        var whole = new WorldGenerationContext(world, new(-60, -60, 120, 120));
        var crop = new WorldGenerationContext(world, new(-23, -19, 60, 60));
        whole.SampleNaturalTerrain(); crop.SampleNaturalTerrain();
        var sampler = new NaturalTerrainSampler(world);
        for (var y = 0; y < 60; y++)
        for (var x = 0; x < 60; x++)
        {
            var sample = crop.Samples[y * 60 + x];
            Assert.Equal(whole.Samples[(y + 41) * 120 + x + 37], sample);
            Assert.Equal(sampler.Sample(x - 23, y - 19), sample);
        }
        // Pin this test-owned crop's first frequency pass, not a per-world quota.
        Assert.Equal(61, whole.Samples.Count(s => s.TerrainTypeId == "rocky-water"));
        Assert.Equal(138, whole.Samples.Count(s => s.TerrainTypeId == "wet-grass"));
        Assert.DoesNotContain(whole.Samples, s => s.TerrainTypeId == "stepping-stones");
    }

    [Fact]
    public void MixedCrossingJoinsIndependentLandAndWaterGraphsAndWallsBlockBoth()
    {
        NaturalTerrainSample[] samples = [new("grass", 0, 0, 0, 0), new("wet-grass", 0, 0, 0, 0),
            new("rocky-water", 0, 0, 0, 0), new("stepping-stones", 0, 0, 0, 0), new("water", 0, 0, 0, 0)];
        var analysis = TerrainAnalyzer.Analyze(new(0, 0, 5, 1), samples);
        Assert.Equal(4, analysis.Connectivity.WalkableArea);
        Assert.Equal(4, analysis.WaterConnectivity.WalkableArea);
        Assert.Equal(1, analysis.WaterConnectivity.ComponentCount);
        Assert.Equal(-1, analysis.WalkableLabels[4]);
        Assert.Equal(-1, analysis.WaterTravelLabels[0]);
        Assert.Equal(analysis.WalkableLabels[0], analysis.WalkableLabels[3]);
        Assert.Equal(analysis.WaterTravelLabels[1], analysis.WaterTravelLabels[4]);
        var blocked = TerrainAnalyzer.Analyze(new(0, 0, 5, 1), samples,
            edges: [new(new(2, 0), EdgeDirection.Right, EdgeKind.Wall)]);
        Assert.Equal(2, blocked.Connectivity.ComponentCount);
        Assert.Equal(2, blocked.WaterConnectivity.ComponentCount);
    }

    [Fact]
    public void AuthorizedRouteConstructsOnlyBoundedWaterCellsAndPreservesMixedTerrain()
    {
        var scenario = Scenario("grass", "wet-grass", "water", "water", "rocky-water", "grass");
        var requirements = RouteRequirements.For(RouteProfileKind.MinorPath, waterCrossings: true) with
            { MaxWaterCells = 2, MaxWaterRun = 2 };
        var plan = Assert.IsType<Planned<RoutePlan>>(Plan(scenario, requirements)).Plan;
        Assert.Equal(2, plan.Patch.TerrainChanges.Count(c => c.After == "stepping-stones"));
        Assert.DoesNotContain(plan.Patch.TerrainChanges, c => c.Before is "wet-grass" or "rocky-water");
        Assert.Equal(2, plan.Route.Preparation.WaterFilled);
        Assert.IsType<Rejected<RoutePlan>>(Plan(scenario, requirements with { MaxWaterCells = 1 }));
        Assert.IsType<Rejected<RoutePlan>>(Plan(scenario, requirements with { MaxWaterRun = 1 }));
        Assert.IsType<Rejected<RoutePlan>>(Plan(scenario, requirements, new HashSet<int> { 2 }));
    }

    [Fact]
    public void ExistingMixedCrossingsNeedNoWaterConstructionAllowance()
    {
        var scenario = Scenario("grass", "wet-grass", "rocky-water", "stepping-stones", "grass");
        var plan = Assert.IsType<Planned<RoutePlan>>(Plan(scenario, RouteRequirements.For(RouteProfileKind.MinorPath))).Plan;
        Assert.DoesNotContain(plan.Patch.TerrainChanges, c => c.Before is "wet-grass" or "rocky-water" or "stepping-stones");
        Assert.Equal(0, plan.Route.Preparation.WaterFilled);
    }

    [Fact]
    public void WideCrossingCountsTheCompleteChangedFootprint()
    {
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 6, Height = 2, DefaultTerrain = TerrainKind.Grass,
                Cells = [new(new(2, 0), TerrainKind.Water), new(new(2, 1), TerrainKind.Water)] },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        };
        var board = new PlanningBoard(scenario, ContentBundle.Canonical());
        var area = new CellBounds(0, 0, 6, 2);
        var draft = board.BeginDraft(area, "wide-crossing", new(32, 100000));
        var scope = new GenerationScope(42, "wide-crossing", draft);
        var requirements = RouteRequirements.For(RouteProfileKind.Road, waterCrossings: true) with
            { RequiredFootprint = 2, MaxWaterCells = 1 };
        var request = new RouteRequest("crossing", area, new("start", [0]), new("end", [4]),
            RouteProfileKind.Road, Requirements: requirements);
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(request, scope));
        var plan = Assert.IsType<Planned<RoutePlan>>(RouteGenerator.Plan(request with
            { Requirements = requirements with { MaxWaterCells = 2 } }, scope)).Plan;
        Assert.Equal(2, plan.Route.Preparation.WaterFilled);
        Assert.Equal(2, plan.Patch.TerrainChanges.Count(c => c.After == "stepping-stones"));
        Assert.Equal(2, plan.Route.VerifiedFootprint);
    }

    private static ScenarioDefinition Scenario(params string[] terrain) => new()
    {
        Board = new() { Width = terrain.Length, Height = 1, DefaultTerrain = TerrainKind.Grass,
            Cells = terrain.Select((id, x) => new CellDefinition(new(x, 0), TerrainKind.StoneFloor) { TerrainTypeId = id }).ToList() },
        UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
    };

    private static PlacementResult<RoutePlan> Plan(ScenarioDefinition scenario, RouteRequirements requirements,
        IReadOnlySet<int>? protectedCells = null)
    {
        var board = new PlanningBoard(scenario, ContentBundle.Canonical(), protectedCells);
        var area = new CellBounds(0, 0, board.Width, board.Height);
        var draft = board.BeginDraft(area, "mixed-water-test", new(32, 100000));
        return RouteGenerator.Plan(new("crossing", area, new("start", [0]), new("end", [board.Width - 1]),
            RouteProfileKind.MinorPath, Requirements: requirements), new(42, "mixed-water-test", draft));
    }
}
