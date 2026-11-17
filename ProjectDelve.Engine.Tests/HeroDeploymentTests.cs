using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class HeroDeploymentTests
{
    private static PlanningBoard Board(int width = 6, int height = 6, TerrainKind terrain = TerrainKind.Grass)
        => new(new ScenarioDefinition
        {
            Board = new() { Width = width, Height = height, DefaultTerrain = terrain },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        }, ContentBundle.Canonical());
    private static HeroDeploymentResult Establish(PlanningBoard board, int capacity = 5, ulong seed = 42, int work = 2_000_000)
        => HeroDeploymentPlanner.Establish(board, new(capacity, work), seed,
            HeroDeploymentPlanner.NaturalConversions(board, Enumerable.Range(0, board.Width * board.Height)));

    [Theory]
    [InlineData(TerrainKind.Tree, "grass")]
    [InlineData(TerrainKind.SolidRock, "stone-floor")]
    [InlineData(TerrainKind.Water, "stepping-stones")]
    public void ConstructsAndProtectsDeploymentOnFullyBlockedNaturalTerrain(TerrainKind terrain, string surface)
    {
        var board = Board(terrain: terrain); var result = Establish(board);
        Assert.Equal(HeroDeploymentStatus.Established, result.Status);
        var deployment = Assert.IsType<HeroDeployment>(result.Deployment);
        Assert.Equal(5, deployment.Capacity); Assert.Equal(13, deployment.Area.Area);
        Assert.True(deployment.ManeuverCore.Area >= 8);
        var area = deployment.Area.Indices(board.Width).ToHashSet();
        Assert.DoesNotContain(deployment.DepartureIndex, area); Assert.Contains(deployment.DepartureFromIndex, area);
        Assert.Equal(area.Count + 1, result.Preparation.Length);
        Assert.All(area.Append(deployment.DepartureIndex), i =>
        { Assert.Equal(surface, board.TerrainAt(i)); Assert.True(board.IsReserved(i)); });
        var state = GameEngine.CreateGame(board.Export(), board.Content);
        Assert.Empty(state.Units); Assert.Null(HeroDeploymentPlanner.Validate(state, deployment));
        var foreign = board.BeginDraft(new(0, 0, board.Width, board.Height), "later-site", new());
        Assert.Throws<ArgumentException>(() => foreign.ReplaceTerrain(area.Min(), "solid-rock"));
        Assert.Throws<ArgumentException>(() => foreign.SetEdge(EdgeKey.Between(deployment.DepartureFromIndex, deployment.DepartureIndex, board.Width), EdgeKind.Wall));
        Assert.Throws<ArgumentException>(() => foreign.PlaceMachine(new("intruder", new(area.Min() % board.Width, area.Min() / board.Width), MachineTypes.DiamondPress)));
        Assert.Equal(deployment.Revision, board.Revision);
    }

    [Fact]
    public void PrefersExistingGroundAndAllowsNonRectangularArea()
    {
        var board = Board(); var result = Establish(board);
        Assert.Equal(HeroDeploymentStatus.Established, result.Status); Assert.Empty(result.Preparation);
        var deployment = result.Deployment!;
        Assert.Equal(13, deployment.Area.Area);
        Assert.True(deployment.Area.Bounds.Width * deployment.Area.Bounds.Height > deployment.Area.Area);
        Assert.All(deployment.PlacementOrder.Take(5), i => Assert.True(deployment.ManeuverCore.Contains(i, board.Width)));
    }

    [Theory]
    [InlineData(8)] [InlineData(12)]
    public void LargerPartiesRetainSpareAreaAndConnectedClearance(int capacity)
    {
        var board = Board(10, 10, TerrainKind.SolidRock); var result = Establish(board, capacity);
        Assert.Equal(HeroDeploymentStatus.Established, result.Status);
        Assert.True(result.Deployment!.Area.Area >= 2 * capacity);
        Assert.True(result.Deployment.ManeuverCore.Area >= (3 * capacity + 1) / 2);
        Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(board.Export()), result.Deployment));
    }

    [Theory]
    [InlineData(1, 30, 5)] [InlineData(2, 5, 5)] [InlineData(3, 3, 5)] [InlineData(6, 6, 0)]
    public void RejectsImpossibleConfigurationsBeforeMutation(int width, int height, int capacity)
    {
        var board = Board(width, height); var before = ScenarioDefinitionJson.ToJson(board.Export());
        Assert.Throws<ArgumentException>(() => Establish(board, capacity));
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export())); Assert.Equal(0, board.Revision);
    }

    [Fact]
    public void SmallFeasibleBoardUsesMinimumInsteadOfRejectingPreferredArea()
    {
        var board = Board(3, 4, TerrainKind.Water); var result = Establish(board);
        Assert.Equal(HeroDeploymentStatus.Established, result.Status);
        Assert.InRange(result.Deployment!.Area.Area, 10, 11);
        Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(board.Export()), result.Deployment));
    }

    [Fact]
    public void NarrowCorridorDoesNotCountAsManeuverAreaOrImplicitConversionPermission()
    {
        var source = Board(5, 5, TerrainKind.SolidRock).Export() with
        { Board = new() { Width = 5, Height = 5, DefaultTerrain = TerrainKind.SolidRock,
            Cells = Enumerable.Range(0, 5).Select(y => new CellDefinition(new(2, y), TerrainKind.Grass)).ToList() } };
        var board = new PlanningBoard(source, ContentBundle.Canonical());
        var before = ScenarioDefinitionJson.ToJson(board.Export());
        var result = HeroDeploymentPlanner.Establish(board, new(), 1, []);
        Assert.Equal(HeroDeploymentStatus.InsufficientSpace, result.Status);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export()));
    }

    [Fact]
    public void TwoSmallPadsConnectedByASingleLaneDoNotFormOneWideCore()
    {
        var source = Board(8, 4, TerrainKind.SolidRock).Export() with
        { Board = new() { Width = 8, Height = 4, DefaultTerrain = TerrainKind.SolidRock,
            Cells = new[] { 8, 9, 16, 17, 13, 14, 21, 22, 10, 11, 12, 15 }
                .Select(i => new CellDefinition(new(i % 8, i / 8), TerrainKind.Grass)).ToList() } };
        var board = new PlanningBoard(source, ContentBundle.Canonical());
        var result = HeroDeploymentPlanner.Establish(board, new(), 1, []);
        Assert.Equal(HeroDeploymentStatus.InsufficientSpace, result.Status);
        Assert.Equal(0, board.Revision);
    }

    [Fact]
    public void ClosedDoorsCannotJoinAnImmediateDeploymentCluster()
    {
        var board = Board(4, 4); var walls = board.BeginDraft(new(0, 0, 4, 4), "structure", new());
        for (var y = 0; y < 4; y++) walls.SetEdge(new(y * 4 + 1, EdgeDirection.Right), EdgeKind.ClosedDoor);
        Assert.True(board.TryCommit(walls.BuildPatch(), out _));
        Assert.Equal(HeroDeploymentStatus.InsufficientSpace, Establish(board).Status);
        var opening = board.BeginDraft(new(0, 0, 4, 4), "structure", new()); opening.ReplaceEdge(new(1, EdgeDirection.Right), EdgeKind.OpenDoor);
        Assert.True(board.TryCommit(opening.BuildPatch(), out _));
        Assert.Equal(HeroDeploymentStatus.Established, Establish(board).Status);
        Assert.Equal(3, board.Export().Board.Edges.Count(e => e.Kind == EdgeKind.ClosedDoor));
    }

    [Fact]
    public void SearchExhaustionIsIncompleteAndLeavesTheBoardUntouched()
    {
        var board = Board(terrain: TerrainKind.Water); var before = ScenarioDefinitionJson.ToJson(board.Export());
        var result = Establish(board, work: 0);
        Assert.Equal(HeroDeploymentStatus.Incomplete, result.Status); Assert.Equal(0, result.WorkUsed);
        Assert.Equal(before, ScenarioDefinitionJson.ToJson(board.Export())); Assert.Equal(0, board.Revision);
    }

    [Fact]
    public void ExcludesCompleteExistingFigureFootprintsAndMachines()
    {
        var source = Board().Export() with
        {
            UnitTypeIds = ["deployment-test-large"], UnitTypes = [new("deployment-test-large", 1, 0, 0, 0, 1) { Footprint = Footprint.TwoByTwo }],
            Units = [new("deployment-test-large", "test", new(0, 0))], Agency = [new("deployment-test-large", "test", ControllerKind.Human)],
            Machines = [new("press", new(3, 3), MachineTypes.DiamondPress)]
        };
        var board = new PlanningBoard(source, ContentBundle.Canonical()); var result = Establish(board);
        Assert.Equal(HeroDeploymentStatus.Established, result.Status);
        var area = result.Deployment!.Area.Indices(6).Append(result.Deployment.DepartureIndex).ToHashSet();
        Assert.All(new[] { 0, 1, 6, 7, 21 }, i => Assert.DoesNotContain(i, area));
    }

    [Fact]
    public void RepeatedSeedProducesIdenticalAreaPreparationAndScenario()
    {
        var first = Board(terrain: TerrainKind.Tree); var second = Board(terrain: TerrainKind.Tree);
        var a = Establish(first); var b = Establish(second);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
        Assert.Equal(ScenarioDefinitionJson.ToJson(first.Export()), ScenarioDefinitionJson.ToJson(second.Export()));
    }

    [Fact]
    public void FinalValidationDetectsLaterOccupationAndClosedInternalEdges()
    {
        var board = Board(); var deployment = Establish(board).Deployment!;
        var state = GameEngine.CreateGame(board.Export());
        state.Types.Add(UnitContent.Find(UnitTypeIds.Grunt)!);
        var index = deployment.PlacementOrder[0];
        state.PlaceUnit(UnitTypeIds.Grunt, "late-occupant", "test", new(index % 6, index / 6));
        Assert.NotNull(HeroDeploymentPlanner.Validate(state, deployment));
        var draft = board.BeginDraft(new(0, 0, 6, 6), "hero-deployment", new());
        foreach (var i in deployment.ManeuverCore.Indices(6))
        foreach (var j in board.Neighbors(i).Where(j => j > i && deployment.ManeuverCore.Contains(j, 6)))
            draft.SetEdge(EdgeKey.Between(i, j, 6), EdgeKind.ClosedDoor);
        Assert.True(board.TryCommit(draft.BuildPatch(), out _));
        Assert.NotNull(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(board.Export()), deployment));
    }

    [Fact]
    public void NormalPipelineEstablishesDeploymentBeforeRoutesDeterministically()
    {
        var recipe = new WorldGenerationRecipe(new(0, 0, 20, 20), new(10, 0, 5), 42,
            new(), new(Campsites: ContentDensity.Low, Settlements: ContentDensity.Low), new(), new(PopulationDensity.Low));
        var before = WorldGenerator.Generate(recipe);
        var foundation = WorldGenerator.Generate(recipe);
        var again = WorldGenerator.Generate(recipe);
        Assert.NotNull(before.Context.Deployment); Assert.Equal(before.Fingerprint, again.Fingerprint);
        var deployment = Assert.IsType<HeroDeployment>(foundation.Context.Deployment);
        var stages = foundation.Preview.Frames.Select(f => f.Stage).ToArray();
        Assert.True(Array.IndexOf(stages, "HeroDeployment") < Array.IndexOf(stages, "PrimaryRoutes"));
        Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(foundation.Definition), deployment));
        Assert.DoesNotContain(foundation.Definition.Units, u => u.SideId == HeroesVsMonsters.Heroes);
        Assert.Contains(foundation.Context.PopulationProtectedAreas, a => a.Id == deployment.Area.Id);
    }

    [Theory]
    [InlineData(100, 0, 0)] [InlineData(0, 100, 0)] [InlineData(0, 0, 100)]
    public void NormalGenerationSupportsExtremeNaturalRecipesWithoutSeedSubstitution(int forest, int water, int rock)
    {
        var recipe = new WorldGenerationRecipe(new(0, 0, 12, 12), new(forest, water, rock), 123);
        var world = WorldGenerator.Generate(recipe);
        Assert.Equal(recipe.ToString(), world.Code);
        Assert.NotNull(world.Context.Deployment);
        Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(world.Definition), world.Context.Deployment!));
    }

    [Fact]
    public void RepresentativePresetTerrainAndLargeRockCropRetainDeploymentInvariants()
    {
        foreach (var preset in WorldGenerationPresets.All)
        foreach (var seed in new ulong[] { 1, 42 })
        {
            var recipe = new WorldGenerationRecipe(new(-17, -13, 30, 24), preset.Settings, seed, preset.Enrichment);
            var world = WorldGenerator.Generate(recipe);
            Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(world.Definition), world.Context.Deployment!));
            Assert.Equal(recipe.ToString(), world.Code);
        }
        var large = WorldGenerator.Generate(new WorldGenerationRecipe(new(0, 0, 120, 120), new(0, 0, 100), 42));
        Assert.Null(HeroDeploymentPlanner.Validate(GameEngine.CreateGame(large.Definition), large.Context.Deployment!));
    }
}
