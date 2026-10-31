using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class SharedMechanicContractTests
{
    private static (AbilityMechanic Payload, AbilityTiming[] Timings)[] Supported() =>
    [
        (new HolyWaveMechanic(), [AbilityTiming.Action]),
        (new HealMechanic(2), [AbilityTiming.Action]),
        (new FireballMechanic(), [AbilityTiming.Action]),
        (new SelfModifiersMechanic([new(Stat.Atk, 1)]), [AbilityTiming.BonusAction, AbilityTiming.Passive]),
        (new AdjacentModifiersMechanic([new(Stat.Def, 1)]), [AbilityTiming.Passive]),
        (new ExtraActionsMechanic(1), [AbilityTiming.BonusAction]),
        (new StandAdjacentFriendlyMechanic(), [AbilityTiming.Action])
    ];

    [Fact]
    public void SharedContractsMatchExistingTypedJsonDiscriminatorsAndContainOnlyInspectionData()
    {
        var descriptors = MechanicCatalog.All.Where(d => d.Family == MechanicFamily.Ability).ToArray();
        Assert.Equal(7, descriptors.Length);
        foreach (var (payload, _) in Supported())
        {
            using var payloadJson = JsonDocument.Parse(JsonSerializer.Serialize<AbilityMechanic>(payload));
            var descriptor = MechanicCatalog.FindByPayload(payload)!;
            Assert.Equal(payloadJson.RootElement.GetProperty("kind").GetString(), descriptor.Id);
            Assert.Equal(descriptor.Id, descriptor.SerializedBinding);
            Assert.True(descriptor.Grantable);
            var json = JsonSerializer.Serialize(descriptor);
            using var metadata = JsonDocument.Parse(json);
            Assert.Equal("Ability", metadata.RootElement.GetProperty("Family").GetString());
            Assert.DoesNotContain("PayloadType", json);
            Assert.DoesNotContain("System.", json);
            var restored = JsonSerializer.Deserialize<MechanicDescriptor>(json)!;
            Assert.Equal(json, JsonSerializer.Serialize(restored));
        }
    }

    [Fact]
    public void ContractsAndEngineAgreeOnTheExistingSourceTimingAndUsageMatrix()
    {
        foreach (var (payload, timings) in Supported())
        foreach (var timing in Enum.GetValues<AbilityTiming>())
        foreach (var source in Enum.GetValues<AbilitySourceKind>())
        foreach (var maxUses in new int?[] { null, 2 })
        {
            var expected = timings.Contains(timing) &&
                (timing != AbilityTiming.Passive || maxUses is null && source != AbilitySourceKind.Item) &&
                (source == AbilitySourceKind.Unit || maxUses is null);
            var context = new MechanicValidationContext(Enum.Parse<MechanicSource>(source.ToString()),
                Enum.Parse<MechanicTiming>(timing.ToString()), maxUses);
            Assert.Equal(expected, MechanicCatalog.Validate(payload, context).IsEmpty);
            var definition = new AbilityDefinition("under-test", "Under Test", timing, payload) { Usage = new(maxUses) };
            var state = new GameState
            {
                Physical = new(new Board(1, 1, []), []), Units = [],
                AbilityDefinitions = [definition], EquipmentTypes = [], ItemTypes = [], LootCardDefinitions = [],
                Types = source == AbilitySourceKind.Unit
                    ? [new("bearer", 1, 1, 1, 1, 1) { AbilityGrants = [new("use", definition.Id)] }] : []
            };
            if (source == AbilitySourceKind.Equipment)
                state.EquipmentTypes.Add(new("equipment", "Equipment") { Grants = [new("use", definition.Id)] });
            if (source == AbilitySourceKind.Item)
                state.ItemTypes.Add(new("item", "Item") { Grants = [new("use", definition.Id)] });
            var exception = Record.Exception(() => AbilityCatalog.ValidateState(state));
            if (expected) Assert.Null(exception); else Assert.IsType<ArgumentException>(exception);
        }
    }

    [Theory]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void PositiveAbilityAmountsRetainTheirUnboundedUpperDomain(int amount)
    {
        foreach (var mechanic in new AbilityMechanic[] { new HealMechanic(amount), new ExtraActionsMechanic(amount) })
        {
            var parameter = Assert.Single(MechanicCatalog.FindByPayload(mechanic)!.Parameters);
            Assert.Equal(1, parameter.Minimum);
            Assert.Null(parameter.Maximum);
            Assert.Null(parameter.DefaultValue);
            Assert.Empty(MechanicCatalog.Validate(mechanic));
        }
        Assert.Equal("ability.mechanic.amount", Assert.Single(MechanicCatalog.Validate(new HealMechanic(0), path: "ability.mechanic")).Path);
        Assert.Equal("minimum", Assert.Single(MechanicCatalog.Validate(new ExtraActionsMechanic(0))).Code);
    }

    [Fact]
    public void ModifierValidationPreservesRepeatedAndZeroValuesAndReportsTheActualInvalidField()
    {
        var values = ImmutableArray.Create(new ModifierThisTurn(Stat.Atk, 0), new(Stat.Atk, -100), new(Stat.Atk, 100));
        Assert.Empty(MechanicCatalog.Validate(new SelfModifiersMechanic(values)));
        Assert.Empty(MechanicCatalog.Validate(new AdjacentModifiersMechanic(values)));
        Assert.NotEmpty(MechanicCatalog.Validate(new SelfModifiersMechanic([])));
        Assert.NotEmpty(MechanicCatalog.Validate(new SelfModifiersMechanic(default)));
        Assert.NotEmpty(MechanicCatalog.Validate(new AdjacentModifiersMechanic([null!])));
        var invalid = MechanicCatalog.Validate(new SelfModifiersMechanic([new((Stat)99, 101)]), path: "ability.mechanic");
        Assert.Contains(invalid, e => e.Path == "ability.mechanic.modifiers[0].stat" && e.Code == "enum");
        Assert.Contains(invalid, e => e.Path == "ability.mechanic.modifiers[0].amount" && e.Code == "maximum");
    }

    [Fact]
    public void DescriptionContractsPreserveBothRenderingAndTheExistingOverrideAcceptance()
    {
        var heal = new AbilityDefinition("heal-example", "Restoration", AbilityTiming.Action, new HealMechanic(7));
        Assert.Equal("Heal an adjacent damaged friendly Unit for 7 HP.", heal.CardEntry().Description);
        var modifiers = new AbilityDefinition("modifiers", "Unchanged", AbilityTiming.Passive,
            new SelfModifiersMechanic([new(Stat.Atk, 2)]));
        Assert.Equal("ATK +2.", modifiers.CardEntry().Description);
        Assert.Equal("ATK +2 this activation.", (modifiers with { Timing = AbilityTiming.BonusAction }).CardEntry().Description);
        AbilityCatalog.ValidateDefinitions([heal with { DescriptionOverride = "Restore {amount} HP; {unrecognized} remains accepted." }]);
        AbilityCatalog.ValidateDefinitions([new("wave", "Quiet", AbilityTiming.Action, new HolyWaveMechanic()) { DescriptionOverride = "" }]);
        Assert.Throws<ArgumentException>(() => AbilityCatalog.ValidateDefinitions([heal with { DescriptionOverride = "Restore 7 HP." }]));
        Assert.Throws<ArgumentException>(() => AbilityCatalog.ValidateDefinitions([heal with { DescriptionOverride = "Restore HP." }]));
        var policy = MechanicCatalog.Find("heal")!.Description;
        Assert.True(policy.AllowsOverride);
        Assert.True(policy.RejectsNumericOverrideLiterals);
        Assert.False(policy.RejectsUnknownOverridePlaceholders);
        Assert.Equal(new[] { "amount" }, policy.RequiredOverridePlaceholders.ToArray());
    }

    [Fact]
    public void StructuralValidationReportsMissingExecutionContextWithoutCheckingBoardLegality()
    {
        Assert.Empty(MechanicCatalog.Validate(new HealMechanic(2), new(MechanicSource.Unit, MechanicTiming.Action)));
        Assert.Contains(MechanicCatalog.Validate(new HealMechanic(2),
            new(MechanicSource.Unit, MechanicTiming.Action, HasUnitContext: false)), d => d.Code == "unitContextRequired");
        Assert.Equal("unknownMechanic", Assert.Single(MechanicCatalog.Validate(new object())).Code);
        Assert.Throws<ArgumentException>(() => MechanicCatalog.ThrowIfInvalid(new HealMechanic(0)));
    }
}
