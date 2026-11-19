using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class PrimitiveCampsiteTests
{
    [Theory]
    [InlineData(SiteConstructionProfile.OpenCamp)]
    [InlineData(SiteConstructionProfile.ShelteredCamp)]
    [InlineData(SiteConstructionProfile.PrimitiveHut)]
    public void ProfilesProducePhysicalConnectedGeometryWithTruthfulMetadata(SiteConstructionProfile profile)
    {
        foreach (var radius in new[] { 2, 3 })
        foreach (var facing in Enum.GetValues<CardinalSide>())
        {
            var board = CampsiteGenerationTests.Board();
            var scope = CampsiteGenerationTests.Scope(board);
            var result = CampsiteGenerator.Plan(new("camp", scope.Draft.AllowedArea, new(),
                new(Radius: radius) { ConstructionProfile = profile }, new(PreferredApproach: facing)), scope);
            var plan = Assert.IsType<Planned<SitePlan>>(result).Plan;
            Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
            Assert.True(scope.Draft.Accept(plan.Patch, out var error), error);
            Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
            Assert.Equal(profile, plan.Site.ConstructionProfile);
            Assert.True(plan.Site.Access.InternallyConnected);
            Assert.Equal(plan.Site.Access.UsableCells, plan.Site.Access.ConnectedCells);
            Assert.NotEmpty(plan.Site.AccessPorts);
            Assert.NotNull(plan.Site.PopulationIntent);
            var cells = plan.Site.Footprint.Indices(board.Width).ToHashSet();
            Assert.Single(cells.Where(i => board.TerrainAt(i) == "campfire"));
            Assert.DoesNotContain(cells, i => board.TerrainAt(i) is "stone-floor" or "stone-floor-with-table");
            Assert.All(cells, i => Assert.True(board.IsReserved(i)));
            var reached = SiteGeometry.Flood(scope.Draft, plan.Site.Entrances[0].OutsideIndex, cells);
            Assert.NotNull(reached);
            Assert.All(cells.Where(board.Passable), i => Assert.Contains(i, reached));
            if (profile == SiteConstructionProfile.PrimitiveHut)
            {
                var hut = Assert.Single(plan.Site.Buildings);
                Assert.Equal(6, Assert.Single(hut.Rooms).Interior.Area);
                var door = Assert.Single(hut.Portals);
                Assert.Equal(EdgeKind.ClosedDoor, board.EdgeAt(door.Edge));
                Assert.True(board.Passable(door.InsideIndex));
                Assert.True(board.Passable(door.OutsideIndex));
                Assert.All(hut.Footprint.Indices(board.Width), i => Assert.False(plan.Site.Exterior.Contains(i, board.Width)));
            }
            else
            {
                Assert.Empty(plan.Site.Buildings);
                if (profile == SiteConstructionProfile.ShelteredCamp)
                    Assert.Equal(7, plan.Patch.EdgeChanges.Count(e => e.After == EdgeKind.Wall));
                else Assert.Empty(plan.Patch.EdgeChanges);
            }
            _ = GameEngine.CreateGame(board.Export());
        }
    }

    [Theory]
    [InlineData(SiteConstructionProfile.OpenCamp)]
    [InlineData(SiteConstructionProfile.ShelteredCamp)]
    [InlineData(SiteConstructionProfile.PrimitiveHut)]
    public void ProhibitedPreparationLeavesParentAndBoardUntouched(SiteConstructionProfile profile)
    {
        var board = CampsiteGenerationTests.Board("tree");
        var scope = CampsiteGenerationTests.Scope(board);
        var before = ScenarioDefinitionJson.ToJson(board.Export());
        Assert.IsType<Rejected<SitePlan>>(CampsiteGenerator.Plan(new("camp", scope.Draft.AllowedArea,
            new(ClearTrees: false), new() { ConstructionProfile = profile }), scope));
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
        Assert.Empty(scope.Draft.BuildPatch().EdgeChanges);
        Assert.Empty(scope.Draft.BuildPatch().Reservations);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export()));
    }

    [Fact]
    public void ProfiledHutPlanIsDeterministic()
    {
        SitePlan Generate()
        {
            var scope = CampsiteGenerationTests.Scope(CampsiteGenerationTests.Board("tree"));
            return Assert.IsType<Planned<SitePlan>>(CampsiteGenerator.Plan(new("camp", scope.Draft.AllowedArea,
                new(), new() { ConstructionProfile = SiteConstructionProfile.PrimitiveHut }), scope)).Plan;
        }
        var first = Generate(); var second = Generate();
        Assert.Equal(JsonSerializer.Serialize(first.Site), JsonSerializer.Serialize(second.Site));
        Assert.Equal(JsonSerializer.Serialize(first.Operations), JsonSerializer.Serialize(second.Operations));
    }
}
