using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class CampsiteGenerationTests
{
    internal static PlanningBoard Board(string terrain = "grass", int width = 30, int height = 25) => new(new ScenarioDefinition
    {
        Board = new() { Width = width, Height = height, DefaultTerrain = TerrainKind.StoneFloor, DefaultTerrainTypeId = terrain },
        Units = [], UnitTypeIds = [], Agency = [], Loot = LootSettings.Empty
    }, ContentBundle.Canonical());
    internal static GenerationScope Scope(PlanningBoard board, ulong seed = 42, PlanningBudget? budget = null) =>
        new(seed, "test", board.BeginDraft(new(0, 0, board.Width, board.Height), "test", budget ?? new(256, 500000)));
    [Theory]
    [InlineData("grass")]
    [InlineData("tree")]
    [InlineData("solid-rock")]
    [InlineData("water")]
    public void CampsitePreparesVariedTerrainAndReturnsOnlyAPrivateCompletePlan(string ground)
    {
        var board = Board(ground); var scope = Scope(board);
        var result = Assert.IsType<Planned<SitePlan>>(CampsiteGenerator.Plan(new("camp", scope.Draft.AllowedArea, new(),
            new(ScenarioRole: "rest-site")), scope));
        Assert.Equal(ground, board.TerrainAt(0)); Assert.Equal(0, board.Revision);
        Assert.True(scope.Draft.Accept(result.Plan.Patch, out _)); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out var error), error);
        var site = result.Plan.Site; Assert.True(site.Access.InternallyConnected); Assert.True(site.Exterior.Area >= 18);
        Assert.Equal("test/camp", site.Id); Assert.Equal(site.Id, Assert.Single(site.Roles).ObjectId);
        Assert.Equal(1, board.Export().Board.Cells.Count(c => c.TerrainTypeId == "campfire"));
        Assert.All(site.Entrances, e => { Assert.True(board.Passable(e.InsideIndex)); Assert.True(board.Passable(e.OutsideIndex)); });
        if (ground == "water") { Assert.True(site.Preparation.WaterFilled > 0); Assert.True(site.Access.RequiresExternalConnection); Assert.Contains(SiteTag.ArtificialIsland, site.Tags); }
        _ = GameEngine.CreateGame(board.Export());
    }
    [Fact]
    public void ProhibitedPreparationAndImpossibleConstraintsLeaveEverythingUntouched()
    {
        var board = Board("tree"); var before = ScenarioDefinitionJson.ToJson(board.Export()); var scope = Scope(board);
        Assert.IsType<Rejected<SitePlan>>(CampsiteGenerator.Plan(new("camp", scope.Draft.AllowedArea, new(ClearTrees: false)), scope));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export())); Assert.Empty(scope.Draft.BuildPatch().Reservations);
        var attempts = scope.Budget.Attempts;
        Assert.IsType<Rejected<SitePlan>>(CampsiteGenerator.Plan(new("tiny", new(0, 0, 2, 2), new()), scope)); Assert.Equal(attempts, scope.Budget.Attempts);
    }
    [Fact]
    public void NestedSeedPathsReproducePlansAndBudgetExhaustionIsStructured()
    {
        var a = Board(); var b = Board();
        var ra = Assert.IsType<Planned<SitePlan>>(CampsiteGenerator.Plan(new("camp", new(0, 0, 30, 25), new()), Scope(a)));
        var rb = Assert.IsType<Planned<SitePlan>>(CampsiteGenerator.Plan(new("camp", new(0, 0, 30, 25), new()), Scope(b)));
        Assert.Equal(JsonSerializer.Serialize(ra.Plan.Site), JsonSerializer.Serialize(rb.Plan.Site));
        Assert.Equal(JsonSerializer.Serialize(ra.Plan.Operations), JsonSerializer.Serialize(rb.Plan.Operations));
        var failed = Assert.IsType<Rejected<SitePlan>>(CampsiteGenerator.Plan(new("camp", new(0, 0, 30, 25), new()), Scope(a, budget: new(0, 0))));
        Assert.Equal(PlacementFailureCode.BudgetExhausted, failed.Failure.Code);
    }
    [Fact]
    public void WorkBudgetExhaustionStopsImmediatelyWithItsOwnFailureCode()
    {
        var scope = Scope(Board(), budget: new(128, 0));
        var result = Assert.IsType<Rejected<SitePlan>>(CampsiteGenerator.Plan(new("camp", scope.Draft.AllowedArea, new()), scope));
        Assert.Equal(PlacementFailureCode.BudgetExhausted, result.Failure.Code); Assert.Equal(1, scope.Budget.Attempts);
    }

    [Fact]
    public void OperationTraceIncludesRestorationToTheDraftBaseline()
    {
        var scope = Scope(Board()); var d = scope.Draft;
        d.SetTerrain(0, "dirt"); d.SetEdge(new(0, EdgeDirection.Right), EdgeKind.Wall); var before = d.BuildPatch();
        d.ReplaceTerrain(0, "grass"); d.ReplaceEdge(new(0, EdgeDirection.Right), EdgeKind.None);
        var op = SiteGeometry.Operation(d, before, "restore", null, "Restore", "test", d.AllowedArea);
        Assert.Equal(new TerrainChange(0, "dirt", "grass"), Assert.Single(op.TerrainChanges));
        Assert.Equal(EdgeKind.None, Assert.Single(op.EdgeChanges).After);
    }
}
