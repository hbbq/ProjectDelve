using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SettlementGenerationTests
{
    [Theory]
    [InlineData("grass", 2)]
    [InlineData("tree", 3)]
    [InlineData("solid-rock", 4)]
    [InlineData("water", 3)]
    public void SettlementComposesConnectedBuildingsInAllSupportedNaturalTerrain(string terrain, int buildings)
    {
        var board = CampsiteGenerationTests.Board(terrain, 40, 35); var scope = CampsiteGenerationTests.Scope(board);
        var result = Assert.IsType<Planned<SitePlan>>(SettlementGenerator.Plan(new("village", scope.Draft.AllowedArea,
            new(MaxChangedCells: 1100, MaxCost: 9000), new(buildings, ScenarioRole: "destination")), scope));
        Assert.Equal(buildings, result.Plan.Site.Buildings.Count); Assert.True(result.Plan.Site.Access.InternallyConnected);
        Assert.All(result.Plan.Site.Buildings, b => Assert.InRange(b.Rooms.Count, 1, 3));
        Assert.True(scope.Draft.Accept(result.Plan.Patch, out _)); Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out var error), error);
        Assert.Equal(GeneratedSiteKind.Settlement, result.Plan.Site.Kind); Assert.Single(result.Plan.Site.Roles);
        Assert.Contains(board.Export().Board.Cells, c => c.TerrainTypeId == "gravel"); Assert.NotEmpty(board.Export().Board.Edges);
        Assert.Equal(result.Plan.Site.Buildings.Count, result.Plan.Site.Buildings.Select(b => b.Id).Distinct().Count());
        if (terrain == "water") { Assert.Contains(SiteTag.ArtificialIsland, result.Plan.Site.Tags); Assert.True(result.Plan.Site.Access.RequiresExternalConnection); }
        _ = GameEngine.CreateGame(board.Export());
    }
    [Fact]
    public void FailedParentAfterSuccessfulChildrenDoesNotLeakConstructionOrReservations()
    {
        var board = CampsiteGenerationTests.Board("grass", 40, 35); var scope = CampsiteGenerationTests.Scope(board); var source = ScenarioDefinitionJson.ToJson(board.Export());
        // Bare grass preparation costs zero, but complete buildings exceed this final Site allowance.
        var result = SettlementGenerator.Plan(new("village", scope.Draft.AllowedArea, new(MaxChangedCells: 160, MaxCost: 300)), scope);
        Assert.IsType<Rejected<SitePlan>>(result); Assert.Equal(source, ScenarioDefinitionJson.ToJson(board.Export()));
        Assert.Empty(scope.Draft.BuildPatch().Reservations); Assert.Empty(scope.Draft.BuildPatch().EdgeChanges); Assert.True(scope.Budget.Attempts >= 4);
    }
    [Fact]
    public void SeedsGiveRepeatableDistinctSettlementsAndStableRequestedObjectPaths()
    {
        var forms = new HashSet<string>();
        for (ulong seed = 0; seed < 12; seed++)
        {
            SitePlan Generate() { var board = CampsiteGenerationTests.Board("grass", 40, 35); var scope = CampsiteGenerationTests.Scope(board, seed); return Assert.IsType<Planned<SitePlan>>(SettlementGenerator.Plan(new("village", scope.Draft.AllowedArea, new()), scope)).Plan; }
            var a = Generate(); var b = Generate(); Assert.Equal(JsonSerializer.Serialize(a.Site), JsonSerializer.Serialize(b.Site));
            Assert.Equal(JsonSerializer.Serialize(a.Operations), JsonSerializer.Serialize(b.Operations)); forms.Add(JsonSerializer.Serialize(a.Patch.EdgeChanges));
            Assert.All(a.Site.Buildings, building => Assert.StartsWith("test/village/", building.Id));
        }
        Assert.True(forms.Count >= 10);
    }
}
