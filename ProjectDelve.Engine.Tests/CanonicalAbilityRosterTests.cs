using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static ProjectDelve.Engine.UnitAuthoring;

namespace ProjectDelve.Engine.Tests;

public sealed class CanonicalAbilityRosterTests
{
    [Fact]
    public void CanonicalSupportedNativeContentUsesTypedLocalDeclarationsOrSupportedLegacyGrants()
    {
        foreach (var type in CanonicalUnitTypes.All)
        {
            Assert.Null(type.Heal);
            Assert.Null(type.HolyWave);
            Assert.Null(type.Fireball);
            Assert.Null(type.AdjacentFriendlyUnitsDefenceBonus);
            Assert.DoesNotContain(type.BonusActions, AbilityQueries.IsSimpleBonus);
            Assert.All(type.AbilityGrants, grant => Assert.Contains(AbilityRoster.All, ability => ability.Id == grant.AbilityId));
        }
        Assert.Empty(UnitRoster.Cleric().AbilityGrants);
        Assert.Equal(new[] { "heal", "holy-wave", "aura" }, UnitRoster.Cleric().LocalAbilities.Select(ability => ability.Key));
        Assert.Equal(new[] { "fireball", "focus" }, UnitRoster.Wizard().AbilityGrants.Select(g => g.AbilityId));
        Assert.Equal(new[] { "dash", "throwing-knife" }, UnitRoster.Rogue().AbilityGrants.Select(g => g.AbilityId));
        Assert.Equal("rage", Assert.Single(UnitRoster.Barbarian().AbilityGrants).AbilityId);
        Assert.Equal("aura", Assert.Single(UnitRoster.DisplacerDemon().AbilityGrants).AbilityId);
    }

    [Fact]
    public void CanonicalDefinitionsAndGrantsRoundTripWithoutLegacyMechanicCopies()
    {
        var definitions = JsonSerializer.Deserialize<List<AbilityDefinition>>(JsonSerializer.Serialize(AbilityRoster.All))!;
        AbilityCatalog.ValidateDefinitions(definitions);
        Assert.Equal(JsonSerializer.Serialize(AbilityRoster.All), JsonSerializer.Serialize(definitions));
        foreach (var type in CanonicalUnitTypes.All)
        {
            var restored = JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(type))!;
            Assert.Equal(type.AbilityGrants.ToArray(), restored.AbilityGrants.ToArray());
            Assert.Equal(JsonSerializer.Serialize(type.LocalAbilities), JsonSerializer.Serialize(restored.LocalAbilities));
            Assert.Equal(type.CardEntries(abilityDefinitions: definitions), restored.CardEntries(abilityDefinitions: definitions));
            Assert.Equal(JsonSerializer.Serialize(type.CreateUnit("original", "side", definitions)),
                JsonSerializer.Serialize(restored.CreateUnit("original", "side", definitions)));
        }
    }

    [Fact]
    public void OrdinaryAuthoringGrantsCanonicalMechanicsWithoutCompatibilityOrUnitIdentity()
    {
        var type = UnitType.Define("new-medic", "New Medic", Stats(2, 1, 2, 1, 1),
            Grant("prayer", "holy-wave"), Grant("medicine", "heal"), Grant("ward", "aura"));
        Assert.All(type.AbilityGrants, grant => Assert.Null(grant.Compatibility));
        var state = GameEngine.CreateGame(new ScenarioDefinition
        {
            Board = new() { Width = 2, Height = 2 }, UnitTypeIds = [type.Id], UnitTypes = [type],
            Units = [new(type.Id, "blue", new(0, 0), UnitId: "medic")],
            Agency = [new(type.Id, "blue", ControllerKind.Human)], Loot = LootSettings.Empty
        });
        var grants = state.AbilitiesOf("medic").ToArray();
        Assert.IsType<HolyWaveMechanic>(grants.Single(g => g.GrantId == "prayer").Definition.Mechanic);
        Assert.Equal(new HealMechanic(2), grants.Single(g => g.GrantId == "medicine").Definition.Mechanic);
        Assert.Equal(new AbilityUses(2, 2), state.AbilityUsesOf("medic", grants.Single(g => g.GrantId == "prayer")));
        Assert.All(grants, grant => Assert.Equal(LegacyAbilityKind.None, grant.Legacy));
        Assert.Null(state.Units[0].HolyWaveUses);
        Assert.Null(state.Units[0].HealUses);
    }

    [Fact]
    public void DerivedPresentationIsOmittedButHistoricalSerializedPropertiesRemainReadable()
    {
        var type = UnitRoster.Barbarian();
        var json = JsonNode.Parse(JsonSerializer.Serialize(type))!;
        Assert.Null(json["Passives"]);
        Assert.Null(json["Fury"]!["Name"]);
        Assert.Null(json["Fury"]!["DisplayText"]);
        json["Passives"] = JsonSerializer.SerializeToNode(type.Passives);
        json["Fury"]!["Name"] = "Old printed Fury";
        json["Fury"]!["DisplayText"] = "Historical generated copy";
        var restored = JsonSerializer.Deserialize<UnitType>(json.ToJsonString())!;
        Assert.Equal(type.Fury, restored.Fury);
        Assert.Equal(type.CardEntries(), restored.CardEntries());
        var backstab = JsonNode.Parse(JsonSerializer.Serialize(UnitRoster.Rogue()))!;
        Assert.Null(backstab["Backstab"]!["Name"]);
        Assert.Null(backstab["Backstab"]!["DisplayText"]);
    }

    [Fact]
    public void GrantAuthoringRejectsRepeatedLocalIdsWithoutRestrictingRepeatedMechanics()
    {
        Assert.Throws<ArgumentException>(() => UnitType.Define("invalid", "Invalid", Stats(1, 1, 1, 1, 1),
            Grant("same", "heal"), Grant("same", "holy-wave")));
        var type = UnitType.Define("two-prayers", "Two Prayers", Stats(1, 1, 1, 1, 1),
            Grant("first", "holy-wave"), Grant("second", "holy-wave"));
        Assert.Equal(2, type.AbilityGrants.Length);
    }
}
