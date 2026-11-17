using System.Text.Json;
using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class Wld1StandardTests
{
    [Theory]
    [InlineData(1)] [InlineData(5)] [InlineData(8)]
    public void EveryWorldEntryPointPublishesTheSameValidatedSetup(int capacity)
    {
        var recipe = new WorldGenerationRecipe(new(0, 0, 12, 12), new(0, 100, 0), 42, HeroCapacity: capacity);
        var normal = WorldGenerator.Generate(recipe);
        var fromCode = WorldGenerator.Generate(recipe.ToString());
        var scheduled = WorldGenerationPipeline.Execute(WorldGenerationPlan.FromRecipe(recipe));
        Assert.Equal(normal.Fingerprint, fromCode.Fingerprint);
        Assert.Equal(ScenarioDefinitionJson.ToJson(normal.Definition), ScenarioDefinitionJson.ToJson(scheduled.Context.Materialize()));
        Assert.Equal(capacity, normal.Definition.Setup!.Deployment.Capacity);
        Assert.Equal(42UL, WorldGenerationRecipe.Parse(normal.Code).Seed);
        Assert.Contains($"-HC{capacity}-S", normal.Code);
        Assert.True(PlayReadyScenarioSetup.Validate(normal.Definition).Valid);
        Assert.Contains(scheduled.Measurements, s => s.Stage == WorldStepKind.HeroDeployment);
        Assert.Contains(scheduled.Measurements, s => s.Stage == WorldStepKind.MandatoryConnectivity);
    }

    [Fact]
    public void UnseededRecipeSelectsItsSeedOnceAndPublishesReproducibleCapacity()
    {
        var world = WorldGenerator.Generate(new WorldGenerationRecipe(new(0, 0, 8, 8), new(), HeroCapacity: 5));
        var completed = WorldGenerationRecipe.Parse(world.Code);
        Assert.NotNull(completed.Seed); Assert.Equal(5, completed.HeroCapacity);
        Assert.Equal(world.Fingerprint, WorldGenerator.Generate(completed).Fingerprint);
        var plan = WorldGenerationPlan.FromRecipe(new(new(0, 0, 8, 8), new()));
        Assert.True(PlayReadyScenarioSetup.Validate(WorldGenerationPipeline.Execute(plan).Context.Materialize()).Valid);
    }

    [Theory]
    [InlineData("Impossible", WorldGenerationFailureKind.ProvenImpossibleConnectivity)]
    [InlineData("Incomplete", WorldGenerationFailureKind.IncompleteAnalysis)]
    [InlineData("WitnessMismatch", WorldGenerationFailureKind.WitnessMaterializationMismatch)]
    [InlineData("InvariantError", WorldGenerationFailureKind.ConstructionInvariantViolation)]
    [InlineData("UnexpectedError", WorldGenerationFailureKind.UnexpectedImplementationFailure)]
    public void PublicFailureClassificationPreservesTheCompleteRepairDiagnostic(string status, WorldGenerationFailureKind expected)
    {
        var plan = WorldGenerationPlan.FromRecipe(new(new(0, 0, 12, 12), new(), 99, HeroCapacity: 8));
        var repair = new MandatoryRepairReport(Enum.Parse<MandatoryRepairStatus>(status), [], 0, 17, true,
            [new("required-dungeon", "room", [25])], ["fixture diagnostic"]);
        var error = new PlayReadyGenerationException(plan, "mandatory-connectivity", 7, "fixture", repair);
        Assert.Equal(expected, error.FailureKind); Assert.Equal(99UL, error.Seed);
        Assert.Equal(7, error.BoardRevision);
        Assert.Contains("required-dungeon", error.DiagnosticsJson);
        using var configuration = JsonDocument.Parse(error.ConfigurationJson);
        var deployment = configuration.RootElement.GetProperty("Steps").EnumerateArray().Single(s => s.GetProperty("Id").GetString() == "hero-deployment");
        Assert.Equal(8, deployment.GetProperty("Parameters").GetProperty("Settings").GetProperty("Capacity").GetInt32());
    }

    [Theory]
    [InlineData("Incomplete", WorldGenerationFailureKind.IncompleteAnalysis)]
    [InlineData("Impossible", WorldGenerationFailureKind.ProvenImpossibleConnectivity)]
    [InlineData("Error", WorldGenerationFailureKind.ConstructionInvariantViolation)]
    public void LateRegistrationFailureRetainsItsOwnClassificationAndFrontierAfterSuccessfulRepair(string status, WorldGenerationFailureKind expected)
    {
        var plan = WorldGenerationPlan.FromRecipe(new(new(0, 0, 12, 12), new(), 99));
        var repaired = new MandatoryRepairReport(MandatoryRepairStatus.Success, [], 0, 0, false, [], []);
        var diagnostic = new CompletionDiagnostic("late-loot", "population", Enum.Parse<CompletionStatus>(status), "fixture", [],
            [new(24, 25, "protected-owner", "Immutable shell")], [new("port", 24, 25, "protected-owner")], 17, false);
        var error = new PlayReadyGenerationException(plan, "population", 7, "fixture", repaired, completion: diagnostic);
        Assert.Equal(expected, error.FailureKind);
        Assert.Contains("protected-owner", error.DiagnosticsJson);
        using var json = JsonDocument.Parse(error.DiagnosticsJson);
        Assert.Equal(17, json.RootElement.GetProperty("Completion").GetProperty("WorkUsed").GetInt32());
    }
}
