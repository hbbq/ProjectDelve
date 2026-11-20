using ProjectDelve.Generation;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class ExpeditionEconomyGenerationTests
{
    private static readonly ContentBundle Content = ContentBundle.Canonical() with
    {
        Items = [.. ContentBundle.Canonical().Items.Where(t => t.Id != "last-resort-fireball"),
            new("last-resort-fireball", "Test Vending Item")
            { SellValue = 7, LocalAbilities = [new("use", "Fireball", AbilityTiming.Action, new FireballMechanic())] }]
    };
    private static readonly WorldGenerationRecipe Recipe = new(new(0, 0, 60, 60), new(0, 0, 0), 17,
        new(), new(Enabled: false), new(Enabled: false), new(PopulationDensity.Low));

    [Fact]
    public void RequestsAreIndependentDeterministicAndOccasional()
    {
        foreach (var type in new[] { MachineTypes.DelveCare, MachineTypes.MeteorOMat })
        {
            var requests = Enumerable.Range(0, 1200).Count(i => ExpeditionEconomyGenerator.Requests((ulong)i, type.Id));
            Assert.InRange(requests, 65, 140);
        }
        Assert.Contains(Enumerable.Range(0, 1200), i => ExpeditionEconomyGenerator.Requests((ulong)i, MachineTypes.DelveCare.Id) !=
            ExpeditionEconomyGenerator.Requests((ulong)i, MachineTypes.MeteorOMat.Id));
    }

    [Theory]
    [InlineData("delvecare")]
    [InlineData("meteor-o-mat")]
    public void PlacementPreservesExistingGeographyPopulationAndLootAndHasOccupiedAccess(string typeId)
    {
        var seed = Enumerable.Range(0, 1200).Select(i => (ulong)i).First(i => ExpeditionEconomyGenerator.Requests(i, typeId));
        // Compare the service stage on its original inputs. Later exploration
        // piles intentionally select from the Cells left free by these Machines.
        var profile = ExpeditionWorldProfile.Create(Recipe.NaturalTerrain) with { CreditsVersion = null, CreditsSettings = null };
        var current = Execute(seed, profile);
        var legacy = Execute(seed, profile with { EconomyVersion = null });
        var definition = current.Context.Materialize();
        var machine = Assert.Single(definition.Machines!, m => m.Type.Id == typeId);
        Assert.Null(machine.OwnerSide);
        var previous = legacy.Context.Materialize();
        Assert.Equal(ScenarioDefinitionJson.ToJson(previous), ScenarioDefinitionJson.ToJson(definition with
        { Machines = definition.Machines!.Where(m => m.Type.Operation.PaidServices is null).ToList(), Setup = previous.Setup }));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(previous.Setup!.Deployment),
            System.Text.Json.JsonSerializer.Serialize(definition.Setup!.Deployment));
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(Execute(seed, profile).Context.Materialize()));
        var state = GameEngine.CreateGame(definition, Content);
        Assert.Equal("Test Vending Item", state.ItemTypes.Single(t => t.Id == "last-resort-fireball").Name);
        Assert.Equal(7, state.ItemTypes.Single(t => t.Id == "last-resort-fireball").SellValue);
        Assert.True(current.Context.Expedition!.HasOccupiedAccess(state, current.Context.Deployment!));
        Assert.Empty(current.Context.Completion!.ActualFailures(state));
        Assert.True(PlayReadyScenarioSetup.Validate(definition, state).Valid);
        Assert.DoesNotContain(machine.Position.Y * 60 + machine.Position.X, current.Context.Expedition.EndpointIndices);
        Assert.False(current.Context.ConstructionBoard!.IsRouteCorridor(machine.Position.Y * 60 + machine.Position.X));
        Assert.Equal(ScenarioDefinitionJson.ToJson(definition), ScenarioDefinitionJson.ToJson(
            ScenarioDefinitionTransport.Decode(ScenarioDefinitionTransport.Encode(definition))));
    }

    [Fact]
    public void LegacyAndOrdinaryRecipesDoNotAcquireServices()
    {
        var profile = ExpeditionWorldProfile.Create(Recipe.NaturalTerrain) with { EconomyVersion = null };
        Assert.DoesNotContain(Execute(0, profile).Context.Materialize().Machines ?? [], m => m.Type.Operation.PaidServices is not null);
        Assert.DoesNotContain(WorldGenerator.Generate(Recipe).Definition.Machines ?? [], m => m.Type.Operation.PaidServices is not null);
        Assert.Throws<ArgumentException>(() => (profile with { EconomyVersion = "future" }).Validate());
    }

    [Fact]
    public void ARequestedServiceWithoutAnOpenPadIsOmittedWithoutRepairingTheRegion()
    {
        var seed = Enumerable.Range(0, 1200).Select(i => (ulong)i)
            .First(i => ExpeditionEconomyGenerator.Requests(i, "meteor-o-mat"));
        var recipe = Recipe with { NaturalTerrain = new(0, 100, 0), Population = new(PopulationDensity.Off) };
        var profile = ExpeditionWorldProfile.Create(recipe.NaturalTerrain);
        var current = ExpeditionRegionGeometry.Execute(recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3), seed, Content, profile: profile);
        var legacy = ExpeditionRegionGeometry.Generate(recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3), seed,
            Content, profile with { EconomyVersion = null });
        Assert.DoesNotContain(current.Context.Materialize().Machines!, m => m.Type.Operation.PaidServices is not null);
        Assert.Contains(current.Context.Completion!.Diagnostics, d => d.CandidateId == "expedition/meteor-o-mat" && d.Status == CompletionStatus.Omitted);
        Assert.Equal(ScenarioDefinitionJson.ToJson(legacy), ScenarioDefinitionJson.ToJson(current.Context.Materialize()));
    }

    [Fact]
    public void OptionalVendingDoesNotInjectAMissingItem()
    {
        var seed = Enumerable.Range(0, 1200).Select(i => (ulong)i)
            .First(i => ExpeditionEconomyGenerator.Requests(i, "meteor-o-mat"));
        var context = ExpeditionRegionGeometry.Execute(Recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3), seed,
            Content with { Items = [.. Content.Items.Where(t => t.Id != "last-resort-fireball")] },
            profile: ExpeditionWorldProfile.Create(Recipe.NaturalTerrain)).Context;
        Assert.DoesNotContain(context.Materialize().Machines!, m => m.Type.Id == "meteor-o-mat");
        Assert.Contains(context.Completion!.Diagnostics, d => d.CandidateId == "expedition/meteor-o-mat" &&
            d.Status == CompletionStatus.Omitted && d.Detail!.Contains("Item Type"));
    }

    [Fact]
    public void PaidMachinePhysicalIdentitiesAreDistinctAcrossRegionsWithTheSameLocalSeed()
    {
        var seed = Enumerable.Range(0, 1200).Select(i => (ulong)i)
            .First(i => ExpeditionEconomyGenerator.Requests(i, "meteor-o-mat"));
        var recipe = Recipe with { Population = new(PopulationDensity.Off) };
        var profile = ExpeditionWorldProfile.Create(recipe.NaturalTerrain);
        Machine Generate(WorldWindow window) => Assert.Single(ExpeditionRegionGeometry.Generate(recipe with { Window = window },
            new(0, 30), new(59, 20), new(57, 19, 2, 3), seed, Content, profile).Machines!, m => m.Type.Id == "meteor-o-mat");
        var first = Generate(new(0, 0, 60, 60));
        var second = Generate(new(-60, 60, 60, 60));
        Assert.Equal("expedition/meteor-o-mat/0,0", first.Id);
        Assert.Equal("expedition/meteor-o-mat/-60,60", second.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Id, Generate(new(0, 0, 60, 60)).Id);
    }

    [Fact]
    public void InitialFiguresCannotBlockEveryServiceApproachEvenWhenTheExitRemainsReachable()
    {
        var seed = Enumerable.Range(0, 1200).Select(i => (ulong)i)
            .First(i => ExpeditionEconomyGenerator.Requests(i, "delvecare"));
        var context = Execute(seed, ExpeditionWorldProfile.Create(Recipe.NaturalTerrain)).Context;
        var definition = context.Materialize();
        var machine = Assert.Single(definition.Machines!, m => m.Type.Id == "delvecare");
        var approaches = MachineGeometry.PotentialOperatingPositions(GameEngine.CreateGame(definition, Content).Physical.Board, machine);
        Assert.Equal(8, approaches.Length);
        const string type = "test-service-blocker";
        definition = definition with
        {
            UnitTypeIds = [type], UnitTypes = [new(type, 1, 1, 1, 1, 1)],
            Agency = [new(type, "blockers", ControllerKind.Automated)],
            Orders = [],
            Units = approaches.Select((cell, i) => new UnitPlacement(type, "blockers", cell, UnitId: "blocker-" + i)).ToList()
        };
        var state = GameEngine.CreateGame(definition, Content);
        var withoutServiceContract = new ExpeditionGeometryPlan(Recipe.Window, 5, new(0, 30), new(59, 20),
            new(57, 19, 2, 3), seed);
        Assert.True(withoutServiceContract.HasOccupiedAccess(state, context.Deployment!));
        Assert.False(context.Expedition!.HasOccupiedAccess(state, context.Deployment!));
    }

    private static WorldGenerationExecution Execute(ulong seed, ExpeditionWorldProfile profile) =>
        ExpeditionRegionGeometry.Execute(Recipe, new(0, 30), new(59, 20), new(57, 19, 2, 3), seed, Content, profile: profile);
}
