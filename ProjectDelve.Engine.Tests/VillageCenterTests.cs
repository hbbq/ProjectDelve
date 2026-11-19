using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class VillageCenterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("gravel-with-well")]
    public void VillageCenterHasRealSquareConnectedBuildingsAndProtectedUsableRing(string? centerpiece)
    {
        var board = CampsiteGenerationTests.Board("grass", 44, 40);
        var scope = CampsiteGenerationTests.Scope(board);
        var plan = Assert.IsType<Planned<SitePlan>>(SettlementGenerator.Plan(new("center", scope.Draft.AllowedArea,
            new(), new() { VillageCenter = true, CenterpieceTypeId = centerpiece }), scope)).Plan;
        Assert.Equal(SiteConstructionProfile.VillageCenter, plan.Site.ConstructionProfile);
        var layout = plan.Site.SettlementLayout!;
        Assert.Equal(SettlementFamily.MarketVillage, layout.Family);
        Assert.True(plan.Site.Buildings.Count >= 3);
        var square = Assert.Single(layout.PublicSpaces);
        Assert.Equal("market-square", square.Role);
        Assert.True(square.Area.Area >= 25);
        Assert.True(scope.Draft.Accept(plan.Patch, out var error), error);
        Assert.True(board.TryCommit(scope.Draft.BuildPatch(), out error), error);
        if (centerpiece is null) Assert.Null(layout.CenterpieceIndex);
        else
        {
            var center = Assert.IsType<int>(layout.CenterpieceIndex);
            Assert.Equal(centerpiece, board.TerrainAt(center));
            Assert.False(board.Passable(center));
            Assert.DoesNotContain(center, layout.Access.Indices(board.Width));
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var cell = center + dy * board.Width + dx;
                Assert.True(board.Passable(cell));
                Assert.Contains(cell, square.Area.Indices(board.Width));
                Assert.Contains(cell, layout.Access.Indices(board.Width));
                Assert.True(board.IsReserved(cell));
            }
            Assert.All(plan.Site.PopulationIntent!.Areas, area =>
                Assert.DoesNotContain(center, area.Area.Indices(board.Width)));
        }
        _ = GameEngine.CreateGame(board.Export());
    }

    [Theory]
    [InlineData(2, null, "gravel-with-well")]
    [InlineData(3, SettlementFamily.Farmstead, "gravel-with-well")]
    [InlineData(3, SettlementFamily.MarketVillage, "tree")]
    [InlineData(3, SettlementFamily.MarketVillage, "stone-floor")]
    [InlineData(3, SettlementFamily.MarketVillage, "missing-well")]
    public void InvalidCenterRequestsNeverMutateParent(int buildings, SettlementFamily? family, string centerpiece)
    {
        var board = CampsiteGenerationTests.Board("grass", 44, 40);
        var before = ScenarioDefinitionJson.ToJson(board.Export());
        var scope = CampsiteGenerationTests.Scope(board);
        Assert.IsType<Rejected<SitePlan>>(SettlementGenerator.Plan(new("center", scope.Draft.AllowedArea,
            new(), new(buildings, Family: family) { VillageCenter = true, CenterpieceTypeId = centerpiece }), scope));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export()));
        Assert.Empty(scope.Draft.BuildPatch().Reservations);
        Assert.Empty(scope.Draft.BuildPatch().TerrainChanges);
    }

    [Fact]
    public void CenterpieceEligibilityIsAuthoredAndValidatedForUnfamiliarContent()
    {
        var assembly = new TerrainAssemblyDefinition("local-center", "Local centerpiece", "gravel")
        { VillageCenterpiece = true, Components = [new("object", "well")] };
        var restored = JsonSerializer.Deserialize<TerrainAssemblyDefinition>(JsonSerializer.Serialize(assembly))!;
        Assert.True(restored.VillageCenterpiece);
        var terrain = TerrainCatalog.Find(TerrainCatalog.Resolve(null, [restored]), assembly.Id);
        Assert.False(terrain.Passable);
        Assert.True(terrain.Assembly!.VillageCenterpiece);
        Assert.Equal("well", Assert.Single(terrain.Assembly!.Components).Definition.Id);
        var scenario = CampsiteGenerationTests.Board("grass", 44, 40).Export() with { TerrainAssemblies = [assembly] };
        var board = new PlanningBoard(scenario, ContentBundle.Canonical());
        var scope = CampsiteGenerationTests.Scope(board);
        var plan = Assert.IsType<Planned<SitePlan>>(SettlementGenerator.Plan(new("local-center", scope.Draft.AllowedArea,
            new(), new() { VillageCenter = true, CenterpieceTypeId = assembly.Id }), scope)).Plan;
        Assert.Contains(plan.Patch.TerrainChanges, change => change.After == assembly.Id);
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve(null,
            [assembly with { Components = [], Id = "empty-center" }]));
        Assert.Throws<ArgumentException>(() => TerrainCatalog.Resolve(null,
            [assembly with { SurfaceId = "wet-grass", Id = "wet-center" }]));
        Assert.DoesNotContain("villageCenterpiece", JsonSerializer.Serialize(new TerrainAssemblyDefinition("plain", "Plain", "gravel")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACurrentComponentOverridePreservesEligibilityWhenTheAssemblyIsRecomposed()
    {
        var canonical = ContentBundle.Canonical();
        var well = canonical.FixedComponents.Single(c => c.Id == "well");
        var overrides = new ContentBundle { FixedComponents = [well with { Name = "Refreshed well" }] };
        var effective = ContentComposition.ComposeScopes(canonical, overrides).Definitions;
        var terrain = TerrainCatalog.Find(effective.Terrain, "gravel-with-well");
        Assert.True(SettlementGenerator.IsVillageCenterpiece(terrain));
        Assert.Equal("Refreshed well", Assert.Single(terrain.Assembly!.Components).Definition.Name);
    }
}
