using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CivilizationGenerationTests
{
    private static WorldGenerationRecipe Recipe(int width = 50, int height = 50) =>
        new(new(-17, -13, width, height), new(0, 0, 0), 12345, new(), new());

    [Fact]
    public void EffectiveCivilizationSettingsRoundtripAndInvalidSettingsFail()
    {
        foreach (var enabled in new[] { false, true })
        foreach (var intensity in new[] { 1, 2, 3 })
        {
            var recipe = Recipe() with { Civilization = new(enabled, ContentDensity.VeryHigh, ContentDensity.Medium, intensity) };
            Assert.Equal(recipe, WorldGenerationRecipe.Parse(recipe.ToString()));
        }
        Assert.Throws<ArgumentException>(() => (Recipe() with { Civilization = new(Campsites: (ContentDensity)9) }).Validate());
        Assert.Throws<FormatException>(() => WorldGenerationRecipe.Parse(Recipe().ToString().Replace("-P2", "-P4")));
        var noSites = WorldGenerator.Generate(Recipe() with { Civilization = new(false) });
        Assert.Empty(noSites.Context.Sites); Assert.DoesNotContain(noSites.Preview.Frames, f => f.Stage == "Civilization");
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(120, 100)]
    [InlineData(120, 120)]
    public void AcceptedNestedEditsReconstructFinalBoardAndCarrySemanticObjects(int width, int height)
    {
        var world = WorldGenerator.Generate(Recipe(width, height));
        Assert.Equal(DensityScaling.Campsites(world.Window, ContentDensity.Low) + DensityScaling.Settlements(world.Window, ContentDensity.Medium), world.Context.Sites.Count);
        var index = world.Preview.Frames.ToList().FindIndex(f => f.Stage == "Civilization");
        var frame = world.Preview.Frames[index];
        var terrain = world.Preview.Frames[index - 1].Terrain.Select(c => world.Preview.TerrainPalette[c - 'A']).ToArray();
        var edges = new Dictionary<EdgeKey, EdgeKind>(); var seen = new HashSet<string>();
        foreach (var operation in frame.Construction)
        {
            Assert.True(seen.Add(operation.Id));
            if (operation.ParentId is not null) Assert.Contains(operation.ParentId, seen);
            foreach (var change in operation.TerrainChanges) { Assert.Equal(change.Before, terrain[change.Index]); terrain[change.Index] = change.After; }
            foreach (var change in operation.EdgeChanges) { Assert.True(change.Before == edges.GetValueOrDefault(change.Key, EdgeKind.None), $"{operation.Id} {change.Key}: trace says {change.Before}, replay has {edges.GetValueOrDefault(change.Key, EdgeKind.None)}; " + string.Join("; ", frame.Construction.Where(o => o.EdgeChanges.Any(e => e.Key == change.Key)).Select(o => o.Id + JsonSerializer.Serialize(o.EdgeChanges.Where(e => e.Key == change.Key))))); if (change.After == EdgeKind.None) edges.Remove(change.Key); else edges[change.Key] = change.After; }
        }
        Assert.Equal(frame.Terrain.Select(c => world.Preview.TerrainPalette[c - 'A']), terrain);
        Assert.Equal(frame.Edges.Count, edges.Count);
        Assert.All(frame.Edges, e => Assert.Equal(e.Kind, edges[new(e.Position.Y * width + e.Position.X, e.Direction)]));
        var board = new PlanningBoard(world.Definition, ContentBundle.Canonical());
        Assert.Equal(terrain, Enumerable.Range(0, width * height).Select(board.TerrainAt));
        Assert.Empty(world.Preview.Frames[^1].ChangedCells); Assert.Empty(world.Preview.Frames[^1].ChangedEdges);
        Assert.True(world.Context.CurrentAnalysis!.Revision > world.Context.OriginalAnalysis!.Revision);
        Assert.All(world.Context.Sites, site => AssertIndependentAccess(board, site));
        Assert.Empty(world.Definition.Units); Assert.Empty(world.Definition.Objectives ?? []);
        Assert.NotEmpty(frame.ChangedEdges); Assert.True(world.Metrics.ConstructionValidations >= 4);
        Assert.InRange(world.Context.CivilizationStats!.Work, 1, 2_000_000);
    }

    [Fact]
    public void RegenerationAndLaterCampsDoNotReseedEarlierSitesOrNaturalTerrain()
    {
        var one = WorldGenerator.Generate(Recipe() with { Civilization = new(Campsites: ContentDensity.Low) });
        var later = Recipe() with { Civilization = new(Campsites: ContentDensity.Medium) };
        var two = WorldGenerator.Generate(later); var repeat = WorldGenerator.Generate(later);
        Assert.Equal(two.Fingerprint, repeat.Fingerprint); Assert.Equal(JsonSerializer.Serialize(two.Preview), JsonSerializer.Serialize(repeat.Preview));
        Assert.Equal(JsonSerializer.Serialize(one.Context.Sites), JsonSerializer.Serialize(two.Context.Sites.Take(one.Context.Sites.Count)));
        var natural = WorldGenerator.Generate(Recipe() with { Civilization = null });
        Assert.Equal(natural.Context.OriginalSamples, two.Context.OriginalSamples);
        Assert.Equal(natural.Preview.Frames.Take(6).Select(f => f.Terrain), two.Preview.Frames.Take(6).Select(f => f.Terrain));
    }

    [Fact]
    public void ImpossibleNarrowWindowReportsFailuresWithoutPartialEdits()
    {
        var world = WorldGenerator.Generate(Recipe(7, 120) with { Enrichment = null });
        Assert.Empty(world.Context.Sites); Assert.Equal(DensityScaling.Campsites(world.Window, ContentDensity.Low) + DensityScaling.Settlements(world.Window, ContentDensity.Medium) +
            MountainDungeonGenerator.Target(world.Window, new()), world.Context.PlanningFailures.Count);
        Assert.Single(world.Context.Construction.Where(o => o.Name == "Civilization")); Assert.Empty(world.Definition.Board.Edges);
        Assert.All(world.Context.Construction, o => Assert.Contains(o.Name, new[] { "Civilization", "Beacon" }));
        var machineCells = (world.Definition.Machines ?? []).Select(m => m.Position.Y * world.Window.Width + m.Position.X).ToHashSet();
        var before = world.Preview.Frames[0].Terrain; var after = world.Preview.Frames[^1].Terrain;
        Assert.All(Enumerable.Range(0, before.Length).Where(i => !machineCells.Contains(i)), i => Assert.Equal(before[i], after[i]));
    }

    [Fact]
    public void CampsiteUsesAnalyzedAndEnrichedClearingOpportunities()
    {
        foreach (var enriched in new[] { false, true })
        {
            var context = new WorldGenerationContext(new(17, new(0, 0, 0)), new(0, 0, 50, 50)); context.SampleNaturalTerrain();
            var analysis = TerrainAnalyzer.Analyze(context.Window, context.Samples);
            var opportunity = new TerrainOpportunity("test-clearing", "clearing", 25 * 50 + 25, new(20, 20, 11, 11), 100, null, null, 0, false);
            context.OriginalAnalysis = analysis; context.CurrentAnalysis = analysis with { Opportunities = enriched ? [] : [opportunity] };
            if (enriched) context.FeatureOpportunities.Add(opportunity);
            CivilizationGenerator.Apply(context, new("civilization", WorldStepKind.Civilization, new CivilizationStepParameters(new(Campsites: ContentDensity.Low, Settlements: ContentDensity.None)), ["formations"]), ContentBundle.Canonical());
            Assert.Equal(DensityScaling.Campsites(context.Window, ContentDensity.Low), context.Sites.Count); Assert.Equal("campfire", context.Samples[opportunity.AnchorIndex].TerrainTypeId);
        }
    }

    [Fact]
    public void EdgeOnlyCommitInvalidatesAndRecomputesTopology()
    {
        var context = new WorldGenerationContext(new(1, new(0, 0, 0)), new(0, 0, 2, 1)); context.SampleNaturalTerrain();
        context.ConstructionBoard = new(context.Materialize(), ContentBundle.Canonical());
        context.RefreshConstructionAnalysis(); var before = context.CurrentAnalysis!;
        var draft = context.ConstructionBoard.BeginDraft(new(0, 0, 2, 1), "test", new()); draft.SetEdge(new(0, EdgeDirection.Right), EdgeKind.Wall);
        Assert.True(context.ConstructionBoard.TryCommit(draft.BuildPatch(), out _)); context.RefreshConstructionAnalysis();
        Assert.Equal(1, before.Connectivity.ComponentCount); Assert.Equal(2, context.CurrentAnalysis!.Connectivity.ComponentCount);
        Assert.NotEqual(before.SourceFingerprint, context.CurrentAnalysis.SourceFingerprint); Assert.True(context.CurrentAnalysis.Revision > before.Revision);
    }

    [Fact]
    public void CivilizationMustBeLastWriterAndDependsOnOriginalAnalysis()
    {
        var plan = WorldGenerationPlan.FromRecipe(Recipe());
        var invalid = new WorldGenerationStep("late-forest", WorldStepKind.ForestEnrichment, new EnrichmentStepParameters(), ["civilization"]);
        Assert.Throws<ArgumentException>(() => (plan with { Steps = [.. plan.Steps, invalid] }).Validate());
    }

    private static void AssertIndependentAccess(PlanningBoard board, SiteInfo site)
    {
        var footprint = site.Footprint.Indices(board.Width).ToHashSet();
        var usable = footprint.Where(board.Passable).ToHashSet();
        var reached = new HashSet<int> { site.Entrances[0].InsideIndex }; var queue = new Queue<int>(reached);
        while (queue.TryDequeue(out var i)) foreach (var j in board.Neighbors(i))
            if (usable.Contains(j) && (board.EdgeAt(EdgeKey.Between(i, j, board.Width)) is EdgeKind.None or EdgeKind.OpenDoor or EdgeKind.ClosedDoor) && reached.Add(j)) queue.Enqueue(j);
        Assert.True(usable.SetEquals(reached)); Assert.Equal(usable.Count, site.Access.ConnectedCells);
        foreach (var building in site.Buildings)
        {
            Assert.All(building.Rooms, room => Assert.All(room.Usable.Indices(board.Width), i => Assert.Contains(i, reached)));
            Assert.All(building.Portals, portal => Assert.Equal(portal.Kind, board.EdgeAt(portal.Edge)));
        }
    }
}
