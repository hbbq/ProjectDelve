using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RouteGeneratorTests
{
    private static ScenarioDefinition Scenario(int width, int height, TerrainKind terrain = TerrainKind.Grass) => new()
    {
        Board = new() { Width = width, Height = height, DefaultTerrain = terrain },
        UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
    };
    private static (PlanningBoard Board, BoardDraft Draft, GenerationScope Scope) Setup(ScenarioDefinition scenario,
        ulong seed = 42, PlanningBudget? budget = null, IReadOnlySet<int>? protectedCells = null)
    {
        var board = new PlanningBoard(scenario, ContentBundle.Canonical(), protectedCells);
        var draft = board.BeginDraft(new(0, 0, board.Width, board.Height), "parent", budget ?? new(32, 1000000));
        return (board, draft, new(seed, "routes", draft));
    }
    private static RouteRequest Request(int width, int height, int start, int end, RouteProfileKind profile = RouteProfileKind.MinorPath,
        RouteRequirements? requirements = null) => new("test", new(0, 0, width, height), new("start", [start]), new("end", [end]),
            profile, Requirements: requirements);
    private static RoutePlan Planned(PlacementResult<RoutePlan> result) => Assert.IsType<Planned<RoutePlan>>(result).Plan;

    [Fact]
    public void DeterministicPrivatePatchHasCompressedWorldGeometryAndActualPorts()
    {
        var a = Setup(Scenario(24, 16)); var b = Setup(Scenario(24, 16));
        var request = Request(24, 16, 24 * 7, 24 * 10 + 23) with { Window = new(80, 120, 24, 16) };
        var first = Planned(RouteGenerator.Plan(request, a.Scope)); var second = Planned(RouteGenerator.Plan(request, b.Scope));
        Assert.Equal(JsonSerializer.Serialize(first.Route), JsonSerializer.Serialize(second.Route));
        Assert.Empty(a.Board.Export().Board.Cells); Assert.False(a.Board.IsRouteCorridor(first.Route.Start.AnchorIndex));
        var path = first.Route.Geometry.LocalIndices(24, request.Window).ToArray();
        Assert.Equal(24 * 7, path[0]); Assert.Equal(24 * 10 + 23, path[^1]);
        Assert.Equal(first.Route.Stats.Length, path.Length); Assert.True(first.Route.Geometry.Runs.Count < path.Length);
        Assert.Equal(80, first.Route.Start.WorldX); Assert.Equal(127, first.Route.Start.WorldY);
        Assert.True(a.Draft.Accept(first.Patch, out var error), error);
        Assert.True(a.Board.TryCommit(a.Draft.BuildPatch(), out error), error);
        Assert.True(a.Board.IsRouteCorridor(path[0]));
    }

    [Fact]
    public void ProfilesHaveConcreteDifferentPreferencesAndForestVariationIsSeeded()
    {
        Assert.Equal(1, RouteRequirements.For(RouteProfileKind.ForestTrail).Width);
        Assert.Equal(2, RouteRequirements.For(RouteProfileKind.Road).Width);
        Assert.True(RoutePreferences.For(RouteProfileKind.ForestTrail).VariationWeight > RoutePreferences.For(RouteProfileKind.Road).VariationWeight);
        Assert.True(RoutePreferences.For(RouteProfileKind.Road).TurnPenalty > RoutePreferences.For(RouteProfileKind.ForestTrail).TurnPenalty);
        HashSet<string> geometries = [];
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var setup = Setup(Scenario(32, 24), seed);
            var plan = Planned(RouteGenerator.Plan(Request(32, 24, 32 * 12, 32 * 12 + 31, RouteProfileKind.ForestTrail), setup.Scope));
            geometries.Add(JsonSerializer.Serialize(plan.Route.Geometry));
        }
        Assert.True(geometries.Count > 1);
    }

    [Fact]
    public void TrailGuideDistributesBendsWhileRoadRemainsDirectOnOpenGround()
    {
        var trail = Planned(RouteGenerator.Plan(Request(80, 60, 30 * 80, 30 * 80 + 79, RouteProfileKind.ForestTrail), Setup(Scenario(80, 60)).Scope));
        var road = Planned(RouteGenerator.Plan(Request(80, 60, 30 * 80, 30 * 80 + 79, RouteProfileKind.Road,
            RouteRequirements.For(RouteProfileKind.Road) with { Width = 1 }), Setup(Scenario(80, 60)).Scope));
        Assert.True(trail.Route.Geometry.Runs.Max(r => r.Length) < road.Route.Geometry.Runs.Max(r => r.Length));
        Assert.True(trail.Route.Stats.Turns > road.Route.Stats.Turns);
        Assert.InRange(trail.Route.Stats.Length, 80, 120);
        var chordRequest = Request(80, 60, 30 * 80, 30 * 80 + 79, RouteProfileKind.ForestTrail);
        chordRequest = chordRequest with { Start = chordRequest.Start with { Kind = RouteEndpointKind.Network },
            Destination = chordRequest.Destination with { Kind = RouteEndpointKind.Network } };
        var chord = Planned(RouteGenerator.Plan(chordRequest, Setup(Scenario(80, 60)).Scope));
        Assert.Equal(JsonSerializer.Serialize(trail.Route.Geometry), JsonSerializer.Serialize(chord.Route.Geometry));
    }

    [Fact]
    public void CropBoundaryIsUsedForPortsRatherThanAnUnnecessaryLongEdgeCorridor()
    {
        var plan = Planned(RouteGenerator.Plan(Request(60, 40, 60, 119), Setup(Scenario(60, 40)).Scope));
        var cells = plan.Route.Geometry.LocalIndices(60).ToArray();
        Assert.True(cells.Count(i => i / 60 >= 4) > cells.Length / 2);
        Assert.InRange(cells.Length, 60, 85);
    }

    [Fact]
    public void ExistingDistantTrunkDoesNotBuyAnExcessivePrimaryRouteDetour()
    {
        var setup = Setup(Scenario(80, 60)); setup.Draft.EnableRoutePlanning();
        var old = Enumerable.Range(3, 53).Select(y => y * 80 + 4).Concat(Enumerable.Range(4, 72).Select(x => 3 * 80 + x)).ToArray();
        setup.Draft.ReserveRoute(old, "existing/trunk");
        var plan = Planned(RouteGenerator.Plan(Request(80, 60, 40 * 80, 40 * 80 + 79, RouteProfileKind.Road,
            RouteRequirements.For(RouteProfileKind.Road) with { Width = 1 }) with { Purpose = RoutePurpose.ThroughRoute }, setup.Scope));
        Assert.InRange(plan.Route.Stats.Length, 80, 110);
    }

    [Fact]
    public void RockExcavationReportsOnlyActualPreparedMouthTransitions()
    {
        var scenario = Scenario(7, 1);
        scenario.Board.Cells.AddRange(Enumerable.Range(2, 3).Select(x => new CellDefinition(new(x, 0), TerrainKind.SolidRock)));
        var setup = Setup(scenario); var plan = Planned(RouteGenerator.Plan(Request(7, 1, 0, 6, RouteProfileKind.RockPassage), setup.Scope));
        Assert.Equal(3, plan.Route.Preparation.RockExcavated); Assert.Equal(2, plan.Route.TunnelEntrances.Count);
        Assert.All(plan.Route.TunnelEntrances, mouth => Assert.DoesNotContain(plan.Patch.EdgeChanges, e => e.Key == mouth.Edge && !e.After.Passable()));
        Assert.Equal("solid-rock", setup.Board.TerrainAt(3));
        Assert.True(setup.Draft.Accept(plan.Patch, out _)); Assert.True(setup.Board.TryCommit(setup.Draft.BuildPatch(), out _));
        Assert.True(setup.Board.Passable(3));
    }

    [Fact]
    public void WaterRemainsUntilExplicitBoundedCrossingIsAuthorized()
    {
        var scenario = Scenario(5, 1); scenario.Board.Cells.Add(new(new(2, 0), TerrainKind.Water)); var setup = Setup(scenario);
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(Request(5, 1, 0, 4), setup.Scope));
        Assert.Equal("water", setup.Board.TerrainAt(2)); Assert.False(setup.Draft.IsRouteCorridor(2));
        var enabled = RouteRequirements.For(RouteProfileKind.MinorPath, waterCrossings: true) with { MaxWaterCells = 1, MaxWaterRun = 1 };
        var plan = Planned(RouteGenerator.Plan(Request(5, 1, 0, 4, requirements: enabled), setup.Scope));
        Assert.Equal(1, plan.Route.Preparation.WaterFilled); Assert.Equal(1, Assert.Single(plan.Route.Crossings).Footprint.Area);
        Assert.True(setup.Draft.Accept(plan.Patch, out _)); Assert.True(setup.Board.TryCommit(setup.Draft.BuildPatch(), out _));
        Assert.True(setup.Board.Passable(2));
    }

    [Fact]
    public void FullWidthBendsGuaranteeContinuousTwoByTwoPlacements()
    {
        var setup = Setup(Scenario(8, 8));
        var r = RouteRequirements.For(RouteProfileKind.Road) with { RequiredFootprint = 2, AvoidCells = [27, 28, 35, 36] };
        var plan = Planned(RouteGenerator.Plan(Request(8, 8, 0, 54, RouteProfileKind.Road, r), setup.Scope));
        Assert.Equal(2, plan.Route.VerifiedFootprint); Assert.Equal(2, plan.Route.RealizedWidth); Assert.True(plan.Route.Stats.Turns > 0);
        Assert.True(setup.Draft.Accept(plan.Patch, out _)); Assert.True(setup.Board.TryCommit(setup.Draft.BuildPatch(), out _));
        foreach (var a in plan.Route.Geometry.LocalIndices(8))
        {
            Assert.All(new[] { a, a + 1, a + 8, a + 9 }, i =>
            { Assert.True(plan.Route.Corridor.Contains(i, 8)); Assert.True(setup.Board.Passable(i)); Assert.DoesNotContain(i, r.AvoidCells!); });
            Assert.True(setup.Board.EdgeAt(new(a, EdgeDirection.Right)).Passable());
            Assert.True(setup.Board.EdgeAt(new(a, EdgeDirection.Down)).Passable());
            Assert.True(setup.Board.EdgeAt(new(a + 1, EdgeDirection.Down)).Passable());
            Assert.True(setup.Board.EdgeAt(new(a + 8, EdgeDirection.Right)).Passable());
        }
    }

    [Fact]
    public void WideCorridorCannotIgnoreInternalBlockingEdges()
    {
        var scenario = Scenario(5, 2); scenario.Board.Edges.Add(new(new(2, 0), EdgeDirection.Down, EdgeKind.Wall));
        var setup = Setup(scenario); var r = RouteRequirements.For(RouteProfileKind.Road) with { RequiredFootprint = 2 };
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(Request(5, 2, 0, 3, RouteProfileKind.Road, r), setup.Scope));
        r = r with { Preparation = r.Preparation! with { ReplaceExistingEdges = true } };
        var plan = Planned(RouteGenerator.Plan(Request(5, 2, 0, 3, RouteProfileKind.Road, r), setup.Scope));
        Assert.Contains(plan.Patch.EdgeChanges, e => e.Key == new EdgeKey(2, EdgeDirection.Down) && e.After == EdgeKind.None);
    }

    [Fact]
    public void ForeignSiteTraversalAndOpeningRequirePublishedExactPermissions()
    {
        var scenario = Scenario(5, 1); var key = new EdgeKey(1, EdgeDirection.Right);
        scenario.Board.Edges.Add(new(new(1, 0), EdgeDirection.Right, EdgeKind.Wall));
        var setup = Setup(scenario); var site = setup.Draft.Fork(new(0, 0, 5, 1), "site"); site.Reserve([2]); Assert.True(site.CommitToParent(out _));
        var r = RouteRequirements.For(RouteProfileKind.MinorPath) with { Preparation = new(ReplaceExistingEdges: true) };
        var request = Request(5, 1, 0, 4, requirements: r);
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(request, setup.Scope));
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(request with { AccessGrants = [new("site", [2], [], [])] }, setup.Scope));
        var plan = Planned(RouteGenerator.Plan(request with { AccessGrants = [new("site", [2], [], [key])] }, setup.Scope));
        Assert.Single(plan.Patch.EdgeChanges); Assert.DoesNotContain(plan.Patch.TerrainChanges, c => c.Index == 2);
        Assert.True(setup.Draft.Accept(plan.Patch, out _)); Assert.False(setup.Draft.IsReserved(2, "site"));
    }

    [Fact]
    public void ClosedDoorRequiresExplicitTraversalOrReplacement()
    {
        var scenario = Scenario(4, 1); scenario.Board.Edges.Add(new(new(1, 0), EdgeDirection.Right, EdgeKind.ClosedDoor));
        var setup = Setup(scenario); var request = Request(4, 1, 0, 3);
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(request, setup.Scope));
        var plan = Planned(RouteGenerator.Plan(request with { Requirements = new(ClosedDoorsTraversable: true) }, setup.Scope));
        Assert.True(plan.Route.RequiresOpeningDoors);
        Assert.Empty(plan.Patch.EdgeChanges); Assert.Equal(EdgeKind.ClosedDoor, setup.Draft.EdgeAt(new(1, EdgeDirection.Right)));
    }

    [Fact]
    public void SharingPreservesExistingCorridorTerrainAndFailedParentLeaksNothing()
    {
        var setup = Setup(Scenario(8, 3)); var request = Request(8, 3, 8, 15) with { Preferences = new(VariationWeight: 0) };
        var first = Planned(RouteGenerator.Plan(request, setup.Scope)); Assert.True(setup.Draft.Accept(first.Patch, out _));
        var second = Planned(RouteGenerator.Plan(request with { Key = "second" }, setup.Scope));
        Assert.Equal(first.Route.Corridor.Area, second.Route.Stats.ReusedCells); Assert.Empty(second.Patch.TerrainChanges);
        Assert.True(setup.Draft.Accept(second.Patch, out _));
        Assert.Equal(new[] { "routes/second", "routes/test" }, setup.Draft.RouteMemberships(8));
        Assert.Empty(setup.Board.RouteMemberships(8)); Assert.Equal("grass", setup.Board.TerrainAt(8));
        var stale = setup.Draft.Fork(new(0, 0, 8, 3), "stale"); setup.Draft.SetTerrain(0, "tree");
        Assert.False(stale.CommitToParent(out _)); Assert.Empty(setup.Board.RouteMemberships(8));
    }

    [Fact]
    public void ProtectedAndUnknownFixedGeometryAreHardObstacles()
    {
        var scenario = Scenario(5, 1); scenario.Board.Cells.Add(new(new(2, 0), TerrainKind.StoneFloorWithTable));
        var fixedSetup = Setup(scenario);
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(Request(5, 1, 0, 4, RouteProfileKind.Road,
            RouteRequirements.For(RouteProfileKind.Road) with { Width = 1 }), fixedSetup.Scope));
        var protectedSetup = Setup(Scenario(5, 1), protectedCells: new HashSet<int> { 2 });
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(Request(5, 1, 0, 4), protectedSetup.Scope));
        Assert.False(protectedSetup.Board.IsRouteCorridor(0));
    }

    [Fact]
    public void HardSearchAndPreparationBudgetsReturnTypedFailuresWithoutLeaking()
    {
        var setup = Setup(Scenario(20, 20)); var request = Request(20, 20, 0, 399, requirements: new(MaxExpandedStates: 1));
        var failure = Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(request, setup.Scope)).Failure;
        Assert.Equal(PlacementFailureCode.BudgetExhausted, failure.Code); Assert.False(setup.Draft.IsRouteCorridor(0));
        var limited = request with { Requirements = new(Preparation: new(MaxChangedCells: 0), MaxAttempts: 1) };
        Assert.Equal(PlacementFailureCode.PreparationLimit, Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(limited, setup.Scope)).Failure.Code);
        Assert.Empty(setup.Board.Export().Board.Cells);
        var exhausted = Setup(Scenario(3, 1), budget: new(0, 100));
        Assert.Equal(PlacementFailureCode.BudgetExhausted, Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(Request(3, 1, 0, 2), exhausted.Scope)).Failure.Code);
    }

    [Fact]
    public void LargeWindowUsesBoundedIndexedSearch()
    {
        var setup = Setup(Scenario(120, 120));
        var plan = Planned(RouteGenerator.Plan(Request(120, 120, 0, 119 * 120 + 119, RouteProfileKind.Road,
            RouteRequirements.For(RouteProfileKind.Road) with { Width = 1, MaxExpandedStates = 60000 }), setup.Scope));
        Assert.InRange(plan.Route.Stats.ExpandedStates, 1, 60000); Assert.InRange(plan.Route.Stats.Length, 239, 1000);
        Assert.InRange(plan.Route.Stats.QueuePeak, 1, 120 * 120 * 8);
    }

    [Fact]
    public void WideWaterAllowanceCountsTheEntireCorridorAndOverlappingWaterRun()
    {
        var scenario = Scenario(5, 2);
        scenario.Board.Cells.Add(new(new(2, 0), TerrainKind.Water)); scenario.Board.Cells.Add(new(new(2, 1), TerrainKind.Water));
        var setup = Setup(scenario);
        var r = RouteRequirements.For(RouteProfileKind.Road, waterCrossings: true) with
            { RequiredFootprint = 2, MaxWaterCells = 1, MaxWaterRun = 2, MaxAttempts = 1 };
        Assert.Equal(PlacementFailureCode.PreparationLimit, Assert.IsType<Rejected<RoutePlan>>(
            RouteGenerator.Plan(Request(5, 2, 0, 3, RouteProfileKind.Road, r), setup.Scope)).Failure.Code);
        r = r with { MaxWaterCells = 2, MaxWaterRun = 1 };
        Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(Request(5, 2, 0, 3, RouteProfileKind.Road, r), setup.Scope));
        r = r with { MaxWaterRun = 2 };
        var plan = Planned(RouteGenerator.Plan(Request(5, 2, 0, 3, RouteProfileKind.Road, r), setup.Scope));
        Assert.Equal(2, plan.Route.Preparation.WaterFilled); Assert.Equal(2, Assert.Single(plan.Route.Crossings).Footprint.Area);
    }

    [Fact]
    public void ExistingRailAndInvalidExplicitSurfacesArePreservedAtTheTypedBoundary()
    {
        var scenario = Scenario(5, 1);
        scenario.Board.Cells.Add(new(new(2, 0), TerrainKind.Rail, RailDirection.East));
        var setup = Setup(scenario); var request = Request(5, 1, 0, 4);
        var beforeAttempts = setup.Scope.Budget.Attempts;
        Assert.Equal(PlacementFailureCode.InvalidContent, Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(request with
            { Requirements = new(SurfaceTypeId: "missing-surface") }, setup.Scope)).Failure.Code);
        Assert.Equal(beforeAttempts, setup.Scope.Budget.Attempts);
        Assert.Equal(PlacementFailureCode.InvalidContent, Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(request with
            { Requirements = new(SurfaceTypeId: "rail") }, setup.Scope)).Failure.Code);
        Assert.Equal(beforeAttempts, setup.Scope.Budget.Attempts);
        var plan = Planned(RouteGenerator.Plan(request, setup.Scope));
        Assert.DoesNotContain(plan.Patch.TerrainChanges, c => c.Index == 2);
        Assert.True(setup.Draft.Accept(plan.Patch, out _)); Assert.True(setup.Board.TryCommit(setup.Draft.BuildPatch(), out _));
        Assert.Equal("rail", setup.Board.TerrainAt(2));
        Assert.Equal(RailDirection.East, Assert.Single(setup.Board.Export().Board.Cells.Where(c => c.Position.X == 2)).Direction);
    }

    [Theory]
    [InlineData(int.MaxValue, 0, 5, 3)]
    [InlineData(0, int.MaxValue, 5, 3)]
    [InlineData(0, 0, 4, 3)]
    [InlineData(0, 0, 5, 2)]
    [InlineData(0, 0, 121, 3)]
    public void InvalidWorldWindowsFailBeforeConsumingAnAttempt(int x, int y, int width, int height)
    {
        var setup = Setup(Scenario(5, 3));
        var request = Request(5, 3, 0, 14) with { Window = new(x, y, width, height) };
        var failure = Assert.IsType<Rejected<RoutePlan>>(RouteGenerator.Plan(request, setup.Scope)).Failure;
        Assert.Equal(PlacementFailureCode.InvalidRequest, failure.Code); Assert.Equal(0, setup.Scope.Budget.Attempts);
        Assert.Empty(setup.Draft.RouteMemberships(0));
    }

    [Theory]
    [InlineData(int.MaxValue - 4, int.MaxValue - 2)]
    [InlineData(int.MinValue, int.MinValue)]
    [InlineData(-100, -200)]
    public void ValidExtremeWorldWindowsRoundTripCompressedCoordinates(int x, int y)
    {
        var setup = Setup(Scenario(5, 3)); var window = new WorldWindow(x, y, 5, 3);
        var plan = Planned(RouteGenerator.Plan(Request(5, 3, 0, 14) with { Window = window }, setup.Scope));
        var path = plan.Route.Geometry.LocalIndices(5, window).ToArray();
        Assert.Equal(0, path[0]); Assert.Equal(14, path[^1]);
        Assert.Equal(x, plan.Route.Start.WorldX); Assert.Equal(y, plan.Route.Start.WorldY);
        Assert.Equal(checked(x + 4), plan.Route.End.WorldX); Assert.Equal(checked(y + 2), plan.Route.End.WorldY);
        Assert.All(path, index => Assert.InRange(index, 0, 14));
        Assert.All(path, index => Assert.True(plan.Route.Corridor.Contains(index, 5)));
    }

    [Theory]
    [InlineData(0, 4, CardinalSide.South, -10, -6)]
    [InlineData(4, 0, CardinalSide.North, -6, -10)]
    public void SingleColumnGeometryPreservesActualNorthSouthWorldDirections(int start, int end, CardinalSide side, int startY, int endY)
    {
        var setup = Setup(Scenario(1, 5)); var window = new WorldWindow(100, -10, 1, 5);
        var plan = Planned(RouteGenerator.Plan(Request(1, 5, start, end) with { Window = window }, setup.Scope));
        var run = Assert.Single(plan.Route.Geometry.Runs);
        Assert.Equal(side, run.Direction); Assert.Equal(4, run.Length); Assert.Equal(100, run.WorldX); Assert.Equal(startY, run.WorldY);
        Assert.Equal(100, plan.Route.End.WorldX); Assert.Equal(endY, plan.Route.End.WorldY);
        Assert.Equal(startY, plan.Route.Start.WorldY);
        Assert.Equal(Enumerable.Range(0, 5).Select(i => start < end ? i : 4 - i), plan.Route.Geometry.LocalIndices(1, window));
    }
}
