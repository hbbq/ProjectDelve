using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MandatoryRepairPipelineTests
{
    [Fact]
    public void MandatoryRepairRunsBeforeFinalizationAndPopulationAndValidatesFinalRequirements()
    {
        var preset = WorldGenerationPresets.All.Single(p => p.Id == "RockyWilds");
        var recipe = new WorldGenerationRecipe(new(-17, -13, 30, 24), preset.Settings, 42, preset.Enrichment,
            preset.Civilization, preset.Routes, preset.Population, "Classic");
        var world = WorldGenerator.Generate(recipe);
        var frames = world.Preview.Frames.Select(f => f.StepId).ToArray();
        Assert.True(Array.IndexOf(frames, "mandatory-connectivity") < Array.IndexOf(frames, "current"));
        Assert.True(Array.IndexOf(frames, "current") < Array.IndexOf(frames, "population"));
        Assert.True(world.Context.MandatoryRepair!.Success);
        var state = GameEngine.CreateGame(world.Definition);
        Assert.Empty(world.Context.Completion!.ActualFailures(state));
        Assert.Null(HeroDeploymentPlanner.Validate(state, world.Context.Deployment!));
        Assert.Equal(recipe.ToString(), world.Code);
        Assert.Contains(world.Preview.Frames, f => f.StepId == "mandatory-connectivity");
    }

    [Theory]
    [InlineData(5)] [InlineData(8)]
    public void CapacityAndFinalActualConnectivitySurvivePopulatedGeneration(int capacity)
    {
        var recipe = new WorldGenerationRecipe(new(10, -9, 30, 24), new(45, 25, 25), 99,
            new(), new(), new(WaterCrossings: false), new(PopulationDensity.High, 5), "Classic");
        var a = WorldGenerator.Generate(recipe with { HeroCapacity = capacity });
        var b = WorldGenerator.Generate(recipe with { HeroCapacity = capacity });
        Assert.Equal(capacity, a.Context.Deployment!.Capacity);
        Assert.True(a.Context.Deployment.Area.Area >= capacity * 2);
        Assert.Empty(a.Context.Completion!.ActualFailures(GameEngine.CreateGame(a.Definition)));
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(a.Context.MandatoryRepair), JsonSerializer.Serialize(b.Context.MandatoryRepair));
    }

    [Fact]
    public void LateContentCannotUseUnrepairedPotentialRegion()
    {
        const int width = 12, height = 8;
        var board = new PlanningBoard(new ScenarioDefinition
        {
            Board = new() { Width = width, Height = height, DefaultTerrain = TerrainKind.Grass,
                Cells = Enumerable.Range(36, 12).Select(i => new CellDefinition(new(i % width, i / width), TerrainKind.SolidRock)).ToList() },
            UnitTypeIds = [], Units = [], Agency = [], Loot = LootSettings.Empty
        }, ContentBundle.Canonical());
        var area = Enumerable.Range(5, 2).SelectMany(y => Enumerable.Range(0, 5).Select(x => y * width + x)).ToArray();
        var core = area.Where(i => i % width < 4).ToArray();
        var deployment = new HeroDeployment(5, GeneratedArea.From("spawn", area, width), GeneratedArea.From("core", core, width),
            core.Concat(area.Except(core)).ToImmutableArray(), 76, 77, 0);
        var reserve = board.BeginDraft(new(0, 0, width, height), "hero-deployment", new()); reserve.Reserve(area.Append(77));
        Assert.True(board.TryCommit(reserve.BuildPatch(), out _));
        var guard = new CompletionGuard(board, deployment, Enumerable.Range(0, width * height));
        Assert.Contains(2, guard.InteractivePlacementDomain()); // legally preparable, but no current route
        Assert.True(MandatoryConnectivityRepair.Run(board, guard).Success);
        guard.MarkActualConnectivityEstablished();
        Assert.DoesNotContain(2, guard.InteractivePlacementDomain());
        var late = board.Export() with { Loot = PopulationLoot.FromPreset("Classic"), LootMarkers = [new("late-unreachable", new(2, 0))] };
        var rejected = guard.RegisterInteractives(late, "late-loot");
        Assert.Equal(CompletionStatus.Error, rejected.Status);
        Assert.Contains(rejected.Failures, f => f.LocationId.EndsWith("late-unreachable"));
        Assert.DoesNotContain(guard.Registry.Locations, l => l.SourceId == "late-unreachable");
        var valid = board.Export() with { Loot = PopulationLoot.FromPreset("Classic"), LootMarkers = [new("late-reachable", new(8, 6))] };
        Assert.Equal(CompletionStatus.Allowed, guard.RegisterInteractives(valid, "late-loot").Status);
        Assert.Empty(guard.ActualFailures(GameEngine.CreateGame(valid)));
    }

    [Fact]
    public void PlanRejectsTopologyConstructionAfterMandatoryRepair()
    {
        var recipe = new WorldGenerationRecipe(new(), new(), 42, Civilization: new(), Routes: new());
        var plan = WorldGenerationPlan.FromRecipe(recipe);
        var steps = plan.Steps.ToList();
        var repair = steps.FindIndex(s => s.Kind == WorldStepKind.MandatoryConnectivity);
        steps.Insert(repair + 1, new("unsafe-late-route", WorldStepKind.RouteNetwork,
            new RouteNetworkStepParameters(RouteNetworkMode.ExplorationLinks, new()), [steps[repair].Id]));
        Assert.Throws<ArgumentException>(() => (plan with { Steps = steps }).Validate());
    }

    [Fact]
    public void FailureRetainsCompleteDerivedSettingsAndStableReproductionContext()
    {
        var recipe = new WorldGenerationRecipe(new(-4, 7, 30, 24), new(20, 20, 40), 123,
            new(Intensity: 3), new(), new(), new(PopulationDensity.High, 5), "Classic");
        var plan = WorldGenerationPlan.FromRecipe(recipe with { HeroCapacity = 8 });
        var failure = new PlayReadyGenerationException(plan, "mandatory-connectivity", 17, "witness mismatch");
        var again = new PlayReadyGenerationException(plan, "mandatory-connectivity", 17, "witness mismatch");
        Assert.Equal(123UL, failure.Seed); Assert.Equal(17L, failure.BoardRevision);
        Assert.Equal("mandatory-connectivity", failure.Stage); Assert.Equal(failure.ConfigurationJson, again.ConfigurationJson);
        using var json = JsonDocument.Parse(failure.ConfigurationJson);
        var deployment = json.RootElement.GetProperty("Steps").EnumerateArray().Single(s => s.GetProperty("Id").GetString() == "hero-deployment");
        Assert.Equal(8, deployment.GetProperty("Parameters").GetProperty("Settings").GetProperty("Capacity").GetInt32());
        Assert.Contains("witness mismatch", failure.Message);
    }

}
