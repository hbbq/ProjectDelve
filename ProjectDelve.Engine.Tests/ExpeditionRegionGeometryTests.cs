using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ExpeditionRegionGeometryTests
{
    private static readonly ContentBundle Content = ContentBundle.Canonical();
    private static WorldGenerationRecipe Recipe(NaturalTerrainSettings? nature = null, ulong seed = 17) =>
        new(new(-60, 0, 60, 60), nature ?? new(), seed, new(),
            new(Enabled: false), new(Enabled: false), new(PopulationDensity.Off));

    [Theory]
    [InlineData(35, 12, 15)]
    [InlineData(0, 100, 0)]
    [InlineData(0, 0, 100)]
    [InlineData(100, 0, 0)]
    public void FinalOrdinaryLandGraphConnectsIncomingLandingAndEveryExitPadCell(int forest, int water, int rock)
    {
        var entry = new Cell(0, 30); var exit = new Cell(59, 20); var pad = new ObjectiveArea(57, 19, 2, 3);
        var definition = ExpeditionRegionGeometry.Generate(Recipe(new(forest, water, rock)), entry, exit, pad, 123, Content);
        var state = GameEngine.CreateGame(definition, Content);
        Assert.True(PlayReadyScenarioSetup.Validate(definition, state).Valid);
        var reached = PlayReadyScenarioSetup.ReachableFromDeployment(definition, state);
        Assert.Contains(30 * 60, reached);
        Assert.Contains(20 * 60 + 59, reached);
        var declared = Assert.Single(definition.Setup!.RequiredLocations, l => l.Id == "expedition/exit-pad");
        Assert.Equal(ScenarioAccessCoverage.All, Assert.Single(declared.Groups).Coverage);
        Assert.Equal(6, declared.Groups[0].Positions.Count);
        foreach (var cell in declared.Groups[0].Positions.Append(entry).Append(exit))
        {
            Assert.Contains(cell.Y * 60 + cell.X, reached);
            Assert.Equal("grass", state.Physical.Board.TerrainDefinitionAt(cell).Id);
        }
        Assert.All(definition.Setup.Deployment.Cells, c => Assert.InRange(Math.Abs(c.X - entry.X), 0, 6));
        Assert.Empty(definition.Units);
        Assert.Empty(definition.Board.Edges); // The witness requires no Door action in this slice.
    }

    [Fact]
    public void SameRecordedSeedsReproduceGeometryAndAdjacentLandingTerrainIsCompatible()
    {
        var sourceRecipe = Recipe(seed: 889);
        var source = ExpeditionRegionGeometry.Generate(sourceRecipe, new(0, 30), new(59, 20), new(57, 19, 2, 3), 123, Content);
        var repeat = ExpeditionRegionGeometry.Generate(sourceRecipe, new(0, 30), new(59, 20), new(57, 19, 2, 3), 123, Content);
        Assert.Equal(ScenarioDefinitionJson.ToJson(source), ScenarioDefinitionJson.ToJson(repeat));
        var destination = ExpeditionRegionGeometry.Generate(sourceRecipe with { Window = new(0, 0, 60, 60) },
            new(0, 20), new(30, 59), new(29, 57, 3, 2), 456, Content);
        var sourceState = GameEngine.CreateGame(source, Content);
        var destinationState = GameEngine.CreateGame(destination, Content);
        Assert.Equal(sourceState.Physical.Board.TerrainDefinitionAt(new(59, 20)).Id, destinationState.Physical.Board.TerrainDefinitionAt(new(0, 20)).Id);
        Assert.True(PlayReadyScenarioSetup.Validate(destination, destinationState).Valid);
    }

    [Fact]
    public void InitialObligationsExistBeforeRepairAndEndpointsHaveEarlyProtection()
    {
        var recipe = Recipe(new(0, 100, 0));
        var plan = new ExpeditionGeometryPlan(recipe.Window, 5, new(0, 30), new(59, 20), new(57, 19, 2, 3), 123);
        var execution = WorldGenerationPipeline.Execute(WorldGenerationPlan.FromRecipe(recipe), Content, plan);
        Assert.Equal(recipe.Seed, execution.Context.World.Seed);
        Assert.NotNull(execution.Context.MandatoryRepair);
        Assert.True(execution.Context.MandatoryRepair.Success);
        Assert.All(plan.EndpointIndices, i => Assert.True(execution.Context.ConstructionBoard!.IsReserved(i)));
        Assert.Contains(execution.Context.Completion!.Registry.Locations, l => l.Id == "expedition/exit-pad");
        // Base nature remains sampled from world coordinates, independently of local deployment seed.
        var sampler = new NaturalTerrainSampler(recipe.World, Content);
        for (var y = 0; y < 60; y += 7)
        for (var x = 0; x < 60; x += 7)
            Assert.Equal(sampler.Sample(x - 60, y).TerrainTypeId, execution.Context.OriginalSamples[y * 60 + x].TerrainTypeId);
        Assert.DoesNotContain(execution.Context.Construction, c => c.Id.Contains("corridor", StringComparison.Ordinal));
        var board = execution.Context.ConstructionBoard!;
        var guarded = board.BeginDraft(new(0, 0, 60, 60), "expedition/endpoints", new());
        guarded.SetTerrain(plan.EndpointIndices[0], "solid-rock");
        Assert.False(execution.Context.Completion.TryPublish(guarded.BuildPatch(), "test/block-endpoint", "test", out var rejection));
        Assert.Equal(CompletionStatus.Error, rejection.Status);
        Assert.Equal("grass", board.TerrainAt(plan.EndpointIndices[0]));
    }

    [Fact]
    public void RequiresRecordedWorldSeedAndStillSupportsExplicitlyDisabledOptionalStages()
    {
        ScenarioDefinition Generate(WorldGenerationRecipe recipe) => ExpeditionRegionGeometry.Generate(recipe,
            new(0, 30), new(59, 20), new(57, 19, 2, 3), 123, Content);
        Assert.Throws<ArgumentException>(() => Generate(Recipe() with { Seed = null }));
        var definition = Generate(Recipe());
        Assert.Empty(definition.Units);
        Assert.True(PlayReadyScenarioSetup.Validate(definition, Content).Valid);
    }
}
