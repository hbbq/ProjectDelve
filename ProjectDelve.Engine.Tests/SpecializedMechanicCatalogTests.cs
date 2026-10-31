using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using static ProjectDelve.Engine.UnitAuthoring;

namespace ProjectDelve.Engine.Tests;

public sealed class SpecializedMechanicCatalogTests
{
    [Fact]
    public void TriggerAndPassiveContractsRetainTheirRequiredContexts()
    {
        (string Id, AbilityMechanic Payload, MechanicTiming Timing)[] expected =
        [
            ("cleave", new Cleave(), MechanicTiming.AfterAttack),
            ("moveAfterAttack", new MoveAfterAttack(1), MechanicTiming.AfterAttack)
        ];
        foreach (var (id, payload, timing) in expected)
        {
            var descriptor = Assert.IsType<MechanicDescriptor>(MechanicCatalog.Find(id));
            Assert.Same(descriptor, MechanicCatalog.FindByPayload(payload));
            Assert.Equal(MechanicFamily.Ability, descriptor.Family);
            Assert.Equal(id, descriptor.SerializedBinding);
            Assert.True(descriptor.Grantable);
            Assert.Equal(1, descriptor.MaxPerSource);
            var profile = Assert.Single(descriptor.Profiles);
            Assert.Equal(MechanicSource.Unit, profile.Source);
            Assert.Equal(timing, profile.Timing);
            Assert.Equal(id == "cleave" ? MechanicUsageSupport.LimitedOrUnlimited : MechanicUsageSupport.UnlimitedOnly, profile.Usage);
            Assert.Empty(MechanicCatalog.Validate(payload, new(MechanicSource.Unit, timing, id == "cleave" ? 2 : null)));
            Assert.NotEmpty(MechanicCatalog.Validate(payload, new(MechanicSource.Equipment, timing, id == "cleave" ? 2 : null)));
            Assert.NotEmpty(MechanicCatalog.Validate(payload, new(MechanicSource.Item, timing, id == "cleave" ? 2 : null)));
        }
        Assert.Empty(MechanicCatalog.Find("telekinesis")!.Parameters);
        Assert.DoesNotContain(MechanicCatalog.Find("cleave")!.Parameters, p => p.Name == "maxUses");
    }

    [Theory]
    [InlineData("cleave", "triggerDamage")]
    [InlineData("cleave", "damage")]
    [InlineData("fury", "adjacentEnemyThreshold")]
    public void NativeParameterBoundsGiveFieldDiagnosticsAndRejectInvalidScenarioContent(string id, string parameter)
    {
        AbilityMechanic payload = parameter switch
        {
            "triggerDamage" => new Cleave { TriggerDamage = 0 },
            "damage" => new Cleave { Damage = 0 },
            _ => new Fury { AdjacentEnemyThreshold = 0 }
        };
        var schema = Assert.Single(MechanicCatalog.Find(id)!.Parameters, p => p.Name == parameter);
        Assert.Equal(1, schema.Minimum);
        var issue = Assert.Single(MechanicCatalog.Validate(payload, path: "unitTypes[0].localAbilities[0].mechanic"));
        Assert.Equal($"unitTypes[0].localAbilities[0].mechanic.{parameter}", issue.Path);
        Assert.Equal("minimum", issue.Code);
        var type = new UnitType("native-check", 0, 0, 0, 0, 1)
        {
            LocalAbilities = [new(id, id, payload is Cleave ? AbilityTiming.AfterAttack : AbilityTiming.Passive, payload)
                { Usage = new(payload is Cleave ? 2 : null) }]
        };
        Assert.Throws<ArgumentException>(() => GameEngine.ValidateScenario(State(type)));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void NativeBonusDomainsRetainAllPreviouslyAcceptedIntegerValues(int bonus)
    {
        var fury = new Fury { AtkBonus = bonus, AdjacentEnemyThreshold = 1 };
        var backstab = new Backstab { AtkBonus = bonus };
        foreach (var payload in new AbilityMechanic[] { fury, backstab })
        {
            var schema = Assert.Single(MechanicCatalog.FindByPayload(payload)!.Parameters, p => p.Name == "atkBonus");
            Assert.Null(schema.Minimum); Assert.Null(schema.Maximum);
            Assert.Empty(MechanicCatalog.Validate(payload));
        }
        GameEngine.ValidateScenario(State(new("native-check", 0, 0, 0, 0, 1)
        {
            LocalAbilities = [new("fury", "Fury", AbilityTiming.Passive, fury), new("backstab", "Backstab", AbilityTiming.Passive, backstab)]
        }));
    }

    [Fact]
    public void ContractDefaultsMatchLocalSerializedPayloadsAndNamesRemainSeparate()
    {
        var type = UnitType.Define("native-content", "Native Content", Stats(0, 1, 1, 0, 1),
            Ability("Follow Through", Uses(2), Cleave(triggerDamage: 2, damage: 1), "follow"),
            Ability("Distant Touch", Unlimited(), Telekinesis(), "touch"),
            Ability("Resolve", Unlimited(), Fury(atkBonus: 1, adjacentEnemies: 2), "resolve"),
            Ability("Flank", Unlimited(), Backstab(atkBonus: 1), "flank"));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var json = JsonSerializer.SerializeToElement(type, options);
        foreach (var ability in json.GetProperty("localAbilities").EnumerateArray())
        {
            var mechanic = ability.GetProperty("mechanic");
            foreach (var parameter in MechanicCatalog.Find(mechanic.GetProperty("kind").GetString()!)!.Parameters)
            {
                Assert.False(parameter.Required);
                Assert.Equal(parameter.DefaultValue!.Value.GetInt32(), mechanic.GetProperty(parameter.Name).GetInt32());
            }
        }
        Assert.False(json.TryGetProperty("cleave", out _));
        Assert.False(json.TryGetProperty("abilityGrants", out _));
        var restored = JsonSerializer.Deserialize<UnitType>(json, options)!;
        Assert.Equal("Follow Through", restored.Ability<Cleave>()!.Name);
        Assert.Equal(type.Mechanic<Cleave>(), restored.Mechanic<Cleave>());
        Assert.Equal(type.Mechanic<Fury>(), restored.Mechanic<Fury>());
        Assert.Equal(type.Mechanic<Backstab>(), restored.Mechanic<Backstab>());
        Assert.Equal(new AbilityUses(2, 2), restored.CreateUnit("actor", "side").Uses("follow"));
    }

    [Fact]
    public void MultipleAttachmentsToSingletonConcreteMechanicsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => UnitType.Define("duplicate", "Duplicate", Stats(0, 1, 1, 0, 1),
            Ability("First", Uses(2), Cleave(2, 1), "one"), Ability("Second", Uses(3), Cleave(3, 2), "two")));
        var multiple = UnitType.Define("multiple", "Multiple", Stats(0, 1, 1, 0, 1),
            Ability("First", Uses(1), Telekinesis(), "one"), Ability("Second", Uses(2), Telekinesis(), "two"));
        Assert.Equal(2, multiple.LocalAbilities.Length);
    }

    [Fact]
    public void ActiveProfilesPermitSourceLocalLimitsWithoutAdvertisingUnsupportedTriggersOrPassives()
    {
        (AbilityMechanic Payload, MechanicTiming[] Timings)[] active =
        [
            (new FireballMechanic(), [MechanicTiming.Action, MechanicTiming.BonusAction, MechanicTiming.FreeAction]),
            (new OpenDoor(), [MechanicTiming.Action, MechanicTiming.FreeAction]),
            (new Telekinesis(), [MechanicTiming.Action]),
            (new TryOpenDoor(3), [MechanicTiming.Action]),
            (new Swap(), [MechanicTiming.BonusAction]),
            (new Displace(1), [MechanicTiming.BonusAction]),
            (new ReshapeRoom(), [MechanicTiming.BonusAction]),
            (new SummonAdjacent(UnitTypeIds.Grunt, Posture.Upright), [MechanicTiming.Action]),
            (new SummonWorker(3), [MechanicTiming.Action]),
            (new SummonTroll(1), [MechanicTiming.Action]),
            (new FireBreath(), [MechanicTiming.Action]),
            (new ClawAttack(), [MechanicTiming.Action]),
            (new PickupResource(), [MechanicTiming.Action]),
            (new GiveResource(), [MechanicTiming.Action]),
            (new Operate(), [MechanicTiming.Action]),
            (new ShootShell(1, 4, 3), [MechanicTiming.Action]),
            (new PlaceBananaPeel(1), [MechanicTiming.BonusAction]),
            (new Boost(1), [MechanicTiming.BonusAction])
        ];
        foreach (var (payload, timings) in active)
        {
            var descriptor = MechanicCatalog.FindByPayload(payload)!;
            Assert.Null(descriptor.MaxPerSource);
            Assert.Equal(timings.Length * 3, descriptor.Profiles.Length);
            foreach (var timing in timings)
            {
                foreach (var source in new[] { MechanicSource.Unit, MechanicSource.Equipment })
                {
                    Assert.Empty(MechanicCatalog.Validate(payload, new(source, timing)));
                    Assert.Empty(MechanicCatalog.Validate(payload, new(source, timing, 2)));
                }
                Assert.Empty(MechanicCatalog.Validate(payload, new(MechanicSource.Item, timing)));
                Assert.Contains(MechanicCatalog.Validate(payload, new(MechanicSource.Item, timing, 1)),
                    issue => issue.Code == "unsupportedUsage" && issue.Message.Contains("physical object"));
            }
        }
        var missingAttack = Assert.Single(MechanicCatalog.Validate(new Cleave(), new(MechanicSource.Unit, MechanicTiming.Action)));
        Assert.Contains("completed Attack", missingAttack.Message);
        Assert.Contains(MechanicCatalog.Validate(new Phase(), new(MechanicSource.Equipment, MechanicTiming.Passive, 1)),
            issue => issue.Message.Contains("no discrete use"));
        Assert.NotEmpty(MechanicCatalog.Validate(new SummonAdjacent(UnitTypeIds.Grunt, Posture.Upright), new(MechanicSource.Equipment, MechanicTiming.FreeAction)));
    }

    [Fact]
    public void SourceIndependentPassivesAllowEquipmentAndMultiplicityButRequireAnOngoingUnlimitedBearer()
    {
        foreach (var payload in new AbilityMechanic[] { new Fury(), new Backstab(), new TransportResource(), new ProjectDelve.Engine.Phase() })
        {
            Assert.Null(MechanicCatalog.FindByPayload(payload)!.MaxPerSource);
            var ability = new LocalAbilityDefinition("first", "First", AbilityTiming.Passive, payload);
            foreach (var source in new[] { AbilitySourceKind.Unit, AbilitySourceKind.Equipment })
            {
                AbilityContent.Validate([ability, ability with { Key = "second" }], source);
                Assert.Throws<ArgumentException>(() => AbilityContent.Validate([ability with { Usage = new(1) }], source));
                Assert.Throws<ArgumentException>(() => AbilityContent.Validate([ability with { Timing = AbilityTiming.Action }], source));
            }
            Assert.Throws<ArgumentException>(() => AbilityContent.Validate([ability], AbilitySourceKind.Item));
        }
    }

    [Theory]
    [InlineData(-101)]
    [InlineData(101)]
    public void SharedModifierBoundsApplyToEveryLocalDeclaration(int amount)
    {
        foreach (var mechanic in new AbilityMechanic[] { new AdjacentModifiersMechanic([new(Stat.Def, amount)]), new SelfModifiersMechanic([new(Stat.Atk, amount)]) })
        {
            var ability = new LocalAbilityDefinition("configured", "Configured", mechanic is AdjacentModifiersMechanic ? AbilityTiming.Passive : AbilityTiming.BonusAction, mechanic);
            Assert.Contains(MechanicCatalog.Validate(mechanic), issue => issue.Path == "mechanic.modifiers[0].amount");
            Assert.Throws<ArgumentException>(() => AbilityContent.Validate([ability], AbilitySourceKind.Unit));
        }
    }

    [Fact]
    public void WaypointTableRequiresExactlySixResolvableFaces()
    {
        Assert.Empty(MechanicCatalog.Validate(new WaypointSlotMachine(RacingSlotTable.Table)));
        foreach (var table in new ResourceOutputTable[]
        {
            new([]), new([ResourceTypes.Shell]), new(default),
            new([ResourceTypes.Shell, ResourceTypes.Shell, ResourceTypes.Shell,
                ResourceTypes.Shell, ResourceTypes.Shell, new("missing", "Missing")])
        })
            Assert.NotEmpty(MechanicCatalog.Validate(new WaypointSlotMachine(table)));
    }

    [Fact]
    public void WaypointFaceMetadataMatchesTheSerializedResourceObject()
    {
        var table = Assert.Single(MechanicCatalog.Find("waypointSlotMachine")!.Parameters);
        Assert.Equal(MechanicParameterKind.Object, table.Kind);
        var faces = Assert.Single(table.Members);
        Assert.Equal(MechanicParameterKind.List, faces.Kind);
        Assert.Equal(MechanicParameterKind.Object, faces.Item!.Kind);
        var id = Assert.Single(faces.Item.Members, parameter => parameter.Name == "id");
        Assert.Equal(MechanicParameterKind.Reference, id.Kind);
        Assert.Equal("resources", id.ReferenceCatalog);
        Assert.Equal(MechanicParameterKind.String, Assert.Single(faces.Item.Members, parameter => parameter.Name == "name").Kind);
        var serialized = JsonSerializer.SerializeToElement(new WaypointSlotMachine(RacingSlotTable.Table),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var face = serialized.GetProperty("outputTable").GetProperty("faces")[0];
        Assert.Equal(ResourceTypes.Banana.Id, face.GetProperty("id").GetString());
        Assert.Equal(ResourceTypes.Banana.Name, face.GetProperty("name").GetString());
    }

    [Fact]
    public void PassiveCapabilitiesRemainContinuousWhileOpenDoorSupportsUsesAndIndependentDeclarations()
    {
        foreach (var ability in new LocalAbilityDefinition[]
        {
            new("pass-through", "Pass Through", AbilityTiming.Passive, new Phase()),
            new("unlock", "Unlock", AbilityTiming.FreeAction, new OpenDoor())
        })
        {
            AbilityContent.Validate([ability], AbilitySourceKind.Unit);
            AbilityContent.Validate([ability], AbilitySourceKind.Equipment);
            if (ability.Mechanic is Phase)
            {
                Assert.Throws<ArgumentException>(() => AbilityContent.Validate([ability], AbilitySourceKind.Item));
                Assert.Throws<ArgumentException>(() => AbilityContent.Validate([ability with { Usage = new(2) }], AbilitySourceKind.Equipment));
                AbilityContent.Validate([ability, ability with { Key = "other" }], AbilitySourceKind.Equipment);
            }
            else
            {
                AbilityContent.Validate([ability], AbilitySourceKind.Item);
                AbilityContent.Validate([ability with { Usage = new(2) }], AbilitySourceKind.Equipment);
                AbilityContent.Validate([ability, ability with { Key = "other" }], AbilitySourceKind.Equipment);
            }
        }
    }

    [Fact]
    public void LocalEquipmentStatModifiersKeepBoundaryZeroAndRepeatedStatAcceptance()
    {
        var ability = new LocalAbilityDefinition("stats", "Stats", AbilityTiming.Passive,
            new SelfModifiersMechanic([new(Stat.Atk, -100), new(Stat.Atk, 0), new(Stat.Atk, 100)]));
        AbilityContent.Validate([ability], AbilitySourceKind.Equipment);
        foreach (var amount in new[] { -101, 101 })
            Assert.Throws<ArgumentException>(() => AbilityContent.Validate(
                [ability with { Mechanic = new SelfModifiersMechanic([new(Stat.Atk, amount)]) }], AbilitySourceKind.Equipment));
    }

    private static GameState State(UnitType type) => new()
    {
        Physical = new(new Board(1, 1, []), []), Types = [type], Units = []
    };
}
