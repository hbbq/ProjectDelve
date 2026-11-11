using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ExplorationRouteTests
{
    private static WorldGenerationContext Context()
    {
        var context = new WorldGenerationContext(new(14, new(0, 0, 0)), new(0, 0, 40, 40)); context.SampleNaturalTerrain();
        context.OriginalAnalysis = context.CurrentAnalysis = TerrainAnalyzer.Analyze(context.Window, context.Samples);
        context.EnsurePlanningBoard(ContentBundle.Canonical()); return context;
    }
    private static void AddNetwork(WorldGenerationContext context, RouteGeometry geometry, string id = "fixture/u")
    {
        var board = context.ConstructionBoard!; var indices = geometry.LocalIndices(board.Width, context.Window).ToArray();
        var draft = board.BeginDraft(new(0, 0, 40, 40), "fixture", new()); draft.EnableRoutePlanning();
        foreach (var i in indices) draft.ReplaceTerrain(i, "dirt"); draft.ReserveRoute(indices, id);
        Assert.True(board.TryCommit(draft.BuildPatch(), out _));
        RoutePort Port(string id, int index) => new(id, index, index % 40, index / 40, RouteEndpointKind.Network);
        context.Routes.Add(new(id, RoutePurpose.ThroughRoute, RouteProfileKind.MinorPath,
            GeneratedArea.From("u/corridor", indices, 40), geometry, Port("start", indices[0]), Port("end", indices[^1]), 1, 1,
            PreparationSummary.Empty, new(1, 0, 0, 0, indices.Length, geometry.Runs.Count - 1, 0), [], [], "fixture/u/materialization", 0));
        context.RefreshConstructionAnalysis();
    }
    [Fact]
    public void UsefulAlternativeReducesAnActualNetworkDetourAndReportsItsLimits()
    {
        var context = Context(); AddNetwork(context, new(5, 5, [new(5, 5, CardinalSide.South, 20), new(5, 25, CardinalSide.East, 20), new(25, 25, CardinalSide.North, 20)]));
        RouteNetworkGenerator.Apply(context, new("alternatives", WorldStepKind.RouteNetwork,
            new RouteNetworkStepParameters(RouteNetworkMode.ExplorationLinks, new(Alternatives: ContentDensity.Low)), ["fixture"]), ContentBundle.Canonical());
        var quality = Assert.Single(context.RouteAlternatives); Assert.True(quality.AddsCycle); Assert.True(quality.NewDistance < quality.PreviousDistance);
        Assert.True(quality.NewCorridorCells >= 6); Assert.False(quality.IndependentSiteEntrance);
        Assert.Equal(2, context.Routes.Count); Assert.Contains(context.Construction, o => o.ObjectId == quality.RouteId);
        _ = GameEngine.CreateGame(context.Materialize());
    }
    [Fact]
    public void OpenTerrainShortcutCanJoinTwoIndividuallyDirectRoutes()
    {
        var context = Context();
        AddNetwork(context, new(5, 5, [new(5, 5, CardinalSide.South, 28)]), "fixture/west");
        AddNetwork(context, new(5, 5, [new(5, 5, CardinalSide.East, 25), new(30, 5, CardinalSide.South, 28)]), "fixture/east");
        RouteNetworkGenerator.Apply(context, new("alternatives", WorldStepKind.RouteNetwork,
            new RouteNetworkStepParameters(RouteNetworkMode.ExplorationLinks, new(Alternatives: ContentDensity.Low)), ["fixture"]), ContentBundle.Canonical());
        var quality = Assert.Single(context.RouteAlternatives);
        Assert.True(quality.AddsCycle); Assert.False(quality.ConnectsRegion);
        Assert.True(quality.PreviousDistance - quality.NewDistance >= 3);
        Assert.True(context.RoutePlanningWork > 0);
    }

    [Fact]
    public void ARouteGraphCutAloneDoesNotProveAUsefulPhysicalBottleneckBypass()
    {
        var context = Context(); AddNetwork(context, new(2, 20, [new(2, 20, CardinalSide.East, 35)]));
        // Analysis candidates are hints. The open surrounding board must still be checked.
        context.CurrentAnalysis = context.CurrentAnalysis! with { Chokepoints = [20 * 40 + 12] };
        RouteNetworkGenerator.Apply(context, new("alternatives", WorldStepKind.RouteNetwork,
            new RouteNetworkStepParameters(RouteNetworkMode.ExplorationLinks, new(Alternatives: ContentDensity.Low)), ["fixture"]), ContentBundle.Canonical());
        Assert.Empty(context.RouteAlternatives);
        Assert.Contains(context.RouteIntents, i => i.Status == RouteIntentStatus.RejectedAlternative);
        Assert.Single(context.Routes);
    }

    [Fact]
    public void StraightOpenRoadDoesNotReceiveAnArtificialLoopToMeetQuota()
    {
        var context = Context(); AddNetwork(context, new(5, 5, [new(5, 5, CardinalSide.East, 25)])); var before = ScenarioDefinitionJson.ToJson(context.Materialize());
        RouteNetworkGenerator.Apply(context, new("alternatives", WorldStepKind.RouteNetwork,
            new RouteNetworkStepParameters(RouteNetworkMode.ExplorationLinks, new(Alternatives: ContentDensity.High)), ["fixture"]), ContentBundle.Canonical());
        Assert.Empty(context.RouteAlternatives); Assert.Single(context.Routes); Assert.Equal(before, ScenarioDefinitionJson.ToJson(context.Materialize()));
    }
    [Fact]
    public void TwoOpportunitiesInOneRegionDoNotProduceTwoClaimsOfNewRegionAccess()
    {
        var context = Context(); var board = context.ConstructionBoard!; var barrier = board.BeginDraft(new(0, 0, 40, 40), "barrier", new());
        foreach (var y in Enumerable.Range(0, 40)) barrier.SetTerrain(y * 40 + 20, "solid-rock");
        Assert.True(board.TryCommit(barrier.BuildPatch(), out _));
        AddNetwork(context, new(5, 5, [new(5, 5, CardinalSide.South, 30)]));
        foreach (var y in new[] { 8, 30 }) context.FeatureOpportunities.Add(new("room-" + y, "clearing", y * 40 + 30, new(25, y, 6, 6), 25, null, null,
            context.CurrentAnalysis!.WalkableLabels[y * 40 + 30], true));
        RouteNetworkGenerator.Apply(context, new("regions", WorldStepKind.RouteNetwork, new RouteNetworkStepParameters(RouteNetworkMode.ExplorationLinks, new(Alternatives: ContentDensity.High)), ["fixture"]), ContentBundle.Canonical());
        Assert.Single(context.RouteAlternatives.Where(q => q.ConnectsRegion));
        Assert.True(context.CurrentAnalysis!.Connectivity.ComponentCount < 2);
    }

    [Fact]
    public void ConstructedCrossingUsesExistingOrdinaryTerrainAndRecordsExactWaterReplacement()
    {
        var context = Context(); var board = context.ConstructionBoard!;
        var water = board.BeginDraft(new(0, 0, 40, 40), "water", new());
        foreach (var y in Enumerable.Range(0, 40)) foreach (var x in Enumerable.Range(18, 3)) water.SetTerrain(y * 40 + x, "water");
        Assert.True(board.TryCommit(water.BuildPatch(), out _));
        var scope = new GenerationScope(22, "crossing", board.BeginDraft(new(0, 0, 40, 40), "crossing", new()));
        var request = new RouteRequest("bridge", scope.Draft.AllowedArea, new("west", [20 * 40 + 3]), new("east", [20 * 40 + 35]), RouteProfileKind.Road,
            Window: context.Window, Requirements: RouteRequirements.For(RouteProfileKind.Road, waterCrossings: true));
        var plan = Assert.IsType<Planned<RoutePlan>>(RouteGenerator.Plan(request, scope)).Plan;
        Assert.NotEmpty(plan.Route.Crossings); Assert.InRange(plan.Route.Preparation.WaterFilled, 6, 24);
        Assert.True(scope.Draft.Accept(plan.Patch, out _)); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out var error), error);
        Assert.All(plan.Route.Crossings.SelectMany(c => c.Footprint.Indices(40)), i => Assert.True(board.Passable(i)));
        _ = GameEngine.CreateGame(board.Export());
    }
}
