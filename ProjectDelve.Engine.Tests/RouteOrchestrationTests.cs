using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class RouteOrchestrationTests
{
    [Fact]
    public void EffectiveRoutesRoundtripAndConstructOrderedOptionalStages()
    {
        var recipe = new WorldGenerationRecipe(new(-17, -13, 50, 50), new(), 42, new(), new(), new());
        foreach (var profile in Enum.GetValues<RouteProfileKind>())
        {
            var configured = recipe with { Routes = new(Profile: profile, ThroughRoutes: 3, WaterCrossings: true) };
            Assert.Equal(configured, WorldGenerationRecipe.Parse(configured.ToString()));
        }
        var plan = WorldGenerationPlan.FromRecipe(recipe).Validate();
        Assert.Equal(new[] { RouteNetworkMode.PrimaryRoutes, RouteNetworkMode.SiteConnections, RouteNetworkMode.ExplorationLinks },
            plan.Steps.Select(s => s.Parameters).OfType<RouteNetworkStepParameters>().Select(p => p.Mode));
        Assert.True(plan.Steps.ToList().FindIndex(s => s.Id == "primary-routes") < plan.Steps.ToList().FindIndex(s => s.Id == "civilization"));
        Assert.DoesNotContain(WorldGenerationPlan.FromRecipe(recipe with { Routes = new(false) }).Steps, s => s.Kind == WorldStepKind.RouteNetwork);
    }

    [Fact]
    public void RepeatedRouteStagesNeedDistinctIdsAndWriterDependencies()
    {
        var recipe = new WorldGenerationRecipe(new(0, 0, 30, 30), new(), 42);
        var plan = WorldGenerationPlan.FromRecipe(recipe);
        var route = new WorldGenerationStep("first", WorldStepKind.RouteNetwork, new RouteNetworkStepParameters(RouteNetworkMode.PrimaryRoutes, new()), ["formations"]);
        var again = route with { Id = "second", DependsOn = ["first"] };
        Assert.NotNull((plan with { Steps = [.. plan.Steps.Take(2), route, again] }).Validate());
        Assert.Throws<ArgumentException>(() => (plan with { Steps = [.. plan.Steps.Take(2), route, again with { DependsOn = ["formations"] }] }).Validate());
        Assert.Throws<ArgumentException>(() => (plan with { Steps = [.. plan.Steps.Take(2), route, again with { Id = "first" }] }).Validate());
    }

    [Fact]
    public void CivilizationReusesExistingBoardAndItsProtectedReservations()
    {
        var context = new WorldGenerationContext(new(1, new(0, 0, 0)), new(0, 0, 40, 40)); context.SampleNaturalTerrain();
        context.OriginalAnalysis = context.CurrentAnalysis = TerrainAnalyzer.Analyze(context.Window, context.Samples);
        var board = context.ConstructionBoard = new(context.Materialize(), ContentBundle.Canonical());
        var draft = board.BeginDraft(new(0, 0, 40, 40), "protected", new()); draft.Reserve(Enumerable.Range(0, 1600)); Assert.True(board.TryCommit(draft.BuildPatch(), out _));
        CivilizationGenerator.Apply(context, new("civilization", WorldStepKind.Civilization, new CivilizationStepParameters(new()), ["formations"]), ContentBundle.Canonical());
        Assert.Same(board, context.ConstructionBoard); Assert.Empty(context.Sites); Assert.True(board.IsReserved(400));
    }
}
