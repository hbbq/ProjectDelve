using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class AbilityCostTests
{
    private static LocalAbilityDefinition Ability(params AbilityCost[] costs) =>
        new("test-heal", "Test Heal", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, 2)) { Costs = [.. costs] };
    private static MechanicValidationContext Context(MechanicSource source = MechanicSource.Unit,
        MechanicTiming timing = MechanicTiming.Action) => new(source, timing);

    [Fact]
    public void CatalogExposesConcreteTypedRequirementsWithExecution()
    {
        Assert.Equal(new[] { "layDownSelf", "spendResource" }, CostCatalog.All.Select(d => d.Id));
        Assert.All(CostCatalog.All, d => Assert.True(d.ExecutionAvailable));
        var spend = CostCatalog.FindByPayload(new SpendResource("test-resource", 3))!;
        Assert.Equal("spendResource", spend.SerializedBinding);
        Assert.Equal("resources", spend.Parameters[0].ReferenceCatalog);
        Assert.Equal(MechanicParameterKind.Reference, spend.Parameters[0].Kind);
        Assert.Equal(1, spend.Parameters[1].Minimum);
        Assert.Equal(9, spend.Profiles.Length);
        Assert.Equal(MechanicContext.ActingUnit, spend.Context);
        Assert.Equal(9, MechanicCatalog.Find("fireball")!.CostProfiles.Length);
        Assert.Single(MechanicCatalog.Find("adjacentUnitHeal")!.CostProfiles.Where(p => p.Source == MechanicSource.Unit));
        Assert.Empty(MechanicCatalog.Find("undying")!.CostProfiles);
        Assert.Empty(MechanicCatalog.Find("worldModifiers")!.CostProfiles);
    }

    [Fact]
    public void SerializationPreservesOrderAndTypedValuesAndOmitsEmptyCosts()
    {
        var options = ContentPackageJson.CreateOptions();
        var ability = Ability(new SpendResource("test-diamond", 2), new LayDownSelf(), new SpendResource("test-diamond", 1));
        var json = JsonSerializer.Serialize(ability, options);
        var restored = JsonSerializer.Deserialize<LocalAbilityDefinition>(json, options)!;
        Assert.Equal(ability.Costs.ToArray(), restored.Costs.ToArray());
        Assert.Equal(json, JsonSerializer.Serialize(restored, options));
        var defaultJson = JsonSerializer.Serialize(ability);
        Assert.Equal(ability.Costs.ToArray(), JsonSerializer.Deserialize<LocalAbilityDefinition>(defaultJson)!.Costs.ToArray());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LocalAbilityDefinition>(
            defaultJson.Replace("\"costs\"", "\"Costs\"", StringComparison.Ordinal)));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("spendResource", document.RootElement.GetProperty("costs")[0].GetProperty("kind").GetString());
        Assert.Equal("layDownSelf", document.RootElement.GetProperty("costs")[1].GetProperty("kind").GetString());
        Assert.DoesNotContain("costs", JsonSerializer.Serialize(Ability(), options));
        Assert.DoesNotContain("Costs", JsonSerializer.Serialize(Ability()));
        var oldJson = "{\"key\":\"test-heal\",\"name\":\"Test Heal\",\"timing\":\"Action\",\"mechanic\":{\"kind\":\"adjacentUnitHeal\",\"selector\":\"Friendlies\",\"amount\":2},\"usage\":{}}";
        Assert.Equal(oldJson, JsonSerializer.Serialize(Ability(), new JsonSerializerOptions(options) { WriteIndented = false }));
        var legacy = JsonSerializer.Deserialize<LocalAbilityDefinition>(oldJson, options)!;
        Assert.Empty(legacy.Costs);
        Assert.Equal(JsonSerializer.Serialize(Ability(), options), JsonSerializer.Serialize(legacy, options));
    }

    [Theory]
    [InlineData("{\"kind\":\"spendResource\",\"Amount\":2}")]
    [InlineData("{\"kind\":\"spendResource\",\"ResourceTypeId\":\"test-resource\"}")]
    [InlineData("{\"kind\":\"unknownCost\"}")]
    [InlineData("{\"kind\":\"layDownSelf\",\"extra\":1}")]
    public void MissingParametersAndUnknownKindsFailEvenWithDefaultJsonOptions(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AbilityCost>(json));

    [Fact]
    public void StructuralValidationRejectsInvalidEntriesWithPrecisePaths()
    {
        Assert.Contains(CostCatalog.Validate(default), d => d.Path == "costs" && d.Code == "required");
        Assert.Contains(CostCatalog.Validate([null!]), d => d.Path == "costs[0]" && d.Code == "unknownCost");
        var invalid = CostCatalog.Validate([new SpendResource(" ", 0), new LayDownSelf(), new LayDownSelf()]);
        Assert.Contains(invalid, d => d.Path == "costs[0].resourceTypeId" && d.Code == "required");
        Assert.Contains(invalid, d => d.Path == "costs[0].amount" && d.Code == "minimum");
        Assert.Contains(invalid, d => d.Path == "costs[2]" && d.Code == "duplicateCost");
        Assert.Empty(CostCatalog.Validate([new SpendResource("same", 1), new SpendResource("same", 2)]));
        Assert.Contains(CostCatalog.Validate([new SpendResource("missing", 1)], availableResourceIds: ImmutableHashSet.Create("present")),
            d => d.Path == "costs[0].resourceTypeId" && d.Code == "unknownReference");
        Assert.Empty(CostCatalog.Validate([new SpendResource("present", 1)], availableResourceIds: ImmutableHashSet.Create("present")));
        var explicitNull = JsonSerializer.Deserialize<LocalAbilityDefinition>(
            "{\"key\":\"test\",\"name\":\"Test\",\"timing\":\"Action\",\"mechanic\":{\"kind\":\"adjacentUnitHeal\",\"selector\":\"Friendlies\",\"amount\":2},\"costs\":null}", ContentPackageJson.CreateOptions())!;
        Assert.True(explicitNull.Costs.IsDefault);
        Assert.Contains(CostCatalog.Validate(explicitNull.Costs), d => d.Code == "required");
    }

    [Fact]
    public void OnlyActiveUnitEquipmentAndItemContextsAreSupported()
    {
        foreach (var source in new[] { MechanicSource.Unit, MechanicSource.Equipment, MechanicSource.Item })
        foreach (var timing in new[] { MechanicTiming.Action, MechanicTiming.BonusAction, MechanicTiming.FreeAction })
            Assert.Empty(CostCatalog.Validate([new LayDownSelf()], Context(source, timing)));
        foreach (var timing in new[] { MechanicTiming.Passive, MechanicTiming.AfterAttack, MechanicTiming.WhenDefeated,
            MechanicTiming.InsteadOfDefeat, MechanicTiming.InsteadOfActivation, MechanicTiming.EndActivation })
            Assert.Contains(CostCatalog.Validate([new LayDownSelf()], Context(timing: timing)), d => d.Code == "unsupportedContext");
        Assert.Contains(CostCatalog.Validate([new LayDownSelf()], Context(MechanicSource.WorldCard)), d => d.Code == "unsupportedContext");
        Assert.Contains(CostCatalog.Validate([new LayDownSelf()], Context() with { HasUnitContext = false }), d => d.Code == "unitContextRequired");
        Assert.Contains(CostCatalog.Validate([new LayDownSelf()], Context(MechanicSource.Item) with { MaxUses = 2 }), d => d.Code == "unsupportedUsage");
    }

    [Fact]
    public void NormalContentBoundaryAcceptsActiveCostsButRejectsInvalidParameters()
    {
        var ability = Ability(new LayDownSelf(), new SpendResource("test-resource", 2));
        AbilityContent.Validate([ability], AbilitySourceKind.Unit);
        var owner = new UnitType("cost-test-owner", 1, 1, 1, 1, 1) { LocalAbilities = [ability] };
        Assert.Empty(ContentDefinitions.Validate(new(owner)));
        var state = new GameState { Physical = new(new Board(1, 1, []), []), Units = [],
            Types = [owner], EquipmentTypes = [], ItemTypes = [], LootCardDefinitions = [] };
        AbilityContent.ValidateState(state);
        var invalid = ability with { Costs = [new SpendResource("test-resource", 0)] };
        var invalidError = Assert.Throws<ArgumentException>(() => AbilityContent.Validate([invalid], AbilitySourceKind.Unit));
        Assert.Equal("localAbilities[0].costs[0].amount", ScenarioValidationContext.PathOf(invalidError));
        Assert.False(state.CanUseAbility("absent", new(ability, AbilitySourceKind.Unit, owner.Id, "absent")));
    }

    [Fact]
    public void ResourceDependenciesAndDescriptionsUseOrderedAuthoredCosts()
    {
        var ability = Ability(new SpendResource("test-resource", 2), new LayDownSelf(), new SpendResource("test-resource", 1));
        var unit = new UnitType("cost-test-owner", 1, 1, 1, 1, 1) { LocalAbilities = [ability] };
        var dependencies = ContentDefinitions.Dependencies(unit);
        Assert.Equal(2, dependencies.Length);
        Assert.All(dependencies, d => Assert.Equal(new ContentIdentity(ContentFamily.Resource, "test-resource"), d.Target));
        Assert.Equal("localAbilities[0].costs[0].resourceTypeId", dependencies[0].Path);
        Assert.Equal("localAbilities[0].costs[2].resourceTypeId", dependencies[1].Path);
        var requirements = string.Join(" ", ability.Costs.Select(CostCatalog.Describe)) + " ";
        Assert.Equal(requirements + Ability().CardEntry().Description, ability.CardEntry().Description);
        var overridden = ability with { DescriptionOverride = "Restore {amount} HP to an adjacent {selector} Unit." };
        Assert.Equal(requirements + "Restore 2 HP to an adjacent friendly Unit.", overridden.CardEntry().Description);
    }
}
