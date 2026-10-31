using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using static ProjectDelve.Engine.UnitAuthoring;

namespace ProjectDelve.Engine.Tests;

public sealed class SpecializedMechanicCatalogTests
{
    [Fact]
    public void NativeContractsIdentifyExistingFieldsWithoutAdvertisingGrantSupport()
    {
        var expected = new Dictionary<string, (object Payload, string Binding, MechanicTiming Timing)>
        {
            ["cleave"] = (new Cleave(), "unitType.cleave", MechanicTiming.AfterAttack),
            ["telekinesis"] = (new Telekinesis(), "unitType.actions", MechanicTiming.Action),
            ["fury"] = (new Fury(), "unitType.fury", MechanicTiming.Passive),
            ["backstab"] = (new Backstab(), "unitType.backstab", MechanicTiming.Passive)
        };
        foreach (var (id, (payload, binding, timing)) in expected)
        {
            var descriptor = Assert.IsType<MechanicDescriptor>(MechanicCatalog.Find(id));
            Assert.Same(descriptor, MechanicCatalog.FindByPayload(payload));
            Assert.Equal(MechanicFamily.NativeUnit, descriptor.Family);
            Assert.Equal(binding, descriptor.SerializedBinding);
            Assert.False(descriptor.Grantable);
            Assert.Equal(1, descriptor.MaxPerSource);
            var profile = Assert.Single(descriptor.Profiles);
            Assert.Equal(MechanicSource.Unit, profile.Source);
            Assert.Equal(timing, profile.Timing);
            Assert.Equal(id == "cleave" ? MechanicUsageSupport.LimitedOnly : MechanicUsageSupport.UnlimitedOnly, profile.Usage);
            Assert.Empty(MechanicCatalog.Validate(payload, new(MechanicSource.Unit, timing, id == "cleave" ? 2 : null)));
            Assert.NotEmpty(MechanicCatalog.Validate(payload, new(MechanicSource.Equipment, timing, id == "cleave" ? 2 : null)));
            Assert.NotEmpty(MechanicCatalog.Validate(payload, new(MechanicSource.Item, timing, id == "cleave" ? 2 : null)));
        }
        Assert.Empty(MechanicCatalog.Find("telekinesis")!.Parameters);
        Assert.Equal(new[] { AbilityTiming.Action, AbilityTiming.BonusAction, AbilityTiming.FreeAction, AbilityTiming.Passive },
            Enum.GetValues<AbilityTiming>());
    }

    [Theory]
    [InlineData("cleave", "maxUses")]
    [InlineData("cleave", "triggerDamage")]
    [InlineData("cleave", "damage")]
    [InlineData("fury", "adjacentEnemyThreshold")]
    public void NativeParameterBoundsGiveFieldDiagnosticsAndRejectInvalidScenarioContent(string id, string parameter)
    {
        object payload = parameter switch
        {
            "maxUses" => new Cleave(0),
            "triggerDamage" => new Cleave { TriggerDamage = 0 },
            "damage" => new Cleave { Damage = 0 },
            _ => new Fury { AdjacentEnemyThreshold = 0 }
        };
        var descriptor = MechanicCatalog.Find(id)!;
        var schema = Assert.Single(descriptor.Parameters, p => p.Name == parameter);
        Assert.Equal(1, schema.Minimum);
        var issue = Assert.Single(MechanicCatalog.Validate(payload, path: $"unitTypes[0].{id}"));
        Assert.Equal($"unitTypes[0].{id}.{parameter}", issue.Path);
        Assert.Equal("minimum", issue.Code);
        var type = new UnitType("native-check", 0, 0, 0, 0, 1)
        {
            Cleave = payload as Cleave,
            Fury = payload as Fury
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
        foreach (var payload in new object[] { fury, backstab })
        {
            var schema = Assert.Single(MechanicCatalog.FindByPayload(payload)!.Parameters, p => p.Name == "atkBonus");
            Assert.Null(schema.Minimum);
            Assert.Null(schema.Maximum);
            Assert.Empty(MechanicCatalog.Validate(payload));
        }
        GameEngine.ValidateScenario(State(new("native-check", 0, 0, 0, 0, 1) { Fury = fury, Backstab = backstab }));
    }

    [Fact]
    public void ContractDefaultsMatchExistingSerializedPayloadsAndNativeNamesRemainSeparate()
    {
        var type = UnitType.Define("native-content", "Native Content", Stats(0, 1, 1, 0, 1),
            Ability("Follow Through", Uses(2), Cleave(triggerDamage: 2, damage: 1)),
            Ability("Distant Touch", Unlimited(), Telekinesis()),
            Ability("Resolve", Unlimited(), Fury(atkBonus: 1, adjacentEnemies: 2)),
            Ability("Flank", Unlimited(), Backstab(atkBonus: 1)));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.SerializeToElement(type, options);
        foreach (var id in new[] { "cleave", "fury", "backstab" })
        foreach (var parameter in MechanicCatalog.Find(id)!.Parameters)
        {
            Assert.False(parameter.Required);
            Assert.Equal(parameter.DefaultValue!.Value.GetInt32(), json.GetProperty(id).GetProperty(parameter.Name).GetInt32());
        }
        Assert.Contains("Telekinesis", json.GetProperty("actions").GetString());
        Assert.False(json.TryGetProperty("telekinesis", out _));
        Assert.Empty(json.GetProperty("abilityGrants").EnumerateArray());
        Assert.Equal("Follow Through", json.GetProperty("abilityNames").GetProperty("cleave").GetString());
        var restored = JsonSerializer.Deserialize<UnitType>(json, options)!;
        Assert.Equal(type.Cleave, restored.Cleave);
        Assert.Equal(type.Fury, restored.Fury);
        Assert.Equal(type.Backstab, restored.Backstab);
        Assert.Equal(type.Actions, restored.Actions);
        Assert.Equal(new AbilityUses(2, 2), restored.CreateUnit("actor", "side").CleaveUses);
    }

    [Fact]
    public void ExistingNativeAuthoringStillRejectsMultipleAttachmentsToOneSlot()
    {
        Assert.Throws<ArgumentException>(() => UnitType.Define("duplicate", "Duplicate", Stats(0, 1, 1, 0, 1),
            Ability("First", Uses(2), Cleave(triggerDamage: 2, damage: 1)),
            Ability("Second", Uses(3), Cleave(triggerDamage: 3, damage: 2))));
        Assert.Throws<ArgumentException>(() => UnitType.Define("duplicate", "Duplicate", Stats(0, 1, 1, 0, 1),
            Ability("First", Unlimited(), Telekinesis()), Ability("Second", Unlimited(), Telekinesis())));
    }

    [Theory]
    [InlineData(-101)]
    [InlineData(101)]
    public void LegacyAuraAndBonusModifierInputsRetainTheirBroaderDomainWhileSharedPayloadsRejectIt(int amount)
    {
        var legacy = new UnitType("legacy-modifiers", 1, 1, 1, 1, 1)
        {
            AdjacentFriendlyUnitsDefenceBonus = new(amount, "Legacy Ward"),
            BonusActions = [new("legacy-boost", 2, [new(Stat.Atk, amount)])]
        };
        var scenario = new ScenarioDefinition
        {
            Board = new() { Width = 2, Height = 1 },
            UnitTypeIds = [legacy.Id],
            UnitTypes = [legacy],
            Units = [new(legacy.Id, "blue", new(0, 0), UnitId: "actor")],
            Agency = [new(legacy.Id, "blue", ControllerKind.Human)],
            Objectives = [new("blue", ScenarioObjective.NoObjective)],
            Loot = LootSettings.Empty
        };
        var restored = ScenarioDefinitionJson.FromJson(ScenarioDefinitionJson.ToJson(scenario));
        var state = GameEngine.CreateGame(restored);
        var type = Assert.Single(state.Types);
        Assert.Equal(amount, type.AdjacentFriendlyUnitsDefenceBonus!.Amount);
        Assert.Equal(amount, Assert.Single(Assert.Single(type.BonusActions).Modifiers).Amount);
        Assert.Equal(new AbilityUses(2, 2), Assert.Single(state.Units).BonusActionUses["legacy-boost"]);

        var adapted = state.AbilitiesOf("actor").ToArray();
        var aura = Assert.Single(adapted, ability => ability.Legacy == LegacyAbilityKind.Aura);
        var bonus = Assert.Single(adapted, ability => ability.Legacy == LegacyAbilityKind.BonusAction);
        Assert.Equal(amount, Assert.Single(Assert.IsType<AdjacentModifiersMechanic>(aura.Definition.Mechanic).Modifiers).Amount);
        Assert.Equal(amount, Assert.Single(Assert.IsType<SelfModifiersMechanic>(bonus.Definition.Mechanic).Modifiers).Amount);
        foreach (var ability in new[] { aura.Definition, bonus.Definition })
        {
            Assert.Contains(MechanicCatalog.Validate(ability.Mechanic), issue => issue.Path == "mechanic.modifiers[0].amount");
            Assert.Throws<ArgumentException>(() => AbilityCatalog.ValidateDefinitions([ability]));
        }
    }

    private static GameState State(UnitType type) => new()
    {
        Physical = new(new Board(1, 1, []), []),
        Types = [type],
        Units = []
    };
}
