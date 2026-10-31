using System.Collections.Immutable;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class WorldAndEnvironmentMechanicCatalogTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void ImmediateAmountContractsAndExistingFamilyValidatorsAgree(int amount, bool valid)
    {
        var loot = new HealCollectorMechanic(amount);
        Assert.Equal(valid, MechanicCatalog.Validate(loot).IsEmpty);
        var lootDefinition = new LootCardDefinition("healing", "Healing") { Immediate = loot };
        AssertValidity(valid, () => LootCardCatalog.Validate([lootDefinition]));

        WorldImmediateMechanic[] world = [new HealDamagedUnitsMechanic(amount),
            new DamageUnitsByAdjacencyMechanic(amount, true), new ReplenishLimitedAbilitiesMechanic(amount)];
        foreach (var mechanic in world)
        {
            var descriptor = MechanicCatalog.FindByPayload(mechanic)!;
            var parameter = Assert.Single(descriptor.Parameters.Where(p => p.Name == "amount"));
            Assert.Equal(1, parameter.Minimum);
            Assert.Equal(100, parameter.Maximum);
            Assert.Null(parameter.DefaultValue);
            Assert.Equal(valid, MechanicCatalog.Validate(mechanic).IsEmpty);
            var definition = new WorldCardDefinition("world", "World", WorldCardLifecycle.Immediate) { Immediate = mechanic };
            AssertValidity(valid, () => WorldCardCatalog.Validate([definition]));
        }

        if (!valid)
            Assert.Equal("card.immediate.amount", Assert.Single(MechanicCatalog.Validate(loot, path: "card.immediate")).Path);
    }

    [Fact]
    public void ImmediateWorldDiscoveryIncludesEveryExistingConcreteOperationWithoutUnitOrGrantSupport()
    {
        WorldImmediateMechanic[] payloads = [new LayDownAllUnitsMechanic(), new SetAllDoorsMechanic(false),
            new FullyHealUnitsMechanic(), new HealDamagedUnitsMechanic(1),
            new DamageUnitsByAdjacencyMechanic(1, false), new ReplenishLimitedAbilitiesMechanic(1)];
        Assert.Equal(payloads.Length, MechanicCatalog.All.Count(d => d.Family == MechanicFamily.ImmediateWorld));
        foreach (var payload in payloads)
        {
            var descriptor = MechanicCatalog.FindByPayload(payload)!;
            Assert.False(descriptor.Grantable);
            Assert.True(descriptor.Description.AllowsOverride);
            Assert.True(descriptor.Description.RejectsNumericOverrideLiterals);
            Assert.True(descriptor.Description.RejectsBlankOverride);
            Assert.True(descriptor.Description.RejectsUnknownOverridePlaceholders);
            Assert.Equal(MechanicContext.Global, descriptor.Context);
            Assert.Equal(MechanicTargetSelection.None, descriptor.Targeting.Selection);
            Assert.Empty(MechanicCatalog.Validate(payload,
                new(MechanicSource.WorldCard, MechanicTiming.WorldCardReveal, HasUnitContext: false)));
            Assert.Contains(MechanicCatalog.Validate(payload, new(MechanicSource.Item, MechanicTiming.Action)),
                diagnostic => diagnostic.Code == "unsupportedContext");
            Assert.Contains(MechanicCatalog.Validate(payload, new(MechanicSource.WorldCard, MechanicTiming.WorldCardReveal, 1)),
                diagnostic => diagnostic.Code == "unsupportedUsage");
        }
        var open = Assert.Single(MechanicCatalog.Find("setAllDoors")!.Parameters);
        Assert.Equal(MechanicParameterKind.Boolean, open.Kind);
        Assert.True(open.Required);
        Assert.Null(open.DefaultValue);
        Assert.Empty(MechanicCatalog.Find("setAllDoors")!.Description.RequiredOverridePlaceholders);
        Assert.Equal(new[] { "amount" }, MechanicCatalog.Find("healDamagedUnits")!.Description.RequiredOverridePlaceholders);
    }

    [Fact]
    public void ImmediateLootRequiresCollectorContextAndKeepsRewardExclusivity()
    {
        var payload = new HealCollectorMechanic(2);
        var descriptor = MechanicCatalog.FindByPayload(payload)!;
        Assert.Equal(MechanicContext.CollectorUnit, descriptor.Context);
        Assert.Empty(MechanicCatalog.Validate(payload, new(MechanicSource.LootCard, MechanicTiming.LootReveal)));
        Assert.Contains(MechanicCatalog.Validate(payload,
            new(MechanicSource.LootCard, MechanicTiming.LootReveal, HasUnitContext: false)),
            diagnostic => diagnostic.Code == "unitContextRequired");
        Assert.Throws<ArgumentException>(() => LootCardCatalog.Validate(
            [new("mixed", "Mixed") { Immediate = payload, ItemTypeId = "second-wind" }]));
        Assert.Throws<ArgumentException>(() => LootCardCatalog.Validate(
            [new("unknown", "Unknown") { Immediate = new UnknownLootMechanic() }]));
    }

    [Fact]
    public void ContinuousWorldModifiersRetainEmptyDefaultsSignedBoundsAndNestedDiagnostics()
    {
        var descriptor = MechanicCatalog.Find("worldModifiers")!;
        var list = Assert.Single(descriptor.Parameters);
        Assert.True(descriptor.Description.AllowsOverride);
        Assert.True(descriptor.Description.RejectsNumericOverrideLiterals);
        Assert.Equal(new[] { "modifiers" }, descriptor.Description.NonemptyListOverridePlaceholders);
        Assert.False(list.Required);
        Assert.Equal(0, list.DefaultValue!.Value.GetArrayLength());
        var target = Assert.Single(list.Item!.Members.Where(p => p.Name == "target"));
        Assert.Equal(Enum.GetNames<WorldModifierTarget>(), target.AllowedValues);
        Assert.DoesNotContain("Rng", target.AllowedValues);

        var empty = new WorldCardDefinition("empty", "Empty", WorldCardLifecycle.Continuous);
        WorldCardCatalog.Validate([empty]);
        Assert.Empty(MechanicCatalog.Validate(empty.Modifiers));
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([empty with { Modifiers = default }]));
        Assert.Equal("card.modifiers", Assert.Single(MechanicCatalog.Validate(default(ImmutableArray<WorldModifier>), path: "card")).Path);

        foreach (var stat in Enum.GetValues<WorldModifierTarget>())
        {
            ImmutableArray<WorldModifier> modifiers = [new(stat, -100), new(stat, 0), new(stat, 100)];
            Assert.Empty(MechanicCatalog.Validate(modifiers));
            WorldCardCatalog.Validate([empty with { Modifiers = modifiers }]);
        }

        ImmutableArray<WorldModifier> invalid = [new((WorldModifierTarget)99, 101), null!];
        var errors = MechanicCatalog.Validate(invalid, path: "card");
        Assert.Contains(errors, error => error.Path == "card.modifiers[0].target" && error.Code == "enum");
        Assert.Contains(errors, error => error.Path == "card.modifiers[0].amount" && error.Code == "maximum");
        Assert.Contains(errors, error => error.Path == "card.modifiers[1]" && error.Code == "required");
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([empty with { Modifiers = invalid }]));
    }

    [Fact]
    public void WorldLifecycleAndStrictDescriptionOverridesStayAtTheirExistingBoundary()
    {
        var card = new WorldCardDefinition("healing", "Healing", WorldCardLifecycle.Immediate)
        { Immediate = new HealDamagedUnitsMechanic(2), DescriptionOverride = "Restore {amount} HP." };
        WorldCardCatalog.Validate([card]);
        Assert.Equal("Restore 2 HP.", card.Description);
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([card with { Lifecycle = WorldCardLifecycle.Continuous }]));
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([card with { Modifiers = [new(WorldModifierTarget.Mov, 1)] }]));
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([card with { Immediate = new UnknownWorldMechanic() }]));
        foreach (var text in new[] { "", "Restore 2 HP.", "Restore health.", "Restore {amount} HP and {other}.", "{amount} {modifiers}" })
            Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([card with { DescriptionOverride = text }]));

        WorldCardCatalog.Validate([card with { Immediate = new SetAllDoorsMechanic(false), DescriptionOverride = "Close every Door." }]);
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate(
            [card with { Immediate = new SetAllDoorsMechanic(false), DescriptionOverride = "Set Doors to {open}." }]));
        var continuous = new WorldCardDefinition("wind", "Wind", WorldCardLifecycle.Continuous)
        { Modifiers = [new(WorldModifierTarget.Mov, -1)], DescriptionOverride = "While active: {modifiers}" };
        WorldCardCatalog.Validate([continuous]);
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([continuous with { DescriptionOverride = "{amount}" }]));
        WorldCardCatalog.Validate([continuous with { Modifiers = [], DescriptionOverride = "No Effect." }]);
        Assert.Throws<ArgumentException>(() => WorldCardCatalog.Validate([continuous with { Modifiers = [], DescriptionOverride = "{modifiers}" }]));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void ProductionMetadataAndValidationShareTheExistingAttemptDomain(int attempts, bool valid)
    {
        var payload = new DirectionalResourceProduction("coal", attempts);
        Assert.Equal(valid, MechanicCatalog.Validate(payload).IsEmpty);
        AssertValidity(valid, () => EnvironmentalProductionContent.Validate(payload));
        if (!valid)
            Assert.Equal("producer.attempts", Assert.Single(MechanicCatalog.Validate(payload, path: "producer")).Path);
    }

    [Fact]
    public void ProductionKeepsExplicitDefaultsReferenceResolutionAndFixedContext()
    {
        var payload = new DirectionalResourceProduction("fruit", 1);
        var descriptor = MechanicCatalog.FindByPayload(payload)!;
        Assert.Equal(MechanicContext.TerrainCell, descriptor.Context);
        Assert.False(descriptor.Grantable);
        Assert.Empty(MechanicCatalog.Validate(payload, new(MechanicSource.Terrain, MechanicTiming.RoundStart, HasUnitContext: false)));
        Assert.Contains(MechanicCatalog.Validate(payload, new(MechanicSource.Terrain, MechanicTiming.Action)),
            error => error.Code == "unsupportedContext");
        var placement = Assert.Single(descriptor.Parameters.Where(p => p.Name == "placement"));
        var sourceLos = Assert.Single(descriptor.Parameters.Where(p => p.Name == "sourceLos"));
        Assert.False(placement.Required);
        Assert.False(sourceLos.Required);
        Assert.Equal(payload.Placement.ToString(), placement.DefaultValue!.Value.GetString());
        Assert.Equal(payload.SourceLos.ToString(), sourceLos.DefaultValue!.Value.GetString());
        Assert.Equal("resources", Assert.Single(descriptor.Parameters.Where(p => p.Name == "resourceTypeId")).ReferenceCatalog);
        foreach (var resource in ResourceTypes.All)
            EnvironmentalProductionContent.Validate(payload with { ResourceTypeId = resource.Id });
        foreach (var policy in Enum.GetValues<ProductionSourceLos>())
            EnvironmentalProductionContent.Validate(payload with { SourceLos = policy });
        EnvironmentalProductionContent.Validate(null);

        var invalid = payload with { ResourceTypeId = "missing", Placement = (ProductionPlacement)99, SourceLos = (ProductionSourceLos)99 };
        var errors = MechanicCatalog.Validate(invalid, path: "producer");
        Assert.Contains(errors, error => error.Path == "producer.resourceTypeId" && error.Code == "unknownReference");
        Assert.Contains(errors, error => error.Path == "producer.placement" && error.Code == "enum");
        Assert.Contains(errors, error => error.Path == "producer.sourceLos" && error.Code == "enum");
        Assert.Throws<ArgumentException>(() => EnvironmentalProductionContent.Validate(invalid));
    }

    private static void AssertValidity(bool valid, Action validate)
    {
        if (valid) validate();
        else Assert.Throws<ArgumentException>(validate);
    }

    private sealed record UnknownLootMechanic : LootImmediateMechanic;
    private sealed record UnknownWorldMechanic : WorldImmediateMechanic;
}
